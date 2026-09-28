using System;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Sixel and ReGIS pictures move with the text when the screen scrolls, and are wiped by
/// a full-screen erase.
///
/// Ronny's call, 9 September 2026, after finding the clear half live on 31 August: he ran
/// a shell's clear after a Sixel picture and the picture stayed put. A real VT340 keeps
/// text and graphics in ONE bitmap, so both move and both are erased. Here they are
/// separate surfaces, and the planes used to be touched by neither ScrollUp nor ED.
///
/// The deliberate limits are tested too. A scroll REGION does not move the picture,
/// because the planes have no notion of a region and sliding the whole picture would drag
/// graphics past rows that never moved. And only ED mode 2 clears: modes 0 and 1 erase
/// relative to the cursor, which the planes cannot express, and mode 3 is the scrollback.
/// </summary>
public class GraphicsScrollAndClearTests
{
    private static TerminalEmulatorBase NewEmulatorWithGraphics(int cols = 80, int rows = 24)
    {
        // A VT340 is the terminal that has graphics; a plain VT100 never builds a
        // compositor, so it cannot answer this question either way.
        var emulator = EmulatorFactory.CreateEmulator("VT340", cols, rows, 100);
        // Any Sixel starts the compositor; this is the shortest thing that draws.
        Feed(emulator, Esc + "Pq#1;2;0;0;100!8~" + Esc + "\\");
        Assert.NotNull(emulator.Graphics);
        return emulator;
    }

    /// <summary>The escape byte, named rather than written as a hex escape.</summary>
    private const string Esc = "\u001b";

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>Paints one solid row of pixels so a move is easy to see.</summary>
    private static void PaintRow(InMemoryGraphicsSurface surface, int y, uint argb)
    {
        for (int x = 0; x < surface.Width; x++) surface.SetPixel(x, y, new GraphicsColor(argb));
    }

    private static uint PixelAt(InMemoryGraphicsSurface surface, int x, int y)
        => surface.Pixels[y * surface.Width + x];

    [Fact]
    public void AWholeScreenScrollMovesThePictureUpByOneTextRow()
    {
        var emulator = NewEmulatorWithGraphics();
        var surface = emulator.Graphics!.PlaneAt(0).Surface;
        int cell = emulator.GraphicsCellHeight;

        surface.Clear(GraphicsColor.Transparent);
        PaintRow(surface, cell * 3, 0xFF00FF00);          // a green line three text rows down

        // Put the cursor on the last row and feed a line feed: a whole-screen scroll.
        Feed(emulator, Esc + "[" + emulator.Height + ";1H\n");

        Assert.Equal(0u, PixelAt(surface, 0, cell * 3));
        Assert.Equal(0xFF00FF00u, PixelAt(surface, 0, cell * 2));
    }

    [Fact]
    public void TheRowsExposedAtTheBottomComeBackTransparent()
    {
        var emulator = NewEmulatorWithGraphics();
        var surface = emulator.Graphics!.PlaneAt(0).Surface;

        surface.Clear(GraphicsColor.Transparent);
        for (int y = 0; y < surface.Height; y++) PaintRow(surface, y, 0xFFFF0000);

        Feed(emulator, Esc + "[" + emulator.Height + ";1H\n");

        // The bottom cell's worth must be empty, or the picture smears as it scrolls.
        int cell = emulator.GraphicsCellHeight;
        Assert.Equal(0u, PixelAt(surface, 0, surface.Height - 1));
        Assert.Equal(0u, PixelAt(surface, 0, surface.Height - cell));
        Assert.Equal(0xFFFF0000u, PixelAt(surface, 0, surface.Height - cell - 1));
    }

    [Fact]
    public void AScrollInsideARegionLeavesThePictureAlone()
    {
        var emulator = NewEmulatorWithGraphics();
        var surface = emulator.Graphics!.PlaneAt(0).Surface;
        int cell = emulator.GraphicsCellHeight;

        surface.Clear(GraphicsColor.Transparent);
        PaintRow(surface, cell * 3, 0xFF00FF00);

        // A scroll region over the top half, then scroll inside it.
        Feed(emulator, Esc + "[1;10r");
        Feed(emulator, Esc + "[10;1H\n");

        Assert.Equal(0xFF00FF00u, PixelAt(surface, 0, cell * 3));
    }

    [Fact]
    public void AFullScreenEraseWipesThePicture()
    {
        var emulator = NewEmulatorWithGraphics();
        var surface = emulator.Graphics!.PlaneAt(0).Surface;

        surface.Clear(GraphicsColor.Transparent);
        PaintRow(surface, 5, 0xFF00FF00);
        Assert.Equal(0xFF00FF00u, PixelAt(surface, 0, 5));

        Feed(emulator, Esc + "[2J");        // what a shell's clear sends

        Assert.Equal(0u, PixelAt(surface, 0, 5));
    }

    [Fact]
    public void EraseToOrFromTheCursorLeavesThePictureAlone()
    {
        // Modes 0 and 1 erase relative to the cursor. The planes cannot express that, and
        // wiping the whole picture would destroy far more than was asked for.
        var emulator = NewEmulatorWithGraphics();
        var surface = emulator.Graphics!.PlaneAt(0).Surface;

        surface.Clear(GraphicsColor.Transparent);
        PaintRow(surface, 5, 0xFF00FF00);

        Feed(emulator, Esc + "[12;1H" + Esc + "[0J");
        Assert.Equal(0xFF00FF00u, PixelAt(surface, 0, 5));

        Feed(emulator, Esc + "[12;1H" + Esc + "[1J");
        Assert.Equal(0xFF00FF00u, PixelAt(surface, 0, 5));
    }

    [Fact]
    public void ErasingTheScrollbackLeavesThePictureAlone()
    {
        // Mode 3 is the saved lines. A picture on screen is not part of the scrollback.
        var emulator = NewEmulatorWithGraphics();
        var surface = emulator.Graphics!.PlaneAt(0).Surface;

        surface.Clear(GraphicsColor.Transparent);
        PaintRow(surface, 5, 0xFF00FF00);

        Feed(emulator, Esc + "[3J");

        Assert.Equal(0xFF00FF00u, PixelAt(surface, 0, 5));
    }

    [Fact]
    public void AnImageTallerThanTheScreenLeavesItsBottomEndOnThePlane()
    {
        // The case that broke when the picture started moving with the text. An image taller
        // than the screen scrolls the terminal while it is being received, so what is left on
        // screen is the BOTTOM of it. Painting the whole image at the cursor and scrolling
        // afterwards cannot show that: the plane is only as tall as the screen, so everything
        // past its bottom edge is clipped away before the scroll can move it, and the scroll
        // then carries the rest off the top. vaxrgl-lntest.six from the VT340 corpus came out
        // completely blank that way.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        // Aspect parameter 9 means one screen row per sixel row, so the arithmetic below is
        // plain: 120 bands of six rows each is 720 pixel rows against a 480-row plane.
        var image = new StringBuilder();
        image.Append(Esc).Append("P9;0;2q");
        image.Append("#0;2;100;0;0#1;2;0;100;0");
        for (int band = 0; band < 119; band++)
        {
            image.Append("#0!20~-");
        }
        image.Append("#1!20~");          // the last band, in a colour of its own
        image.Append(Esc).Append('\\');

        Feed(emulator, image.ToString());

        var surface = emulator.Graphics!.PlaneAt(0).Surface;
        Assert.True(emulator.Graphics!.HasAnythingToDraw(),
            "a tall image must not scroll itself off the plane entirely");

        // Find the green band. It is the last six rows of the picture, so it belongs near the
        // bottom of the plane - not at the top, which is where an unshifted paint would put it.
        const uint Green = 0xFF00FF00u;
        int firstGreenRow = -1;
        for (int y = 0; y < surface.Height && firstGreenRow < 0; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (PixelAt(surface, x, y) == Green) { firstGreenRow = y; break; }
            }
        }

        Assert.True(firstGreenRow >= 0, "the last band of the image never reached the plane");
        Assert.True(firstGreenRow >= surface.Height - 2 * emulator.GraphicsCellHeight,
            $"the last band landed at row {firstGreenRow}, not near the bottom of the plane");
    }

    [Fact]
    public void ScrollingFurtherThanTheSurfaceIsHighClearsItRatherThanReadingPastTheEnd()
    {
        var surface = new InMemoryGraphicsSurface(16, 8);
        for (int y = 0; y < surface.Height; y++) PaintRow(surface, y, 0xFF0000FF);

        surface.ScrollUp(surface.Height + 5);

        for (int y = 0; y < surface.Height; y++)
        {
            Assert.Equal(0u, PixelAt(surface, 0, y));
        }
    }

    [Fact]
    public void ScrollingByNothingChangesNothing()
    {
        var surface = new InMemoryGraphicsSurface(16, 8);
        PaintRow(surface, 2, 0xFF0000FF);

        surface.ScrollUp(0);
        surface.ScrollUp(-3);

        Assert.Equal(0xFF0000FFu, PixelAt(surface, 0, 2));
    }
}
