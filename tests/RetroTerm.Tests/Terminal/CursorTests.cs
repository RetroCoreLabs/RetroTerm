using RetroTerm.Core.Terminal;

namespace RetroTerm.Tests.Terminal;

public class CursorTests
{
    [Fact]
    public void Constructor_Should_Initialize_At_Home()
    {
        var cursor = new Cursor(24, 80);

        Assert.Equal(0, cursor.Row);
        Assert.Equal(0, cursor.Column);
        Assert.Equal(CursorStyle.Block, cursor.Style);
        Assert.True(cursor.Visible);
        Assert.True(cursor.AutoWrap);
    }

    [Fact]
    public void MoveTo_Should_Set_Position()
    {
        var cursor = new Cursor(24, 80);

        cursor.MoveTo(10, 20);

        Assert.Equal(10, cursor.Row);
        Assert.Equal(20, cursor.Column);
    }

    [Fact]
    public void MoveTo_Should_Clamp_To_Valid_Range()
    {
        var cursor = new Cursor(24, 80);

        cursor.MoveTo(-5, -10);
        Assert.Equal(0, cursor.Row);
        Assert.Equal(0, cursor.Column);

        cursor.MoveTo(100, 200);
        Assert.Equal(23, cursor.Row);
        Assert.Equal(79, cursor.Column);
    }

    [Fact]
    public void Home_Should_Move_To_Origin()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);

        cursor.Home();

        Assert.Equal(0, cursor.Row);
        Assert.Equal(0, cursor.Column);
    }

    [Fact]
    public void MoveUp_Should_Move_Cursor_Up()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);

        cursor.MoveUp(3);

        Assert.Equal(7, cursor.Row);
        Assert.Equal(20, cursor.Column);
    }

    [Fact]
    public void MoveUp_Should_Not_Go_Above_Zero()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(2, 0);

        cursor.MoveUp(5);

        Assert.Equal(0, cursor.Row);
    }

    [Fact]
    public void MoveDown_Should_Move_Cursor_Down()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);

        cursor.MoveDown(3);

        Assert.Equal(13, cursor.Row);
        Assert.Equal(20, cursor.Column);
    }

    [Fact]
    public void MoveDown_Should_Not_Go_Below_Bottom()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(22, 0);

        cursor.MoveDown(5);

        Assert.Equal(23, cursor.Row);
    }

    [Fact]
    public void MoveForward_Should_Move_Cursor_Right()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);

        cursor.MoveForward(5);

        Assert.Equal(10, cursor.Row);
        Assert.Equal(25, cursor.Column);
    }

    [Fact]
    public void MoveForward_Should_Not_Go_Past_Right_Edge()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(0, 78);

        cursor.MoveForward(5);

        Assert.Equal(79, cursor.Column);
    }

    [Fact]
    public void MoveBackward_Should_Move_Cursor_Left()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);

        cursor.MoveBackward(5);

        Assert.Equal(10, cursor.Row);
        Assert.Equal(15, cursor.Column);
    }

    [Fact]
    public void MoveBackward_Should_Not_Go_Before_Left_Edge()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(0, 2);

        cursor.MoveBackward(5);

        Assert.Equal(0, cursor.Column);
    }

    [Fact]
    public void CarriageReturn_Should_Move_To_Column_Zero()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 50);

        cursor.CarriageReturn();

        Assert.Equal(10, cursor.Row);
        Assert.Equal(0, cursor.Column);
    }

    [Fact]
    public void LineFeed_Should_Move_Down_One_Row()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);

        var needsScroll = cursor.LineFeed();

        Assert.Equal(11, cursor.Row);
        Assert.Equal(20, cursor.Column);
        Assert.False(needsScroll);
    }

    [Fact]
    public void LineFeed_At_Bottom_Should_Indicate_Scroll_Needed()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(23, 20);

        var needsScroll = cursor.LineFeed();

        Assert.Equal(23, cursor.Row);
        Assert.True(needsScroll);
    }

    [Fact]
    public void LineFeed_With_Return_Should_Move_To_Column_Zero()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 50);

        cursor.LineFeed(returnToStart: true);

        Assert.Equal(11, cursor.Row);
        Assert.Equal(0, cursor.Column);
    }

    [Fact]
    public void Advance_Should_Move_Right()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(0, 10);

        var lineFeed = cursor.Advance();

        Assert.Equal(11, cursor.Column);
        Assert.False(lineFeed);
    }

    [Fact]
    public void Advance_At_End_With_AutoWrap_Arms_The_Last_Column_Flag_Instead_Of_Moving()
    {
        // THIS TEST USED TO EXPECT AN IMMEDIATE MOVE to row 6, column 0. A real VT does not do
        // that: it leaves the cursor on the last column and arms the Last Column Flag, and the
        // wrap happens when the NEXT printable character arrives. See Cursor.PendingWrap.
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(5, 79);
        cursor.AutoWrap = true;

        var lineFeed = cursor.Advance();

        Assert.Equal(5, cursor.Row);
        Assert.Equal(79, cursor.Column);
        Assert.True(cursor.PendingWrap);
        Assert.False(lineFeed);         // nothing fed; ResolvePendingWrap is where that happens
    }

    [Fact]
    public void ResolvePendingWrap_Performs_The_Move_That_Advance_Deferred()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(5, 79);
        cursor.AutoWrap = true;
        cursor.Advance();

        bool needsScroll = cursor.ResolvePendingWrap();

        Assert.Equal(6, cursor.Row);
        Assert.Equal(0, cursor.Column);
        Assert.False(cursor.PendingWrap);
        Assert.False(needsScroll);      // row 5 of 24 is nowhere near the bottom
    }

    [Fact]
    public void Any_Deliberate_Move_Cancels_A_Pending_Wrap()
    {
        // The flag must not survive a cursor-position command. If it did, ESC[80G followed by a
        // character would wrap a line the host never filled.
        var cursor = new Cursor(24, 80);
        cursor.AutoWrap = true;

        cursor.MoveTo(5, 79);
        cursor.Advance();
        Assert.True(cursor.PendingWrap);
        cursor.MoveTo(5, 79);
        Assert.False(cursor.PendingWrap);

        cursor.Advance();
        Assert.True(cursor.PendingWrap);
        cursor.Reverse();               // backspace out of the last column
        Assert.False(cursor.PendingWrap);

        cursor.MoveTo(5, 79);
        cursor.Advance();
        Assert.True(cursor.PendingWrap);
        cursor.CarriageReturn();
        Assert.False(cursor.PendingWrap);
    }

    [Fact]
    public void Advance_At_End_Without_AutoWrap_Should_Stay()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(5, 79);
        cursor.AutoWrap = false;

        var lineFeed = cursor.Advance();

        Assert.Equal(5, cursor.Row);
        Assert.Equal(79, cursor.Column);
        Assert.False(lineFeed);
    }

    [Fact]
    public void Reverse_Should_Move_Left()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(5, 10);

        cursor.Reverse();

        Assert.Equal(9, cursor.Column);
    }

    [Fact]
    public void Reverse_At_Start_Without_ReverseWrap_Should_Stay()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(5, 0);
        cursor.ReverseWrap = false;

        cursor.Reverse();

        Assert.Equal(5, cursor.Row);
        Assert.Equal(0, cursor.Column);
    }

    [Fact]
    public void Reverse_At_Start_With_ReverseWrap_Should_Wrap()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(5, 0);
        cursor.ReverseWrap = true;

        cursor.Reverse();

        Assert.Equal(4, cursor.Row);
        Assert.Equal(79, cursor.Column);
    }

    [Fact]
    public void Save_And_Restore_Should_Preserve_State()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);
        cursor.Style = CursorStyle.Underline;

        cursor.Save();

        cursor.MoveTo(5, 15);
        cursor.Style = CursorStyle.Bar;

        cursor.Restore();

        Assert.Equal(10, cursor.Row);
        Assert.Equal(20, cursor.Column);
        Assert.Equal(CursorStyle.Underline, cursor.Style);
    }

    [Fact]
    public void Reset_Should_Return_To_Default_State()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);
        cursor.Style = CursorStyle.Underline;
        cursor.Visible = false;
        cursor.AutoWrap = false;

        cursor.Reset();

        Assert.Equal(0, cursor.Row);
        Assert.Equal(0, cursor.Column);
        Assert.Equal(CursorStyle.Block, cursor.Style);
        Assert.True(cursor.Visible);
        Assert.True(cursor.AutoWrap);
    }

    [Theory]
    [InlineData(0, 79, true)]
    [InlineData(0, 78, false)]
    public void AtLastColumn_Should_Detect_Last_Column(int row, int col, bool expected)
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(row, col);

        Assert.Equal(expected, cursor.AtLastColumn);
    }

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(22, 0, false)]
    public void AtLastRow_Should_Detect_Last_Row(int row, int col, bool expected)
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(row, col);

        Assert.Equal(expected, cursor.AtLastRow);
    }

    [Fact]
    public void WithNewDimensions_Should_Create_New_Cursor_With_Preserved_State()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(10, 20);
        cursor.Style = CursorStyle.Underline;
        cursor.Visible = false;
        cursor.AutoWrap = false;

        var newCursor = cursor.WithNewDimensions(30, 100);

        Assert.Equal(10, newCursor.Row);
        Assert.Equal(20, newCursor.Column);
        Assert.Equal(CursorStyle.Underline, newCursor.Style);
        Assert.False(newCursor.Visible);
        Assert.False(newCursor.AutoWrap);
    }

    [Fact]
    public void WithNewDimensions_Should_Clamp_Position_To_New_Bounds()
    {
        var cursor = new Cursor(24, 80);
        cursor.MoveTo(20, 70);

        var newCursor = cursor.WithNewDimensions(15, 60);

        Assert.Equal(14, newCursor.Row); // Clamped from 20
        Assert.Equal(59, newCursor.Column); // Clamped from 70
    }
}

