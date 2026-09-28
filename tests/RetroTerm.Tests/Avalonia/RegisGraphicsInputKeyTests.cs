using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Graphics;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The keyboard half of ReGIS graphics input mode, driven through the real terminal canvas.
/// </summary>
/// <remarks>
/// <para><b>Why this cannot be tested on the emulator alone</b></para>
/// The emulator exposes "move the cursor" and "send the report". Which KEY does which, and whether a
/// key that graphics input took also reaches the host, are decisions the canvas makes - and getting
/// them wrong sends an arrow key's escape sequence to a host that is waiting for a position report.
///
/// <para><b>Keys only, by decision</b></para>
/// Decided with Ronny on 2026-08-20: the manual specifies the arrow-key half completely and says
/// nothing about what wins when a program is also asking for xterm mouse reports.
///
/// <para><b>The mouse half was added later, and this file still covers only the keys</b></para>
/// On 31 August 2026 Ronny overturned the keyboard-only part: a click now PLACES the cursor and
/// the arrow keys refine it. The conflict the original decision guarded against cannot arise,
/// because one-shot input suspends the host. Everything asserted here is unchanged - the keys
/// still do exactly what they did - and the pointing half lives in
/// <see cref="RegisGraphicsInputMouseTests"/>.
/// </remarks>
[Collection("Avalonia")]
public class RegisGraphicsInputKeyTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a window around a VT340 canvas and collects everything it sends.
    /// </summary>
    /// <param name="canvas">
    /// Receives the canvas, ready to take key events.
    /// </param>
    /// <param name="emulator">
    /// Receives the terminal behind it.
    /// </param>
    /// <param name="sent">
    /// Receives what the canvas passed on as ordinary keyboard input.
    /// </param>
    /// <param name="reports">
    /// Receives everything the TERMINAL sent the host of its own accord - the reports.
    /// </param>
    /// <returns>
    /// The window, which the caller must close.
    /// </returns>
    private static Window NewCanvas(out TerminalCanvas canvas, out TerminalEmulatorBase emulator,
        out List<string> sent, out List<string> reports)
    {
        var terminal = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        var control = new TerminalCanvas();
        control.SetEmulator(terminal);

        var collected = new List<string>();
        control.InputReceived += text => collected.Add(text);

        // The terminal answers the host directly, without going through the canvas, so the reports
        // have to be collected from the emulator rather than from InputReceived.
        var answered = new List<string>();
        terminal.DataToSend += data => answered.Add(Encoding.ASCII.GetString(data));
        reports = answered;

        var window = new Window { Width = 800, Height = 600, Content = control };
        window.Show();
        control.Focus();

        canvas = control;
        emulator = terminal;
        sent = collected;
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
    /// Presses a key on the canvas.
    /// </summary>
    /// <param name="canvas">
    /// The canvas under test.
    /// </param>
    /// <param name="key">
    /// Which key.
    /// </param>
    /// <param name="modifiers">
    /// Any modifiers held with it.
    /// </param>
    /// <remarks>
    /// THIS CALLS THE PRODUCTION METHOD, not a copy of it. <c>DeliverLocalKey</c> exists so the
    /// MCP <c>LOCALKEY</c> command can press a key at the terminal, and it raises the same routed
    /// event these tests used to build by hand. Pointing the helper at it means every test in this
    /// file also covers the path an agent drives - and a break in that path shows up here rather
    /// than only on a live machine.
    /// </remarks>
    private static void Press(TerminalCanvas canvas, Key key,
        KeyModifiers modifiers = KeyModifiers.None)
        => canvas.DeliverLocalKey(key, modifiers);

    /// <summary>
    /// Types a character on the canvas, the way a layout delivers one.
    /// </summary>
    /// <param name="canvas">
    /// The canvas under test.
    /// </param>
    /// <param name="text">
    /// The character produced.
    /// </param>
    /// <remarks>
    /// The production method again, for the same reason as <see cref="Press"/>.
    /// </remarks>
    private static void Type(TerminalCanvas canvas, string text)
        => canvas.DeliverLocalText(text);

    /// <summary>
    /// Everything the canvas passed on to the host as keyboard input.
    /// </summary>
    /// <param name="sent">
    /// The collected strings.
    /// </param>
    /// <returns>
    /// One string.
    /// </returns>
    private static string Typed(List<string> sent)
    {
        var text = new StringBuilder();
        for (int i = 0; i < sent.Count; i++) text.Append(sent[i]);
        return text.ToString();
    }

    [AvaloniaFact]
    public void AnArrowKeyMovesTheCrosshairAndNeverReachesTheHost()
    {
        // The defect this exists to stop: an arrow key sending its escape sequence while the host is
        // waiting for a position report. "The cursor moves one pixel in the direction of the arrow."
        var window = NewCanvas(out var canvas, out var emulator, out var sent, out var reports);
        try
        {
            Regis(emulator, "P[100,100]R(I0)R(P(I))");
            sent.Clear();

            Press(canvas, Key.Right);
            Press(canvas, Key.Down);

            Assert.Equal(string.Empty, Typed(sent));
            Assert.Equal(RegisGraphicsInputMode.OneShot, emulator.RegisInputMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShiftMakesTheArrowKeyMoveTenPixels()
    {
        // "Shift-arrow key - The cursor moves 10 pixels in the direction of the arrow." Read back
        // through the report, because that is the only thing the host ever sees.
        var window = NewCanvas(out var canvas, out var emulator, out _, out var reports);
        try
        {
            Regis(emulator, "P[100,100]R(I0)R(P(I))");

            reports.Clear();

            Press(canvas, Key.Right, KeyModifiers.Shift);
            Type(canvas, "A");

            Assert.Equal("A[110,100]\r", Typed(reports));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnOrdinaryKeyAnswersWithItsOwnCharacter()
    {
        // "Locator reports begin with the code(s) of the active non-arrow key... pressed." The
        // manual's worked example is A[102,200]CR.
        var window = NewCanvas(out var canvas, out var emulator, out _, out var reports);
        try
        {
            Regis(emulator, "P[102,200]R(I0)R(P(I))");

            reports.Clear();

            Type(canvas, "A");

            Assert.Equal("A[102,200]\r", Typed(reports));
            Assert.Equal(RegisGraphicsInputMode.Off, emulator.RegisInputMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheAnsweringKeyIsNotAlsoTypedAtTheHost()
    {
        // It travels INSIDE the report. Sending it twice would put a stray letter into whatever the
        // application reads next.
        var window = NewCanvas(out var canvas, out var emulator, out var sent, out var reports);
        try
        {
            Regis(emulator, "P[10,20]R(I0)R(P(I))");
            sent.Clear();

            Type(canvas, "A");

            Assert.Equal(string.Empty, Typed(sent));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeCancelsAndLetsTheSessionRunAgain()
    {
        // Ours, not the manual's. Without it a host that sent R(I0) alone would leave the session
        // suspended with no way back short of disconnecting.
        var window = NewCanvas(out var canvas, out var emulator, out _, out var reports);
        try
        {
            Regis(emulator, "P[10,20]R(I0)");
            Assert.True(emulator.IsRegisInputSuspended);

            Press(canvas, Key.Escape);

            Assert.False(emulator.IsRegisInputSuspended);
            Assert.Equal(RegisGraphicsInputMode.Off, emulator.RegisInputMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AModifierHeldOnItsOwnDoesNotAnswerTheHost()
    {
        // Reaching for shift is not a keystroke. Answering on it would end the mode the moment the
        // operator tried to move ten pixels.
        var window = NewCanvas(out var canvas, out var emulator, out _, out var reports);
        try
        {
            Regis(emulator, "P[10,20]R(I0)R(P(I))");

            Press(canvas, Key.LeftShift);

            Assert.Equal(RegisGraphicsInputMode.OneShot, emulator.RegisInputMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheArrowKeysGoBackToNormalOnceTheModeEnds()
    {
        // By far the most common case, and the one a mistake here would break for every session: a
        // terminal not in graphics input mode must treat an arrow key exactly as it always did.
        var window = NewCanvas(out var canvas, out var emulator, out var sent, out var reports);
        try
        {
            Regis(emulator, "P[10,20]R(I0)R(P(I))");
            Type(canvas, "A");
            sent.Clear();

            Press(canvas, Key.Right);

            Assert.NotEqual(string.Empty, Typed(sent));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnArrowKeyStillReachesTheHostInMultipleMode()
    {
        // "If you press an arrow key in multiple mode, the terminal sends that key's escape sequence
        // to the host." The one place the two modes are deliberately different at the keyboard.
        var window = NewCanvas(out var canvas, out var emulator, out var sent, out var reports);
        try
        {
            Regis(emulator, "R(I1)");
            sent.Clear();

            Press(canvas, Key.Right);

            Assert.NotEqual(string.Empty, Typed(sent));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheStatusChangeIsAnnouncedWhenTheHostStartsIt()
    {
        // The mode is entered by the HOST, so a view watching only its own key handling would never
        // notice it start - which is the moment a suspended session most needs explaining.
        var window = NewCanvas(out var canvas, out var emulator, out _, out var reports);
        try
        {
            var announced = new List<RegisGraphicsInputMode>();
            canvas.RegisGraphicsInputChanged += mode => announced.Add(mode);

            Regis(emulator, "R(I0)R(P(I))");
            Type(canvas, "A");

            Assert.Equal(new[] { RegisGraphicsInputMode.OneShot, RegisGraphicsInputMode.Off },
                announced);
        }
        finally
        {
            window.Close();
        }
    }

}
