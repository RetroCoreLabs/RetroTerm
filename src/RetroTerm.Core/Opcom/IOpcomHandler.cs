using System;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Transfer;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Direction of an OPCOM log entry.
/// </summary>
public enum OpcomLogDirection { Tx, Rx }

/// <summary>
/// A log entry for the OPCOM traffic log.
/// </summary>
public readonly struct OpcomLogEntry
{
    public DateTime Timestamp { get; }
    public OpcomLogDirection Direction { get; }
    public byte[] Data { get; }

    public OpcomLogEntry(OpcomLogDirection direction, byte[] data)
    {
        Timestamp = DateTime.Now;
        Direction = direction;
        Data = data;
    }
}

/// <summary>
/// OPCOM CPU state as detected from protocol responses.
/// </summary>
public enum OpcomCpuState
{
    Unknown,
    Running,
    Stopped,
}

/// <summary>
/// Interface for the OPCOM protocol handler that intercepts serial data.
/// Installed into TerminalSession similarly to IFileTransferHandler.
/// </summary>
public interface IOpcomHandler
{
    /// <summary>
    /// Gets or sets whether incoming data should also be forwarded to the terminal emulator.
    /// When false (default), OPCOM fully intercepts all data.
    /// </summary>
    bool PassThrough { get; set; }

    /// <summary>
    /// Gets the current OPCOM state machine state.
    /// </summary>
    OpcomProtocolState State { get; }

    /// <summary>
    /// Gets the detected CPU state.
    /// </summary>
    OpcomCpuState CpuState { get; }

    /// <summary>
    /// Gets whether the handler is active (attached to session).
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the register state model.
    /// </summary>
    OpcomRegisterState Registers { get; }

    /// <summary>
    /// Gets the memory cache.
    /// </summary>
    OpcomMemoryCache Memory { get; }

    /// <summary>
    /// Processes incoming data from the serial connection.
    /// Called by TerminalSession when OPCOM is active.
    /// </summary>
    void ProcessIncomingData(ReadOnlySpan<byte> data);

    /// <summary>
    /// Activates the OPCOM handler with the given send delegate.
    /// </summary>
    void Activate(SendBytesAsync sendDelegate);

    /// <summary>
    /// Deactivates the OPCOM handler.
    /// </summary>
    void Deactivate();

    // -- Command methods (all async, queue internally) --

    Task<OpcomResult> ReadMemoryAsync(int address, CancellationToken ct = default);
    Task<OpcomResult> WriteMemoryAsync(int address, ushort value, CancellationToken ct = default);
    Task<OpcomResult> DumpMemoryAsync(int startAddress, int endAddress, CancellationToken ct = default);
    Task<OpcomResult> ReadRegisterAsync(int level, string registerName, CancellationToken ct = default);
    Task<OpcomResult> WriteRegisterAsync(int level, string registerName, ushort value, CancellationToken ct = default);
    Task<OpcomResult> ReadInternalRegisterAsync(string registerName, CancellationToken ct = default);
    Task<OpcomResult> WriteInternalRegisterAsync(string registerName, ushort value, CancellationToken ct = default);
    Task<OpcomResult> DumpRegistersAsync(int startLevel, int endLevel, CancellationToken ct = default);
    Task<OpcomResult> DumpInternalRegistersAsync(CancellationToken ct = default);
    Task<OpcomResult> IOXReadAsync(int deviceAddress, CancellationToken ct = default);
    Task<OpcomResult> IOXWriteAsync(int deviceAddress, ushort oprValue, CancellationToken ct = default);
    Task<OpcomResult> StopCpuAsync(CancellationToken ct = default);
    Task<OpcomResult> MasterClearAsync(CancellationToken ct = default);
    Task<OpcomResult> StartAsync(int address, CancellationToken ct = default);
    Task<OpcomResult> SingleStepAsync(int count = 1, CancellationToken ct = default);
    Task<OpcomResult> SetBreakpointAsync(int address, CancellationToken ct = default);
    Task<OpcomResult> EscapeAsync(CancellationToken ct = default);
    /// <summary>
    /// Runs the microprogrammed memory test on one 64K bank. STOP mode only, and it
    /// WRITES memory - see the implementation for what a real machine left behind.
    /// </summary>
    /// <param name="bank">
    /// The 64K bank number.
    /// </param>
    /// <param name="ct">
    /// Cancels the wait for the reply.
    /// </param>
    Task<OpcomResult> MemoryTestAsync(int bank, CancellationToken ct = default);

    Task<OpcomResult> BootLoadAsync(string opcomCommand, CancellationToken ct = default);
    Task<OpcomResult> SetExamineModeAsync(int? pageTable, CancellationToken ct = default);

    // -- Upload --

    Task<OpcomResult> UploadWordsAsync(int startAddress, ushort[] words, int count,
        Action<int, int>? progressCallback, CancellationToken ct = default);

    // -- Events --

    event Action<OpcomLogEntry>? LogEntry;
    event Action<OpcomProtocolState>? StateChanged;
    event Action<OpcomCpuState>? CpuStateChanged;
}
