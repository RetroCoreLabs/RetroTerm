using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// A colour introducer that carries fewer than five numbers.
/// </summary>
/// <remarks>
/// <para><b>What the manuals say, quoted</b></para>
/// Two DEC documents held in <c>spec\DEC\</c> agree, and both were read for this rather than
/// recalled:
///  - VT330/VT340 Programmer Reference Volume 2, page 236, on the parameter separator: "If there is
///    no number before the separator, the terminal assumes that parameter is 0." The same rule is
///    printed for a number missing after it.
///  - Level 2 Sixel Programming Reference, DECGCI Error Handling: "If HLS is selected, and hue,
///    lightness, or saturation is omitted, a value of 0 is assumed. If RGB is selected, and red,
///    green, or blue is omitted, a value of 0 is assumed."
/// So a SHORT definition is still a definition. Only a bare colour number is a selection: "Once the
/// assignment has been made, Pc can be specified alone to select a color."
/// <para><b>What we did before 26 August 2026</b></para>
/// Anything with fewer than five numbers was treated as a selection and defined nothing. A host
/// sending the perfectly legal <c>#2;1;120;50</c> got its register left at the power-on colour and
/// its picture drawn in the wrong one - silently, because a wrong colour still draws.
/// <para><b>Blast radius, measured before the change</b></para>
/// Every Sixel stream in the fetched corpus was scanned for short introducers. There is exactly
/// ONE, <c>#1;10;0;0</c> in <c>cat-vt240.six</c>, and it is unaffected because its coordinate
/// system is neither HLS nor RGB. So no fixture moves; this is cover for hosts we have not met.
/// </remarks>
public class SixelShortColourIntroducerTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A VT340 with the cursor hidden, so the only ink is the image.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?25l"));
        return emulator;
    }

    /// <summary>
    /// Runs one Sixel string and reads the colour of the pixel it paints at the origin.
    /// </summary>
    /// <param name="body">
    /// What goes between the introducer and the terminator.
    /// </param>
    /// <returns>
    /// The colour at plane pixel 0,0.
    /// </returns>
    private static RetroTerm.Core.Terminal.Graphics.GraphicsColor PaintedColour(string body)
    {
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "Pq" + body + Escape + "\\"));

        var plane = emulator.Graphics!.FindPlane("sixel");
        Assert.NotNull(plane);

        var drawn = plane!.Surface.GetPixel(0, 0);
        Assert.False(drawn.IsTransparent, "nothing was drawn at all");
        return drawn;
    }

    [Fact]
    public void AColourNumberOnItsOwnSelectsAndDefinesNothing()
    {
        // The one case that really IS a selection. Register 3 is green in DEC's Table 2-3, so if a
        // bare number ever started defining, this would go black and say so.
        var drawn = PaintedColour("#3@");

        Assert.True(drawn.G > drawn.R && drawn.G > drawn.B,
            "expected register 3's default green, got " + drawn.R + "," + drawn.G + "," + drawn.B);
    }

    [Fact]
    public void AnOmittedSaturationIsTakenAsZero()
    {
        // #1;1;120;50 - HLS, hue 120, lightness 50, saturation MISSING. Saturation zero is the grey
        // path whatever the hue is, so this must come out a mid grey and not a green.
        var drawn = PaintedColour("#1;1;120;50" + "#1@");

        Assert.Equal(drawn.R, drawn.G);
        Assert.Equal(drawn.G, drawn.B);
        Assert.True(drawn.R > 0, "lightness 50 is a grey, not black");
    }

    [Fact]
    public void AnOmittedLightnessAndSaturationAreBothTakenAsZero()
    {
        // #1;1;120 - only the hue survives. Lightness zero is black no matter what the hue was.
        var drawn = PaintedColour("#1;1;120" + "#1@");

        Assert.Equal(0, drawn.R);
        Assert.Equal(0, drawn.G);
        Assert.Equal(0, drawn.B);
    }

    [Fact]
    public void AnOmittedBlueIsTakenAsZero()
    {
        // #1;2;100;100 - RGB with blue missing. Red and green at full, no blue, is yellow. Picked
        // because a wrong reading gives blue a value, and yellow going white would show it.
        var drawn = PaintedColour("#1;2;100;100" + "#1@");

        Assert.Equal(0, drawn.B);
        Assert.True(drawn.R > 200 && drawn.G > 200,
            "expected a yellow, got " + drawn.R + "," + drawn.G + "," + drawn.B);
    }

    [Fact]
    public void AnUnknownCoordinateSystemDefinesNothing()
    {
        // THE cat-vt240 CASE, pinned. Its second colour is #1;10;0;0 and 10 is neither HLS nor RGB.
        // The Level 2 reference's table of coordinate systems ends "Other -- Ignore sequence", so
        // register 1 keeps the power-on blue of DEC's Table 2-3 and the cat comes out blue.
        //
        // The real hardware capture beside that fixture draws it GREEN, which is register 0's
        // colour. We cannot explain that and do not copy it: hackerb9's own sixelcomments.md records
        // that this picture exists in a ReGIS-palette version because "early VT240 firmware did not
        // handle sixel color palettes correctly", so the photograph may be a record of that.
        var drawn = PaintedColour("#0;1;280;35;60" + "#1;10;0;0" + "#1@");

        Assert.True(drawn.B > drawn.R && drawn.B > drawn.G,
            "expected register 1's power-on blue, got " + drawn.R + "," + drawn.G + "," + drawn.B);
    }
}
