using System;
using System.IO;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Parsed a.out 16-bit file header and data.
/// </summary>
public sealed class Aout16FileData
{
    /// <summary>
    /// Magic number (0407=normal, 0410=read-only text, 0411=separated I and D, etc.).
    /// </summary>
    public ushort Magic { get; set; }

    /// <summary>
    /// Text segment size in words.
    /// </summary>
    public ushort TextSize { get; set; }

    /// <summary>
    /// Data segment size in words.
    /// </summary>
    public ushort DataSize { get; set; }

    /// <summary>
    /// BSS segment size in words (uninitialized, zero-filled).
    /// </summary>
    public ushort BssSize { get; set; }

    /// <summary>
    /// Symbol table size.
    /// </summary>
    public ushort SymbolsSize { get; set; }

    /// <summary>
    /// Entry point address.
    /// </summary>
    public ushort EntryPoint { get; set; }

    /// <summary>
    /// Zero page size in words.
    /// </summary>
    public ushort ZeroPageSize { get; set; }

    /// <summary>
    /// Flags.
    /// </summary>
    public ushort Flags { get; set; }

    /// <summary>
    /// Total number of words to load (text + data).
    /// </summary>
    public int TotalWords => TextSize + DataSize;

    /// <summary>
    /// Human-readable magic number description.
    /// </summary>
    public string MagicDescription => Magic switch
    {
        0x0107 => "Normal (0407)",            // 0407 octal
        0x0108 => "Read-only text (0410)",     // 0410 octal
        0x0109 => "Separated I&D (0411)",      // 0411 octal
        0x0105 => "Read-only shareable (0405)", // 0405 octal
        0x0118 => "Auto-overlay nonsep (0430)", // 0430 octal
        0x0119 => "Auto-overlay sep (0431)",    // 0431 octal
        _ => $"Unknown ({OctalHelper.ToOctal6(Magic)})"
    };

    /// <summary>
    /// The loaded words. Text segment followed by data segment.
    /// </summary>
    public ushort[] Words { get; set; } = Array.Empty<ushort>();

    /// <summary>
    /// Load base address (text segment starts here).
    /// </summary>
    public ushort LoadAddress { get; set; }
}

/// <summary>
/// Parser for 16-bit a.out format executables (BSD 2.11 / ND-100).
/// Ported from nd100x/external/libsymbols/src/aout.c.
///
/// File layout:
/// - Header: 8 x 16-bit words (magic, text, data, bss, syms, entry, zp, flag)
/// - Zero page (zp words, skipped)
/// - Text segment (text words)
/// - Data segment (data words)
/// - Relocation tables (ignored)
/// - Symbol table (ignored)
///
/// All values are little-endian (LSB first), unlike BPUN which is big-endian.
/// </summary>
public static class Aout16FileParser
{
    // Magic numbers (stored as octal in the file, read as little-endian 16-bit)
    private const ushort MAGIC_NORMAL = 0x0107;      // 0407 octal
    private const ushort MAGIC_RDONLY_TEXT = 0x0108;   // 0410 octal
    private const ushort MAGIC_SEPARATED = 0x0109;     // 0411 octal
    private const ushort MAGIC_SHAREABLE = 0x0105;     // 0405 octal
    private const ushort MAGIC_OVERLAY_NS = 0x0118;    // 0430 octal
    private const ushort MAGIC_OVERLAY_S = 0x0119;     // 0431 octal

    /// <summary>
    /// Parses an a.out 16-bit file from disk.
    /// </summary>
    public static Aout16FileData? Parse(string filePath, ushort textStart = 0)
    {
        if (!File.Exists(filePath)) return null;
        byte[] fileBytes = File.ReadAllBytes(filePath);
        return Parse(fileBytes, textStart);
    }

    /// <summary>
    /// Parses a.out 16-bit data from a byte array.
    /// </summary>
    public static Aout16FileData? Parse(byte[] data, ushort textStart = 0)
    {
        if (data == null || data.Length < 16) return null; // Header is 8 words = 16 bytes

        var result = new Aout16FileData();
        int pos = 0;

        // Read header (8 x 16-bit words, little-endian)
        result.Magic = ReadWordLE(data, ref pos);
        result.TextSize = ReadWordLE(data, ref pos);
        result.DataSize = ReadWordLE(data, ref pos);
        result.BssSize = ReadWordLE(data, ref pos);
        result.SymbolsSize = ReadWordLE(data, ref pos);
        result.EntryPoint = ReadWordLE(data, ref pos);
        result.ZeroPageSize = ReadWordLE(data, ref pos);
        result.Flags = ReadWordLE(data, ref pos);

        // Validate magic number
        if (!IsValidMagic(result.Magic))
            return null;

        // Use entry point as load address if specified, otherwise use textStart parameter
        result.LoadAddress = result.EntryPoint != 0 ? result.EntryPoint : textStart;

        // Skip zero page
        pos += result.ZeroPageSize * 2;

        if (pos > data.Length) return null;

        // Allocate words array for text + data
        int totalWords = result.TextSize + result.DataSize;
        result.Words = new ushort[totalWords];
        int wordIndex = 0;

        // Load text segment
        for (int i = 0; i < result.TextSize; i++)
        {
            if (pos + 1 >= data.Length) break;
            result.Words[wordIndex++] = ReadWordLE(data, ref pos);
        }

        // Load data segment
        for (int i = 0; i < result.DataSize; i++)
        {
            if (pos + 1 >= data.Length) break;
            result.Words[wordIndex++] = ReadWordLE(data, ref pos);
        }

        return result;
    }

    /// <summary>
    /// Reads a 16-bit word in little-endian order.
    /// </summary>
    private static ushort ReadWordLE(byte[] data, ref int pos)
    {
        if (pos + 1 >= data.Length)
        {
            pos += 2;
            return 0;
        }
        ushort lo = data[pos++];
        ushort hi = data[pos++];
        return (ushort)((hi << 8) | lo);
    }

    private static bool IsValidMagic(ushort magic)
    {
        return magic == MAGIC_NORMAL ||
               magic == MAGIC_RDONLY_TEXT ||
               magic == MAGIC_SEPARATED ||
               magic == MAGIC_SHAREABLE ||
               magic == MAGIC_OVERLAY_NS ||
               magic == MAGIC_OVERLAY_S;
    }

    /// <summary>
    /// Attempts to detect if a file is in a.out 16-bit format by checking the magic number.
    /// </summary>
    public static bool IsAout16File(byte[] data)
    {
        if (data == null || data.Length < 16) return false;
        int pos = 0;
        ushort magic = ReadWordLE(data, ref pos);
        return IsValidMagic(magic);
    }
}
