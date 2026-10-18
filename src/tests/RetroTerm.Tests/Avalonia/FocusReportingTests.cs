using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Focus reporting (mode 1004) driven from the real control, not from a method call.
///
/// The Core tests cover what the emulator sends. This covers the part only the UI can answer: that
/// a real focus change on a real canvas actually reaches it. A report that works when called
/// directly and is never wired up is worth nothing.
/// </summary>
[Collection("Avalonia")]
public class FocusReportingTests
{
    /// <summary>
    /// A window holding the canvas AND something else that can take focus.
    /// </summary>
    /// <remarks>
    /// The second control is the point. The canvas focuses itself the moment it is attached, so
    /// calling Focus() on it again raises nothing - the first version of this test asserted against
    /// an event that had already happened before it started listening. Focus has to be somewhere
    /// else first for moving it to be a change.
    /// </remarks>
    private static (Window Window, TerminalCanvas Canvas, Button Other, VT100Emulator Emulator,
        StringBuilder Replies) BuildTerminalAndSomewhereElseToLook()
    {
        var window = new Window { Width = 800, Height = 600 };
        var canvas = new TerminalCanvas();
        var other = new Button { Content = "elsewhere" };

        var panel = new StackPanel();
        panel.Children.Add(other);
        panel.Children.Add(canvas);
        window.Content = panel;
        window.Show();

        var emulator = new VT100Emulator(80, 24);
        canvas.SetEmulator(emulator);

        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        return (window, canvas, other, emulator, replies);
    }

    [AvaloniaFact]
    public void LookingAwayAndBackTellsAHostThatAskedToBeTold()
    {
        var (window, canvas, other, emulator, replies) = BuildTerminalAndSomewhereElseToLook();
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[?1004h"));
        canvas.Focus();
        replies.Clear();

        other.Focus();     // focus out
        canvas.Focus();    // and back in

        Assert.Equal("\x1b[O\x1b[I", replies.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void AndSaysNothingWhenTheHostNeverAsked()
    {
        // The default. A shell that did not enable the mode would print the letters.
        var (window, canvas, other, _, replies) = BuildTerminalAndSomewhereElseToLook();
        canvas.Focus();
        replies.Clear();

        other.Focus();
        canvas.Focus();

        Assert.Equal("", replies.ToString());
        window.Close();
    }
}
