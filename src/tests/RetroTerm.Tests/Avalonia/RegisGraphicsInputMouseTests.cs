using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Graphics;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The pointing half of ReGIS graphics input mode, driven through the real terminal canvas.
/// </summary>
/// <remarks>
/// <para><b>A rule that was deliberately overturned</b></para>
/// From 2026-08-20 the mouse was kept OUT of graphics input, because the ReGIS manual describes only
/// the arrow keys and nothing said who should win when a program is also asking for xterm mouse
/// reports. Ronny overturned that on 31 August 2026 once it was clear the conflict cannot arise:
/// one-shot graphics input SUSPENDS the host, so while the cursor is up no program is listening for
/// mouse reports and no new text is arriving to select. A click places the cursor; the arrow keys
/// still refine it.
///
/// <para><b>What is asserted, and what is deliberately not</b></para>
/// These read the cursor back through the POSITION REPORT, because that is the only thing a host
/// ever sees, and it is how M6.5 was measured by hand. They assert PROPERTIES of the mapping - the
/// centre goes to the centre, the corner goes to the origin, an arrow still steps one - rather than
/// recomputing the ratio the canvas uses. A test that repeated the formula would agree with a wrong
/// formula.
///
/// <para><b>Not the manual's</b></para>
/// Whether a real VT330/VT340 accepted a mouse or tablet as a ReGIS locator is NOT known here;
/// nothing in the specifications held in this repository covers it. This is our behaviour, and it is
/// recorded as a standing judgement call rather than as a reading of the hardware.
/// </remarks>
[Collection("Avalonia")]
public class RegisGraphicsInputMouseTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// How far a click is allowed to land from the point it should map to.
    /// </summary>
    /// <remarks>
    /// The plane is stretched into the text area, so one screen pixel covers rather more than one
    /// ReGIS pixel and the inverse can only round. The tolerance is about that rounding, not about
    /// letting a broken mapping through: a transform that was wrong in any interesting way - a
    /// swapped axis, a missing offset, a factor of two - misses by far more than this.
    /// </remarks>
    private const int RoundingTolerance = 12;

    /// <summary>
    /// Builds a window around a VT340 canvas and collects the reports the terminal sends.
    /// </summary>
    /// <param name="canvas">
    /// Receives the canvas, ready to take pointer and key events.
    /// </param>
    /// <param name="emulator">
    /// Receives the terminal behind it.
    /// </param>
    /// <param name="reports">
    /// Receives everything the TERMINAL sent the host of its own accord.
    /// </param>
    /// <returns>
    /// The window, which the caller must close.
    /// </returns>
    private static Window NewCanvas(out TerminalCanvas canvas, out TerminalEmulatorBase emulator,
        out List<string> reports)
    {
        var terminal = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        var control = new TerminalCanvas();
        control.SetEmulator(terminal);

        var answered = new List<string>();
        terminal.DataToSend += data => answered.Add(Encoding.ASCII.GetString(data));
        reports = answered;

        var window = new Window { Width = 800, Height = 600, Content = control };
        window.Show();
        control.Focus();

        canvas = control;
        emulator = terminal;
        return window;
    }

    /// <summary>
    /// Feeds host data to the terminal.
    /// </summary>
    /// <param name="emulator">
    /// The terminal.
    /// </param>
    /// <param name="commands">
    /// ReGIS to run, wrapped in its device control string here.
    /// </param>
    private static void Regis(TerminalEmulatorBase emulator, string commands)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "P1p" + commands + Escape + "\\"));

    /// <summary>
    /// Joins everything collected into one string.
    /// </summary>
    /// <param name="parts">
    /// The pieces, in the order they arrived.
    /// </param>
    /// <returns>
    /// The pieces run together.
    /// </returns>
    private static string Joined(List<string> parts)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            sb.Append(parts[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Answers the pending request and reads the coordinates out of the report.
    /// </summary>
    /// <param name="canvas">
    /// The canvas under test.
    /// </param>
    /// <param name="reports">
    /// Where the terminal's own output is being collected.
    /// </param>
    /// <returns>
    /// The reported cursor position.
    /// </returns>
    /// <remarks>
    /// The report reads <c>A[x,y]</c> followed by a carriage return - the manual's worked example is
    /// <c>A[102,200]CR</c>. Parsed by hand rather than with a regular expression, because the shape
    /// is fixed and the failure message is clearer when the raw text is quoted.
    /// </remarks>
    private static (int X, int Y) AnswerAndReadPosition(TerminalCanvas canvas, List<string> reports)
    {
        reports.Clear();
        canvas.DeliverLocalText("A");

        string report = Joined(reports);

        int open = report.IndexOf('[');
        int comma = report.IndexOf(',');
        int close = report.IndexOf(']');
        Assert.True(open >= 0 && comma > open && close > comma,
            "expected a position report shaped A[x,y] but got: " + report);

        int x = int.Parse(report.Substring(open + 1, comma - open - 1));
        int y = int.Parse(report.Substring(comma + 1, close - comma - 1));
        return (x, y);
    }

    /// <summary>
    /// The size of the graphics plane the cursor moves on.
    /// </summary>
    /// <param name="emulator">
    /// The terminal.
    /// </param>
    /// <returns>
    /// The composite's width and height in ReGIS pixels.
    /// </returns>
    private static (int Width, int Height) PlaneSize(TerminalEmulatorBase emulator)
    {
        var graphics = emulator.Graphics;
        Assert.NotNull(graphics);
        var output = graphics!.Output;
        Assert.NotNull(output);
        return (output!.Width, output.Height);
    }

    [AvaloniaFact]
    public void ClickingTheMiddleOfTheScreenPutsTheCursorInTheMiddleOfThePlane()
    {
        // The whole point of the feature: aim by pointing. Crossing the plane with the keys is 800
        // unshifted presses or 80 shifted, and this is one action.
        var window = NewCanvas(out var canvas, out var emulator, out var reports);
        try
        {
            Regis(emulator, "P[10,10]R(I0)R(P(I))");

            var (planeWidth, planeHeight) = PlaneSize(emulator);
            window.MouseDown(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2),
                MouseButton.Left);

            var (x, y) = AnswerAndReadPosition(canvas, reports);

            Assert.InRange(x, planeWidth / 2 - RoundingTolerance, planeWidth / 2 + RoundingTolerance);
            Assert.InRange(y, planeHeight / 2 - RoundingTolerance, planeHeight / 2 + RoundingTolerance);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingTheTopLeftCornerPutsTheCursorAtTheOrigin()
    {
        // Catches a missing offset and a swapped axis at once: any transform that forgets to undo
        // the canvas offset, or that flips Y, lands somewhere else entirely from the corner.
        var window = NewCanvas(out var canvas, out var emulator, out var reports);
        try
        {
            Regis(emulator, "P[400,300]R(I0)R(P(I))");

            window.MouseDown(new Point(0, 0), MouseButton.Left);

            var (x, y) = AnswerAndReadPosition(canvas, reports);

            Assert.InRange(x, 0, RoundingTolerance);
            Assert.InRange(y, 0, RoundingTolerance);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnArrowKeyStillRefinesAfterAClick()
    {
        // The decision was click to place AND arrows to refine, not one instead of the other. The
        // plane is scaled into the text area, so the last few pixels can only come from the keys.
        var window = NewCanvas(out var canvas, out var emulator, out var reports);
        try
        {
            Regis(emulator, "P[10,10]R(I0)R(P(I))");

            window.MouseDown(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2),
                MouseButton.Left);

            // Read where the click landed, which ends the round, then set up an identical one and
            // repeat it with a single arrow press added.
            var (clicked, clickedY) = AnswerAndReadPosition(canvas, reports);

            Regis(emulator, "P[10,10]R(I0)R(P(I))");
            window.MouseDown(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2),
                MouseButton.Left);
            canvas.DeliverLocalKey(Key.Right, KeyModifiers.None);

            var (refined, refinedY) = AnswerAndReadPosition(canvas, reports);

            Assert.Equal(clicked + 1, refined);
            Assert.Equal(clickedY, refinedY);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AClickDoesNothingWhenGraphicsInputIsNotRunning()
    {
        // The mouse keeps its ordinary job everywhere else. Without this guard the click would move
        // a cursor that is not on screen, and text selection would be lost for the whole session.
        var window = NewCanvas(out var canvas, out var emulator, out var reports);
        try
        {
            // A drawing, but no R(I0) - so no graphics input mode.
            Regis(emulator, "P[100,100]V[200,200]");
            reports.Clear();

            window.MouseDown(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2),
                MouseButton.Left);

            Assert.Equal(RegisGraphicsInputMode.Off, emulator.RegisInputMode);
            Assert.Equal(string.Empty, Joined(reports));
        }
        finally
        {
            window.Close();
        }
    }
}
