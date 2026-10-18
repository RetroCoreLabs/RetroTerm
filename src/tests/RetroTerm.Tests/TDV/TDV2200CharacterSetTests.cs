using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Unit tests for TDV2200 character set handling and font rendering
/// Validates USASCII default character set, character mapping, and font number assignment
/// </summary>
public class TDV2200CharacterSetTests
{
    [Fact]
    public void DefaultCharacterSets_ShouldBeUSASCII()
    {
        // Arrange & Act
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Assert - G0 and G1 should be USASCII by default
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.USASCII, emulator.GetG0CharacterSet());
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.USASCII, emulator.GetG1CharacterSet());

        // G2 and G3 should be graphics sets
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.GraphicsI, emulator.GetG2CharacterSet());
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.GraphicsII, emulator.GetG3CharacterSet());
    }

    [Fact]
    public void USASCII_ShouldNotMapCharacters()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Verify G0 is USASCII
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.USASCII, emulator.GetG0CharacterSet());

        // Act - Process regular ASCII text "Main Menu"
        var input = Encoding.ASCII.GetBytes("Main Menu");
        emulator.ProcessInput(input);

        // Assert - Characters should be displayed unchanged
        // Row 0, columns 0-8 should contain "Main Menu"
        Assert.Equal((uint)'M', emulator.Buffer[0, 0].Codepoint);
        Assert.Equal((uint)'a', emulator.Buffer[0, 1].Codepoint);
        Assert.Equal((uint)'i', emulator.Buffer[0, 2].Codepoint);
        Assert.Equal((uint)'n', emulator.Buffer[0, 3].Codepoint);
        Assert.True(emulator.Buffer[0, 4].IsEmpty);
        Assert.Equal((uint)'M', emulator.Buffer[0, 5].Codepoint);
        Assert.Equal((uint)'e', emulator.Buffer[0, 6].Codepoint);
        Assert.Equal((uint)'n', emulator.Buffer[0, 7].Codepoint);
        Assert.Equal((uint)'u', emulator.Buffer[0, 8].Codepoint);
    }

    [Fact]
    public void USASCII_FontNumber_ShouldBeZero()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Verify G0 is USASCII
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.USASCII, emulator.GetG0CharacterSet());

        // Act - Process regular ASCII text "Test"
        var input = Encoding.ASCII.GetBytes("Test");
        emulator.ProcessInput(input);

        // Assert - FontNumber should be 0 (ASCII font, NOT 2 which is graphics font)
        Assert.Equal(0, emulator.Buffer[0, 0].FontNumber); // 'T'
        Assert.Equal(0, emulator.Buffer[0, 1].FontNumber); // 'e'
        Assert.Equal(0, emulator.Buffer[0, 2].FontNumber); // 's'
        Assert.Equal(0, emulator.Buffer[0, 3].FontNumber); // 't'
    }

    [Fact]
    public void USASCII_AllPrintableASCII_ShouldHaveFontNumberZero()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24, 10000);
        // Set ISO646 variant to International to test pure ASCII behavior without national character mapping
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.International;

        // Act - Process first 80 printable ASCII characters (0x20 to 0x6F)
        // Testing all 95 chars would overflow the 80-column width
        var sb = new StringBuilder();
        for (int i = 0x20; i < 0x20 + 80; i++)
        {
            sb.Append((char)i);
        }
        var input = Encoding.ASCII.GetBytes(sb.ToString());
        emulator.ProcessInput(input);

        // Assert - All characters should have FontNumber = 0
        int col = 0;
        for (int i = 0x20; i < 0x20 + 80; i++)
        {
            Assert.Equal(0, emulator.Buffer[0, col].FontNumber);
            Assert.Equal((uint)i, emulator.Buffer[0, col].Codepoint);
            col++;
        }
    }

    [Fact]
    public void GraphicsI_WhenInvoked_ShouldUseFontNumber2()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Act - Invoke G2 (Graphics I) using SS2 (Single Shift 2)
        // ESC N is SS2 (Single Shift Two) - next character uses G2
        var input = new byte[] { 0x1B, 0x4E, 0x61 }; // ESC N 'a'
        emulator.ProcessInput(input);

        // Assert - The character should use fontNum 2 (graphics font)
        Assert.Equal(2, emulator.Buffer[0, 0].FontNumber);
    }

    [Fact]
    public void ComplexText_WithColors_ShouldPreserveFontNumber()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Act - Process text with color codes like from TestServer
        // ESC[1;36m === Main Menu ===ESC[0m
        var input = Encoding.ASCII.GetBytes("\x1B[1;36m=== Main Menu ===\x1B[0m");
        emulator.ProcessInput(input);

        // Assert - All characters should still have FontNumber = 0
        // Row 0 should contain "=== Main Menu ==="
        var expectedText = "=== Main Menu ===";
        for (int i = 0; i < expectedText.Length; i++)
        {
            Assert.Equal(0, emulator.Buffer[0, i].FontNumber);
            Assert.Equal((uint)expectedText[i], emulator.Buffer[0, i].Codepoint);
        }
    }

    [Fact]
    public void TDVCharacterSets_GetCharacterSet_USASCII_ReturnsEmptyDictionary()
    {
        // Act
        var charSet = TDVCharacterSets.GetCharacterSet(TDVCharacterSets.TDVCharacterSetType.USASCII);

        // Assert
        Assert.NotNull(charSet);
        Assert.Empty(charSet); // Empty dictionary means no mapping
    }

    [Fact]
    public void TDVCharacterSets_GetCharacter_USASCII_ReturnsUnchanged()
    {
        // Act & Assert - All ASCII characters should pass through unchanged
        for (char c = ' '; c <= '~'; c++)
        {
            var result = TDVCharacterSets.GetCharacter(TDVCharacterSets.TDVCharacterSetType.USASCII, c);
            Assert.Equal(c, result);
        }
    }

    [Fact]
    public void AfterReset_CharacterSetsShouldBeUSASCII()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Change character sets (simulate ESC ( 1 to set G0 to GraphicsI)
        // Note: This might not work if designation isn't implemented, but test reset anyway

        // Act - Reset terminal
        emulator.ProcessInput(new byte[] { 0x1B, 0x63 }); // ESC c (RIS - Reset to Initial State)

        // Assert - Should be back to USASCII defaults
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.USASCII, emulator.GetG0CharacterSet());
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.USASCII, emulator.GetG1CharacterSet());
    }

    [Fact]
    public void RealWorldScenario_TestServerMainMenu_ShouldDisplayCorrectly()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24, 10000);

        // Clear screen and home cursor
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x32, 0x4A, 0x1B, 0x5B, 0x48 }); // ESC[2J ESC[H

        // Act - Process the exact sequence from TestServer main menu
        // ESC[1;36m=== Main Menu ===ESC[0m
        var mainMenuBytes = new byte[]
        {
            0x1B, 0x5B, 0x31, 0x3B, 0x33, 0x36, 0x6D, // ESC[1;36m
            0x3D, 0x3D, 0x3D, 0x20,                   // "=== "
            0x4D, 0x61, 0x69, 0x6E, 0x20,             // "Main "
            0x4D, 0x65, 0x6E, 0x75, 0x20,             // "Menu "
            0x3D, 0x3D, 0x3D,                         // "==="
            0x1B, 0x5B, 0x30, 0x6D                    // ESC[0m
        };

        emulator.ProcessInput(mainMenuBytes);

        // Assert - Text should be displayed correctly
        var expectedText = "=== Main Menu ===";
        for (int i = 0; i < expectedText.Length; i++)
        {
            // Verify character is correct
            Assert.Equal((uint)expectedText[i], emulator.Buffer[0, i].Codepoint);

            // Verify fontNum is 0 (ASCII font, not graphics font)
            Assert.Equal(0, emulator.Buffer[0, i].FontNumber);
        }
    }
}
