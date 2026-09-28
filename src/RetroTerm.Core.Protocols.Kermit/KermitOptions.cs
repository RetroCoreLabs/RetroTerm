namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Configuration options for the Kermit protocol engine.
/// These values represent our local capabilities and preferences,
/// which are communicated to the remote side during Send-Init negotiation.
/// </summary>
public sealed class KermitOptions
{
    /// <summary>
    /// Maximum packet size we are willing to receive (default 80, max 94 for short packets).
    /// </summary>
    public int MaxReceivePacketSize { get; set; } = KermitConst.DefaultPacketSize;

    /// <summary>
    /// Timeout in seconds we request the remote side to use (default 8).
    /// </summary>
    public int Timeout { get; set; } = KermitConst.DefaultTimeout;

    /// <summary>
    /// Maximum number of retries per packet before aborting (default 10).
    /// </summary>
    public int MaxRetries { get; set; } = KermitConst.DefaultMaxRetries;

    /// <summary>
    /// Number of padding characters to send before each packet (default 0).
    /// </summary>
    public int PaddingCount { get; set; } = KermitConst.DefaultPadCount;

    /// <summary>
    /// Padding character (default NUL).
    /// </summary>
    public byte PaddingChar { get; set; } = KermitConst.DefaultPadChar;

    /// <summary>
    /// End-of-line character that terminates each packet (default CR).
    /// </summary>
    public byte EndOfLine { get; set; } = KermitConst.DefaultEol;

    /// <summary>
    /// Control-character quote prefix (default '#').
    /// </summary>
    public byte ControlQuote { get; set; } = KermitConst.DefaultCtlQuote;

    /// <summary>
    /// 8th-bit quote prefix for 7-bit channels (the ampersand by default).
    /// </summary>
    public byte EighthBitQuote { get; set; } = KermitConst.Default8BitQuote;

    /// <summary>
    /// Parity mode for the communication link (default None).
    /// </summary>
    public ParityMode Parity { get; set; } = ParityMode.None;

    /// <summary>
    /// When true, 8th-bit quoting is always enabled regardless of parity setting.
    /// When false, 8th-bit quoting is automatically enabled when Parity is not None.
    /// </summary>
    public bool Force8BitQuoting { get; set; }

    /// <summary>
    /// Start-of-header marker byte (default SOH / 0x01).
    /// </summary>
    public byte Mark { get; set; } = KermitConst.SOH;

    /// <summary>
    /// Block check type: 1 (6-bit checksum), 2 (12-bit), 3 (CRC-16). Default 1.
    /// </summary>
    public int BlockCheckType { get; set; } = 1;

    /// <summary>
    /// Sender delay in seconds before transmitting the first Send-Init packet (default 0).
    /// </summary>
    public int Delay { get; set; } = 0;

    /// <summary>
    /// Returns true if 8th-bit quoting should be used for this configuration.
    /// </summary>
    public bool Use8BitQuoting => Force8BitQuoting || Parity != ParityMode.None;
}
