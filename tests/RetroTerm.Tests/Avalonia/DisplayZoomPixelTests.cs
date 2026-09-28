using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// What the zoom actually PUTS ON THE SCREEN, in a window that is not the terminal's natural size.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists</b></para>
/// DisplayZoomTests has sixteen cases and not one of them draws a pixel. Every one asserts that an
/// integer property changed - <c>Assert.Equal(125, canvas.ZoomPercent)</c> - so all sixteen would
/// pass with a Render method that drew nothing at all. They tested the number, never the picture.
/// Ronny found three faults by eye that the whole file was blind to: 300 percent showed nothing,
/// leaving full screen showed nothing, and 75 percent looked like it moved the screen rather than
/// scaling it.
/// So every assertion here is measured off rendered pixels in a window-shaped control, which is the
/// only arrangement any of those faults can appear in.
/// </remarks>
[Collection("Avalonia")]
public class DisplayZoomPixelTests
{
    /// <summary>
    /// A window-shaped area to draw into: wider and taller than a natural 80x24, the way a real
    /// window is. The faults only exist in the gap between the two.
    /// </summary>
    private const int WindowWidth = 1200;

    /// <summary>
    /// See <see cref="WindowWidth"/>.
    /// </summary>
    private const int WindowHeight = 700;

    /// <summary>
    /// Renders a canvas at a window size with a given zoom, and hands back the pixels.
    /// </summary>
    /// <param name="zoomPercent">
    /// Zoom to apply, or 0 for fit.
    /// </param>
    /// <param name="content">
    /// What to put on the screen. A full screen shows WHETHER anything is drawn and how big it is;
    /// a single mark shows WHERE the grid landed, and leaves the cursor beside it.
    /// </param>
    /// <param name="saveAs">
    /// PNG name to write into the images folder, or null.
    /// </param>
    /// <returns>
    /// The rendered window.
    /// </returns>
    private static SKBitmap RenderAtZoom(int zoomPercent, ScreenContent content, string? saveAs = null)
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);

        if (content == ScreenContent.FullScreen)
        {
            // Every cell inked, so the drawn area is the grid itself.
            for (int row = 0; row < 24; row++)
            {
                var line = new System.Text.StringBuilder(80);
                for (int col = 0; col < 80; col++) line.Append('X');
                emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes(line.ToString()));
            }
        }
        else if (content == ScreenContent.MarkAtTopLeft)
        {
            // One character in the home cell, which leaves the cursor beside it. That is where a
            // terminal starts and where a login prompt sits.
            emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("X"));
        }
        else
        {
            // A mark in the LAST cell, with the cursor there - the far corner from home, so a view
            // that only ever shows the origin cannot pass by accident.
            var toBottomRight = new byte[]
                { 0x1B, (byte)'[', (byte)'2', (byte)'4', (byte)';', (byte)'8', (byte)'0', (byte)'H', (byte)'X' };
            emulator.ProcessData(toBottomRight);
        }

        emulator.PublishFrame();

        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);
        canvas.ZoomPercent = zoomPercent;

        var size = new Size(WindowWidth, WindowHeight);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));

        using var target = new RenderTargetBitmap(new PixelSize(WindowWidth, WindowHeight), new Vector(96, 96));
        target.Render(canvas);

        byte[] png;
        using (var stream = new MemoryStream())
        {
            target.Save(stream);
            png = stream.ToArray();
        }

        var bitmap = SKBitmap.Decode(png);

        if (!string.IsNullOrEmpty(saveAs))
        {
            var folder = RenderedScreenshot.ImagesFolder;
            Directory.CreateDirectory(folder);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(folder, saveAs + ".png"));
            data.SaveTo(file);
        }

        return bitmap;
    }

    /// <summary>
    /// The smallest rectangle holding every pixel that is not the letterbox colour.
    /// </summary>
    /// <param name="bitmap">
    /// The rendered window.
    /// </param>
    /// <returns>
    /// Left, top, right and bottom of the ink, and how many pixels it holds. All zero when the
    /// window is empty.
    /// </returns>
    private static (int Left, int Top, int Right, int Bottom, int Count) InkBounds(SKBitmap bitmap)
    {
        // The letterbox is whatever is in the extreme corner: outside any terminal at every zoom
        // that does not fill the window, and the page colour when one does. Either way it is the
        // colour that means "nothing was written here".
        var ground = bitmap.GetPixel(0, 0);

        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1, count = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                int d = Math.Abs(c.Red - ground.Red) + Math.Abs(c.Green - ground.Green) + Math.Abs(c.Blue - ground.Blue);
                if (d <= 24) continue;

                count++;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        if (count == 0) return (0, 0, 0, 0, 0);
        return (left, top, right, bottom, count);
    }

    /// <summary>
    /// THE ONE THAT WOULD HAVE CAUGHT IT: a zoom bigger than the window must still show the part of
    /// the screen being written to.
    /// </summary>
    /// <remarks>
    /// Centring a picture that is LARGER than its window throws away the corner everything is in. A
    /// prompt on row 0 lands off the top edge and the reader sees an empty screen - which is what
    /// Ronny saw at 300 percent, and what coming back from full screen did too.
    /// </remarks>
    [AvaloniaFact]
    public void ZoomingPastTheWindowKeepsTheWrittenCornerOnScreen()
    {
        using var bitmap = RenderAtZoom(300, ScreenContent.MarkAtTopLeft, saveAs: "zoom-300-home");
        var ink = InkBounds(bitmap);

        Assert.True(ink.Count > 0, "300 percent drew NOTHING into the window");

        // The mark is one cell at the terminal's home position, and the cursor is beside it. At
        // three times the fitted size a cell is roughly 40 by 90, so the mark has to sit within the
        // first cell or two of the window - i.e. home is on screen rather than off past the corner.
        Assert.True(ink.Left < 120, $"the home cell is not on screen: ink starts at x={ink.Left}");
        Assert.True(ink.Top < 200, $"the home cell is not on screen: ink starts at y={ink.Top}");
    }

    /// <summary>
    /// The view follows the cursor, so writing in the far corner brings that corner into view.
    /// </summary>
    /// <remarks>
    /// Without this, "keep the top-left on screen" would satisfy the test above while leaving the
    /// reader unable to see anything they typed below the fold.
    /// </remarks>
    [AvaloniaFact]
    public void TheViewFollowsTheCursorIntoTheFarCorner()
    {
        using var bitmap = RenderAtZoom(300, ScreenContent.MarkAtBottomRight, saveAs: "zoom-300-far-corner");
        var ink = InkBounds(bitmap);

        Assert.True(ink.Count > 0, "300 percent drew NOTHING into the window");

        // The only thing on screen is the mark in the LAST cell. Seeing it at all means the view
        // moved most of a screen right and down to reach it.
        Assert.True(ink.Right > WindowWidth / 2,
            $"the cursor's own cell is not in view: ink ends at x={ink.Right}");
        Assert.True(ink.Bottom > WindowHeight / 2,
            $"the cursor's own cell is not in view: ink ends at y={ink.Bottom}");
    }

    /// <summary>
    /// A zoom below 100 makes the picture that fraction of the normal view.
    /// </summary>
    /// <remarks>
    /// Measured as a RATIO against the same screen at 100 percent, not against a pixel count worked
    /// out on paper. My first version of this test asserted 600 pixels because I had assumed a
    /// 10-pixel cell; the real VT100 cell is about 7.7 wide and the correct answer was 463. The code
    /// was right and the assertion was wrong, which is the more embarrassing way round. A ratio
    /// cannot be got wrong that way - it needs no arithmetic about fonts at all.
    /// </remarks>
    [AvaloniaFact]
    public void SeventyFivePercentDrawsThreeQuartersOfTheNormalView()
    {
        using var atSeventyFive = RenderAtZoom(75, ScreenContent.FullScreen, saveAs: "zoom-75-full");
        using var atHundred = RenderAtZoom(100, ScreenContent.FullScreen, saveAs: "zoom-100-full");

        var small = InkBounds(atSeventyFive);
        var normal = InkBounds(atHundred);

        Assert.True(small.Count > 0, "75 percent drew NOTHING into the window");
        Assert.True(normal.Count > 0, "100 percent drew NOTHING into the window");

        double smallWidth = small.Right - small.Left + 1;
        double normalWidth = normal.Right - normal.Left + 1;

        double ratio = smallWidth / normalWidth;
        Assert.InRange(ratio, 0.72, 0.78);
    }

    /// <summary>
    /// A CHARACTER at 200 percent is twice the size of the same character at 100 percent. Pins the
    /// magnification itself, so a zoom that quietly stopped scaling cannot pass.
    /// </summary>
    /// <remarks>
    /// Measured on ONE glyph rather than on the whole screen. A full screen already fills the window
    /// at 100 percent, so above that its ink is clipped to the window edge and every zoom measures
    /// the same 1200 pixels - which is how my first version of this test failed while the code was
    /// right. A single character is never clipped, so its size is the honest signal.
    /// </remarks>
    [AvaloniaFact]
    public void TheDrawnSizeTracksTheZoom()
    {
        using var atHundred = RenderAtZoom(100, ScreenContent.MarkAtTopLeft);
        using var atTwoHundred = RenderAtZoom(200, ScreenContent.MarkAtTopLeft);

        var small = InkBounds(atHundred);
        var large = InkBounds(atTwoHundred);

        double smallHeight = small.Bottom - small.Top + 1;
        double largeHeight = large.Bottom - large.Top + 1;

        Assert.True(smallHeight > 1, "100 percent drew nothing");

        double ratio = largeHeight / smallHeight;
        Assert.InRange(ratio, 1.8, 2.2);
    }

    /// <summary>
    /// THE BASELINE: 100 percent is the picture that fills the window, not the font's natural size.
    /// </summary>
    /// <remarks>
    /// This is the fault behind "75 percent just centres the screen". The ladder used to be measured
    /// from the font's own size, so a fixed 80 by 24 grid filled an ordinary window at about 177
    /// percent - and every rung below that made the picture SMALLER than the one the window had been
    /// showing. Choosing 100 shrank it by half, and 75 left a postage stamp in a black window.
    /// </remarks>
    [AvaloniaFact]
    public void TheNormalViewFillsTheWindow()
    {
        using var bitmap = RenderAtZoom(100, ScreenContent.FullScreen, saveAs: "zoom-100-full");
        var ink = InkBounds(bitmap);

        Assert.True(ink.Count > 0, "the normal view drew NOTHING into the window");

        int width = ink.Right - ink.Left + 1;
        int height = ink.Bottom - ink.Top + 1;

        // One of the two axes has to be within a cell of the window, or 100 percent is not the
        // fitted picture and the ladder is measured from the wrong place again.
        Assert.True(width >= WindowWidth - 20 || height >= WindowHeight - 24,
            $"100 percent left the window mostly empty: ink is {width} by {height} in {WindowWidth} by {WindowHeight}");
    }

    /// <summary>
    /// What the screen holds for a measurement.
    /// </summary>
    private enum ScreenContent
    {
        /// <summary>
        /// A character in every cell.
        /// </summary>
        FullScreen,

        /// <summary>
        /// One character in the home cell, cursor beside it.
        /// </summary>
        MarkAtTopLeft,

        /// <summary>
        /// One character in the last cell, cursor on it.
        /// </summary>
        MarkAtBottomRight,
    }
}
