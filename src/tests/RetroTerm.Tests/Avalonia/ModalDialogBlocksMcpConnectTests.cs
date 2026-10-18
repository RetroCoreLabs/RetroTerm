using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A connection opened from outside must not build a tab underneath an open dialog.
/// </summary>
/// <remarks>
/// <para><b>What happened</b></para>
/// On 25 August 2026 a connection was opened from the MCP side while Ronny was working in Manage
/// Connections. The interface broke and he had to kill the terminal, losing what he had been
/// editing.
/// <para><b>Why it breaks</b></para>
/// Manage Connections is shown with <c>ShowDialog(this)</c>, which is a true modal - the main window
/// is DISABLED for as long as it is up. The MCP open path then calls <c>CreateTab</c> on that main
/// window, and a <c>TerminalCanvas</c> takes the focus the moment it is attached to the visual tree.
/// A control grabbing focus inside a window the platform has disabled behind a modal is not
/// something the interface is prepared for.
/// <para><b>Why it refuses rather than queues</b></para>
/// The caller is told exactly what is in the way, and the person at the keyboard is not interrupted
/// by something they never asked for. Queueing would mean a tab appearing later for reasons the
/// reader cannot connect to anything they did.
/// <para><b>What is and is not tested here</b></para>
/// The detection is tested. Whether a focus grab inside a disabled window breaks a real Win32
/// message loop is not something a headless test can answer - which is exactly why the fix is to
/// stay out of that situation rather than to try to survive it.
/// </remarks>
[Collection("Avalonia")]
public class ModalDialogBlocksMcpConnectTests
{
    [AvaloniaFact]
    public void WithNoDialogOpenNothingIsInTheWay()
    {
        // The control case, and the one that matters most: this guard must not stop ordinary use.
        var main = new MainWindow();

        Assert.Null(main.OpenModalDialogTitle());
    }

    [AvaloniaFact]
    public void AnOpenDialogIsReportedByName()
    {
        // Reported BY NAME so the refusal can say which window is in the way. "A dialog is open" is
        // a worse message than "Manage Connections is open" by exactly the amount of hunting it
        // saves the reader.
        var main = new MainWindow();
        main.Show();

        var dialog = new Window { Title = "Manage Connections" };
        dialog.Show(main);

        try
        {
            Assert.Equal("Manage Connections", main.OpenModalDialogTitle());
        }
        finally
        {
            dialog.Close();
            main.Close();
        }
    }

    [AvaloniaFact]
    public void ClosingTheDialogClearsTheWay()
    {
        // A guard that never lifts would be a worse defect than the one it fixes: MCP would stop
        // working permanently the first time anybody opened a dialog.
        var main = new MainWindow();
        main.Show();

        var dialog = new Window { Title = "Manage Connections" };
        dialog.Show(main);
        Assert.NotNull(main.OpenModalDialogTitle());

        dialog.Close();

        try
        {
            Assert.Null(main.OpenModalDialogTitle());
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void ADialogBelongingToAnotherWindowIsNotOurs()
    {
        // The window list holds every window in the application. Only the ones OWNED by this main
        // window disable it, so only those are in the way - a second main window's dialog is that
        // window's business.
        var main = new MainWindow();
        var other = new MainWindow();
        main.Show();
        other.Show();

        var dialog = new Window { Title = "Somebody Else's Dialog" };
        dialog.Show(other);

        try
        {
            Assert.Null(main.OpenModalDialogTitle());
            Assert.Equal("Somebody Else's Dialog", other.OpenModalDialogTitle());
        }
        finally
        {
            dialog.Close();
            other.Close();
            main.Close();
        }
    }

    [AvaloniaFact]
    public void ADialogWithNoTitleStillReportsSomethingUseful()
    {
        // An empty title must not turn the refusal message into "A dialog is open () so...". The
        // type name is a poor label but it is a findable one.
        var main = new MainWindow();
        main.Show();

        var dialog = new Window();
        dialog.Show(main);

        try
        {
            string? reported = main.OpenModalDialogTitle();
            Assert.False(string.IsNullOrEmpty(reported));
        }
        finally
        {
            dialog.Close();
            main.Close();
        }
    }
}
