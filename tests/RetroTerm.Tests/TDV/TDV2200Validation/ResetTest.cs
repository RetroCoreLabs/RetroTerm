using System.Text;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

public class ResetTest : TDV2200ValidationTestBase
{
    [Fact]
    public void DirectReset_ShouldClearBuffer()
    {
        // Arrange - Write to buffer
        SendSequence("Test");

        // Act - Call Reset directly
        Emulator.Reset();

        // Assert
        var cell = GetCell(0, 0);
        Assert.NotNull(cell);
        if (cell.HasValue)
        {
            Assert.True(cell.Value.IsEmpty);
        }
    }

    [Fact]
    public void RISSequence_ShouldInvokeReset()
    {
        // Arrange - Write to buffer
        SendSequence("Test");

        // Verify buffer has data
        var cellBefore = GetCell(0, 0);
        Assert.NotNull(cellBefore);
        if (cellBefore.HasValue)
        {
            Assert.Equal('T', (char)cellBefore.Value.Codepoint);
        }

        // Act - Send RIS sequence byte by byte to simulate how ProcessInput works
        Emulator.ProcessInput(new byte[] { 0x1B }); // ESC
        Emulator.ProcessInput(new byte[] { 0x63 }); // 'c'

        // Assert - Buffer should be cleared
        var cellAfter = GetCell(0, 0);
        Assert.NotNull(cellAfter);
        if (cellAfter.HasValue)
        {
            Assert.True(cellAfter.Value.IsEmpty);
        }
    }

    [Fact]
    public void RISSequence_Together_ShouldInvokeReset()
    {
        // Arrange - Write to buffer
        SendSequence("Test");

        // Verify buffer has data
        var cellBefore = GetCell(0, 0);
        Assert.NotNull(cellBefore);
        if (cellBefore.HasValue)
        {
            Assert.Equal('T', (char)cellBefore.Value.Codepoint);
        }

        // Reset the tracking flags
        Emulator.ResetWasCalled = false;
        Emulator.LastEscapeFinalByte = null;
        Emulator.OnEscapeDispatchInvoked = false;

        // Act - Send RIS sequence as single call
        Emulator.ProcessInput(new byte[] { 0x1B, 0x63 }); // ESC c

        // Verify OnEscapeDispatch was invoked
        Assert.True(Emulator.OnEscapeDispatchInvoked, "OnEscapeDispatch should be invoked");

        // Verify HandleEscapeSequence was called
        Assert.NotNull(Emulator.LastEscapeFinalByte);
        Assert.Equal('c', Emulator.LastEscapeFinalByte!.Value);

        // Assert - Reset() should have been called
        Assert.True(Emulator.ResetWasCalled, "Reset() should be called when RIS sequence is sent");

        // Assert - Buffer should be cleared
        var cellAfter = GetCell(0, 0);
        Assert.NotNull(cellAfter);
        if (cellAfter.HasValue)
        {
            Assert.True(cellAfter.Value.IsEmpty);
        }

        // Assert - Cursor should be at home position
        var (row, col) = GetCursorPosition();
        Assert.Equal(0, row);
        Assert.Equal(0, col);
    }

    [Fact]
    public void Parser_ShouldInvokeOnEscapeDispatch()
    {
        // Arrange - Create a fresh parser instance to avoid emulator's handlers interfering
        var parser = new RetroTerm.Core.Terminal.Parsing.EscapeSequenceParser();
        parser.Reset();

        bool handlerCalled = false;
        byte finalByte = 0;

        // Wire up handler directly to parser
        parser.OnEscapeDispatch += (p) =>
        {
            handlerCalled = true;
            finalByte = p.FinalByte; // Read FinalByte immediately in handler
        };

        // Act - Send RIS sequence
        parser.ProcessBytes(new byte[] { 0x1B, 0x63 }); // ESC c

        // Assert
        Assert.True(handlerCalled, "OnEscapeDispatch should be called");
        Assert.Equal((byte)'c', finalByte);
    }

    [Fact]
    public void ProcessInput_ShouldInvokeHandleEscapeSequence()
    {
        // Arrange - Reset tracking and parser state
        Emulator.GetParser().Reset(); // CRITICAL: Reset parser to Ground state
        Emulator.ResetWasCalled = false;
        Emulator.LastEscapeFinalByte = null;
        Emulator.OnEscapeDispatchInvoked = false;

        // Act - Send a simple escape sequence (DECSC - Save Cursor) byte by byte
        // Use raw bytes, not UTF-8 encoded strings - terminal emulators process raw bytes!
        var escByte = new byte[] { 0x1B }; // ESC
        var sevenByte = new byte[] { 0x37 }; // '7'

        // Check parser state before first byte - should be Ground
        var stateBefore = Emulator.GetParser().State;
        Assert.Equal(ParserState.Ground, stateBefore);

        // Process first byte (ESC)
        Emulator.ProcessInput(escByte);
        var stateAfterESC = Emulator.GetParser().State;

        // Check parser state after ESC - should be Escape
        Assert.Equal(ParserState.Escape, stateAfterESC);

        // Process second byte ('7')
        Emulator.ProcessInput(sevenByte);
        var stateAfter7 = Emulator.GetParser().State;

        // Verify OnEscapeDispatch was invoked
        Assert.True(Emulator.OnEscapeDispatchInvoked, $"OnEscapeDispatch should be invoked. Parser state after '7': {stateAfter7}");

        // Assert - HandleEscapeSequence should be called
        Assert.NotNull(Emulator.LastEscapeFinalByte);
        Assert.Equal('7', Emulator.LastEscapeFinalByte!.Value);

        // Parser should be back in Ground state after processing escape sequence
        Assert.Equal(ParserState.Ground, stateAfter7);
    }
}

