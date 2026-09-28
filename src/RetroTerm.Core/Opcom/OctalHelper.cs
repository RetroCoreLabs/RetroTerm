using System;
using System.Buffers;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Zero-allocation octal formatting and parsing utilities for OPCOM communication.
/// All ND-100 values are octal (base 8).
/// </summary>
public static class OctalHelper
{
    /// <summary>
    /// Formats a 16-bit value as a 6-digit octal string with leading zeros.
    /// </summary>
    public static string ToOctal6(ushort value)
    {
        Span<char> buf = stackalloc char[6];
        FormatOctal(value, buf);
        return new string(buf);
    }

    /// <summary>
    /// Formats a 16-bit value as octal without leading zeros. Returns "0" for zero.
    /// </summary>
    public static string ToOctalTrimmed(int value)
    {
        if (value == 0) return "0";
        Span<char> buf = stackalloc char[6];
        FormatOctal((ushort)(value & 0xFFFF), buf);
        int start = 0;
        while (start < 5 && buf[start] == '0') start++;
        return new string(buf.Slice(start));
    }

    /// <summary>
    /// Formats a 16-bit value as octal into the provided buffer (must be at least 6 chars).
    /// Returns the number of characters written (always 6).
    /// </summary>
    public static int FormatOctal(ushort value, Span<char> destination)
    {
        if (destination.Length < 6)
            throw new ArgumentException("Destination must be at least 6 characters", nameof(destination));

        for (int i = 5; i >= 0; i--)
        {
            destination[i] = (char)('0' + (value & 7));
            value >>= 3;
        }
        return 6;
    }

    /// <summary>
    /// Formats an 18-bit value (for ALD register) as a 6-digit octal string.
    /// </summary>
    public static string ToOctal6_18bit(int value)
    {
        Span<char> buf = stackalloc char[6];
        for (int i = 5; i >= 0; i--)
        {
            buf[i] = (char)('0' + (value & 7));
            value >>= 3;
        }
        return new string(buf);
    }

    /// <summary>
    /// Formats a 24-bit physical address as an 8-digit octal string.
    /// </summary>
    public static string ToOctal8(int value)
    {
        Span<char> buf = stackalloc char[8];
        for (int i = 7; i >= 0; i--)
        {
            buf[i] = (char)('0' + (value & 7));
            value >>= 3;
        }
        return new string(buf);
    }

    /// <summary>
    /// Parses an octal string to a 16-bit unsigned value.
    /// Returns false if the string contains non-octal characters.
    /// </summary>
    public static bool TryParseOctal(ReadOnlySpan<char> text, out ushort result)
    {
        result = 0;
        if (text.Length == 0) return false;

        int val = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < '0' || c > '7') return false;
            // Both operands stay int here, so there is no widening and no sign extension.
            val = (val << 3) | (c - '0');
            if (val > 0x1FFFF) return false; // overflow for 16-bit
        }
        result = (ushort)(val & 0xFFFF);
        return true;
    }

    /// <summary>
    /// Parses an octal string to a 32-bit integer (for addresses and ALD values).
    /// Returns false if the string contains non-octal characters.
    /// </summary>
    public static bool TryParseOctal32(ReadOnlySpan<char> text, out int result)
    {
        result = 0;
        if (text.Length == 0) return false;

        long val = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < '0' || c > '7') return false;
            // Cast to uint before the or: an int operand would be sign-extended into the
            // long (CS0675). The digit is already validated to 0-7, so this is a no-op
            // numerically - it just stops the widening from being a sign extension.
            val = (val << 3) | (uint)(c - '0');
            if (val > int.MaxValue) return false;
        }
        result = (int)val;
        return true;
    }

    /// <summary>
    /// Parses octal digits from a byte span (ASCII bytes '0'-'7').
    /// Returns the parsed value and the number of bytes consumed.
    /// </summary>
    public static bool TryParseOctalBytes(ReadOnlySpan<byte> data, out ushort result, out int bytesConsumed)
    {
        result = 0;
        bytesConsumed = 0;
        int val = 0;

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            if (b < (byte)'0' || b > (byte)'7') break;
            val = (val << 3) | (b - '0');
            bytesConsumed++;
        }

        if (bytesConsumed == 0) return false;
        result = (ushort)(val & 0xFFFF);
        return true;
    }

    /// <summary>
    /// Checks if a byte is an octal digit ('0'-'7').
    /// </summary>
    public static bool IsOctalDigit(byte b)
    {
        return b >= (byte)'0' && b <= (byte)'7';
    }

    /// <summary>
    /// Converts a 16-bit word to two ASCII characters (high byte first).
    /// Non-printable characters are replaced with '.'.
    /// </summary>
    public static void WordToAscii(ushort word, Span<char> destination)
    {
        if (destination.Length < 2)
            throw new ArgumentException("Destination must be at least 2 characters", nameof(destination));

        byte hi = (byte)((word >> 8) & 0x7F);
        byte lo = (byte)(word & 0x7F);
        destination[0] = (hi >= 0x20 && hi < 0x7F) ? (char)hi : '.';
        destination[1] = (lo >= 0x20 && lo < 0x7F) ? (char)lo : '.';
    }

    /// <summary>
    /// Converts octal string bytes to their ASCII string representation.
    /// </summary>
    public static string OctalBytesToString(ReadOnlySpan<byte> data, int length)
    {
        if (length <= 0) return string.Empty;
        int count = Math.Min(length, data.Length);
        Span<char> buf = count <= 128 ? stackalloc char[count] : new char[count];
        for (int i = 0; i < count; i++)
        {
            buf[i] = (char)data[i];
        }
        return new string(buf);
    }
}
