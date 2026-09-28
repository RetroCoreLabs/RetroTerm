using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;

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

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("name", CommandParameterType.String, required: false, defaultValue: null,
            "Stored connection name (CONNLIST shows them); when given, other parameters are ignored"),
        new CommandParameter("host", CommandParameterType.String, required: false, defaultValue: null,
            "Host name or IP for an ad-hoc telnet/ssh connection (serial uses port_name instead)"),
        new CommandParameter("port", CommandParameterType.Int, required: false, defaultValue: "23",
            "TCP port for an ad-hoc telnet/ssh connection"),
        new CommandParameter("protocol", CommandParameterType.String, required: false, defaultValue: "telnet",
            "telnet, ssh or serial"),
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
            "Serial stop bits: 1, 1.5 or 2")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (session.IsConnected)
        {
            return CommandResult.Fail("already connected — DISCONNECT first");
        }

        ConnectionFactory.ConnectionParameters parameters;
        var name = args.GetString("name");
        if (name != null)
        {
            var config = ConnectionCommands.FindByName(_manager, name);
            if (config == null)
            {
                return CommandResult.Fail($"no stored connection named '{name}' — CONNLIST shows them");
            }
            parameters = config.ToConnectionParameters();
            config.MarkAsUsed();
            await _manager.SaveAsync().ConfigureAwait(false);
        }
        else
        {
            var protocol = ConnectionFactory.ParseProtocolType(args.GetString("protocol", "telnet")!);
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
                    EmulatorType = session.Emulator.GetType().Name
                };
            }
        }

        // A stored serial connection saved without a port name would otherwise die inside the
        // factory with its internal parameter name — catch it here with the field to fix.
        if (parameters.Protocol == ConnectionFactory.ProtocolType.Serial
            && string.IsNullOrWhiteSpace(parameters.PortName))
        {
            return CommandResult.Fail(
                $"stored connection '{name}' has no serial port name — CONNSAVE it with port_name=COM...");
        }

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

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("name", CommandParameterType.String, required: true, defaultValue: null,
            "Connection name (unique, case-insensitive)"),
        new CommandParameter("host", CommandParameterType.String, required: false, defaultValue: null,
            "Host name or IP (required when creating a telnet/ssh connection; Serial uses port_name)"),
        new CommandParameter("port", CommandParameterType.Int, required: false, defaultValue: "23",
            "TCP port (telnet/ssh only)"),
        new CommandParameter("protocol", CommandParameterType.String, required: false, defaultValue: "Telnet",
            "Telnet, SSH or Serial"),
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
            "Script (library name) to run when the connection DROPS (not on deliberate disconnect); empty string clears it")
    };

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

        if (existing == null)
        {
            // The terminal's own screen unless the caller gives a size. A TDV saved from a script
            // used to get 24 rows, the literal here, and lose its 25th line.
            var newEmulator = args.GetString("emulator", "VT100")!;
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
                OnDisconnectScript = NullIfEmpty(args.GetString("ondisconnect"))
            };

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
