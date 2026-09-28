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
/// The desktop half of LOCALKEY, ZOOM and PASTE - that they really reach the terminal.
/// </summary>
/// <remarks>
/// <para><b>What these add over the command tests</b></para>
/// <c>LocalInputCommandTests</c> checks what the commands hand to the desktop, with a stand-in
/// standing where the window would be. It cannot tell whether the window then does anything, which
/// is the half that matters: a delegate that is wired to nothing passes every one of those tests.
/// <para><b>Why the routes are worth pinning</b></para>
/// Each of the three goes somewhere a sent byte cannot follow. A key raised here runs the canvas's
/// real <c>OnKeyDown</c>, so the ReGIS graphics input branch, the auto-repeat suppression and the
/// zoom shortcuts all get their say - reproducing that routing in the test would be a second answer
/// that agrees with itself and nothing else.
/// </remarks>
[Collection("Avalonia")]
public class LocalInputReachesTheTerminalTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a window around a VT340 canvas.
    /// </summary>
    /// <param name="canvas">
    /// Receives the canvas.
    /// </param>
    /// <param name="emulator">
    /// Receives the terminal behind it.
    /// </param>
    /// <param name="sent">
    /// Receives everything the canvas passed on as keyboard or pasted input.
    /// </param>
    /// <returns>
    /// The window, which the caller must close.
    /// </returns>
    private static Window NewCanvas(out TerminalCanvas canvas, out TerminalEmulatorBase emulator,
        out List<string> sent)
    {
        var terminal = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        var control = new TerminalCanvas();
        control.SetEmulator(terminal);

        var collected = new List<string>();
        control.InputReceived += text => collected.Add(text);

        var window = new Window { Width = 800, Height = 600, Content = control };
        window.Show();
        control.Focus();

        canvas = control;
        emulator = terminal;
        sent = collected;
        return window;
    }

    /// <summary>
    /// Joins everything the canvas sent into one string.
    /// </summary>
    /// <param name="sent">
    /// The collected pieces.
    /// </param>
    /// <returns>
    /// One string.
    /// </returns>
    private static string Joined(List<string> sent)
    {
        var text = new StringBuilder();
        for (int i = 0; i < sent.Count; i++) text.Append(sent[i]);
        return text.ToString();
    }

    [AvaloniaFact]
    public void ADeliveredArrowMovesTheRegisInputCursorAndNeverReachesTheHost()
    {
        // THE CASE THE WHOLE THING WAS BUILT FOR. In ReGIS one-shot graphics input the arrows move
        // the crosshair and the host is waiting, suspended. An arrow SENT down the wire moves
        // nothing and tells the host something it did not ask for, which is why LOCALKEY had to
        // exist before M6.5 could be driven without a person at the keyboard.
        var window = NewCanvas(out var canvas, out var emulator, out var sent);
        try
        {
            var answered = new List<string>();
            emulator.DataToSend += data => answered.Add(Encoding.ASCII.GetString(data));

            emulator.ProcessData(Encoding.ASCII.GetBytes(
                Escape + "P1p" + "P[400,240]R(I0)R(P(I))" + Escape + "\\"));

            Assert.Equal(RegisGraphicsInputMode.OneShot, emulator.RegisInputMode);

            // One plain step and one shifted step: "The cursor moves one pixel in the direction of
            // the arrow... Shift-arrow key - The cursor moves 10 pixels."
            canvas.DeliverLocalKey(Key.Right, KeyModifiers.None);
            canvas.DeliverLocalKey(Key.Right, KeyModifiers.Shift);

            // Read the position back through the REPORT, because that is the only thing the host
            // ever sees - and because it is what the run sheet's case actually checks.
            canvas.DeliverLocalText("A");

            // THE LEADING CARRIAGE RETURN IS THE SPECIFIED ONE, not a stray byte. Chapter 10: "When
            // the terminal receives R(I), it returns a carriage return (CR). Applications can use
            // the CR for synchronization." So entering graphics input answers with a bare CR, and
            // the position report follows once a key is pressed. Asserted rather than cleared away,
            // because a test that discarded it would also discard its disappearance.
            Assert.Equal("\rA[" + (400 + RegisDecoder.InputCursorStep + RegisDecoder.InputCursorShiftStep)
                + ",240]\r", Joined(answered));

            // And no arrow travelled to the host as an escape sequence, which is the other half of
            // the rule: the host is suspended and waiting for the report, not for a cursor key.
            Assert.Equal(string.Empty, Joined(sent));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ADeliveredCharacterAnswersTheHostAndEndsGraphicsInput()
    {
        // "The terminal sends a position report when you press any non-arrow key that is not dead."
        // The character has to be delivered as TEXT, because the report carries the character itself
        // and only the keyboard layout knows what a key produces.
        var window = NewCanvas(out var canvas, out var emulator, out var sent);
        try
        {
            var answered = new List<string>();
            emulator.DataToSend += data => answered.Add(Encoding.ASCII.GetString(data));

            emulator.ProcessData(Encoding.ASCII.GetBytes(
                Escape + "P1p" + "P[400,240]R(I0)R(P(I))" + Escape + "\\"));

            Assert.Equal(RegisGraphicsInputMode.OneShot, emulator.RegisInputMode);

            canvas.DeliverLocalText("A");

            Assert.NotEmpty(answered);
            Assert.Contains("A", Joined(answered));

            // Answering the request is what ends one-shot mode.
            Assert.Equal(RegisGraphicsInputMode.Off, emulator.RegisInputMode);

            // The character answered the host and went no further - it must not also be typed.
            Assert.Equal(string.Empty, Joined(sent));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PastedTextTravelsTheClipboardsOwnRouteRatherThanBeingTyped()
    {
        // PASTE is not SEND. A paste goes through the terminal, so it picks up bracketed paste when
        // the host has asked for it - and testing paste with SEND would test none of that.
        var window = NewCanvas(out var canvas, out var emulator, out var sent);
        try
        {
            // Private mode 2004 on: the host wants pastes bracketed.
            emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?2004h"));

            canvas.PasteText("LIST-FILES\r");

            var all = Joined(sent);

            // ESC [ 200 ~ before and ESC [ 201 ~ after, with the text between them.
            Assert.Contains(Escape + "[200~", all);
            Assert.Contains("LIST-FILES", all);
            Assert.Contains(Escape + "[201~", all);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void APasteThatSpansLinesArrivesAsOnePieceAndKeepsItsLineBreaks()
    {
        // The wrapped-line case. A paste of several lines must arrive as ONE paste - if it were
        // typed a character at a time the host would see an ordinary typed line, and bracketed
        // paste, which exists so an editor can tell the difference, would be pointless.
        var window = NewCanvas(out var canvas, out var emulator, out var sent);
        try
        {
            canvas.PasteText("first\rsecond\rthird\r");

            Assert.Single(sent);

            var all = Joined(sent);
            Assert.Contains("first\rsecond\rthird\r", all);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnEmptyPasteDoesNothingAtAll()
    {
        // Null and empty both mean "there is nothing to paste". Sending an empty bracketed-paste
        // wrapper would put two escape sequences on the wire for no reason.
        var window = NewCanvas(out var canvas, out var emulator, out var sent);
        try
        {
            canvas.PasteText(null);
            canvas.PasteText(string.Empty);

            Assert.Empty(sent);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheZoomCanBeSetAndSteppedAlongTheSharedLadder()
    {
        // 300 percent is a real case on the run sheet, and until now the only way to reach it was
        // the dropdown or the keyboard shortcut.
        var window = NewCanvas(out var canvas, out var emulator, out var sent);
        try
        {
            canvas.ZoomPercent = 300;
            Assert.Equal(300, canvas.ZoomPercent);

            // One place DOWN the shared ladder from 300 is 250, not 275 - the ladder is
            // 50, 75, 100, 125, 150, 175, 200, 250, 300, 400 and stepping walks its entries.
            canvas.StepZoom(-1);
            Assert.Equal(250, canvas.ZoomPercent);

            canvas.StepZoom(1);
            Assert.Equal(300, canvas.ZoomPercent);

            // The top of the ladder stays put rather than running off the end.
            canvas.ZoomPercent = TerminalCanvas.ZoomSteps[TerminalCanvas.ZoomSteps.Length - 1];
            canvas.StepZoom(1);
            Assert.Equal(TerminalCanvas.ZoomSteps[TerminalCanvas.ZoomSteps.Length - 1], canvas.ZoomPercent);
        }
        finally
        {
            window.Close();
        }
    }
}
