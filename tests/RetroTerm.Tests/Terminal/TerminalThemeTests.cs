using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Presentation themes, and the line between them and the canonical palette.
///
/// <see cref="TerminalPalette"/> says what an index MEANS — index 2 is green, the xterm value, the
/// number a host sending <c>SGR 32</c> is entitled to assume. A theme says only what gets DRAWN.
/// Keeping them apart is what lets a screen read back over MCP or a script report the colour the
/// host asked for rather than the shade a phosphor theme happened to paint.
///
/// The interesting case is a single-phosphor screen. A real amber or green terminal had one gun and
/// no way to show a second hue, so sixteen colours arrived as sixteen BRIGHTNESSES. That is what a
/// monochrome theme reproduces.
/// </summary>
public class TerminalThemeTests
{
    private static readonly (byte R, byte G, byte B) Amber = (0xFF, 0xBF, 0x00);
    private static readonly (byte R, byte G, byte B) AmberBackground = (0x0A, 0x08, 0x00);

    private static TerminalTheme AmberMono()
        => TerminalTheme.Monochrome("Amber", Amber, AmberBackground);

    /// <summary>
    /// Brightness by the same weights the theme uses, for ordering assertions.
    /// </summary>
    private static double Luma((byte R, byte G, byte B) c)
        => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    // ─────────────────────────────────────────────────────────────
    // A colour theme changes nothing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AColourThemeLeavesEveryIndexAlone()
    {
        // The guard that matters most: adding themes must not change how anything looks today.
        var theme = TerminalTheme.Colour("Amber", Amber, AmberBackground);

        for (int i = 0; i <= 255; i++)
        {
            Assert.False(theme.TryGetBaseColour((byte)i, out _),
                $"a colour theme must decline index {i} so the canonical palette is used");
        }
    }

    [Fact]
    public void AThemeCarriesItsDefaultColours()
    {
        var theme = TerminalTheme.Colour("Amber", Amber, AmberBackground);

        Assert.Equal(Amber, theme.DefaultForeground);
        Assert.Equal(AmberBackground, theme.DefaultBackground);
        Assert.False(theme.IsMonochrome);
    }

    // ─────────────────────────────────────────────────────────────
    // A monochrome theme collapses the sixteen onto one phosphor
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnlyTheSixteenBaseColoursAreThemed()
    {
        // The 6x6x6 cube and the grey ramp are addressed by their exact RGB. Remapping those would
        // be a lie about what the host asked for, not a presentation choice.
        var theme = AmberMono();

        for (int i = 0; i <= 15; i++)
        {
            Assert.True(theme.TryGetBaseColour((byte)i, out _), $"index {i} should be themed");
        }
        for (int i = 16; i <= 255; i++)
        {
            Assert.False(theme.TryGetBaseColour((byte)i, out _), $"index {i} must not be themed");
        }
    }

    [Fact]
    public void BlackLandsOnTheBackgroundAndWhiteOnThePhosphor()
    {
        var theme = AmberMono();

        Assert.True(theme.TryGetBaseColour(0, out var black));
        Assert.True(theme.TryGetBaseColour(15, out var white));

        Assert.Equal(AmberBackground, black);
        Assert.Equal(Amber, white);
    }

    [Fact]
    public void EveryThemedColourIsAShadeOfTheOnePhosphor()
    {
        // "Single phosphor" is the whole point: nothing may come out a different hue. Every result
        // has to sit on the line between the background and the phosphor colour, which for amber
        // means blue stays at zero and red never exceeds green's share.
        var theme = AmberMono();

        for (int i = 0; i <= 15; i++)
        {
            Assert.True(theme.TryGetBaseColour((byte)i, out var c));
            Assert.Equal(0, c.B);                       // amber has no blue, so no shade of it may
            Assert.InRange(c.R, AmberBackground.R, Amber.R);
            Assert.InRange(c.G, AmberBackground.G, Amber.G);
        }
    }

    [Fact]
    public void BrighterColoursComeOutBrighter()
    {
        // Bright red (9) is a brighter thing than dark red (1), and the mapping has to keep that
        // ordering or a host's bold/normal distinction disappears on a monochrome screen.
        var theme = AmberMono();

        Assert.True(theme.TryGetBaseColour(1, out var darkRed));
        Assert.True(theme.TryGetBaseColour(9, out var brightRed));

        Assert.True(Luma(brightRed) > Luma(darkRed),
            "bright red must come out brighter than dark red");
    }

    [Fact]
    public void BlueIsDimmerThanGreen()
    {
        // The reason for luma weights rather than a flat average. The eye is far more sensitive to
        // green than to blue, and on real single-phosphor hardware blue text was the dimmest thing
        // on the display. A flat average would make them nearly equal.
        var theme = AmberMono();

        Assert.True(theme.TryGetBaseColour(4, out var blue));    // canonical 0,0,238
        Assert.True(theme.TryGetBaseColour(2, out var green));   // canonical 0,205,0

        Assert.True(Luma(blue) < Luma(green),
            "blue must come out dimmer than green, as it did on the hardware");
    }

    [Fact]
    public void NothingIsDrawnDarkerThanTheBackground()
    {
        // Shades are blended FROM the background rather than scaled towards black. An amber screen's
        // background is a very dark brown, not black, so scaling towards black would put dim text
        // BELOW the surface it sits on and make it invisible.
        var theme = AmberMono();
        double floor = Luma(AmberBackground);

        for (int i = 0; i <= 15; i++)
        {
            Assert.True(theme.TryGetBaseColour((byte)i, out var c));
            Assert.True(Luma(c) >= floor - 0.5,
                $"index {i} came out darker than the background it sits on");
        }
    }

    [Fact]
    public void APaperWhiteScreenInvertsTheDirection()
    {
        // A light-background theme runs the other way: "black" is the darkest ink, "white" is the
        // paper. Blending from the background handles both without a special case, and this is the
        // test that would fail if the mapping assumed a dark screen.
        var ink = ((byte)0x22, (byte)0x22, (byte)0x22);
        var paper = ((byte)0xF0, (byte)0xF0, (byte)0xF5);
        var theme = TerminalTheme.Monochrome("Paper White", ink, paper);

        Assert.True(theme.TryGetBaseColour(0, out var black));
        Assert.True(theme.TryGetBaseColour(15, out var white));

        Assert.Equal(paper, black);   // canonical black is the DIMMEST, which on paper is the page
        Assert.Equal(ink, white);     // canonical white is the BRIGHTEST, which on paper is the ink
    }
}
