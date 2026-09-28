using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// A resize must re-bound the cursor, not just move it.
///
/// THE DEFECT. <c>TerminalEmulatorBase.Resize</c> called <c>Cursor.WithNewDimensions</c> — which
/// builds a correctly-bounded cursor — and then threw that object away, copying back only Row and
/// Column. The live cursor kept its ORIGINAL _maxRows/_maxCols, which were readonly. After a
/// shrink it therefore never wrapped at the new right margin: the column walked past the end of
/// the buffer and the next printable character threw ArgumentOutOfRangeException out of
/// TerminalBuffer's indexer. A crash, in the ordinary path of dragging a window narrower.
///
/// Found by libvterm's 16state_resize script — see tests\RetroTerm.Tests\Conformance.
/// </summary>
public class CursorResizeBoundsTests
{
    private static TerminalEmulatorBase Build(int width, int height)
        => EmulatorFactory.CreateEmulator("ANSI", width, height, 100);

    [Fact]
    public void TypingPastTheOldWidthAfterAShrinkDoesNotThrow()
    {
        var emulator = Build(80, 25);
        emulator.Resize(10, 5);

        // 40 characters into a 10-column screen. Before the fix this threw on the 11th.
        emulator.ProcessData(Encoding.ASCII.GetBytes(new string('A', 40)));

        Assert.True(emulator.GetCursor().Column < emulator.Width);
    }

    [Fact]
    public void TheCursorIsPulledInsideTheSmallerScreen()
    {
        var emulator = Build(80, 25);
        emulator.ProcessData(Encoding.ASCII.GetBytes("\u001b[20;70H"));   // row 19, col 69

        emulator.Resize(10, 5);

        var cursor = emulator.GetCursor();
        Assert.Equal(4, cursor.Row);
        Assert.Equal(9, cursor.Column);
    }

    [Fact]
    public void SetDimensionsRebindsTheClampAndNotJustThePosition()
    {
        // The narrow version of the same bug, on Cursor alone: after growing, the setters must
        // accept the wider range; after shrinking, they must reject it.
        var cursor = new Cursor(25, 80);

        cursor.SetDimensions(5, 10);
        cursor.Column = 40;
        Assert.Equal(9, cursor.Column);

        cursor.SetDimensions(25, 80);
        cursor.Column = 40;
        Assert.Equal(40, cursor.Column);
    }
}
