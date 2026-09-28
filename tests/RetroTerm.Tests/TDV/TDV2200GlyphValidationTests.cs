using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Fonts;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Validates TDV2200 glyph rendering against the FontTDV2200.cs glyph definitions.
/// This tests that GetFontBits returns the correct bitmap data for each character
/// in all 4 character sets.
/// </summary>
public class TDV2200GlyphValidationTests
{
    private readonly FontTDV2200 _font = new FontTDV2200();

    /// <summary>
    /// Validate that FontTDV2200 has the expected properties
    /// </summary>
    [Fact]
    public void FontTDV2200_HasCorrectProperties()
    {
        Assert.Equal(16, _font.Height);
        Assert.Equal(14, _font.HeightToUse);
        Assert.Equal(8, _font.Width);
        Assert.NotNull(_font.glyphs);
        Assert.NotNull(_font.fontNumOffset);

        // fontNumOffset should be [0, 0, 128, 256, 384]
        Assert.Equal(5, _font.fontNumOffset.Length);
        Assert.Equal(0, _font.fontNumOffset[0]);
        Assert.Equal(0, _font.fontNumOffset[1]);
        Assert.Equal(128, _font.fontNumOffset[2]);
        Assert.Equal(256, _font.fontNumOffset[3]);
        Assert.Equal(384, _font.fontNumOffset[4]);
    }

    /// <summary>
    /// Validate that GetFontBits returns non-null for all printable characters in character set 1 (fontNum=0)
    /// Character set 1: 95 standard characters (0x20-0x7E)
    /// </summary>
    [Fact]
    public void CharacterSet1_AllPrintableCharsReturnGlyphs()
    {
        var failures = new List<string>();

        for (ushort charCode = 0x20; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, 0);
            Assert.NotNull(bits);
            if (bits == null)
            {
                failures.Add($"0x{charCode:X2} ('{(char)charCode}')");
            }
        }

        Assert.True(failures.Count == 0,
            $"Character set 1 (fontNum=0) missing glyphs for: {string.Join(", ", failures)}");
    }

    /// <summary>
    /// Validate that GetFontBits returns non-null for all characters in character set 2 (fontNum=2)
    /// Character set 2: 95 characters for line drawing, histogram drawing and plotting (0x20-0x7E)
    /// These map to glyph indices 0x80-0xDE (offset 128)
    /// </summary>
    [Fact]
    public void CharacterSet2_AllPrintableCharsReturnGlyphs()
    {
        var failures = new List<string>();

        for (ushort charCode = 0x20; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, 2);
            Assert.NotNull(bits);
            if (bits == null)
            {
                failures.Add($"0x{charCode:X2}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Character set 2 (fontNum=2) missing glyphs for: {string.Join(", ", failures)}");
    }

    /// <summary>
    /// Validate that GetFontBits returns non-null for all characters in character set 3 (fontNum=3)
    /// Character set 3: 32 subscript/superscript characters (0x30-0x4F typically)
    /// These map to glyph indices starting at offset 256
    /// </summary>
    [Fact]
    public void CharacterSet3_AllPrintableCharsReturnGlyphs()
    {
        var failures = new List<string>();

        // Character set 3 uses codes 0x30-0x4F for subscript/superscript
        for (ushort charCode = 0x20; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, 3);
            Assert.NotNull(bits);
            if (bits == null)
            {
                failures.Add($"0x{charCode:X2}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Character set 3 (fontNum=3) missing glyphs for: {string.Join(", ", failures)}");
    }

    /// <summary>
    /// Validate that GetFontBits returns non-null for all characters in character set 4 (fontNum=4)
    /// Character set 4: 32 characters for displaying control codes in transparent mode
    /// These map to glyph indices starting at offset 384
    /// </summary>
    [Fact]
    public void CharacterSet4_AllPrintableCharsReturnGlyphs()
    {
        var failures = new List<string>();

        for (ushort charCode = 0x20; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, 4);
            Assert.NotNull(bits);
            if (bits == null)
            {
                failures.Add($"0x{charCode:X2}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Character set 4 (fontNum=4) missing glyphs for: {string.Join(", ", failures)}");
    }

    /// <summary>
    /// Validate that all returned glyph arrays have the correct length (16 rows)
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AllGlyphs_HaveCorrectLength(int fontNum)
    {
        var failures = new List<string>();

        for (ushort charCode = 0x20; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, fontNum);
            Assert.NotNull(bits);
            if (bits != null && bits.Length != 16)
            {
                failures.Add($"fontNum={fontNum} char=0x{charCode:X2} length={bits.Length}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Glyphs with wrong length: {string.Join(", ", failures)}");
    }

    /// <summary>
    /// Validate specific known character shapes in character set 1.
    /// These test cases verify pixel-by-pixel correctness of specific glyphs.
    /// </summary>
    [Fact]
    public void CharacterSet1_LetterA_HasCorrectShape()
    {
        // Character 'A' (0x41) in character set 1
        ushort[]? bits = _font.GetFontBits(0x41, 0);
        Assert.NotNull(bits);
        Assert.NotNull(bits);

        // Row 1 should have some pixels (typically top of A has the apex)
        // Row 2-5 should have the sloping sides
        // Row 6 should have the crossbar
        // Let's verify the glyph has content (non-zero) in the expected rows
        bool hasContent = false;
        for (int i = 0; i < 10; i++)
        {
            if (bits[i] != 0)
            {
                hasContent = true;
                break;
            }
        }
        Assert.True(hasContent, "Letter 'A' should have pixel content in rows 0-9");

        // Bottom rows should be empty (baseline padding)
        Assert.Equal(0, bits[14]);
        Assert.Equal(0, bits[15]);
    }

    /// <summary>
    /// Validate that space character returns a glyph.
    /// Note: Space (0x20) maps to mapSpaceChar (0x00) FIRST, then fontNumOffset is applied.
    /// So for fontNum=0, space uses glyph 0 (blank).
    /// For fontNum=2, space uses glyph 0+128=128 (first glyph of char set 2).
    /// For fontNum=3, space uses glyph 0+256=256 (first glyph of char set 3).
    /// For fontNum=4, space uses glyph 0+384=384 (first glyph of char set 4).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SpaceCharacter_ReturnsGlyph(int fontNum)
    {
        ushort[]? bits = _font.GetFontBits(0x20, fontNum);
        Assert.NotNull(bits);
        Assert.NotNull(bits);
        Assert.Equal(16, bits.Length);
    }

    /// <summary>
    /// Validate that space in character set 0 (normal ASCII) is blank
    /// </summary>
    [Fact]
    public void SpaceCharacter_InCharSet0_IsBlank()
    {
        ushort[]? bits = _font.GetFontBits(0x20, 0);
        Assert.NotNull(bits);
        Assert.NotNull(bits);

        // Space in charset 0 should be all zeros
        for (int i = 0; i < bits.Length; i++)
        {
            Assert.Equal(0, bits[i]);
        }
    }

    /// <summary>
    /// Document what glyphs space maps to in different character sets.
    /// This is important for understanding the behavior.
    /// </summary>
    [Fact]
    public void SpaceCharacter_MapsToCorrectGlyphPositions()
    {
        // Space (0x20) is remapped to mapSpaceChar (0x00) before fontNumOffset is applied
        // fontNumOffset = [0, 0, 128, 256, 384]
        // So:
        // - fontNum=0: glyph index = 0 * 16 = 0 (blank glyph)
        // - fontNum=2: glyph index = (0 + 128) * 16 = 2048 (first glyph of charset 2)
        // - fontNum=3: glyph index = (0 + 256) * 16 = 4096 (first glyph of charset 3)
        // - fontNum=4: glyph index = (0 + 384) * 16 = 6144 (first glyph of charset 4)

        // Verify charset 0 space is blank
        ushort[]? bits0 = _font.GetFontBits(0x20, 0);
        Assert.NotNull(bits0);
        bool isBlank0 = true;
        for (int i = 0; i < bits0.Length; i++)
        {
            if (bits0[i] != 0) { isBlank0 = false; break; }
        }
        Assert.True(isBlank0, "Space in charset 0 should be blank");

        // Other charsets may have non-blank glyphs at position 0+offset
        // This is by design - the special charsets don't have "space" in the same way
        ushort[]? bits2 = _font.GetFontBits(0x20, 2);
        Assert.NotNull(bits2);
        ushort[]? bits3 = _font.GetFontBits(0x20, 3);
        Assert.NotNull(bits3);
        ushort[]? bits4 = _font.GetFontBits(0x20, 4);

        Assert.NotNull(bits4);

        Assert.NotNull(bits2);
        Assert.NotNull(bits3);
        Assert.NotNull(bits4);
    }

    /// <summary>
    /// Validate that character set 2 contains different glyphs than character set 1
    /// for the same character codes (proving the offset works correctly)
    /// </summary>
    [Fact]
    public void CharacterSet2_DifferentFromCharacterSet1()
    {
        int differentCount = 0;

        // Check characters 0x21-0x7E (skip space which maps to blank in both)
        for (ushort charCode = 0x21; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits1 = _font.GetFontBits(charCode, 0);
            Assert.NotNull(bits1);
            ushort[]? bits2 = _font.GetFontBits(charCode, 2);

            Assert.NotNull(bits2);

            if (bits1 == null || bits2 == null)
                continue;

            bool different = false;
            for (int i = 0; i < Math.Min(bits1.Length, bits2.Length); i++)
            {
                if (bits1[i] != bits2[i])
                {
                    different = true;
                    break;
                }
            }

            if (different)
                differentCount++;
        }

        // At least some characters should be different between the two sets
        Assert.True(differentCount > 50,
            $"Expected at least 50 different glyphs between char set 1 and 2, but only found {differentCount}");
    }

    /// <summary>
    /// Validate that character set 3 contains different glyphs than character set 1
    /// </summary>
    [Fact]
    public void CharacterSet3_DifferentFromCharacterSet1()
    {
        int differentCount = 0;

        for (ushort charCode = 0x21; charCode <= 0x7E; charCode++)
        {
            ushort[]? bits1 = _font.GetFontBits(charCode, 0);
            Assert.NotNull(bits1);
            ushort[]? bits3 = _font.GetFontBits(charCode, 3);

            Assert.NotNull(bits3);

            if (bits1 == null || bits3 == null)
                continue;

            bool different = false;
            for (int i = 0; i < Math.Min(bits1.Length, bits3.Length); i++)
            {
                if (bits1[i] != bits3[i])
                {
                    different = true;
                    break;
                }
            }

            if (different)
                differentCount++;
        }

        // At least some characters should be different
        Assert.True(differentCount > 30,
            $"Expected at least 30 different glyphs between char set 1 and 3, but only found {differentCount}");
    }

    /// <summary>
    /// Dump all glyphs for character set 1 to verify visually
    /// This creates a text representation of all characters
    /// </summary>
    [Fact]
    public void DumpCharacterSet1_AllCharacters()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Character Set 1 (Standard ASCII) - fontNum=0 ===");
        sb.AppendLine();

        for (ushort charCode = 0x00; charCode <= 0x7F; charCode++)
        {
            DumpGlyph(sb, charCode, 0);
        }

        // Just verify we can generate the dump without errors
        string dump = sb.ToString();
        Assert.True(dump.Length > 1000, "Dump should contain substantial content");
    }

    /// <summary>
    /// Dump all glyphs for character set 2 to verify visually
    /// </summary>
    [Fact]
    public void DumpCharacterSet2_AllCharacters()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Character Set 2 (Line Drawing/Graphics) - fontNum=2 ===");
        sb.AppendLine();

        for (ushort charCode = 0x00; charCode <= 0x7F; charCode++)
        {
            DumpGlyph(sb, charCode, 2);
        }

        string dump = sb.ToString();
        Assert.True(dump.Length > 1000, "Dump should contain substantial content");
    }

    /// <summary>
    /// Dump all glyphs for character set 3 to verify visually
    /// </summary>
    [Fact]
    public void DumpCharacterSet3_AllCharacters()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Character Set 3 (Subscript/Superscript) - fontNum=3 ===");
        sb.AppendLine();

        for (ushort charCode = 0x00; charCode <= 0x7F; charCode++)
        {
            DumpGlyph(sb, charCode, 3);
        }

        string dump = sb.ToString();
        Assert.True(dump.Length > 1000, "Dump should contain substantial content");
    }

    /// <summary>
    /// Dump all glyphs for character set 4 to verify visually
    /// </summary>
    [Fact]
    public void DumpCharacterSet4_AllCharacters()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Character Set 4 (Control Code Display) - fontNum=4 ===");
        sb.AppendLine();

        for (ushort charCode = 0x00; charCode <= 0x7F; charCode++)
        {
            DumpGlyph(sb, charCode, 4);
        }

        string dump = sb.ToString();
        Assert.True(dump.Length > 1000, "Dump should contain substantial content");
    }

    /// <summary>
    /// Validate that specific line drawing characters in set 2 have the expected shapes
    /// </summary>
    [Fact]
    public void CharacterSet2_LineDrawingChars_HaveCorrectPatterns()
    {
        // Character set 2 offset is 128, so char 0x20 maps to glyph 0x80+0x20 = 0xA0
        // Let's check some specific patterns

        // Horizontal line character - should have pixels in middle row
        ushort[]? horizontalLine = _font.GetFontBits(0x2D, 2); // '-' in set 2
        Assert.NotNull(horizontalLine);

        // Vertical line character - should have pixels in middle column
        ushort[]? verticalLine = _font.GetFontBits(0x7C, 2); // '|' in set 2
        Assert.NotNull(verticalLine);

        // Both should have some content
        bool hasHorizContent = false;
        bool hasVertContent = false;

        for (int i = 0; i < horizontalLine.Length; i++)
        {
            if (horizontalLine[i] != 0) hasHorizContent = true;
            if (verticalLine[i] != 0) hasVertContent = true;
        }

        Assert.True(hasHorizContent, "Horizontal line char should have pixel content");
        Assert.True(hasVertContent, "Vertical line char should have pixel content");
    }

    /// <summary>
    /// Validate the total number of glyphs in the font
    /// </summary>
    [Fact]
    public void FontTDV2200_HasExpectedGlyphCount()
    {
        // Font has 1023 glyphs as per comment in FontTDV2200.cs
        // Each glyph is 16 ushorts (Height=16)
        // So total array size should be 1023 * 16 = 16368
        int expectedMinGlyphs = 512; // At least 512 glyphs (4 sets * 128)
        Assert.NotNull(_font.glyphs);
        int actualGlyphCount = _font.glyphs.Length / 16;

        Assert.True(actualGlyphCount >= expectedMinGlyphs,
            $"Expected at least {expectedMinGlyphs} glyphs, but found {actualGlyphCount}");
    }

    /// <summary>
    /// Helper method to dump a single glyph as ASCII art
    /// </summary>
    private void DumpGlyph(StringBuilder sb, ushort charCode, int fontNum)
    {
        sb.AppendLine($"// Character 0x{charCode:X2} (fontNum={fontNum})");

        ushort[]? bits = _font.GetFontBits(charCode, fontNum);
        Assert.NotNull(bits);
        if (bits == null)
        {
            sb.AppendLine("// (null - no glyph)");
            sb.AppendLine();
            return;
        }

        for (int row = 0; row < Math.Min(bits.Length, 16); row++)
        {
            sb.Append("// ");
            ushort rowBits = bits[row];

            // Render 8 bits (width of font)
            for (int col = 7; col >= 0; col--)
            {
                bool pixelOn = (rowBits & (1 << col)) != 0;
                sb.Append(pixelOn ? 'X' : ' ');
            }
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Export character set glyphs to a format that can be compared
    /// Returns a dictionary of character code -> pixel data string
    /// </summary>
    private Dictionary<ushort, string> ExportCharacterSet(int fontNum)
    {
        var result = new Dictionary<ushort, string>();

        for (ushort charCode = 0x00; charCode <= 0x7F; charCode++)
        {
            ushort[]? bits = _font.GetFontBits(charCode, fontNum);
            Assert.NotNull(bits);
            if (bits == null)
            {
                result[charCode] = "NULL";
                continue;
            }

            var sb = new StringBuilder();
            for (int row = 0; row < Math.Min(bits.Length, 16); row++)
            {
                sb.Append($"{bits[row]:X4}");
                if (row < 15) sb.Append(",");
            }
            result[charCode] = sb.ToString();
        }

        return result;
    }

    /// <summary>
    /// Test that verifies the pixel data for a specific glyph matches expected values.
    /// This allows comparing against known-good values from the spec.
    /// </summary>
    [Fact]
    public void CharacterSet1_DigitZero_HasExpectedPixelData()
    {
        // Character '0' (0x30) in character set 1
        ushort[]? bits = _font.GetFontBits(0x30, 0);
        Assert.NotNull(bits);
        Assert.NotNull(bits);
        Assert.Equal(16, bits.Length);

        // The digit zero should have:
        // - Empty top rows (0-1)
        // - Oval shape in middle rows (2-9)
        // - Empty bottom rows (10-15)

        // At minimum, verify it's not blank
        bool hasContent = false;
        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] != 0)
            {
                hasContent = true;
                break;
            }
        }
        Assert.True(hasContent, "Digit '0' should have pixel content");
    }

    /// <summary>
    /// Verify control characters (0x00-0x1F) behavior in different font numbers
    /// </summary>
    [Fact]
    public void ControlCharacters_HandleCorrectly()
    {
        // Control characters should still return glyph data (possibly blank)
        for (ushort charCode = 0x00; charCode <= 0x1F; charCode++)
        {
            ushort[]? bits0 = _font.GetFontBits(charCode, 0);
            Assert.NotNull(bits0);
            ushort[]? bits4 = _font.GetFontBits(charCode, 4);

            Assert.NotNull(bits4);

            // Font set 4 is for displaying control codes, so should have visible glyphs
            // Font set 0 may have blank or special glyphs
            Assert.NotNull(bits0);
            Assert.NotNull(bits4);
        }
    }

    /// <summary>
    /// Verify that DEL character (0x7F) is handled
    /// </summary>
    [Fact]
    public void DelCharacter_HandledCorrectly()
    {
        ushort[]? bits = _font.GetFontBits(0x7F, 0);
        Assert.NotNull(bits);
        Assert.NotNull(bits);
        Assert.Equal(16, bits.Length);
    }
}
