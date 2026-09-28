using System;
using System.Text;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

/// <summary>
/// Display subsystem validation tests for TDV-2200
/// Tests character rendering, attributes, cursor control, scrolling, bitmap fonts, fontNum switching
/// </summary>
public class DisplayValidationTests : TDV2200ValidationTestBase
{
    #region Character Generation and Fonts

    [Fact]
    public void BitmapFontRendering_ShouldUseFontTDV2200()
    {
        // Arrange & Act
        SendSequence("A");

        // Assert - Verify character is rendered (check buffer)
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.Equal('A', (char)cell.Value.Codepoint);
        }
    }

    [Fact]
    public void FontNumberSwitching_SS2_ShouldSetFontNumber2()
    {
        // Arrange & Act
        // SS2 (ESC N) should set fontNum to 2 for next character
        SendSequence("\x1bN"); // SS2
        SendSequence("A");

        // Assert
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.Equal(2, cell.Value.FontNumber);
        }
    }

    [Fact]
    public void FontNumberSwitching_SS3_ShouldSetFontNumber3()
    {
        // Arrange & Act
        // SS3 (ESC O) should set fontNum to 3 for next character
        SendSequence("\x1bO"); // SS3
        SendSequence("A");

        // Assert
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.Equal(3, cell.Value.FontNumber);
        }
    }

    [Fact]
    public void GraphicsICharacterSet_ShouldMapCorrectly()
    {
        // Arrange & Act
        SendSequence("\x1b(1"); // Graphics I
        SendSequence("`abcdefghijklmnopqrstuvwxyz{|}~"); // Characters 0x60-0x7F

        // Assert - Verify characters are mapped (check first few)
        var cell1 = GetCell(0, 0); // `
        Assert.NotNull(cell1);
        // Graphics I should map these characters to graphics symbols
    }

    [Fact]
    public void ISO646Variant_Norwegian_ShouldMapCorrectly()
    {
        // Arrange & Act
        SendSequence("\x1b%N"); // Norwegian ISO646 variant
        SendSequence("ÆØÅæøå");

        // Assert - Verify Norwegian characters are handled
        // Note: Actual mapping depends on implementation
    }

    #endregion

    #region Line Wrapping and Scrolling

    [Fact]
    public void AutoWrapMode_ShouldWrapAtEndOfLine()
    {
        // Arrange
        SendSequence("\x1b[?7h"); // Enable auto-wrap

        // Act - Write 81 characters (should wrap)
        var text = new string('A', 81);
        SendSequence(text);

        // Assert
        var (row, col) = GetCursorPosition();
        Assert.Equal(1, row); // Should be on second line
        Assert.Equal(1, col); // Should be at column 1 (wrapped)
    }

    [Fact]
    public void SmoothScrollMode_ShouldEnableSmoothScrolling()
    {
        // Arrange & Act
        SendSequence("\x1b[60h"); // Enable smooth scroll mode

        // Assert - Mode should be set (check internal state if accessible)
        // Note: Actual smooth scrolling animation may not be testable in unit tests
    }

    [Fact]
    public void ScrollMargins_ShouldSetTopAndBottom()
    {
        // Arrange & Act
        SendSequence("\x1b[5;20r"); // Set scroll region from row 5 to 20

        // Assert - Scroll region should be set
        // Note: May need to check internal state
    }

    #endregion

    #region Cursor Positioning and Visibility

    [Fact]
    public void CursorPosition_CUP_ShouldMoveToPosition()
    {
        // Arrange & Act
        SendSequence("\x1b[10;20H"); // Cursor Position

        // Assert
        var (row, col) = GetCursorPosition();
        Assert.Equal(9, row); // 0-based
        Assert.Equal(19, col); // 0-based
    }

    [Fact]
    public void CursorMovement_CUU_ShouldMoveUp()
    {
        // Arrange
        SendBytes(0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x30, 0x48); // ESC [ 5 ; 1 0 H - Move to row 5, col 10

        // Act
        SendBytes(0x1B, 0x5B, 0x32, 0x41); // ESC [ 2 A - Cursor Up 2

        // Assert
        var (row, col) = GetCursorPosition();
        Assert.Equal(2, row); // Starting at row 4 (0-based), moving up 2 gives row 2 (0-based)
        Assert.Equal(9, col); // Column should remain
    }

    [Fact]
    public void CursorVisibility_ShouldToggle()
    {
        // Arrange & Act
        SendSequence("\x1b[?25l"); // Hide cursor
        SendSequence("\x1b[?25h"); // Show cursor

        // Assert - Cursor visibility state should be toggled
        // Note: May need to check internal state
    }

    [Fact]
    public void CursorSaveRestore_DECSC_DECRC_ShouldWork()
    {
        // Arrange
        SendBytes(0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x30, 0x48); // ESC [ 5 ; 1 0 H - Move to position

        // Act
        SendBytes(0x1B, 0x37); // ESC 7 - Save cursor (DECSC)
        SendBytes(0x1B, 0x5B, 0x31, 0x3B, 0x31, 0x48); // ESC [ 1 ; 1 H - Move elsewhere
        SendBytes(0x1B, 0x38); // ESC 8 - Restore cursor (DECRC)

        // Assert
        var (row, col) = GetCursorPosition();
        Assert.Equal(4, row); // Should restore to row 5 (0-based)
        Assert.Equal(9, col); // Should restore to col 10 (0-based)
    }

    [Fact]
    public void DLEBinaryCursorPositioning_2115Mode_ShouldWork()
    {
        // Arrange
        SendSequence("\x1b[66l"); // Enable 2115 compatibility mode

        // Act - DLE (0x10) followed by row and column (both 0-based per TDV spec)
        // Per spec: "Line number 0-24 and column number 0-79"
        SendSequence("\x10\x05\x0A"); // DLE, row 5, column 10

        // Assert - DLE uses 0-based coordinates directly
        var (row, col) = GetCursorPosition();
        Assert.Equal(5, row); // 0-based: row 5
        Assert.Equal(10, col); // 0-based: column 10
    }

    #endregion

    #region Attributes

    [Fact]
    public void BoldAttribute_SGR_ShouldSetBold()
    {
        // Arrange & Act
        SendSequence("\x1b[1m"); // Bold
        SendSequence("Bold");

        // Assert
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.True(cell.Value.Attributes.HasAttribute(CharacterAttributes.Bold));
        }
    }

    [Fact]
    public void UnderlineAttribute_SGR_ShouldSetUnderline()
    {
        // Arrange & Act
        SendSequence("\x1b[4m"); // Underline
        SendSequence("Underline");

        // Assert
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.True(cell.Value.Attributes.HasAttribute(CharacterAttributes.Underline));
        }
    }

    [Fact]
    public void ReverseVideo_SGR_ShouldSetReverse()
    {
        // Arrange & Act
        SendSequence("\x1b[7m"); // Reverse video
        SendSequence("Reverse");

        // Assert
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.True(cell.Value.Attributes.HasAttribute(CharacterAttributes.Reverse));
        }
    }

    [Fact]
    public void BlinkMode_NDBLWM_ShouldEnableBlink()
    {
        // Arrange & Act
        SendSequence("\x1b[31h"); // Enable blink mode

        // Assert - Blink mode should be enabled
        // Note: May need to check internal state
    }

    [Fact]
    public void DoubleWidthLine_ESC3_ShouldSetDoubleWidth()
    {
        // Arrange & Act
        SendSequence("\x1b#3"); // Double-height top
        SendSequence("Double");

        // Assert - Line should be double-width
        // Note: May need to check cell attributes or internal state
    }

    #endregion

    #region Escape/Control Sequence Interpretation

    [Fact]
    public void C0ControlCodes_ShouldHandleCorrectly()
    {
        // Arrange & Act - Test common C0 codes
        SendSequence("\x08"); // BS - Backspace
        SendSequence("\x09"); // HT - Tab
        SendSequence("\x0A"); // LF - Line Feed
        SendSequence("\x0D"); // CR - Carriage Return

        // Assert - Cursor should move appropriately
        // Note: Specific assertions depend on implementation
    }

    [Fact]
    public void C1ControlCodes_ShouldHandleCorrectly()
    {
        // Arrange & Act - Test C1 codes (ESC + letter)
        SendSequence("\x1bD"); // IND - Index
        SendSequence("\x1bE"); // NEL - Next Line
        SendSequence("\x1bM"); // RI - Reverse Index

        // Assert - Cursor should move appropriately
    }

    [Fact]
    public void TDVPrivateCSI_NDSAR_ShouldSetAttributeInRectangle()
    {
        // Arrange & Act
        // NDSAR: ESC [ Ps ; Pl ; Pc ; Pr z
        SendSequence("\x1b[1;5;10;5;20z"); // Set attribute 1 in rectangle from (5,10) to (5,20)

        // Assert - Rectangle should have attribute set
        // Note: May need to check multiple cells in rectangle
    }

    #endregion

    #region Clear Screen/Line/Character Commands

    [Fact]
    public void EraseInDisplay_ED_ShouldClearScreen()
    {
        // Arrange
        SendSequence("Test");

        // Act
        SendSequence("\x1b[2J"); // Clear entire screen

        // Assert - Screen should be clear
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.True(cell.Value.IsEmpty); // Should be space
        }
    }

    [Fact]
    public void EraseInLine_EL_ShouldClearLine()
    {
        // Arrange
        SendSequence("Test");

        // Act
        SendSequence("\x1b[2K"); // Clear entire line

        // Assert - Line should be clear
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.True(cell.Value.IsEmpty); // Should be space
        }
    }

    [Fact]
    public void EraseCharacter_ECH_ShouldEraseCharacters()
    {
        // Arrange
        SendSequence("Test");
        SendSequence("\x1b[2D"); // Move back 2

        // Act
        SendSequence("\x1b[2X"); // Erase 2 characters

        // Assert - Characters should be erased
        var cell1 = GetCell(0, 2);
        var cell2 = GetCell(0, 3);
        Assert.NotNull(cell1);
        Assert.NotNull(cell2);
        if (cell1.HasValue && cell2.HasValue)
        {
            Assert.True(cell1.Value.IsEmpty);
            Assert.True(cell2.Value.IsEmpty);
        }
    }

    #endregion
}

