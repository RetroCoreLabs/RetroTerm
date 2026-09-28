using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Transfer;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// OPCOM protocol state machine states.
/// </summary>
public enum OpcomProtocolState
{
    Inactive,
    Idle,
    SendingCommand,
    AwaitingEcho,
    ReadingValue,
    WritingValue,
    DumpingMemory,
    DumpingRegisters,
    WaitingPrompt,
    Uploading,
    WaitingMclComplete,

    /// <summary>
    /// Every value a dump was asked for has arrived, but OPCOM has not stopped talking.
    /// Bytes are read and thrown away until the line goes quiet, and only then is the
    /// command finished. See OpcomProtocol.DumpSettleMs for why this state has to exist.
    /// </summary>
    DrainingDump,
    Error,
}

/// <summary>
/// OPCOM protocol handler implementing the character-echo based communication
/// with the ND-100 OPCOM microprogram.
///
/// Protocol rules (from OPCOM-COMMAND-REFERENCE.md and ndcomm.c):
/// - Send one character, wait for echo before sending next
/// - '#' = OPCOM ready prompt
/// - '/' after address/register = examine, response is octal value followed by space
/// - CR advances to next address or completes a deposit
/// - Space or unrecognized char cancels input and produces '?'
/// - All values are octal
///
/// Measured on a real ND-120/CX OPCOM over COM11 (9600 7E1) on 6 September 2026, and
/// different from what the first version of this class assumed:
/// - A space typed in examine mode is ECHOED ONLY. No '#' prompt follows it. The next
///   command can be typed straight away. Waiting for a '#' here stalled the queue, which
///   showed up as "Refresh all" reading one register per button press.
/// - CR in MEMORY examine mode (with or without a value typed first) answers with
///   CR LF '#' VALUE SPACE. That '#' is the line start, NOT a ready prompt, and no
///   address is printed. Register examine plus CR answers CR LF '#' and leaves examine.
/// - A dump (addr, less-than, addr, CR; level, less-than, level, RD, CR; IRD, CR) answers
///   CR LF '#' CR first, THEN the lines "AAAAAA /VVVVVV VVVVVV ... " eight words per line
///   separated by CR LF, and NOTHING after the last word. The only way to know a dump is
///   finished is to count. The RD dump prints the start of one more line ("000020 /")
///   after its last value.
/// - IRD prints 15 values, not the 14 in InternalRegisterDefs nor the 16 in the manual.
///
/// Every command is guarded by a response watchdog: if OPCOM goes quiet for
/// ResponseTimeoutMs while a command is in progress, the command fails with a message
/// naming the state it was in, a space is sent to cancel whatever OPCOM was doing, and
/// the next queued command starts. Without this a single missed byte stalled the queue
/// for good, and the MCP timeout only gave up on the caller's task, not on the queue.
/// </summary>
public sealed class OpcomProtocol : IOpcomHandler
{
    private readonly OpcomCommandQueue _queue = new();
    private readonly byte[] _responseBuffer = new byte[256];
    private readonly byte[] _singleByteBuf = new byte[1]; // Reusable buffer for SendByteAsync
    private int _responseLength;

    // Echo tracking
    private byte[] _commandBytes = Array.Empty<byte>();
    private int _commandIndex;

    // Dump parsing
    private ushort[] _dumpValues = Array.Empty<ushort>();
    private int[] _dumpAddresses = Array.Empty<int>();
    private int _dumpCount;
    private int _dumpExpectedEnd;
    private int _dumpCurrentAddress;

    // Upload state
    private ushort[]? _uploadWords;
    private int _uploadStartAddress;
    private int _uploadCount;
    private int _uploadIndex;
    private Action<int, int>? _uploadProgressCallback;
    private OpcomUploadSubState _uploadSubState;

    // State
    private OpcomProtocolState _state = OpcomProtocolState.Inactive;
    private OpcomCpuState _cpuState = OpcomCpuState.Unknown;
    private SendBytesAsync? _sendDelegate;
    private OpcomCommand? _currentCommand;
    private bool _pendingReadComplete;  // After pure read, waiting for CR echo then '#'
    private bool _writePhase;           // True when we've read the value and are now writing
    private bool _memoryWriteExitPending; // After memory CR deposit, waiting for advance data before sending space to exit
    private volatile bool _bpunTransferActive; // True during raw BPUN binary transfer — suppresses protocol processing

    // Response watchdog. Armed whenever a command is in progress, re-armed on every
    // received byte, disarmed when the command completes. Fires on a thread-pool thread.
    private readonly Timer _watchdog;
    private readonly object _sync = new();

    // Fires once the line has been quiet for DumpSettleMs while draining a dump.
    private readonly Timer _settle;
    private OpcomResult? _drainResult; // the finished dump's result, held until quiet

    /// <summary>
    /// How long OPCOM may stay silent in the middle of a command before the command is
    /// abandoned. One character at 9600 7E1 takes about a millisecond and the real
    /// ND-120 answers each one in 15 to 30 ms, so two seconds is generous. A dump of a
    /// large range is protected too: every received byte re-arms the timer, so it only
    /// fires when the machine has really gone quiet.
    /// </summary>
    public int ResponseTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// How long the line must stay quiet after a dump's last value before the dump is
    /// called finished.
    ///
    /// Measured on a real ND-120/CX over COM11 on 6 September 2026, and the reason this
    /// exists at all: a working-register dump goes on printing after its final value.
    /// The recorded bytes at the end of "0&lt;1RD" were the sixteenth value, then
    /// CR LF "000020 /" - the start of a line that never gets any values. Finishing the
    /// command at the sixteenth value and sending the next command immediately meant the
    /// character was typed while OPCOM was still transmitting, and OPCOM SILENTLY DROPPED
    /// IT. No echo ever came back, and the next command died on the watchdog. This is why
    /// a dump followed by anything else failed on the real machine while every unit test
    /// passed: a fake replies only when spoken to and is never busy.
    ///
    /// The ND emits characters about 16 ms apart, so a tenth of a second of silence
    /// means it has genuinely stopped rather than paused between characters.
    /// </summary>
    public int DumpSettleMs { get; set; } = 150;

    // Sub-state for register dump parsing
    private int _regDumpCurrentLevel;
    private int _regDumpEndLevel;
    private int _regDumpRegIndex;

    public bool PassThrough { get; set; } = true;
    public OpcomProtocolState State => _state;
    public OpcomCpuState CpuState => _cpuState;
    public bool IsActive => _state != OpcomProtocolState.Inactive;
    public OpcomRegisterState Registers { get; } = new();
    public OpcomMemoryCache Memory { get; } = new();

    public event Action<OpcomLogEntry>? LogEntry;
    public event Action<OpcomProtocolState>? StateChanged;
    public event Action<OpcomCpuState>? CpuStateChanged;

    /// <summary>
    /// Reports what the protocol is doing, so a window can show it without guessing.
    ///
    /// Raised when a command starts, as a dump's words arrive, and once more when the
    /// command finishes. Every OPCOM exchange is one character at a time with a wait for
    /// each echo, and the machine answers in tens of milliseconds, so even a modest dump
    /// takes seconds: a seventeen-word dump measured 3.2 seconds. Without this the window
    /// simply sat there and there was no way to tell a slow dump from a hung one.
    /// </summary>
    public event Action<OpcomProgress>? ProgressChanged;

    private void ReportProgress(string message, int completed, int total)
    {
        ProgressChanged?.Invoke(new OpcomProgress(OpcomProgressKind.Running, message, completed, total));
    }

    /// <summary>
    /// Says how the command that has just finished turned out. A finished command does
    /// not go quiet: it leaves a line saying whether it worked, because a status bar that
    /// clears itself the moment something fails is a status bar nobody can trust.
    /// </summary>
    private void ReportOutcome(OpcomCommand? cmd, OpcomResult result)
    {
        string what = cmd != null ? DescribeCommand(cmd) : "OPCOM";
        if (!result.Success)
        {
            ProgressChanged?.Invoke(new OpcomProgress(OpcomProgressKind.Failed,
                what + " FAILED: " + (result.ErrorMessage ?? "no reason given"), 0, 0));
            return;
        }

        string done;
        if (result.DumpValues != null)
        {
            done = what + " - done, " + result.DumpValues.Length + " words read";
        }
        else if (cmd != null && HasReadValue(cmd.Type))
        {
            done = what + " = " + OctalHelper.ToOctal6(result.Value);
        }
        else
        {
            done = what + " - done";
        }
        ProgressChanged?.Invoke(new OpcomProgress(OpcomProgressKind.Succeeded, done, 0, 0));
    }

    /// <summary>
    /// Whether this kind of command comes back with a value worth printing in the status
    /// line. A deposit does too, since it reports what it wrote.
    /// </summary>
    private static bool HasReadValue(OpcomCommandType type)
    {
        return type == OpcomCommandType.ReadMemory
            || type == OpcomCommandType.WriteMemory
            || type == OpcomCommandType.ReadRegister
            || type == OpcomCommandType.WriteRegister
            || type == OpcomCommandType.ReadInternalRegister
            || type == OpcomCommandType.WriteInternalRegister
            || type == OpcomCommandType.IOXRead
            || type == OpcomCommandType.IOXWrite;
    }

    /// <summary>
    /// A one-line description of what a command is about to do, for the status bar.
    /// Written for somebody watching the window rather than reading the protocol.
    /// </summary>
    private static string DescribeCommand(OpcomCommand cmd)
    {
        switch (cmd.Type)
        {
            case OpcomCommandType.ReadMemory: return "Reading memory " + cmd.CommandText;
            case OpcomCommandType.WriteMemory: return "Writing memory " + cmd.CommandText;
            case OpcomCommandType.DumpMemory:
                return "Dumping memory " + cmd.CommandText + " to " + OctalHelper.ToOctal6((ushort)cmd.EndAddress);
            case OpcomCommandType.ReadRegister: return "Reading register " + cmd.CommandText;
            case OpcomCommandType.WriteRegister: return "Writing register " + cmd.CommandText;
            case OpcomCommandType.ReadInternalRegister: return "Reading " + cmd.CommandText;
            case OpcomCommandType.WriteInternalRegister: return "Writing " + cmd.CommandText;
            case OpcomCommandType.DumpRegisters: return "Dumping working registers";
            case OpcomCommandType.DumpInternalRegisters: return "Dumping internal registers";
            case OpcomCommandType.IOXRead: return "IOX read from device " + cmd.CommandText;
            case OpcomCommandType.IOXWrite: return "IOX write to device " + cmd.CommandText;
            case OpcomCommandType.Stop: return "Stopping the CPU";
            case OpcomCommandType.MasterClear: return "Master clear";
            case OpcomCommandType.Start: return "Starting the CPU";
            case OpcomCommandType.SingleStep: return "Single step";
            case OpcomCommandType.Breakpoint: return "Running to a breakpoint at " + cmd.CommandText;
            case OpcomCommandType.Escape: return "Leaving OPCOM";
            case OpcomCommandType.SetExamineMode: return "Setting examine mode";
            case OpcomCommandType.UploadFile: return "Uploading";
            case OpcomCommandType.MemoryTest: return "Memory test on bank " + cmd.CommandText;
            case OpcomCommandType.BootLoad: return "Boot load";
            default: return "Working";
        }
    }

    public OpcomProtocol()
    {
        _queue.CommandEnqueued += OnCommandEnqueued;
        _watchdog = new Timer(OnWatchdog, null, Timeout.Infinite, Timeout.Infinite);
        _settle = new Timer(OnSettled, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Holds a finished dump back until OPCOM has stopped transmitting, then completes
    /// it. Sending the next command while the machine is still printing loses that
    /// character - see DumpSettleMs.
    /// </summary>
    private void BeginDrain(OpcomResult result)
    {
        _drainResult = result;
        _dumpAtLineStart = true;
        _dumpRunIsLabel = false;
        _responseLength = 0;
        SetState(OpcomProtocolState.DrainingDump);
        DisarmWatchdog();
        _settle.Change(DumpSettleMs, Timeout.Infinite);
    }

    private void OnSettled(object? state)
    {
        lock (_sync)
        {
            if (_state != OpcomProtocolState.DrainingDump) return;
            var result = _drainResult ?? OpcomResult.Ok();
            _drainResult = null;
            CompleteCurrentCommand(result);
        }
    }

    private void ArmWatchdog()
    {
        _watchdog.Change(ResponseTimeoutMs, Timeout.Infinite);
    }

    private void DisarmWatchdog()
    {
        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnWatchdog(object? state)
    {
        lock (_sync)
        {
            if (_state == OpcomProtocolState.Inactive || _state == OpcomProtocolState.Idle) return;
            if (_state == OpcomProtocolState.DrainingDump) return; // the settle timer owns this
            if (_bpunTransferActive) return;

            // OPCOM went quiet in the middle of a command. Give up on it, say exactly
            // where the flow was stuck, and put the line back in a known state: a space
            // cancels a half-typed command or examine mode on the real machine.
            string where = _state.ToString();
            if (_state == OpcomProtocolState.Uploading)
                where += "/" + _uploadSubState + " word " + _uploadIndex + " of " + _uploadCount;
            else if (_state == OpcomProtocolState.AwaitingEcho || _state == OpcomProtocolState.WritingValue)
                where += " byte " + _commandIndex + " of " + _commandBytes.Length;
            var cmd = _currentCommand;
            string what = cmd != null ? cmd.Type + " " + cmd.CommandText : "(no command)";
            var result = OpcomResult.Fail("No response from OPCOM for " + ResponseTimeoutMs + " ms during "
                + what + " (" + where + ")");

            _pendingReadComplete = false;
            _memoryWriteExitPending = false;
            _writePhase = false;
            _dumpAtLineStart = true;
            _dumpRunIsLabel = false;
            _responseLength = 0;
            SendByteAsync((byte)' ');
            CompleteCurrentCommand(result);
        }
    }

    public void Activate(SendBytesAsync sendDelegate)
    {
        _sendDelegate = sendDelegate ?? throw new ArgumentNullException(nameof(sendDelegate));
        SetState(OpcomProtocolState.Idle);
    }

    public void Deactivate()
    {
        DisarmWatchdog();
        _settle.Change(Timeout.Infinite, Timeout.Infinite);
        _drainResult = null;
        _queue.CancelAll("OPCOM deactivated");
        _sendDelegate = null;
        _currentCommand = null;
        SetState(OpcomProtocolState.Inactive);
    }

    private void SetState(OpcomProtocolState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            StateChanged?.Invoke(newState);
        }
    }

    private void SetCpuState(OpcomCpuState newState)
    {
        if (_cpuState != newState)
        {
            _cpuState = newState;
            CpuStateChanged?.Invoke(newState);
        }
    }

    private void Log(OpcomLogDirection direction, byte[] data)
    {
        LogEntry?.Invoke(new OpcomLogEntry(direction, data));
    }

    private void Log(OpcomLogDirection direction, byte singleByte)
    {
        LogEntry?.Invoke(new OpcomLogEntry(direction, new[] { singleByte }));
    }

    // ==================== ProcessIncomingData ====================

    /// <summary>
    /// While set, every received byte goes to this receiver instead of the OPCOM
    /// command state machine. Used by the NDBoot fast transfer: once the monitor is
    /// started the console belongs to it, not to OPCOM. Bytes are still logged.
    /// </summary>
    public Action<byte[]>? RawReceiver { get; set; }

    /// <summary>Sends bytes straight to the line (logged as Tx) for a raw protocol.</summary>
    public Task SendRawAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var send = _sendDelegate;
        if (send == null)
            return Task.FromException(new InvalidOperationException("OPCOM not connected"));
        Log(OpcomLogDirection.Tx, data.ToArray());
        return send(data, ct);
    }

    public void ProcessIncomingData(ReadOnlySpan<byte> data)
    {
        // During BPUN binary transfer, skip protocol processing — the binary loader
        // handles its own data and the protocol shouldn't interpret responses.
        if (_bpunTransferActive) return;

        // Log all received data
        Log(OpcomLogDirection.Rx, data.ToArray());

        var raw = RawReceiver;
        if (raw != null)
        {
            raw(data.ToArray());
            return;
        }

        lock (_sync)
        {
            for (int i = 0; i < data.Length; i++)
            {
                byte b = (byte)(data[i] & 0x7F); // Strip parity bit (7E1 mode)
                ProcessByte(b);
            }

            if (_state == OpcomProtocolState.DrainingDump)
            {
                // Still printing. Push the quiet deadline out from THIS byte, so the
                // dump only finishes once the machine has actually stopped.
                _settle.Change(DumpSettleMs, Timeout.Infinite);
            }
            else if (_state != OpcomProtocolState.Inactive && _state != OpcomProtocolState.Idle)
            {
                // The machine is talking: give it the full timeout again from this byte.
                ArmWatchdog();
            }
            else
            {
                DisarmWatchdog();
            }
        }
    }

    private void ProcessByte(byte b)
    {
        switch (_state)
        {
            case OpcomProtocolState.Idle:
                if (b == (byte)'#')
                {
                    // Prompt received - try to dequeue next command
                    TryStartNextCommand();
                }
                break;

            case OpcomProtocolState.AwaitingEcho:
                HandleEcho(b);
                break;

            case OpcomProtocolState.ReadingValue:
                HandleReadingValue(b);
                break;

            case OpcomProtocolState.WritingValue:
                HandleEcho(b); // Writing value also uses echo protocol
                break;

            case OpcomProtocolState.DumpingMemory:
                HandleDumpingMemory(b);
                break;

            case OpcomProtocolState.DumpingRegisters:
                HandleDumpingRegisters(b);
                break;

            case OpcomProtocolState.WaitingPrompt:
                if (_memoryWriteExitPending)
                {
                    // Memory CR deposit answered CR LF '#' VALUE SPACE (measured on the real
                    // ND-120/CX). That '#' is the line start of the advance line, not a ready
                    // prompt, so it must NOT complete the command: OPCOM is still in examine
                    // mode at the next address. Wait for the SPACE that ends the value, then
                    // send a space of our own to leave examine mode. The real machine only
                    // echoes that space, so the command completes on the echo, not on a '#'.
                    if (b == (byte)' ')
                    {
                        _memoryWriteExitPending = false;
                        SendCancelSpace();
                    }
                }
                else if (b == (byte)'#')
                {
                    // The OPR deposit has finished and the machine has stopped talking.
                    // NOW the IOX itself can go out - see the note where this phase is set.
                    if (_ioxPhase == IoxWritePhase.DepositingOpr && _currentCommand != null)
                    {
                        _ioxPhase = IoxWritePhase.RunningIox;
                        _commandBytes = System.Text.Encoding.ASCII.GetBytes(_currentCommand.CommandText + "IO/");
                        _commandIndex = 0;
                        SetState(OpcomProtocolState.AwaitingEcho);
                        SendNextByte();
                        break;
                    }
                    CompleteOk();
                }
                break;

            case OpcomProtocolState.WaitingMclComplete:
                HandleMclWait(b);
                break;

            case OpcomProtocolState.Uploading:
                HandleUploading(b);
                break;

            case OpcomProtocolState.DrainingDump:
                // The dump already has every value it asked for. Whatever OPCOM is still
                // printing is read and dropped; the settle timer ends the command.
                break;

            case OpcomProtocolState.Error:
                // In error state, wait for '#' to recover
                if (b == (byte)'#')
                {
                    SetState(OpcomProtocolState.Idle);
                    TryStartNextCommand();
                }
                break;
        }
    }

    // ==================== Command Starting ====================

    private void OnCommandEnqueued()
    {
        lock (_sync)
        {
            if (_state == OpcomProtocolState.Idle)
            {
                TryStartNextCommand();
            }
        }
    }

    private void TryStartNextCommand()
    {
        var cmd = _queue.TryDequeue();
        if (cmd == null) return;

        _currentCommand = cmd;
        _responseLength = 0;

        // Tell the window what is starting. A dump also knows how many words it expects,
        // so it can fill a bar; everything else is one step and reports no count.
        int expected = 0;
        if (cmd.Type == OpcomCommandType.DumpMemory)
        {
            OctalHelper.TryParseOctal32(cmd.CommandText.AsSpan(), out int dumpFrom);
            expected = cmd.EndAddress - dumpFrom + 1;
            if (expected < 1) expected = 1;
        }
        else if (cmd.Type == OpcomCommandType.DumpRegisters)
        {
            int levels = cmd.EndAddress - cmd.Level + 1;
            if (levels < 1) levels = 1;
            expected = levels * WorkingRegisterNames.Count;
        }
        else if (cmd.Type == OpcomCommandType.DumpInternalRegisters)
        {
            expected = InternalRegisterDefs.DumpWordCount;
        }
        ReportProgress(DescribeCommand(cmd), 0, expected);

        switch (cmd.Type)
        {
            case OpcomCommandType.ReadMemory:
                // Send "address/"
                StartSendCommand(cmd.CommandText + "/");
                break;

            case OpcomCommandType.WriteMemory:
                // Send "address/" first, then will write value after reading current
                StartSendCommand(cmd.CommandText + "/");
                break;

            case OpcomCommandType.DumpMemory:
                // Initialize dump state BEFORE sending (echoes may arrive fast)
                InitDumpMemory(cmd);
                // Send "startaddr<endaddr\r"
                StartSendCommand(cmd.CommandText + "<" + OctalHelper.ToOctal6((ushort)cmd.EndAddress) + "\r");
                break;

            case OpcomCommandType.ReadRegister:
                // Send "[level]regname/"
                string regCmd = cmd.Level > 0 ? OctalHelper.ToOctalTrimmed(cmd.Level) : "";
                StartSendCommand(regCmd + cmd.CommandText + "/");
                break;

            case OpcomCommandType.WriteRegister:
                // Send "[level]regname/" first, then write after reading
                string wregCmd = cmd.Level > 0 ? OctalHelper.ToOctalTrimmed(cmd.Level) : "";
                StartSendCommand(wregCmd + cmd.CommandText + "/");
                break;

            case OpcomCommandType.ReadInternalRegister:
                // Send "regname/"
                StartSendCommand(cmd.CommandText + "/");
                break;

            case OpcomCommandType.WriteInternalRegister:
                // Send "regname/" first
                StartSendCommand(cmd.CommandText + "/");
                break;

            case OpcomCommandType.DumpRegisters:
                // Initialize BEFORE sending (echoes may arrive fast)
                InitRegisterDump(cmd);
                // Send "startlevel<endlevelRD\r"
                string rdCmd = OctalHelper.ToOctalTrimmed(cmd.Level);
                string rdEnd = OctalHelper.ToOctalTrimmed(cmd.EndAddress);
                StartSendCommand(rdCmd + "<" + rdEnd + "RD\r");
                break;

            case OpcomCommandType.DumpInternalRegisters:
                // Send "IRD\r"
                // Initialize BEFORE sending (echoes may arrive fast). The echo handler
                // moves to DumpingRegisters once "IRD" CR has been echoed.
                _regDumpCurrentLevel = -1; // Special flag for internal registers
                _regDumpRegIndex = 0;
                _responseLength = 0;
                _dumpAtLineStart = true;
                _dumpRunIsLabel = false;
                _regDumpValuesExpected = InternalRegisterDefs.DumpWordCount;
                _regDumpValuesLeft = _regDumpValuesExpected;
                StartSendCommand("IRD\r");
                break;

            case OpcomCommandType.IOXRead:
                // Send "ddddIO/"
                StartSendCommand(cmd.CommandText + "IO/");
                break;

            case OpcomCommandType.IOXWrite:
                // First set OPR, then IOX
                // Send "OPR/" to start - we'll chain the IOX after
                StartSendCommand("OPR/");
                break;

            case OpcomCommandType.Stop:
                // STOP needs a carriage return. Measured on a real ND-120/CX on 6 September
                // 2026: "STOP" on its own is echoed and answered with NOTHING, and the
                // carriage return is what draws the CR LF '#' this code then waits for.
                // Without it the command sat until the watchdog gave up.
                StartSendCommand("STOP\r");
                // State transitions handled by TransitionAfterCommandSent
                break;

            case OpcomCommandType.MasterClear:
                _mclHashCount = 0;
                // MASTER CLEAR IS "MACL" AND IT NEEDS A CARRIAGE RETURN. Measured on a
                // real ND-120/CX over COM11 on 6 September 2026: "MACL" CR answers
                // CR LF '#' '#'. "MCL" - the spelling in this repo's own command
                // reference and in the code until now - is echoed and then REJECTED with
                // '?', so master clear could never have worked against the machine.
                StartSendCommand("MACL\r");
                // State transitions handled by TransitionAfterCommandSent
                break;

            case OpcomCommandType.Start:
                string startCmd = cmd.CommandText.Length > 0 ? cmd.CommandText + "!" : "!";
                StartSendCommand(startCmd);
                // State transitions handled by TransitionAfterCommandSent
                break;

            case OpcomCommandType.SingleStep:
                // Single step needs a carriage return, same as STOP and MACL. Measured on
                // a real ND-120/CX on 6 September 2026: "Z" alone is only echoed; the CR is
                // what runs the instruction and answers CR LF '#'. Sometimes a value and
                // a space follow that prompt, depending on what was examined beforehand;
                // OPCOM is NOT left in examine mode either way, which a following CR
                // proves by drawing a plain prompt rather than advancing.
                string stepCmd = cmd.StepCount > 1 ? OctalHelper.ToOctalTrimmed(cmd.StepCount) + "Z" : "Z";
                StartSendCommand(stepCmd + "\r");
                // State transitions handled by TransitionAfterCommandSent
                break;

            case OpcomCommandType.Breakpoint:
                // SETTING A BREAKPOINT RUNS THE CPU. Measured on a real ND-120/CX on
                // 6 September 2026, and it is not what the name suggests: "4." was echoed
                // and then, 1.4 seconds later, a SECOND '.' came back - the machine had
                // executed its way round to address 4 and stopped there. The command
                // reference says the same thing once read carefully: "if the specified
                // address is never reached, execution continues until a character other
                // than 0-7 or A-Y is typed".
                //
                // So this is a RUN command wearing another name, and the reply is a '.',
                // never the '#' this code used to wait for. Anything calling it must warn
                // first, the way the Start button does.
                StartSendCommand(cmd.CommandText + ".");
                // State transitions handled by TransitionAfterCommandSent
                break;

            case OpcomCommandType.Escape:
                SendByteAsync(0x1B); // ESC
                SetCpuState(OpcomCpuState.Unknown);
                CompleteCurrentCommand(OpcomResult.Ok());
                break;

            case OpcomCommandType.SetExamineMode:
                if (cmd.CommandText.Length > 0)
                    StartSendCommand(cmd.CommandText + "E\r");
                else
                    StartSendCommand("E\r");
                // State transitions handled by TransitionAfterCommandSent
                break;

            case OpcomCommandType.UploadFile:
                // Handled by UploadWordsAsync directly
                break;

            case OpcomCommandType.RawSend:
                if (cmd.RawBytes != null)
                {
                    SendBytesRawAsync(cmd.RawBytes);
                }
                CompleteCurrentCommand(OpcomResult.Ok());
                break;

            case OpcomCommandType.MemoryTest:
                // bb# - the microprogrammed memory test for one 64K bank. Measured on a
                // real ND-120/CX on 9 September 2026: echoed, then a single prompt and no
                // report at all. It writes memory, so it is not a read-only check.
                StartSendCommand(cmd.CommandText + "#");
                break;

            case OpcomCommandType.BootLoad:
                StartSendCommand(cmd.CommandText + "&");
                // State transitions handled by TransitionAfterCommandSent
                break;
        }
    }

    // ==================== Sending ====================

    private void StartSendCommand(string command)
    {
        _commandBytes = System.Text.Encoding.ASCII.GetBytes(command);
        _commandIndex = 0;
        SetState(OpcomProtocolState.AwaitingEcho);
        SendNextByte();
    }

    private void SendNextByte()
    {
        if (_commandIndex < _commandBytes.Length)
        {
            byte b = _commandBytes[_commandIndex];
            SendByteAsync(b);
            ArmWatchdog();
        }
    }

    private async void SendByteAsync(byte b)
    {
        if (_sendDelegate == null) return;
        try
        {
            Log(OpcomLogDirection.Tx, b);
            _singleByteBuf[0] = b;
            await _sendDelegate(_singleByteBuf, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            SetState(OpcomProtocolState.Error);
            CompleteCurrentCommand(OpcomResult.Fail("Send failed"));
        }
    }

    private async void SendBytesRawAsync(byte[] data)
    {
        if (_sendDelegate == null) return;
        try
        {
            Log(OpcomLogDirection.Tx, data);
            await _sendDelegate(data, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            SetState(OpcomProtocolState.Error);
        }
    }

    // ==================== Echo Handling ====================

    private void HandleEcho(byte b)
    {
        if (_commandIndex >= _commandBytes.Length)
        {
            // All command bytes sent and echoed, transition based on command type
            TransitionAfterCommandSent();
            return;
        }

        byte expected = _commandBytes[_commandIndex];
        if (b == expected)
        {
            _commandIndex++;
            if (_commandIndex >= _commandBytes.Length)
            {
                // Command fully echoed
                TransitionAfterCommandSent();
            }
            else
            {
                SendNextByte();
            }
        }
        else if (b == (byte)'?')
        {
            // OPCOM rejected input
            SetState(OpcomProtocolState.Error);
            CompleteCurrentCommand(OpcomResult.Fail($"OPCOM rejected input (got '?')"));
        }
        else
        {
            // Echo mismatch - could be line noise or buffered output
            // Try to continue by checking if this matches a later expected byte
            // For now, treat as non-fatal and wait for the correct echo
        }
    }

    private void TransitionAfterCommandSent()
    {
        if (_pendingReadComplete)
        {
            // The examine-cancelling space has been echoed. The real ND-120 sends
            // NOTHING after that echo - no '#' - and accepts the next command at once,
            // so start the next queued command now instead of waiting for a prompt
            // that never comes. (Waiting here is what made "Refresh all" read one
            // register per click.) A memory write reaches this point too, with its
            // command still current; complete it the normal way.
            _pendingReadComplete = false;
            if (_currentCommand != null)
            {
                CompleteOk();
            }
            else
            {
                SetState(OpcomProtocolState.Idle);
                TryStartNextCommand();
            }
            return;
        }

        if (_currentCommand == null) return;

        // If we just finished echoing the write value + CR/DEP
        if (_writePhase)
        {
            _writePhase = false;

            // An IOX write has one more step after the OPR deposit: the IOX itself. It
            // must NOT be sent yet. The deposit answers CR LF '#', and only the CR has
            // been echoed at this point - the LF and the prompt are still on their way.
            // Sending the next character into that gap loses it: measured on the machine
            // on 9 September 2026, the '4' of "401IO/" went out between the CR echo and
            // the LF, was DROPPED, and the command died on the watchdog. Wait for the
            // prompt, which WaitingPrompt below does.
            if (_ioxPhase == IoxWritePhase.DepositingOpr && _currentCommand != null)
            {
                SetState(OpcomProtocolState.WaitingPrompt);
                return;
            }

            if (_currentCommand != null &&
                _currentCommand.Type == OpcomCommandType.WriteMemory &&
                _cpuState != OpcomCpuState.Running)
            {
                // Memory CR in STOP mode: deposits AND advances to next address.
                // The real ND-120 answers CR LF '#' VALUE SPACE (no address). Skip that
                // advance data up to its SPACE, then send a space to exit examine mode.
                _memoryWriteExitPending = true;
                SetState(OpcomProtocolState.WaitingPrompt);
            }
            else
            {
                // Registers or RUN mode DEP: exits examine, returns #
                SetState(OpcomProtocolState.WaitingPrompt);
            }
            return;
        }

        switch (_currentCommand.Type)
        {
            case OpcomCommandType.ReadMemory:
            case OpcomCommandType.ReadRegister:
            case OpcomCommandType.ReadInternalRegister:
            case OpcomCommandType.IOXRead:
                SetState(OpcomProtocolState.ReadingValue);
                _responseLength = 0;
                break;

            case OpcomCommandType.WriteMemory:
            case OpcomCommandType.WriteRegister:
            case OpcomCommandType.WriteInternalRegister:
                // First read the current value, then write new value
                SetState(OpcomProtocolState.ReadingValue);
                _responseLength = 0;
                break;

            case OpcomCommandType.IOXWrite:
                // We just sent OPR/, now read current OPR value
                SetState(OpcomProtocolState.ReadingValue);
                _responseLength = 0;
                break;

            case OpcomCommandType.DumpMemory:
                SetState(OpcomProtocolState.DumpingMemory);
                ArmWatchdog();
                break;

            case OpcomCommandType.DumpRegisters:
            case OpcomCommandType.DumpInternalRegisters:
                SetState(OpcomProtocolState.DumpingRegisters);
                ArmWatchdog();
                break;

            case OpcomCommandType.Stop:
                SetCpuState(OpcomCpuState.Stopped);
                SetState(OpcomProtocolState.WaitingPrompt);
                break;

            case OpcomCommandType.MasterClear:
                SetState(OpcomProtocolState.WaitingMclComplete);
                break;

            case OpcomCommandType.Start:
                SetCpuState(OpcomCpuState.Running);
                // Start doesn't produce a prompt response
                CompleteCurrentCommand(OpcomResult.Ok());
                break;

            default:
                SetState(OpcomProtocolState.WaitingPrompt);
                break;
        }
    }

    // ==================== Value Reading ====================

    private void HandleReadingValue(byte b)
    {
        if (OctalHelper.IsOctalDigit(b))
        {
            if (_responseLength < _responseBuffer.Length)
            {
                _responseBuffer[_responseLength++] = b;
            }
        }
        else if (b == (byte)' ' || b == (byte)'\r' || b == (byte)'\n')
        {
            // Value complete
            ushort value = 0;
            if (_responseLength > 0)
            {
                OctalHelper.TryParseOctalBytes(_responseBuffer.AsSpan(0, _responseLength), out value, out _);
            }

            if (_currentCommand == null) return;

            // The IOX itself has just shown its value. That is the end of the write:
            // OPCOM is sitting in examine mode, so a space closes it.
            if (_ioxPhase == IoxWritePhase.RunningIox)
            {
                _ioxPhase = IoxWritePhase.None;
                var iox = _currentCommand;
                _currentCommand = null;
                _queue.Complete(OpcomResult.Ok(value));
                iox?.Completion.TrySetResult(OpcomResult.Ok(value));
                ReportOutcome(iox, OpcomResult.Ok(value));
                SendCancelSpace();
                return;
            }

            // Check if this is a read-then-write command
            if (_currentCommand.WriteValue.HasValue)
            {
                // We just read the current value, now write the new one
                string newVal = OctalHelper.ToOctalTrimmed(_currentCommand.WriteValue.Value);

                // STOP mode: value + CR (deposits and advances/exits examine)
                // RUN mode: value + DEP (safety mechanism per OPCOM spec)
                string suffix = (_cpuState == OpcomCpuState.Running) ? "DEP" : "\r";

                // An IOX write is THREE steps on the wire, not one. Measured on a real
                // ND-120/CX over COM11 on 9 September 2026:
                //
                //   OPR/000000       examine the operator register
                //   0 CR             deposit the word to be written - answers CR LF '#'
                //   401IO/000000     the ODD device address performs the output, taking
                //                    its data from OPR, then displays a value and a SPACE
                //                    exactly like any examine and stays in examine mode
                //
                // This code used to stop after reading OPR and report that as the result,
                // so the write never happened at all. The deposit below is step two; the
                // transition after its echo sends step three.
                if (_currentCommand.Type == OpcomCommandType.IOXWrite)
                {
                    _ioxPhase = IoxWritePhase.DepositingOpr;
                }

                _writePhase = true; // Mark that next echo completion is the write phase
                _commandBytes = System.Text.Encoding.ASCII.GetBytes(newVal + suffix);
                _commandIndex = 0;
                SetState(OpcomProtocolState.AwaitingEcho);
                SendNextByte();

                // Store the read value in registers/memory as appropriate
                StoreReadValue(value);
            }
            else
            {
                // Pure read - store the value, complete the command, then send
                // a space to cancel examine mode and return to '#' prompt.
                // (CR would advance to next address in memory examine mode)
                StoreReadValue(value);
                // Complete the command first (caller gets the result)
                var cmd = _currentCommand;
                _currentCommand = null;
                _queue.Complete(OpcomResult.Ok(value));
                cmd?.Completion.TrySetResult(OpcomResult.Ok(value));

                // A pure read finishes here rather than in CompleteCurrentCommand, so it
                // has to report its own outcome or the status bar would never hear that
                // the commonest command of all had succeeded.
                ReportOutcome(cmd, OpcomResult.Ok(value));

                // Send a space to cancel examine mode. The real machine only echoes it.
                SendCancelSpace();
            }
        }
        // Ignore other characters (could be formatting)
    }

    /// <summary>
    /// Sends the space that takes OPCOM out of examine mode and arranges for the
    /// command flow to continue as soon as that space is echoed. The real ND-120 sends
    /// no '#' after this space (measured 6 September 2026), so the echo is the signal.
    /// </summary>
    private void SendCancelSpace()
    {
        _pendingReadComplete = true;
        _commandBytes = new byte[] { (byte)' ' }; // Space cancels examine
        _commandIndex = 0;
        SetState(OpcomProtocolState.AwaitingEcho);
        SendNextByte();
    }

    private void StoreReadValue(ushort value)
    {
        if (_currentCommand == null) return;

        switch (_currentCommand.Type)
        {
            case OpcomCommandType.ReadMemory:
            case OpcomCommandType.WriteMemory:
                if (OctalHelper.TryParseOctal32(_currentCommand.CommandText.AsSpan(), out int memAddr))
                {
                    Memory.Set(memAddr, value);
                }
                break;

            case OpcomCommandType.ReadRegister:
            case OpcomCommandType.WriteRegister:
                // Find register index from name
                for (int i = 0; i < WorkingRegisterNames.Count; i++)
                {
                    if (string.Equals(WorkingRegisterNames.ByNumber[i], _currentCommand.CommandText, StringComparison.OrdinalIgnoreCase))
                    {
                        Registers.SetWorkingRegister(_currentCommand.Level, i, value);
                        break;
                    }
                }
                break;

            case OpcomCommandType.ReadInternalRegister:
            case OpcomCommandType.WriteInternalRegister:
                // Match by Ixx notation (e.g., "I0", "I12") or by name (e.g., "OPR")
                for (int i = 0; i < InternalRegisterDefs.Count; i++)
                {
                    string cmdText = _currentCommand.CommandText;
                    // Check Ixx match
                    if (string.Equals(InternalRegisterDefs.GetReadCommand(i), cmdText, StringComparison.OrdinalIgnoreCase))
                    {
                        Registers.SetInternalRegister(i, value);
                        break;
                    }
                    // Also check name match (for OPR)
                    if (string.Equals(InternalRegisterDefs.Registers[i].OpcomName, cmdText, StringComparison.OrdinalIgnoreCase))
                    {
                        Registers.SetInternalRegister(i, value);
                        break;
                    }
                }
                break;
        }
    }

    // ==================== Memory Dump ====================

    private void InitDumpMemory(OpcomCommand cmd)
    {
        if (OctalHelper.TryParseOctal32(cmd.CommandText.AsSpan(), out int startAddr))
        {
            _dumpCurrentAddress = startAddr;
        }
        _dumpExpectedEnd = cmd.EndAddress;
        _dumpCount = 0;

        int range = _dumpExpectedEnd - _dumpCurrentAddress + 1;
        if (range <= 0) range = 1;
        _dumpValues = new ushort[range];
        _dumpAddresses = new int[range];
        _responseLength = 0;
        _dumpAtLineStart = true;
        _dumpRunIsLabel = false;
    }

    /// <summary>
    /// Where an IOX write has got to. The command is three steps on the wire: examine
    /// OPR, deposit the word into it, then run the IOX at the odd device address.
    /// </summary>
    private enum IoxWritePhase
    {
        /// <summary>Not an IOX write, or it has finished.</summary>
        None,

        /// <summary>The word is being deposited into OPR.</summary>
        DepositingOpr,

        /// <summary>The IOX itself has been sent and is showing its value.</summary>
        RunningIox,
    }

    private IoxWritePhase _ioxPhase = IoxWritePhase.None;

    // Dump line scanner state.
    // _dumpAtLineStart: the next digit run begins a line, so it is that line's address
    // label rather than a value. Set by CR and LF, cleared by the first digit after them.
    // _dumpRunIsLabel: the run currently being collected started at a line start.
    private bool _dumpAtLineStart = true;
    private bool _dumpRunIsLabel;

    /// <summary>
    /// Feeds one byte of dump output through the shared line scanner and reports
    /// whether a VALUE has just been completed into the response buffer.
    /// Real dump output (measured on the ND-120/CX, 6 September 2026) is
    /// CR LF '#' CR, then lines of "AAAAAA /VVVVVV VVVVVV ... VVVVVV " separated by CR LF,
    /// with nothing at all after the final word.
    /// The address label at the head of every line must not be taken for a value, and
    /// position is what tells them apart: a label is the FIRST digit run on its line and
    /// a value never is. Deciding by position rather than by peeking at the byte after
    /// the run matters at the very end of a dump, where there is no next byte to peek at
    /// and a trailing "000020 /" would otherwise be counted as a value.
    /// </summary>
    private bool ScanDumpByte(byte b)
    {
        if (OctalHelper.IsOctalDigit(b))
        {
            if (_responseLength == 0)
            {
                // A new run starts here. Its role is fixed by where the line stands.
                _dumpRunIsLabel = _dumpAtLineStart;
                _dumpAtLineStart = false;
            }
            if (_responseLength < _responseBuffer.Length)
            {
                _responseBuffer[_responseLength++] = b;
            }
            return false;
        }

        if (b == (byte)' ')
        {
            if (_responseLength == 0) return false;
            if (_dumpRunIsLabel)
            {
                // "AAAAAA /" - the line's address label. Drop it.
                _responseLength = 0;
                return false;
            }
            return true; // a value, complete
        }

        if (b == (byte)'\r' || b == (byte)'\n')
        {
            _dumpAtLineStart = true;
            if (_responseLength == 0) return false;
            if (_dumpRunIsLabel)
            {
                _responseLength = 0;
                return false;
            }
            return true; // a value ended by the line break rather than by a space
        }

        // '/', '#' and anything else: formatting between tokens, ignore.
        return false;
    }

    private void HandleDumpingMemory(byte b)
    {
        if (b == (byte)'#' && _dumpCount > 0)
        {
            // A prompt AFTER data: the dump is over. The real machine never sends one
            // (its '#' comes before the data), but it costs nothing to accept it.
            FlushDumpValue();
            FinishMemoryDump();
            return;
        }

        if (ScanDumpByte(b))
        {
            FlushDumpValue();
            if (_dumpCount >= _dumpValues.Length)
            {
                // Every expected word is in. The real machine sends nothing more, so the
                // count is the only end marker there is.
                FinishMemoryDump();
            }
        }
    }

    private void FinishMemoryDump()
    {
        // Store all dumped values in cache
        for (int i = 0; i < _dumpCount; i++)
        {
            Memory.Set(_dumpAddresses[i], _dumpValues[i]);
        }
        // The values are all in, but OPCOM may still be printing. Wait for quiet.
        // (DumpSettleMs of zero is the pre-fix behaviour, kept so the hardware test can
        // be shown to catch the defect.)
        if (DumpSettleMs <= 0)
        {
            CompleteCurrentCommand(OpcomResult.OkDump(
                _dumpValues.AsSpan(0, _dumpCount).ToArray(),
                _dumpAddresses.AsSpan(0, _dumpCount).ToArray()));
            return;
        }
        BeginDrain(OpcomResult.OkDump(
            _dumpValues.AsSpan(0, _dumpCount).ToArray(),
            _dumpAddresses.AsSpan(0, _dumpCount).ToArray()));
    }

    private void FlushDumpValue()
    {
        if (_responseLength > 0 && _dumpCount < _dumpValues.Length)
        {
            if (OctalHelper.TryParseOctalBytes(_responseBuffer.AsSpan(0, _responseLength), out ushort val, out _))
            {
                _dumpAddresses[_dumpCount] = _dumpCurrentAddress;
                _dumpValues[_dumpCount] = val;
                _dumpCount++;
                _dumpCurrentAddress++;
                if (_currentCommand != null)
                {
                    ReportProgress(DescribeCommand(_currentCommand), _dumpCount, _dumpValues.Length);
                }
            }
        }
        _responseLength = 0;
    }

    // ==================== Register Dump ====================

    // How many values the register dump has to deliver, and how many are still due.
    // The real machine sends nothing after the last one, so counting is the end marker.
    private int _regDumpValuesExpected;
    private int _regDumpValuesLeft;

    private void InitRegisterDump(OpcomCommand cmd)
    {
        _regDumpCurrentLevel = cmd.Level;
        _regDumpEndLevel = cmd.EndAddress;
        _regDumpRegIndex = 0;
        _responseLength = 0;
        _dumpAtLineStart = true;
        _dumpRunIsLabel = false;
        int levels = _regDumpEndLevel - _regDumpCurrentLevel + 1;
        if (levels < 1) levels = 1;
        _regDumpValuesExpected = levels * WorkingRegisterNames.Count;
        _regDumpValuesLeft = _regDumpValuesExpected;
    }

    private void HandleDumpingRegisters(byte b)
    {
        if (b == (byte)'#' && _regDumpValuesLeft < _regDumpValuesExpected)
        {
            // A prompt after at least one value: end of dump (the real machine's '#'
            // comes BEFORE the data and is ignored here because nothing is in yet).
            FlushRegValue();
            FinishRegisterDump();
            return;
        }

        if (ScanDumpByte(b))
        {
            FlushRegValue();
            if (_regDumpValuesLeft <= 0)
            {
                // Counting is the only end marker: nothing follows the last value.
                FinishRegisterDump();
            }
        }
    }

    private void FinishRegisterDump()
    {
        // A register dump keeps printing after its last value - "0<1RD" ends with the
        // start of one more line - so the command cannot finish until OPCOM goes quiet.
        // Setting DumpSettleMs to zero finishes the moment the last value lands. That is
        // the pre-fix behaviour and it FAILS on real hardware; it exists so the hardware
        // test can be shown to catch the defect.
        if (DumpSettleMs <= 0) { CompleteCurrentCommand(OpcomResult.Ok()); return; }
        BeginDrain(OpcomResult.Ok());
    }

    private void FlushRegValue()
    {
        if (_responseLength == 0) return;

        if (OctalHelper.TryParseOctalBytes(_responseBuffer.AsSpan(0, _responseLength), out ushort val, out _))
        {
            _regDumpValuesLeft--;
            if (_currentCommand != null && _regDumpValuesExpected > 0)
            {
                ReportProgress(DescribeCommand(_currentCommand),
                    _regDumpValuesExpected - _regDumpValuesLeft, _regDumpValuesExpected);
            }
            if (_regDumpCurrentLevel == -1)
            {
                // Internal register dump. The real machine prints one value more than
                // InternalRegisterDefs knows; anything past the table is counted, not kept.
                if (_regDumpRegIndex < InternalRegisterDefs.Count)
                {
                    Registers.SetInternalRegister(_regDumpRegIndex, val);
                }
                _regDumpRegIndex++;
            }
            else
            {
                // Working register dump: STS, D, P, B, L, A, T, X per level
                if (_regDumpRegIndex < WorkingRegisterNames.Count)
                {
                    Registers.SetWorkingRegister(_regDumpCurrentLevel, _regDumpRegIndex, val);
                    _regDumpRegIndex++;
                    if (_regDumpRegIndex >= WorkingRegisterNames.Count)
                    {
                        _regDumpRegIndex = 0;
                        _regDumpCurrentLevel++;
                    }
                }
            }
        }
        _responseLength = 0;
    }

    // ==================== MCL Wait ====================

    private int _mclHashCount;

    private void HandleMclWait(byte b)
    {
        if (b == (byte)'#')
        {
            _mclHashCount++;
            if (_mclHashCount >= 2)
            {
                // "##" received - MCL complete
                SetCpuState(OpcomCpuState.Stopped);
                CompleteCurrentCommand(OpcomResult.Ok());
            }
        }
        else
        {
            _mclHashCount = 0;
        }
    }

    // ==================== Upload ====================

    private enum OpcomUploadSubState
    {
        AwaitingAddressEcho,   // Echoing addr/ for first word
        ReadingCurrentValue,   // Reading current value after examine
        AwaitingValueEcho,     // Echoing value\r deposit
        SkippingAdvanceData,   // Skipping \r\n ADDR VALUE response after CR deposit
        WaitingExitPrompt,     // Waiting for the echo of the final space (the real machine sends no '#')
    }

    private void HandleUploading(byte b)
    {
        switch (_uploadSubState)
        {
            case OpcomUploadSubState.AwaitingAddressEcho:
            case OpcomUploadSubState.AwaitingValueEcho:
                HandleEchoForUpload(b);
                break;

            case OpcomUploadSubState.ReadingCurrentValue:
                // After examine or advance: read octal digits, space terminates
                if (OctalHelper.IsOctalDigit(b))
                {
                    // Accumulate but we don't need the value
                }
                else if (b == (byte)' ')
                {
                    // Current value read, send new value + CR to deposit and advance
                    StartUploadDepositValue();
                }
                break;

            case OpcomUploadSubState.SkippingAdvanceData:
                // After the value CR deposit the real machine sends CR LF '#' VALUE SPACE
                // (that '#' is a line start, not a prompt). Skip everything up to the
                // space that ends the displayed value.
                if (b == (byte)' ')
                {
                    // Advance response complete — next word's address is already examined
                    _uploadIndex++;
                    _uploadProgressCallback?.Invoke(_uploadIndex, _uploadCount);

                    if (_uploadIndex >= _uploadCount)
                    {
                        // All words deposited — send space to exit examine mode
                        _commandBytes = new byte[] { (byte)' ' };
                        _commandIndex = 0;
                        _uploadSubState = OpcomUploadSubState.WaitingExitPrompt;
                        SendNextByte();
                    }
                    else
                    {
                        // Chain next deposit (already at next address)
                        StartUploadDepositValue();
                    }
                }
                break;

            case OpcomUploadSubState.WaitingExitPrompt:
                // The real ND-120 only echoes the examine-cancelling space; no '#' follows.
                if (b == (byte)' ' || b == (byte)'#')
                {
                    CompleteCurrentCommand(OpcomResult.Ok());
                }
                break;
        }
    }

    private void HandleEchoForUpload(byte b)
    {
        if (_commandIndex >= _commandBytes.Length)
        {
            // Command fully echoed — transition
            if (_uploadSubState == OpcomUploadSubState.AwaitingAddressEcho)
            {
                _uploadSubState = OpcomUploadSubState.ReadingCurrentValue;
            }
            else if (_uploadSubState == OpcomUploadSubState.AwaitingValueEcho)
            {
                // value\r echoed — now skip the advance response data
                _uploadSubState = OpcomUploadSubState.SkippingAdvanceData;
            }
            return;
        }

        byte expected = _commandBytes[_commandIndex];
        if (b == expected || b == (byte)'?')
        {
            _commandIndex++;
            if (_commandIndex < _commandBytes.Length)
            {
                SendNextByte();
            }
            else
            {
                if (_uploadSubState == OpcomUploadSubState.AwaitingAddressEcho)
                    _uploadSubState = OpcomUploadSubState.ReadingCurrentValue;
                else if (_uploadSubState == OpcomUploadSubState.AwaitingValueEcho)
                    _uploadSubState = OpcomUploadSubState.SkippingAdvanceData;
            }
        }
    }

    private void StartUploadNextWord()
    {
        // Only used for the FIRST word — sends addr/ to start examine mode
        int addr = _uploadStartAddress + _uploadIndex;
        string addrStr = OctalHelper.ToOctalTrimmed(addr);

        _commandBytes = System.Text.Encoding.ASCII.GetBytes(addrStr + "/");
        _commandIndex = 0;
        _uploadSubState = OpcomUploadSubState.AwaitingAddressEcho;
        SetState(OpcomProtocolState.Uploading);
        SendNextByte();
    }

    private void StartUploadDepositValue()
    {
        // Send value + CR: deposits the value and advances to next address.
        // STOP mode deposit: value followed by CR.
        // The simulator will deposit, advance, and show next address + value + space.
        if (_uploadWords == null || _uploadIndex >= _uploadCount) return;

        ushort value = _uploadWords[_uploadIndex];
        string valStr = OctalHelper.ToOctalTrimmed(value);

        _commandBytes = System.Text.Encoding.ASCII.GetBytes(valStr + "\r");
        _commandIndex = 0;
        _uploadSubState = OpcomUploadSubState.AwaitingValueEcho;
        SendNextByte();

        // Store in memory cache
        Memory.Set(_uploadStartAddress + _uploadIndex, value);
    }

    // ==================== Command Completion ====================

    /// <summary>
    /// Finishes a command that succeeded, carrying the value a deposit wrote so the
    /// caller can report it. A write used to come back with zero, which the MCP surface
    /// printed as "mem = 000000" - indistinguishable from having written a zero, and
    /// useless as confirmation of what actually went into the machine.
    /// </summary>
    private void CompleteOk()
    {
        ushort value = _currentCommand?.WriteValue ?? 0;
        CompleteCurrentCommand(OpcomResult.Ok(value));
    }

    private void CompleteCurrentCommand(OpcomResult result)
    {
        DisarmWatchdog();
        _settle.Change(Timeout.Infinite, Timeout.Infinite);
        _ioxPhase = IoxWritePhase.None;   // a half-finished IOX write must not leak into the next command
        var cmd = _currentCommand;
        _currentCommand = null;

        // Complete via queue if the command went through the queue, otherwise complete directly.
        // Upload commands bypass the queue and are set as _currentCommand directly.
        if (_queue.IsBusy)
            _queue.Complete(result);
        else
            cmd?.Completion.TrySetResult(result);

        SetState(OpcomProtocolState.Idle);

        // Say how it went BEFORE starting anything else, so the outcome of this command
        // is what the window shows unless another command immediately overwrites it.
        ReportOutcome(cmd, result);

        // Try to start next command
        TryStartNextCommand();
    }

    // ==================== Public Command Methods ====================

    private Task<OpcomResult> EnqueueCommand(OpcomCommand cmd, CancellationToken ct)
    {
        if (ct.CanBeCanceled)
        {
            ct.Register(() => cmd.Completion.TrySetResult(OpcomResult.Fail("Cancelled")));
        }
        _queue.Enqueue(cmd);
        return cmd.Completion.Task;
    }

    public Task<OpcomResult> ReadMemoryAsync(int address, CancellationToken ct = default)
    {
        string addrStr = OctalHelper.ToOctalTrimmed(address);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.ReadMemory, addrStr), ct);
    }

    public Task<OpcomResult> WriteMemoryAsync(int address, ushort value, CancellationToken ct = default)
    {
        string addrStr = OctalHelper.ToOctalTrimmed(address);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.WriteMemory, addrStr, writeValue: value), ct);
    }

    public Task<OpcomResult> DumpMemoryAsync(int startAddress, int endAddress, CancellationToken ct = default)
    {
        string addrStr = OctalHelper.ToOctalTrimmed(startAddress);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.DumpMemory, addrStr, endAddress: endAddress), ct);
    }

    public Task<OpcomResult> ReadRegisterAsync(int level, string registerName, CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.ReadRegister, registerName, level: level), ct);
    }

    public Task<OpcomResult> WriteRegisterAsync(int level, string registerName, ushort value, CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.WriteRegister, registerName, writeValue: value, level: level), ct);
    }

    public Task<OpcomResult> ReadInternalRegisterAsync(string registerName, CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.ReadInternalRegister, registerName), ct);
    }

    public Task<OpcomResult> WriteInternalRegisterAsync(string registerName, ushort value, CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.WriteInternalRegister, registerName, writeValue: value), ct);
    }

    public Task<OpcomResult> DumpRegistersAsync(int startLevel, int endLevel, CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.DumpRegisters, "", level: startLevel, endAddress: endLevel), ct);
    }

    public Task<OpcomResult> DumpInternalRegistersAsync(CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.DumpInternalRegisters), ct);
    }

    public Task<OpcomResult> IOXReadAsync(int deviceAddress, CancellationToken ct = default)
    {
        string devStr = OctalHelper.ToOctalTrimmed(deviceAddress);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.IOXRead, devStr), ct);
    }

    public Task<OpcomResult> IOXWriteAsync(int deviceAddress, ushort oprValue, CancellationToken ct = default)
    {
        string devStr = OctalHelper.ToOctalTrimmed(deviceAddress);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.IOXWrite, devStr, writeValue: oprValue), ct);
    }

    public Task<OpcomResult> StopCpuAsync(CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.Stop), ct);
    }

    public Task<OpcomResult> MasterClearAsync(CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.MasterClear), ct);
    }

    public Task<OpcomResult> StartAsync(int address, CancellationToken ct = default)
    {
        string addrStr = OctalHelper.ToOctalTrimmed(address);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.Start, addrStr), ct);
    }

    public Task<OpcomResult> SingleStepAsync(int count = 1, CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.SingleStep, stepCount: count), ct);
    }

    public Task<OpcomResult> SetBreakpointAsync(int address, CancellationToken ct = default)
    {
        string addrStr = OctalHelper.ToOctalTrimmed(address);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.Breakpoint, addrStr), ct);
    }

    public Task<OpcomResult> EscapeAsync(CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.Escape), ct);
    }


    /// <summary>
    /// Runs the microprogrammed memory test on one 64K bank. STOP mode only.
    ///
    /// WRITES MEMORY. Measured on a real ND-120/CX on 9 September 2026: bank 0 left
    /// address 000000 holding 177777 where it had held 114631, and the rest of the first
    /// thirty-two words untouched. It answers with a single prompt and no report, so
    /// whether the bank passed cannot be read from the reply - only from the memory.
    /// </summary>
    /// <param name="bank">
    /// The 64K bank number, in octal on the wire.
    /// </param>
    /// <param name="ct">
    /// Cancels the wait for the reply.
    /// </param>
    public Task<OpcomResult> MemoryTestAsync(int bank, CancellationToken ct = default)
    {
        string bankStr = OctalHelper.ToOctalTrimmed(bank);
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.MemoryTest, bankStr), ct);
    }

    public Task<OpcomResult> BootLoadAsync(string opcomCommand, CancellationToken ct = default)
    {
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.BootLoad, opcomCommand), ct);
    }

    public Task<OpcomResult> SetExamineModeAsync(int? pageTable, CancellationToken ct = default)
    {
        string ptStr = pageTable.HasValue ? OctalHelper.ToOctalTrimmed(pageTable.Value) : "";
        return EnqueueCommand(new OpcomCommand(OpcomCommandType.SetExamineMode, ptStr), ct);
    }

    public Task<OpcomResult> UploadWordsAsync(int startAddress, ushort[] words, int count,
        Action<int, int>? progressCallback, CancellationToken ct = default)
    {
        _uploadWords = words;
        _uploadStartAddress = startAddress;
        _uploadCount = count;
        _uploadIndex = 0;
        _uploadProgressCallback = progressCallback;

        var cmd = new OpcomCommand(OpcomCommandType.UploadFile);
        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                _currentCommand = null;
                SetState(OpcomProtocolState.Idle);
                cmd.Completion.TrySetResult(OpcomResult.Fail("Upload cancelled"));
            });
        }
        // Upload bypasses the queue — it manages its own multi-step state machine.
        // Set as current command directly so CompleteCurrentCommand can finish it.
        _currentCommand = cmd;

        // Start the first word
        StartUploadNextWord();

        return cmd.Completion.Task;
    }

    /// <summary>
    /// Uploads a BPUN file using the ND-100 binary loader (300$ command).
    /// Sends 300$ via OPCOM echo protocol, then streams raw BPUN bytes directly.
    /// Much faster than deposit loop (~3x: 2 bytes/word vs 6+ ASCII chars/word).
    /// Caller must handle serial reconfiguration to 8N1 before calling this.
    /// </summary>
    public async Task<OpcomResult> UploadBpunBinaryAsync(byte[] bpunFileBytes,
        Action<int, int>? progressCallback, CancellationToken ct = default)
    {
        if (_sendDelegate == null)
            return OpcomResult.Fail("Not connected");

        // Step 1: Send "300$" character by character, waiting for echo of each.
        // The binary loader activates on '$' — no '#' prompt returned.
        // After all 4 chars echoed, the loader is active and accepting data.
        byte[] activateCmd = System.Text.Encoding.ASCII.GetBytes("300$");
        for (int ci = 0; ci < activateCmd.Length; ci++)
        {
            ct.ThrowIfCancellationRequested();
            byte[] oneByte = { activateCmd[ci] };
            await _sendDelegate(oneByte, ct).ConfigureAwait(false);
            Log(OpcomLogDirection.Tx, oneByte);
            // Wait briefly for echo — the simulator echoes each char immediately
            await Task.Delay(20, ct);
        }

        // Step 2: Suppress protocol processing and start streaming immediately.
        // The binary loader is already active and waiting for BPUN data.
        _bpunTransferActive = true;

        // Step 3: Stream the raw BPUN file bytes — no echo, no protocol wrapping
        int totalBytes = bpunFileBytes.Length;
        int chunkSize = 256;
        int bytesSent = 0;

        try
        {
            while (bytesSent < totalBytes)
            {
                ct.ThrowIfCancellationRequested();

                int remaining = totalBytes - bytesSent;
                int toSend = remaining < chunkSize ? remaining : chunkSize;

                await _sendDelegate(new ReadOnlyMemory<byte>(bpunFileBytes, bytesSent, toSend), ct).ConfigureAwait(false);

                bytesSent += toSend;
                progressCallback?.Invoke(bytesSent, totalBytes);
            }

            // Step 4: Wait for loader to finish processing and return to OPCOM
            await Task.Delay(500, ct);
        }
        finally
        {
            _bpunTransferActive = false;
        }

        return OpcomResult.Ok();
    }
}
