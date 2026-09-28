using System.Runtime.CompilerServices;

namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Kermit protocol checksum computation.
/// Ported from C-Kermit ckcfn2.c chk1(), chk2(), chk3() functions.
///
/// Type 1 (default): 6-bit folded checksum encoded as one tochar'd byte.
/// Type 2: 12-bit checksum encoded as two tochar'd bytes.
/// Type 3: 16-bit CRC-CCITT encoded as three tochar'd bytes.
/// </summary>
public static class KermitChecksum
{
    /// <summary>
    /// Computes a type 1 (6-bit) checksum over the given bytes.
    /// The algorithm sums all bytes, then folds bits 6-7 into bits 0-5.
    /// </summary>
    /// <remarks>
    /// From C-Kermit ckcfn2.c:
    /// <code>
    /// chk = (((chk AND 0xC0) shifted right 6) + chk) AND 0x3F
    /// </code>
    /// The checksum covers LEN through end of DATA (not MARK or EOL).
    /// </remarks>
    /// <param name="data">
    /// Bytes to checksum (LEN through last DATA byte).
    /// </param>
    /// <returns>
    /// 6-bit checksum value (0-63), ready to be passed to tochar().
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ComputeType1(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }
        return ((sum + ((sum & 0xC0) >> 6)) & 0x3F);
    }

    /// <summary>
    /// Computes a type 2 (12-bit) checksum over the given bytes.
    /// </summary>
    /// <param name="data">
    /// Bytes to checksum.
    /// </param>
    /// <returns>
    /// 12-bit checksum value (0-4095).
    /// </returns>
    public static int ComputeType2(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }
        return sum & 0x0FFF;
    }

    /// <summary>
    /// CRC-CCITT lookup table for high nibble.
    /// Ported from C-Kermit ckcfn2.c crcta[] (octal constants converted to hex).
    /// </summary>
    private static readonly ushort[] CrcTableHigh =
    [
        0x0000, 0x1081, 0x2102, 0x3183,
        0x4204, 0x5285, 0x6306, 0x7387,
        0x8408, 0x9489, 0xA50A, 0xB58B,
        0xC60C, 0xD68D, 0xE70E, 0xF78F
    ];

    /// <summary>
    /// CRC-CCITT lookup table for low nibble.
    /// Ported from C-Kermit ckcfn2.c crctb[] (octal constants converted to hex).
    /// </summary>
    private static readonly ushort[] CrcTableLow =
    [
        0x0000, 0x1189, 0x2312, 0x329B,
        0x4624, 0x57AD, 0x6536, 0x74BF,
        0x8C48, 0x9DC1, 0xAF5A, 0xBED3,
        0xCA6C, 0xDBE5, 0xE97E, 0xF8F7
    ];

    /// <summary>
    /// Computes a type 3 (16-bit CRC-CCITT) checksum over the given bytes.
    /// Uses the split-nibble lookup table algorithm from C-Kermit.
    /// </summary>
    /// <param name="data">
    /// Bytes to checksum.
    /// </param>
    /// <returns>
    /// 16-bit CRC value (0-65535).
    /// </returns>
    public static int ComputeType3(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        for (int i = 0; i < data.Length; i++)
        {
            int c = crc ^ data[i];
            crc = (crc >> 8) ^ CrcTableHigh[(c & 0xF0) >> 4] ^ CrcTableLow[c & 0x0F];
        }
        return crc & 0xFFFF;
    }
}
