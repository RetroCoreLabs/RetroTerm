using System.Collections.Generic;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// DECARM - private mode 8, whether a key held down repeats.
/// </summary>
/// <remarks>
/// <para><b>Why this is a UI test and not a Core one</b></para>
/// Every other mode in this family changes what a key ENCODES to, so it can be checked against the
/// mapper. This one changes whether a keypress happens at all, and by the time the mapper sees a
/// press it is already a press. So the only place it can be honoured is the input path, and the only
/// place it can be TESTED is a real control with real key events going into it.
/// <para><b>What would have made this a fake</b></para>
/// A flag on the emulator that nothing reads. That is exactly what
/// <c>TDV2200Emulator._isTektronixMode</c> is - set, cleared, and used for nothing but a capability
/// string - and the point of these tests is that the second and later presses genuinely stop
/// arriving.
/// </remarks>
[Collection("Avalonia")]
public class DecAutoRepeatTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// A canvas with a VT340 behind it, and a list that collects everything typed at the host.
    /// </summary>
    /// <param name="sent">
    /// Receives one entry per thing the canvas decided to send.
    /// </param>
    /// <returns>
    /// The canvas and its emulator.
    /// </returns>
    private static (TerminalCanvas Canvas, TerminalEmulatorBase Emulator) CanvasWithTerminal(
        List<string> sent)
    {
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);
        canvas.InputReceived += text => sent.Add(text);

        return (canvas, emulator);
    }

    /// <summary>
    /// Presses a key without releasing it.
    /// </summary>
    /// <param name="canvas">
    /// The control to send to.
    /// </param>
    /// <param name="key">
    /// The key going down.
    /// </param>
    private static void PressAndHold(TerminalCanvas canvas, Key key)
    {
        canvas.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = global::Avalonia.Input.KeyModifiers.None,
        });
    }

    /// <summary>
    /// Releases a key.
    /// </summary>
    /// <param name="canvas">
    /// The control to send to.
    /// </param>
    /// <param name="key">
    /// The key coming up.
    /// </param>
    private static void Release(TerminalCanvas canvas, Key key)
    {
        canvas.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Key = key,
            KeyModifiers = global::Avalonia.Input.KeyModifiers.None,
        });
    }

    [AvaloniaFact]
    public void ByDefaultAHeldKeyRepeats()
    {
        // The control case, and the important one. A keyboard repeats; getting this wrong would
        // break every held arrow key in the program, which is a far worse defect than the one the
        // mode exists to fix.
        var sent = new List<string>();
        var (canvas, _) = CanvasWithTerminal(sent);

        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);

        Assert.Equal(3, sent.Count);
    }

    [AvaloniaFact]
    public void WithAutoRepeatOffAHeldKeyArrivesOnce()
    {
        // The mode doing its job. Three KeyDowns, no release between them - which is exactly what
        // Avalonia raises while a key is leaned on, since it gives no "this is a repeat" flag.
        var sent = new List<string>();
        var (canvas, emulator) = CanvasWithTerminal(sent);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?8l"));

        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);

        Assert.Single(sent);
    }

    [AvaloniaFact]
    public void ReleasingTheKeyAllowsTheNextPress()
    {
        // Suppression must be about REPEATS, not about the key. A mode that made a key work once
        // per session would be worse than no mode at all.
        var sent = new List<string>();
        var (canvas, emulator) = CanvasWithTerminal(sent);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?8l"));

        PressAndHold(canvas, Key.Down);
        Release(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);
        Release(canvas, Key.Down);

        Assert.Equal(2, sent.Count);
    }

    [AvaloniaFact]
    public void ADifferentKeyIsNotSuppressed()
    {
        // Held keys are tracked one at a time. Suppressing every key because ONE was held would
        // stop a two-finger typist dead.
        var sent = new List<string>();
        var (canvas, emulator) = CanvasWithTerminal(sent);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?8l"));

        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Up);
        PressAndHold(canvas, Key.Left);

        Assert.Equal(3, sent.Count);
    }

    [AvaloniaFact]
    public void TurningAutoRepeatBackOnRestoresRepeating()
    {
        var sent = new List<string>();
        var (canvas, emulator) = CanvasWithTerminal(sent);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?8l"));
        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);
        Assert.Single(sent);

        Release(canvas, Key.Down);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?8h"));

        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);

        Assert.Equal(3, sent.Count);
    }

    [AvaloniaFact]
    public void TurningItOffPartWayThroughAHeldKeyStopsTheRestOfThatKeysRepeats()
    {
        // The key is recorded as held whether or not the mode is on, so that a host which resets
        // DECARM while a key is already down gets what it asked for immediately rather than from
        // the next press. Without that the flag would appear to do nothing for as long as the
        // reader kept leaning on the key - which is precisely when they would notice.
        var sent = new List<string>();
        var (canvas, emulator) = CanvasWithTerminal(sent);

        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);
        Assert.Equal(2, sent.Count);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?8l"));

        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);

        Assert.Equal(2, sent.Count);
    }

    [AvaloniaFact]
    public void AHardResetPutsRepeatingBack()
    {
        // A terminal that came out of a reset with auto-repeat still off would feel broken in a way
        // nobody would think to look for.
        var sent = new List<string>();
        var (canvas, emulator) = CanvasWithTerminal(sent);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?8l"));
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "c"));

        PressAndHold(canvas, Key.Down);
        PressAndHold(canvas, Key.Down);

        Assert.Equal(2, sent.Count);
    }
}
