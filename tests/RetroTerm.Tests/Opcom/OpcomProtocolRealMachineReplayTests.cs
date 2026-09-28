using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Transfer;
using Xunit;

namespace RetroTerm.Tests.Opcom;

/// <summary>
/// Drives OpcomProtocol against a fake OPCOM that answers with the exact bytes a real
/// ND-120/CX sent over COM11 (9600 7E1) on 6 September 2026. Every response string in this
/// file was read off the protocol trace of that session, not written from the manual.
///
/// The fake echoes every byte it is sent, as the real machine does, and then appends the
/// recorded reply for the command that byte completed. Replies are pushed back into the
/// protocol synchronously from inside the send delegate, so a whole command runs to
/// completion within the first ProcessIncomingData call that starts it.
/// </summary>
public class OpcomProtocolRealMachineReplayTests
{
    private readonly OpcomProtocol _protocol = new();
    private readonly StringBuilder _sent = new();
    private readonly StringBuilder _typed = new(); // what OPCOM has seen since the last reply
    private readonly Dictionary<string, string> _replies = new(StringComparer.Ordinal);

    // Bytes the protocol has sent that the fake machine has not answered yet. Nothing is
    // answered from inside the send delegate: a real serial line replies later, and the
    // difference matters. Answering inline let each command finish before the next one
    // was even enqueued, which is exactly the case the "Refresh all" defect does NOT hit
    // - there several reads are queued first and the queue then has to keep itself going.
    private readonly Queue<byte> _unanswered = new();
    private bool _dropNextSlash; // used by the watchdog test to make the machine go silent

    public OpcomProtocolRealMachineReplayTests()
    {
        SendBytesAsync send = (data, ct) =>
        {
            for (int i = 0; i < data.Length; i++)
            {
                _sent.Append((char)data.Span[i]);
                _unanswered.Enqueue(data.Span[i]);
            }
            return Task.CompletedTask;
        };
        _protocol.Activate(send);
        // A dump now finishes only after the line has been quiet for a moment (the real
        // machine keeps printing past its last value). The fake goes quiet the instant
        // the pump drains, so a short settle keeps these tests fast.
        _protocol.DumpSettleMs = 20;
    }

    /// <summary>
    /// Runs the fake OPCOM until it has nothing left to answer: each queued byte is
    /// echoed, and when the characters typed so far match a recorded command, that
    /// command's measured reply follows. Feeding a reply usually makes the protocol send
    /// more bytes, which the loop then picks up, so one call drives a whole exchange.
    /// </summary>
    private void Pump()
    {
        int guard = 0;
        while (_unanswered.Count > 0)
        {
            if (++guard > 10000) throw new InvalidOperationException("the fake OPCOM never went quiet");
            byte b = _unanswered.Dequeue();
            char c = (char)b;
            _typed.Append(c);

            if (c == '/' && _dropNextSlash)
            {
                // The machine goes silent here: no echo, no reply, nothing.
                _dropNextSlash = false;
                _typed.Clear();
                continue;
            }

            _protocol.ProcessIncomingData(new[] { b }); // the echo

            string key = _typed.ToString();
            if (_replies.TryGetValue(key, out string? reply))
            {
                _typed.Clear();
                if (reply.Length > 0)
                    _protocol.ProcessIncomingData(Encoding.ASCII.GetBytes(reply));
            }
            else if (c == ' ')
            {
                // A space is echoed and nothing else follows it; the typed line restarts.
                _typed.Clear();
            }
        }
    }

    /// <summary>
    /// Awaits a command that finishes on a timer rather than on a byte, and fails with a
    /// readable message instead of hanging the suite if it never does.
    /// </summary>
    private static async Task<OpcomResult> WithTimeout(Task<OpcomResult> task, string whatWentWrong)
    {
        var finished = await Task.WhenAny(task, Task.Delay(5000));
        Assert.True(ReferenceEquals(finished, task), whatWentWrong);
        return await task;
    }

    // ---------- measured replies ----------
    // "0/" gave "114631 "; CR then gave CR LF "#031463 " (value of address 1, no address).
    // "P/" gave "000004 "; CR then gave CR LF "#".
    // A space after any of these was echoed and nothing else came back.
    private const string CrLfHash = "\r\n#";

    [Fact]
    public async Task TwoRegisterReadsInARowBothComplete_TheRealMachineSendsNoPromptAfterTheSpace()
    {
        // This is the "Refresh all reads one register per click" defect. The old code
        // waited for a '#' after the examine-cancelling space; the real machine never
        // sends one, so the second read never started.
        _replies["P/"] = "000004 ";
        _replies["A/"] = "052525 ";

        // Both are queued BEFORE the machine answers anything, which is what the
        // Refresh-all button does. Only then does the fake OPCOM start replying.
        var p = _protocol.ReadRegisterAsync(0, "P");
        var a = _protocol.ReadRegisterAsync(0, "A");
        Pump();

        Assert.True(p.IsCompleted, "first read did not complete");
        Assert.True(a.IsCompleted, "second read did not complete: the queue stalled waiting for a '#' that the real machine never sends");
        Assert.True((await p).Success);
        Assert.True((await a).Success);
        Assert.Equal((ushort)0x0004, (await p).Value);
        Assert.Equal((ushort)0x5555, (await a).Value);
        Assert.Equal("P/ A/ ", _sent.ToString());
        Assert.Equal(OpcomProtocolState.Idle, _protocol.State);
    }

    [Fact]
    public async Task MemoryReadCancelsWithASpaceAndTheNextCommandFollowsAtOnce()
    {
        _replies["0/"] = "114631 ";
        _replies["1/"] = "031463 ";

        var first = _protocol.ReadMemoryAsync(0);
        var second = _protocol.ReadMemoryAsync(1);
        Pump();

        Assert.True(first.IsCompleted && second.IsCompleted);
        Assert.Equal((ushort)0x9999, (await first).Value);  // 114631 octal
        Assert.Equal((ushort)0x3333, (await second).Value); // 031463 octal
        Assert.Equal("0/ 1/ ", _sent.ToString());
    }

    [Fact]
    public async Task MemoryWriteSkipsTheHashOnTheAdvanceLineAndLeavesExamineMode()
    {
        // Real bytes after "0/114631 " + "114631" CR were CR LF "#031463 " - the '#'
        // there is a line start, not a prompt: OPCOM is still in examine mode at
        // address 1. The old code took it for a prompt, completed the write, and left
        // the machine in examine mode, so the NEXT command's characters were typed
        // into the examine line.
        _replies["0/"] = "114631 ";
        _replies["114631\r"] = CrLfHash + "031463 ";
        _replies["P/"] = "000004 ";

        var write = _protocol.WriteMemoryAsync(0, 0x9999);
        var read = _protocol.ReadRegisterAsync(0, "P");
        Pump();

        Assert.True(write.IsCompleted, "write did not complete");
        Assert.True((await write).Success, (await write).ErrorMessage);
        // A deposit reports the value it wrote. It used to report zero, which the MCP
        // surface printed as "mem = 000000" - the same thing it would print after really
        // writing a zero, so the result confirmed nothing.
        Assert.Equal((ushort)0x9999, (await write).Value);
        Assert.True(read.IsCompleted, "read after write did not complete");
        Assert.True((await read).Success);
        // The space that leaves examine mode must be sent BEFORE the next command.
        Assert.Equal("0/114631\r P/ ", _sent.ToString());
    }

    [Fact]
    public async Task RegisterWriteCompletesOnTheHashAfterCr()
    {
        // Register deposit measured: "P/000004 " then "4" CR gave CR LF "#".
        _replies["P/"] = "000004 ";
        _replies["4\r"] = CrLfHash;

        var write = _protocol.WriteRegisterAsync(0, "P", 4);
        Pump();

        Assert.True(write.IsCompleted);
        Assert.True((await write).Success, (await write).ErrorMessage);
        Assert.Equal((ushort)4, (await write).Value); // the value written, not zero
        Assert.Equal("P/4\r", _sent.ToString());
        Assert.Equal(OpcomProtocolState.Idle, _protocol.State);
    }

    [Fact]
    public async Task StopIsSentWithACarriageReturnBecauseTheMachineAnswersNothingWithoutOne()
    {
        // Measured on a real ND-120/CX on 6 September 2026: "STOP" on its own is echoed and
        // answered with NOTHING. The carriage return is what draws CR LF and the prompt,
        // so without it the command sat there until the watchdog gave up.
        _replies["STOP\r"] = CrLfHash;

        var stop = _protocol.StopCpuAsync();
        Pump();

        Assert.True(stop.IsCompleted, "STOP never finished - the carriage return is missing");
        Assert.True((await stop).Success, (await stop).ErrorMessage);
        Assert.Equal("STOP\r", _sent.ToString());
        Assert.Equal(OpcomCpuState.Stopped, _protocol.CpuState);
    }

    [Fact]
    public async Task SingleStepIsSentWithACarriageReturnToo()
    {
        // The same measurement: "Z" alone is only echoed. The carriage return is what
        // executes the instruction and answers CR LF and a prompt.
        _replies["Z\r"] = CrLfHash;

        var step = _protocol.SingleStepAsync();
        Pump();

        Assert.True(step.IsCompleted, "single step never finished - the carriage return is missing");
        Assert.True((await step).Success, (await step).ErrorMessage);
        Assert.Equal("Z\r", _sent.ToString());
    }

    [Fact]
    public async Task AnIoxReadIsJustAnExamineAndComesBackWithTheDeviceWord()
    {
        // Measured on the ND-120/CX, 9 September 2026: "400IO/" answered "000000 " -
        // six octal digits and a space, exactly like examining a register.
        _replies["400IO/"] = "000000 ";

        var iox = _protocol.IOXReadAsync(0x100); // 400 octal
        Pump();

        Assert.True(iox.IsCompleted, "the IOX read never finished");
        Assert.True((await iox).Success, (await iox).ErrorMessage);
        Assert.Equal((ushort)0, (await iox).Value);
        Assert.Equal("400IO/ ", _sent.ToString());   // the trailing space leaves examine mode
    }

    [Fact]
    public async Task AnIoxWriteSetsOprFirstAndThenRunsTheOddAddress()
    {
        // Three steps on the wire, measured the same day:
        //   OPR/     -> the current operator register
        //   0 CR     -> deposit the word to write, answered CR LF '#'
        //   401IO/   -> the odd address performs the output and shows a value
        //
        // Before this was measured the code stopped after reading OPR and reported that
        // as the result, so no write ever reached the device.
        _replies["OPR/"] = "000000 ";
        _replies["0\r"] = CrLfHash;
        _replies["401IO/"] = "000000 ";

        var iox = _protocol.IOXWriteAsync(0x101, 0);   // 401 octal, write zero
        Pump();

        Assert.True(iox.IsCompleted, "the IOX write never finished - it used to stop after reading OPR");
        Assert.True((await iox).Success, (await iox).ErrorMessage);
        Assert.Equal("OPR/0\r401IO/ ", _sent.ToString());
    }

    [Fact]
    public async Task TheMemoryTestIsAnsweredWithAPromptAndNoReport()
    {
        // Measured on the ND-120/CX: "0#" is echoed and answered with a single prompt.
        // Nothing says whether the bank passed - that has to be read from memory.
        _replies["0#"] = CrLfHash;

        var test = _protocol.MemoryTestAsync(0);
        Pump();

        Assert.True(test.IsCompleted, "the memory test never finished");
        Assert.True((await test).Success, (await test).ErrorMessage);
        Assert.Equal("0#", _sent.ToString());
    }

    [Fact]
    public async Task MemoryDumpParsesTheAddressLabelledLinesAndCompletesOnTheCount()
    {
        // "0<20" CR measured reply, verbatim. Note: the '#' comes BEFORE the data, every
        // line starts with "AAAAAA /", and nothing at all follows the last word.
        _replies["0<000020\r"] = CrLfHash + "\r"
            + "000000 /114631 031463 073567 167356 146314 114631 021042 042104 \r\n"
            + "000010 /135673 010421 073567 167356 146314 114631 021042 042104 \r\n"
            + "000020 /135673 ";

        var dump = _protocol.DumpMemoryAsync(0, 16); // 0..20 octal = 17 words
        Pump();

        var result = await WithTimeout(dump, "the memory dump never finished: the parser waited for a trailing '#' that never comes");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.DumpValues);
        ushort[] words = result.DumpValues!;
        Assert.Equal(17, words.Length);
        Assert.Equal((ushort)0x9999, words[0]);  // 114631
        Assert.Equal((ushort)0x3333, words[1]);  // 031463
        Assert.Equal((ushort)0xBBBB, words[8]);  // 135673 - first word of the second line
        Assert.Equal((ushort)0x1111, words[9]);  // 010421
        Assert.Equal((ushort)0xBBBB, words[16]); // 135673 - the lone word on the last line
        Assert.True(_protocol.Memory.TryGet(8, out ushort cached));
        Assert.Equal((ushort)0xBBBB, cached);
        Assert.Equal(OpcomProtocolState.Idle, _protocol.State);
    }

    [Fact]
    public async Task RegisterDumpFillsTwoLevelsAndIgnoresTheDanglingLineStart()
    {
        // "0<1RD" CR measured reply, verbatim, including the "000020 /" it leaves hanging.
        _replies["0<1RD\r"] = CrLfHash + "\r"
            + "000000 /000051 177777 000004 170400 000011 073567 000001 000002 \r\n"
            + "000010 /000000 000000 040440 000000 000000 000000 000000 000000 \r\n"
            + "000020 /";

        var dump = _protocol.DumpRegistersAsync(0, 1);
        Pump();

        var regResult = await WithTimeout(dump, "the register dump never finished");
        Assert.True(regResult.Success, regResult.ErrorMessage);
        // Level 0: STS D P B L A T X
        Assert.Equal((ushort)0x0029, _protocol.Registers.Levels[0].GetByIndex(0)); // 000051
        Assert.Equal((ushort)0xFFFF, _protocol.Registers.Levels[0].GetByIndex(1)); // 177777
        Assert.Equal((ushort)0x0004, _protocol.Registers.Levels[0].GetByIndex(2)); // P
        Assert.Equal((ushort)0x0002, _protocol.Registers.Levels[0].GetByIndex(7)); // X
        // Level 1: P is 040440
        Assert.Equal((ushort)0x4120, _protocol.Registers.Levels[1].GetByIndex(2));
        Assert.Equal(OpcomProtocolState.Idle, _protocol.State);
    }

    [Fact]
    public async Task InternalRegisterDumpCompletesAfterFifteenValues()
    {
        // "IRD" CR measured reply, verbatim: fifteen values, not fourteen, not sixteen.
        _replies["IRD\r"] = CrLfHash + "\r"
            + "000000 /140000 010051 000000 041361 153612 000000 000000 000000 \r\n"
            + "000010 /000014 000001 020500 036000 051766 000000 000000 ";

        var dump = _protocol.DumpInternalRegistersAsync();
        Pump();

        var irdResult = await WithTimeout(dump, "IRD never finished");
        Assert.True(irdResult.Success, irdResult.ErrorMessage);
        Assert.Equal((ushort)0xC000, _protocol.Registers.Internal[0]);  // PANS 140000
        Assert.Equal((ushort)0x1029, _protocol.Registers.Internal[1]);  // STS 010051
        Assert.Equal((ushort)0x2140, _protocol.Registers.Internal[10]); // ALD 020500
        Assert.Equal((ushort)0x53F6, _protocol.Registers.Internal[12]); // PCR 051766
        Assert.Equal(OpcomProtocolState.Idle, _protocol.State);
    }

    [Fact]
    public async Task ACommandThatGetsNoAnswerFailsWithTheWatchdogAndTheQueueMovesOn()
    {
        // OPCOM goes silent after echoing "P": no echo of the '/'. The command must not
        // hang forever; it must fail naming where it was stuck, a cancelling space
        // must go out, and the next queued command must still run.
        _protocol.ResponseTimeoutMs = 100;
        _replies["P/"] = "000004 ";
        _replies["A/"] = "052525 ";
        _dropNextSlash = true; // the machine swallows the first '/' and says nothing

        var stuck = _protocol.ReadRegisterAsync(0, "P");
        var next = _protocol.ReadRegisterAsync(0, "A");
        Pump();

        var finished = await Task.WhenAny(stuck, Task.Delay(3000));
        Assert.Same(stuck, finished);
        Assert.False((await stuck).Success);
        Assert.Contains("No response from OPCOM", (await stuck).ErrorMessage);
        Assert.Contains("ReadRegister P", (await stuck).ErrorMessage);
        Assert.Contains("AwaitingEcho byte 1 of 2", (await stuck).ErrorMessage);

        // The watchdog sent its cancelling space and started the queued read; let the
        // machine answer those.
        Pump();
        finished = await Task.WhenAny(next, Task.Delay(3000));
        Assert.Same(next, finished);
        Assert.True((await next).Success, (await next).ErrorMessage);
        Assert.Equal((ushort)0x5555, (await next).Value);
        // "P/" typed, silence, the watchdog's cancelling space, then the next command.
        Assert.Equal("P/ A/ ", _sent.ToString());
    }
}
