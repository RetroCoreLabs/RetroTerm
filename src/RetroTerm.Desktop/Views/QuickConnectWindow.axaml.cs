using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Asks for an address, a protocol and an emulator, and connects.
/// </summary>
/// <remarks>
/// <para><b>What this replaced, and why</b></para>
/// Until 25 August 2026 Quick Connect showed a search box over the SAVED connections. That is what
/// Manage Connections is for, so the one thing the window was named after - reaching something that
/// is not saved, quickly - was the one thing it could not do. Ronny's words: "it should be a ui
/// where it asks for what to connect to, protocol, emulator and then connect. no load/save".
/// <para><b>Nothing is saved and nothing is loaded</b></para>
/// No configuration manager is touched. The only thing that outlives the dialog is what was typed
/// last, kept in memory so a second connection to the same machine is one keypress - and that dies
/// with the process.
/// <para><b>Three fields, because three are all that cannot be guessed</b></para>
/// The port follows the protocol until somebody types over it, and the credentials only appear for
/// SSH, which is the only protocol here that cannot start without them.
/// </remarks>
public partial class QuickConnectWindow : Window
{
    /// <summary>
    /// The protocols offered, in the order they are offered.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from the enum so the ORDER is a decision. Telnet first because
    /// it is what nearly every machine on Ronny's bench speaks; the simulator last because it takes
    /// no address at all.
    /// </remarks>
    private static readonly ConnectionFactory.ProtocolType[] Protocols =
    {
        ConnectionFactory.ProtocolType.Telnet,
        ConnectionFactory.ProtocolType.SSH,
        ConnectionFactory.ProtocolType.Serial,
        ConnectionFactory.ProtocolType.TN3270,
        ConnectionFactory.ProtocolType.Gateway,
        ConnectionFactory.ProtocolType.OpcomSimulator,
    };

    /// <summary>
    /// What was typed the last time this dialog was used, for the rest of this session.
    /// </summary>
    /// <remarks>
    /// NOT saved to disk, and deliberately so - Ronny asked for no load and no save. This is only
    /// so that reconnecting to the machine you just left does not mean typing it again. The
    /// password is not among these: keeping one in memory for the life of the process, for a dialog
    /// that never asked to hold it, is not a trade worth making for a keystroke.
    /// </remarks>
    private static string _lastHost = string.Empty;

    private static ConnectionFactory.ProtocolType _lastProtocol = ConnectionFactory.ProtocolType.Telnet;

    private static string _lastEmulator = string.Empty;

    private static string _lastUser = string.Empty;

    /// <summary>
    /// True once the port has been typed in by hand, so changing protocol stops overwriting it.
    /// </summary>
    private bool _portEdited;

    /// <summary>
    /// True while the code is writing the port box, so its own change is not read as a person
    /// typing.
    /// </summary>
    private bool _settingPort;

    public QuickConnectWindow()
    {
        InitializeComponent();
        Build();
    }

    /// <summary>
    /// Kept so the call site does not have to change; nothing here reads the configuration.
    /// </summary>
    /// <param name="configManager">
    /// Ignored. Quick Connect neither loads nor saves.
    /// </param>
    /// <remarks>
    /// The saved connections belong to Manage Connections. Taking the manager and not using it
    /// would normally be worth deleting - it is kept only because removing it changes the call in
    /// MainWindow for no gain, and because the parameter name says plainly what it is not for.
    /// </remarks>
    public QuickConnectWindow(ConfigurationManager configManager)
        : this()
    {
        _ = configManager;
    }

    /// <summary>
    /// Fills the lists in, restores what was typed last, and wires the buttons.
    /// </summary>
    private void Build()
    {
        var hostBox = this.FindControl<TextBox>("HostBox");
        var protocolBox = this.FindControl<ComboBox>("ProtocolBox");
        var emulatorBox = this.FindControl<ComboBox>("EmulatorBox");
        var portBox = this.FindControl<TextBox>("PortBox");
        var userBox = this.FindControl<TextBox>("UserBox");
        var cancelBtn = this.FindControl<Button>("CancelBtn");
        var connectBtn = this.FindControl<Button>("ConnectBtn");

        // Protocols, by their own names.
        if (protocolBox != null)
        {
            var names = new string[Protocols.Length];
            for (int i = 0; i < Protocols.Length; i++)
            {
                names[i] = Protocols[i].ToString();
            }

            protocolBox.ItemsSource = names;
            protocolBox.SelectedIndex = IndexOfProtocol(_lastProtocol);
            protocolBox.SelectionChanged += (_, _) => OnProtocolChanged();
        }

        // Emulators, read from the factory rather than written out, so a new terminal appears here
        // the day it is added rather than the day somebody remembers this list exists.
        if (emulatorBox != null)
        {
            var emulators = EmulatorFactory.AvailableEmulators;
            emulatorBox.ItemsSource = emulators;

            int selected = 0;
            for (int i = 0; i < emulators.Length; i++)
            {
                if (string.Equals(emulators[i], _lastEmulator, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                    break;
                }
            }

            emulatorBox.SelectedIndex = selected;
        }

        if (hostBox != null)
        {
            hostBox.Text = _lastHost;

            // THE PROPERTY, not the TextChanged event. TextChanged does not fire when the text is
            // set from code, which is how the headless tests drive the form - so the Connect button
            // stayed grey with a perfectly good address in the box, and the port below stayed
            // "unedited" no matter what was put in it. Both were caught by the tests on the first
            // run. Watching the property is right in the app too: it cannot miss a change whatever
            // caused it.
            hostBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty) UpdateConnectEnabled();
            };
        }

        if (userBox != null) userBox.Text = _lastUser;

        if (portBox != null)
        {
            portBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && !_settingPort) _portEdited = true;
            };
        }

        if (cancelBtn != null) cancelBtn.Click += (_, _) => Close(null);
        if (connectBtn != null) connectBtn.Click += (_, _) => OnConnect();

        OnProtocolChanged();
        UpdateConnectEnabled();

        // ENTER CONNECTS FROM ANYWHERE, which is most of what "quick" means here. Handled on the
        // window rather than on each box so a field added later gets it for nothing.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        Opened += (_, _) => hostBox?.Focus();
    }

    /// <summary>
    /// Enter connects, Escape closes.
    /// </summary>
    /// <param name="sender">
    /// Unused.
    /// </param>
    /// <param name="e">
    /// The key that was pressed.
    /// </param>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnConnect();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
        }
    }

    /// <summary>
    /// Where a protocol sits in the list.
    /// </summary>
    /// <param name="protocol">
    /// The protocol to find.
    /// </param>
    /// <returns>
    /// Its index, or 0 when it is not offered.
    /// </returns>
    private static int IndexOfProtocol(ConnectionFactory.ProtocolType protocol)
    {
        for (int i = 0; i < Protocols.Length; i++)
        {
            if (Protocols[i] == protocol) return i;
        }

        return 0;
    }

    /// <summary>
    /// The protocol currently chosen.
    /// </summary>
    /// <returns>
    /// The protocol.
    /// </returns>
    private ConnectionFactory.ProtocolType SelectedProtocol()
    {
        var protocolBox = this.FindControl<ComboBox>("ProtocolBox");
        int index = protocolBox?.SelectedIndex ?? 0;
        if (index < 0 || index >= Protocols.Length) return ConnectionFactory.ProtocolType.Telnet;
        return Protocols[index];
    }

    /// <summary>
    /// Reshapes the form for the protocol that was picked.
    /// </summary>
    /// <remarks>
    /// A serial line has a port NAME and no number, so the number box goes away rather than sitting
    /// there inviting a value that would be ignored. SSH gains the two fields it cannot start
    /// without. Everything else is a host and a number.
    /// </remarks>
    private void OnProtocolChanged()
    {
        ConnectionFactory.ProtocolType protocol = SelectedProtocol();

        var hostBox = this.FindControl<TextBox>("HostBox");
        var portPanel = this.FindControl<StackPanel>("PortPanel");
        var credentials = this.FindControl<Grid>("CredentialsPanel");
        var portBox = this.FindControl<TextBox>("PortBox");

        bool serial = protocol == ConnectionFactory.ProtocolType.Serial;

        if (portPanel != null) portPanel.IsVisible = !serial;
        if (credentials != null) credentials.IsVisible = protocol == ConnectionFactory.ProtocolType.SSH;

        if (hostBox != null)
        {
            hostBox.Watermark = serial ? "COM3" : "host or host:port";
        }

        // The port follows the protocol only while nobody has typed one. Overwriting a number
        // somebody chose, because they then changed a dropdown, is the kind of helpfulness that
        // makes a form untrustworthy.
        if (portBox != null && !_portEdited)
        {
            _settingPort = true;
            portBox.Text = ConnectionFactory.DefaultPortFor(protocol).ToString();
            _settingPort = false;
        }

        UpdateConnectEnabled();
    }

    /// <summary>
    /// Greys the Connect button out until there is something to connect to.
    /// </summary>
    private void UpdateConnectEnabled()
    {
        var hostBox = this.FindControl<TextBox>("HostBox");
        var connectBtn = this.FindControl<Button>("ConnectBtn");
        if (connectBtn == null) return;

        // The simulator is a program, not a machine, so it needs no address.
        if (SelectedProtocol() == ConnectionFactory.ProtocolType.OpcomSimulator)
        {
            connectBtn.IsEnabled = true;
            return;
        }

        connectBtn.IsEnabled = !string.IsNullOrWhiteSpace(hostBox?.Text);
    }

    /// <summary>
    /// Shows why the dialog will not connect yet.
    /// </summary>
    /// <param name="message">
    /// What is wrong, or null to clear it.
    /// </param>
    private void ShowError(string? message)
    {
        var errorText = this.FindControl<TextBlock>("ErrorText");
        if (errorText == null) return;

        errorText.Text = message ?? string.Empty;
        errorText.IsVisible = !string.IsNullOrEmpty(message);
    }

    /// <summary>
    /// Builds the parameters and closes with them.
    /// </summary>
    private void OnConnect()
    {
        var parameters = BuildParameters(out string? error);
        if (parameters == null)
        {
            ShowError(error);
            return;
        }

        Close(parameters);
    }

    /// <summary>
    /// Turns what is on the form into connection parameters.
    /// </summary>
    /// <param name="error">
    /// Receives why it could not be done, or null when it could.
    /// </param>
    /// <returns>
    /// The parameters, or null when the form is not ready.
    /// </returns>
    /// <remarks>
    /// Internal rather than private so the headless tests can drive it. The alternative is a test
    /// that clicks a button and reads a dialog result, which cannot say WHICH field was wrong.
    /// </remarks>
    internal ConnectionFactory.ConnectionParameters? BuildParameters(out string? error)
    {
        error = null;

        ConnectionFactory.ProtocolType protocol = SelectedProtocol();
        var hostBox = this.FindControl<TextBox>("HostBox");
        var portBox = this.FindControl<TextBox>("PortBox");
        var emulatorBox = this.FindControl<ComboBox>("EmulatorBox");
        var userBox = this.FindControl<TextBox>("UserBox");
        var passwordBox = this.FindControl<TextBox>("PasswordBox");

        string typed = hostBox?.Text ?? string.Empty;
        string emulator = emulatorBox?.SelectedItem as string ?? "VT100";

        // The terminal's own screen: this dialog used to leave the 80 by 24 default in place, so
        // a TDV opened here was one row short.
        var (width, height) = RetroTerm.Core.Configuration.EmulatorFactory.GetRecommendedSize(emulator);
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = protocol,
            EmulatorType = emulator,
            Width = width,
            Height = height,
        };

        if (protocol == ConnectionFactory.ProtocolType.Serial)
        {
            if (string.IsNullOrWhiteSpace(typed))
            {
                error = "Which serial port? Something like COM3.";
                return null;
            }

            parameters.PortName = typed.Trim();
        }
        else if (protocol == ConnectionFactory.ProtocolType.OpcomSimulator)
        {
            // Takes no address at all; whatever was typed is ignored rather than refused.
            parameters.Host = typed.Trim();
        }
        else
        {
            if (!ConnectionFactory.SplitHostAndPort(typed, out string host, out int portFromHost))
            {
                error = "Type a host to connect to.";
                return null;
            }

            parameters.Host = host;

            // A port typed INTO THE ADDRESS wins over the box: it is the more specific thing, and
            // it is what somebody who pastes "machine:2323" means.
            if (portFromHost >= 0)
            {
                parameters.Port = portFromHost;
            }
            else if (!int.TryParse(portBox?.Text, out int port) || port <= 0 || port > 65535)
            {
                error = "The port has to be a number from 1 to 65535.";
                return null;
            }
            else
            {
                parameters.Port = port;
            }
        }

        if (protocol == ConnectionFactory.ProtocolType.SSH)
        {
            parameters.Username = userBox?.Text ?? string.Empty;
            parameters.Password = passwordBox?.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(parameters.Username))
            {
                error = "SSH needs a username.";
                return null;
            }
        }

        _lastHost = typed;
        _lastProtocol = protocol;
        _lastEmulator = emulator;
        _lastUser = parameters.Username ?? string.Empty;

        return parameters;
    }

    /// <summary>
    /// Forgets what was typed last. For tests, so one does not lean on another.
    /// </summary>
    internal static void ForgetLastUsed()
    {
        _lastHost = string.Empty;
        _lastProtocol = ConnectionFactory.ProtocolType.Telnet;
        _lastEmulator = string.Empty;
        _lastUser = string.Empty;
    }
}
