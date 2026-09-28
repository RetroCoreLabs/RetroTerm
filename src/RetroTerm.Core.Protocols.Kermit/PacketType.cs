namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Kermit protocol packet type identifiers.
/// Each packet carries a single-byte type code in position 3 (after SOH, LEN, SEQ)
/// that determines how the data field is interpreted.
/// </summary>
/// <remarks>
/// Ported from C-Kermit ckcker.h. Only the types relevant to file transfer
/// and basic server mode are included.
/// </remarks>
public static class PacketType
{
    /// <summary>
    /// Send-Init: initiates a file transfer, carries negotiation parameters.
    /// </summary>
    public const byte SendInit = (byte)'S';

    /// <summary>
    /// Init: client-to-server initialization (like S but for server mode).
    /// </summary>
    public const byte Init = (byte)'I';

    /// <summary>
    /// File header: carries the (quoted) filename being transferred.
    /// </summary>
    public const byte FileHeader = (byte)'F';

    /// <summary>
    /// Data: carries file content (quoted/encoded).
    /// </summary>
    public const byte Data = (byte)'D';

    /// <summary>
    /// End of file: signals end of current file. Data field may contain 'D' for discard.
    /// </summary>
    public const byte EndOfFile = (byte)'Z';

    /// <summary>
    /// Break/EOT: signals end of entire transfer batch.
    /// </summary>
    public const byte Break = (byte)'B';

    /// <summary>
    /// Acknowledgment: positive response to a received packet.
    /// </summary>
    public const byte Ack = (byte)'Y';

    /// <summary>
    /// Negative acknowledgment: requests retransmission.
    /// </summary>
    public const byte Nak = (byte)'N';

    /// <summary>
    /// Error: signals a fatal protocol error; data field contains error message.
    /// </summary>
    public const byte Error = (byte)'E';

    /// <summary>
    /// Generic server command.
    /// </summary>
    public const byte Generic = (byte)'G';

    /// <summary>
    /// Text header: like FileHeader but content is for screen display.
    /// </summary>
    public const byte Text = (byte)'X';
}
