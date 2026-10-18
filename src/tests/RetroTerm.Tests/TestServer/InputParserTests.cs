using System.Text;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// Unit tests for InputParser state machine
/// </summary>
public class InputParserTests
{
    [Fact]
    public void SingleCharacter_ShouldReturnCharacterInput()
    {
        var parser = new InputParser();
        parser.Feed("A");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Character, result.Type);
        Assert.Equal("A", result.Value);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void MultipleCharacters_ShouldReturnEachSeparately()
    {
        var parser = new InputParser();
        parser.Feed("ABC");

        Assert.True(parser.TryGetNext(out var a));
        Assert.Equal("A", a.Value);

        Assert.True(parser.TryGetNext(out var b));
        Assert.Equal("B", b.Value);

        Assert.True(parser.TryGetNext(out var c));
        Assert.Equal("C", c.Value);

        Assert.False(parser.TryGetNext(out _));
    }

    [Fact]
    public void Enter_CR_ShouldReturnEnterInput()
    {
        var parser = new InputParser();
        parser.Feed("\r");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Enter, result.Type);
    }

    [Fact]
    public void Enter_LF_ShouldReturnEnterInput()
    {
        var parser = new InputParser();
        parser.Feed("\n");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Enter, result.Type);
    }

    [Fact]
    public void ArrowUp_SingleRead_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[A");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1B[A", result.Value);
        Assert.Equal("VT100 Up", result.Name);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ArrowDown_SingleRead_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[B");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("VT100 Down", result.Name);
    }

    [Fact]
    public void ArrowUp_FragmentedRead_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();

        // First fragment: ESC
        parser.Feed("\x1B");
        Assert.False(parser.TryGetNext(out _)); // Not complete yet

        // Second fragment: [
        parser.Feed("[");
        Assert.False(parser.TryGetNext(out _)); // Not complete yet

        // Third fragment: A (final byte)
        parser.Feed("A");
        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1B[A", result.Value);
        Assert.Equal("VT100 Up", result.Name);
    }

    [Fact]
    public void F5_SingleRead_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[15~");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1B[15~", result.Value);
        Assert.Equal("VT220 F5", result.Name);
    }

    [Fact]
    public void F5_FragmentedRead_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();

        // Fragment 1: ESC [
        parser.Feed("\x1B[");
        Assert.False(parser.TryGetNext(out _));

        // Fragment 2: 15
        parser.Feed("15");
        Assert.False(parser.TryGetNext(out _));

        // Fragment 3: ~ (final byte)
        parser.Feed("~");
        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal("VT220 F5", result.Name);
    }

    [Fact]
    public void F1_SS3Style_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1BOP");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1BOP", result.Value);
        Assert.Equal("VT100 F1 (SS3)", result.Name);
    }

    [Fact]
    public void F1_SS3Style_Fragmented_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();

        parser.Feed("\x1B");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("O");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("P");
        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal("VT100 F1 (SS3)", result.Name);
    }

    [Fact]
    public void CharacterThenEscapeSequence_ShouldReturnBothInOrder()
    {
        var parser = new InputParser();
        parser.Feed("1\x1B[A");

        // First: character '1'
        Assert.True(parser.TryGetNext(out var char1));
        Assert.Equal(InputType.Character, char1.Type);
        Assert.Equal("1", char1.Value);

        // Second: arrow up
        Assert.True(parser.TryGetNext(out var arrow));
        Assert.Equal(InputType.EscapeSequence, arrow.Type);
        Assert.Equal("VT100 Up", arrow.Name);
    }

    [Fact]
    public void EscapeSequenceThenCharacter_ShouldReturnBothInOrder()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[A1");

        // First: arrow up
        Assert.True(parser.TryGetNext(out var arrow));
        Assert.Equal(InputType.EscapeSequence, arrow.Type);
        Assert.Equal("VT100 Up", arrow.Name);

        // Second: character '1'
        Assert.True(parser.TryGetNext(out var char1));
        Assert.Equal(InputType.Character, char1.Type);
        Assert.Equal("1", char1.Value);
    }

    [Fact]
    public void MultipleEscapeSequences_ShouldReturnAllInOrder()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[A\x1B[B\x1B[C");

        Assert.True(parser.TryGetNext(out var up));
        Assert.Equal("VT100 Up", up.Name);

        Assert.True(parser.TryGetNext(out var down));
        Assert.Equal("VT100 Down", down.Name);

        Assert.True(parser.TryGetNext(out var right));
        Assert.Equal("VT100 Right", right.Name);
    }

    [Fact]
    public void OrphanEsc_ShouldTimeoutAndReturnIncomplete()
    {
        var parser = new InputParser();
        parser.Feed("\x1B");

        // Not complete yet
        Assert.False(parser.TryGetNext(out _));
        Assert.True(parser.IsParsingEscape);

        // Simulate timeout by waiting (in real code, CheckTimeout is called)
        // For test, we manually trigger timeout check after setting time
        System.Threading.Thread.Sleep(150); // Wait > 100ms timeout
        parser.CheckTimeout();

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1B", result.Value);
        Assert.False(result.IsComplete); // Marked as incomplete
    }

    [Fact]
    public void Delete_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[3~");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("VT220 Delete", result.Name);
    }

    [Fact]
    public void Home_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[H");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("VT100 Home", result.Name);
    }

    [Fact]
    public void PageUp_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[5~");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("VT220 PageUp", result.Name);
    }

    [Fact]
    public void Backspace_0x08_ShouldReturnBackspaceInput()
    {
        var parser = new InputParser();
        parser.Feed("\x08");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Backspace, result.Type);
    }

    [Fact]
    public void Backspace_0x7F_ShouldReturnBackspaceInput()
    {
        var parser = new InputParser();
        parser.Feed("\x7F");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Backspace, result.Type);
    }

    [Fact]
    public void Tab_ShouldReturnTabInput()
    {
        var parser = new InputParser();
        parser.Feed("\t");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Tab, result.Type);
    }

    [Fact]
    public void ControlC_ShouldReturnControlInput()
    {
        var parser = new InputParser();
        parser.Feed("\x03"); // Ctrl+C

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Control, result.Type);
        Assert.Equal("\x03", result.Value);
    }

    [Fact]
    public void IsMenuCommand_ShouldBeTrueForSingleCharacter()
    {
        var parser = new InputParser();
        parser.Feed("1");

        Assert.True(parser.TryGetNext(out var result));
        Assert.True(result.IsMenuCommand);
        Assert.Equal('1', result.CommandChar);
    }

    [Fact]
    public void IsMenuCommand_ShouldBeFalseForEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[A");

        Assert.True(parser.TryGetNext(out var result));
        Assert.False(result.IsMenuCommand);
    }

    [Fact]
    public void AllFunctionKeys_F1ToF12_ShouldBeRecognized()
    {
        var fkeys = new[]
        {
            ("\x1BOP", "VT100 F1 (SS3)"), ("\x1BOQ", "VT100 F2 (SS3)"),
            ("\x1BOR", "VT100 F3 (SS3)"), ("\x1BOS", "VT100 F4 (SS3)"),
            ("\x1B[15~", "VT220 F5"), ("\x1B[17~", "VT220 F6"),
            ("\x1B[18~", "VT220 F7"), ("\x1B[19~", "VT220 F8"),
            ("\x1B[20~", "VT220 F9"), ("\x1B[21~", "VT220 F10"),
            ("\x1B[23~", "VT220 F11"), ("\x1B[24~", "VT220 F12")
        };

        for (int i = 0; i < fkeys.Length; i++)
        {
            var (seq, name) = fkeys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(InputType.EscapeSequence, result.Type);
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void ByteByByte_ShouldStillWork()
    {
        var parser = new InputParser();
        var sequence = "\x1B[15~"; // F5

        // Feed byte by byte
        for (int i = 0; i < sequence.Length; i++)
        {
            parser.Feed(sequence[i].ToString());
        }

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal("VT220 F5", result.Name);
    }

    [Fact]
    public void MixedInput_ByteByByte_ShouldWork()
    {
        var parser = new InputParser();
        var input = "1\x1B[A2"; // '1', Arrow Up, '2'

        // Feed byte by byte
        for (int i = 0; i < input.Length; i++)
        {
            parser.Feed(input[i].ToString());
        }

        Assert.True(parser.TryGetNext(out var r1));
        Assert.Equal("1", r1.Value);

        Assert.True(parser.TryGetNext(out var r2));
        Assert.Equal("VT100 Up", r2.Name);

        Assert.True(parser.TryGetNext(out var r3));
        Assert.Equal("2", r3.Value);
    }

    // =================================================================
    // TDV Extended Control Mode — CSI nn _ sequences
    // Source: TDV-2200/9 User's Guide, Section 9.1 (fresh OCR verified)
    // Format: ESC [ <digit> <digit> _ (1B 5B nn 5F)
    // =================================================================

    [Fact]
    public void CsiUnderscore_MERK_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[00_");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1B[00_", result.Value);
        Assert.Equal("TDV MERK", result.Name);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void CsiUnderscore_ShiftMERK_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[01_");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal("TDV Shift+MERK", result.Name);
    }

    [Fact]
    public void CsiUnderscore_AllEditingKeys_ShouldBeRecognized()
    {
        var editingKeys = new[]
        {
            ("\x1B[00_", "TDV MERK"),
            ("\x1B[01_", "TDV Shift+MERK"),
            ("\x1B[02_", "TDV FELT"),
            ("\x1B[03_", "TDV Shift+FELT"),
            ("\x1B[04_", "TDV AVSN"),
            ("\x1B[05_", "TDV Shift+AVSN"),
            ("\x1B[06_", "TDV SETN"),
            ("\x1B[07_", "TDV Shift+SETN"),
            ("\x1B[08_", "TDV ORD"),
            ("\x1B[09_", "TDV Shift+ORD")
        };

        for (int i = 0; i < editingKeys.Length; i++)
        {
            var (seq, name) = editingKeys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(InputType.EscapeSequence, result.Type);
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void CsiUnderscore_ActionKeys_ShouldBeRecognized()
    {
        var actionKeys = new[]
        {
            ("\x1B[10_", "TDV STRYK"),
            ("\x1B[11_", "TDV Shift+STRYK"),
            ("\x1B[12_", "TDV KOPI"),
            ("\x1B[13_", "TDV Shift+KOPI"),
            ("\x1B[14_", "TDV FLYTT"),
            ("\x1B[15_", "TDV Shift+FLYTT")
        };

        for (int i = 0; i < actionKeys.Length; i++)
        {
            var (seq, name) = actionKeys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void CsiUnderscore_SystemKeys_ShouldBeRecognized()
    {
        var systemKeys = new[]
        {
            ("\x1B[42_", "TDV FUNK"),
            ("\x1B[43_", "TDV Shift+FUNK"),
            ("\x1B[44_", "TDV SKRIV"),
            ("\x1B[45_", "TDV Shift+SKRIV"),
            ("\x1B[46_", "TDV HJELP"),
            ("\x1B[47_", "TDV Shift+HJELP"),
            ("\x1B[48_", "TDV SLUTT"),
            ("\x1B[49_", "TDV Shift+SLUTT")
        };

        for (int i = 0; i < systemKeys.Length; i++)
        {
            var (seq, name) = systemKeys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void CsiUnderscore_FunctionKeys_ShouldBeRecognized()
    {
        var fkeys = new[]
        {
            ("\x1B[50_", "TDV F1"),
            ("\x1B[51_", "TDV Shift+F1"),
            ("\x1B[52_", "TDV F2"),
            ("\x1B[53_", "TDV Shift+F2"),
            ("\x1B[54_", "TDV Ctrl+F2"),
            ("\x1B[55_", "TDV F3"),
            ("\x1B[56_", "TDV Shift+F3"),
            ("\x1B[57_", "TDV Ctrl+F3"),
            ("\x1B[58_", "TDV F4"),
            ("\x1B[59_", "TDV Shift+F4"),
            ("\x1B[60_", "TDV F5"),
            ("\x1B[61_", "TDV Shift+F5"),
            ("\x1B[62_", "TDV F6"),
            ("\x1B[63_", "TDV Shift+F6"),
            ("\x1B[64_", "TDV F7"),
            ("\x1B[65_", "TDV Shift+F7"),
            ("\x1B[66_", "TDV F8"),
            ("\x1B[67_", "TDV Shift+F8")
        };

        for (int i = 0; i < fkeys.Length; i++)
        {
            var (seq, name) = fkeys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void CsiUnderscore_NavigationAndMiscKeys_ShouldBeRecognized()
    {
        var navKeys = new[]
        {
            ("\x1B[16_", "TDV TAB+"),
            ("\x1B[17_", "TDV TAB-"),
            ("\x1B[22_", "TDV GUILLEMETS"),
            ("\x1B[28_", "TDV ROLLUP"),
            ("\x1B[29_", "TDV ROLLLEFT"),
            ("\x1B[30_", "TDV ANGRE"),
            ("\x1B[32_", "TDV ROLLDN"),
            ("\x1B[33_", "TDV ROLLRIGHT"),
            ("\x1B[34_", "TDV FIELDLEFT"),
            ("\x1B[36_", "TDV FIELDRIGHT"),
            ("\x1B[38_", "TDV TABLEFT"),
            ("\x1B[40_", "TDV TABRIGHT"),
            ("\x1B[82_", "TDV EKSP"),
            ("\x1B[83_", "TDV INNS"),
            ("\x1B[84_", "TDV MODE"),
            ("\x1B[86_", "TDV NEWPARA"),
            ("\x1B[87_", "TDV Shift+NEWPARA")
        };

        for (int i = 0; i < navKeys.Length; i++)
        {
            var (seq, name) = navKeys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void CsiUnderscore_Fragmented_ShouldWork()
    {
        var parser = new InputParser();

        // Feed byte by byte: ESC [ 4 2 _
        parser.Feed("\x1B");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("[");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("4");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("2");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("_");
        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal("TDV FUNK", result.Name);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void CsiUnderscore_FollowedByCharacter_ShouldReturnBoth()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[00_A");

        Assert.True(parser.TryGetNext(out var csi));
        Assert.Equal("TDV MERK", csi.Name);

        Assert.True(parser.TryGetNext(out var ch));
        Assert.Equal(InputType.Character, ch.Type);
        Assert.Equal("A", ch.Value);
    }

    [Fact]
    public void CsiUnderscore_MultipleSequences_ShouldReturnAllInOrder()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[00_\x1B[42_\x1B[60_");

        Assert.True(parser.TryGetNext(out var r1));
        Assert.Equal("TDV MERK", r1.Name);

        Assert.True(parser.TryGetNext(out var r2));
        Assert.Equal("TDV FUNK", r2.Name);

        Assert.True(parser.TryGetNext(out var r3));
        Assert.Equal("TDV F5", r3.Name);
    }

    [Fact]
    public void CsiUnderscore_InterruptedByEsc_ShouldEmitIncompleteAndStartNew()
    {
        var parser = new InputParser();
        // ESC [ 0 ESC [ A — interrupted CSI underscore, then arrow up
        parser.Feed("\x1B[0\x1B[A");

        Assert.True(parser.TryGetNext(out var incomplete));
        Assert.Equal(InputType.EscapeSequence, incomplete.Type);
        Assert.False(incomplete.IsComplete);

        Assert.True(parser.TryGetNext(out var arrow));
        Assert.Equal("VT100 Up", arrow.Name);
    }

    // =================================================================
    // TDV Numpad Function Mode — CSI nn _ sequences (all use _ terminator)
    // =================================================================

    [Fact]
    public void NumpadFunction_0_ShouldReturnEscapeSequence()
    {
        var parser = new InputParser();
        parser.Feed("\x1B[68_");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1B[68_", result.Value);
        Assert.Equal("TDV Numpad_0", result.Name);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void NumpadFunction_AllKeys_ShouldBeRecognized()
    {
        var numpadKeys = new[]
        {
            ("\x1B[68_", "TDV Numpad_0"),
            ("\x1B[69_", "TDV Numpad_1"),
            ("\x1B[70_", "TDV Numpad_2"),
            ("\x1B[71_", "TDV Numpad_3"),
            ("\x1B[72_", "TDV Numpad_4"),
            ("\x1B[73_", "TDV Numpad_5"),
            ("\x1B[74_", "TDV Numpad_6"),
            ("\x1B[75_", "TDV Numpad_7"),
            ("\x1B[76_", "TDV Numpad_8"),
            ("\x1B[77_", "TDV Numpad_9"),
            ("\x1B[78_", "TDV Numpad_."),
            ("\x1B[79_", "TDV Numpad_-"),
            ("\x1B[80_", "TDV Numpad_SP"),
            ("\x1B[81_", "TDV Numpad_ENTER")
        };

        for (int i = 0; i < numpadKeys.Length; i++)
        {
            var (seq, name) = numpadKeys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void NumpadFunction_Numpad7_Fragmented_ShouldWork()
    {
        var parser = new InputParser();

        parser.Feed("\x1B");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("[");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("7");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("5");
        Assert.False(parser.TryGetNext(out _));

        parser.Feed("_");
        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal("TDV Numpad_7", result.Name);
    }

    [Fact]
    public void CsiQuestionPrefix_ShouldNotBeTerminator()
    {
        // ESC [ ? 1 h — standard DEC private mode set, '?' is a parameter byte (0x3F)
        var parser = new InputParser();
        parser.Feed("\x1B[?1h");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1B[?1h", result.Value);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void CsiQuestionPrefix_DecPrivateMode_ShouldStillWork()
    {
        // ESC [ ? 40 h — DEC private mode set (Extended Control Mode ON)
        var parser = new InputParser();
        parser.Feed("\x1b[66l");

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.EscapeSequence, result.Type);
        Assert.Equal("\x1b[66l", result.Value);
        Assert.True(result.IsComplete);
    }

    // =================================================================
    // VT100 Modifier + Arrow/Home/End
    // =================================================================

    [Fact]
    public void VT100_ShiftArrows_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[1;2A", "VT100 Shift+Up"),
            ("\x1B[1;2B", "VT100 Shift+Down"),
            ("\x1B[1;2C", "VT100 Shift+Right"),
            ("\x1B[1;2D", "VT100 Shift+Left"),
            ("\x1B[1;2H", "VT100 Shift+Home"),
            ("\x1B[1;2F", "VT100 Shift+End")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void VT100_CtrlArrows_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[1;5A", "VT100 Ctrl+Up"),
            ("\x1B[1;5B", "VT100 Ctrl+Down"),
            ("\x1B[1;5C", "VT100 Ctrl+Right"),
            ("\x1B[1;5D", "VT100 Ctrl+Left"),
            ("\x1B[1;5H", "VT100 Ctrl+Home"),
            ("\x1B[1;5F", "VT100 Ctrl+End")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void VT100_AltArrows_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[1;3A", "VT100 Alt+Up"),
            ("\x1B[1;3B", "VT100 Alt+Down"),
            ("\x1B[1;3C", "VT100 Alt+Right"),
            ("\x1B[1;3D", "VT100 Alt+Left")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void VT100_CtrlShiftArrows_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[1;6A", "VT100 Ctrl+Shift+Up"),
            ("\x1B[1;6B", "VT100 Ctrl+Shift+Down"),
            ("\x1B[1;6C", "VT100 Ctrl+Shift+Right"),
            ("\x1B[1;6D", "VT100 Ctrl+Shift+Left")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    // =================================================================
    // VT220 Modifier + Navigation
    // =================================================================

    [Fact]
    public void VT220_ShiftNavigation_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[2;2~", "VT220 Shift+Insert"),
            ("\x1B[3;2~", "VT220 Shift+Delete"),
            ("\x1B[5;2~", "VT220 Shift+PageUp"),
            ("\x1B[6;2~", "VT220 Shift+PageDown")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void VT220_CtrlNavigation_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[2;5~", "VT220 Ctrl+Insert"),
            ("\x1B[3;5~", "VT220 Ctrl+Delete"),
            ("\x1B[5;5~", "VT220 Ctrl+PageUp"),
            ("\x1B[6;5~", "VT220 Ctrl+PageDown")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    // =================================================================
    // VT220 Modifier + F-keys
    // =================================================================

    [Fact]
    public void VT220_ShiftFKeys_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[11;2~", "VT220 Shift+F1"),
            ("\x1B[12;2~", "VT220 Shift+F2"),
            ("\x1B[13;2~", "VT220 Shift+F3"),
            ("\x1B[14;2~", "VT220 Shift+F4"),
            ("\x1B[15;2~", "VT220 Shift+F5"),
            ("\x1B[17;2~", "VT220 Shift+F6"),
            ("\x1B[18;2~", "VT220 Shift+F7"),
            ("\x1B[19;2~", "VT220 Shift+F8"),
            ("\x1B[20;2~", "VT220 Shift+F9"),
            ("\x1B[21;2~", "VT220 Shift+F10"),
            ("\x1B[23;2~", "VT220 Shift+F11"),
            ("\x1B[24;2~", "VT220 Shift+F12")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void VT220_CtrlFKeys_ShouldBeRecognized()
    {
        var keys = new[]
        {
            ("\x1B[11;5~", "VT220 Ctrl+F1"),
            ("\x1B[12;5~", "VT220 Ctrl+F2"),
            ("\x1B[13;5~", "VT220 Ctrl+F3"),
            ("\x1B[14;5~", "VT220 Ctrl+F4"),
            ("\x1B[15;5~", "VT220 Ctrl+F5"),
            ("\x1B[17;5~", "VT220 Ctrl+F6"),
            ("\x1B[18;5~", "VT220 Ctrl+F7"),
            ("\x1B[19;5~", "VT220 Ctrl+F8"),
            ("\x1B[20;5~", "VT220 Ctrl+F9"),
            ("\x1B[21;5~", "VT220 Ctrl+F10"),
            ("\x1B[23;5~", "VT220 Ctrl+F11"),
            ("\x1B[24;5~", "VT220 Ctrl+F12")
        };

        for (int i = 0; i < keys.Length; i++)
        {
            var (seq, name) = keys[i];
            var parser = new InputParser();
            parser.Feed(seq);

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(name, result.Name);
        }
    }

    // =================================================================
    // TDV C0 Control Codes
    // =================================================================

    [Fact]
    public void TDV_C0_CursorCodes_ShouldBeRecognized()
    {
        var codes = new[]
        {
            ((byte)0x1C, "Ctrl+\\ / TDV Cursor_Up"),
            ((byte)0x0B, "Ctrl+K / TDV Cursor_Down"),
            ((byte)0x18, "Ctrl+X / TDV Cursor_Right"),
            ((byte)0x1D, "Ctrl+] / TDV Cursor_Home")
        };

        for (int i = 0; i < codes.Length; i++)
        {
            var (b, name) = codes[i];
            var parser = new InputParser();
            parser.Feed(new ReadOnlySpan<byte>(new[] { b }));

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(InputType.Control, result.Type);
            Assert.Equal(name, result.Name);
        }
    }

    [Fact]
    public void TDV_C0_Backspace_ShowsBothIdentifications()
    {
        var parser = new InputParser();
        parser.Feed(new ReadOnlySpan<byte>(new byte[] { 0x08 }));

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Backspace, result.Type);
        Assert.Equal("Backspace / TDV Cursor_Left", result.Name);
    }

    [Fact]
    public void TDV_C0_Tab_ShowsBothIdentifications()
    {
        var parser = new InputParser();
        parser.Feed(new ReadOnlySpan<byte>(new byte[] { 0x09 }));

        Assert.True(parser.TryGetNext(out var result));
        Assert.Equal(InputType.Tab, result.Type);
        Assert.Equal("Tab / TDV TAB (C0)", result.Name);
    }

    [Fact]
    public void TDV_C0_OtherCodes_ShouldBeRecognized()
    {
        var codes = new[]
        {
            ((byte)0x02, "Ctrl+B / TDV Video_Off"),
            ((byte)0x03, "Ctrl+C / TDV Video_On"),
            ((byte)0x04, "Ctrl+D / TDV Erase_Line"),
            ((byte)0x0C, "Ctrl+L / TDV Roll_Up"),
            ((byte)0x17, "Ctrl+W / TDV Roll_Down"),
            ((byte)0x19, "Ctrl+Y / TDV Erase_Page")
        };

        for (int i = 0; i < codes.Length; i++)
        {
            var (b, name) = codes[i];
            var parser = new InputParser();
            parser.Feed(new ReadOnlySpan<byte>(new[] { b }));

            Assert.True(parser.TryGetNext(out var result), $"Failed to parse {name}");
            Assert.Equal(InputType.Control, result.Type);
            Assert.Equal(name, result.Name);
        }
    }
}
