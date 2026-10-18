using System;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Renders a Sixel picture before and after a scroll, so the result can be LOOKED AT.
///
/// The pixel assertions next door prove a row of colour lands one text row higher. They
/// cannot see the picture tearing, smearing a copy of itself down the screen, or losing
/// its colours as it moves, and those are exactly the ways a hand-written bitmap scroll
/// goes wrong. This repo has already been caught by red and blue swapped in every graphic
/// while every assertion passed.
///
/// The PNGs land in tests\RetroTerm.Tests\Avalonia\images\rendered\.
/// </summary>
[Collection("Avalonia")]
public class GraphicsScrollRenderTests
{
    private const string Esc = "\u001b";

    private readonly ITestOutputHelper _output;

    public GraphicsScrollRenderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// A picture with a distinct top and bottom, so a scroll is obvious and so a tear or
    /// a duplicated band shows up rather than hiding in a flat colour.
    /// </summary>
    private static void DrawBandedPicture(TerminalEmulatorBase emulator)
    {
        var sb = new StringBuilder();
        sb.Append(Esc).Append("Pq");
        sb.Append("#1;2;100;0;0");     // red
        sb.Append("#2;2;0;100;0");     // green
        sb.Append("#3;2;0;0;100");     // blue
        // TALL on purpose. A sixel row is six pixels and a text row on a 24-row screen is
        // twenty plane pixels, so a three-row picture is gone after a single scroll and
        // proves nothing about movement. Twelve bands is 72 pixels, over three text rows.
        for (int band = 0; band < 12; band++)
        {
            sb.Append('#').Append((band % 3) + 1).Append(new string('~', 60));
            if (band < 11) sb.Append('-');
        }
        sb.Append(Esc).Append('\\');
        Feed(emulator, sb.ToString());
    }

    [AvaloniaFact]
    public void APictureIsStillItselfAfterTheScreenScrolls()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 40, 24, 100);
        Feed(emulator, Esc + "[?25l" + Esc + "[1;1H");
        DrawBandedPicture(emulator);
        emulator.Graphics!.Composite();

        using (var before = RenderedScreenshot.Capture(emulator, "graphics-scroll-before"))
        {
            Assert.NotNull(before);
            _output.WriteLine("before: " + before!.SavedPath);
        }

        // One whole-screen scroll: park on the last row and feed a line feed.
        Feed(emulator, Esc + "[" + emulator.Height + ";1H\n");
        emulator.Graphics.Composite();

        using (var after = RenderedScreenshot.Capture(emulator, "graphics-scroll-after"))
        {
            Assert.NotNull(after);
            _output.WriteLine("after:  " + after!.SavedPath);
            Assert.True(after.Width > 0 && after.Height > 0, "the scrolled screen rendered with no size");
        }

        // The picture is three text rows tall, so one scroll must move it without
        // removing it. An empty plane here means the scroll moved much too far.
        Assert.True(emulator.Graphics.HasAnythingToDraw(),
            "the picture vanished after a single scroll, so it moved further than one text row");
    }

    [AvaloniaFact]
    public void APictureIsGoneAfterAFullScreenErase()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 40, 24, 100);
        Feed(emulator, Esc + "[?25l" + Esc + "[1;1H");
        DrawBandedPicture(emulator);
        emulator.Graphics!.Composite();
        Assert.True(emulator.Graphics.HasAnythingToDraw(), "nothing was drawn to begin with");

        Feed(emulator, Esc + "[2J");
        emulator.Graphics.Composite();

        using (var shot = RenderedScreenshot.Capture(emulator, "graphics-after-clear"))
        {
            Assert.NotNull(shot);
            _output.WriteLine("cleared: " + shot!.SavedPath);
        }

        Assert.False(emulator.Graphics.HasAnythingToDraw(),
            "clear left the picture on the planes, which is the bug this fixes");
    }
}
