using System.Numerics;
using System.Runtime.CompilerServices;

namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Core Kermit protocol constants and encoding primitives.
/// Ported from C-Kermit ckcker.h (tochar/xunchar/ctl macros) and ckcfns.c.
///
/// The Kermit protocol encodes small integers as printable ASCII characters
/// by adding 32 (space). Control characters are made printable by XOR with 64.
/// These two primitives underpin all packet encoding.
/// </summary>
public static class KermitConst
{
    /// <summary>
    /// Start-of-header marker byte (Ctrl-A).
    /// </summary>
    public const byte SOH = 0x01;

    /// <summary>
    /// Default packet terminator: carriage return (0x0D).
    /// </summary>
    public const byte DefaultEol = 0x0D;

    /// <summary>
    /// Default control-character quote prefix: '#' (0x23).
    /// </summary>
    public const byte DefaultCtlQuote = (byte)'#';

    /// <summary>
    /// Default 8th-bit quote prefix for 7-bit channels: the ampersand (0x26).
    /// </summary>
    public const byte Default8BitQuote = (byte)'&';

    /// <summary>
    /// Default repeat-count prefix: '~' (0x7E). Reserved for future use.
    /// </summary>
    public const byte DefaultRepeatPrefix = (byte)'~';

    /// <summary>
    /// Space character (32), the base offset for tochar/unchar encoding.
    /// </summary>
    public const int SP = 32;

    /// <summary>
    /// Maximum unextended (short) packet length field value: 94.
    /// </summary>
    public const int MaxShortLength = 94;

    /// <summary>
    /// Default outbound packet data size before negotiation.
    /// </summary>
    public const int DefaultPacketSize = 80;

    /// <summary>
    /// Sequence numbers wrap modulo 64 (0-63).
    /// </summary>
    public const int SequenceModulo = 64;

    /// <summary>
    /// Default timeout for inbound packets, in seconds.
    /// </summary>
    public const int DefaultTimeout = 8;

    /// <summary>
    /// Default maximum retry count per packet.
    /// </summary>
    public const int DefaultMaxRetries = 10;

    /// <summary>
    /// Default padding count (no padding).
    /// </summary>
    public const int DefaultPadCount = 0;

    /// <summary>
    /// Default padding character (NUL).
    /// </summary>
    public const byte DefaultPadChar = 0x00;

    /// <summary>
    /// Converts an integer (0-94) to a printable ASCII character by adding 32 (space).
    /// This is the fundamental Kermit encoding primitive, used for LEN, SEQ, and
    /// parameter fields. The inverse is <see cref="UnChar"/>.
    /// </summary>
    /// <example>tochar(0) = ' ', tochar(13) = '-', tochar(63) = '?'</example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToChar(int value) => (byte)((value + SP) & 0xFF);

    /// <summary>
    /// Converts a printable ASCII character back to an integer (0-94) by subtracting 32.
    /// Inverse of <see cref="ToChar"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UnChar(int ch) => (ch - SP) & 0xFF;

    /// <summary>
    /// Toggles bit 6 (XOR with 64) to convert between control characters and
    /// their printable representations. This function is its own inverse:
    /// Ctl(Ctl(x)) == x.
    /// </summary>
    /// <remarks>
    /// Control chars 0-31 map to printable chars 64-95 ('@' through '_').
    /// DEL (127) maps to '?' (63). Applied symmetrically for encode and decode.
    /// </remarks>
    /// <example>Ctl(0x0D) = 'M', Ctl('M') = 0x0D, Ctl(127) = '?', Ctl('?') = 127</example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Ctl(int ch) => (byte)((ch ^ 64) & 0xFF);

    /// <summary>
    /// Strips the parity (high) bit from a byte, yielding a 7-bit value.
    /// Used when receiving data over a link with even/odd parity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte StripParity(byte b) => (byte)(b & 0x7F);

    /// <summary>
    /// Computes even parity for a 7-bit value and returns the byte
    /// with bit 7 set so the total number of 1-bits is even.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte AddEvenParity(byte b)
    {
        int v = b & 0x7F;
        int bits = BitOperations.PopCount((uint)v);
        return (byte)(v | ((bits & 1) << 7));
    }

    /// <summary>
    /// Returns true if the byte is a control character (0-31 or 127).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsControlChar(byte b) => b < 32 || b == 127;

    /// <summary>
    /// Returns true if the byte has the high (8th) bit set.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasHighBit(byte b) => (b & 0x80) != 0;

    /// <summary>
    /// Returns true if the 7-bit value (after stripping parity) is in the
    /// range 63-95 ('?' through '_'), meaning it represents a ctl()-encoded
    /// control character and should be un-ctl'd during decode.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCtlEncoded(byte b) => (b & 0x7F) >= 63 && (b & 0x7F) <= 95;
}
