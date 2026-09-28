using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Protocols.Net;

/// <summary>
/// Simulates an ND-100 OPCOM microprogram for testing the OPCOM Debug Window.
/// Zero-allocation hot paths using ArrayPool, Span, fixed buffers, and Channel.
///
/// CPU: 64KW memory, 16 levels x 8 working registers, 14 internal registers.
/// Protocol: character-echo, '#' prompt, octal only, uppercase only.
/// </summary>
public sealed class OpcomSimulatorConnection : IConnection, ISerialConfigurable
{
    // ==================== CPU State ====================
    private readonly ushort[] _memory = new ushort[65536];
    private readonly ushort[] _workingRegs = new ushort[16 * 8]; // [level * 8 + reg]
    private readonly ushort[] _internalRegs = new ushort[16];      // TRA read registers
    private readonly ushort[] _internalWriteRegs = new ushort[16]; // TRR write registers (separate hardware)
    private bool _cpuRunning;
    private int _currentExamineAddress;
    private int _breakpointAddress = -1;

    // ==================== Parser State (single-threaded access via Channel) ====================
    private OpcomSimState _state;
    private readonly byte[] _cmdBuf = new byte[64]; // Fixed command accumulation buffer
    private int _cmdLen;

    // Examine target: encoded as int.
    // Positive = memory address, negative = -(1 + level*8+reg) for working reg,
    // -1000-idx for internal reg, -2000-dev for IOX
    private int _examineTarget;
    private bool _inExamineMode;

    // ==================== Threading ====================
    private readonly Channel<byte> _inputChannel = Channel.CreateUnbounded<byte>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private Task? _processingTask;
    private CancellationTokenSource? _cts;

    // ==================== Response output (pooled) ====================
    private readonly byte[] _responseBuf = new byte[1024]; // Pre-allocated response buffer
    private int _responseLen;

    // ==================== IConnection ====================
    private ConnectionStatus _status;
    private bool _disposed;

    public ConnectionStatus Status => _status;
    public string ConnectionType => "OPCOM Simulator";
    public string Description => "ND-100 OPCOM Simulator";

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;

    // Required by IConnection, but this simulator is purely in-process and has no
    // transport that can fail, so it never raises the event. Suppressed rather than
    // removed because the interface mandates it.
#pragma warning disable CS0067
    public event Action<Exception>? ErrorOccurred;
#pragma warning restore CS0067

    // Binary loader state — follows the microcode ETLO1 protocol exactly:
    // Phase 0: ASCII preamble (SEEK/SIKI) — reads octal + CR/! terminators, 7-bit masked
    // Phase 1: Binary header — 2 bytes load address, 2 bytes word count (via BIN subroutine)
    // Phase 2: Binary data — exactly wordCount words, 2 bytes each big-endian (STLP loop)
    // Phase 3: Binary checksum — 2 bytes, XOR with accumulated sum
    // Phase 4: ASCII action — octal digits + CR (via ASS8)
    private bool _binaryLoaderActive;
    private int _blPhase;           // Current loader phase (0-4)
    private int _blOctalAccum;      // Octal number accumulator (like OCTNR in microcode)
    private int _blByteHigh;        // High byte of current 16-bit word (-1 = waiting)
    private int _blLoadAddress;     // X register — memory write pointer
    private int _blWordCount;       // T register — words remaining
    private ushort _blChecksum;     // L register — additive checksum accumulator

    public void ReconfigureSerial(int dataBits, int parity, int stopBits)
    {
        // No-op for simulator — no physical UART to reconfigure
    }

    public OpcomSimulatorConnection()
    {
        InitializeCpu();
    }

    private void InitializeCpu()
    {
        for (int i = 0; i < 256; i++)
            _memory[i] = (ushort)i;

        _memory[512] = 0x5A01;
        _memory[513] = 0xFFFF;
        _memory[514] = 0x5A02;
        _memory[515] = 0x4200;

        // Level 0 registers: S,D,P,B,L,A,T,X
        _workingRegs[0] = 0xFFFF; // S
        _workingRegs[2] = 0x0200; // P = 1000 octal
        _workingRegs[5] = 0x1234; // A

        _internalRegs[1] = 0xFFFF;  // STS
        _internalRegs[10] = 0x2360; // ALD = 021540 octal = 9056 dec

        _cpuRunning = false;
        _state = OpcomSimState.Ready;
        _cmdLen = 0;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(ConnectionStatus.Connecting);
        _state = OpcomSimState.Ready;
        _cmdLen = 0;
        _cts = new CancellationTokenSource();
        SetStatus(ConnectionStatus.Connected);

        // Start the single-threaded processing loop
        _processingTask = Task.Run(() => ProcessingLoopAsync(_cts.Token), _cts.Token);

        // Send initial prompt after a brief delay
        Task.Run(async () =>
        {
            await Task.Delay(100, cancellationToken);
            Emit((byte)'\r');
            Emit((byte)'\n');
            Emit((byte)'#');
            FlushResponse();
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_status != ConnectionStatus.Connected)
            throw new InvalidOperationException("Not connected");

        // Write bytes to channel - zero-copy, non-blocking
        // Binary loader needs full 8-bit data; OPCOM masks to 7 bits in ProcessInputByte
        var span = data.Span;
        var writer = _inputChannel.Writer;
        bool raw = _binaryLoaderActive;
        for (int i = 0; i < span.Length; i++)
        {
            writer.TryWrite(raw ? span[i] : (byte)(span[i] & 0x7F));
        }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        SetStatus(ConnectionStatus.Disconnecting);
        _cts?.Cancel();
        _state = OpcomSimState.Ready;
        SetStatus(ConnectionStatus.Disconnected);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            if (_status == ConnectionStatus.Connected)
            {
                _status = ConnectionStatus.Disconnected;
                StatusChanged?.Invoke(ConnectionStatus.Disconnected);
            }
        }
    }

    private void SetStatus(ConnectionStatus s)
    {
        if (_status != s) { _status = s; StatusChanged?.Invoke(s); }
    }

    // ==================== Single-threaded processing loop ====================

    private async Task ProcessingLoopAsync(CancellationToken ct)
    {
        var reader = _inputChannel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out byte b))
                {
                    ProcessInputByte(b);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ==================== Response output (zero-alloc hot path) ====================

    private void Emit(byte b)
    {
        if (_responseLen < _responseBuf.Length)
            _responseBuf[_responseLen++] = b;
    }

    private void EmitOctal6(ushort value)
    {
        // Write 6 octal digits directly to response buffer
        if (_responseLen + 6 > _responseBuf.Length) return;
        for (int i = 5; i >= 0; i--)
        {
            _responseBuf[_responseLen + i] = (byte)('0' + (value & 7));
            value >>= 3;
        }
        _responseLen += 6;
    }

    private void EmitString(ReadOnlySpan<byte> data)
    {
        int toCopy = Math.Min(data.Length, _responseBuf.Length - _responseLen);
        data.Slice(0, toCopy).CopyTo(_responseBuf.AsSpan(_responseLen));
        _responseLen += toCopy;
    }

    private void EmitAsciiString(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (_responseLen < _responseBuf.Length)
                _responseBuf[_responseLen++] = (byte)s[i];
        }
    }

    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;
    private static ReadOnlySpan<byte> CrLfHash => "\r\n#"u8;
    private static ReadOnlySpan<byte> QuestionMark => "?"u8;

    private void EmitPrompt() { EmitString(CrLfHash); }
    /// <summary>
    /// Rejects the input the way the real machine does. Measured on an ND-120/CX over COM11
    /// on 6 September 2026: a lowercase letter or any unrecognised character is ECHOED
    /// and then answered with a single question mark, and NOTHING else. There is no
    /// CR LF and no prompt afterwards, even though the input has been cancelled and
    /// OPCOM is ready for the next command straight away.
    /// </summary>
    private void EmitError() { EmitString(QuestionMark); }

    private void FlushResponse()
    {
        if (_responseLen == 0 || _status != ConnectionStatus.Connected) return;
        // Copy to a pooled buffer for the event
        byte[] buf = ArrayPool<byte>.Shared.Rent(_responseLen);
        Buffer.BlockCopy(_responseBuf, 0, buf, 0, _responseLen);
        int len = _responseLen;
        _responseLen = 0;
        DataReceived?.Invoke(new ReadOnlyMemory<byte>(buf, 0, len));
        ArrayPool<byte>.Shared.Return(buf);
    }

    // ==================== Input Processing ====================

    private enum OpcomSimState : byte
    {
        Ready,
        ExamineResult,
        WaitingMcl,
    }

    private void ProcessInputByte(byte b)
    {
        // Binary loader intercepts all input when active
        if (_binaryLoaderActive)
        {
            ProcessBinaryLoaderByte(b);
            return;
        }

        if (b == 0x1B) // ESC
        {
            Emit(b);
            FlushResponse();
            if (_cpuRunning) { _state = OpcomSimState.Ready; _inExamineMode = false; }
            return;
        }

        switch (_state)
        {
            case OpcomSimState.Ready:
                ProcessReady(b);
                break;
            case OpcomSimState.ExamineResult:
                ProcessExamineResult(b);
                break;
            case OpcomSimState.WaitingMcl:
                break; // Ignore input during MCL
        }
    }

    // ==================== Ready State ====================

    private void ProcessReady(byte b)
    {
        // Lowercase → reject
        if (b >= (byte)'a' && b <= (byte)'z')
        {
            Emit(b);
            EmitError();
            FlushResponse();
            _cmdLen = 0;
            return;
        }

        Emit(b); // Echo

        if (b == (byte)' ')
        {
            // Echo only, like the real machine: a space just clears the typed input.
            _cmdLen = 0;
            FlushResponse();
            return;
        }

        if (b == (byte)'/')
        {
            HandleExamine();
            _cmdLen = 0;
            FlushResponse();
            return;
        }

        if (b == 0x0D)
        {
            HandleCrCommand();
            _cmdLen = 0;
            FlushResponse();
            return;
        }

        // Accumulate valid chars
        byte upper = ToUpper(b);
        if (!IsValidChar(upper))
        {
            _cmdLen = 0;
            EmitError();
            FlushResponse();
            return;
        }

        if (_cmdLen < _cmdBuf.Length)
            _cmdBuf[_cmdLen++] = upper;

        // Check immediate commands
        if (MatchCmd("STOP"u8))
        {
            _cmdLen = 0;
            _cpuRunning = false;
            EmitPrompt();
            FlushResponse();
            return;
        }

        // Master clear is NOT immediate here - it is "MACL" plus CR, handled in
        // HandleCrCommand. The real machine rejects a bare "MCL" with '?'.

        if (_cmdLen >= 1)
        {
            byte last = _cmdBuf[_cmdLen - 1];
            if (last == (byte)'!')
            {
                HandleStart();
                _cmdLen = 0;
                FlushResponse();
                return;
            }
            if (last == (byte)'Z')
            {
                HandleSingleStep();
                _cmdLen = 0;
                FlushResponse();
                return;
            }
            if (last == (byte)'.')
            {
                HandleBreakpoint();
                _cmdLen = 0;
                FlushResponse();
                return;
            }
            if (last == (byte)'&' || last == (byte)'$')
            {
                HandleLoad();
                _cmdLen = 0;
                return;
            }
            if (_cmdLen == 1 && last == (byte)'*')
            {
                // Print the current location. Measured on the real ND-120/CX: the six octal
                // digits are followed by a SPACE and nothing else - no prompt.
                EmitOctal6((ushort)_currentExamineAddress);
                Emit((byte)' ');
                _cmdLen = 0;
                FlushResponse();
                return;
            }
            // TODO: '"' (execute instruction from console) — not implemented in simulator
            if (_cmdLen == 1 && last == (byte)'"')
            {
                EmitError(); // Acknowledge but reject
                _cmdLen = 0;
                FlushResponse();
                return;
            }
            // The microprogrammed memory test, "bb#" for one 64K bank. Measured on a real
            // ND-120/CX on 9 September 2026: echoed, then a single prompt and NO report,
            // finishing in well under a second. The simulator used to reject it with '?',
            // which is not what the machine does.
            //
            // The machine also WRITES memory - address 000000 came back holding 177777
            // where it had held 114631, with the next thirty-one words untouched. That
            // side effect is deliberately NOT reproduced here: one sampled address is not
            // enough to know what the test really does to a bank, and inventing the rest
            // would be a guess wearing the clothes of a measurement.
            if (last == (byte)'#')
            {
                _cmdLen = 0;
                _cpuRunning = false;
                EmitPrompt();
                FlushResponse();
                return;
            }
        }

        FlushResponse(); // Flush echo
    }

    // ==================== Examine Result State ====================

    private void ProcessExamineResult(byte b)
    {
        if (b >= (byte)'a' && b <= (byte)'z')
        {
            Emit(b);
            EmitError();
            FlushResponse();
            _cmdLen = 0;
            _inExamineMode = false;
            _state = OpcomSimState.Ready;
            return;
        }

        Emit(b);

        if (b == 0x0D)
        {
            // Deposit if value typed, then advance
            if (_cmdLen > 0 && TryParseOctalBuf(out ushort val))
                DepositValue(val);
            _cmdLen = 0;
            AdvanceExamine();
            FlushResponse();
            return;
        }

        if (b == (byte)' ')
        {
            // The real ND-120 only echoes the space that leaves examine mode. No '#'
            // follows; the next command is accepted straight away. (Measured over
            // COM11 on 6 September 2026. Sending a prompt here hid a stall in
            // OpcomProtocol that the real machine exposed.)
            _cmdLen = 0;
            _inExamineMode = false;
            _state = OpcomSimState.Ready;
            FlushResponse();
            return;
        }

        if (b == (byte)'/')
        {
            // New examine command from within examine mode
            _inExamineMode = false;
            HandleExamine();
            _cmdLen = 0;
            FlushResponse();
            return;
        }

        byte upper = ToUpper(b);
        if (_cmdLen < _cmdBuf.Length)
            _cmdBuf[_cmdLen++] = upper;

        // Check DEP suffix
        if (_cmdLen >= 3 &&
            _cmdBuf[_cmdLen - 3] == (byte)'D' &&
            _cmdBuf[_cmdLen - 2] == (byte)'E' &&
            _cmdBuf[_cmdLen - 1] == (byte)'P')
        {
            int valLen = _cmdLen - 3;
            if (valLen > 0)
            {
                int savedLen = _cmdLen;
                _cmdLen = valLen;
                if (TryParseOctalBuf(out ushort depVal))
                    DepositValue(depVal);
                _cmdLen = savedLen;
            }
            _cmdLen = 0;
            _inExamineMode = false;
            _state = OpcomSimState.Ready;
            EmitPrompt();
            FlushResponse();
            return;
        }

        // Check immediate commands in examine mode
        if (MatchCmd("STOP"u8))
        {
            _cmdLen = 0;
            _inExamineMode = false;
            _cpuRunning = false;
            _state = OpcomSimState.Ready;
            EmitPrompt();
            FlushResponse();
            return;
        }
        // Master clear is "MACL" plus CR and is handled where CR is handled, not here.
        if (_cmdLen >= 1)
        {
            byte last = _cmdBuf[_cmdLen - 1];
            if (last == (byte)'!' || last == (byte)'Z' || last == (byte)'.' ||
                last == (byte)'&' || last == (byte)'$')
            {
                _inExamineMode = false;
                _state = OpcomSimState.Ready;
                if (last == (byte)'!') { HandleStart(); }
                else if (last == (byte)'Z') { HandleSingleStep(); }
                else if (last == (byte)'.') { HandleBreakpoint(); }
                else { HandleLoad(); }
                _cmdLen = 0;
                FlushResponse();
                return;
            }
        }

        FlushResponse(); // Flush echo
    }

    // ==================== Command Handlers ====================

    private void HandleExamine()
    {
        // Parse _cmdBuf[0.._cmdLen] to determine what to examine
        ReadOnlySpan<byte> cmd = _cmdBuf.AsSpan(0, _cmdLen);

        // Extract optional level prefix
        int level = 0;
        int nameStart = 0;
        while (nameStart < cmd.Length && cmd[nameStart] >= (byte)'0' && cmd[nameStart] <= (byte)'7')
            nameStart++;

        if (nameStart > 0 && nameStart < cmd.Length)
        {
            level = ParseOctalSpan(cmd.Slice(0, nameStart));
            cmd = cmd.Slice(nameStart);
        }

        // OPR pseudo-register (special case per OPCOM spec)
        if (cmd.Length == 3 && cmd[0] == (byte)'O' && cmd[1] == (byte)'P' && cmd[2] == (byte)'R')
        {
            ExamineInternal(2);
            return;
        }

        // Internal register Ixx notation
        if (cmd.Length >= 2 && cmd[0] == (byte)'I' && cmd[1] >= (byte)'0' && cmd[1] <= (byte)'7')
        {
            int iNum = ParseOctalSpan(cmd.Slice(1));
            if (iNum >= 0 && iNum < 16)
            {
                ExamineInternal(iNum);
                return;
            }
        }

        // Working register by name or Rn
        int wReg = MatchWorkingRegister(cmd);
        if (wReg >= 0)
        {
            ushort value = _workingRegs[level * 8 + wReg];
            _examineTarget = -(1 + level * 8 + wReg);
            _inExamineMode = true;
            _state = OpcomSimState.ExamineResult;
            EmitOctal6(value);
            Emit((byte)' ');
            return;
        }

        // IOX: ddddIO
        if (cmd.Length >= 3 && cmd[cmd.Length - 2] == (byte)'I' && cmd[cmd.Length - 1] == (byte)'O')
        {
            int devAddr = ParseOctalSpan(cmd.Slice(0, cmd.Length - 2));
            if (devAddr >= 0)
            {
                HandleIOX((ushort)devAddr);
                return;
            }
        }

        // Display commands: ACT, BUS, U. Measured on a real ND-120/CX on 6 September 2026:
        // "ACT/" is echoed and answers NOTHING at all. The front panel changes and the
        // console stays silent, so there is no prompt to emit here.
        if ((cmd.Length == 3 && cmd[0] == (byte)'A' && cmd[1] == (byte)'C' && cmd[2] == (byte)'T') ||
            (cmd.Length >= 3 && cmd[cmd.Length - 3] == (byte)'B' && cmd[cmd.Length - 2] == (byte)'U' && cmd[cmd.Length - 1] == (byte)'S') ||
            (cmd.Length == 1 && cmd[0] == (byte)'U'))
        {
            _state = OpcomSimState.Ready;
            return;
        }

        // Memory address (all octal digits)
        int addr = ParseOctalSpan(_cmdBuf.AsSpan(0, _cmdLen)); // Use original buffer with level prefix
        if (addr >= 0 && addr <= 0xFFFF)
        {
            _currentExamineAddress = addr;
            ushort value = _memory[addr];
            _examineTarget = addr;
            _inExamineMode = true;
            _state = OpcomSimState.ExamineResult;
            EmitOctal6(value);
            Emit((byte)' ');
            return;
        }

        // Unrecognized
        EmitError();
        _state = OpcomSimState.Ready;
    }

    private void ExamineInternal(int iNum)
    {
        ushort value = _internalRegs[iNum];
        _examineTarget = -1000 - iNum;
        _inExamineMode = true;
        _state = OpcomSimState.ExamineResult;
        EmitOctal6(value);
        Emit((byte)' ');
    }

    private void HandleCrCommand()
    {
        ReadOnlySpan<byte> cmd = _cmdBuf.AsSpan(0, _cmdLen);

        // Memory dump: start<end
        int ltIdx = -1;
        for (int i = 0; i < cmd.Length; i++)
            if (cmd[i] == (byte)'<') { ltIdx = i; break; }

        if (ltIdx > 0)
        {
            ReadOnlySpan<byte> after = cmd.Slice(ltIdx + 1);

            // Register dump: xx<yyRD
            if (after.Length >= 2 && after[after.Length - 2] == (byte)'R' && after[after.Length - 1] == (byte)'D')
            {
                int startLvl = ParseOctalSpan(cmd.Slice(0, ltIdx));
                int endLvl = ParseOctalSpan(after.Slice(0, after.Length - 2));
                HandleRegisterDump(startLvl < 0 ? 0 : startLvl, endLvl < 0 ? 0 : endLvl);
                return;
            }

            // Memory dump
            int fromAddr = ParseOctalSpan(cmd.Slice(0, ltIdx));
            int toAddr = ParseOctalSpan(after);
            if (fromAddr >= 0 && toAddr >= 0)
            {
                HandleMemoryDump((ushort)fromAddr, (ushort)toAddr);
                return;
            }
        }

        // Handle "<RD" (no start prefix)
        if (ltIdx == 0)
        {
            ReadOnlySpan<byte> after = cmd.Slice(1);
            if (after.Length >= 2 && after[after.Length - 2] == (byte)'R' && after[after.Length - 1] == (byte)'D')
            {
                int endLvl = after.Length > 2 ? ParseOctalSpan(after.Slice(0, after.Length - 2)) : 0;
                HandleRegisterDump(0, endLvl < 0 ? 0 : endLvl);
                return;
            }
        }

        // IRD
        if (_cmdLen == 3 && cmd[0] == (byte)'I' && cmd[1] == (byte)'R' && cmd[2] == (byte)'D')
        {
            HandleInternalRegisterDump();
            return;
        }

        // MASTER CLEAR: "MACL" followed by CR. Measured on a real ND-120/CX over COM11 on
        // 6 September 2026, where it answered CR LF '#' '#'. It is NOT "MCL" and it is
        // NOT immediate on the last letter: typing "MCL" gets echoed and then rejected
        // with '?', and "MACL" on its own sits there until a carriage return arrives.
        if (_cmdLen == 4 && cmd[0] == (byte)'M' && cmd[1] == (byte)'A' && cmd[2] == (byte)'C' && cmd[3] == (byte)'L')
        {
            HandleMcl();
            return;
        }

        // Examine mode: E or ptE
        if (_cmdLen >= 1 && cmd[_cmdLen - 1] == (byte)'E')
        {
            EmitPrompt();
            _state = OpcomSimState.Ready;
            return;
        }

        // Just CR in examine mode → advance
        if (_cmdLen == 0 && _inExamineMode)
        {
            AdvanceExamine();
            return;
        }

        // A bare CR at the command level just draws a new prompt line.
        if (_cmdLen == 0)
        {
            EmitPrompt();
            return;
        }

        // Anything else typed and then ended with CR was not a command. Measured on the
        // real ND-120/CX on 6 September 2026 with the wrong master-clear spelling: "MCL" CR
        // answered CR LF '#' '?'. So the prompt line comes first and the question mark
        // follows it - the opposite order from a single unrecognised character, which is
        // answered with a bare '?' and no prompt at all.
        EmitPrompt();
        Emit((byte)'?');
    }

    private void HandleStart()
    {
        int addrLen = _cmdLen - 1; // Exclude '!'
        if (addrLen > 0)
        {
            int addr = ParseOctalSpan(_cmdBuf.AsSpan(0, addrLen));
            if (addr >= 0) _workingRegs[2] = (ushort)addr; // Set P on level 0
        }
        _cpuRunning = true;
        _inExamineMode = false;
    }

    private void HandleSingleStep()
    {
        int countLen = _cmdLen - 1;
        int count = 1;
        if (countLen > 0)
        {
            int c = ParseOctalSpan(_cmdBuf.AsSpan(0, countLen));
            if (c > 0) count = c;
        }
        ushort p = _workingRegs[2]; // P on level 0
        p = (ushort)(p + count);
        _workingRegs[2] = p;
        if (p < _memory.Length) _workingRegs[5] = _memory[p]; // A = memory at P
        EmitPrompt();
    }

    private void HandleBreakpoint()
    {
        int addrLen = _cmdLen - 1;
        if (addrLen > 0)
        {
            int addr = ParseOctalSpan(_cmdBuf.AsSpan(0, addrLen));
            if (addr >= 0) _breakpointAddress = addr;
        }
        // The '.' was already echoed by ProcessReady — just emit the prompt
        EmitPrompt();
    }

    private void HandleLoad()
    {
        // Parse octal value from command buffer (excluding the trailing & or $)
        int addrLen = _cmdLen - 1;
        int bootValue;
        if (addrLen > 0)
        {
            bootValue = ParseOctalSpan(_cmdBuf.AsSpan(0, addrLen));
            if (bootValue < 0) bootValue = 0;
        }
        else
        {
            // Bare & or $ — use ALD register default (index 10 = I12 = ALD)
            bootValue = _internalRegs[10];
        }

        // Device 300 (0xC0 = 192 decimal) = console binary loader
        int deviceAddr = bootValue & 0x1FFF;
        if (deviceAddr == 0xC0) // 300 octal = 192 decimal
        {
            StartBinaryLoader();
            return;
        }

        var info = OpcomAldDecoder.DecodeBootCommand(bootValue);

        EmitString(CrLf);
        if (info.PerformsLoad)
        {
            EmitAsciiString("Booting from ");
            EmitAsciiString(info.DeviceName);
            EmitAsciiString(" - ");
            EmitAsciiString(info.LoadFormatName);
            EmitAsciiString(info.AutoRun ? ", load and run" : ", load only");
        }
        else
        {
            EmitAsciiString("No boot device");
        }
        EmitString(CrLf);
        FlushResponse();

        // Capture boot state for the background task
        bool autoRun = info.AutoRun;

        // Simulate load delay on a background task
        Task.Run(async () =>
        {
            await Task.Delay(500);
            if (autoRun)
            {
                _workingRegs[2] = 0; // P = 0 (MASS loader always starts at address 0)
                _cpuRunning = true;
            }
            else
            {
                _cpuRunning = false;
            }
            // Boot returns single # prompt (unlike MCL which returns ##)
            Emit((byte)'#');
            FlushResponse();
        });
    }

    private void HandleMcl()
    {
        _state = OpcomSimState.WaitingMcl;
        _cpuRunning = false;
        _inExamineMode = false;
        FlushResponse();
        Task.Run(async () =>
        {
            await Task.Delay(300);
            _state = OpcomSimState.Ready;
            EmitString(CrLf);
            Emit((byte)'#');
            Emit((byte)'#');
            FlushResponse();
        });
    }

    // ==================== Binary Loader (300$ / ETLO1 protocol) ====================
    //
    // Follows the ND-110 microcode ETLO1 binary loader exactly:
    // Phase 0 (SEEK/SIKI): ASCII preamble — octal digits masked to 7 bits.
    //   CR stores accumulated value as start address (P register).
    //   '!' transitions to binary phase.
    // Phase 1: BIN reads 2 bytes → load address (X). BIN reads 2 bytes → word count (T).
    // Phase 2 (STLP): Reads exactly T words via BIN (2 bytes each, big-endian).
    //   Each word written to memory at X, X++, T--, checksum accumulated in L.
    // Phase 3: BIN reads 2-byte checksum, XOR with L. Non-zero = error ('?').
    // Phase 4 (ASS8): ASCII octal action code + CR.
    //   Action 0 = COMM,START (run). Action != 0 = stay in OPCOM.

    private const int BL_PHASE_PREAMBLE = 0;
    private const int BL_PHASE_HEADER = 1;
    private const int BL_PHASE_DATA = 2;
    private const int BL_PHASE_CHECKSUM = 3;
    private const int BL_PHASE_ACTION = 4;
    private int _blHeaderBytesRead; // 0-3: load addr high, low, count high, low

    private void StartBinaryLoader()
    {
        _binaryLoaderActive = true;
        _blPhase = BL_PHASE_PREAMBLE;
        _blOctalAccum = 0;
        _blByteHigh = -1;
        _blLoadAddress = 0;
        _blWordCount = 0;
        _blChecksum = 0;
        _blHeaderBytesRead = 0;
        _cpuRunning = false;
        _inExamineMode = false;
        EmitString(CrLf);
        EmitAsciiString("Binary loader active on device 300");
        EmitString(CrLf);
        FlushResponse();
    }

    private void ProcessBinaryLoaderByte(byte b)
    {
        switch (_blPhase)
        {
            case BL_PHASE_PREAMBLE:
            {
                // SEEK/SIKI via ASS8: ASCII phase, 7-bit masked (STS = 0x7F)
                byte c = (byte)(b & 0x7F);
                if (c >= '0' && c <= '7')
                {
                    _blOctalAccum = (_blOctalAccum << 3) | (c - '0');
                }
                else if (c == '\r')
                {
                    // CR: store as start address in P register, reset accumulator
                    _workingRegs[2] = (ushort)_blOctalAccum;
                    _blOctalAccum = 0;
                }
                else if (c == '!')
                {
                    // '!' (EXFOU): switch to binary phase, STS = 0xFF
                    _blPhase = BL_PHASE_HEADER;
                    _blHeaderBytesRead = 0;
                    _blByteHigh = -1;
                }
                else
                {
                    // Non-octal, non-terminator: ASS8 resets accumulator (SIKI loops back)
                    _blOctalAccum = 0;
                }
                break;
            }

            case BL_PHASE_HEADER:
            {
                // BIN reads 4 bytes: 2 for load address (X), 2 for word count (T)
                // Each BIN call reads high byte first, then low byte → 16-bit word
                if (_blByteHigh < 0)
                {
                    _blByteHigh = b;
                }
                else
                {
                    ushort word = (ushort)((_blByteHigh << 8) | b);
                    _blByteHigh = -1;
                    _blHeaderBytesRead++;

                    if (_blHeaderBytesRead == 1)
                    {
                        // First word: load address → X register
                        _blLoadAddress = word;
                    }
                    else
                    {
                        // Second word: word count → T register
                        _blWordCount = word;
                        _blChecksum = 0;

                        if (_blWordCount == 0)
                        {
                            // Zero words: skip straight to checksum
                            _blPhase = BL_PHASE_CHECKSUM;
                        }
                        else
                        {
                            _blPhase = BL_PHASE_DATA;
                        }
                    }
                }
                break;
            }

            case BL_PHASE_DATA:
            {
                // STLP: BIN reads 2 bytes → word, COMM,WRRQ,PT writes to memory[X], X++, T--
                if (_blByteHigh < 0)
                {
                    _blByteHigh = b;
                }
                else
                {
                    ushort word = (ushort)((_blByteHigh << 8) | b);
                    _blByteHigh = -1;

                    // Write to memory at load address
                    if (_blLoadAddress >= 0 && _blLoadAddress < _memory.Length)
                        _memory[_blLoadAddress] = word;

                    _blLoadAddress++;                                    // X++
                    _blChecksum = (ushort)(_blChecksum + word);          // L += Z
                    _blWordCount--;                                      // T--

                    if (_blWordCount == 0)
                    {
                        // STLP done — fall through to checksum
                        _blPhase = BL_PHASE_CHECKSUM;
                    }
                }
                break;
            }

            case BL_PHASE_CHECKSUM:
            {
                // BIN reads 2-byte checksum, XOR with accumulated L
                if (_blByteHigh < 0)
                {
                    _blByteHigh = b;
                }
                else
                {
                    ushort fileChecksum = (ushort)((_blByteHigh << 8) | b);
                    _blByteHigh = -1;

                    ushort xorResult = (ushort)(fileChecksum ^ _blChecksum);
                    if (xorResult != 0)
                    {
                        // Checksum error → ILLEG → RONLY → SPACE → CONT
                        // Aborts transfer, returns to OPCOM
                        EmitAsciiString("?");
                        _binaryLoaderActive = false;
                        _state = OpcomSimState.Ready;
                        EmitPrompt();
                        FlushResponse();
                        return;
                    }

                    // Checksum OK — move to action phase (ASCII)
                    _blPhase = BL_PHASE_ACTION;
                    _blOctalAccum = 0;
                }
                break;
            }

            case BL_PHASE_ACTION:
            {
                // ASS8: reads ASCII octal action code + CR
                byte c = (byte)(b & 0x7F);
                if (c >= '0' && c <= '7')
                {
                    _blOctalAccum = (_blOctalAccum << 3) | (c - '0');
                }
                else if (c == '\r')
                {
                    if (_blOctalAccum == 0)
                    {
                        // Action 0: COMM,START → ESCAP — start execution at P
                        _cpuRunning = true;
                        _binaryLoaderActive = false;
                        _state = OpcomSimState.Ready;
                        EmitString(CrLf);
                        Emit((byte)'#');
                        FlushResponse();
                    }
                    else
                    {
                        // Action != 0: loop back to SEEK for next block
                        // (multi-block BPUN: intermediate blocks use action=1)
                        _blPhase = BL_PHASE_PREAMBLE;
                        _blOctalAccum = 0;
                    }
                }
                break;
            }
        }
    }

    private void HandleIOX(ushort deviceAddress)
    {
        if ((deviceAddress & 1) == 0)
        {
            ushort value = (ushort)(deviceAddress ^ 0x5555);
            _examineTarget = -2000 - deviceAddress;
            _inExamineMode = true;
            _state = OpcomSimState.ExamineResult;
            EmitOctal6(value);
            Emit((byte)' ');
        }
        else
        {
            _state = OpcomSimState.Ready;
            EmitPrompt();
        }
    }

    /// <summary>
    /// Writes a dump the way the real ND-120/CX does (measured over COM11, 6 September
    /// 2026): CR LF '#' CR first, then one line per eight words, each line being the
    /// six-digit octal index of its first word, a space, a slash, and the words each
    /// followed by a space; lines are separated by CR LF and NOTHING follows the last
    /// word - no prompt. The '#' comes before the data, not after it.
    /// </summary>
    private void EmitDumpLines(ushort[] source, int first, int count)
    {
        EmitString(CrLfHash);
        Emit(0x0D);
        for (int i = 0; i < count; i++)
        {
            if ((i & 7) == 0)
            {
                if (i > 0) EmitString(CrLf);
                EmitOctal6((ushort)(first + i));
                EmitString(" /"u8);
            }
            EmitOctal6(source[first + i]);
            Emit((byte)' ');
            // Flush periodically to avoid buffer overflow on large dumps
            if (_responseLen > 900) FlushResponse();
        }
    }

    private void HandleMemoryDump(ushort fromAddr, ushort toAddr)
    {
        if (toAddr < fromAddr) toAddr = fromAddr;
        EmitDumpLines(_memory, fromAddr, toAddr - fromAddr + 1);
        _state = OpcomSimState.Ready;
    }

    private void HandleRegisterDump(int startLevel, int endLevel)
    {
        if (endLevel > 15) endLevel = 15;
        if (startLevel > endLevel) startLevel = endLevel;
        if (startLevel < 0) startLevel = 0;

        int levels = endLevel - startLevel + 1;
        EmitDumpLines(_workingRegs, startLevel * 8, levels * 8);
        // The real RD dump then starts one more line - the index after the last
        // register, a space and a slash - and stops there.
        EmitString(CrLf);
        EmitOctal6((ushort)((endLevel + 1) * 8));
        EmitString(" /"u8);
        _state = OpcomSimState.Ready;
    }

    private void HandleInternalRegisterDump()
    {
        if (_cpuRunning) { EmitError(); _state = OpcomSimState.Ready; return; }
        // The real IRD prints fifteen values (measured 6 September 2026), one more than
        // the register table names; the array holds sixteen so the fifteenth is there.
        EmitDumpLines(_internalRegs, 0, 15);
        _state = OpcomSimState.Ready;
    }

    // ==================== Deposit / Advance ====================

    private void DepositValue(ushort value)
    {
        int t = _examineTarget;
        if (t >= 0)
        {
            // Memory
            if (t < _memory.Length) _memory[t] = value;
        }
        else if (t >= -1000)
        {
            // Working register: -(1 + level*8+reg)
            int idx = -(t + 1);
            if (idx >= 0 && idx < _workingRegs.Length) _workingRegs[idx] = value;
        }
        else if (t >= -2000)
        {
            // Internal register: -1000-iNum
            // Deposit writes to TRR (write) register, NOT the TRA (read) register.
            // E.g. slot 2: TRA reads OPR, TRR writes LMP — different hardware.
            int idx = -(t + 1000);
            if (idx >= 0 && idx < 16) _internalWriteRegs[idx] = value;
        }
    }

    private void AdvanceExamine()
    {
        if (_examineTarget >= 0)
        {
            // Memory: advance to next address
            _currentExamineAddress = (_examineTarget + 1) & 0xFFFF;
            _examineTarget = _currentExamineAddress;
            ushort value = _memory[_currentExamineAddress];
            // The real ND-120 prints CR LF '#' then the NEXT address's value and a
            // space, and stays in examine mode. No address is printed. (Measured over
            // COM11 on 6 September 2026: "0/114631 " CR gave CR LF "#031463 ".)
            EmitString(CrLfHash);
            EmitOctal6(value);
            Emit((byte)' ');
            _state = OpcomSimState.ExamineResult;
        }
        else
        {
            _inExamineMode = false;
            _state = OpcomSimState.Ready;
            EmitPrompt();
        }
    }

    // ==================== Helpers (zero-alloc) ====================

    private bool TryParseOctalBuf(out ushort result)
    {
        result = 0;
        int val = 0;
        for (int i = 0; i < _cmdLen; i++)
        {
            byte c = _cmdBuf[i];
            if (c < (byte)'0' || c > (byte)'7') return false;
            val = (val << 3) | (c - '0');
        }
        result = (ushort)(val & 0xFFFF);
        return _cmdLen > 0;
    }

    private static int ParseOctalSpan(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return -1;
        int val = 0;
        for (int i = 0; i < data.Length; i++)
        {
            byte c = data[i];
            if (c < (byte)'0' || c > (byte)'7') return -1;
            val = (val << 3) | (c - '0');
        }
        return val;
    }

    private bool MatchCmd(ReadOnlySpan<byte> expected)
    {
        if (_cmdLen != expected.Length) return false;
        return _cmdBuf.AsSpan(0, _cmdLen).SequenceEqual(expected);
    }

    private static int MatchWorkingRegister(ReadOnlySpan<byte> name)
    {
        if (name.Length == 1)
        {
            return name[0] switch
            {
                (byte)'S' => 0,
                (byte)'D' => 1,
                (byte)'P' => 2,
                (byte)'B' => 3,
                (byte)'L' => 4,
                (byte)'A' => 5,
                (byte)'T' => 6,
                (byte)'X' => 7,
                _ => -1
            };
        }
        if (name.Length == 2 && name[0] == (byte)'R' && name[1] >= (byte)'0' && name[1] <= (byte)'7')
        {
            return name[1] - '0';
        }
        return -1;
    }

    private static byte ToUpper(byte b)
    {
        if (b >= (byte)'a' && b <= (byte)'z') return (byte)(b - 32);
        return b;
    }

    private static bool IsValidChar(byte c)
    {
        if (c >= (byte)'0' && c <= (byte)'7') return true;
        if (c >= (byte)'A' && c <= (byte)'Z') return true;
        return c == (byte)'<' || c == (byte)'!' || c == (byte)'.' ||
               c == (byte)'*' || c == (byte)'&' || c == (byte)'$' ||
               c == (byte)'#' || c == (byte)'"';
    }
}
