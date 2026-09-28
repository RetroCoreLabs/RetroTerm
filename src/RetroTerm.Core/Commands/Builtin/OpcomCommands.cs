using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// OPCOM debug access for scripts, the console and MCP — everything the OPCOM Debug
/// window does: examine/deposit memory, read/write working and internal registers per
/// program level, CPU control (stop/start/master-clear/step/breakpoint), IOX, boot.
///
/// ND numbers are OCTAL: addresses, values and register levels are all octal, matching
/// the OPCOM console and the debug window. `level` is the octal program level 0..17.
///
/// One verb with `action=` rather than ~20 classes — the OPCOM operations share the
/// same handler, address/value parsing and octal results, and "do everything the OPCOM
/// view does" is one capability. HELP OPCOM lists every action.
/// </summary>
public sealed class OpcomDebugCommand : ISessionCommand
{
    public string Name => "OPCOM";
    public string Summary => "OPCOM debug: examine/deposit memory and registers, CPU control, IOX, boot (all values OCTAL)";
    public string Example => "OPCOM action=readreg level=0 reg=P";
    public bool ProducesOutput => true;
    public bool ProducesCapture => true; // into=var stores the octal result value

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("action", CommandParameterType.String, required: true, defaultValue: null,
            "What to do: status, passthrough, readmem, writemem, dumpmem, readreg, writereg, " +
            "readintreg, writeintreg, dumpregs, dumpintregs, ioxread, ioxwrite, stop, start, " +
            "masterclear, step, breakpoint, escape, boot"),
        new CommandParameter("addr", CommandParameterType.String, required: false, defaultValue: null,
            "Octal address (readmem/writemem/dumpmem/start/breakpoint) or device (ioxread/ioxwrite)"),
        new CommandParameter("endaddr", CommandParameterType.String, required: false, defaultValue: null,
            "Octal end address for dumpmem"),
        new CommandParameter("value", CommandParameterType.String, required: false, defaultValue: null,
            "Octal value to write (writemem/writereg/writeintreg/ioxwrite)"),
        new CommandParameter("reg", CommandParameterType.String, required: false, defaultValue: null,
            "Register name: working S D P B L A T X, or internal PANS STS OPR ALD ... (read/writereg, read/writeintreg)"),
        new CommandParameter("level", CommandParameterType.String, required: false, defaultValue: "0",
            "Octal program level 0..17 for working-register and dumpregs actions"),
        new CommandParameter("endlevel", CommandParameterType.String, required: false, defaultValue: null,
            "Octal end level for dumpregs (defaults to level)"),
        new CommandParameter("count", CommandParameterType.Int, required: false, defaultValue: "1",
            "Instruction count for step"),
        new CommandParameter("pass", CommandParameterType.Bool, required: false, defaultValue: null,
            "For action=passthrough: also forward OPCOM data to the emulator (true/false)"),
        new CommandParameter("timeout", CommandParameterType.Int, required: false, defaultValue: "10000",
            "Give up after this many MILLISECONDS waiting for the OPCOM response")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var action = (args.GetString("action") ?? string.Empty).Trim().ToLowerInvariant();

        // The debug window attaches an OpcomProtocol; reuse it if present, otherwise
        // attach a fresh one so MCP works whether or not the window was ever opened.
        var protocol = session.ActiveOpcomHandler as OpcomProtocol;
        if (protocol == null)
        {
            if (!session.IsConnected)
            {
                return CommandResult.Fail("not connected — OPCOM needs an open connection (an OPCOM Simulator or a real ND-100)");
            }
            protocol = new OpcomProtocol();
            session.AttachOpcomHandler(protocol);
        }

        // status / passthrough are local — no round trip.
        if (action == "status")
        {
            var sb = new StringBuilder(128);
            sb.Append("opcom active: ").Append(protocol.IsActive ? "yes" : "no").Append('\n');
            sb.Append("state: ").Append(protocol.State).Append('\n');
            sb.Append("cpu: ").Append(protocol.CpuState).Append('\n');
            sb.Append("passthrough: ").Append(protocol.PassThrough ? "on" : "off");
            return CommandResult.Ok(sb.ToString());
        }
        if (action == "passthrough")
        {
            if (args.Contains("pass"))
            {
                protocol.PassThrough = args.GetBool("pass", protocol.PassThrough);
            }
            return CommandResult.Ok($"passthrough: {(protocol.PassThrough ? "on" : "off")}");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(args.GetInt("timeout", 10_000));
        var ct = cts.Token;

        try
        {
            switch (action)
            {
                case "readmem":
                    return Report(await protocol.ReadMemoryAsync(ReqAddr(args), ct), "mem");
                case "writemem":
                    return Report(await protocol.WriteMemoryAsync(ReqAddr(args), ReqValue(args), ct), "mem");
                case "dumpmem":
                    return ReportDump(await protocol.DumpMemoryAsync(ReqAddr(args), ReqEndAddr(args), ct), ReqAddr(args));
                case "readreg":
                    return Report(await protocol.ReadRegisterAsync(Level(args), ReqReg(args), ct), "reg");
                case "writereg":
                    return Report(await protocol.WriteRegisterAsync(Level(args), ReqReg(args), ReqValue(args), ct), "reg");
                case "readintreg":
                    return Report(await protocol.ReadInternalRegisterAsync(ReqReg(args), ct), "ireg");
                case "writeintreg":
                    return Report(await protocol.WriteInternalRegisterAsync(ReqReg(args), ReqValue(args), ct), "ireg");
                case "dumpregs":
                    return ReportWorkingRegisters(await protocol.DumpRegistersAsync(Level(args), EndLevel(args), ct),
                        protocol, Level(args), EndLevel(args));
                case "dumpintregs":
                    return ReportInternalRegisters(await protocol.DumpInternalRegistersAsync(ct), protocol);
                case "ioxread":
                    return Report(await protocol.IOXReadAsync(ReqAddr(args), ct), "iox");
                case "ioxwrite":
                    return Report(await protocol.IOXWriteAsync(ReqAddr(args), ReqValue(args), ct), "iox");
                case "stop":
                    return Report(await protocol.StopCpuAsync(ct), "cpu");
                case "start":
                    return Report(await protocol.StartAsync(ReqAddr(args), ct), "cpu");
                case "masterclear":
                    return Report(await protocol.MasterClearAsync(ct), "cpu");
                case "step":
                    return Report(await protocol.SingleStepAsync(args.GetInt("count", 1), ct), "cpu");
                case "breakpoint":
                    return Report(await protocol.SetBreakpointAsync(ReqAddr(args), ct), "cpu");
                case "escape":
                    return Report(await protocol.EscapeAsync(ct), "cpu");
                case "boot":
                    return Report(await protocol.BootLoadAsync(ReqRawReg(args, "reg or a boot command string is required for boot"), ct), "boot");
                default:
                    return CommandResult.Fail($"OPCOM: unknown action '{action}' — see HELP OPCOM");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CommandResult.Fail($"OPCOM {action}: timed out waiting for the ND response");
        }
        catch (ArgumentException ex) // ReqAddr/ReqValue/ReqReg missing or bad octal
        {
            return CommandResult.Fail("OPCOM " + action + ": " + ex.Message);
        }
    }

    // ── octal argument helpers ───────────────────────────────────────────
    private static int ReqAddr(CommandArgs a) => ParseOctal32(Require(a, "addr"), "addr");
    private static int ReqEndAddr(CommandArgs a) => ParseOctal32(Require(a, "endaddr"), "endaddr");
    private static ushort ReqValue(CommandArgs a) => ParseOctal16(Require(a, "value"), "value");
    private static string ReqReg(CommandArgs a) => Require(a, "reg");
    private static string ReqRawReg(CommandArgs a, string msg)
    {
        var v = a.GetString("reg");
        if (string.IsNullOrWhiteSpace(v)) throw new ArgumentException(msg);
        return v!;
    }
    private static int Level(CommandArgs a) => ParseOctal32(a.GetString("level", "0")!, "level");
    private static int EndLevel(CommandArgs a) => a.Contains("endlevel") ? ParseOctal32(a.GetString("endlevel")!, "endlevel") : Level(a);

    private static string Require(CommandArgs a, string name)
    {
        var v = a.GetString(name);
        if (string.IsNullOrWhiteSpace(v)) throw new ArgumentException($"'{name}=' is required for this action");
        return v!;
    }

    private static int ParseOctal32(string text, string name)
    {
        if (!OctalHelper.TryParseOctal32(text.AsSpan(), out int value))
            throw new ArgumentException($"'{name}={text}' is not a valid octal number");
        return value;
    }

    private static ushort ParseOctal16(string text, string name)
    {
        if (!OctalHelper.TryParseOctal(text.AsSpan(), out ushort value))
            throw new ArgumentException($"'{name}={text}' is not a valid octal 16-bit value");
        return value;
    }

    // ── result formatting (octal, ND style) ──────────────────────────────
    private static CommandResult Report(OpcomResult r, string label)
    {
        if (!r.Success)
        {
            return CommandResult.Fail($"OPCOM: {r.ErrorMessage ?? "failed"}");
        }
        var octal = OctalHelper.ToOctal6(r.Value);
        var outcome = CommandResult.Ok($"{label} = {octal} (octal)");
        outcome.CaptureValue = octal; // into=var gets the octal value
        return outcome;
    }

    /// <summary>
    /// Prints what a working-register dump actually read. A register dump returns no
    /// DumpValues - the values land in the protocol's register table - so reporting it
    /// like a memory dump printed "0 value(s)" and hid whether the dump had worked at
    /// all. One line per program level, the eight registers in OPCOM's own order.
    /// </summary>
    private static CommandResult ReportWorkingRegisters(OpcomResult r, OpcomProtocol protocol, int startLevel, int endLevel)
    {
        if (!r.Success)
        {
            return CommandResult.Fail($"OPCOM: {r.ErrorMessage ?? "failed"}");
        }
        if (endLevel < startLevel) endLevel = startLevel;
        var sb = new StringBuilder(256);
        sb.Append("level ");
        for (int i = 0; i < WorkingRegisterNames.Count; i++)
        {
            sb.Append(' ').Append(WorkingRegisterNames.ByNumber[i].PadLeft(6));
        }
        sb.Append('\n');
        for (int level = startLevel; level <= endLevel && level < OpcomRegisterState.MaxLevels; level++)
        {
            sb.Append(OctalHelper.ToOctalTrimmed(level).PadLeft(5)).Append(' ');
            for (int i = 0; i < WorkingRegisterNames.Count; i++)
            {
                sb.Append(' ').Append(OctalHelper.ToOctal6(protocol.Registers.Levels[level].GetByIndex(i)));
            }
            sb.Append('\n');
        }
        return CommandResult.Ok(sb.ToString().TrimEnd('\n'));
    }

    /// <summary>
    /// Prints what an internal-register dump read, by name, for the same reason as
    /// ReportWorkingRegisters above.
    /// </summary>
    private static CommandResult ReportInternalRegisters(OpcomResult r, OpcomProtocol protocol)
    {
        if (!r.Success)
        {
            return CommandResult.Fail($"OPCOM: {r.ErrorMessage ?? "failed"}");
        }
        var sb = new StringBuilder(256);
        for (int i = 0; i < InternalRegisterDefs.Count; i++)
        {
            sb.Append(InternalRegisterDefs.Registers[i].OpcomName.PadLeft(4))
              .Append(": ")
              .Append(OctalHelper.ToOctal6(protocol.Registers.Internal[i]))
              .Append('\n');
        }
        return CommandResult.Ok(sb.ToString().TrimEnd('\n'));
    }

    private static CommandResult ReportDump(OpcomResult r, int startAddr)
    {
        if (!r.Success)
        {
            return CommandResult.Fail($"OPCOM: {r.ErrorMessage ?? "failed"}");
        }
        var values = r.DumpValues ?? Array.Empty<ushort>();
        var addresses = r.DumpAddresses;
        var sb = new StringBuilder(values.Length * 20 + 32);
        sb.Append(values.Length).Append(" value(s), octal:\n");
        for (int i = 0; i < values.Length; i++)
        {
            int addr = addresses != null && i < addresses.Length ? addresses[i] : startAddr + i;
            sb.Append(OctalHelper.ToOctal6_18bit(addr)).Append(": ").Append(OctalHelper.ToOctal6(values[i])).Append('\n');
        }
        return CommandResult.Ok(sb.ToString().TrimEnd('\n'));
    }
}
