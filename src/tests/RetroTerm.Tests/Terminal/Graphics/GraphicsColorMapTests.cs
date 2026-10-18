using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The one colour map both graphics languages write to.
/// </summary>
/// <remarks>
/// <para><b>The default map is checked against the page, not against itself</b></para>
/// Table 2-3, "VT340 Default Color Map", in
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>. The table prints each
/// entry in HLS and in RGB percentages, so every expectation below can be read off the page - and
/// the two systems check each other, because converting the HLS has to land on the RGB.
///
/// <para><b>What this replaced</b></para>
/// Two private palettes, one per decoder, both wrong and wrong differently. The Sixel one made
/// entries 9 to 14 BRIGHTER than 1 to 6; the manual's own footnote says "These colors are less
/// saturated than colors 1 through 6" and gives them lower numbers. The ReGIS one used fully
/// saturated primaries, so its blue was 0,0,255 where a VT340's is 20,20,80 percent.
/// </remarks>
public class GraphicsColorMapTests
{
    [Fact]
    public void TheDefaultMapIsTheOneInTableTwoThree()
    {
        var map = new GraphicsColorMap();

        // Location 0 black, 1 blue at 20/20/80 percent, 2 red at 80/13/13, 3 green at 20/80/20.
        Assert.Equal(new GraphicsColor(0, 0, 0), map.Register(0));
        Assert.Equal(new GraphicsColor(51, 51, 204), map.Register(1));
        Assert.Equal(new GraphicsColor(204, 33, 33), map.Register(2));
        Assert.Equal(new GraphicsColor(51, 204, 51), map.Register(3));

        // 7 is gray 50 percent, 8 gray 25 percent, 15 gray 75 percent.
        Assert.Equal(new GraphicsColor(135, 135, 135), map.Register(7));
        Assert.Equal(new GraphicsColor(66, 66, 66), map.Register(8));
        Assert.Equal(new GraphicsColor(204, 204, 204), map.Register(15));
    }

    [Fact]
    public void TheSecondSetOfColoursIsDULLERThanTheFirst()
    {
        // The footnote under the table: "These colors are less saturated than colors 1 through 6."
        // The old Sixel palette had these BRIGHTER, which is the defect this pins. Blue at 9 is
        // 33/33/60 percent against blue at 1 being 20/20/80 - less blue, more of everything else.
        var map = new GraphicsColorMap();

        var blue = map.Register(1);
        var dullBlue = map.Register(9);

        Assert.True(dullBlue.B < blue.B, "the less saturated blue must be less blue, not more");
        Assert.True(dullBlue.R > blue.R, "and it must carry more of the other channels");
        Assert.Equal(new GraphicsColor(84, 84, 153), dullBlue);
    }

    [Fact]
    public void TheHlsInTheTableAgreesWithTheRgbInTheTable()
    {
        // Each row is printed both ways, so converting one must land on the other. This is what
        // pins the hue convention: DEC's 0 degrees is BLUE.
        //
        //   1  blue   H 0   L 50 S 60  ->  20 20 80
        //   3  green  H 240 L 50 S 60  ->  20 80 20
        //   6  yellow H 180 L 50 S 60  ->  80 80 20
        Assert.Equal(new GraphicsColor(51, 51, 204), GraphicsColorMap.FromHls(0, 50, 60));
        Assert.Equal(new GraphicsColor(51, 204, 51), GraphicsColorMap.FromHls(240, 50, 60));
        Assert.Equal(new GraphicsColor(204, 204, 51), GraphicsColorMap.FromHls(180, 50, 60));
    }

    [Fact]
    public void ARegisterOutsideTheFileIsTransparentRatherThanAThrow()
    {
        var map = new GraphicsColorMap();

        Assert.True(map.Register(-1).IsTransparent);
        Assert.True(map.Register(GraphicsColorMap.RegisterCount).IsTransparent);

        // And writing to one is ignored: a mangled stream should not lose the rest of a picture.
        map.SetFromRgbPercent(-1, 100, 0, 0);
        map.SetFromHls(GraphicsColorMap.RegisterCount, 120, 50, 100);
    }

    [Fact]
    public void ARegisPaletteReachesASixelImage()
    {
        // THE POINT OF THE WHOLE CHANGE. hackerb9's cat-original.six sets its palette with ReGIS
        // and then sends a Sixel image that defines no colours of its own. With a register file per
        // decoder that image drew in the power-on colours and every colour in it was wrong.
        //
        // ESC P p ... ESC \  is ReGIS; S(M2(H120L50S100)) sets map location 2 to pure red.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 20, 6, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?25l\x1b[1;1H"));
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bPpS(M2(H120L50S100))\x1b\\"));

        // Now a Sixel using register 2 and defining nothing.
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bPq#2!8~\x1b\\"));

        var surface = emulator.Graphics!.Output;
        var pixel = surface.GetPixel(0, 0);

        Assert.False(pixel.IsTransparent, "the image should have been drawn");
        Assert.True(pixel.R > pixel.G && pixel.R > pixel.B,
            $"register 2 was set to pure red through ReGIS; the Sixel drew {pixel}");
        Assert.Equal(255, pixel.R);
    }

    [Fact]
    public void AndTheSixelPaletteReachesAReGisDrawing()
    {
        // The same seam in the other direction, because one shared map means either can write it.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 20, 6, 100);
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?25l"));

        // Sixel defines register 5 as pure blue, in RGB percent.
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bPq#5;2;0;0;100~\x1b\\"));

        // ReGIS then draws a line in register 5.
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bPpW(I5)P[10,10]V[60,10]\x1b\\"));

        var surface = emulator.Graphics!.Output;
        var pixel = surface.GetPixel(30, 10);

        Assert.False(pixel.IsTransparent, "the line should have been drawn");
        Assert.True(pixel.B > pixel.R && pixel.B > pixel.G,
            $"register 5 was set to blue by the Sixel; ReGIS drew {pixel}");
    }
}
