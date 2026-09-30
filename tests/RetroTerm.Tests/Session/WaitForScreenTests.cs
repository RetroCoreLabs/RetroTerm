using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Tests for WaitForScreenAsync (P1.4) and the P1.3 session additions
/// (SendBytesAsync, DataReceived event, byte counters).
/// The scenarios mirror the failures behind the MCP terminal-control rules in
/// docs\MCP-AND-SCRIPTING.md ("Rules the design enforces", the 28-minute login):
/// prompts split across chunks, prompts echoed mid-listing, timeouts that
/// must still return the screen.
/// </summary>
public class WaitForScreenTests : IDisposable
{
    private readonly VT100Emulator _emulator;
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;

    public WaitForScreenTests()
    {
        _emulator = new VT100Emulator(80, 24);
        _session = new TerminalSession(_emulator, "WaitTest");
        _connection = new InMemoryConnection();
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // WaitForScreenAsync — pattern matching
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WaitForScreen_MatchesPromptAlreadyOnScreen()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("SINTRAN III\r\nENTER"));

        var result = await _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = "ENTER",
            TimeoutMs = 5000
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        Assert.False(result.TimedOut);
        Assert.Equal("ENTER", result.MatchedText);
        Assert.Contains("ENTER", result.Screen.Text);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForScreen_MatchesPromptArrivingLater_SplitAcrossChunks()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        var waitTask = _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = "PASSWORD:",
            TimeoutMs = 5000
        }, TestContext.Current.CancellationToken);

        // Prompt arrives in pieces, after a delay — the 28-minute-login-race scenario.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("PASS"));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("WORD:"));

        var result = await waitTask;

        Assert.True(result.Matched);
        Assert.Equal("PASSWORD:", result.MatchedText);
    }

    [Fact]
    public async Task WaitForScreen_TailMatch_IgnoresPromptEchoedMidListing()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        // "X-C:" appears in the middle of a listing but the output continues —
        // tail matching must NOT trigger on it (handover §2.2).
        _connection.SimulateReceive(Encoding.UTF8.GetBytes(
            "line before\r\necho of X-C: inside listing\r\nmore output follows"));

        var result = await _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = "X-C:",
            Where = ScreenMatchWhere.ScreenTail,
            TimeoutMs = 300
        }, TestContext.Current.CancellationToken);

        Assert.False(result.Matched);
        Assert.True(result.TimedOut);

        // Now the real prompt arrives at the end of output — tail match must succeed.
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("\r\nX-C:"));

        var result2 = await _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = "X-C:",
            Where = ScreenMatchWhere.ScreenTail,
            TimeoutMs = 5000
        }, TestContext.Current.CancellationToken);

        Assert.True(result2.Matched);
    }

    [Fact]
    public async Task WaitForScreen_AnywhereMatch_FindsMidListingText()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes(
            "header\r\nthe needle is here\r\nfooter"));

        var result = await _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = "needle",
            Where = ScreenMatchWhere.AnywhereOnScreen,
            TimeoutMs = 5000
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
    }

    [Fact]
    public async Task WaitForScreen_RegexMatch_ReturnsMatchedText()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("SINTRAN III - VSX/500 VERSION K"));

        var result = await _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = @"VERSION [A-Z]",
            MatchType = ScreenMatchType.Regex,
            Where = ScreenMatchWhere.AnywhereOnScreen,
            TimeoutMs = 5000
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        Assert.Equal("VERSION K", result.MatchedText);
    }

    [Fact]
    public async Task WaitForScreen_Timeout_StillReturnsScreenAndElapsed()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("partial output the machine managed to say"));

        var result = await _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = "NEVER-APPEARS",
            TimeoutMs = 200
        }, TestContext.Current.CancellationToken);

        // Handover rules 4+5: a timeout still returns what the machine said, and how long we waited.
        Assert.False(result.Matched);
        Assert.True(result.TimedOut);
        Assert.Contains("partial output", result.Screen.Text);
        Assert.True(result.Elapsed >= TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task WaitForScreen_Disconnect_EndsWaitAndSaysSo()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("some output"));

        var waitTask = _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = "NEVER-APPEARS",
            TimeoutMs = 10_000
        }, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        await _connection.DisconnectAsync(); // remote drop → StatusChanged(Disconnected)

        var result = await waitTask;

        // Rule 6: do not swallow the error — the result says the connection dropped
        // and still shows the screen.
        Assert.False(result.Matched);
        Assert.True(result.Disconnected);
        Assert.Contains("some output", result.Screen.Text);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task WaitForScreen_IdleMode_FiresWhenScreenGoesQuiet()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("output"));

        var result = await _session.WaitForScreenAsync(new ScreenWaitOptions
        {
            Pattern = null,
            IdleMs = 150,
            TimeoutMs = 5000
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        Assert.Null(result.MatchedText);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForScreen_NoPatternAndNoIdle_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _session.WaitForScreenAsync(new ScreenWaitOptions { Pattern = null, IdleMs = 0 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForScreen_Cancellation_Throws()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _session.WaitForScreenAsync(new ScreenWaitOptions
            {
                Pattern = "NEVER-APPEARS",
                TimeoutMs = 10_000
            }, cts.Token));
    }

    // ─────────────────────────────────────────────────────────────
    // P1.3: SendBytesAsync, DataReceived, counters
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendBytesAsync_SendsRawEscByte()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        // ESC is how you wake a SINTRAN line — must survive as a single raw byte.
        await _session.SendBytesAsync(new byte[] { 0x1B }, TestContext.Current.CancellationToken);

        var sent = _connection.GetSentData();
        Assert.Single(sent);
        Assert.Single(sent[0]);
        Assert.Equal(0x1B, sent[0][0]);
    }

    [Fact]
    public async Task DataReceivedEvent_ForwardsRawBytes()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        byte[]? forwarded = null;
        _session.DataReceived += bytes => forwarded = bytes;

        var payload = Encoding.UTF8.GetBytes("host says hi");
        _connection.SimulateReceive(payload);
        await _session.FlushAsync();

        Assert.NotNull(forwarded);
        Assert.Equal(payload, forwarded);
    }

    [Fact]
    public async Task ConnectionLost_FiresOnRemoteDrop_NotOnUserDisconnect()
    {
        string? lostReason = null;
        _session.ConnectionLost += reason => lostReason = reason;

        // User-initiated disconnect: must NOT fire ConnectionLost.
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        await _session.DisconnectAsync();
        Assert.Null(lostReason);

        // Remote drop (connection raises Disconnected itself): must fire.
        var connection2 = new InMemoryConnection();
        await _session.ConnectAsync(connection2, TestContext.Current.CancellationToken);
        await connection2.DisconnectAsync();
        Assert.NotNull(lostReason);
        Assert.Contains("remote", lostReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Counters_TrackBytesAndTimeSinceLastReceive()
    {
        Assert.Null(_session.TimeSinceLastReceive); // nothing received yet

        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        _connection.SimulateReceive(Encoding.UTF8.GetBytes("12345"));
        await _session.FlushAsync();
        await _session.SendInputAsync("abc", TestContext.Current.CancellationToken);

        Assert.Equal(5, _session.BytesReceived);
        Assert.Equal(3, _session.BytesSent);
        Assert.NotNull(_session.TimeSinceLastReceive);
        Assert.True(_session.TimeSinceLastReceive < TimeSpan.FromSeconds(10));
    }
}
