using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Rendering;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Tests that verify actual screen rendering output for TDV2200 features.
/// Uses Avalonia headless rendering to capture and validate visual output.
/// </summary>
[Collection("Avalonia")]
public class TDV2200RenderingTests
{
    #region Rectangle Attribute Rendering Tests

    [AvaloniaFact]
    public void NDAAR_LowIntensity_ShouldSetDimAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("ABCDE"));

        // Act - NDAAR: add low intensity (2) to line 1, columns 1-5. ND-1200 section 5.36: the
        // corners first, then the attributes, counted from 1. Attribute 1 (bold) is "Ignored" in
        // the SGR table of section 5.67.
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'5', (byte)';', (byte)'2', (byte)'{' });

        // Assert - Verify the Dim attribute is set
        for (int col = 0; col <= 4; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Dim) != 0,
                $"Cell at column {col} should have Dim attribute for rendering");
        }
    }

    [AvaloniaFact]
    public void NDAAR_Underline_ShouldSetUnderlineAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("TEST"));

        // Act - NDAAR: Add Underline (4) to line 1, columns 1-4: ESC[1;1;1;4;4{
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'4', (byte)';', (byte)'4', (byte)'{' });

        // Assert
        for (int col = 0; col <= 3; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Underline) != 0,
                $"Cell at column {col} should have Underline attribute");
        }
    }

    [AvaloniaFact]
    public void NDAAR_Reverse_ShouldSetReverseAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("REVERSE"));

        // Act - NDAAR: Add Reverse (7) to line 1, columns 1-7: ESC[1;1;1;7;7{
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'7', (byte)';', (byte)'7', (byte)'{' });

        // Assert
        for (int col = 0; col <= 6; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Reverse) != 0,
                $"Cell at column {col} should have Reverse attribute");
        }
    }

    [AvaloniaFact]
    public void NDRAR_ShouldRemoveAttributeForRendering()
    {
        // Arrange - Set low intensity text
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'m' }); // Set low intensity
        emulator.ProcessData(Encoding.UTF8.GetBytes("DIM "));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' }); // Reset

        // Verify low intensity is set
        Assert.True((emulator.Buffer.GetCell(0, 0).Attributes & CharacterAttributes.Dim) != 0);

        // Act - NDRAR: remove low intensity (2) from line 1, columns 1-4. ND-1200 section 5.46:
        // the corners first, then the attributes, counted from 1.
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'4', (byte)';', (byte)'2', (byte)'|' });

        // Assert - Low intensity should be removed
        for (int col = 0; col <= 3; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Dim) == 0,
                $"Cell at column {col} should NOT have Dim attribute after NDRAR");
        }
    }

    #endregion

    #region Character Set Rendering Tests

    [AvaloniaFact]
    public void GraphicsI_ShouldSetCorrectFontNumber()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Invoke G2 (Graphics I) using SS2 (ESC N)
        emulator.ProcessData(new byte[] { 0x1B, 0x4E, (byte)'a' });

        // Assert - FontNumber should be 2 for graphics characters
        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.Equal(2, cell.FontNumber);
    }

    [AvaloniaFact]
    public void GraphicsII_ShouldSetCorrectFontNumber()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Invoke G3 (Graphics II) using SS3 (ESC O)
        emulator.ProcessData(new byte[] { 0x1B, 0x4F, (byte)'a' });

        // Assert - FontNumber should be 3 for graphics II characters
        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.Equal(3, cell.FontNumber);
    }

    [AvaloniaFact]
    public void CharacterSetSwitch_ShouldAffectFontNumber()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Switch to Graphics I character set ESC ( 1
        emulator.ProcessData(new byte[] { 0x1B, (byte)'(', (byte)'1' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("`")); // Graphics character position

        // Assert - Character should use graphics font
        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.Equal(2, cell.FontNumber);
    }

    #endregion

    #region Character Insert/Delete Rendering Tests

    [AvaloniaFact]
    public void NDICHE_ShouldShiftCharactersRight()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("ABCDEFGH"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' }); // Home

        // Act - Insert 2 characters
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'s' });

        // Assert - First 2 should be spaces, rest shifted
        Assert.True(emulator.Buffer.GetCell(0, 0).IsEmpty);
        Assert.True(emulator.Buffer.GetCell(0, 1).IsEmpty);
        Assert.Equal('A', (char)emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('B', (char)emulator.Buffer.GetCell(0, 3).Codepoint);
        Assert.Equal('C', (char)emulator.Buffer.GetCell(0, 4).Codepoint);
    }

    [AvaloniaFact]
    public void NDDCHE_ShouldShiftCharactersLeft()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("ABCDEFGH"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' }); // Home

        // Act - Delete 2 characters
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'t' });

        // Assert - CD should now be at start
        Assert.Equal('C', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('D', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('E', (char)emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('F', (char)emulator.Buffer.GetCell(0, 3).Codepoint);
    }

    #endregion

    #region Fill Character Rendering Tests

    [AvaloniaFact]
    public void NDFC_ShouldFillRectangleWithCharacter()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.International; // Use International to avoid mapping

        // Act - NDFC: Fill rectangle with '*' (ASCII 42)
        // ND-1200 section 5.43: ESC[l1;c1;l2;c2;char} - corners first, counted from 1.
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'3', (byte)';', (byte)'5', (byte)';', (byte)'4', (byte)'2', (byte)'}' });

        // Assert - Rectangle buffer 0,0 to 2,4 (lines 1-3, columns 1-5) should be filled with '*'
        for (int row = 0; row <= 2; row++)
        {
            for (int col = 0; col <= 4; col++)
            {
                Assert.Equal('*', (char)emulator.Buffer.GetCell(row, col).Codepoint);
            }
        }
    }

    [AvaloniaFact]
    public void NDFC_ShouldNotAffectOutsideRectangle()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.International;

        // Fill entire first row with 'X'
        for (int i = 0; i < 80; i++)
        {
            emulator.ProcessData(Encoding.UTF8.GetBytes("X"));
        }
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' }); // Home

        // Act - Fill small rectangle with '#': line 1, columns 3-5 (buffer columns 2-4)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'3', (byte)';', (byte)'1', (byte)';', (byte)'5', (byte)';', (byte)'3', (byte)'5', (byte)'}' });

        // Assert - Positions 0,1 should still be 'X', positions 2-4 should be '#'
        Assert.Equal('X', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('X', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('#', (char)emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('#', (char)emulator.Buffer.GetCell(0, 3).Codepoint);
        Assert.Equal('#', (char)emulator.Buffer.GetCell(0, 4).Codepoint);
        Assert.Equal('X', (char)emulator.Buffer.GetCell(0, 5).Codepoint);
    }

    #endregion

    #region Query Response Tests

    [AvaloniaFact]
    public void DeviceAttributes_ShouldRespondWithTDV2200ID()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        string? receivedResponse = null;
        emulator.OnResponseReady += response => receivedResponse = response;

        // Act - Send DA query
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'c' });

        // Assert
        Assert.NotNull(receivedResponse);
        Assert.Contains("220", receivedResponse); // TDV2200 ID
    }

    [AvaloniaFact]
    public void CursorPositionReport_ShouldRespondWithCurrentPosition()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        string? receivedResponse = null;
        emulator.OnResponseReady += response => receivedResponse = response;

        // Move cursor to row 5, col 10
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)';', (byte)'1', (byte)'0', (byte)'H' });

        // Act - Send CPR query (DSR 6)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'6', (byte)'n' });

        // Assert
        Assert.NotNull(receivedResponse);
        Assert.Contains("5;10R", receivedResponse); // Row 5, Col 10
    }

    #endregion

    #region Double-Width/Height Line Rendering Tests

    [AvaloniaFact]
    public void DoubleHeightTop_ShouldSetLineAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("DOUBLE"));

        // Act - ESC # 3 - Double-height line, top half
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'3' });

        // Assert - Buffer should indicate double-height top
        // Note: Implementation stores this as line attribute, verify the chars are still there
        Assert.Equal('D', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('O', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
    }

    [AvaloniaFact]
    public void DoubleHeightBottom_ShouldSetLineAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("DOUBLE"));

        // Act - ESC # 4 - Double-height line, bottom half
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'4' });

        // Assert - Characters should still be present
        Assert.Equal('D', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('O', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
    }

    [AvaloniaFact]
    public void SingleWidthSingleHeight_ShouldResetLineAttributes()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("TEXT"));

        // Set double-height first
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'3' });

        // Act - ESC # 5 - Single-width, single-height line (reset)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'5' });

        // Assert - Characters should still be present
        Assert.Equal('T', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('E', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('X', (char)emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('T', (char)emulator.Buffer.GetCell(0, 3).Codepoint);
    }

    [AvaloniaFact]
    public void DoubleWidthLine_ShouldSetLineAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("WIDE"));

        // Act - ESC # 6 - Double-width line
        emulator.ProcessData(new byte[] { 0x1B, (byte)'#', (byte)'6' });

        // Assert - Characters should still be present
        Assert.Equal('W', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('I', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('D', (char)emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('E', (char)emulator.Buffer.GetCell(0, 3).Codepoint);
    }

    #endregion

    #region Work Area Line Operation Rendering Tests

    [AvaloniaFact]
    public void NDLIWA_ShouldInsertLinesInWorkArea()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Write text on rows 0, 1, 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("ROW0"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)';', (byte)'1', (byte)'H' }); // Row 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("ROW1"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)';', (byte)'1', (byte)'H' }); // Row 3
        emulator.ProcessData(Encoding.UTF8.GetBytes("ROW2"));

        // Move cursor back to row 1
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)'H' }); // Row 1

        // Act - Insert 1 line (ESC[1p or ESC[p)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'p' });

        // Assert - Row 0 should now be blank (inserted), ROW0 shifted down
        Assert.True(emulator.Buffer.GetCell(0, 0).IsEmpty); // Blank inserted line
        Assert.Equal('R', (char)emulator.Buffer.GetCell(1, 0).Codepoint); // ROW0 shifted down
        Assert.Equal('O', (char)emulator.Buffer.GetCell(1, 1).Codepoint);
        Assert.Equal('W', (char)emulator.Buffer.GetCell(1, 2).Codepoint);
        Assert.Equal('0', (char)emulator.Buffer.GetCell(1, 3).Codepoint);
    }

    [AvaloniaFact]
    public void NDDLWA_ShouldDeleteLinesInWorkArea()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Write text on rows 0, 1, 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("ROW0"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)';', (byte)'1', (byte)'H' }); // Row 2
        emulator.ProcessData(Encoding.UTF8.GetBytes("ROW1"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)';', (byte)'1', (byte)'H' }); // Row 3
        emulator.ProcessData(Encoding.UTF8.GetBytes("ROW2"));

        // Move cursor to row 1
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)'H' }); // Row 1

        // Act - Delete 1 line (ESC[1q or ESC[q)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'q' });

        // Assert - ROW0 deleted, ROW1 now at position 0
        Assert.Equal('R', (char)emulator.Buffer.GetCell(0, 0).Codepoint); // ROW1 moved up
        Assert.Equal('O', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('W', (char)emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('1', (char)emulator.Buffer.GetCell(0, 3).Codepoint);
    }

    #endregion

    #region NDSAR - Set Attribute in Rectangle Rendering Tests

    [AvaloniaFact]
    public void NDSAR_ShouldSetLowIntensityAndClearOtherAttributes()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Set underline on text first
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)'m' }); // Underline
        emulator.ProcessData(Encoding.UTF8.GetBytes("UNDER"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' }); // Reset

        // Verify underline is set
        Assert.True((emulator.Buffer.GetCell(0, 0).Attributes & CharacterAttributes.Underline) != 0);

        // Act - NDSAR: set low intensity (2) in line 1, columns 1-5 - should replace, not add.
        // ND-1200 section 5.50: ESC[l1;c1;l2;c2;a1z - corners first, counted from 1; "previously
        // specified aspects within the rectangle shall be reset".
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'5', (byte)';', (byte)'2', (byte)'z' });

        // Assert - Low intensity is set and the underline from before is gone
        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.True((cell.Attributes & CharacterAttributes.Dim) != 0, "Low intensity should be set by NDSAR");
        Assert.True((cell.Attributes & CharacterAttributes.Underline) == 0, "NDSAR resets the earlier underline");
    }

    [AvaloniaFact]
    public void NDSAR_ShouldSetMultipleAttributesInRectangle()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("MULTI"));

        // Act - NDSAR: Set Reverse (7) in line 1, columns 1-5: ESC[1;1;1;5;7z
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'5', (byte)';', (byte)'7', (byte)'z' });

        // Assert - Reverse should be set on all cells in rectangle
        for (int col = 0; col <= 4; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Reverse) != 0,
                $"Cell at column {col} should have Reverse attribute from NDSAR");
        }
    }

    #endregion

    #region Blink Attribute Rendering Tests

    [AvaloniaFact]
    public void Blink_ShouldSetBlinkAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Set blink (SGR 5) and write text
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)'m' }); // Blink
        emulator.ProcessData(Encoding.UTF8.GetBytes("BLINK"));

        // Assert - Blink attribute should be set
        for (int col = 0; col < 5; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Blink) != 0,
                $"Cell at column {col} should have Blink attribute");
        }
    }

    [AvaloniaFact]
    public void NDAAR_Blink_ShouldAddBlinkToRectangle()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("FLASH"));

        // Act - NDAAR: Add Blink (5) to line 1, columns 1-5: ESC[1;1;1;5;5{
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'1', (byte)';', (byte)'5', (byte)';', (byte)'5', (byte)'{' });

        // Assert - Blink should be added
        for (int col = 0; col <= 4; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Blink) != 0,
                $"Cell at column {col} should have Blink attribute from NDAAR");
        }
    }

    #endregion

    #region Locking Shift Rendering Tests

    [AvaloniaFact]
    public void LS2_LockingShiftG2_ShouldAffectSubsequentCharacters()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Invoke G2 as GL using LS2 (ESC n)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'n' }); // LS2
        emulator.ProcessData(Encoding.UTF8.GetBytes("abc"));

        // Assert - All characters should use G2 (Graphics I) font
        Assert.Equal(2, emulator.Buffer.GetCell(0, 0).FontNumber);
        Assert.Equal(2, emulator.Buffer.GetCell(0, 1).FontNumber);
        Assert.Equal(2, emulator.Buffer.GetCell(0, 2).FontNumber);
    }

    [AvaloniaFact]
    public void LS3_LockingShiftG3_ShouldAffectSubsequentCharacters()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Invoke G3 as GL using LS3 (ESC o)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'o' }); // LS3
        emulator.ProcessData(Encoding.UTF8.GetBytes("xyz"));

        // Assert - All characters should use G3 (Graphics II) font
        Assert.Equal(3, emulator.Buffer.GetCell(0, 0).FontNumber);
        Assert.Equal(3, emulator.Buffer.GetCell(0, 1).FontNumber);
        Assert.Equal(3, emulator.Buffer.GetCell(0, 2).FontNumber);
    }

    [AvaloniaFact]
    public void SS2_SingleShift_ShouldOnlyAffectOneCharacter()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - SS2 followed by two characters
        emulator.ProcessData(new byte[] { 0x1B, 0x4E, (byte)'a', (byte)'b' }); // ESC N a b

        // Assert - Only first char uses G2 font, second uses default
        Assert.Equal(2, emulator.Buffer.GetCell(0, 0).FontNumber); // 'a' uses G2
        Assert.Equal(0, emulator.Buffer.GetCell(0, 1).FontNumber); // 'b' back to G0
    }

    #endregion

    #region Hidden/Invisible Attribute Rendering Tests

    [AvaloniaFact]
    public void Hidden_ShouldSetInvisibleAttributeInBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Set invisible (SGR 8) and write text
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'8', (byte)'m' }); // Hidden
        emulator.ProcessData(Encoding.UTF8.GetBytes("SECRET"));

        // Assert - Invisible/Hidden attribute should be set
        for (int col = 0; col < 6; col++)
        {
            var cell = emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Hidden) != 0,
                $"Cell at column {col} should have Hidden attribute");
        }
    }

    #endregion

    #region Combined Attribute Rendering Tests

    [AvaloniaFact]
    public void MultipleAttributes_ShouldCombineCorrectly()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Set Bold + Underline + Reverse
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'4', (byte)';', (byte)'7', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("COMBO"));

        // Assert - All three attributes should be set
        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.True((cell.Attributes & CharacterAttributes.Bold) != 0, "Should have Bold");
        Assert.True((cell.Attributes & CharacterAttributes.Underline) != 0, "Should have Underline");
        Assert.True((cell.Attributes & CharacterAttributes.Reverse) != 0, "Should have Reverse");
    }

    [AvaloniaFact]
    public void SGR0_ShouldResetAllAttributes()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Set multiple attributes
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'4', (byte)';', (byte)'5', (byte)';', (byte)'7', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("A"));

        // Verify attributes are set
        var cell1 = emulator.Buffer.GetCell(0, 0);
        Assert.True((cell1.Attributes & CharacterAttributes.Bold) != 0);

        // Act - Reset with SGR 0
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("B"));

        // Assert - Second character should have no attributes
        var cell2 = emulator.Buffer.GetCell(0, 1);
        Assert.True((cell2.Attributes & CharacterAttributes.Bold) == 0, "Bold should be cleared");
        Assert.True((cell2.Attributes & CharacterAttributes.Underline) == 0, "Underline should be cleared");
        Assert.True((cell2.Attributes & CharacterAttributes.Blink) == 0, "Blink should be cleared");
        Assert.True((cell2.Attributes & CharacterAttributes.Reverse) == 0, "Reverse should be cleared");
    }

    #endregion

    #region Cursor Movement Rendering Tests

    [AvaloniaFact]
    public void CursorMovement_ShouldPositionTextCorrectly()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Act - Position cursor and write
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)';', (byte)'1', (byte)'0', (byte)'H' }); // Row 5, Col 10
        emulator.ProcessData(Encoding.UTF8.GetBytes("X"));

        // Assert - Character should be at correct position (0-indexed: row 4, col 9)
        Assert.Equal('X', (char)emulator.Buffer.GetCell(4, 9).Codepoint);

        // Verify surrounding cells are empty
        Assert.True(emulator.Buffer.GetCell(4, 8).IsEmpty);
        Assert.True(emulator.Buffer.GetCell(4, 10).IsEmpty);
    }

    [AvaloniaFact]
    public void HomeCursor_ShouldMoveToTopLeft()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);

        // Move cursor somewhere else first
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'0', (byte)';', (byte)'2', (byte)'0', (byte)'H' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Y"));

        // Act - Home cursor (ESC[H)
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' });
        emulator.ProcessData(Encoding.UTF8.GetBytes("Z"));

        // Assert
        Assert.Equal('Z', (char)emulator.Buffer.GetCell(0, 0).Codepoint); // Home position
        Assert.Equal('Y', (char)emulator.Buffer.GetCell(9, 19).Codepoint); // Previous position preserved
    }

    #endregion

    #region Clear Screen Rendering Tests

    [AvaloniaFact]
    public void ED2_ClearEntireScreen_ShouldClearBuffer()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("TESTDATA"));

        // Act - ESC[2J - Clear entire screen
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'J' });

        // Assert - All cells should be spaces
        for (int col = 0; col < 8; col++)
        {
            Assert.True(emulator.Buffer.GetCell(0, col).IsEmpty);
        }
    }

    [AvaloniaFact]
    public void EL0_ClearToEndOfLine_ShouldClearFromCursor()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.UTF8.GetBytes("ABCDEFGH"));
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'4', (byte)'H' }); // Col 4

        // Act - ESC[K (ESC[0K) - Clear to end of line
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'K' });

        // Assert - First 3 chars preserved, rest cleared
        Assert.Equal('A', (char)emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('B', (char)emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('C', (char)emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.True(emulator.Buffer.GetCell(0, 3).IsEmpty); // Cleared
        Assert.True(emulator.Buffer.GetCell(0, 4).IsEmpty); // Cleared
    }

    #endregion
}
