using System;
using System.Threading.Tasks;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Types of OPCOM commands that can be queued for execution.
/// </summary>
public enum OpcomCommandType
{
    ReadMemory,
    WriteMemory,
    DumpMemory,
    ReadRegister,
    WriteRegister,
    ReadInternalRegister,
    WriteInternalRegister,
    DumpRegisters,
    DumpInternalRegisters,
    IOXRead,
    IOXWrite,
    Stop,
    MasterClear,
    Start,
    SingleStep,
    Breakpoint,
    Escape,
    SetExamineMode,
    UploadFile,
    RawSend,
    BootLoad,

    /// <summary>
    /// The microprogrammed memory test, bb followed by a hash. STOP mode only.
    ///
    /// Measured on a real ND-120/CX on 9 September 2026: it is echoed and answered with a
    /// single prompt and NO report, and it finishes in well under a second. It DOES touch
    /// memory - address 000000 came back holding 177777 where it had held 114631 - so it
    /// is not a read-only check.
    /// </summary>
    MemoryTest,
}

/// <summary>
/// Result of an OPCOM command execution.
/// </summary>
public class OpcomResult
{
    public bool Success { get; }
    public ushort Value { get; }
    public string? ErrorMessage { get; }
    public ushort[]? DumpValues { get; }
    public int[]? DumpAddresses { get; }

    private OpcomResult(bool success, ushort value, string? error, ushort[]? dumpValues, int[]? dumpAddresses)
    {
        Success = success;
        Value = value;
        ErrorMessage = error;
        DumpValues = dumpValues;
        DumpAddresses = dumpAddresses;
    }

    public static OpcomResult Ok(ushort value = 0) => new(true, value, null, null, null);
    public static OpcomResult OkDump(ushort[] values, int[] addresses) => new(true, 0, null, values, addresses);
    public static OpcomResult Fail(string error) => new(false, 0, error, null, null);
}

/// <summary>
/// Base class for OPCOM commands. Each command carries a TaskCompletionSource
/// so callers can await the result.
/// </summary>
public class OpcomCommand
{
    public OpcomCommandType Type { get; }
    public TaskCompletionSource<OpcomResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Octal string to send as the command body (e.g., address, register name).
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Optional value to write (for write commands).
    /// </summary>
    public ushort? WriteValue { get; }

    /// <summary>
    /// Optional end address for dump commands.
    /// </summary>
    public int EndAddress { get; }

    /// <summary>
    /// Optional level for register commands (0-15 decimal, 0-17 octal).
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// Optional step count for single step.
    /// </summary>
    public int StepCount { get; }

    /// <summary>
    /// Raw bytes to send (for RawSend command).
    /// </summary>
    public byte[]? RawBytes { get; }

    public OpcomCommand(OpcomCommandType type, string commandText = "", ushort? writeValue = null,
        int endAddress = 0, int level = 0, int stepCount = 1, byte[]? rawBytes = null)
    {
        Type = type;
        CommandText = commandText;
        WriteValue = writeValue;
        EndAddress = endAddress;
        Level = level;
        StepCount = stepCount;
        RawBytes = rawBytes;
    }
}

/// <summary>
/// Where a command has got to, for the status bar to show.
/// </summary>
public enum OpcomProgressKind
{
    /// <summary>Nothing is running.</summary>
    Idle,

    /// <summary>A command is in flight.</summary>
    Running,

    /// <summary>The last command finished and did what was asked.</summary>
    Succeeded,

    /// <summary>The last command failed, and Message says why.</summary>
    Failed,
}

/// <summary>
/// What the OPCOM protocol is doing at this moment, and how the last thing turned out.
///
/// <see cref="Total"/> of zero means there is no count to show and the work is simply in
/// progress: a register read has one step and no meaningful percentage. A dump knows how
/// many words it asked for, so it reports both numbers and a bar can fill.
///
/// A finished command does NOT go straight back to idle. It reports Succeeded or Failed
/// with a line saying which, because "it stopped moving" and "it worked" look identical
/// otherwise, and a failure that clears itself is a failure nobody sees.
/// </summary>
public readonly struct OpcomProgress
{
    /// <summary>
    /// A line for a person to read: "Dumping memory 000000 to 000377" while it runs, then
    /// something like "Dumped 17 words" or the reason it failed. Empty when idle.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// How many of the expected items have arrived. Zero when nothing is counted.
    /// </summary>
    public int Completed { get; }

    /// <summary>
    /// How many items are expected in total, or zero when the work has no count.
    /// </summary>
    public int Total { get; }

    /// <summary>
    /// Where the command has got to.
    /// </summary>
    public OpcomProgressKind Kind { get; }

    /// <summary>
    /// True only while a command is actually in flight.
    /// </summary>
    public bool IsRunning => Kind == OpcomProgressKind.Running;

    /// <summary>
    /// Percentage complete, or -1 when there is no count to derive one from.
    /// </summary>
    public int Percent => Total > 0 ? (int)((long)Completed * 100 / Total) : -1;

    /// <summary>
    /// Creates a progress report.
    /// </summary>
    /// <param name="kind">
    /// Where the command has got to.
    /// </param>
    /// <param name="message">
    /// The line to show. Empty for idle.
    /// </param>
    /// <param name="completed">
    /// How many items have arrived so far.
    /// </param>
    /// <param name="total">
    /// How many are expected, or zero when the work has no count.
    /// </param>
    public OpcomProgress(OpcomProgressKind kind, string message, int completed, int total)
    {
        Kind = kind;
        Message = message ?? string.Empty;
        Completed = completed;
        Total = total;
    }

    /// <summary>
    /// Nothing is running and there is nothing to report.
    /// </summary>
    public static readonly OpcomProgress Idle = new(OpcomProgressKind.Idle, string.Empty, 0, 0);
}
