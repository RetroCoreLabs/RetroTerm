using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.Net;
using Xunit;

namespace RetroTerm.Tests.Opcom;

/// <summary>
/// Drives a REAL OPCOM over a serial port. Everything here was measured against an
/// ND-120/CX; the ND-100 family shares the microprogram, so it should hold across it.
/// The replay tests next door prove the parser against recorded bytes; only these prove
/// the whole path against a machine.
///
/// They are OFF unless a port is named, because the port is a shared physical resource and
/// the running RetroTerm usually holds it. To run them:
///
///   $env:RETROTERM_OPCOM_PORT = "COM11"
///   dotnet test tests\RetroTerm.Tests\RetroTerm.Tests.csproj --filter "FullyQualifiedName~OpcomAgainstRealHardwareTests"
///
/// The machine must be sitting at the OPCOM prompt with the CPU stopped, wired 9600 7E1
/// (override with RETROTERM_OPCOM_BAUD if a machine is strapped differently). Close
/// RetroTerm first, or it will be holding the port.
///
/// NOTHING here hardcodes a value read from a particular machine, because those change
/// with what the machine is doing. Each test instead reads the SAME facts by two
/// independent OPCOM routes and requires them to agree: a block dump against single
/// examines. A parser that mis-frames a dump cannot make the two agree by accident, and
/// the check stays valid on any machine in any state. The one thing written is a word put
/// back exactly as it was found.
///
/// Every failure message carries the bytes that actually crossed the wire. A rendered
/// screen is not evidence - it has already been through cursor movement and overwriting -
/// and the first attempt at diagnosing these tests from a screen went nowhere.
/// </summary>
[Trait("Category", "Hardware")]
[Collection("OpcomHardware")]
public class OpcomAgainstRealHardwareTests : IDisposable
{
    private const string PortVariable = "RETROTERM_OPCOM_PORT";
    private const string BaudVariable = "RETROTERM_OPCOM_BAUD";

    private readonly SerialConnection? _connection;
    private readonly OpcomProtocol? _protocol;

    // Every byte sent and received, in order, for the failure messages and for the
    // ordering checks. Kept as direction-tagged bytes rather than text so a test can ask
    // "what did the machine send between our slash and our next character".
    private readonly System.Collections.Generic.List<(bool Tx, byte Value)> _events = new();
    private readonly object _wireLock = new();

    /// <summary>
    /// The port to drive, or null when the caller has not opted in. Every test returns
    /// immediately in that case, so a normal suite run neither touches hardware nor fails.
    /// </summary>
    private static string? PortName
    {
        get
        {
            string? name = Environment.GetEnvironmentVariable(PortVariable);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }

    public OpcomAgainstRealHardwareTests()
    {
        string? port = PortName;
        if (port == null) return;

        int baud = 9600;
        string? baudText = Environment.GetEnvironmentVariable(BaudVariable);
        if (!string.IsNullOrWhiteSpace(baudText)) int.TryParse(baudText, out baud);

        // 7 data bits, 1 stop bit, even parity - parityValue 2 is Even.
        _connection = new SerialConnection(port, baud, dataBits: 7, stopBitsValue: 1, parityValue: 2);
        _protocol = new OpcomProtocol();
        _protocol.LogEntry += Record;
        _connection.DataReceived += data => _protocol!.ProcessIncomingData(data.Span);
        _connection.ConnectAsync().GetAwaiter().GetResult();
        _protocol.Activate((data, ct) => _connection!.SendAsync(data, ct));
    }

    public void Dispose()
    {
        _protocol?.Deactivate();
        _connection?.DisconnectAsync().GetAwaiter().GetResult();
        _connection?.Dispose();
    }

    private void Record(OpcomLogEntry entry)
    {
        lock (_wireLock)
        {
            bool tx = entry.Direction == OpcomLogDirection.Tx;
            for (int i = 0; i < entry.Data.Length; i++)
            {
                // 7E1: the parity bit is not part of the character.
                _events.Add((tx, (byte)(entry.Data[i] & 0x7F)));
            }
        }
    }

    private static void Describe(StringBuilder sb, byte b)
    {
        if (b == 0x0D) { sb.Append("CR"); return; }
        if (b == 0x0A) { sb.Append("LF"); return; }
        if (b == 0x20) { sb.Append("SP"); return; }
        if (b >= 0x21 && b <= 0x7E) { sb.Append((char)b); return; }
        sb.Append('[').Append(b.ToString("X2")).Append(']');
    }

    /// <summary>
    /// The tail of the recorded exchange as readable text, for a failure message.
    /// Bytes we sent are prefixed with a greater-than sign, bytes the machine sent with
    /// a less-than sign.
    /// </summary>
    private string Wire()
    {
        lock (_wireLock)
        {
            var sb = new StringBuilder();
            int from = _events.Count > 260 ? _events.Count - 260 : 0;
            if (from > 0) sb.Append("...");
            for (int i = from; i < _events.Count; i++)
            {
                sb.Append(_events[i].Tx ? " >" : " <");
                Describe(sb, _events[i].Value);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Clears the recording so a test can reason about one exchange on its own.
    /// </summary>
    private void ClearWire()
    {
        lock (_wireLock) _events.Clear();
    }

    /// <summary>
    /// A snapshot of the recording, so a test can walk it without it changing underneath.
    /// </summary>
    private (bool Tx, byte Value)[] Events()
    {
        lock (_wireLock) return _events.ToArray();
    }

    private string Because(string what, OpcomResult r) => what + ": " + r.ErrorMessage + " | wire:" + Wire();

    /// <summary>
    /// Wakes the machine and leaves it at a clean prompt: a space cancels any half-typed
    /// command or examine mode left over from an earlier session, and CR draws a prompt.
    /// </summary>
    private async Task SettleAsync()
    {
        await _connection!.SendAsync(new byte[] { (byte)' ' });
        await Task.Delay(200);
        await _connection.SendAsync(new byte[] { 0x0D });
        await Task.Delay(400);
    }

    private static string Octal(ushort v) => OctalHelper.ToOctal6(v);

    [Fact]
    public async Task EightRegisterReadsInARowAllComplete()
    {
        if (_protocol == null) return;
        await SettleAsync();

        // This is what the debug window's Refresh button does: eight reads, one awaited
        // after the other. It used to return after the FIRST one and leave the rest stuck
        // in the queue, so the button appeared to read a single register per click.
        for (int i = 0; i < WorkingRegisterNames.Count; i++)
        {
            string name = WorkingRegisterNames.ByNumber[i];
            var result = await _protocol.ReadRegisterAsync(0, name, TestContext.Current.CancellationToken);
            Assert.True(result.Success, Because("read " + i + " of the refresh sequence, register " + name, result));
        }
        // A read hands back its value as soon as the value has arrived, while the space
        // that leaves examine mode is still on its way out. So the protocol is briefly
        // AwaitingEcho after the last read - that is by design, not a stall. What must be
        // true is that it gets back to Idle by itself shortly afterwards.
        for (int waited = 0; waited < 2000 && _protocol.State != OpcomProtocolState.Idle; waited += 50)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.True(OpcomProtocolState.Idle == _protocol.State,
            "two seconds after the eighth read the protocol is still " + _protocol.State
            + " instead of Idle - the line was left mid-command | wire:" + Wire());
    }

    [Fact]
    public async Task ARegisterDumpAgreesWithReadingTheSameRegistersOneByOne()
    {
        if (_protocol == null) return;
        await SettleAsync();

        // Route one: the block dump "0<1RD".
        var dump = await _protocol.DumpRegistersAsync(0, 1, TestContext.Current.CancellationToken);
        Assert.True(dump.Success, Because("the working-register dump 0<1RD failed", dump));
        ushort[] fromDump = new ushort[WorkingRegisterNames.Count * 2];
        for (int level = 0; level < 2; level++)
            for (int i = 0; i < WorkingRegisterNames.Count; i++)
                fromDump[level * WorkingRegisterNames.Count + i] = _protocol.Registers.Levels[level].GetByIndex(i);

        // Route two: sixteen single examines of the same registers.
        for (int level = 0; level < 2; level++)
        {
            for (int i = 0; i < WorkingRegisterNames.Count; i++)
            {
                string name = WorkingRegisterNames.ByNumber[i];
                var one = await _protocol.ReadRegisterAsync(level, name, TestContext.Current.CancellationToken);
                Assert.True(one.Success, Because("examine of level " + level + " register " + name + " failed", one));
                ushort expected = fromDump[level * WorkingRegisterNames.Count + i];
                Assert.True(expected == one.Value,
                    "level " + level + " register " + name + ": the dump said " + Octal(expected)
                    + " but examining it alone said " + Octal(one.Value)
                    + " - the dump line format is being mis-parsed");
            }
        }
    }

    [Fact]
    public async Task AMemoryDumpAgreesWithReadingTheSameWordsOneByOne()
    {
        if (_protocol == null) return;
        await SettleAsync();

        // A range that spans three dump LINES, so the "AAAAAA /" label at the head of
        // each line has to be recognised and skipped twice, and the last line is short.
        const int first = 0;
        const int last = 16; // 20 octal

        var dump = await _protocol.DumpMemoryAsync(first, last, TestContext.Current.CancellationToken);
        Assert.True(dump.Success, Because("the memory dump failed", dump));
        Assert.NotNull(dump.DumpValues);
        ushort[] block = dump.DumpValues!;
        Assert.True(block.Length == last - first + 1,
            "the dump returned " + block.Length + " words for " + (last - first + 1)
            + " addresses - a line label was counted as a value, or the end was missed | wire:" + Wire());

        for (int addr = first; addr <= last; addr++)
        {
            var one = await _protocol.ReadMemoryAsync(addr, TestContext.Current.CancellationToken);
            Assert.True(one.Success, Because("examine of address " + Octal((ushort)addr) + " failed", one));
            Assert.True(block[addr - first] == one.Value,
                "address " + Octal((ushort)addr) + ": the dump said " + Octal(block[addr - first])
                + " but examining it alone said " + Octal(one.Value));
        }
    }

    [Fact]
    public async Task AnInternalRegisterDumpAgreesWithReadingThemOneByOne()
    {
        if (_protocol == null) return;
        await SettleAsync();

        var dump = await _protocol.DumpInternalRegistersAsync(TestContext.Current.CancellationToken);
        Assert.True(dump.Success, Because("IRD failed", dump));
        ushort[] fromDump = new ushort[InternalRegisterDefs.Count];
        for (int i = 0; i < InternalRegisterDefs.Count; i++) fromDump[i] = _protocol.Registers.Internal[i];

        for (int i = 0; i < InternalRegisterDefs.Count; i++)
        {
            string name = InternalRegisterDefs.Registers[i].OpcomName;
            // IIC, PES and PEA are cleared by being read, and PANS and ACTL follow the
            // panel, so only the registers that hold still can be compared.
            if (name == "IIC" || name == "PES" || name == "PEA" || name == "PANS" || name == "ACTL") continue;

            var one = await _protocol.ReadInternalRegisterAsync(InternalRegisterDefs.GetReadCommand(i), TestContext.Current.CancellationToken);
            Assert.True(one.Success, Because("examine of internal register " + name + " failed", one));
            Assert.True(fromDump[i] == one.Value,
                "internal register " + name + ": IRD said " + Octal(fromDump[i])
                + " but examining it alone said " + Octal(one.Value)
                + " - IRD's value count or line format is being mis-parsed");
        }
    }

    [Fact]
    public async Task WritingAWordBackAsItWasLeavesItUnchangedAndTheLineUsable()
    {
        if (_protocol == null) return;
        await SettleAsync();

        // Deposit is exercised without changing the machine: the word is read, written
        // back with the value it already had, and read again. What is being tested is
        // that the deposit's advance line is consumed and examine mode is left, which the
        // following command proves by working at all.
        const int address = 0;
        var before = await _protocol.ReadMemoryAsync(address, TestContext.Current.CancellationToken);
        Assert.True(before.Success, Because("could not read the word before writing it", before));

        var write = await _protocol.WriteMemoryAsync(address, before.Value, TestContext.Current.CancellationToken);
        Assert.True(write.Success, Because("the deposit failed", write));

        var after = await _protocol.ReadMemoryAsync(address, TestContext.Current.CancellationToken);
        Assert.True(after.Success, Because(
            "the read AFTER the deposit failed - OPCOM was probably left in examine mode, so the next command was typed into it",
            after));
        Assert.True(before.Value == after.Value,
            "address 0 changed from " + Octal(before.Value) + " to " + Octal(after.Value)
            + " when it was written back with its own value");
    }

    /// <summary>
    /// Master clear. Ronny cleared this one to run freely, 6 September 2026: "MCL you can
    /// run as much as you want". START is the opposite and is never sent from here - it
    /// sets the machine running and he then has to reset it by hand.
    ///
    /// The command is MACL and it needs a carriage return. "MCL", which this repo's own
    /// command reference gave and which the code sent until today, is rejected by the
    /// machine, so master clear could never have worked against real hardware.
    /// </summary>
    [Fact]
    public async Task MasterClearCompletesAndLeavesTheLineUsable()
    {
        if (_protocol == null) return;
        await SettleAsync();
        ClearWire();

        var mcl = await _protocol.MasterClearAsync(TestContext.Current.CancellationToken);
        Assert.True(mcl.Success, Because("master clear failed", mcl));

        // The two hashes are the machine saying it is done. Anything else and the command
        // would have timed out above, so what is left to prove is that the line still
        // works: a plain examine has to answer normally straight afterwards.
        var read = await _protocol.ReadRegisterAsync(0, "X", TestContext.Current.CancellationToken);
        Assert.True(read.Success, Because("the examine after a master clear failed", read));
    }

    /// <summary>
    /// An IOX read against the paper-tape reader's status. An EVEN device address is an
    /// input, which the reference says is "displayed on the console but not stored
    /// anywhere", so nothing on the machine changes. 400 is the paper-tape reader, which
    /// has nothing loaded in it.
    /// </summary>
    [Fact]
    public async Task AnIoxReadComesBackWithADeviceWord()
    {
        if (_protocol == null) return;
        await SettleAsync();

        var iox = await _protocol.IOXReadAsync(0x100, TestContext.Current.CancellationToken);   // 400 octal
        Assert.True(iox.Success, Because("IOX read from device 400 failed", iox));

        // The line must still work afterwards: the read leaves examine mode behind it.
        var after = await _protocol.ReadRegisterAsync(0, "X", TestContext.Current.CancellationToken);
        Assert.True(after.Success, Because("the examine after an IOX read failed", after));
    }

    /// <summary>
    /// An IOX write, which is three steps on the wire: examine OPR, deposit the word,
    /// then run the ODD device address, which takes its data from OPR.
    ///
    /// The word written is ZERO, and 401 is the paper-tape reader's control register.
    /// Zero is the most harmless word an output can carry: on an ND controller it means
    /// no operation with no interrupts enabled.
    /// </summary>
    [Fact]
    public async Task AnIoxWriteSetsOprAndRunsTheOddAddress()
    {
        if (_protocol == null) return;
        await SettleAsync();

        var iox = await _protocol.IOXWriteAsync(0x101, 0, TestContext.Current.CancellationToken);   // 401 octal, write zero
        Assert.True(iox.Success, Because("IOX write to device 401 failed", iox));

        var after = await _protocol.ReadRegisterAsync(0, "X", TestContext.Current.CancellationToken);
        Assert.True(after.Success, Because("the examine after an IOX write failed", after));
    }

    /// <summary>
    /// A command ending in a slash says "a change may be coming": OPCOM answers with the
    /// current contents and then a SPACE, and only after that space may the replacement
    /// value be typed. This walks the recorded bytes of a real deposit and proves the
    /// order held - that between our slash and the first character of the new value, the
    /// machine sent its value AND the space that closes it, and we typed nothing early.
    ///
    /// Getting this wrong does not fail loudly. Characters typed before OPCOM is ready
    /// are dropped by the machine, exactly as they were after a dump, so the symptom is a
    /// command that quietly does nothing rather than an error.
    /// </summary>
    [Fact]
    public async Task NothingIsTypedBetweenTheSlashAndTheSpaceThatClosesTheExamine()
    {
        if (_protocol == null) return;
        await SettleAsync();
        ClearWire();

        const int address = 0;
        var before = await _protocol.ReadMemoryAsync(address, TestContext.Current.CancellationToken);
        Assert.True(before.Success, Because("could not read the word before writing it", before));
        ClearWire();

        var write = await _protocol.WriteMemoryAsync(address, before.Value, TestContext.Current.CancellationToken);
        Assert.True(write.Success, Because("the deposit failed", write));

        var events = Events();

        // Find the slash WE sent - the one that opens the examine.
        int slash = -1;
        for (int i = 0; i < events.Length; i++)
        {
            if (events[i].Tx && events[i].Value == (byte)'/') { slash = i; break; }
        }
        Assert.True(slash >= 0, "no slash was sent at all during the deposit | wire:" + Wire());

        // Walk forward to the first thing we send AFTER that slash. That is the first
        // character of the replacement value.
        int firstTyped = -1;
        for (int i = slash + 1; i < events.Length; i++)
        {
            if (events[i].Tx) { firstTyped = i; break; }
        }
        Assert.True(firstTyped >= 0,
            "after the slash nothing was ever typed, so the deposit never happened | wire:" + Wire());

        // Everything the machine sent in between must be: the slash echo, the current
        // value, and a closing space - with the SPACE last.
        var between = new StringBuilder();
        byte lastFromMachine = 0;
        int received = 0;
        for (int i = slash + 1; i < firstTyped; i++)
        {
            Describe(between, events[i].Value);
            lastFromMachine = events[i].Value;
            received++;
        }

        Assert.True(received > 0,
            "the replacement value was typed with NOTHING received in between - the machine had not"
            + " even echoed the slash, so the characters were sent while OPCOM was not listening | wire:" + Wire());

        Assert.True(lastFromMachine == (byte)' ',
            "the last thing the machine sent before the new value was typed was '" + between
            + "', not a space - OPCOM had not finished presenting the current contents,"
            + " and anything typed that early is dropped | wire:" + Wire());

        // And the machine really did present the current value: six octal digits sitting
        // right before that space.
        int digits = 0;
        for (int i = firstTyped - 2; i > slash && digits < 8; i--)
        {
            byte b = events[i].Value;
            if (events[i].Tx || b < (byte)'0' || b > (byte)'7') break;
            digits++;
        }
        Assert.True(digits == 6,
            "the machine sent " + digits + " octal digits before the closing space, not the six of a"
            + " full word - the examine reply is not the shape this code expects | wire:" + Wire());
    }

    [Fact]
    public async Task ARegisterIsWrittenBackAsItWasAndTheLineStaysUsable()
    {
        if (_protocol == null) return;
        await SettleAsync();

        // Register deposit answers CR LF '#' and leaves examine mode, unlike a memory
        // deposit. The X register is used because nothing on a stopped CPU moves it.
        var before = await _protocol.ReadRegisterAsync(0, "X", TestContext.Current.CancellationToken);
        Assert.True(before.Success, Because("could not read X", before));

        var write = await _protocol.WriteRegisterAsync(0, "X", before.Value, TestContext.Current.CancellationToken);
        Assert.True(write.Success, Because("the register deposit failed", write));

        var after = await _protocol.ReadRegisterAsync(0, "X", TestContext.Current.CancellationToken);
        Assert.True(after.Success, Because("the read after the register deposit failed", after));
        Assert.True(before.Value == after.Value,
            "X changed from " + Octal(before.Value) + " to " + Octal(after.Value)
            + " when it was written back with its own value");
    }
}

/// <summary>
/// Serialises the hardware tests: there is one serial port and one machine, and two tests
/// talking to OPCOM at once would interleave their characters into nonsense.
/// </summary>
[CollectionDefinition("OpcomHardware")]
public class OpcomHardwareCollection
{
    // Marker only - xUnit needs a class to hang the collection definition on.
}
