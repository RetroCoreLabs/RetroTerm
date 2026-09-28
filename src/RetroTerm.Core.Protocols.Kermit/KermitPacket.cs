namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Represents a parsed Kermit protocol packet.
/// This is a lightweight value type used to pass packet data between
/// the framing layer and the protocol engine. The <see cref="Data"/>
/// slice is valid only until the next call to the parser.
/// </summary>
/// <remarks>
/// Kermit packet wire format (short packets):
/// <code>
/// MARK  LEN  SEQ  TYPE  DATA[0..n-1]  CHECK  EOL
///  0     1    2    3     4..4+n-1     4+n    4+n+1
/// </code>
/// LEN = tochar(n + 2 + checkLength), where n = data byte count.
/// The checksum covers bytes from LEN through the last DATA byte (inclusive).
/// </remarks>
public readonly struct KermitPacket
{
    /// <summary>
    /// Packet sequence number (0-63).
    /// </summary>
    public readonly byte Sequence;

    /// <summary>
    /// Packet type character (see <see cref="PacketType"/>).
    /// </summary>
    public readonly byte Type;

    /// <summary>
    /// Packet data field. For D packets this contains quoted/encoded file data.
    /// For S/Y packets this contains negotiation parameters.
    /// For F packets this contains the quoted filename.
    /// </summary>
    public readonly ReadOnlyMemory<byte> Data;

    /// <summary>
    /// True if the packet checksum was verified successfully.
    /// </summary>
    public readonly bool CheckValid;

    /// <summary>
    /// Creates a new parsed packet.
    /// </summary>
    public KermitPacket(byte sequence, byte type, ReadOnlyMemory<byte> data, bool checkValid)
    {
        Sequence = sequence;
        Type = type;
        Data = data;
        CheckValid = checkValid;
    }

    /// <summary>
    /// Returns true if this is an ACK packet.
    /// </summary>
    public bool IsAck => Type == PacketType.Ack;

    /// <summary>
    /// Returns true if this is a NAK packet.
    /// </summary>
    public bool IsNak => Type == PacketType.Nak;

    /// <summary>
    /// Returns true if this is an Error packet.
    /// </summary>
    public bool IsError => Type == PacketType.Error;
}
