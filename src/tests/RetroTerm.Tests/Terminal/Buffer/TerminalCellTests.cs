using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Tests.Terminal.Buffer;

public class TerminalCellTests
{
    [Fact]
    public void Empty_Cell_Should_Be_Zero_With_No_Attributes()
    {
        var cell = TerminalCell.Empty;

        Assert.Equal(0u, cell.Codepoint);
        Assert.Equal(CharacterAttributes.None, cell.Attributes);
        Assert.Equal(TerminalColor.Default, cell.Foreground);
        Assert.Equal(TerminalColor.Default, cell.Background);
        Assert.True(cell.IsEmpty);
    }

    [Fact]
    public void Constructor_With_Char_Should_Create_Cell()
    {
        var cell = new TerminalCell('A');

        Assert.Equal('A', (char)cell.Codepoint);
        Assert.Equal("A", cell.GetString());
        Assert.False(cell.IsEmpty);
    }

    [Fact]
    public void Constructor_With_Codepoint_Should_Create_Cell()
    {
        var cell = new TerminalCell(0x1F600); // 😀 emoji

        Assert.Equal(0x1F600u, cell.Codepoint);
        Assert.Equal("😀", cell.GetString());
    }

    [Fact]
    public void Full_Constructor_Should_Set_All_Properties()
    {
        var fg = TerminalColor.FromIndex(StandardColors.Red);
        var bg = TerminalColor.FromIndex(StandardColors.Blue);
        var attr = CharacterAttributes.Bold | CharacterAttributes.Underline;

        var cell = new TerminalCell('X', attr, fg, bg, 1);

        Assert.Equal('X', (char)cell.Codepoint);
        Assert.Equal(attr, cell.Attributes);
        Assert.Equal(fg, cell.Foreground);
        Assert.Equal(bg, cell.Background);
        Assert.Equal(1, cell.CharacterSet);
    }

    [Fact]
    public void HasAttributes_Should_Detect_Attributes()
    {
        var cell1 = new TerminalCell('A');
        Assert.False(cell1.HasAttributes);

        var cell2 = new TerminalCell('A', CharacterAttributes.Bold, TerminalColor.Default, TerminalColor.Default);
        Assert.True(cell2.HasAttributes);
    }

    [Fact]
    public void Clear_Should_Reset_Cell_To_Empty()
    {
        var cell = new TerminalCell('X',
            CharacterAttributes.Bold,
            TerminalColor.FromIndex(StandardColors.Red),
            TerminalColor.FromIndex(StandardColors.Blue));

        cell.Clear();

        Assert.Equal(0u, cell.Codepoint);
        Assert.Equal(CharacterAttributes.None, cell.Attributes);
        Assert.Equal(TerminalColor.Default, cell.Foreground);
        Assert.Equal(TerminalColor.Default, cell.Background);
        Assert.True(cell.IsEmpty);
    }

    [Fact]
    public void CopyAttributesFrom_Should_Copy_Only_Attributes()
    {
        var source = new TerminalCell('A',
            CharacterAttributes.Bold,
            TerminalColor.FromIndex(StandardColors.Red),
            TerminalColor.FromIndex(StandardColors.Blue),
            2);

        var dest = new TerminalCell('B');
        dest.CopyAttributesFrom(source);

        Assert.Equal('B', (char)dest.Codepoint); // Character should not be copied
        Assert.Equal(CharacterAttributes.Bold, dest.Attributes);
        Assert.Equal(TerminalColor.FromIndex(StandardColors.Red), dest.Foreground);
        Assert.Equal(TerminalColor.FromIndex(StandardColors.Blue), dest.Background);
        Assert.Equal(2, dest.CharacterSet);
    }

    [Fact]
    public void Equality_Should_Work_Correctly()
    {
        var cell1 = new TerminalCell('A', CharacterAttributes.Bold,
            TerminalColor.FromIndex(StandardColors.Red), TerminalColor.Default);
        var cell2 = new TerminalCell('A', CharacterAttributes.Bold,
            TerminalColor.FromIndex(StandardColors.Red), TerminalColor.Default);
        var cell3 = new TerminalCell('B', CharacterAttributes.Bold,
            TerminalColor.FromIndex(StandardColors.Red), TerminalColor.Default);

        Assert.Equal(cell2, cell1);
        Assert.NotEqual(cell3, cell1);
    }

    [Fact]
    public void GetString_Should_Handle_Empty_Codepoint()
    {
        var cell = new TerminalCell(0);
        Assert.Equal(" ", cell.GetString());
    }

    [Fact]
    public void GetString_Should_Handle_Unicode_Supplementary_Planes()
    {
        // Test emoji from supplementary plane
        var cell = new TerminalCell(0x1F680); // 🚀 rocket
        Assert.Equal("🚀", cell.GetString());
    }
}
