using System.Linq;
using RetroTerm.Core.Selection;
using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Selection;

/// <summary>
/// Unit tests for SelectionManager - text selection functionality
/// </summary>
public class SelectionManagerTests
{
    [Fact]
    public void StartSelection_ShouldCreateNewSelection()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);

        // Act
        manager.StartSelection(5, 10, SelectionMode.Character);

        // Assert
        Assert.NotNull(manager.CurrentSelection);
        Assert.Equal((5, 10), manager.CurrentSelection.Start);
        Assert.Equal((5, 10), manager.CurrentSelection.End);
        Assert.Equal(SelectionMode.Character, manager.CurrentSelection.Mode);
    }

    [Fact]
    public void ExtendSelection_ShouldUpdateEndPosition()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.StartSelection(5, 10);

        // Act
        manager.ExtendSelection(5, 20);

        // Assert
        Assert.NotNull(manager.CurrentSelection);
        Assert.Equal((5, 10), manager.CurrentSelection.Start);
        Assert.Equal((5, 20), manager.CurrentSelection.End);
    }

    [Fact]
    public void ClearSelection_ShouldRemoveSelection()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.StartSelection(5, 10);

        // Act
        manager.ClearSelection();

        // Assert
        Assert.Null(manager.CurrentSelection);
        Assert.False(manager.HasSelection);
    }

    [Fact]
    public void HasSelection_EmptySelection_ShouldReturnFalse()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.StartSelection(5, 10);

        // Act - Extend to same position
        manager.ExtendSelection(5, 10);

        // Assert
        Assert.False(manager.HasSelection);
    }

    [Fact]
    public void GetSelectedText_CharacterSelection_ShouldReturnText()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('H');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('l');
        buffer[0, 3] = new TerminalCell('l');
        buffer[0, 4] = new TerminalCell('o');

        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 0);
        manager.ExtendSelection(0, 4);

        // Act
        var text = manager.GetSelectedText();

        // Assert
        Assert.Equal("Hello", text);
    }

    [Fact]
    public void GetSelectedText_MultiLineSelection_ShouldIncludeNewlines()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('L');
        buffer[0, 1] = new TerminalCell('i');
        buffer[0, 2] = new TerminalCell('n');
        buffer[0, 3] = new TerminalCell('e');
        buffer[0, 4] = new TerminalCell('1');

        buffer[1, 0] = new TerminalCell('L');
        buffer[1, 1] = new TerminalCell('i');
        buffer[1, 2] = new TerminalCell('n');
        buffer[1, 3] = new TerminalCell('e');
        buffer[1, 4] = new TerminalCell('2');

        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 0);
        manager.ExtendSelection(1, 4);

        // Act
        var text = manager.GetSelectedText();

        // Assert
        Assert.Contains("Line1", text);
        Assert.Contains("Line2", text);
        Assert.Contains('\n', text);
    }

    [Fact]
    public void GetSelectedText_WordSelection_ShouldSelectWholeWords()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('H');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('l');
        buffer[0, 3] = new TerminalCell('l');
        buffer[0, 4] = new TerminalCell('o');
        buffer[0, 5] = new TerminalCell(' ');
        buffer[0, 6] = new TerminalCell('W');
        buffer[0, 7] = new TerminalCell('o');
        buffer[0, 8] = new TerminalCell('r');
        buffer[0, 9] = new TerminalCell('l');
        buffer[0, 10] = new TerminalCell('d');

        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 2, SelectionMode.Word); // Start in middle of "Hello"
        manager.ExtendSelection(0, 7); // Extend to middle of "World"

        // Act
        var text = manager.GetSelectedText();

        // Assert
        Assert.Equal("Hello World", text);
    }

    [Fact]
    public void GetSelectedText_LineSelection_ShouldSelectFullLines()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('L');
        buffer[0, 1] = new TerminalCell('i');
        buffer[0, 2] = new TerminalCell('n');
        buffer[0, 3] = new TerminalCell('e');
        buffer[0, 4] = new TerminalCell('1');

        buffer[1, 0] = new TerminalCell('L');
        buffer[1, 1] = new TerminalCell('i');
        buffer[1, 2] = new TerminalCell('n');
        buffer[1, 3] = new TerminalCell('e');
        buffer[1, 4] = new TerminalCell('2');

        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 2, SelectionMode.Line); // Start in middle of line 0
        manager.ExtendSelection(1, 2); // Extend to middle of line 1

        // Act
        var text = manager.GetSelectedText();

        // Assert
        // Should include full width of both lines
        Assert.Contains("Line1", text);
        Assert.Contains("Line2", text);
    }

    [Fact]
    public void GetSelectedText_RectangularSelection_ShouldSelectRectangle()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        // Create a 3x3 block
        buffer[0, 0] = new TerminalCell('A');
        buffer[0, 1] = new TerminalCell('B');
        buffer[0, 2] = new TerminalCell('C');
        buffer[1, 0] = new TerminalCell('D');
        buffer[1, 1] = new TerminalCell('E');
        buffer[1, 2] = new TerminalCell('F');
        buffer[2, 0] = new TerminalCell('G');
        buffer[2, 1] = new TerminalCell('H');
        buffer[2, 2] = new TerminalCell('I');

        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 0, SelectionMode.Rectangular);
        manager.ExtendSelection(2, 2);

        // Act
        var text = manager.GetSelectedText();

        // Assert
        Assert.Contains("ABC", text);
        Assert.Contains("DEF", text);
        Assert.Contains("GHI", text);
        Assert.Equal(2, text.Count(c => c == '\n')); // 2 newlines for 3 lines
    }

    [Fact]
    public void GetSelectedCells_CharacterSelection_ShouldReturnAllCells()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 0);
        manager.ExtendSelection(0, 4);

        // Act
        var cells = manager.GetSelectedCells().ToList();

        // Assert
        Assert.Equal(5, cells.Count);
        Assert.Contains((0, 0), cells);
        Assert.Contains((0, 4), cells);
    }

    [Fact]
    public void GetSelectedCells_RectangularSelection_ShouldReturnRectangle()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 0, SelectionMode.Rectangular);
        manager.ExtendSelection(2, 2);

        // Act
        var cells = manager.GetSelectedCells().ToList();

        // Assert
        Assert.Equal(9, cells.Count); // 3x3 = 9 cells
        Assert.Contains((0, 0), cells);
        Assert.Contains((0, 2), cells);
        Assert.Contains((2, 0), cells);
        Assert.Contains((2, 2), cells);
    }

    [Fact]
    public void Normalized_ReversedSelection_ShouldSwapStartAndEnd()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 10);
        manager.ExtendSelection(0, 5); // End before start

        // Act
        var (start, end) = manager.CurrentSelection!.Normalized;

        // Assert
        Assert.Equal((0, 5), start);
        Assert.Equal((0, 10), end);
    }

    [Fact]
    public void GetSelectedText_ReversedSelection_ShouldWork()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 5] = new TerminalCell('W');
        buffer[0, 6] = new TerminalCell('o');
        buffer[0, 7] = new TerminalCell('r');
        buffer[0, 8] = new TerminalCell('l');
        buffer[0, 9] = new TerminalCell('d');

        var manager = new SelectionManager(buffer);
        manager.StartSelection(0, 9); // Start at end
        manager.ExtendSelection(0, 5); // Extend to start

        // Act
        var text = manager.GetSelectedText();

        // Assert
        Assert.Equal("World", text);
    }

    [Fact]
    public void ExtendSelection_WithoutStart_ShouldStartNewSelection()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);

        // Act
        manager.ExtendSelection(5, 10);

        // Assert
        Assert.NotNull(manager.CurrentSelection);
        Assert.Equal((5, 10), manager.CurrentSelection.Start);
        Assert.Equal((5, 10), manager.CurrentSelection.End);
    }

    [Fact]
    public void SelectAll_ShouldSelectEntireBuffer()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);

        // Act
        manager.SelectAll();

        // Assert
        Assert.NotNull(manager.CurrentSelection);
        Assert.True(manager.HasSelection);
        var (start, end) = manager.CurrentSelection.Normalized;
        Assert.Equal((0, 0), start);
        Assert.Equal((23, 79), end);
        Assert.Equal(SelectionMode.Character, manager.CurrentSelection.Mode);
    }

    [Fact]
    public void SelectAll_SmallBuffer_ShouldSelectEntireBuffer()
    {
        // Arrange
        var buffer = new TerminalBuffer(10, 5);
        var manager = new SelectionManager(buffer);

        // Act
        manager.SelectAll();

        // Assert
        Assert.NotNull(manager.CurrentSelection);
        Assert.True(manager.HasSelection);
        var (start, end) = manager.CurrentSelection.Normalized;
        Assert.Equal((0, 0), start);
        Assert.Equal((4, 9), end);
    }

    [Fact]
    public void SelectAll_ShouldReturnAllBufferText()
    {
        // Arrange
        var buffer = new TerminalBuffer(10, 3);
        buffer[0, 0] = new TerminalCell('A');
        buffer[0, 1] = new TerminalCell('B');
        buffer[0, 2] = new TerminalCell('C');
        buffer[1, 0] = new TerminalCell('D');
        buffer[1, 1] = new TerminalCell('E');
        buffer[2, 0] = new TerminalCell('X');
        buffer[2, 1] = new TerminalCell('Y');
        buffer[2, 2] = new TerminalCell('Z');

        var manager = new SelectionManager(buffer);

        // Act
        manager.SelectAll();
        var text = manager.GetSelectedText();

        // Assert
        Assert.Contains("ABC", text);
        Assert.Contains("DE", text);
        Assert.Contains("XYZ", text);
    }

    [Fact]
    public void SelectAll_EmptyBuffer_HasSelection_ShouldBeTrue()
    {
        // Arrange — buffer has no content but SelectAll still selects the range
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);

        // Act
        manager.SelectAll();

        // Assert — start != end so HasSelection is true
        Assert.True(manager.HasSelection);
    }

    [Fact]
    public void SelectAll_ThenClear_ShouldRemoveSelection()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.SelectAll();
        Assert.True(manager.HasSelection);

        // Act
        manager.ClearSelection();

        // Assert
        Assert.False(manager.HasSelection);
        Assert.Null(manager.CurrentSelection);
    }

    [Fact]
    public void SelectAll_OverwritesPreviousSelection()
    {
        // Arrange — start with a small selection, then SelectAll
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);
        manager.StartSelection(5, 10);
        manager.ExtendSelection(5, 20);

        // Act
        manager.SelectAll();

        // Assert — should now span entire buffer, not the old range
        Assert.NotNull(manager.CurrentSelection);
        var (start, end) = manager.CurrentSelection.Normalized;
        Assert.Equal((0, 0), start);
        Assert.Equal((23, 79), end);
    }
}

