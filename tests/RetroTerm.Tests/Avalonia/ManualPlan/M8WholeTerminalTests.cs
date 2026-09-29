using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Selection;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Services;
using Xunit;

namespace RetroTerm.Tests.Avalonia.ManualPlan;

/// <summary>
/// The machine half of the by-hand pass in
/// <c>docs\manual-tests\M8-WHOLE-TERMINAL.md</c>.
/// </summary>
/// <remarks>
/// <para><b>Why the names carry the case numbers</b></para>
/// Each method is named after the case it covers, so the document and the suite can be walked in
/// either direction by searching for the ID. The document says, per case, what these tests CANNOT
/// see - the real Windows clipboard, whether a beep sounds like a beep, whether a letterbox border
/// looks right - and that leftover is what a person's time is for.
///
/// These are the whole-program cases: the window, the scrollback, the clipboard, the speaker. They
/// belong to no single emulator, which is why they had almost no coverage until the manual document
/// was written and each step had to be checked against the source before it could be printed.
/// </remarks>
[Collection("Avalonia")]
public class M8WholeTerminalTests
{
    /// <summary>
    /// A real canvas in a real window with a real emulator, the way the program builds it.
    /// </summary>
    /// <param name="scrollbackLines">
    /// How much history the buffer keeps.
    /// </param>
    /// <returns>
    /// The window, the canvas, and the emulator the canvas is driving.
    /// </returns>
    private static (Window Window, TerminalCanvas Canvas, VT100Emulator Emulator) BuildTerminal(
        int scrollbackLines = 200)
    {
        var window = new Window { Width = 800, Height = 600 };
        var canvas = new TerminalCanvas();
        window.Content = canvas;
        window.Show();

        var emulator = new VT100Emulator(80, 24, scrollbackLines);
        canvas.SetEmulator(emulator);

        return (window, canvas, emulator);
    }

    /// <summary>
    /// Feeds ASCII to the emulator the way a host would.
    /// </summary>
    /// <param name="emulator">
    /// The emulator under test.
    /// </param>
    /// <param name="text">
    /// The bytes, written as a string.
    /// </param>
    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Writes enough numbered lines to push earlier ones off a 24-row screen.
    /// </summary>
    /// <param name="emulator">
    /// The emulator under test.
    /// </param>
    /// <param name="count">
    /// How many lines to write.
    /// </param>
    private static void WriteNumberedLines(TerminalEmulatorBase emulator, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Feed(emulator, "LINE " + i.ToString("D3") + "\r\n");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // M8.2 - scrollback
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// M8.2a - rolling the wheel back shows rows that have left the screen.
    /// </summary>
    /// <remarks>
    /// Drives a REAL wheel event through the real control rather than calling a scroll method, so
    /// the modifier handling, the mouse-reporting branch and the clamping are all on the path. The
    /// document keeps the eye-judgement half: whether it scrolls smoothly.
    /// </remarks>
    [AvaloniaFact]
    public void M8_2a_RollingTheWheelBackShowsRowsThatLeftTheScreen()
    {
        var (window, canvas, emulator) = BuildTerminal();
        WriteNumberedLines(emulator, 60);

        Assert.False(canvas.IsScrolledBack, "the view should start live");
        Assert.True(emulator.GetBuffer().ScrollbackLineCount > 0, "nothing scrolled off to roll back to");

        window.MouseWheel(new Point(40, 40), new Vector(0, 1));

        Assert.True(canvas.IsScrolledBack, "the wheel did not move the view into the history");

        // ...and rolling forward again comes back to the live view.
        window.MouseWheel(new Point(40, 40), new Vector(0, -1));
        Assert.False(canvas.IsScrolledBack, "the wheel did not come back to the live view");

        window.Close();
    }

    /// <summary>
    /// M8.2b - the alternate screen adds nothing to the scrollback.
    /// </summary>
    /// <remarks>
    /// This is what makes quitting vim leave the shell exactly as it was. The check is the LINE
    /// COUNT across the switch: a full screen written on the alternate buffer must not push a
    /// single row into the history.
    /// </remarks>
    [Fact]
    public void M8_2b_TheAlternateScreenAddsNothingToTheScrollback()
    {
        var emulator = new VT100Emulator(80, 24, 200);
        WriteNumberedLines(emulator, 60);

        int before = emulator.GetBuffer().ScrollbackLineCount;
        Assert.True(before > 0);

        Feed(emulator, "\x1b[?1049h");          // what vim sends on the way in
        WriteNumberedLines(emulator, 60);       // a whole editor session's worth of drawing
        Feed(emulator, "\x1b[?1049l");          // ...and on the way out

        Assert.Equal(before, emulator.GetBuffer().ScrollbackLineCount);
    }

    /// <summary>
    /// M8.2c - the wheel does not scroll back while the alternate screen is up.
    /// </summary>
    /// <remarks>
    /// There is nothing behind the alternate screen to scroll to, so the wheel must leave the view
    /// alone - and in the running program it belongs to the full-screen program instead.
    /// </remarks>
    [AvaloniaFact]
    public void M8_2c_TheWheelDoesNotScrollBackWhileTheAlternateScreenIsUp()
    {
        var (window, canvas, emulator) = BuildTerminal();
        WriteNumberedLines(emulator, 60);
        Feed(emulator, "\x1b[?1049h");

        window.MouseWheel(new Point(40, 40), new Vector(0, 1));

        Assert.False(canvas.IsScrolledBack,
            "the wheel scrolled into history while the alternate screen was up");

        window.Close();
    }

    // ─────────────────────────────────────────────────────────────
    // M8.3 - copy and paste
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// M8.3b - a line the terminal wrapped copies as ONE line.
    /// </summary>
    /// <remarks>
    /// <para><b>The defect this was written for</b></para>
    /// A line longer than the screen is one logical line that the TERMINAL split across two rows.
    /// The buffer records that - <c>TerminalBuffer.IsLineWrapped</c> - and <c>ScreenReader</c>
    /// already honoured it, but the copy path put a newline at every row boundary regardless. A
    /// copied long command pasted back into a shell therefore ran as two commands, the second being
    /// whatever the tail of the line happened to say.
    /// </remarks>
    [Fact]
    public void M8_3b_ALineTheTerminalWrappedCopiesWithoutABreak()
    {
        var emulator = new VT100Emulator(80, 24, 200);

        // 100 characters at 80 columns: the terminal wraps it itself onto a second row.
        var line = new string('A', 80) + new string('B', 20);
        Feed(emulator, line);

        var buffer = emulator.GetBuffer();
        Assert.True(buffer.IsLineWrapped(0), "row 0 was not marked as wrapped - the setup is wrong");

        var selection = new SelectionManager(buffer);
        selection.StartSelection(0, 0);
        selection.ExtendSelection(1, 19);

        var copied = selection.GetSelectedText();

        Assert.Equal(line, copied);
        Assert.DoesNotContain("\n", copied);
    }

    /// <summary>
    /// M8.3b - the same holds for a wrapped line that has scrolled into the history.
    /// </summary>
    /// <remarks>
    /// History lines carry their own wrap flag (<c>IsScrollbackLineWrapped</c>), which is a separate
    /// array from the screen's - so joining the screen rows and forgetting the history ones would
    /// leave the defect in place for exactly the long output people scroll back to copy.
    /// </remarks>
    [Fact]
    public void M8_3b_AWrappedLineInTheHistoryAlsoCopiesWithoutABreak()
    {
        var emulator = new VT100Emulator(80, 24, 200);

        var line = new string('A', 80) + new string('B', 20);
        Feed(emulator, line + "\r\n");
        WriteNumberedLines(emulator, 40);       // push it off the top

        var buffer = emulator.GetBuffer();
        var selection = new SelectionManager(buffer);

        // The wrapped line is the first thing that ever scrolled off, so it is history rows 0 and 1.
        int firstHistoryRow = -buffer.ScrollbackLineCount;
        Assert.True(buffer.IsScrollbackLineWrapped(0), "the history line lost its wrap flag");

        selection.StartSelection(firstHistoryRow, 0);
        selection.ExtendSelection(firstHistoryRow + 1, 19);

        var copied = selection.GetSelectedText();

        Assert.Equal(line, copied);
        Assert.DoesNotContain("\n", copied);
    }

    /// <summary>
    /// M8.3b - two separate lines still copy with a break between them.
    /// </summary>
    /// <remarks>
    /// The guard on the fix above. Joining every row would be as wrong as joining none, and much
    /// harder to notice: a copied build log would come out as one enormous line.
    /// </remarks>
    [Fact]
    public void M8_3b_TwoSeparateLinesStillCopyWithABreakBetweenThem()
    {
        var emulator = new VT100Emulator(80, 24, 200);
        Feed(emulator, "first\r\nsecond\r\n");

        var buffer = emulator.GetBuffer();
        Assert.False(buffer.IsLineWrapped(0), "row 0 must not be marked wrapped - it ended with CRLF");

        var selection = new SelectionManager(buffer);
        selection.StartSelection(0, 0);
        selection.ExtendSelection(1, 5);

        Assert.Equal("first\nsecond", selection.GetSelectedText());
    }

    // ─────────────────────────────────────────────────────────────
    // M8.4 - the bell
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// M8.4b - the bell is written out as a wave file so it can be listened to.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a file rather than an assertion</b></para>
    /// Nothing in this repository can hear. The nearest useful thing is to put the exact samples
    /// the program would play somewhere a person can double-click them, next to the rendered PNGs
    /// that serve the same purpose for pictures. If the file sounds right and the app does not, the
    /// fault is in playback rather than in the tone - which is a split no listening test of the app
    /// alone can make.
    ///
    /// The assertions only check that the file describes the tone <c>BellService</c> was asked for:
    /// a real RIFF/WAVE header, and a length that matches the duration.
    /// </remarks>
    [Fact]
    public void M8_4b_TheBellIsWrittenOutAsAWaveFileToListenTo()
    {
        var wave = BellService.GetWaveFile();
        Assert.NotNull(wave);

        // RIFF....WAVE - the first twelve bytes of any wave file.
        Assert.True(wave.Length > 44, "a wave file is at least a 44-byte header plus samples");
        Assert.Equal((byte)'R', wave[0]);
        Assert.Equal((byte)'I', wave[1]);
        Assert.Equal((byte)'F', wave[2]);
        Assert.Equal((byte)'F', wave[3]);
        Assert.Equal((byte)'W', wave[8]);
        Assert.Equal((byte)'A', wave[9]);
        Assert.Equal((byte)'V', wave[10]);
        Assert.Equal((byte)'E', wave[11]);

        // Sample rate sits at offset 24, little endian, and the byte rate that follows it tells us
        // how long the samples last. Both are read rather than assumed.
        int sampleRate = wave[24] | (wave[25] << 8) | (wave[26] << 16) | (wave[27] << 24);
        int byteRate = wave[28] | (wave[29] << 8) | (wave[30] << 16) | (wave[31] << 24);
        Assert.True(sampleRate >= 8000, $"sample rate {sampleRate} is too low for a clean tone");
        Assert.True(byteRate > 0);

        double seconds = (wave.Length - 44) / (double)byteRate;
        double expected = BellService.DurationMs / 1000.0;
        Assert.True(Math.Abs(seconds - expected) < 0.02,
            $"the wave lasts {seconds:F3}s but BellService asks for {expected:F3}s");

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "bell.wav");

        // A viewer or player may hold the previous copy open. A manual artefact is not worth
        // failing a test run over - the same rule the PDF artefacts follow.
        try
        {
            File.WriteAllBytes(path, wave);
        }
        catch (IOException)
        {
        }
    }

    // ─────────────────────────────────────────────────────────────
    // M8.7 - display zoom
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a canvas into a window-sized bitmap and hands back the pixels.
    /// </summary>
    /// <param name="canvas">
    /// The canvas, already holding an emulator.
    /// </param>
    /// <param name="width">
    /// Window width in pixels.
    /// </param>
    /// <param name="height">
    /// Window height in pixels.
    /// </param>
    /// <returns>
    /// The rendered pixels.
    /// </returns>
    private static SkiaSharp.SKBitmap RenderWindow(TerminalCanvas canvas, int width, int height)
    {
        var size = new Size(width, height);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));

        using var target = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(width, height), new Vector(96, 96));
        target.Render(canvas);

        using var stream = new MemoryStream();
        target.Save(stream);
        return SkiaSharp.SKBitmap.Decode(stream.ToArray());
    }

    /// <summary>
    /// Whether the text cursor can be SEEN anywhere in the window.
    /// </summary>
    /// <remarks>
    /// <para><b>Asked by hiding it, not by hunting for it</b></para>
    /// The screen is drawn twice - once as it is, and once with the cursor turned off by DECTCEM
    /// (<c>ESC [ ? 2 5 l</c>) - and the two pictures are compared. If they differ anywhere, the
    /// cursor is being drawn somewhere inside the window; if they are identical, it is not on
    /// screen at all.
    /// The obvious alternative was to search the pixels for the cursor's own shape, and it is worse
    /// in two ways. A block cursor is drawn in the SAME colour as the text, so telling them apart
    /// means reasoning about which runs of pixels are "solid enough" - a second copy of the
    /// renderer's own knowledge living in a test, which this repository has a rule against. And
    /// working out where the cursor OUGHT to be would mean recomputing the scale and the pan, which
    /// is the very arithmetic under test.
    /// Hiding it needs none of that. It also cannot pass for the wrong reason: nothing else on the
    /// screen changes between the two renders.
    /// </remarks>
    /// <param name="canvas">
    /// The canvas to draw.
    /// </param>
    /// <param name="emulator">
    /// The emulator the canvas is driving.
    /// </param>
    /// <param name="width">
    /// Window width in pixels.
    /// </param>
    /// <param name="height">
    /// Window height in pixels.
    /// </param>
    /// <returns>
    /// True when the cursor is visible somewhere in the window.
    /// </returns>
    private static bool CursorIsOnScreen(TerminalCanvas canvas, TerminalEmulatorBase emulator,
        int width, int height)
    {
        using var withCursor = RenderWindow(canvas, width, height);

        // The hide/show sequences below are written with a four-digit unicode escape, NOT with a
        // two-digit hex one and never with a raw ESC typed into the source. The hex form takes up
        // to four digits and swallows whatever follows if it happens to be one; a raw ESC is
        // invisible in a diff and in most editors. Both traps have already cost time here - one of
        // them wrote a test that enabled nothing and asserted successfully anyway.

        Feed(emulator, "\u001b[?25l");     // DECTCEM off - hide the cursor
        emulator.PublishFrame();
        using var withoutCursor = RenderWindow(canvas, width, height);

        Feed(emulator, "\u001b[?25h");     // back on, so the caller can carry on
        emulator.PublishFrame();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (withCursor.GetPixel(x, y) != withoutCursor.GetPixel(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Sends one key press with Control held, to the real canvas.
    /// </summary>
    /// <param name="canvas">
    /// The canvas.
    /// </param>
    /// <param name="key">
    /// The key.
    /// </param>
    private static void PressWithControl(TerminalCanvas canvas, global::Avalonia.Input.Key key)
    {
        canvas.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = global::Avalonia.Input.KeyModifiers.Control,
        });
    }

    /// <summary>
    /// M8.7b - zooming in keeps the cursor in view, at every rung of the ladder.
    /// </summary>
    /// <remarks>
    /// The case Ronny asked to see automated. It walks the ladder with REAL Ctrl+plus presses, the
    /// way the document tells a person to, and after each one checks two things: the text got
    /// bigger, and the cursor can still be seen.
    /// This is the fault that reached him. Above about 175 percent the picture is wider and taller
    /// than the window, and it used to be CENTRED - so the terminal's origin, where the prompt and
    /// the cursor are, sat outside the window and the screen came up blank. Every rung from 200
    /// upwards fails here without the anchoring.
    /// What is left for the eye: whether following the cursor feels right while typing rather than
    /// lurching, and whether the magnified text is pleasant to read. Neither is a pixel question.
    /// </remarks>
    [AvaloniaFact]
    public void M8_7b_ZoomingInKeepsTheCursorInView()
    {
        var (window, canvas, emulator) = BuildTerminal();
        try
        {
            // A window bigger than a natural 80x24, which is where the fault lives: at 100 percent
            // the picture fits, and from about 175 up it does not.
            const int WindowWidth = 1100;
            const int WindowHeight = 640;

            Feed(emulator, "$ ");
            emulator.PublishFrame();

            Assert.Equal(TerminalCanvas.NaturalZoomPercent, canvas.ZoomPercent);
            Assert.True(CursorIsOnScreen(canvas, emulator, WindowWidth, WindowHeight),
                "the cursor is not visible even at the normal view");

            int previous = canvas.ZoomPercent;

            // Up the whole ladder, one Ctrl+plus at a time, exactly as the document says.
            for (int step = 0; step < TerminalCanvas.ZoomSteps.Length; step++)
            {
                PressWithControl(canvas, global::Avalonia.Input.Key.OemPlus);

                if (canvas.ZoomPercent == previous)
                {
                    break;      // the top of the ladder
                }

                Assert.True(canvas.ZoomPercent > previous,
                    $"Ctrl+plus went from {previous} to {canvas.ZoomPercent}");
                previous = canvas.ZoomPercent;

                Assert.True(CursorIsOnScreen(canvas, emulator, WindowWidth, WindowHeight),
                    $"at {canvas.ZoomPercent} percent the cursor is off screen - " +
                    "this is the blank window Ronny reported at 300 percent");
            }

            // It really did climb, rather than stopping at the first rung.
            Assert.Equal(TerminalCanvas.ZoomSteps[TerminalCanvas.ZoomSteps.Length - 1],
                canvas.ZoomPercent);

            // And Ctrl+0 comes back to the whole screen.
            PressWithControl(canvas, global::Avalonia.Input.Key.D0);
            Assert.Equal(TerminalCanvas.NaturalZoomPercent, canvas.ZoomPercent);
            Assert.True(CursorIsOnScreen(canvas, emulator, WindowWidth, WindowHeight),
                "the cursor is not visible after Ctrl+0");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// M8.7b - the view follows the cursor down the screen as lines are typed.
    /// </summary>
    /// <remarks>
    /// The second half of the case: at a zoom where most of the screen is off the window, pressing
    /// Enter walks the cursor down, and it has to stay in view the whole way. Anchoring at the
    /// top-left corner alone would pass the first half of M8.7b and fail this one, which is why
    /// the two are separate tests.
    /// </remarks>
    [AvaloniaFact]
    public void M8_7b_TheViewFollowsTheCursorDownTheScreen()
    {
        var (window, canvas, emulator) = BuildTerminal();
        try
        {
            const int WindowWidth = 1100;
            const int WindowHeight = 640;

            canvas.ZoomPercent = 300;

            for (int line = 0; line < 20; line++)
            {
                Feed(emulator, "line " + line.ToString("D2") + "\r\n");
                emulator.PublishFrame();

                Assert.True(CursorIsOnScreen(canvas, emulator, WindowWidth, WindowHeight),
                    $"after {line + 1} lines the cursor has gone off screen at 300 percent");
            }

            // The cursor really did travel - a screen that never scrolled would prove nothing.
            Assert.True(emulator.GetCursor().Row > 10,
                $"the fixture did not move the cursor down the screen (row {emulator.GetCursor().Row})");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// M8.7c - going full screen and back never leaves a blank window.
    /// </summary>
    /// <remarks>
    /// The other fault Ronny reported, and it had the same cause. Here the WINDOW changes size
    /// while the zoom stays put, which is what maximising and restoring does.
    /// </remarks>
    [AvaloniaFact]
    public void M8_7c_GoingFullScreenAndBackKeepsTheCursorInView()
    {
        var (window, canvas, emulator) = BuildTerminal();
        try
        {
            Feed(emulator, "$ ");
            emulator.PublishFrame();

            canvas.ZoomPercent = 200;

            // Ordinary, then maximised, then back to ordinary.
            Assert.True(CursorIsOnScreen(canvas, emulator, 1100, 640), "blank before maximising");
            Assert.True(CursorIsOnScreen(canvas, emulator, 1920, 1080), "blank while maximised");
            Assert.True(CursorIsOnScreen(canvas, emulator, 1100, 640), "blank after restoring");

            // And a small window, which is the other end of the same arithmetic.
            Assert.True(CursorIsOnScreen(canvas, emulator, 500, 300), "blank in a small window");
        }
        finally
        {
            window.Close();
        }
    }
}
