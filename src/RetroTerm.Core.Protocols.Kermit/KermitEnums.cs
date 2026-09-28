namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Protocol state machine states for the Kermit engine.
/// Ported from C-Kermit ckcpro.w state definitions.
/// </summary>
public enum KermitState
{
    /// <summary>
    /// No transfer in progress.
    /// </summary>
    Idle,

    // --- Sender states ---

    /// <summary>
    /// Sender has transmitted S (Send-Init), awaiting ACK.
    /// </summary>
    SendingInit,

    /// <summary>
    /// Sender has transmitted F (File Header), awaiting ACK.
    /// </summary>
    SendingFileHeader,

    /// <summary>
    /// Sender is transmitting D (Data) packets, awaiting ACKs.
    /// </summary>
    SendingData,

    /// <summary>
    /// Sender has transmitted Z (End of File), awaiting ACK.
    /// </summary>
    SendingEof,

    /// <summary>
    /// Sender has transmitted B (Break/EOT), awaiting ACK.
    /// </summary>
    SendingBreak,

    // --- Receiver states ---

    /// <summary>
    /// Receiver is waiting for S (Send-Init) packet.
    /// </summary>
    ReceivingInit,

    /// <summary>
    /// Receiver is waiting for F (File Header) or B (Break) packet.
    /// </summary>
    ReceivingFileHeader,

    /// <summary>
    /// Receiver is waiting for D (Data) or Z (End of File) packet.
    /// </summary>
    ReceivingData,

    // --- Server states ---

    /// <summary>
    /// Server mode, waiting for client commands.
    /// </summary>
    Serving,

    // --- Terminal states ---

    /// <summary>
    /// Transfer completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Transfer failed due to error or excessive retries.
    /// </summary>
    Failed
}

/// <summary>
/// Parity mode for the communication link.
/// Determines whether parity bits are stripped on receive and added on send.
/// </summary>
public enum ParityMode
{
    /// <summary>
    /// No parity, 8-bit clean channel.
    /// </summary>
    None,

    /// <summary>
    /// Even parity: high bit set so total 1-bits count is even.
    /// </summary>
    Even,

    /// <summary>
    /// Odd parity: high bit set so total 1-bits count is odd.
    /// </summary>
    Odd,

    /// <summary>
    /// Mark parity: high bit always set.
    /// </summary>
    Mark,

    /// <summary>
    /// Space parity: high bit always clear (same as 7-bit stripping).
    /// </summary>
    Space
}

/// <summary>
/// Determines behavior when a received file has the same name as an existing file.
/// </summary>
public enum FileCollisionMode
{
    /// <summary>
    /// Rename the incoming file with a unique suffix (safest).
    /// </summary>
    Rename,

    /// <summary>
    /// Overwrite the existing file.
    /// </summary>
    Overwrite,

    /// <summary>
    /// Skip the incoming file and continue with the next.
    /// </summary>
    Skip
}
