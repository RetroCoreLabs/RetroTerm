using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Pressing a key the host loaded with DECUDK, through the real control.
///
/// The Core tests say the definition is stored and looked up. This says a real keypress sends it -
/// which is the half that earns the 8 in the VT220's device attributes reply. Storing a definition
/// nothing presses would have made that claim false.
/// </summary>
[Collection("Avalonia")]
public class UserDefinedKeyPressTests
{
    private static (Window Window, TerminalCanvas Canvas, TerminalEmulatorBase Emulator, StringBuilder Sent)
        BuildTerminal()
    {
        var window = new Window { Width = 800, Height = 600 };
        var canvas = new TerminalCanvas();
        window.Content = canvas;
        window.Show();

        var emulator = EmulatorFactory.CreateEmulator("VT220", 80, 24, 100);
        canvas.SetEmulator(emulator);
        canvas.Focus();

        var sent = new StringBuilder();
        canvas.InputReceived += text => sent.Append(text);

        return (window, canvas, emulator, sent);
    }

    [AvaloniaFact]
    public void ADefinedKeySendsWhatTheHostLoaded()
    {
        var (window, canvas, emulator, sent) = BuildTerminal();

        // F6 is DEC key 17. "6c73 0d" is "ls" and a carriage return.
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bP1;1|17/6c730d\x1b\\"));
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);

        Assert.Equal("ls\r", sent.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void AnUndefinedKeyStillSendsItsFactorySequence()
    {
        // The guard against overreach: a terminal with no definitions must behave exactly as it
        // did before any of this existed.
        var (window, canvas, _, sent) = BuildTerminal();

        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);

        Assert.Equal("\x1b[17~", sent.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void ClearingTheDefinitionGivesTheFactorySequenceBack()
    {
        var (window, canvas, emulator, sent) = BuildTerminal();
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bP1;1|17/6c73\x1b\\"));

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bP1;1|17/\x1b\\"));
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);

        Assert.Equal("\x1b[17~", sent.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void ATerminalWithoutTheFeatureIsUnaffected()
    {
        // A VT100 has no user-defined keys, so the sequence that loads them does nothing and F6
        // keeps sending what it always sent.
        var window = new Window { Width = 800, Height = 600 };
        var canvas = new TerminalCanvas();
        window.Content = canvas;
        window.Show();

        var emulator = EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        canvas.SetEmulator(emulator);
        canvas.Focus();

        var sent = new StringBuilder();
        canvas.InputReceived += text => sent.Append(text);

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bP1;1|17/6c73\x1b\\"));
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);

        Assert.Equal("\x1b[17~", sent.ToString());
        window.Close();
    }
}
