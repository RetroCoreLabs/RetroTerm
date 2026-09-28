using System;
using System.Text;
using Xunit;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

/// <summary>
/// Communication interface validation tests for TDV-2200
/// Tests query/response commands, handshaking, transmission timing, line discipline
/// </summary>
public class CommunicationValidationTests : TDV2200ValidationTestBase
{
    #region Query/Response Commands

    [Fact]
    public void PrimaryDA_ShouldRespondCorrectly()
    {
        // Arrange
        ClearResponses();

        // Act
        SendSequence("\x1b[c"); // Primary DA query

        // Assert
        Assert.Single(Responses);
        var response = Responses[0];
        Assert.StartsWith("\x1b[", response);
        Assert.Contains("c", response);
    }

    [Fact]
    public void SecondaryDA_ShouldRespondWithFirmwareID()
    {
        // Arrange
        ClearResponses();

        // Act
        SendSequence("\x1b[>c"); // Secondary DA query

        // Assert
        Assert.Single(Responses);
        var response = Responses[0];
        Assert.StartsWith("\x1b[>", response);
        // TDV2200 should respond with firmware ID 220
        Assert.Contains("220", response);
    }

    [Fact]
    public void CursorPositionReport_ShouldRespondWithCurrentPosition()
    {
        // Arrange
        SendSequence("\x1b[10;20H"); // Move cursor
        ClearResponses();

        // Act
        SendSequence("\x1b[6n"); // CPR query

        // Assert
        Assert.Single(Responses);
        var response = Responses[0];
        Assert.StartsWith("\x1b[", response);
        Assert.Contains("10", response); // Row
        Assert.Contains("20", response); // Column
        Assert.EndsWith("R", response);
    }

    [Fact]
    public void DeviceStatusReport_ShouldRespondWithStatus()
    {
        // Arrange
        ClearResponses();

        // Act
        SendSequence("\x1b[5n"); // DSR query

        // Assert
        Assert.Single(Responses);
        var response = Responses[0];
        Assert.StartsWith("\x1b[", response);
        Assert.Contains("n", response);
    }

    [Fact]
    public void TerminalIdentification_ShouldRespondWithID()
    {
        // Arrange
        ClearResponses();

        // Act
        SendSequence("\x1bZ"); // Terminal ID query

        // Assert
        Assert.Single(Responses);
        var response = Responses[0];
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    /// <summary>
    /// DECRQM is answered by the base class, on the wire, using DEC&#39;s own numbers and values.
    /// </summary>
    /// <remarks>
    /// This test used to send <c>CSI ? 67 $ p</c> and call 67 "smooth scroll", on the strength of
    /// an uncited document. Mode 67 is HAN, the XON/XOFF handshake, in TDV 2215 Functional
    /// Specifications section 8.7.1; smooth scroll is 60, RT Roll Type. And no TDV manual has
    /// DECRQM at all - it answers here only because <c>TerminalEmulatorBase</c> answers it for
    /// every emulator, which is our extension rather than a claim about Tandberg hardware.
    ///
    /// It also watched <c>OnResponseReady</c>, which is the TDV observation event. A base-class
    /// reply never raises it. The wire is <c>DataToSend</c>, so that is what is checked.
    ///
    /// The value is DEC&#39;s DECRPM: 0 not recognised, 1 set, 2 reset. Mode 4 is DECSCLM, which
    /// this emulator implements, so a freshly built one must answer 2 - "I know that mode and it is
    /// off" - and not 0.
    ///
    /// See <c>docs&#92;TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    [Fact]
    public void ModeQuery_DECRQM_IsAnsweredOnTheWireWithDecsValues()
    {
        var wire = new List<byte[]>();
        Emulator.DataToSend += bytes => wire.Add(bytes);

        SendSequence("\x1b[?4$p");   // DECRQM for DECSCLM, smooth scroll

        Assert.Single(wire);
        var reply = Encoding.ASCII.GetString(wire[0]);
        Assert.Equal("\x1b[?4;2$y", reply);
    }

    #endregion

    #region Handshaking

    [Fact]
    public void XONXOFF_ShouldHandleFlowControl()
    {
        // Arrange & Act
        SendSequence("\x11"); // XON
        SendSequence("\x13"); // XOFF

        // Assert - Flow control should be handled
        // Note: May need to check internal state or connection behavior
    }

    #endregion

    #region Transmission Timing

    [Fact]
    public void ResponseDelay_ShouldBeWithinTimeout()
    {
        // Arrange
        ClearResponses();
        var startTime = DateTime.Now;

        // Act
        SendSequence("\x1b[c"); // Primary DA query

        // Assert - Response should arrive quickly
        var elapsed = DateTime.Now - startTime;
        Assert.True(elapsed.TotalMilliseconds < 1000, "Response should arrive within 1 second");
        Assert.Single(Responses);
    }

    #endregion

    #region Line Discipline

    [Fact]
    public void CRLF_ShouldHandleCorrectly()
    {
        // Arrange & Act
        SendSequence("\x0D\x0A"); // CR+LF

        // Assert - Cursor should move to start of next line
        var (row, col) = GetCursorPosition();
        Assert.Equal(1, row); // Should be on second line
        Assert.Equal(0, col); // Should be at start of line
    }

    [Fact]
    public void NUL_ShouldBeIgnored()
    {
        // Arrange
        SendSequence("Test");
        var initialPos = GetCursorPosition();

        // Act
        SendSequence("\x00"); // NUL

        // Assert - Cursor should not move
        var finalPos = GetCursorPosition();
        Assert.Equal(initialPos.row, finalPos.row);
        Assert.Equal(initialPos.col, finalPos.col);
    }

    #endregion
}

