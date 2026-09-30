using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Scripting;

/// <summary>
/// Tests for the script runner (P3.2): stop-on-failure with a live session, optional
/// steps, connection-loss abort, and the handover's canonical 5-step SINTRAN login.
/// </summary>
public class ScriptRunnerTests : IDisposable
{
    private readonly VT100Emulator _emulator;
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;
    private readonly CommandRegistry _registry;
    private readonly ScriptParser _parser;
    private readonly ScriptRunner _runner;

    public ScriptRunnerTests()
    {
        _emulator = new VT100Emulator(80, 24);
        _session = new TerminalSession(_emulator, "RunnerTest");
        _connection = new InMemoryConnection();
        _registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(_registry);
        _parser = new ScriptParser(_registry);
        _runner = new ScriptRunner(_registry);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    /// <summary>
    /// Polls until the connection has seen the given number of sends.
    /// </summary>
    private async Task WaitForSentCount(int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (_connection.GetSentData().Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {count} sends");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Run_SintranLogin_FiveSteps_EndToEnd()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        // The canonical login from the handover: ESC → ENTER prompt → user →
        // PASSWORD: prompt → password → command prompt. No fixed delays anywhere —
        // each step waits for the RENDERED screen (this is the 28-minute-race fix).
        var script = _parser.Parse(
            "# SINTRAN login\n" +
            "SENDRAW ESC\n" +
            "WAITFOR \"ENTER\" where=screen timeout=5000\n" +
            "SEND \"USER-NAME\\r\"\n" +
            "WAITFOR \"PASSWORD:\" where=screen timeout=5000\n" +
            "SEND \"SECRET\\r\"\n" +
            "WAITFOR \"@\" timeout=5000\n");
        Assert.True(script.IsValid);

        var runTask = _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        // Play the ND side, reacting to what the script sends — with delays, like a
        // busy machine whose banner arrives late.
        await WaitForSentCount(1); // ESC went out
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("SINTRAN III\r\nENTER "));

        await WaitForSentCount(2); // user name went out
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("\r\nPASSWORD: "));

        await WaitForSentCount(3); // password went out
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("\r\nOK\r\n@"));

        var result = await runTask;

        Assert.True(result.Success, result.Error);
        Assert.Equal(6, result.Transcript.Count);
        Assert.Null(result.FailedStep);

        // The sends happened in order and with the right content.
        var sent = _connection.GetSentData();
        Assert.Equal(0x1B, sent[0][0]);
        Assert.Equal("USER-NAME\r", Encoding.UTF8.GetString(sent[1]));
        Assert.Equal("SECRET\r", Encoding.UTF8.GetString(sent[2]));

        // Every step reported its timing (rule 5).
        for (int i = 0; i < result.Transcript.Count; i++)
        {
            Assert.True(result.Transcript[i].Result.Elapsed >= TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task Run_StopsOnFailingStep_SessionStaysLive()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("some output"));

        var script = _parser.Parse(
            "WAITFOR \"NEVER-THERE\" timeout=150\n" +
            "SEND \"MUST-NOT-RUN\"\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.FailedStep);
        Assert.Equal(1, result.FailedStep!.LineNumber);
        Assert.Contains("Timeout", result.Transcript[0].Result.Error);
        // Rule 4: the failing step still carries what the machine said.
        Assert.Contains("some output", result.Transcript[0].Result.Output);

        // The step after the failure did NOT run...
        Assert.Empty(_connection.GetSentData());

        // ...and the session is handed back LIVE for interactive poking.
        Assert.True(_session.IsConnected);
        await _session.SendInputAsync("poke", TestContext.Current.CancellationToken);
        Assert.Equal("poke", _connection.GetLastSentAsString());
    }

    [Fact]
    public async Task Run_OptionalStepFailure_Continues()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse(
            "WAITFOR \"NOT-THERE\" timeout=100 optional=true\n" +
            "SEND \"STILL-RUNS\\r\"\n");   // \r explicit — SEND appends nothing
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, result.Transcript.Count);
        Assert.False(result.Transcript[0].Result.Success); // it did fail...
        Assert.Equal("STILL-RUNS\r", _connection.GetLastSentAsString()); // ...but the script went on
    }

    [Fact]
    public async Task Run_ConnectionLostMidWait_AbortsAndSaysSo()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse(
            "WAITFOR \"NEVER\" timeout=10000\n" +
            "SEND \"MUST-NOT-RUN\"\n");
        Assert.True(script.IsValid);

        var runTask = _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await _connection.DisconnectAsync(); // remote drop mid-WAITFOR

        var result = await runTask;

        // Rule 6: the result says the connection dropped and at which step.
        Assert.False(result.Success);
        Assert.True(result.ConnectionLost);
        Assert.NotNull(result.FailedStep);
        Assert.Equal(1, result.FailedStep!.LineNumber);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(10)); // did not sit out the timeout
    }

    [Fact]
    public async Task Run_ReadScreenStep_IsACheckpoint_TranscriptShowsItsOutput()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("SCREEN CONTENT HERE"));

        var script = _parser.Parse("READSCREEN\nSEND \"next\"\n");
        Assert.True(script.IsValid);

        var result = await _runner.RunAsync(script, _session, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        // READSCREEN exists to produce output — its transcript entry is flagged and
        // carries the screen; SEND's is not.
        Assert.True(result.Transcript[0].ShowOutput);
        Assert.Contains("SCREEN CONTENT HERE", result.Transcript[0].Result.Output);
        Assert.False(result.Transcript[1].ShowOutput);
    }

    [Fact]
    public async Task Run_ScriptWithParseErrors_Throws()
    {
        var script = _parser.Parse("NOSUCHVERB");
        await Assert.ThrowsAsync<ArgumentException>(() => _runner.RunAsync(script, _session, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Run_ProgressReportsEachStep()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var script = _parser.Parse("SEND \"one\"\nSEND \"two\"\n");
        int reported = 0;
        var progress = new Progress<ScriptStepResult>(_ => System.Threading.Interlocked.Increment(ref reported));

        var result = await _runner.RunAsync(script, _session, progress: progress, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        // Progress<T> posts via sync context; give it a beat.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(2, reported);
    }
}
