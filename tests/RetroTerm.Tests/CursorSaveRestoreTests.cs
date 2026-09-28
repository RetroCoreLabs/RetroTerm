using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests;

public class CursorSaveRestoreTests
{
    private readonly ITestOutputHelper _output;

    public CursorSaveRestoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private string GetLineText(TestTerminalEmulator emulator, int row)
    {
        var buffer = emulator.GetBuffer();
        var sb = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            sb.Append(char.ConvertFromUtf32((int)buffer[row, col].Codepoint));
        }
        return sb.ToString().TrimEnd();
    }

    [Fact]
    public void CursorSaveRestore_ShouldRestorePosition()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(80, 24);

        // Act: Move to 5,5 and write text
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[5;5HOriginal Position"));

        var cursorAfterWrite = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Cursor after writing 'Original Position': Row={cursorAfterWrite.Row}, Col={cursorAfterWrite.Column}");

        // Save cursor
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b" + "7"));

        var cursorAfterSave = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Cursor after save: Row={cursorAfterSave.Row}, Col={cursorAfterSave.Column}");

        // Move to different position
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;10HMoved to 10,10"));

        var cursorAfterMove = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Cursor after moving and writing: Row={cursorAfterMove.Row}, Col={cursorAfterMove.Column}");

        // Restore cursor
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b" + "8"));

        var cursorAfterRestore = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Cursor after restore: Row={cursorAfterRestore.Row}, Col={cursorAfterRestore.Column}");

        // Write more text
        emulator.ProcessData(Encoding.UTF8.GetBytes(" <- Restored!"));

        // Assert: Check the buffer
        var buffer = emulator.GetBuffer();

        // Check specific characters in the buffer
        _output.WriteLine($"\nChecking buffer at row 4:");
        var sb = new StringBuilder();
        for (int col = 0; col < 40; col++)
        {
            sb.Append(char.ConvertFromUtf32((int)buffer[4, col].Codepoint));
        }
        _output.WriteLine($"Columns 0-39: '{sb}'");

        // Verify "Original Position" is at columns 4-20
        Assert.Equal("O", char.ConvertFromUtf32((int)buffer[4, 4].Codepoint));
        Assert.Equal("n", char.ConvertFromUtf32((int)buffer[4, 20].Codepoint));

        // Verify " <- Restored!" starts at column 21
        Assert.Equal(" ", char.ConvertFromUtf32((int)buffer[4, 21].Codepoint));
        Assert.Equal("<", char.ConvertFromUtf32((int)buffer[4, 22].Codepoint));
        Assert.Equal("-", char.ConvertFromUtf32((int)buffer[4, 23].Codepoint));

        // Cursor should have been restored to position after "Original Position"
        Assert.Equal(4, cursorAfterRestore.Row); // Row 5 (1-indexed) = 4 (0-indexed)
        Assert.Equal(21, cursorAfterRestore.Column); // After "Original Position" (17 chars) starting at col 4
    }

    [Fact]
    public void CursorSave_ShouldSaveCurrentPosition()
    {
        // Arrange
        var emulator = new TestTerminalEmulator(24, 80);

        // Act: Move to specific position
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[10;20H"));
        var positionBefore = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Position after move: {positionBefore}");

        // Save
        var bytes = Encoding.UTF8.GetBytes("\x1b" + "7");
        _output.WriteLine($"Sending ESC 7: bytes = {BitConverter.ToString(bytes)}");
        emulator.ProcessData(bytes);
        var positionAfterSave = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Position after save: {positionAfterSave}");

        // Move elsewhere
        emulator.ProcessData(Encoding.UTF8.GetBytes("\x1b[1;1H"));
        var positionAfterMove = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Position after move to 1,1: {positionAfterMove}");

        // Restore
        var bytes2 = Encoding.UTF8.GetBytes("\x1b" + "8");
        _output.WriteLine($"Sending ESC 8: bytes = {BitConverter.ToString(bytes2)}");
        emulator.ProcessData(bytes2);
        var positionAfter = (emulator.GetCursor().Row, emulator.GetCursor().Column);
        _output.WriteLine($"Position after restore: {positionAfter}");

        // Assert
        Assert.Equal(positionBefore, positionAfter);
    }
}

