namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Kermit data payload encoding (quoting) and decoding (unquoting).
/// Ported from C-Kermit ckcfns.c getpkt() (encode) and decode() functions.
///
/// The Kermit protocol must transmit arbitrary 8-bit binary data over links
/// that may only support 7-bit printable ASCII. This is achieved by quoting:
///
/// Control characters (0-31, 127): prefixed with '#' then XOR'd with 64.
///   Example: CR (0x0D) becomes '#M' (0x23 0x4D)
///
/// The quote prefix itself ('#'): doubled as '##'.
///
/// 8th-bit quoting (for 7-bit links): bytes with bit 7 set are prefixed
///   with an ampersand, the high bit stripped, then control-quoted if needed.
///   Example: 0x8D becomes ampersand #M (0x26 0x23 0x4D)
///
/// The 8th-bit prefix, the ampersand, when it appears as literal data: hash then ampersand.
/// </summary>
public static class KermitEncoding
{
    /// <summary>
    /// Encodes (quotes) raw data bytes into Kermit wire format.
    /// Stops encoding when the output would exceed <paramref name="maxOutput"/> bytes,
    /// ensuring the result fits in a single packet data field.
    /// </summary>
    /// <param name="input">
    /// Raw data bytes to encode.
    /// </param>
    /// <param name="output">
    /// Buffer to receive encoded bytes.
    /// </param>
    /// <param name="maxOutput">
    /// Maximum bytes to write to output.
    /// </param>
    /// <param name="ctlQuote">
    /// Control-character quote prefix (typically '#').
    /// </param>
    /// <param name="ebqQuote">
    /// 8th-bit quote prefix, typically the ampersand.
    /// </param>
    /// <param name="use8BitQuoting">
    /// True if 8th-bit quoting is active (7-bit link).
    /// </param>
    /// <param name="bytesConsumed">
    /// Number of input bytes consumed.
    /// </param>
    /// <returns>
    /// Number of bytes written to output.
    /// </returns>
    public static int Encode(
        ReadOnlySpan<byte> input,
        Span<byte> output,
        int maxOutput,
        byte ctlQuote,
        byte ebqQuote,
        bool use8BitQuoting,
        out int bytesConsumed)
    {
        int outPos = 0;
        int inPos = 0;

        for (; inPos < input.Length; inPos++)
        {
            byte raw = input[inPos];
            byte b = raw;
            bool needEbq = false;

            // Determine how many wire bytes this character requires,
            // so we can check if it fits before writing anything.
            int needed = 1; // the character itself

            if (use8BitQuoting && KermitConst.HasHighBit(b))
            {
                needEbq = true;
                b = KermitConst.StripParity(b);
                needed++; // 8th-bit prefix
            }

            if (KermitConst.IsControlChar(b))
            {
                needed++; // control prefix
            }
            else if (b == ctlQuote)
            {
                needed++; // quote the quote prefix
            }
            else if (use8BitQuoting && b == ebqQuote)
            {
                needed++; // quote the 8th-bit prefix with control prefix
            }

            // Check if there's room in the output for this character
            if (outPos + needed > maxOutput)
                break;

            // Emit 8th-bit prefix if needed
            if (needEbq)
            {
                output[outPos++] = ebqQuote;
            }

            // Emit control prefix + transformed character
            if (KermitConst.IsControlChar(b))
            {
                output[outPos++] = ctlQuote;
                output[outPos++] = KermitConst.Ctl(b);
            }
            else if (b == ctlQuote || (use8BitQuoting && b == ebqQuote))
            {
                output[outPos++] = ctlQuote;
                output[outPos++] = b;
            }
            else
            {
                output[outPos++] = b;
            }
        }

        bytesConsumed = inPos;
        return outPos;
    }

    /// <summary>
    /// Decodes (unquotes) Kermit wire format back to raw data bytes.
    /// </summary>
    /// <param name="input">
    /// Encoded bytes from a packet data field.
    /// </param>
    /// <param name="output">
    /// Buffer to receive decoded raw bytes.
    /// </param>
    /// <param name="ctlQuote">
    /// Control-character quote prefix.
    /// </param>
    /// <param name="ebqQuote">
    /// 8th-bit quote prefix.
    /// </param>
    /// <param name="use8BitQuoting">
    /// True if 8th-bit quoting is active.
    /// </param>
    /// <returns>
    /// Number of bytes written to output.
    /// </returns>
    public static int Decode(
        ReadOnlySpan<byte> input,
        Span<byte> output,
        byte ctlQuote,
        byte ebqQuote,
        bool use8BitQuoting)
    {
        int outPos = 0;
        int i = 0;

        while (i < input.Length)
        {
            byte b = input[i++];
            byte highBit = 0;

            // Check for 8th-bit prefix
            if (use8BitQuoting && b == ebqQuote)
            {
                if (i >= input.Length) break;
                highBit = 0x80;
                b = input[i++];
            }

            // Check for control prefix
            if (b == ctlQuote)
            {
                if (i >= input.Length) break;
                b = input[i++];

                // If the character (7-bit value) is in the ctl-encoded range
                // (63-95 = '?' through '_'), apply ctl() to recover the original
                // control character. Otherwise it's a quoted literal (like '##' or '#&').
                if (KermitConst.IsCtlEncoded(b))
                {
                    b = KermitConst.Ctl(b);
                }
            }

            output[outPos++] = (byte)(b | highBit);
        }

        return outPos;
    }
}
