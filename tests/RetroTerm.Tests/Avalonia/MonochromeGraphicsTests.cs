using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A single-phosphor screen has ONE GUN, and that now applies to drawings as well as text.
///
/// It could not show a blue line whatever the host asked for; it showed a dim one. Collapsing the
/// sixteen text colours while leaving Sixel and ReGIS in full colour put a picture on screen the
/// hardware could not have produced — which is what a VT240, a monochrome ReGIS machine, makes
/// obvious.
/// </summary>
[Collection("Avalonia")]
public class MonochromeGraphicsTests
{
    private static readonly (byte R, byte G, byte B) Amber = (255, 176, 0);
    private static readonly (byte R, byte G, byte B) AmberBackground = (10, 8, 0);

    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    /// <summary>
    /// A VT240 with a red, a green and a blue bar drawn in ReGIS.
    /// </summary>
    private static TerminalEmulatorBase WithThreeColouredBars()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT240", 80, 24, 100);
        Feed(emulator, "\x1b[?25l");
        Feed(emulator,
            "\x1bPp" +
            "W(I2)P[100,100]V[700,100]P[100,101]V[700,101]P[100,102]V[700,102]" +
            "W(I3)P[100,200]V[700,200]P[100,201]V[700,201]P[100,202]V[700,202]" +
            "W(I1)P[100,300]V[700,300]P[100,301]V[700,301]P[100,302]V[700,302]" +
            "\x1b\\");
        return emulator;
    }

    [AvaloniaFact]
    public void OnAColourScreenTheDrawingKeepsItsColours()
    {
        // The guard against overreach: a colour theme must leave a drawing exactly as the host
        // sent it.
        using var shot = RenderedScreenshot.Capture(WithThreeColouredBars(), "regis-colour-screen",
            configureRenderer: r => r.SetTheme(TerminalTheme.Colour("Amber", Amber, AmberBackground)));

        var red = shot.BrightestColorInCell(5, 20);
        Assert.True(red.Red > red.Blue, $"a colour theme must not collapse the drawing; got {red}");
    }

    [AvaloniaFact]
    public void OnASinglePhosphorScreenEveryLineIsThePhosphor()
    {
        using var shot = RenderedScreenshot.Capture(WithThreeColouredBars(), "regis-single-phosphor",
            configureRenderer: r => r.SetTheme(TerminalTheme.Monochrome("Amber", Amber, AmberBackground)));

        // All three bars, and not one of them blue: an amber gun cannot draw blue.
        for (int row = 5; row <= 15; row += 5)
        {
            var ink = shot.BrightestColorInCell(row, 20);
            if (ink.Red == 0 && ink.Green == 0 && ink.Blue == 0) continue;

            Assert.True(ink.Red >= ink.Blue,
                $"row {row} should be amber on a single-phosphor screen; got {ink}");
        }
    }

    [AvaloniaFact]
    public void ASinglePhosphorScreenStillSeparatesBrightFromDim()
    {
        // Collapsing hue must not collapse INTENSITY too, or a picture becomes a silhouette.
        // Blue is the dimmest thing on any screen by the luma weights, green the brightest.
        var emulator = WithThreeColouredBars();

        using var shot = RenderedScreenshot.Capture(emulator, "regis-phosphor-intensity",
            configureRenderer: r => r.SetTheme(TerminalTheme.Monochrome("Amber", Amber, AmberBackground)));

        var green = shot.BrightestColorInCell(10, 20);
        var blue = shot.BrightestColorInCell(15, 20);

        Assert.True(green.Red > blue.Red,
            $"green should collapse brighter than blue; got green {green} and blue {blue}");
    }

    [AvaloniaFact]
    public void TheVt240SaysWhatItIs()
    {
        // 62 is the VT200 family, 3 is ReGIS, 4 is Sixel.
        //
        // THE 4 WAS MISSING, and this test used to say why: "no source in this repository confirms
        // a VT240 had it, and a DA reply is not the place to guess." A source does now. The
        // VT330/VT340 Text Programming manual lists the alias replies a VT300 sends when told to
        // identify as an earlier terminal, and its VT240 line is
        // "CSI ? 62; 1; 2; 3; 4; 6; 7; 8; 9 c".
        //
        // The 2 and the 9 are still absent, and for the original reason rather than for want of a
        // document: there is no printer port here and no national replacement character sets.
        var emulator = EmulatorFactory.CreateEmulator("VT240", 80, 24, 100);
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[c");

        Assert.Equal("\x1b[?62;1;3;4;6;7;8c", replies.ToString());
    }

    [AvaloniaFact]
    public void AndItDrawsBothReGisAndSixel()
    {
        // A claim in a DA reply that the terminal cannot honour is worse than no claim, so the two
        // are checked together: it says 3 and 4, and it draws both.
        var withRegis = EmulatorFactory.CreateEmulator("VT240", 80, 24, 100);
        Feed(withRegis, "\x1bPpP[10,10]V[100,10]\x1b\\");
        Assert.True(withRegis.Graphics?.HasAnythingToDraw() ?? false);

        var withSixel = EmulatorFactory.CreateEmulator("VT240", 80, 24, 100);
        Feed(withSixel, "\x1bPq#1~\x1b\\");
        Assert.True(withSixel.Graphics?.HasAnythingToDraw() ?? false);
    }
}
