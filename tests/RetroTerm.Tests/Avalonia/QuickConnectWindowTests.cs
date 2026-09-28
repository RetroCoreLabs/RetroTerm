using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Protocols;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Quick Connect - the form that asks where to go and goes there.
/// </summary>
/// <remarks>
/// <para><b>What it used to be</b></para>
/// A search box over the SAVED connections, which is what Manage Connections is for. So the window
/// could not do the one thing its name promises: reach something that is not saved. Replaced on
/// 25 August 2026 at Ronny's request - "it should be a ui where it asks for what to connect to,
/// protocol, emulator and then connect. no load/save".
/// <para><b>What these tests can and cannot say</b></para>
/// They drive the real window and read the parameters it would hand back, so they cover every rule
/// about what the form MEANS. Whether the layout reads well on his monitor is not something a test
/// can answer and is not claimed here.
/// </remarks>
[Collection("Avalonia")]
public class QuickConnectWindowTests
{
    /// <summary>
    /// A fresh window with nothing remembered from an earlier one.
    /// </summary>
    /// <returns>
    /// The window.
    /// </returns>
    /// <remarks>
    /// The dialog remembers the last address for the life of the process, which is a convenience in
    /// the app and a way for one test to change another's answer. Cleared every time.
    /// </remarks>
    private static QuickConnectWindow FreshWindow()
    {
        QuickConnectWindow.ForgetLastUsed();
        return new QuickConnectWindow();
    }

    /// <summary>
    /// Types an address into the form.
    /// </summary>
    /// <param name="window">
    /// The window to type into.
    /// </param>
    /// <param name="text">
    /// What to type.
    /// </param>
    private static void TypeAddress(QuickConnectWindow window, string text)
    {
        var hostBox = window.FindControl<TextBox>("HostBox");
        Assert.NotNull(hostBox);
        hostBox!.Text = text;
    }

    /// <summary>
    /// Picks a protocol by its position in the dropdown.
    /// </summary>
    /// <param name="window">
    /// The window to change.
    /// </param>
    /// <param name="protocol">
    /// The protocol to pick.
    /// </param>
    private static void ChooseProtocol(QuickConnectWindow window,
        ConnectionFactory.ProtocolType protocol)
    {
        var protocolBox = window.FindControl<ComboBox>("ProtocolBox");
        Assert.NotNull(protocolBox);

        var names = (string[])protocolBox!.ItemsSource!;
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == protocol.ToString())
            {
                protocolBox.SelectedIndex = i;
                return;
            }
        }

        Assert.Fail("the protocol " + protocol + " is not offered in the dropdown");
    }

    [AvaloniaFact]
    public void ItOpensAsAFormRatherThanAListOfSavedConnections()
    {
        // The whole point of the change. If a list ever comes back, this is the test that says so.
        var window = FreshWindow();

        Assert.NotNull(window.FindControl<TextBox>("HostBox"));
        Assert.NotNull(window.FindControl<ComboBox>("ProtocolBox"));
        Assert.NotNull(window.FindControl<ComboBox>("EmulatorBox"));
        Assert.Null(window.FindControl<ListBox>("ConnectionsListBox"));
    }

    [AvaloniaFact]
    public void EveryEmulatorTheProgramHasIsOffered()
    {
        // Read from the factory rather than a written-out list, so a terminal added later shows up
        // here without anybody remembering this window exists.
        var window = FreshWindow();
        var emulatorBox = window.FindControl<ComboBox>("EmulatorBox");

        Assert.Equal(RetroTerm.Core.Configuration.EmulatorFactory.AvailableEmulators,
            emulatorBox!.ItemsSource);
    }

    [AvaloniaFact]
    public void AHostAndTheDefaultPortAreEnoughToConnect()
    {
        var window = FreshWindow();
        TypeAddress(window, "localhost");

        var parameters = window.BuildParameters(out string? error);

        Assert.Null(error);
        Assert.NotNull(parameters);
        Assert.Equal(ConnectionFactory.ProtocolType.Telnet, parameters!.Protocol);
        Assert.Equal("localhost", parameters.Host);
        Assert.Equal(23, parameters.Port);
    }

    [AvaloniaFact]
    public void APortTypedIntoTheAddressWins()
    {
        // What somebody means when they paste "machine:2323" - the more specific thing beats the
        // box that was filled in for them.
        var window = FreshWindow();
        TypeAddress(window, "127.0.0.1:2323");

        var parameters = window.BuildParameters(out _);

        Assert.Equal("127.0.0.1", parameters!.Host);
        Assert.Equal(2323, parameters.Port);
    }

    [AvaloniaFact]
    public void ChoosingSshMovesThePortToTwentyTwo()
    {
        var window = FreshWindow();
        ChooseProtocol(window, ConnectionFactory.ProtocolType.SSH);

        var portBox = window.FindControl<TextBox>("PortBox");

        Assert.Equal("22", portBox!.Text);
    }

    [AvaloniaFact]
    public void APortTypedByHandSurvivesChangingTheProtocol()
    {
        // The kind of helpfulness that makes a form untrustworthy: silently replacing a number
        // somebody chose because they then touched a dropdown.
        var window = FreshWindow();
        var portBox = window.FindControl<TextBox>("PortBox");
        portBox!.Text = "2323";

        ChooseProtocol(window, ConnectionFactory.ProtocolType.SSH);

        Assert.Equal("2323", portBox.Text);
    }

    [AvaloniaFact]
    public void SshAsksForAUsernameAndRefusesWithoutOne()
    {
        var window = FreshWindow();
        ChooseProtocol(window, ConnectionFactory.ProtocolType.SSH);
        TypeAddress(window, "ubuntu18lts.hackercorp.no");

        Assert.True(window.FindControl<Grid>("CredentialsPanel")!.IsVisible,
            "SSH is the one protocol here that cannot start without a username, so the fields have "
            + "to be on screen");

        var parameters = window.BuildParameters(out string? error);

        Assert.Null(parameters);
        Assert.Contains("username", error!, System.StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TelnetDoesNotAskForCredentials()
    {
        var window = FreshWindow();

        Assert.False(window.FindControl<Grid>("CredentialsPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void ASerialLineAsksForAPortNameAndNotANumber()
    {
        var window = FreshWindow();
        ChooseProtocol(window, ConnectionFactory.ProtocolType.Serial);
        TypeAddress(window, "COM14");

        Assert.False(window.FindControl<StackPanel>("PortPanel")!.IsVisible,
            "a serial line has no port NUMBER, so a box inviting one would be answered and ignored");

        var parameters = window.BuildParameters(out string? error);

        Assert.Null(error);
        Assert.Equal("COM14", parameters!.PortName);
    }

    [AvaloniaFact]
    public void AnEmptyAddressIsRefusedWithAReasonRatherThanSilently()
    {
        var window = FreshWindow();

        var parameters = window.BuildParameters(out string? error);

        Assert.Null(parameters);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [AvaloniaFact]
    public void ConnectIsGreyedOutUntilThereIsSomewhereToGo()
    {
        var window = FreshWindow();
        var connectBtn = window.FindControl<Button>("ConnectBtn");

        Assert.False(connectBtn!.IsEnabled);

        TypeAddress(window, "localhost");

        Assert.True(connectBtn.IsEnabled);
    }

    [AvaloniaFact]
    public void APortOutsideTheRangeIsRefused()
    {
        var window = FreshWindow();
        TypeAddress(window, "localhost");
        window.FindControl<TextBox>("PortBox")!.Text = "70000";

        var parameters = window.BuildParameters(out string? error);

        Assert.Null(parameters);
        Assert.Contains("65535", error!);
    }

    [AvaloniaFact]
    public void TheFormActuallyDraws()
    {
        // AND THE PNG GETS OPENED. Every assertion above is about what the form MEANS; none of them
        // would notice a control off the bottom of the window, a label the same colour as the
        // background, or two boxes on top of each other. Three defects in this project were found
        // exactly that way while every test was green.
        var window = FreshWindow();
        window.Show();

        var shot = RenderedScreenshot.CaptureWindow(window, "quick-connect-form");

        Assert.NotNull(shot);
        using (shot)
        {
            Assert.True(shot!.HasAnyPixelDifferentFrom(shot.PixelAt(0, 0)),
                "the whole window is one flat colour, so nothing was drawn at all");
        }
    }

    [AvaloniaFact]
    public void TheNextWindowOpensOnWhereYouJustWent()
    {
        // The one thing that outlives the dialog, and the reason it does: connecting twice to the
        // same machine should not mean typing it twice. It is memory, not a saved connection -
        // nothing reaches the disk.
        var first = FreshWindow();
        TypeAddress(first, "d100.lab:9010");
        Assert.NotNull(first.BuildParameters(out _));

        var second = new QuickConnectWindow();

        Assert.Equal("d100.lab:9010", second.FindControl<TextBox>("HostBox")!.Text);
    }
}
