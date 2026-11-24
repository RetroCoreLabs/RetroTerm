//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Connection commands: connect the CURRENT session ad-hoc or by stored connection
/// name, disconnect it, and manage the stored connection list (the same list the
/// Quick Connect / Manage Connections windows use).
///
/// The connection factory is pluggable so tests connect to in-memory endpoints.
/// </summary>
public static class ConnectionCommands
{
    public static void RegisterAll(CommandRegistry registry, ConfigurationManager configurationManager,
        Func<ConnectionFactory.ConnectionParameters, IConnection>? connectionFactory = null)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (configurationManager == null) throw new ArgumentNullException(nameof(configurationManager));

        var factory = connectionFactory ?? ConnectionFactory.CreateConnection;
        registry.Register(new ConnectCommand(configurationManager, factory));
        registry.Register(new DisconnectCommand());
        registry.Register(new ConnListCommand(configurationManager));
        registry.Register(new ConnShowCommand(configurationManager));
        registry.Register(new ConnSaveCommand(configurationManager));
        registry.Register(new ConnDelCommand(configurationManager));
    }

    /// <summary>
    /// Finds a stored connection by display name (case-insensitive), or null.
    /// Public because the MCP tool provider's open-by-name resolves through the
    /// same lookup — one rule for what a name means, on every surface.
    /// </summary>
    public static HostConfiguration? FindByName(ConfigurationManager manager, string name)
    {
        var configs = manager.Configurations;
        for (int i = 0; i < configs.Count; i++)
        {
            if (string.Equals(configs[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return configs[i];
            }
        }
        return null;
    }
}

/// <summary>
/// CONNECT — by stored name, or ad-hoc with host/port.
/// </summary>
public sealed class ConnectCommand : ISessionCommand
{
    private readonly ConfigurationManager _manager;
    private readonly Func<ConnectionFactory.ConnectionParameters, IConnection> _factory;

    public ConnectCommand(ConfigurationManager manager, Func<ConnectionFactory.ConnectionParameters, IConnection> factory)
    {
        _manager = manager;
        _factory = factory;
    }

    public string Name => "CONNECT";
    public string Summary => "Connect this session: by stored connection name, ad-hoc with host and port, "
        + "or ad-hoc serial with port_name and baud";
    public string Example => "CONNECT \"ND-lab\"   or   CONNECT host=127.0.0.1 port=5001   or   "
        + "CONNECT protocol=serial port_name=COM11 baud=115200 parity=even data_bits=7";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = Ibm3270ConnectionSettings.Join(new[]
    {
        new CommandParameter("name", CommandParameterType.String, required: false, defaultValue: null,
            "Stored connection name (CONNLIST shows them); when given, other parameters are ignored"),
        new CommandParameter("host", CommandParameterType.String, required: false, defaultValue: null,
            "Host name or IP for an ad-hoc telnet/ssh connection (serial uses port_name instead)"),
        new CommandParameter("port", CommandParameterType.Int, required: false, defaultValue: "23",
            "TCP port for an ad-hoc telnet/ssh connection"),
        // The allowed words are declared here (BUGS.md B20): a misspelled protocol is refused
        // by name on both surfaces instead of quietly becoming telnet.
        new CommandParameter("protocol", CommandParameterType.String, required: false, defaultValue: "telnet",
            "telnet, ssh, tn3270, serial or opcomsimulator (the built-in OPCOM simulator, no host)",
            allowedValues: ConnectionFactory.OpenableProtocolNames),
        new CommandParameter("username", CommandParameterType.String, required: false, defaultValue: null,
            "SSH user name"),
        new CommandParameter("password", CommandParameterType.String, required: false, defaultValue: null,
            "SSH password"),
        new CommandParameter("port_name", CommandParameterType.String, required: false, defaultValue: null,
            "Serial port name, e.g. COM11 (required for ad-hoc protocol=serial)"),
        new CommandParameter("baud", CommandParameterType.Int, required: false, defaultValue: "9600",
            "Serial speed in bits per second"),
        new CommandParameter("data_bits", CommandParameterType.Int, required: false, defaultValue: "8",
            "Serial data bits, 5 to 8"),
        new CommandParameter("parity", CommandParameterType.String, required: false, defaultValue: "none",
            "Serial parity: none, odd, even, mark or space"),
        new CommandParameter("stop_bits", CommandParameterType.String, required: false, defaultValue: "1",
            "Serial stop bits: 1, 1.5 or 2"),
        // The 3270 choices, declared once for CONNECT, CONNSAVE and terminal_open with the
        // words of the tables the Manage Connections window uses (Ibm3270ConnectionSettings).
        Ibm3270ConnectionSettings.ModelParameter(),
        Ibm3270ConnectionSettings.CodePageParameter(),
        // A 3278's screen is fixed when its terminal is built, and CONNECT keeps the tab's
        // terminal, so CONNECT takes screen_size only to check it: the tab's own screen is
        // accepted, any other is refused by name and terminal_open screen_size=... makes it.
        Ibm3270ConnectionSettings.ScreenSizeParameter()
    },
    // The double click and the card reader: both work on the tab CONNECT runs in, so CONNECT
    // takes them. The 3287 printer settings it does not (Ibm3270ConnectionSettings.PrinterArgumentNames
    // says why).
    Ibm3270ConnectionSettings.TabParameters());

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (session.IsConnected)
        {
            return CommandResult.Fail("already connected — DISCONNECT first");
        }

        ConnectionFactory.ConnectionParameters parameters;
        Ibm3270Choices choices;
        var name = args.GetString("name");
        if (name != null)
        {
            var config = ConnectionCommands.FindByName(_manager, name);
            if (config == null)
            {
                return CommandResult.Fail($"no stored connection named '{name}' — CONNLIST shows them");
            }
            parameters = config.ToConnectionParameters();

            // A stored 3270 connection on a tab that already is a 3278: the tab's terminal is
            // kept, so the stored model and screen must be the tab's, or the host would be told
            // one screen and given another.
            if (parameters.Protocol == ConnectionFactory.ProtocolType.TN3270 && session.Emulator is IModelScreenTerminal tabTerminal)
            {
                Ibm3270ConnectionSettings.Describe(tabTerminal, out var tabModel, out var tabScreen);
                string storedModel = ConnectionFactory.EmulatorTypeFor(ConnectionFactory.ProtocolType.TN3270, parameters.EmulatorType);
                string? storedScreen = parameters.Ibm3270ScreenSize;
                if (!string.Equals(storedModel, ConnectionFactory.Ibm3287, StringComparison.OrdinalIgnoreCase)
                    && (storedModel != tabModel || storedScreen != tabScreen))
                {
                    return CommandResult.Fail("stored connection '" + name + "' is " + storedModel + " with a "
                        + Ibm3270ConnectionSettings.ScreenText(storedModel, storedScreen) + " screen, but this tab's terminal is "
                        + tabModel + " with a " + Ibm3270ConnectionSettings.ScreenText(tabModel, tabScreen)
                        + " screen; CONNECT keeps the tab's terminal - open it with terminal_open name=" + name);
                }
            }

            config.MarkAsUsed();
            await _manager.SaveAsync().ConfigureAwait(false);
        }
        else
        {
            var protocol = ConnectionFactory.ParseProtocolType(args.GetString("protocol", "telnet")!);

            // model and codepage: only with tn3270. CONNECT keeps this tab's terminal (it does
            // not swap it), so a model can only be checked against the terminal the tab already
            // has: a different one is refused by name, and terminal_open model=... makes a new one.
            var problem3270 = Ibm3270ConnectionSettings.Check(protocol, null,
                args.GetString(Ibm3270ConnectionSettings.ModelArgument), args.GetString(Ibm3270ConnectionSettings.CodePageArgument),
                out var model, out var codePage);
            if (problem3270 != null)
            {
                return CommandResult.Fail(problem3270);
            }
            if (model != null && !IsTerminal(session, model))
            {
                return CommandResult.Fail("'" + Ibm3270ConnectionSettings.ModelArgument + "' is " + model
                    + " but this tab's terminal is " + session.Emulator.Profile.Name
                    + "; CONNECT keeps the tab's terminal - open a new one with terminal_open model=" + model);
            }

            // screen_size: by the emulator's own limits, then against the screen this tab has.
            // Its terminal is kept and a 3278's screen is fixed when it is built.
            var tab3278 = session.Emulator as IModelScreenTerminal;
            string? tabModel = null;
            string? tabScreen = null;
            if (tab3278 != null)
            {
                Ibm3270ConnectionSettings.Describe(tab3278, out tabModel, out tabScreen);
            }
            var problemScreen = Ibm3270ConnectionSettings.CheckScreenSize(protocol, model ?? tabModel,
                args.GetString(Ibm3270ConnectionSettings.ScreenSizeArgument), out var screen);
            if (problemScreen != null)
            {
                return CommandResult.Fail(problemScreen);
            }
            if (args.Contains(Ibm3270ConnectionSettings.ScreenSizeArgument))
            {
                string wantedModel = ConnectionFactory.EmulatorTypeFor(protocol, model ?? tabModel);
                if (tab3278 == null)
                {
                    return CommandResult.Fail("'" + Ibm3270ConnectionSettings.ScreenSizeArgument + "' needs a 3278 tab, and this tab's terminal is "
                        + session.Emulator.Profile.Name + "; open one with terminal_open protocol=tn3270 "
                        + Ibm3270ConnectionSettings.ScreenSizeArgument + "=" + args.GetString(Ibm3270ConnectionSettings.ScreenSizeArgument));
                }
                if (screen != tabScreen)
                {
                    return CommandResult.Fail("'" + Ibm3270ConnectionSettings.ScreenSizeArgument + "' is "
                        + Ibm3270ConnectionSettings.ScreenText(wantedModel, screen) + " but this tab's screen is "
                        + Ibm3270ConnectionSettings.ScreenText(tabModel!, tabScreen)
                        + "; CONNECT keeps the tab's terminal - open a new one with terminal_open "
                        + Ibm3270ConnectionSettings.ScreenSizeArgument + "=" + args.GetString(Ibm3270ConnectionSettings.ScreenSizeArgument));
                }
            }

            // The double click and the card reader, checked against the terminal this tab has.
            var problemChoices = Ibm3270ConnectionSettings.ReadChoices(args, protocol, session.Emulator.Profile.Name, out choices);
            if (problemChoices != null)
            {
                return CommandResult.Fail(problemChoices);
            }

            if (protocol == ConnectionFactory.ProtocolType.Serial)
            {
                // Serial has its own fields. host/port do not map onto a COM port, and
                // silently reinterpreting them cost a real debugging session — say so instead.
                if (args.Contains("host") || args.Contains("port"))
                {
                    return CommandResult.Fail(
                        "serial connections take port_name and baud, not host/port — "
                        + "e.g. CONNECT protocol=serial port_name=COM11 baud=115200");
                }
                var portName = args.GetString("port_name");
                if (portName == null)
                {
                    return CommandResult.Fail(
                        "serial needs port_name=... (e.g. port_name=COM11), plus optional "
                        + "baud, data_bits, parity, stop_bits");
                }

                int parityValue = 0;
                int stopBitsValue = 1;
                var problem = ConnectionFactory.ValidateSerialFields(
                    args.GetInt("baud", 9600), args.GetInt("data_bits", 8),
                    args.GetString("parity"), args.GetString("stop_bits"),
                    ref parityValue, ref stopBitsValue);
                if (problem != null)
                {
                    return CommandResult.Fail(problem);
                }

                parameters = new ConnectionFactory.ConnectionParameters
                {
                    Protocol = ConnectionFactory.ProtocolType.Serial,
                    PortName = portName,
                    BaudRate = args.GetInt("baud", 9600),
                    DataBits = args.GetInt("data_bits", 8),
                    ParityValue = parityValue,
                    StopBitsValue = stopBitsValue,
                    // The session keeps its current emulator — CONNECT does not swap it.
                    EmulatorType = session.Emulator.GetType().Name
                };
            }
            else
            {
                // The mirror-image guard: a serial field with a network protocol is a
                // mistake worth naming, not a value to drop on the floor.
                if (args.Contains("port_name") || args.Contains("baud") || args.Contains("data_bits")
                    || args.Contains("parity") || args.Contains("stop_bits"))
                {
                    return CommandResult.Fail(
                        "port_name/baud/data_bits/parity/stop_bits only apply with protocol=serial");
                }
                var host = args.GetString("host");
                // The OPCOM simulator runs inside RetroTerm and has no host to name.
                if (protocol == ConnectionFactory.ProtocolType.OpcomSimulator)
                {
                    host ??= string.Empty;
                }
                if (host == null)
                {
                    return CommandResult.Fail("CONNECT needs a stored name, host=..., or protocol=serial with port_name=...");
                }
                parameters = new ConnectionFactory.ConnectionParameters
                {
                    Protocol = protocol,
                    Host = host,
                    Port = args.GetInt("port", 23),
                    Username = args.GetString("username"),
                    Password = args.GetString("password"),
                    // The session keeps its current emulator — CONNECT does not swap it.
                    EmulatorType = session.Emulator.GetType().Name,
                    CodePage = codePage
                };
                choices.ApplyTo(parameters);
            }
        }

        // The terminal type the host is told comes from the tab's terminal, which CONNECT keeps,
        // on both paths (TellHostTheTabsTerminal says why).
        var terminalProblem = TellHostTheTabsTerminal(session, parameters, name);
        if (terminalProblem != null)
        {
            return CommandResult.Fail(terminalProblem);
        }

        // A stored serial connection saved without a port name would otherwise die inside the
        // factory with its internal parameter name — catch it here with the field to fix.
        if (parameters.Protocol == ConnectionFactory.ProtocolType.Serial
            && string.IsNullOrWhiteSpace(parameters.PortName))
        {
            return CommandResult.Fail(
                $"stored connection '{name}' has no serial port name — CONNSAVE it with port_name=COM...");
        }

        // The 3270 code page, given now or saved with the stored connection, through the same
        // helper the desktop tab and the headless host use. Before the connect, so the first
        // screen is already in the right characters.
        var codePageProblem = await Ibm3270ConnectionSettings.ApplyCodePageAsync(session, parameters.CodePage).ConfigureAwait(false);
        if (codePageProblem != null)
        {
            return CommandResult.Fail(codePageProblem);
        }

        // The double click and the card reader SUBMIT sends to, given now or saved with the stored
        // connection, by the rule the desktop tab and the headless host use. Always set, so a
        // connection that names no card reader does not keep the one the tab had before.
        Ibm3270ConnectionSettings.ApplyToSession(session, parameters);

        try
        {
            // The factory is inside the try: a creation failure (bad port name, missing
            // driver) must report like any other connect failure, not escape as a raw throw.
            var connection = _factory(parameters);
            await session.ConnectAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"connect to {parameters.DisplayName} failed: {ex.Message}");
        }

        return CommandResult.Ok($"connected to {parameters.DisplayName}");
    }

    // A TN3270 connection tells the host what the terminal is (TERMINAL-TYPE), and CONNECT keeps
    // the tab's terminal, so the name must be the tab's: a 3278's model and, when it is larger,
    // its screen (IBM-DYNAMIC), or the 3287 printer's name. Any other terminal cannot run the 3270
    // data stream and is refused by name. The ONE place either path sets the name:
    //  - until 24 November 2026 a CONNECT with host=... sent the emulator's class name
    //    ("Ibm3270Emulator"), which ConnectionFactory.EmulatorTypeFor made a model 2, so a model 5
    //    tab announced IBM-3278-2-E;
    //  - until 25 November 2026 a CONNECT by stored name sent the STORED model whenever the tab
    //    was not a 3278 (a printer tab, a VT100 tab), and a stored printer connection on a 3278
    //    tab told the host IBM-3287-1 while the tab kept its 3278 (PLAN.md, found 24 November).
    // Returns null when the name is set, else why the connection is refused.
    private static string? TellHostTheTabsTerminal(TerminalSession session, ConnectionFactory.ConnectionParameters parameters, string? storedName)
    {
        if (parameters.Protocol != ConnectionFactory.ProtocolType.TN3270)
        {
            return null;
        }

        if (session.Emulator is IModelScreenTerminal tab3278)
        {
            Ibm3270ConnectionSettings.Describe(tab3278, out var tabModel, out var tabScreen);
            parameters.EmulatorType = tabModel;
            parameters.Ibm3270ScreenSize = tabScreen;
            return null;
        }

        // The 3287 printer's profile carries the name it announces (Ibm3287PrinterEmulator,
        // TerminalType); this layer does not reference that class.
        if (string.Equals(session.Emulator.GetTerminalType(), ConnectionFactory.Ibm3287, StringComparison.Ordinal))
        {
            parameters.EmulatorType = ConnectionFactory.Ibm3287;
            parameters.Ibm3270ScreenSize = null;
            return null;
        }

        return "this tab's terminal is " + session.Emulator.Profile.Name
            + ", which cannot run a TN3270 connection; CONNECT keeps the tab's terminal - open a 3270 one with terminal_open "
            + (storedName != null ? "name=" + storedName : "protocol=tn3270");
    }

    // Whether the tab's terminal is this 3278 model. The emulator's profile carries the name it
    // announces to the host, the model plus "-E" (Ibm3270Emulator.TerminalTypeFor), so the
    // model is that name without the two-character suffix.
    private static bool IsTerminal(TerminalSession session, string model)
    {
        // A screen larger than the model's announces IBM-DYNAMIC, which names no model, so the
        // model is read from the terminal itself when it can say.
        if (session.Emulator is IModelScreenTerminal terminal)
        {
            Ibm3270ConnectionSettings.Describe(terminal, out var tabModel, out _);
            return string.Equals(tabModel, model, StringComparison.OrdinalIgnoreCase);
        }

        string name = session.Emulator.Profile.Name;
        return string.Equals(name, model, StringComparison.OrdinalIgnoreCase)
            || (name.Length == model.Length + 2 && name.StartsWith(model + "-", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// DISCONNECT — user-initiated disconnect of this session.
/// </summary>
public sealed class DisconnectCommand : ISessionCommand
{
    public string Name => "DISCONNECT";
    public string Summary => "Disconnect this session (deliberate — does not fire the connection-lost handling)";
    public string Example => "DISCONNECT";

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (!session.IsConnected)
        {
            return CommandResult.Ok("already disconnected");
        }
        await session.DisconnectAsync().ConfigureAwait(false);
        return CommandResult.Ok("disconnected");
    }
}

/// <summary>
/// CONNLIST — the stored connections.
/// </summary>
public sealed class ConnListCommand : ISessionCommand
{
    private readonly ConfigurationManager _manager;

    public ConnListCommand(ConfigurationManager manager) => _manager = manager;

    public string Name => "CONNLIST";
    public string Summary => "List the stored connections (name, target, protocol; * = favorite)";
    public string Example => "CONNLIST";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var configs = _manager.Configurations;
        if (configs.Count == 0)
        {
            return Task.FromResult(CommandResult.Ok("no stored connections"));
        }

        var sb = new StringBuilder(configs.Count * 48);
        for (int i = 0; i < configs.Count; i++)
        {
            var c = configs[i];
            sb.Append(c.IsFavorite ? "* " : "  ")
              .Append(c.Name).Append("  ")
              .Append(c.ToConnectionParameters().DisplayName)
              .Append("  ").Append(c.Protocol)
              .Append("  ").Append(c.EmulatorType).Append('\n');
        }
        return Task.FromResult(CommandResult.Ok(sb.ToString()));
    }
}

/// <summary>
/// CONNSHOW — details of one stored connection.
/// </summary>
public sealed class ConnShowCommand : ISessionCommand
{
    private readonly ConfigurationManager _manager;

    public ConnShowCommand(ConfigurationManager manager) => _manager = manager;

    public string Name => "CONNSHOW";
    public string Summary => "Show the details of one stored connection";
    public string Example => "CONNSHOW \"ND-lab\"";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("name", CommandParameterType.String, required: true, defaultValue: null,
            "Stored connection name")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var config = ConnectionCommands.FindByName(_manager, args.GetString("name")!);
        if (config == null)
        {
            return Task.FromResult(CommandResult.Fail($"no stored connection named '{args.GetString("name")}'"));
        }

        var sb = new StringBuilder(256);
        sb.Append("name: ").Append(config.Name).Append('\n');
        sb.Append("protocol: ").Append(config.Protocol).Append('\n');
        sb.Append("target: ").Append(config.ToConnectionParameters().DisplayName).Append('\n');
        if (ConnectionFactory.ParseProtocolType(config.Protocol) == ConnectionFactory.ProtocolType.Serial)
        {
            sb.Append("framing: ")
              .Append(ConnectionFactory.DescribeFraming(config.DataBits, config.ParityValue, config.StopBitsValue))
              .Append('\n');
        }
        sb.Append("emulator: ").Append(config.EmulatorType)
          .Append(' ').Append(config.Width).Append('x').Append(config.Height).Append('\n');
        if (ConnectionFactory.ParseProtocolType(config.Protocol) == ConnectionFactory.ProtocolType.TN3270)
        {
            // The same default the window's code page box names when none is saved.
            sb.Append("code page: ")
              .Append(config.CodePage?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "37 (the terminal's default)")
              .Append('\n');
        }
        // The double click, the card reader and the 3287 printer settings, each where it means
        // something, by the same helper that reads them from CONNSAVE.
        Ibm3270ConnectionSettings.Describe(config, sb);
        if (!string.IsNullOrEmpty(config.Username))
        {
            sb.Append("username: ").Append(config.Username).Append('\n');
        }
        sb.Append("language: ").Append(config.Language).Append('\n');
        sb.Append("favorite: ").Append(config.IsFavorite ? "yes" : "no").Append('\n');
        if (config.OnConnectScript != null)
        {
            sb.Append("on connect: ").Append(config.OnConnectScript).Append('\n');
        }
        if (config.OnDisconnectScript != null)
        {
            sb.Append("on disconnect: ").Append(config.OnDisconnectScript).Append('\n');
        }
        sb.Append("used: ").Append(config.UseCount).Append(" times, last ")
          .Append(config.LastUsedAt.ToString("yyyy-MM-dd HH:mm")).Append(" UTC");
        if (!string.IsNullOrEmpty(config.Notes))
        {
            sb.Append("\nnotes: ").Append(config.Notes);
        }
        return Task.FromResult(CommandResult.Ok(sb.ToString()));
    }
}

/// <summary>
/// CONNSAVE — create a stored connection, or update an existing one by name.
/// </summary>
public sealed class ConnSaveCommand : ISessionCommand
{
    private readonly ConfigurationManager _manager;

    public ConnSaveCommand(ConfigurationManager manager) => _manager = manager;

    public string Name => "CONNSAVE";
    public string Summary => "Create or update a stored connection (existing name = update; only given values change)";
    public string Example => "CONNSAVE \"ND-lab\" host=127.0.0.1 port=5001 emulator=TDV2200   or   "
        + "CONNSAVE \"nexys\" protocol=Serial port_name=COM11 baud=115200 data_bits=7 parity=even";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = Ibm3270ConnectionSettings.Join(new[]
    {
        new CommandParameter("name", CommandParameterType.String, required: true, defaultValue: null,
            "Connection name (unique, case-insensitive)"),
        new CommandParameter("host", CommandParameterType.String, required: false, defaultValue: null,
            "Host name or IP (required when creating a telnet/ssh connection; Serial uses port_name)"),
        new CommandParameter("port", CommandParameterType.Int, required: false, defaultValue: "23",
            "TCP port (telnet/ssh only)"),
        // Every protocol a stored connection can hold, gateway included (the window opens those);
        // a misspelling is refused by name rather than stored and later read as telnet (BUGS.md B20).
        new CommandParameter("protocol", CommandParameterType.String, required: false, defaultValue: "Telnet",
            "telnet, ssh, tn3270, serial, gateway or opcomsimulator",
            allowedValues: ConnectionFactory.ProtocolNames),
        new CommandParameter("port_name", CommandParameterType.String, required: false, defaultValue: null,
            "Serial port name, e.g. COM11 (required when creating a Serial connection)"),
        new CommandParameter("baud", CommandParameterType.Int, required: false, defaultValue: "9600",
            "Serial speed in bits per second"),
        new CommandParameter("data_bits", CommandParameterType.Int, required: false, defaultValue: "8",
            "Serial data bits, 5 to 8"),
        new CommandParameter("parity", CommandParameterType.String, required: false, defaultValue: "none",
            "Serial parity: none, odd, even, mark or space"),
        new CommandParameter("stop_bits", CommandParameterType.String, required: false, defaultValue: "1",
            "Serial stop bits: 1, 1.5 or 2"),
        // Built from the factory's own list, so this help never again names three terminals when
        // the program can build thirteen.
        new CommandParameter("emulator", CommandParameterType.String, required: false, defaultValue: "VT100",
            "Terminal type: " + string.Join(", ", RetroTerm.Core.Configuration.EmulatorFactory.AvailableEmulators)),
        new CommandParameter("username", CommandParameterType.String, required: false, defaultValue: null,
            "SSH user name"),
        new CommandParameter("width", CommandParameterType.Int, required: false, defaultValue: null,
            "Screen columns; when omitted, the terminal type's own (80)"),
        new CommandParameter("height", CommandParameterType.Int, required: false, defaultValue: null,
            "Screen rows; when omitted, the terminal type's own (24 for a VT, 25 for a TDV)"),
        new CommandParameter("favorite", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Mark as favorite"),
        new CommandParameter("onconnect", CommandParameterType.String, required: false, defaultValue: null,
            "Script (library name) to run right after this connection is established, e.g. auto-login; empty string clears it"),
        new CommandParameter("ondisconnect", CommandParameterType.String, required: false, defaultValue: null,
            "Script (library name) to run when the connection DROPS (not on deliberate disconnect); empty string clears it"),
        // The same two 3270 choices as CONNECT and terminal_open, saved where the Manage
        // Connections window reads them: the model in EmulatorType (the size box), the code page
        // in CodePage (the code page box).
        Ibm3270ConnectionSettings.ModelParameter(),
        Ibm3270ConnectionSettings.CodePageParameter(),
        // A screen larger than the model's, saved where the window's size box reads it
        // (Ibm3270ScreenSize).
        Ibm3270ConnectionSettings.ScreenSizeParameter()
    },
    // The double click, the card reader and the 3287 printer settings, saved where the window's
    // Keyboard, Protocol and Printer tabs read them.
    Ibm3270ConnectionSettings.TabParameters(),
    Ibm3270ConnectionSettings.PrinterParameters());

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var name = args.GetString("name")!;
        var existing = ConnectionCommands.FindByName(_manager, name);

        // Which protocol the SAVED config ends up with decides which fields make sense —
        // the protocol given now, or on an update the one already stored.
        var effectiveProtocol = ConnectionFactory.ParseProtocolType(
            args.GetString("protocol") ?? existing?.Protocol ?? "Telnet");
        bool isSerial = effectiveProtocol == ConnectionFactory.ProtocolType.Serial;

        // Cross-protocol fields are refused, never silently dropped. This used to accept
        // host=COM11 port=115200 protocol=Serial, store nothing usable, and report success
        // as "? @ 9600bps" — the caller only found out when the connection failed later.
        if (isSerial && (args.Contains("host") || args.Contains("port")))
        {
            return CommandResult.Fail(
                "serial connections take port_name and baud, not host/port — "
                + "e.g. CONNSAVE \"" + name + "\" protocol=Serial port_name=COM11 baud=115200");
        }
        if (!isSerial && (args.Contains("port_name") || args.Contains("baud") || args.Contains("data_bits")
            || args.Contains("parity") || args.Contains("stop_bits")))
        {
            return CommandResult.Fail(
                "port_name/baud/data_bits/parity/stop_bits only apply with protocol=Serial");
        }

        // model and codepage: only on a TN3270 connection, and a model must not contradict the
        // emulator given in the same command. The model is the terminal type the window's size
        // box writes (IBM-3278-2 to IBM-3278-5).
        var problem3270 = Ibm3270ConnectionSettings.Check(effectiveProtocol, args.GetString("emulator"),
            args.GetString(Ibm3270ConnectionSettings.ModelArgument), args.GetString(Ibm3270ConnectionSettings.CodePageArgument),
            out var terminal3270, out var codePage);
        if (problem3270 != null)
        {
            return CommandResult.Fail(problem3270);
        }
        bool modelGiven = args.Contains(Ibm3270ConnectionSettings.ModelArgument);

        // The terminal the saved connection ends up with: the model or emulator given now, else
        // the one already stored. A TN3270 one is what the protocol allows (EmulatorTypeFor),
        // which is what decides whether the printer settings apply.
        string? effectiveTerminal = modelGiven ? terminal3270 : args.GetString("emulator") ?? existing?.EmulatorType;
        if (effectiveProtocol == ConnectionFactory.ProtocolType.TN3270)
        {
            effectiveTerminal = ConnectionFactory.EmulatorTypeFor(effectiveProtocol, effectiveTerminal);
        }
        var problemChoices = Ibm3270ConnectionSettings.ReadChoices(args, effectiveProtocol, effectiveTerminal, out var choices);
        if (problemChoices != null)
        {
            return CommandResult.Fail(problemChoices);
        }

        // screen_size: by the emulator's own limits, for the model the saved connection ends up with.
        var problemScreen = Ibm3270ConnectionSettings.CheckScreenSize(effectiveProtocol, effectiveTerminal,
            args.GetString(Ibm3270ConnectionSettings.ScreenSizeArgument), out var screen);
        if (problemScreen != null)
        {
            return CommandResult.Fail(problemScreen);
        }
        bool screenGiven = args.Contains(Ibm3270ConnectionSettings.ScreenSizeArgument);

        if (existing == null)
        {
            // The terminal's own screen unless the caller gives a size. A TDV saved from a script
            // used to get 24 rows, the literal here, and lose its 25th line. A TN3270 connection
            // gets the terminal the protocol allows (ConnectionFactory.EmulatorTypeFor, the rule
            // the window and the connect paths use), so the stored type is the model it opens.
            var newEmulator = modelGiven ? terminal3270! : args.GetString("emulator", "VT100")!;
            if (effectiveProtocol == ConnectionFactory.ProtocolType.TN3270)
            {
                newEmulator = ConnectionFactory.EmulatorTypeFor(effectiveProtocol, newEmulator);
            }
            var (newWidth, newHeight) = EmulatorFactory.GetRecommendedSize(newEmulator);
            var config = new HostConfiguration
            {
                Name = name,
                Protocol = args.GetString("protocol", "Telnet")!,
                EmulatorType = newEmulator,
                Username = args.GetString("username"),
                Width = args.GetInt("width", newWidth),
                Height = args.GetInt("height", newHeight),
                IsFavorite = args.GetBool("favorite", false),
                OnConnectScript = NullIfEmpty(args.GetString("onconnect")),
                OnDisconnectScript = NullIfEmpty(args.GetString("ondisconnect")),
                CodePage = codePage,
                Ibm3270ScreenSize = screen
            };
            choices.ApplyTo(config);

            if (isSerial)
            {
                var portName = args.GetString("port_name");
                if (portName == null)
                {
                    return CommandResult.Fail($"creating serial '{name}' needs port_name=... (e.g. port_name=COM11)");
                }
                int parityValue = 0;
                int stopBitsValue = 1;
                var problem = ConnectionFactory.ValidateSerialFields(
                    args.GetInt("baud", 9600), args.GetInt("data_bits", 8),
                    args.GetString("parity"), args.GetString("stop_bits"),
                    ref parityValue, ref stopBitsValue);
                if (problem != null)
                {
                    return CommandResult.Fail(problem);
                }
                config.PortName = portName;
                config.BaudRate = args.GetInt("baud", 9600);
                config.DataBits = args.GetInt("data_bits", 8);
                config.ParityValue = parityValue;
                config.StopBitsValue = stopBitsValue;
            }
            else
            {
                var host = args.GetString("host");
                if (host == null)
                {
                    return CommandResult.Fail($"creating '{name}' needs at least host=...");
                }
                config.Host = host;
                config.Port = args.GetInt("port", 23);
            }

            await _manager.AddAsync(config).ConfigureAwait(false);
            return CommandResult.Ok($"created '{name}' → {config.ToConnectionParameters().DisplayName}");
        }

        // Update: only the values that were actually given change.
        if (args.Contains("host")) existing.Host = args.GetString("host")!;
        if (args.Contains("port")) existing.Port = args.GetInt("port", existing.Port);
        if (args.Contains("protocol")) existing.Protocol = args.GetString("protocol", existing.Protocol)!;
        if (args.Contains("emulator"))
        {
            existing.EmulatorType = args.GetString("emulator", existing.EmulatorType)!;
            // Changing the terminal changes its screen: the new type's own size, unless width or
            // height are given in the same command, which then win below.
            (existing.Width, existing.Height) = EmulatorFactory.GetRecommendedSize(existing.EmulatorType);
        }
        if (modelGiven)
        {
            // The model is the terminal and fixes the screen, as the window's size box does.
            existing.EmulatorType = terminal3270!;
            (existing.Width, existing.Height) = EmulatorFactory.GetRecommendedSize(existing.EmulatorType);
        }
        if (codePage != null) existing.CodePage = codePage;

        // A larger screen belongs to the model it was given for: a new model or terminal without
        // a screen_size goes back to its own screen, as the window's size box does. Kept, a
        // 100x50 saved for a model 2 would be one a model 5 (132 columns) cannot have.
        if (screenGiven)
        {
            existing.Ibm3270ScreenSize = screen;
        }
        else if (modelGiven || args.Contains("emulator"))
        {
            existing.Ibm3270ScreenSize = null;
        }
        choices.ApplyTo(existing);
        if (args.Contains("username")) existing.Username = args.GetString("username");
        if (args.Contains("width")) existing.Width = args.GetInt("width", existing.Width);
        if (args.Contains("height")) existing.Height = args.GetInt("height", existing.Height);
        if (args.Contains("favorite")) existing.IsFavorite = args.GetBool("favorite", existing.IsFavorite);
        if (args.Contains("onconnect")) existing.OnConnectScript = NullIfEmpty(args.GetString("onconnect"));
        if (args.Contains("ondisconnect")) existing.OnDisconnectScript = NullIfEmpty(args.GetString("ondisconnect"));

        if (args.Contains("port_name") || args.Contains("baud") || args.Contains("data_bits")
            || args.Contains("parity") || args.Contains("stop_bits"))
        {
            int parityValue = existing.ParityValue;
            int stopBitsValue = existing.StopBitsValue;
            var problem = ConnectionFactory.ValidateSerialFields(
                args.GetInt("baud", existing.BaudRate), args.GetInt("data_bits", existing.DataBits),
                args.GetString("parity"), args.GetString("stop_bits"),
                ref parityValue, ref stopBitsValue);
            if (problem != null)
            {
                return CommandResult.Fail(problem);
            }
            if (args.Contains("port_name")) existing.PortName = args.GetString("port_name");
            if (args.Contains("baud")) existing.BaudRate = args.GetInt("baud", existing.BaudRate);
            if (args.Contains("data_bits")) existing.DataBits = args.GetInt("data_bits", existing.DataBits);
            existing.ParityValue = parityValue;
            existing.StopBitsValue = stopBitsValue;
        }

        await _manager.UpdateAsync(existing).ConfigureAwait(false);
        return CommandResult.Ok($"updated '{existing.Name}' → {existing.ToConnectionParameters().DisplayName}");
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// CONNDEL — delete a stored connection.
/// </summary>
public sealed class ConnDelCommand : ISessionCommand
{
    private readonly ConfigurationManager _manager;

    public ConnDelCommand(ConfigurationManager manager) => _manager = manager;

    public string Name => "CONNDEL";
    public string Summary => "Delete a stored connection by name";
    public string Example => "CONNDEL \"old-lab\"";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("name", CommandParameterType.String, required: true, defaultValue: null,
            "Stored connection name to delete")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var name = args.GetString("name")!;
        var config = ConnectionCommands.FindByName(_manager, name);
        if (config == null)
        {
            return CommandResult.Fail($"no stored connection named '{name}'");
        }
        await _manager.RemoveAsync(config.Id).ConfigureAwait(false);
        return CommandResult.Ok($"deleted '{config.Name}'");
    }
}
