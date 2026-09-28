using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// What a hard reset actually returns to its defaults.
///
/// RIS means "you are a terminal that has just been switched on". Every mode a host turned on is
/// something the NEXT host knows nothing about, and the mouse modes are the sharpest case: a
/// terminal that kept reporting the pointer would send bytes to a program that never asked, and
/// those bytes land in the middle of whatever it was reading.
///
/// This file is an audit as much as a test — one assertion per piece of state this emulator has
/// grown, so the next thing added has an obvious place to be checked.
/// </summary>
public class HardResetTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build(string type = "VT420")
        => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void MouseReportingStops()
    {
        // THE one that corrupts another program's input if it is wrong.
        var emulator = Build();
        Feed(emulator, Esc + "[?1003h" + Esc + "[?1006h");
        Assert.True(emulator.Mouse.IsTracking);

        emulator.Reset();

        Assert.False(emulator.Mouse.IsTracking);
        Assert.Equal(MouseTrackingMode.Off, emulator.Mouse.Mode);
        Assert.Equal(MouseEncoding.X10, emulator.Mouse.Encoding);
        Assert.False(emulator.ReportMouse(MouseEventKind.Press, MouseButton.Left, 1, 1));
    }

    [Fact]
    public void BracketedPasteAndFocusReportingStop()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?2004h" + Esc + "[?1004h");

        emulator.Reset();

        Assert.False(emulator.BracketedPasteMode);
        Assert.False(emulator.FocusReporting);
        Assert.Equal("ls", emulator.WrapForPaste("ls"));
    }

    [Fact]
    public void TheMarginsAndTheirModeGo()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?69h" + Esc + "[5;15s");

        emulator.Reset();

        Assert.False(emulator.LeftRightMarginMode);
        Assert.Equal(0, emulator.LeftMargin);
        Assert.Equal(19, emulator.RightMargin);
    }

    [Fact]
    public void TheAttributeExtentGoesBackToStream()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[2*x");

        emulator.Reset();

        Assert.False(emulator.AttributeChangeIsRectangular);
    }

    [Fact]
    public void Vt52ModeGoes()
    {
        var emulator = Build();
        Feed(emulator, Esc + "[?2l");

        emulator.Reset();

        Assert.False(emulator.Vt52Mode);
    }

    [Fact]
    public void TheFunctionKeysAndTheirLockGo()
    {
        var emulator = Build("VT220");
        Feed(emulator, Esc + "P1|17/6c73" + Esc + "\\");
        Assert.True(emulator.UserKeys.IsLocked);

        emulator.Reset();

        Assert.Equal(0, emulator.UserKeys.Count);
        Assert.False(emulator.UserKeys.IsLocked);
    }

    [Fact]
    public void TheDownloadedCharacterSetGoes()
    {
        var emulator = Build("VT220");
        Feed(emulator, Esc + "P1;1;1;8;0;0;12;0{ @~" + Esc + "\\");

        emulator.Reset();

        Assert.Equal(0, emulator.SoftFont.Count);
    }

    [Fact]
    public void GraphicsAndTheirDecodersGo()
    {
        // The planes were already cleared. The DECODERS were not - a ReGIS drawing point and a
        // Sixel palette are host state that survives between sequences on purpose, and a hard
        // reset is exactly the event that ends them.
        var emulator = Build("VT340");
        Feed(emulator, Esc + "Pq#1;2;100;0;0~" + Esc + "\\");
        Feed(emulator, Esc + "PpW(I2)P[10,10]V[100,10]" + Esc + "\\");
        Assert.True(emulator.Graphics!.HasAnythingToDraw());

        emulator.Reset();
        emulator.Graphics.Composite();

        Assert.False(emulator.Graphics.HasAnythingToDraw());
        Assert.Null(emulator.Regis);
    }

    [Fact]
    public void AndTheOrdinaryScreenStateGoes()
    {
        // The pieces that were always reset, kept here so this file is the whole audit rather than
        // only the new parts.
        var emulator = Build();
        Feed(emulator, Esc + "[3;5r" + Esc + "[1;4m" + Esc + "[?6h" + Esc + "[?7l");
        Feed(emulator, Esc + "[1\"q");

        emulator.Reset();

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
        Assert.True(emulator.GetCursor().AutoWrap);

        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));
        Feed(emulator, Esc + "P$qm" + Esc + "\\");
        Feed(emulator, Esc + "P$q\"q" + Esc + "\\");

        Assert.Equal(Esc + "P1$r0m" + Esc + "\\" + Esc + "P1$r0\"q" + Esc + "\\", replies.ToString());
    }

    [Fact]
    public void TheDisplayColoursAreNotHostStateAndStay()
    {
        // The one thing that must NOT be reset. These describe the screen the user chose, not
        // anything a host set, and clearing them would answer the next OSC 11 with a colour this
        // terminal does not paint.
        var emulator = Build();
        emulator.DisplayForeground = (255, 176, 0);
        emulator.DisplayBackground = (10, 8, 0);

        emulator.Reset();

        Assert.Equal(((byte)255, (byte)176, (byte)0), emulator.DisplayForeground);
        Assert.Equal(((byte)10, (byte)8, (byte)0), emulator.DisplayBackground);
    }
}
