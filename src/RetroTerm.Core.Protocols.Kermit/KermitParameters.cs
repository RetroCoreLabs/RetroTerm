namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Kermit Send-Init parameter negotiation.
/// Ported from C-Kermit ckcfns.c spar() and rpar() functions.
///
/// The S (Send-Init) and Y (ACK) packets carry a structured data field
/// where each byte position encodes a specific protocol parameter.
/// Both sides exchange these to agree on transfer settings.
///
/// Field layout (1-based positions matching the protocol spec):
/// <code>
/// Pos  Field   Encoding        Meaning
///  1   MAXL    tochar(n)       Max packet length I can receive
///  2   TIME    tochar(n)       Timeout in seconds I want you to use
///  3   NPAD    tochar(n)       Padding characters I need
///  4   PADC    ctl(c)          Padding character I need
///  5   EOL     tochar(c)       End-of-line character I need
///  6   QCTL    literal         Control quote prefix I will use
///  7   QBIN    literal/Y/N     8th-bit quote capability
///  8   CHKT    '1','2','3'     Block check type I can do
///  9   REPT    literal/space   Repeat prefix (space = not supported)
/// </code>
/// </summary>
public sealed class KermitParameters
{
    /// <summary>
    /// Maximum packet length the side can receive.
    /// </summary>
    public int MaxPacketLength { get; set; } = KermitConst.DefaultPacketSize;

    /// <summary>
    /// Timeout in seconds the remote side should use.
    /// </summary>
    public int Timeout { get; set; } = KermitConst.DefaultTimeout;

    /// <summary>
    /// Number of padding characters needed before each packet.
    /// </summary>
    public int PaddingCount { get; set; } = KermitConst.DefaultPadCount;

    /// <summary>
    /// Padding character (NUL default).
    /// </summary>
    public byte PaddingChar { get; set; } = KermitConst.DefaultPadChar;

    /// <summary>
    /// End-of-line / packet terminator character.
    /// </summary>
    public byte EndOfLine { get; set; } = KermitConst.DefaultEol;

    /// <summary>
    /// Control-character quote prefix.
    /// </summary>
    public byte ControlQuote { get; set; } = KermitConst.DefaultCtlQuote;

    /// <summary>
    /// 8th-bit quote character. 'Y' means willing but let other side pick,
    /// 'N' means unable, any other printable char means "use this character".
    /// </summary>
    public byte EighthBitQuote { get; set; } = (byte)'Y';

    /// <summary>
    /// Block check type: '1' = 6-bit, '2' = 12-bit, '3' = CRC-16.
    /// </summary>
    public byte CheckType { get; set; } = (byte)'1';

    /// <summary>
    /// Repeat prefix character. Space (0x20) means not supported.
    /// </summary>
    public byte RepeatPrefix { get; set; } = (byte)' ';

    /// <summary>
    /// Encodes this parameter set into a Send-Init / ACK data field.
    /// </summary>
    /// <param name="output">
    /// Buffer to write encoded parameters into (at least 9 bytes).
    /// </param>
    /// <returns>
    /// Number of bytes written.
    /// </returns>
    public int Encode(Span<byte> output)
    {
        output[0] = KermitConst.ToChar(MaxPacketLength);
        output[1] = KermitConst.ToChar(Timeout);
        output[2] = KermitConst.ToChar(PaddingCount);
        output[3] = KermitConst.Ctl(PaddingChar);
        output[4] = KermitConst.ToChar(EndOfLine);
        output[5] = ControlQuote;
        output[6] = EighthBitQuote;
        output[7] = CheckType;
        output[8] = RepeatPrefix;
        return 9;
    }

    /// <summary>
    /// Decodes a received Send-Init / ACK data field into this parameter set.
    /// Missing trailing fields are left at their default values, per the protocol
    /// specification (shorter packets mean the sender doesn't support those features).
    /// </summary>
    /// <param name="data">
    /// Encoded parameter data from a received S or Y packet.
    /// </param>
    public void Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 1)
            MaxPacketLength = KermitConst.UnChar(data[0]);

        if (data.Length >= 2)
            Timeout = KermitConst.UnChar(data[1]);

        if (data.Length >= 3)
            PaddingCount = KermitConst.UnChar(data[2]);

        if (data.Length >= 4)
            PaddingChar = KermitConst.Ctl(data[3]);

        if (data.Length >= 5)
            EndOfLine = KermitConst.ToChar(KermitConst.UnChar(data[4]));

        // EOL field: the value is tochar'd, so we unchar it to get the actual EOL byte.
        // Re-do this correctly:
        if (data.Length >= 5)
            EndOfLine = (byte)KermitConst.UnChar(data[4]);

        if (data.Length >= 6)
            ControlQuote = data[5];

        if (data.Length >= 7)
            EighthBitQuote = data[6];

        if (data.Length >= 8)
            CheckType = data[7];

        if (data.Length >= 9)
            RepeatPrefix = data[8];
    }

    /// <summary>
    /// Creates a <see cref="KermitParameters"/> from our local <see cref="KermitOptions"/>.
    /// This produces the parameter set we advertise to the remote side.
    /// </summary>
    public static KermitParameters FromOptions(KermitOptions options)
    {
        return new KermitParameters
        {
            MaxPacketLength = options.MaxReceivePacketSize,
            Timeout = options.Timeout,
            PaddingCount = options.PaddingCount,
            PaddingChar = options.PaddingChar,
            EndOfLine = options.EndOfLine,
            ControlQuote = options.ControlQuote,
            EighthBitQuote = options.Use8BitQuoting ? options.EighthBitQuote : (byte)'N',
            CheckType = (byte)('0' + options.BlockCheckType),
            RepeatPrefix = (byte)' '
        };
    }

    /// <summary>
    /// Negotiates the effective 8th-bit quoting state between local and remote parameters.
    /// Returns true if 8th-bit quoting should be used, along with the agreed prefix character.
    /// </summary>
    /// <remarks>
    /// Negotiation rules from the Kermit protocol specification:
    /// - If both sides specify 'Y': use 8th-bit quoting with the default prefix, the ampersand.
    /// - If one specifies 'Y' and the other specifies a character: use that character.
    /// - If both specify the same character: use it.
    /// - If one specifies 'N' or they disagree on character: no 8th-bit quoting.
    /// </remarks>
    public static bool Negotiate8BitQuoting(byte local, byte remote, out byte prefix)
    {
        prefix = KermitConst.Default8BitQuote;

        if (local == (byte)'N' || remote == (byte)'N')
            return false;

        if (local == (byte)'Y' && remote == (byte)'Y')
        {
            prefix = KermitConst.Default8BitQuote;
            return true;
        }

        if (local == (byte)'Y' && remote != (byte)'Y' && remote != (byte)'N')
        {
            prefix = remote;
            return true;
        }

        if (remote == (byte)'Y' && local != (byte)'Y' && local != (byte)'N')
        {
            prefix = local;
            return true;
        }

        if (local == remote)
        {
            prefix = local;
            return true;
        }

        return false;
    }
}
