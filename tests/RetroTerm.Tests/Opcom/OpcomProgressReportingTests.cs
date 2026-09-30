using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Transfer;
using Xunit;

namespace RetroTerm.Tests.Opcom;

/// <summary>
/// What the protocol reports while it works, which is what the OPCOM window's status bar
/// shows.
///
/// This exists because a dump against a real machine takes seconds - every character is
/// sent singly and waits for its echo, and a seventeen-word dump measured 3.2 seconds -
/// and until now the window looked exactly the same whether it was working or hung.
///
/// The fake machine here replies only when pumped, so each report can be checked in order
/// rather than raced against.
/// </summary>
public class OpcomProgressReportingTests
{
    private readonly OpcomProtocol _protocol = new();
    private readonly Queue<byte> _unanswered = new();
    private readonly StringBuilder _typed = new();
    private readonly Dictionary<string, string> _replies = new(StringComparer.Ordinal);
    private readonly List<OpcomProgress> _reports = new();

    private const string CrLfHash = "\r\n#";

    public OpcomProgressReportingTests()
    {
        SendBytesAsync send = (data, ct) =>
        {
            for (int i = 0; i < data.Length; i++) _unanswered.Enqueue(data.Span[i]);
            return Task.CompletedTask;
        };
        _protocol.ProgressChanged += p => _reports.Add(p);
        _protocol.Activate(send);
        _protocol.DumpSettleMs = 20;
    }

    private void Pump()
    {
        int guard = 0;
        while (_unanswered.Count > 0)
        {
            if (++guard > 10000) throw new InvalidOperationException("the fake OPCOM never went quiet");
            byte b = _unanswered.Dequeue();
            char c = (char)b;
            _typed.Append(c);
            _protocol.ProcessIncomingData(new[] { b });

            string key = _typed.ToString();
            if (_replies.TryGetValue(key, out string? reply))
            {
                _typed.Clear();
                if (reply.Length > 0) _protocol.ProcessIncomingData(Encoding.ASCII.GetBytes(reply));
            }
            else if (c == ' ')
            {
                _typed.Clear();
            }
        }
    }

    private static async Task<OpcomResult> WithTimeout(Task<OpcomResult> task, string whatWentWrong)
    {
        var finished = await Task.WhenAny(task, Task.Delay(5000));
        Assert.True(ReferenceEquals(finished, task), whatWentWrong);
        return await task;
    }

    [Fact]
    public async Task AReadSaysWhatItIsDoingAndThenWhatItGot()
    {
        _replies["P/"] = "000004 ";

        var read = _protocol.ReadRegisterAsync(0, "P", TestContext.Current.CancellationToken);
        Pump();
        Assert.True(read.IsCompleted);
        Assert.True((await read).Success);

        // First report: it started, and says which register.
        Assert.True(_reports.Count >= 2, "the protocol reported fewer than a start and a finish");
        Assert.Equal(OpcomProgressKind.Running, _reports[0].Kind);
        Assert.Contains("register P", _reports[0].Message);

        // Last report: it finished, and says what came back.
        var last = _reports[_reports.Count - 1];
        Assert.Equal(OpcomProgressKind.Succeeded, last.Kind);
        Assert.Contains("000004", last.Message);
    }

    [Fact]
    public async Task AFailedCommandSaysSoAndSaysWhy()
    {
        // The machine never answers, so the watchdog abandons the command.
        _protocol.ResponseTimeoutMs = 100;
        var read = _protocol.ReadRegisterAsync(0, "P", TestContext.Current.CancellationToken);
        // Deliberately do NOT pump: nothing is echoed at all.
        var result = await WithTimeout(read, "the watchdog never fired");
        Assert.False(result.Success);

        var last = _reports[_reports.Count - 1];
        Assert.Equal(OpcomProgressKind.Failed, last.Kind);
        Assert.Contains("FAILED", last.Message);
        Assert.Contains("No response from OPCOM", last.Message);
    }

    [Fact]
    public async Task AMemoryDumpCountsItsWordsUpAsTheyArrive()
    {
        // The measured reply shape: prompt first, then address-labelled lines of eight.
        _replies["0<000020\r"] = CrLfHash + "\r"
            + "000000 /114631 031463 073567 167356 146314 114631 021042 042104 \r\n"
            + "000010 /135673 010421 073567 167356 146314 114631 021042 042104 \r\n"
            + "000020 /135673 ";

        var dump = _protocol.DumpMemoryAsync(0, 16, TestContext.Current.CancellationToken);
        Pump();
        var result = await WithTimeout(dump, "the dump never finished");
        Assert.True(result.Success, result.ErrorMessage);

        // It said what it was about to do, with both addresses in the message.
        Assert.Equal(OpcomProgressKind.Running, _reports[0].Kind);
        Assert.Contains("Dumping memory", _reports[0].Message);
        Assert.Equal(17, _reports[0].Total);
        Assert.Equal(0, _reports[0].Completed);

        // Every word moved the count on by one, in order, never past the total.
        int expectedSoFar = 0;
        for (int i = 0; i < _reports.Count; i++)
        {
            if (_reports[i].Kind != OpcomProgressKind.Running) continue;
            Assert.Equal(17, _reports[i].Total);
            Assert.True(_reports[i].Completed >= expectedSoFar,
                "the count went backwards at report " + i);
            Assert.True(_reports[i].Completed <= 17, "the count went past the total");
            expectedSoFar = _reports[i].Completed;
        }
        Assert.Equal(17, expectedSoFar);

        // And it finished by saying how many words it read.
        var last = _reports[_reports.Count - 1];
        Assert.Equal(OpcomProgressKind.Succeeded, last.Kind);
        Assert.Contains("17 words", last.Message);
    }

    [Fact]
    public void PercentIsMinusOneWhenThereIsNothingToCount()
    {
        // A single examine has one step, so a bar cannot show a fraction of it. The window
        // reads this as "run the bar indeterminate" rather than drawing a false zero.
        var running = new OpcomProgress(OpcomProgressKind.Running, "Reading register P", 0, 0);
        Assert.Equal(-1, running.Percent);
        Assert.True(running.IsRunning);

        var half = new OpcomProgress(OpcomProgressKind.Running, "Dumping", 5, 10);
        Assert.Equal(50, half.Percent);

        Assert.False(OpcomProgress.Idle.IsRunning);
        Assert.Equal(OpcomProgressKind.Idle, OpcomProgress.Idle.Kind);
    }

    [Fact]
    public async Task ARegisterDumpCountsUpToTheNumberOfRegistersItAskedFor()
    {
        _replies["0<1RD\r"] = CrLfHash + "\r"
            + "000000 /000051 177777 000004 170400 000011 073567 000001 000002 \r\n"
            + "000010 /000000 000000 040440 000000 000000 000000 000000 000000 \r\n"
            + "000020 /";

        var dump = _protocol.DumpRegistersAsync(0, 1, TestContext.Current.CancellationToken);
        Pump();
        var result = await WithTimeout(dump, "the register dump never finished");
        Assert.True(result.Success, result.ErrorMessage);

        // Two levels of eight registers.
        Assert.Equal(16, _reports[0].Total);

        int highest = 0;
        for (int i = 0; i < _reports.Count; i++)
        {
            if (_reports[i].Kind == OpcomProgressKind.Running && _reports[i].Completed > highest)
                highest = _reports[i].Completed;
        }
        Assert.Equal(16, highest);
    }
}
