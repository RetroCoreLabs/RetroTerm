using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Read-only access to the ND-100 gateway state for scripts, the console and MCP —
/// everything the gateway UI shows: listening status, port, whether the emulator and
/// disk worker are connected, and the registered terminals with their ident codes and
/// in-use flags.
///
/// This command lives in the WebSocket project (which owns <see cref="GatewayListener"/>);
/// the desktop registers it with a provider that returns the app's listener, so Core
/// stays free of a gateway dependency.
/// </summary>
public sealed class GatewayCommand : ISessionCommand
{
    private readonly Func<GatewayListener?> _listenerProvider;

    public GatewayCommand(Func<GatewayListener?> listenerProvider)
    {
        _listenerProvider = listenerProvider ?? throw new ArgumentNullException(nameof(listenerProvider));
    }

    public string Name => "GATEWAY";
    public string Summary => "Report ND-100 gateway state: listening, port, emulator/disk connected, and the registered terminals";
    public string Example => "GATEWAY";
    public bool ProducesOutput => true;
    public bool ProducesCapture => true; // into=var stores the terminal count

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("action", CommandParameterType.String, required: false, defaultValue: "status",
            "status (listener + emulator + disk) or terminals (the registered terminal list only)")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var listener = _listenerProvider();
        if (listener == null)
        {
            return Task.FromResult(CommandResult.Fail(
                "gateway listener is not available — enable it in Connection → Gateway Settings"));
        }

        var action = (args.GetString("action", "status") ?? "status").Trim().ToLowerInvariant();
        var terminals = listener.GetTerminals();

        var sb = new StringBuilder(256);
        if (action != "terminals")
        {
            sb.Append("listening: ").Append(listener.IsListening ? "yes" : "no").Append('\n');
            sb.Append("port: ").Append(listener.Port).Append('\n');
            sb.Append("emulator connected: ").Append(listener.IsEmulatorConnected ? "yes" : "no").Append('\n');
            sb.Append("disk worker connected: ").Append(listener.IsDiskWorkerConnected ? "yes" : "no").Append('\n');
            sb.Append("terminals: ").Append(terminals.Count).Append('\n');
        }
        else if (terminals.Count == 0)
        {
            sb.Append("no terminals registered\n");
        }

        for (int i = 0; i < terminals.Count; i++)
        {
            var t = terminals[i];
            bool inUse = listener.IsIdentCodeInUse(t.IdentCode);
            sb.Append("  ident ").Append(t.IdentCode)
              .Append(" — ").Append(string.IsNullOrEmpty(t.Name) ? "(unnamed)" : t.Name)
              .Append(" — logical device ").Append(t.LogicalDevice)
              .Append(inUse ? " [in use]" : " [free]")
              .Append('\n');
        }

        var outcome = CommandResult.Ok(sb.ToString().TrimEnd('\n'));
        outcome.CaptureValue = terminals.Count.ToString();
        return Task.FromResult(outcome);
    }

    /// <summary>
    /// Registers GATEWAY with the given listener provider.
    /// </summary>
    public static void RegisterAll(CommandRegistry registry, Func<GatewayListener?> listenerProvider)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new GatewayCommand(listenerProvider));
    }
}
