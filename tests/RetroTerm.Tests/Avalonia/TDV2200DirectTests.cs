using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Direct TDV2200 tests that don't require TCP connections.
/// These tests validate TDV2200 emulator behavior directly through TerminalSession.
/// </summary>
public class TDV2200DirectTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    // Assigned by SetupSession(), called from the constructor - the compiler cannot
    // see through that call, hence null! rather than a nullable field.
    private TDV2200Emulator _emulator = null!;
    private TerminalSession _session = null!;
    private InMemoryConnection _connection = null!;
    private readonly List<string> _responses = new List<string>();

    public TDV2200DirectTests(ITestOutputHelper output)
    {
        _output = output;
        SetupSession();
    }

    private void SetupSession()
    {
        _emulator = new TDV2200Emulator(80, 24);
        _session = new TerminalSession(_emulator, "TDV2200 Direct Test");
        _connection = new InMemoryConnection();

        // Wire up response capture
        _emulator.OnResponseReady += response =>
        {
            _responses.Add(response);
            _output.WriteLine($"Response captured: {TDVResponseValidator.ToVisibleString(response)}");
        };
    }

    public void Dispose()
    {
        _session?.Dispose();
        _connection?.Dispose();
    }

    #region Primary DA Tests

    [Fact]
    public void TDV2200_PrimaryDA_ShouldRespondWithCorrectID()
    {
        // Arrange
        var query = TDVSequenceBuilder.BuildDAQuery();
        _responses.Clear();

        // Act - Process DA query through emulator
        _emulator.ProcessInput(query);

        // Assert
        Assert.Single(_responses);
        var response = _responses[0];
        Assert.Equal("\x1b[?220;0c", response);

        var daResponse = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(daResponse.IsValid);
        Assert.False(daResponse.IsSecondary);
        Assert.Equal(220, daResponse.FirmwareId);

        _output.WriteLine($"TDV2200 Primary DA: FirmwareId={daResponse.FirmwareId}");
    }

    [Fact]
    public void TDV2200_SecondaryDA_ShouldRespondWithFirmwareID()
    {
        // Arrange
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();
        _responses.Clear();

        // Act
        _emulator.ProcessInput(query);

        // Assert
        Assert.Single(_responses);
        var response = _responses[0];
        Assert.Equal("\x1b[>220;0;0c", response);

        var daResponse = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(daResponse.IsValid);
        Assert.True(daResponse.IsSecondary);
        Assert.Equal(220, daResponse.FirmwareId);

        _output.WriteLine($"TDV2200 Secondary DA: FirmwareId={daResponse.FirmwareId}");
    }

    #endregion

    #region CPR Tests

    [Fact]
    public void TDV2200_CPR_AtHome_ShouldRespondWithPosition1_1()
    {
        // Arrange
        var query = TDVSequenceBuilder.BuildCPRQuery();
        _responses.Clear();

        // Act
        _emulator.ProcessInput(query);

        // Assert
        Assert.Single(_responses);
        var response = _responses[0];

        var cprResponse = TDVResponseValidator.ParseCPRResponse(response);
        Assert.True(cprResponse.IsValid);
        Assert.Equal(1, cprResponse.Row);
        Assert.Equal(1, cprResponse.Column);

        _output.WriteLine($"TDV2200 CPR at home: Row={cprResponse.Row}, Col={cprResponse.Column}");
    }

    [Fact]
    public void TDV2200_CPR_AfterMove_ShouldRespondWithNewPosition()
    {
        // Arrange - Move cursor to row 10, col 20
        var moveSequence = Encoding.UTF8.GetBytes("\x1b[10;20H");
        _emulator.ProcessInput(moveSequence);

        var query = TDVSequenceBuilder.BuildCPRQuery();
        _responses.Clear();

        // Act
        _emulator.ProcessInput(query);

        // Assert
        Assert.Single(_responses);
        var response = _responses[0];

        var cprResponse = TDVResponseValidator.ParseCPRResponse(response);
        Assert.True(cprResponse.IsValid);
        Assert.Equal(10, cprResponse.Row);
        Assert.Equal(20, cprResponse.Column);

        _output.WriteLine($"TDV2200 CPR after move: Row={cprResponse.Row}, Col={cprResponse.Column}");
    }

    #endregion

    #region DSR Tests

    [Fact]
    public void TDV2200_DSR_ShouldRespondWithReadyStatus()
    {
        // Arrange
        var query = TDVSequenceBuilder.BuildDSRQuery();
        _responses.Clear();

        // Act
        _emulator.ProcessInput(query);

        // Assert
        Assert.Single(_responses);
        var response = _responses[0];

        var dsrResponse = TDVResponseValidator.ParseDSRResponse(response);
        Assert.True(dsrResponse.IsValid);
        Assert.True(dsrResponse.IsOK);
        Assert.False(dsrResponse.IsFailure);

        _output.WriteLine($"TDV2200 DSR: OK={dsrResponse.IsOK}");
    }

    #endregion

    #region Terminal ID Tests

    [Fact]
    public void TDV2200_TerminalID_ShouldRespondCorrectly()
    {
        // Arrange
        var query = TDVSequenceBuilder.BuildTerminalIDQuery();
        _responses.Clear();

        // Act
        _emulator.ProcessInput(query);

        // Assert
        Assert.Single(_responses);
        var response = _responses[0];

        var terminalIdResponse = TDVResponseValidator.ParseTerminalIDResponse(response);
        Assert.True(terminalIdResponse.IsValid);
        Assert.Equal(220, terminalIdResponse.FirmwareId);

        _output.WriteLine($"TDV2200 Terminal ID: Type={terminalIdResponse.TerminalType}");
    }

    #endregion

    #region Mode Query Tests

    /// <summary>
    /// The 2115 switch is mode 66, and RESETTING it is what turns 2115 mode ON.
    /// </summary>
    /// <remarks>
    /// <para><b>Two tests used to sit here and both were built on an invented sequence</b></para>
    /// They sent <c>CSI ? 40 $ y</c> and read the reply as the 2115 state. Mode 40 is PCF, the
    /// printer code format (TDV 2215 Functional Specifications section 8.7.1). And no TDV manual
    /// has a mode query at all - section 8.7 lists every CSI sequence the terminal accepts, with no
    /// <c>$</c> intermediate anywhere in it, and section 8.3.2 lists everything it sends, which is
    /// CPR alone.
    ///
    /// So there is nothing to ask, and the state is read directly instead. What IS worth pinning is
    /// the polarity, because it is the reverse of what this program assumed for months: section 3.1
    /// says of the Extended Control switch, mode 66, "When this switch is set to OFF, the terminal
    /// works like a TDV 2115 from the host computer&#39;s point of view."
    ///
    /// See <c>docs&#92;TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    [Fact]
    public void TDV2200_ResettingModeSixtySixEntersCompatibilityModeAndSettingItLeaves()
    {
        Assert.False(_emulator.Is2115CompatibilityMode, "a fresh TDV2200 is in extended operation");

        _emulator.ProcessInput(TDVSequenceBuilder.Build2115CompatibilityEnable());
        Assert.True(_emulator.Is2115CompatibilityMode, "RM 66 should have entered 2115 mode");

        _emulator.ProcessInput(TDVSequenceBuilder.Build2115CompatibilityDisable());
        Assert.False(_emulator.Is2115CompatibilityMode, "SM 66 should have left 2115 mode");

        _output.WriteLine("TDV2200: CSI 66 l enters 2115 mode, CSI 66 h leaves it");
    }

    /// <summary>
    /// No mode query draws a reply out of a TDV2200, in either state.
    /// </summary>
    [Fact]
    public void TDV2200_AnswersNoModeQuery()
    {
        _responses.Clear();
        _emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[?40$y"));
        Assert.Empty(_responses);

        _emulator.ProcessInput(TDVSequenceBuilder.Build2115CompatibilityEnable());
        _responses.Clear();
        _emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[?66$y"));
        Assert.Empty(_responses);
    }

    #endregion

    #region Display Content Tests

    [Fact]
    public void TDV2200_TextDisplay_ShouldAppearInBuffer()
    {
        // Arrange
        var text = "Hello TDV2200 World!";

        // Act
        _emulator.ProcessData(Encoding.UTF8.GetBytes(text));

        // Assert
        var buffer = _emulator.GetBuffer();
        var lineBuilder = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer[0, col];
            if (cell.Codepoint == 0) break;
            lineBuilder.Append((char)cell.Codepoint);
        }
        var bufferLine = lineBuilder.ToString().TrimEnd();

        Assert.Equal(text, bufferLine);
        _output.WriteLine($"Buffer content: '{bufferLine}'");
    }

    [Fact]
    public void TDV2200_CursorMove_ShouldUpdatePosition()
    {
        // Arrange - Move cursor to row 5, column 10
        var moveSequence = Encoding.UTF8.GetBytes("\x1b[5;10H");

        // Act
        _emulator.ProcessData(moveSequence);
        var cursor = _emulator.GetCursor();

        // Assert (cursor positions are 0-indexed internally, 1-indexed in sequences)
        Assert.Equal(4, cursor.Row);
        Assert.Equal(9, cursor.Column);

        _output.WriteLine($"Cursor position: Row={cursor.Row} (0-indexed), Col={cursor.Column} (0-indexed)");
    }

    [Fact]
    public void TDV2200_ClearScreen_ShouldClearBuffer()
    {
        // Arrange - Write some text first
        _emulator.ProcessData(Encoding.UTF8.GetBytes("Some text to clear"));

        // Act - Clear screen and home cursor
        _emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[2J\x1b[H"));

        // Assert
        var buffer = _emulator.GetBuffer();
        bool hasContent = false;
        for (int row = 0; row < buffer.Height; row++)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                var cell = buffer[row, col];
                if (cell.Codepoint != 0 && cell.Codepoint != ' ')
                {
                    hasContent = true;
                    break;
                }
            }
            if (hasContent) break;
        }

        Assert.False(hasContent, "Buffer should be empty after clear screen");
        _output.WriteLine("Buffer cleared successfully");
    }

    #endregion

    #region Attribute Tests

    [Fact]
    public void TDV2200_SGR_Bold_ShouldSetAttribute()
    {
        // Arrange & Act
        _emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1mBOLD\x1b[0m"));

        // Assert
        var buffer = _emulator.GetBuffer();
        var cell = buffer[0, 0];

        Assert.Equal('B', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Bold));

        _output.WriteLine($"Bold test: '{(char)cell.Codepoint}' Bold={cell.Attributes.HasAttribute(CharacterAttributes.Bold)}");
    }

    [Fact]
    public void TDV2200_SGR_Underline_ShouldSetAttribute()
    {
        // Arrange & Act
        _emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[4mUNDER\x1b[0m"));

        // Assert
        var buffer = _emulator.GetBuffer();
        var cell = buffer[0, 0];

        Assert.Equal('U', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Underline));

        _output.WriteLine($"Underline test: '{(char)cell.Codepoint}' Underline={cell.Attributes.HasAttribute(CharacterAttributes.Underline)}");
    }

    [Fact]
    public void TDV2200_SGR_Reverse_ShouldSetAttribute()
    {
        // Arrange & Act
        _emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[7mREV\x1b[0m"));

        // Assert
        var buffer = _emulator.GetBuffer();
        var cell = buffer[0, 0];

        Assert.Equal('R', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Reverse));

        _output.WriteLine($"Reverse test: '{(char)cell.Codepoint}' Reverse={cell.Attributes.HasAttribute(CharacterAttributes.Reverse)}");
    }

    [Fact]
    public void TDV2200_SGR_Blink_ShouldSetAttribute()
    {
        // Arrange & Act
        _emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5mBLINK\x1b[0m"));

        // Assert
        var buffer = _emulator.GetBuffer();
        var cell = buffer[0, 0];

        Assert.Equal('B', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Blink));

        _output.WriteLine($"Blink test: '{(char)cell.Codepoint}' Blink={cell.Attributes.HasAttribute(CharacterAttributes.Blink)}");
    }

    [Fact]
    public void TDV2200_SGR_Combined_ShouldSetMultipleAttributes()
    {
        // Arrange & Act - Bold + Underline
        _emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;4mCOMBO\x1b[0m"));

        // Assert
        var buffer = _emulator.GetBuffer();
        var cell = buffer[0, 0];

        Assert.Equal('C', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Bold));
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Underline));

        _output.WriteLine($"Combined test: Bold={cell.Attributes.HasAttribute(CharacterAttributes.Bold)}, Underline={cell.Attributes.HasAttribute(CharacterAttributes.Underline)}");
    }

    #endregion

    #region Multiple Query Tests

    [Fact]
    public void TDV2200_MultipleQueries_ShouldAllRespond()
    {
        _responses.Clear();

        // Test 1: Primary DA
        _emulator.ProcessInput(TDVSequenceBuilder.BuildDAQuery());
        Assert.Single(_responses);
        var daResponse = TDVResponseValidator.ParseDAResponse(_responses[0]);
        Assert.True(daResponse.IsValid);
        _output.WriteLine("Query 1 (Primary DA): Valid");

        // Test 2: Secondary DA
        _responses.Clear();
        _emulator.ProcessInput(TDVSequenceBuilder.BuildSecondaryDAQuery());
        Assert.Single(_responses);
        var secondaryDaResponse = TDVResponseValidator.ParseDAResponse(_responses[0]);
        Assert.True(secondaryDaResponse.IsValid && secondaryDaResponse.IsSecondary);
        _output.WriteLine("Query 2 (Secondary DA): Valid");

        // Test 3: CPR
        _responses.Clear();
        _emulator.ProcessInput(TDVSequenceBuilder.BuildCPRQuery());
        Assert.Single(_responses);
        var cprResponse = TDVResponseValidator.ParseCPRResponse(_responses[0]);
        Assert.True(cprResponse.IsValid);
        _output.WriteLine("Query 3 (CPR): Valid");

        // Test 4: DSR
        _responses.Clear();
        _emulator.ProcessInput(TDVSequenceBuilder.BuildDSRQuery());
        Assert.Single(_responses);
        var dsrResponse = TDVResponseValidator.ParseDSRResponse(_responses[0]);
        Assert.True(dsrResponse.IsValid);
        _output.WriteLine("Query 4 (DSR): Valid");

        _output.WriteLine("All 4 queries responded correctly!");
    }

    #endregion

    #region Keyboard Mapping Tests

    [Fact]
    public void TDV2200_KeyboardMapper_F1_ShouldReturnCorrectSequence()
    {
        // Arrange
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Act
        var sequence = mapper.MapKey("F1");

        // Assert
        Assert.Equal("\x1b[11~", sequence);
        _output.WriteLine($"F1 -> {TDVResponseValidator.ToVisibleString(sequence)}");
    }

    [Fact]
    public void TDV2200_KeyboardMapper_ArrowKeys_WithEscMode_ShouldReturnCSISequences()
    {
        // Arrange
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Act & Assert
        Assert.Equal("\x1b[A", mapper.MapKey("UP"));
        Assert.Equal("\x1b[B", mapper.MapKey("DOWN"));
        Assert.Equal("\x1b[C", mapper.MapKey("RIGHT"));
        Assert.Equal("\x1b[D", mapper.MapKey("LEFT"));

        _output.WriteLine("Arrow keys with ESC mode: All correct");
    }

    [Fact]
    public void TDV2200_KeyboardMapper_ArrowKeys_WithC0Mode_ShouldReturnControlCodes()
    {
        // Arrange - Default mode uses C0 control codes
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: false);

        // Act & Assert
        Assert.Equal("\x1c", mapper.MapKey("UP"));   // FS
        Assert.Equal("\x0b", mapper.MapKey("DOWN")); // VT
        Assert.Equal("\x18", mapper.MapKey("RIGHT")); // CAN
        Assert.Equal("\x08", mapper.MapKey("LEFT")); // BS

        _output.WriteLine("Arrow keys with C0 mode: All correct");
    }

    [Fact]
    public void TDV2200_KeyboardMapper_ControlKeys_ShouldReturnCorrectSequences()
    {
        // Arrange
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Act & Assert - Note: Mapper uses "RETURN" not "ENTER" for the return key
        Assert.Equal("\r", mapper.MapKey("RETURN"));
        Assert.Equal("\t", mapper.MapKey("TAB"));
        Assert.Equal("\x1b", mapper.MapKey("ESCAPE"));
        Assert.Equal("\b", mapper.MapKey("BACKSPACE"));
        Assert.Equal(" ", mapper.MapKey("SPACE"));

        _output.WriteLine("Control keys: All correct");
    }

    [Fact]
    public void TDV2200_KeyboardMapper_TDVSpecificKeys_ShouldReturnCorrectSequences()
    {
        // Arrange
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Act & Assert
        Assert.Equal("\x1b[28~", mapper.MapKey("HELP"));
        Assert.Equal("\x1b[29~", mapper.MapKey("DO"));
        Assert.Equal("\x1b[@", mapper.MapKey("FUNC"));
        Assert.Equal("\x1b[M", mapper.MapKey("COPY"));
        Assert.Equal("\x1b[N", mapper.MapKey("MOVE"));

        _output.WriteLine("TDV-specific keys: All correct");
    }

    [Fact]
    public void TDV2200_KeyboardMapper_PushKeys_ShouldReturnCorrectSequences()
    {
        // Arrange
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Act & Assert
        Assert.Equal("\x1b[?1~", mapper.MapKey("PUSH1"));
        Assert.Equal("\x1b[?2~", mapper.MapKey("PUSH2"));
        Assert.Equal("\x1b[?8~", mapper.MapKey("PUSH8"));

        _output.WriteLine("PUSH keys: All correct");
    }

    [Fact]
    public void TDV2200_KeyboardMapper_ModifiedArrows_ShouldReturnXtermStyle()
    {
        // Arrange
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Act & Assert - xterm-style modified arrow sequences
        Assert.Equal("\x1b[1;2A", mapper.MapKey("UP", shift: true));
        Assert.Equal("\x1b[1;5C", mapper.MapKey("RIGHT", ctrl: true));
        Assert.Equal("\x1b[1;3B", mapper.MapKey("DOWN", alt: true));

        _output.WriteLine("Modified arrow keys: All correct");
    }

    [Fact]
    public void TDV2200_KeyboardMapper_ModifiedArrows_AllModifiers()
    {
        // Test all modifier combinations for arrow keys

        // Arrange
        var mapper = new TDVKeyboardMapper(is2115Mode: false, tdvArrowsIsEscCode: true);

        // Act & Assert - Shift modifiers (;2)
        Assert.Equal("\x1b[1;2A", mapper.MapKey("UP", shift: true));
        Assert.Equal("\x1b[1;2B", mapper.MapKey("DOWN", shift: true));
        Assert.Equal("\x1b[1;2C", mapper.MapKey("RIGHT", shift: true));
        Assert.Equal("\x1b[1;2D", mapper.MapKey("LEFT", shift: true));

        // Act & Assert - Ctrl modifiers (;5)
        Assert.Equal("\x1b[1;5A", mapper.MapKey("UP", ctrl: true));
        Assert.Equal("\x1b[1;5B", mapper.MapKey("DOWN", ctrl: true));
        Assert.Equal("\x1b[1;5C", mapper.MapKey("RIGHT", ctrl: true));
        Assert.Equal("\x1b[1;5D", mapper.MapKey("LEFT", ctrl: true));

        // Act & Assert - Alt modifiers (;3)
        Assert.Equal("\x1b[1;3A", mapper.MapKey("UP", alt: true));
        Assert.Equal("\x1b[1;3B", mapper.MapKey("DOWN", alt: true));
        Assert.Equal("\x1b[1;3C", mapper.MapKey("RIGHT", alt: true));
        Assert.Equal("\x1b[1;3D", mapper.MapKey("LEFT", alt: true));

        _output.WriteLine("All modified arrow keys: Correct xterm-style sequences");
    }

    #endregion
}
