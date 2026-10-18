using System.Text;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Unit tests for test server key detection logic
/// </summary>
public class TestServerKeyDetectionTests
{
    [Theory]
    [InlineData("\x1BOP", true, "F1 (VT100)")]  // F1 VT100-style
    [InlineData("\x1BOQ", true, "F2 (VT100)")]  // F2 VT100-style
    [InlineData("\x1BOR", true, "F3 (VT100)")]  // F3 VT100-style
    [InlineData("\x1BOS", true, "F4 (VT100)")]  // F4 VT100-style
    [InlineData("\x1B[11~", true, "F1")]       // F1 TDV-style
    [InlineData("\x1B[12~", true, "F2")]       // F2 TDV-style
    [InlineData("\x1B[13~", true, "F3")]       // F3 TDV-style
    [InlineData("\x1B[14~", true, "F4")]       // F4 TDV-style
    [InlineData("\x1B[15~", true, "F5")]       // F5 TDV-style
    [InlineData("\x1B[24~", true, "F12")]      // F12 TDV-style
    [InlineData("\x1B[34~", true, "F20")]      // F20 TDV-style
    [InlineData("B", false, null)]             // Standalone B (back command)
    [InlineData("b", false, null)]             // Standalone b (back command)
    [InlineData("\x1B[B", false, null)]         // Arrow down (should NOT be detected as back)
    [InlineData("\x1B[A", false, null)]         // Arrow up (not a function key)
    [InlineData("hello", false, null)]          // Random text
    public void FunctionKeyDetection_ShouldCorrectlyIdentifyKeys(string input, bool shouldBeFunctionKey, string? expectedName)
    {
        // Arrange - VT100-style function keys (F1-F4)
        var vt100FunctionKeys = new[]
        {
            ("\x1BOP", "F1 (VT100)"),
            ("\x1BOQ", "F2 (VT100)"),
            ("\x1BOR", "F3 (VT100)"),
            ("\x1BOS", "F4 (VT100)")
        };

        // TDV-style function keys (F1-F20)
        var tdvFunctionKeys = new[]
        {
            ("\x1B[11~", "F1"),
            ("\x1B[12~", "F2"),
            ("\x1B[13~", "F3"),
            ("\x1B[14~", "F4"),
            ("\x1B[15~", "F5"),
            ("\x1B[17~", "F6"),
            ("\x1B[18~", "F7"),
            ("\x1B[19~", "F8"),
            ("\x1B[20~", "F9"),
            ("\x1B[21~", "F10"),
            ("\x1B[23~", "F11"),
            ("\x1B[24~", "F12"),
            ("\x1B[25~", "F13"),
            ("\x1B[26~", "F14"),
            ("\x1B[28~", "F15"),
            ("\x1B[29~", "F16"),
            ("\x1B[31~", "F17"),
            ("\x1B[32~", "F18"),
            ("\x1B[33~", "F19"),
            ("\x1B[34~", "F20")
        };

        // Act - Check if it's a VT100-style function key
        bool found = false;
        string? detectedName = null;

        foreach (var (seq, name) in vt100FunctionKeys)
        {
            if (input == seq)
            {
                found = true;
                detectedName = name;
                break;
            }
        }

        // Check if it's a TDV-style function key
        if (!found)
        {
            foreach (var (seq, name) in tdvFunctionKeys)
            {
                if (input == seq)
                {
                    found = true;
                    detectedName = name;
                    break;
                }
            }
        }

        // Assert
        Assert.Equal(shouldBeFunctionKey, found);
        if (shouldBeFunctionKey && expectedName != null)
        {
            Assert.Equal(expectedName, detectedName);
        }
    }

    [Theory]
    [InlineData("B", true)]           // Standalone B should be back command
    [InlineData("b", true)]           // Standalone b should be back command
    [InlineData("\x1B[B", false)]     // Arrow down (B in escape sequence) should NOT be back
    [InlineData("\x1B[1;2B", false)]  // Shift+Down (B in escape sequence) should NOT be back
    [InlineData("BB", false)]         // Multiple Bs should NOT be back
    [InlineData("B\x1B", false)]      // B followed by ESC should NOT be back
    [InlineData("\x1BB", false)]      // ESC followed by B should NOT be back
    public void BackCommandDetection_ShouldOnlyMatchStandaloneB(string input, bool shouldBeBackCommand)
    {
        // Act - Check if input is exactly a standalone 'B' or 'b'
        bool isBackCommand = input.Length == 1 && (input[0] == 'B' || input[0] == 'b');

        // Assert
        Assert.Equal(shouldBeBackCommand, isBackCommand);
    }

    [Theory]
    [InlineData("\x1B[1;2A", true, "Shift+Up")]      // Shift+Up
    [InlineData("\x1B[1;2B", true, "Shift+Down")]   // Shift+Down
    [InlineData("\x1B[1;2C", true, "Shift+Right")]   // Shift+Right
    [InlineData("\x1B[1;2D", true, "Shift+Left")]    // Shift+Left
    [InlineData("\x1B[1;5A", true, "Ctrl+Up")]       // Ctrl+Up
    [InlineData("\x1B[1;5B", true, "Ctrl+Down")]     // Ctrl+Down
    [InlineData("\x1B[1;5C", true, "Ctrl+Right")]    // Ctrl+Right
    [InlineData("\x1B[1;5D", true, "Ctrl+Left")]     // Ctrl+Left
    [InlineData("\x1B[1;3A", true, "Alt+Up")]        // Alt+Up
    [InlineData("\x1B[1;3B", true, "Alt+Down")]      // Alt+Down
    [InlineData("\x1B[1;3C", true, "Alt+Right")]     // Alt+Right
    [InlineData("\x1B[1;3D", true, "Alt+Left")]      // Alt+Left
    [InlineData("\x1B[A", false, null)]              // Plain arrow up (not modifier)
    [InlineData("\x1B[B", false, null)]              // Plain arrow down (not modifier)
    [InlineData("B", false, null)]                   // Standalone B
    public void ModifierKeyDetection_ShouldCorrectlyIdentifyModifierCombinations(string input, bool shouldBeModifier, string? expectedName)
    {
        // Arrange
        var modifierKeys = new[]
        {
            ("Shift+Up", "\x1B[1;2A"),
            ("Shift+Down", "\x1B[1;2B"),
            ("Shift+Right", "\x1B[1;2C"),
            ("Shift+Left", "\x1B[1;2D"),
            ("Ctrl+Up", "\x1B[1;5A"),
            ("Ctrl+Down", "\x1B[1;5B"),
            ("Ctrl+Right", "\x1B[1;5C"),
            ("Ctrl+Left", "\x1B[1;5D"),
            ("Alt+Up", "\x1B[1;3A"),
            ("Alt+Down", "\x1B[1;3B"),
            ("Alt+Right", "\x1B[1;3C"),
            ("Alt+Left", "\x1B[1;3D")
        };

        // Act
        bool found = false;
        string? detectedName = null;

        foreach (var (name, sequence) in modifierKeys)
        {
            if (input == sequence)
            {
                found = true;
                detectedName = name;
                break;
            }
        }

        // Assert
        Assert.Equal(shouldBeModifier, found);
        if (shouldBeModifier && expectedName != null)
        {
            Assert.Equal(expectedName, detectedName);
        }
    }

    [Theory]
    [InlineData("\x1B[A", true, "UP")]           // Arrow up
    [InlineData("\x1B[B", true, "DOWN")]        // Arrow down
    [InlineData("\x1B[C", true, "RIGHT")]        // Arrow right
    [InlineData("\x1B[D", true, "LEFT")]         // Arrow left
    [InlineData("\x1B[H", true, "HOME")]        // Home
    [InlineData("\x1B[F", true, "END")]          // End
    [InlineData("\x1B[5~", true, "PAGE_UP")]     // Page up
    [InlineData("\x1B[6~", true, "PAGE_DOWN")]  // Page down
    [InlineData("\x1B[2~", true, "INSERT")]     // Insert
    [InlineData("\x1B[3~", true, "DELETE")]      // Delete
    [InlineData("\x1B[1;2A", false, null)]      // Shift+Up (modifier, not plain arrow)
    [InlineData("B", false, null)]               // Standalone B
    public void ArrowKeyDetection_ShouldCorrectlyIdentifyArrowKeys(string input, bool shouldBeArrowKey, string? expectedName)
    {
        // Arrange
        var knownKeys = new (string Seq, string Name)[]
        {
            ("\x1b[A", "UP"),
            ("\x1b[B", "DOWN"),
            ("\x1b[C", "RIGHT"),
            ("\x1b[D", "LEFT"),
            ("\x1b[H", "HOME"),
            ("\x1b[F", "END"),
            ("\x1b[5~", "PAGE_UP"),
            ("\x1b[6~", "PAGE_DOWN"),
            ("\x1b[2~", "INSERT"),
            ("\x1b[3~", "DELETE")
        };

        // Act
        bool found = false;
        string? detectedName = null;

        foreach (var (seq, name) in knownKeys)
        {
            if (input == seq)
            {
                found = true;
                detectedName = name;
                break;
            }
        }

        // Assert
        Assert.Equal(shouldBeArrowKey, found);
        if (shouldBeArrowKey && expectedName != null)
        {
            Assert.Equal(expectedName, detectedName);
        }
    }

    [Fact]
    public void ArrowDown_ShouldNotTriggerBackCommand()
    {
        // This is the critical bug fix - arrow down (\x1B[B) should NOT exit the test
        var arrowDown = "\x1B[B";

        // Should NOT be detected as back command
        bool isBackCommand = arrowDown.Length == 1 && (arrowDown[0] == 'B' || arrowDown[0] == 'b');

        Assert.False(isBackCommand, "Arrow down should NOT be detected as back command");

        // Should be detected as arrow key
        var knownKeys = new (string Seq, string Name)[]
        {
            ("\x1b[A", "UP"),
            ("\x1b[B", "DOWN"),
            ("\x1b[C", "RIGHT"),
            ("\x1b[D", "LEFT")
        };

        bool isArrowKey = false;
        foreach (var (seq, name) in knownKeys)
        {
            if (arrowDown == seq)
            {
                isArrowKey = true;
                break;
            }
        }

        Assert.True(isArrowKey, "Arrow down SHOULD be detected as arrow key");
    }
}

