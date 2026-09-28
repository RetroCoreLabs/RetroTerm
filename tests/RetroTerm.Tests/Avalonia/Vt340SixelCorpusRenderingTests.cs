using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The vt340test Sixel streams drawn on the real renderer, saved so a human can look at them.
/// </summary>
/// <remarks>
/// <para><b>What this corpus is</b></para>
/// hackerb9's vt340test collects Sixel streams that were run against a REAL VT340 and a real
/// VT240, with the resulting screen captured or photographed beside each one. That pairing is the
/// whole value: the <c>.six</c> is the input and the <c>.png</c> is what the hardware actually
/// made of it. It is CC0, and it is NOT committed here - fetch it with
/// <c>tools\fetch-conformance-corpora.ps1</c>.
///
/// <para><b>Why these tests assert so little</b></para>
/// Several of the reference pictures are photographs of a curved glass screen. Comparing our
/// pixels to theirs would fail on focus, geometry and colour temperature while telling nobody
/// anything about the decoder. So the assertions here only pin what CAN be stated without a
/// judgement - that the stream decoded, that ink reached the screen, and for the cases where a
/// colour is written into the stream in plain numbers, that the colour came out.
///
/// The rest is done by eye. Every file writes a PNG into
/// <c>tests\RetroTerm.Tests\Avalonia\images\rendered\</c> named <c>vt340-sixel-NAME.png</c>, to be
/// opened next to <c>NAME.png</c> in the fetched corpus.
///
/// <para><b>Geometry</b></para>
/// A VT340's graphics space is 800 by 480 and the renderer stretches the plane over the whole text
/// area. At 80 by 24 cells of 8 by 16 pixels that area is 640 by 384 - the same 5:3 - so nothing
/// is distorted by the choice of terminal size.
///
/// <para><b>One fixture is not a screen image - vaxrgl-lntest.six</b></para>
/// It comes out looking rotated, and it is: the rotation is IN THE FILE. Decoded at its natural
/// size it is 972 pixels wide by 1548 tall - a portrait sheet with landscape content drawn onto it
/// sideways, which is what happens when landscape artwork is printed on portrait paper. Four
/// things say it is a page rather than a screen:
///  - It is the only fixture in the corpus with no reference capture. Every other one was
///    photographed off a real VT340 screen.
///  - 1548 sixel rows. A VT340 screen is 480.
///  - It opens with SSU, Select Size Unit, which sets a real-world dot size for printing, and then
///    sets a 66 line region. 66 lines is a printed page.
///  - It carries no raster attributes at all, so it never claims a screen size.
///
/// On screen it shows the top left 800 by 480 corner of that page, which is also what a real
/// VT340 would show, because an oversized image clips rather than shrinking.
///
/// Nothing on the SCREEN rotates, and this fixture is where that was established: multisize.six and
/// extremeratio.six come out in the same orientation as the hardware captures beside them, and both
/// cats stand upright matching theirs. A renderer that rotated would have laid those on their
/// sides too.
///
/// PRINTING is the opposite, and it is why this page looks the way it does. DEC STD 070 section
/// 7.8.2.3 defines a rotated print mode, DECGRPM, which turns the image 90 degrees so an expanded
/// image fits one 8.5 inch page - counter-clockwise on a VT240, "so that the left side of the paper
/// (as it comes out of a typical dot matrix printer) corresponds to the top of the image on the
/// terminal screen. This scanning order was chosen to allow punching holes for a looseleaf notebook
/// on the left side of the page." A landscape drawing on a portrait sheet is exactly what that
/// produces. See docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md.
/// </remarks>
[Collection("Avalonia")]
public class Vt340SixelCorpusRenderingTests
{
    /// <summary>
    /// Where the fetched corpus lands. Resolved from the assembly so it points at the source tree.
    /// </summary>
    private static string CorpusFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(Vt340SixelCorpusRenderingTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "vt340test"));

    /// <summary>
    /// Name used when the corpus has not been fetched, so the theory still has a case to run.
    /// A theory with no cases is an error in xunit, and a missing corpus is not a failure.
    /// </summary>
    private const string NotFetched = "(corpus not fetched)";

    public static IEnumerable<object[]> SixelFiles()
    {
        if (!Directory.Exists(CorpusFolder))
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        string[] files = Directory.GetFiles(CorpusFolder, "*.six");
        if (files.Length == 0)
        {
            yield return new object[] { NotFetched };
            yield break;
        }

        Array.Sort(files, StringComparer.Ordinal);

        for (int i = 0; i < files.Length; i++)
        {
            yield return new object[] { Path.GetFileName(files[i]) };
        }
    }

    /// <summary>
    /// Builds a VT340 with the cursor parked and plays a fixture into it.
    /// </summary>
    /// <param name="fixture">
    /// File name inside the fetched corpus.
    /// </param>
    /// <returns>
    /// The emulator, ready to be rendered.
    /// </returns>
    private static TerminalEmulatorBase Play(string fixture)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        // A visible cursor is ink like any other, and it would sit in the middle of the picture.
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'?', (byte)'2', (byte)'5', (byte)'l' });
        emulator.ProcessData(File.ReadAllBytes(Path.Combine(CorpusFolder, fixture)));

        return emulator;
    }

    [AvaloniaTheory]
    [MemberData(nameof(SixelFiles))]
    public void EveryFixtureDecodesAndIsSavedToBeLookedAt(string fixture)
    {
        if (fixture == NotFetched)
        {
            return;
        }

        var emulator = Play(fixture);

        string name = "vt340-sixel-" + Path.GetFileNameWithoutExtension(fixture);
        using var shot = RenderedScreenshot.Capture(emulator, name, saveZoom: 1);

        // The only thing that can be stated without looking: the stream put SOMETHING on the
        // plane. A decoder that silently drops a fixture is the failure this catches; whether the
        // picture is the right picture is a judgement, and the PNG is what it is made against.
        Assert.True(emulator.Graphics!.HasAnythingToDraw(),
            $"{fixture} decoded to nothing - no pixels reached the graphics plane");
    }

    [AvaloniaFact]
    public void TheEightMapColoursComeOutInTheRightOrder()
    {
        // map8.six writes its eight colours as plain numbers in the stream itself, so this one CAN
        // be asserted without a photograph. Its raster is 93 by 14 with eight bands of about twelve
        // pixels each across the top, in the order dark red, green, olive, blue-violet, magenta,
        // cyan, grey, black - which is what the reference picture beside it shows.
        if (!File.Exists(Path.Combine(CorpusFolder, "map8.six")))
        {
            return;
        }

        var emulator = Play("map8.six");

        using var shot = RenderedScreenshot.Capture(emulator, "vt340-sixel-map8-checked", saveZoom: 1);

        // Sample by PIXEL, not by cell. A band is about nine screen pixels wide and a cell is
        // eight, so no band lines up with a cell and a per-cell sample would straddle two colours.
        //
        // The 800 by 480 plane is stretched over the 640 by 384 text area, so a raster pixel lands
        // at 0.8 of its own x. The bands are 11, then six of 12, then 10 - 93 across.
        //
        //   band 0  raster 0..10   centre 5    ->  x  4   #0 = 60, 0, 0     dark red
        //   band 1  raster 11..22  centre 17   ->  x 13    #1 = 0, 66, 0     green
        //   band 5  raster 59..70  centre 65   ->  x 52    #5 = 0, 66, 72    cyan
        //   band 7  raster 83..92  centre 88   ->  x 70    #7 = 0, 0, 0      black
        var red = shot.PixelAt(4, 5);
        var green = shot.PixelAt(13, 5);
        var cyan = shot.PixelAt(52, 5);

        Assert.True(red.Red > red.Green && red.Red > red.Blue,
            $"band 0 is #0 = 60,0,0 - red; got {red}");
        Assert.True(green.Green > green.Red && green.Green > green.Blue,
            $"band 1 is #1 = 0,66,0 - green; got {green}");

        // Cyan is the one that pins the channels the right way round. Red and blue were once
        // swapped in the blit for months, invisible because the only thing drawing was a single
        // green - see SixelRenderingTests for that story. A cyan band that came out orange-red
        // would be impossible to miss here.
        Assert.True(cyan.Blue > cyan.Red && cyan.Green > cyan.Red,
            $"band 5 is #5 = 0,66,72 - cyan; got {cyan}");
    }

    [AvaloniaFact]
    public void TheCommentFixtureDrawsAllSixteenOfItsColours()
    {
        // comment.six builds ONE picture out of fourteen DCS strings, each positioning itself from
        // the page origin with leading graphics newlines. It is the only fixture in the corpus that
        // composes across DCS boundaries, which is why it caught what nothing else did.
        //
        // Without DECSDM every string landed lower than the one before, the table marched off the
        // bottom of the plane, and only three of its sixteen colours were ever drawn. The old
        // assertion - "something reached the plane" - was perfectly true the whole time.
        if (!File.Exists(Path.Combine(CorpusFolder, "comment.six")))
        {
            return;
        }

        var emulator = Play("comment.six");

        using var shot = RenderedScreenshot.Capture(emulator, "vt340-sixel-comment-checked", saveZoom: 1);

        // Count colours with real area, so anti-aliased edges and the white grid rules do not pad
        // the number. The swatches are the only large blocks in the picture.
        var areas = new Dictionary<uint, int>();

        for (int y = 0; y < shot.Height; y++)
        {
            for (int x = 0; x < shot.Width; x++)
            {
                uint key = (uint)shot.PixelAt(x, y);
                areas.TryGetValue(key, out int seen);
                areas[key] = seen + 1;
            }
        }

        int blocks = 0;
        foreach (var pair in areas)
        {
            if (pair.Value >= 400) blocks++;
        }

        // Sixteen swatches plus the background. One of the sixteen is the black #0 swatch, which is
        // drawn as an empty outline rather than a filled block, so the floor is fifteen.
        Assert.True(blocks >= 15,
            $"comment.six defines sixteen colours and lays them out as a table; only {blocks} large blocks were drawn");
    }

    [Fact]
    public void AFetchedCorpusIsCompleteRatherThanHalfThere()
    {
        // Absence is expected; a HALF-PRESENT corpus is not. It would quietly shrink the number of
        // pictures being produced while every one of them still passed.
        if (!Directory.Exists(CorpusFolder))
        {
            return;
        }

        string[] files = Directory.GetFiles(CorpusFolder, "*.six");
        if (files.Length == 0)
        {
            return;
        }

        // 16 .six files as fetched on 2026-08-17. A smaller number means the fetch was interrupted;
        // a larger one means upstream added fixtures, which is worth noticing rather than ignoring.
        Assert.Equal(16, files.Length);
    }
}
