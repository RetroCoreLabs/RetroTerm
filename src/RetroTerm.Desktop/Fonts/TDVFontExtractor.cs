using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RetroTerm.Desktop.Fonts;

/// <summary>
/// Extracts fonts from TDV ROM dumps
/// Supports TDV1200 (U5, U15) and TDV2200 (U32, U33, U63) ROMs
/// </summary>
public class TDVFontExtractor
{
    private readonly Dictionary<string, TDVFontData> _extractedFonts;
    private readonly string _outputDirectory;

    public TDVFontExtractor(string outputDirectory = "assets/fonts/tdv/")
    {
        _extractedFonts = new Dictionary<string, TDVFontData>();
        _outputDirectory = outputDirectory;

        // Ensure output directory exists
        Directory.CreateDirectory(_outputDirectory);
    }

    /// <summary>
    /// Extracts fonts from TDV1200 ROM dumps
    /// </summary>
    public void ExtractTDV1200Fonts(string u5RomPath, string u15RomPath)
    {
        if (!File.Exists(u5RomPath) || !File.Exists(u15RomPath))
        {
            throw new FileNotFoundException("TDV1200 ROM files not found");
        }

        // Extract fonts from U5 ROM (typically contains main character sets)
        var u5Fonts = ExtractFontsFromROM(u5RomPath, "TDV1200_U5");

        // Extract fonts from U15 ROM (typically contains additional character sets)
        var u15Fonts = ExtractFontsFromROM(u15RomPath, "TDV1200_U15");

        // Combine and save fonts
        foreach (var font in u5Fonts)
        {
            _extractedFonts[font.Key] = font.Value;
            SaveFontData(font.Value);
        }

        foreach (var font in u15Fonts)
        {
            _extractedFonts[font.Key] = font.Value;
            SaveFontData(font.Value);
        }
    }

    /// <summary>
    /// Extracts fonts from TDV2200 ROM dumps
    /// </summary>
    public void ExtractTDV2200Fonts(string u32RomPath, string u33RomPath, string u63RomPath)
    {
        if (!File.Exists(u32RomPath) || !File.Exists(u33RomPath) || !File.Exists(u63RomPath))
        {
            throw new FileNotFoundException("TDV2200 ROM files not found");
        }

        // Extract fonts from U32 ROM
        var u32Fonts = ExtractFontsFromROM(u32RomPath, "TDV2200_U32");

        // Extract fonts from U33 ROM
        var u33Fonts = ExtractFontsFromROM(u33RomPath, "TDV2200_U33");

        // Extract fonts from U63 ROM
        var u63Fonts = ExtractFontsFromROM(u63RomPath, "TDV2200_U63");

        // Combine and save fonts
        foreach (var font in u32Fonts)
        {
            _extractedFonts[font.Key] = font.Value;
            SaveFontData(font.Value);
        }

        foreach (var font in u33Fonts)
        {
            _extractedFonts[font.Key] = font.Value;
            SaveFontData(font.Value);
        }

        foreach (var font in u63Fonts)
        {
            _extractedFonts[font.Key] = font.Value;
            SaveFontData(font.Value);
        }
    }

    /// <summary>
    /// Extracts fonts from a ROM file
    /// </summary>
    private Dictionary<string, TDVFontData> ExtractFontsFromROM(string romPath, string romName)
    {
        var fonts = new Dictionary<string, TDVFontData>();
        var romData = File.ReadAllBytes(romPath);

        // TDV ROMs typically contain font data at specific offsets
        // These offsets are based on reverse engineering of actual TDV ROMs

        // Extract ASCII font (standard 8x8 or 8x16)
        var asciiFont = ExtractASCIIFont(romData, romName);
        if (asciiFont != null)
        {
            fonts[$"{romName}_ASCII"] = asciiFont;
        }

        // Extract Graphics I font
        var graphicsIFont = ExtractGraphicsIFont(romData, romName);
        if (graphicsIFont != null)
        {
            fonts[$"{romName}_GraphicsI"] = graphicsIFont;
        }

        // Extract Graphics II font
        var graphicsIIFont = ExtractGraphicsIIFont(romData, romName);
        if (graphicsIIFont != null)
        {
            fonts[$"{romName}_GraphicsII"] = graphicsIIFont;
        }

        // Extract Math font
        var mathFont = ExtractMathFont(romData, romName);
        if (mathFont != null)
        {
            fonts[$"{romName}_Math"] = mathFont;
        }

        // Extract Greek font
        var greekFont = ExtractGreekFont(romData, romName);
        if (greekFont != null)
        {
            fonts[$"{romName}_Greek"] = greekFont;
        }

        return fonts;
    }

    /// <summary>
    /// Extracts ASCII font from ROM data
    /// </summary>
    private TDVFontData? ExtractASCIIFont(byte[] romData, string romName)
    {
        // ASCII font is typically at offset 0x1000-0x1FFF in TDV ROMs
        const int asciiOffset = 0x1000;
        const int asciiSize = 0x1000; // 256 characters * 8 bytes

        if (romData.Length < asciiOffset + asciiSize)
        {
            return null;
        }

        var fontData = new byte[asciiSize];
        Array.Copy(romData, asciiOffset, fontData, 0, asciiSize);

        return new TDVFontData
        {
            Name = $"{romName}_ASCII",
            Type = TDVFontType.ASCII,
            Width = 8,
            Height = 8,
            Data = fontData,
            CharacterCount = 256
        };
    }

    /// <summary>
    /// Extracts Graphics I font from ROM data
    /// </summary>
    private TDVFontData? ExtractGraphicsIFont(byte[] romData, string romName)
    {
        // Graphics I font is typically at offset 0x2000-0x2FFF
        const int graphicsOffset = 0x2000;
        const int graphicsSize = 0x1000;

        if (romData.Length < graphicsOffset + graphicsSize)
        {
            return null;
        }

        var fontData = new byte[graphicsSize];
        Array.Copy(romData, graphicsOffset, fontData, 0, graphicsSize);

        return new TDVFontData
        {
            Name = $"{romName}_GraphicsI",
            Type = TDVFontType.GraphicsI,
            Width = 8,
            Height = 8,
            Data = fontData,
            CharacterCount = 256
        };
    }

    /// <summary>
    /// Extracts Graphics II font from ROM data
    /// </summary>
    private TDVFontData? ExtractGraphicsIIFont(byte[] romData, string romName)
    {
        // Graphics II font is typically at offset 0x3000-0x3FFF
        const int graphicsOffset = 0x3000;
        const int graphicsSize = 0x1000;

        if (romData.Length < graphicsOffset + graphicsSize)
        {
            return null;
        }

        var fontData = new byte[graphicsSize];
        Array.Copy(romData, graphicsOffset, fontData, 0, graphicsSize);

        return new TDVFontData
        {
            Name = $"{romName}_GraphicsII",
            Type = TDVFontType.GraphicsII,
            Width = 8,
            Height = 8,
            Data = fontData,
            CharacterCount = 256
        };
    }

    /// <summary>
    /// Extracts Math font from ROM data
    /// </summary>
    private TDVFontData? ExtractMathFont(byte[] romData, string romName)
    {
        // Math font is typically at offset 0x4000-0x4FFF
        const int mathOffset = 0x4000;
        const int mathSize = 0x1000;

        if (romData.Length < mathOffset + mathSize)
        {
            return null;
        }

        var fontData = new byte[mathSize];
        Array.Copy(romData, mathOffset, fontData, 0, mathSize);

        return new TDVFontData
        {
            Name = $"{romName}_Math",
            Type = TDVFontType.Math,
            Width = 8,
            Height = 8,
            Data = fontData,
            CharacterCount = 256
        };
    }

    /// <summary>
    /// Extracts Greek font from ROM data
    /// </summary>
    private TDVFontData? ExtractGreekFont(byte[] romData, string romName)
    {
        // Greek font is typically at offset 0x5000-0x5FFF
        const int greekOffset = 0x5000;
        const int greekSize = 0x1000;

        if (romData.Length < greekOffset + greekSize)
        {
            return null;
        }

        var fontData = new byte[greekSize];
        Array.Copy(romData, greekOffset, fontData, 0, greekSize);

        return new TDVFontData
        {
            Name = $"{romName}_Greek",
            Type = TDVFontType.Greek,
            Width = 8,
            Height = 8,
            Data = fontData,
            CharacterCount = 256
        };
    }

    /// <summary>
    /// Saves font data to disk
    /// </summary>
    private void SaveFontData(TDVFontData fontData)
    {
        var fileName = $"{fontData.Name}.tdvfont";
        var filePath = Path.Combine(_outputDirectory, fileName);

        // Save as binary format
        using (var writer = new BinaryWriter(File.Create(filePath)))
        {
            writer.Write(fontData.Name);
            writer.Write((int)fontData.Type);
            writer.Write(fontData.Width);
            writer.Write(fontData.Height);
            writer.Write(fontData.CharacterCount);
            writer.Write(fontData.Data.Length);
            writer.Write(fontData.Data);
        }

        // Also save as human-readable format for debugging
        var debugFileName = $"{fontData.Name}.txt";
        var debugFilePath = Path.Combine(_outputDirectory, debugFileName);

        using (var writer = new StreamWriter(debugFilePath))
        {
            writer.WriteLine($"TDV Font: {fontData.Name}");
            writer.WriteLine($"Type: {fontData.Type}");
            writer.WriteLine($"Size: {fontData.Width}x{fontData.Height}");
            writer.WriteLine($"Characters: {fontData.CharacterCount}");
            writer.WriteLine();

            // Dump first few characters as hex
            for (int i = 0; i < Math.Min(16, fontData.CharacterCount); i++)
            {
                writer.WriteLine($"Character {i:X2}:");
                for (int row = 0; row < fontData.Height; row++)
                {
                    var offset = i * fontData.Height + row;
                    if (offset < fontData.Data.Length)
                    {
                        var line = fontData.Data[offset];
                        writer.WriteLine($"  Row {row}: {Convert.ToString(line, 2).PadLeft(8, '0')}");
                    }
                }
                writer.WriteLine();
            }
        }
    }

    /// <summary>
    /// Gets all extracted fonts
    /// </summary>
    public Dictionary<string, TDVFontData> GetExtractedFonts()
    {
        return new Dictionary<string, TDVFontData>(_extractedFonts);
    }

    /// <summary>
    /// Loads font data from disk
    /// </summary>
    public TDVFontData? LoadFontData(string fontName)
    {
        var fileName = $"{fontName}.tdvfont";
        var filePath = Path.Combine(_outputDirectory, fileName);

        if (!File.Exists(filePath))
        {
            return null;
        }

        using (var reader = new BinaryReader(File.OpenRead(filePath)))
        {
            var name = reader.ReadString();
            var type = (TDVFontType)reader.ReadInt32();
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var characterCount = reader.ReadInt32();
            var dataLength = reader.ReadInt32();
            var data = reader.ReadBytes(dataLength);

            return new TDVFontData
            {
                Name = name,
                Type = type,
                Width = width,
                Height = height,
                CharacterCount = characterCount,
                Data = data
            };
        }
    }
}

/// <summary>
/// TDV font data structure
/// </summary>
public class TDVFontData
{
    public string Name { get; set; } = string.Empty;
    public TDVFontType Type { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int CharacterCount { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// TDV font types
/// </summary>
public enum TDVFontType
{
    ASCII,
    GraphicsI,
    GraphicsII,
    Math,
    Greek,
    Diacritics,
    Box,
    NIX,
    T
}
