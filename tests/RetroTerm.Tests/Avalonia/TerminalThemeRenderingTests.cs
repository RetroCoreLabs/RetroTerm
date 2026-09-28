using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Presentation themes on real pixels.
///
/// The seven colour presets have always set the default foreground and background and nothing else,
/// so the sixteen ANSI colours were hard-wired to xterm's values: an amber terminal drew a host's
/// <c>SGR 32</c> in xterm green, on an amber screen. A single-gun CRT could not do that — it had
/// one phosphor, and sixteen colours arrived as sixteen brightnesses.
///
/// These tests check the drawn pixels, because "is the green text actually amber now" is a question
/// about what reached the screen, not about what a flag says.
/// </summary>
[Collection("Avalonia")]
public class TerminalThemeRenderingTests
{
    private static readonly (byte R, byte G, byte B) Amber = (0xFF, 0xBF, 0x00);
    private static readonly (byte R, byte G, byte B) AmberBackground = (0x0A, 0x08, 0x00);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Green text on row 0, cursor parked out of the way.
    /// </summary>
    private static VT100Emulator GreenText()
    {
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "\u001b[32mMMMM");
        Feed(emulator, "\u001b[4;20H");
        return emulator;
    }

    [AvaloniaFact]
    public void WithoutAThemeGreenTextIsDrawnGreen()
    {
        // The control, and the guard against overreach: an ordinary colour preset must keep drawing
        // the canonical palette exactly as it always has.
        using var shot = RenderedScreenshot.Capture(GreenText(), "theme-colour-green",
            configureRenderer: r => r.SetTheme(
                TerminalTheme.Colour("Amber", Amber, AmberBackground)));

        var ink = shot.BrightestColorInCell(0, 0);
        Assert.True(ink.Green > ink.Red, "canonical green must still be drawn green");
        Assert.True(ink.Green > 100, $"the glyph should be clearly green, got {ink}");
    }

    [AvaloniaFact]
    public void OnASinglePhosphorScreenGreenTextIsDrawnInThePhosphor()
    {
        // THE test. Same bytes from the host, same amber screen, and now the text is amber.
        using var shot = RenderedScreenshot.Capture(GreenText(), "theme-mono-amber",
            configureRenderer: r => r.SetTheme(
                TerminalTheme.Monochrome("Amber", Amber, AmberBackground)));

        var ink = shot.BrightestColorInCell(0, 0);
        Assert.True(ink.Red > ink.Green, $"amber has more red than green; got {ink}");
        Assert.True(ink.Blue < 40, $"a single-phosphor amber screen cannot draw blue; got {ink}");
    }

    [AvaloniaFact]
    public void ASinglePhosphorScreenStillSeparatesBrightFromDim()
    {
        // Collapsing hues must not collapse INTENSITY too, or bold and normal text become
        // indistinguishable and a host's emphasis is lost entirely.
        var emulator = new VT100Emulator(20, 4);
        Feed(emulator, "\u001b[34mB\u001b[97mW");     // dim blue, then bright white
        Feed(emulator, "\u001b[4;20H");

        using var shot = RenderedScreenshot.Capture(emulator, "theme-mono-intensity",
            configureRenderer: r => r.SetTheme(
                TerminalTheme.Monochrome("Amber", Amber, AmberBackground)));

        var dim = shot.BrightestColorInCell(0, 0);
        var bright = shot.BrightestColorInCell(0, 1);

        Assert.True(bright.Red > dim.Red,
            $"bright white must come out brighter than dim blue; dim={dim} bright={bright}");
    }

    [AvaloniaFact]
    public void ChangingTheThemeRepaintsTheScreen()
    {
        // The row cache diffs cell CONTENT, and a theme change touches no cell at all - so without
        // an explicit invalidation the screen would keep the previous theme until something else
        // happened to change a row. That is exactly the sort of staleness a cache invites.
        var emulator = GreenText();

        using var beforeTheme = RenderedScreenshot.Capture(emulator, null,
            configureRenderer: r => r.SetTheme(
                TerminalTheme.Colour("Amber", Amber, AmberBackground)));
        using var afterTheme = RenderedScreenshot.Capture(emulator, null,
            configureRenderer: r => r.SetTheme(
                TerminalTheme.Monochrome("Amber", Amber, AmberBackground)));

        Assert.True(afterTheme.FractionDifferentFrom(beforeTheme) > 0.0,
            "switching to a single-phosphor theme must actually change the picture");
    }
}
