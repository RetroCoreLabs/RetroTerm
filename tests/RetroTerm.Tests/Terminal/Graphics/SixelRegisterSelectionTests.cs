using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// Which colour register a Sixel image ends up drawing in.
/// </summary>
/// <remarks>
/// <para><b>THE DEFECT THESE WERE WRITTEN FOR WAS NOT REAL. Settled 27 August 2026.</b></para>
/// This file used to open by saying that `compare-cat-original.png` showed the cat's hat and coat
/// RED where the hardware capture had them BLACK, and that the remaining question was which
/// register was being selected. Both halves of that were wrong, and the reason is worth keeping.
///
/// The comparison sheets were built from a capture taken at 616 by 393 - the size a VT340 comes out
/// when it draws through Consolas at 14 point - and stacked against an 800 by 480 hardware
/// photograph. Everything in our half was therefore RESAMPLED, and a black coat beside a white
/// stripe blurs to a mid tone that reads as red against a green ground. Measured once the capture
/// was taken at the hardware's own 10 by 20 cell: our half went from 1,940 distinct colours to 15,
/// against the photograph's 21, and the hat and coat came out BLACK in both. `cat-original` and
/// `cat-vt340` now agree with the hardware apart from the background green being five units off in
/// G and B, and white being 252 rather than 255.
///
/// So there is no selection defect. An open bug had been carried in this repository since 21 August
/// on the strength of a picture that was never at the right resolution to judge - which is the same
/// lesson as the axis labels and the swapped red and blue, arriving from the opposite direction:
/// looking at the picture found a defect that was not there.
///
/// <para><b>What the tests below are still worth</b></para>
/// They pin the behaviour that turned out to be correct all along: a register defined black in the
/// stream draws black, one defined red draws red, and a register written by ReGIS is the same
/// register Sixel then selects. Keep them. They are cheap, and they are what would catch a real
/// selection defect if one ever appeared.
/// </remarks>
public class SixelRegisterSelectionTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A VT340.
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
    /// Reads a pixel from the Sixel plane.
    /// </summary>
    /// <param name="emulator">
    /// The terminal.
    /// </param>
    /// <param name="x">
    /// Plane column.
    /// </param>
    /// <param name="y">
    /// Plane row.
    /// </param>
    /// <returns>
    /// The colour there.
    /// </returns>
    private static RetroTerm.Core.Terminal.Graphics.GraphicsColor SixelPixel(
        TerminalEmulatorBase emulator, int x, int y)
    {
        var plane = emulator.Graphics!.FindPlane("sixel");
        Assert.NotNull(plane);
        return plane!.Surface.GetPixel(x, y);
    }

    [Fact]
    public void ARegisterDefinedBlackInTheSixelStreamDrawsBlack()
    {
        // cat-vt340.six's own definition, drawn with cat-vt340.six's own selection. If this comes
        // out red then the register the image asked for is not the register it got, and that is the
        // whole of the remaining defect.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "#1;1;0;0;0" + "#2;1;120;50;100" + "#1@" + Escape + "\\"));

        var drawn = SixelPixel(emulator, 0, 0);

        Assert.False(drawn.IsTransparent, "nothing was drawn at all");
        Assert.Equal(0, drawn.R);
        Assert.Equal(0, drawn.G);
        Assert.Equal(0, drawn.B);
    }

    [Fact]
    public void ARegisterDefinedRedDrawsRed()
    {
        // The other half of the same question, so a failure above cannot be read as "everything is
        // black". Register 2 is the red both fixtures define.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "#1;1;0;0;0" + "#2;1;120;50;100" + "#2@" + Escape + "\\"));

        var drawn = SixelPixel(emulator, 0, 0);

        Assert.False(drawn.IsTransparent, "nothing was drawn at all");
        Assert.True(drawn.R > drawn.G && drawn.R > drawn.B,
            "expected a red, got " + drawn.R + "," + drawn.G + "," + drawn.B);
    }

    [Fact]
    public void SelectingARegisterDefinedThroughRegisDrawsThatColour()
    {
        // cat-original's route: the palette arrives by ReGIS and the image that follows defines no
        // colours of its own. A VT340 has ONE colour map that both languages write to, so selecting
        // register 1 here has to give the black ReGIS just put there.
        var emulator = NewVt340();

        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pp" + "S(M1(H0L0S0))" + Escape + "\\"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pq" + "#1@" + Escape + "\\"));

        var drawn = SixelPixel(emulator, 0, 0);

        Assert.False(drawn.IsTransparent, "nothing was drawn at all");
        Assert.Equal(0, drawn.R);
        Assert.Equal(0, drawn.G);
        Assert.Equal(0, drawn.B);
    }
}
