using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Keyboard configuration for scripts, the console and MCP — read and edit the TDV
/// key bindings (which PC key combo maps to which TDV keyboard grid position), the
/// same store the Virtual Keyboard binding UI uses. Persists to
/// %AppData%\RetroTerm\tdv-key-bindings.json.
///
/// One verb with action=: list, bind, unbind, defaults. A key combination is given
/// as a Windows virtual-key code plus modifiers, because that is exactly what a
/// binding stores (a key NAME is ambiguous across layouts): vk=72 mods=alt = Alt+H.
/// The target is a TDV grid position such as G53 (HJELP).
/// </summary>
public sealed class KeyboardCommand : ISessionCommand
{
    public string Name => "KEYBIND";
    public string Summary => "Configure TDV keyboard bindings: list, bind (vk+mods to a grid position), unbind, defaults";
    public string Example => "KEYBIND action=bind vk=72 mods=alt grid=G53";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("action", CommandParameterType.String, required: true, defaultValue: null,
            "list, bind, unbind, or defaults"),
        new CommandParameter("vk", CommandParameterType.Int, required: false, defaultValue: null,
            "Windows virtual-key code of the source key (e.g. 72=H, 112=F1, 9=Tab). Required for bind/unbind"),
        new CommandParameter("mods", CommandParameterType.String, required: false, defaultValue: "",
            "Modifiers, comma or plus separated: shift, ctrl, alt, meta (empty = none). For bind/unbind"),
        new CommandParameter("grid", CommandParameterType.String, required: false, defaultValue: null,
            "Target TDV grid position, e.g. G53 (HJELP), F51 (F1). Required for bind"),
        new CommandParameter("shifted", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Send the shifted variant of the target key (bind only)"),
        new CommandParameter("save", CommandParameterType.Bool, required: false, defaultValue: "true",
            "Persist the change to disk (bind/unbind/defaults)")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var config = TDVKeyBindingConfiguration.Instance;
        var action = (args.GetString("action") ?? string.Empty).Trim().ToLowerInvariant();

        switch (action)
        {
            case "list":
                return Task.FromResult(ListBindings(config));

            case "bind":
                return Task.FromResult(Bind(config, args));

            case "unbind":
                return Task.FromResult(Unbind(config, args));

            case "defaults":
                config.SetDefaults();
                if (args.GetBool("save", true)) config.Save();
                return Task.FromResult(CommandResult.Ok($"keyboard reset to defaults — {config.Count} binding(s)"));

            default:
                return Task.FromResult(CommandResult.Fail($"KEYBIND: unknown action '{action}' — use list, bind, unbind or defaults"));
        }
    }

    private static CommandResult ListBindings(TDVKeyBindingConfiguration config)
    {
        var all = config.GetAllBindings();
        if (all.Count == 0)
        {
            return CommandResult.Ok("no key bindings");
        }
        var sb = new StringBuilder(all.Count * 32 + 16);
        sb.Append(all.Count).Append(" binding(s):\n");
        for (int i = 0; i < all.Count; i++)
        {
            var src = all[i].Key;
            var tgt = all[i].Value;
            sb.Append(TDVKeyBindingConfiguration.GetBindingLabel(src))
              .Append("  (vk=").Append(src.VKCode).Append(" mods=").Append(ModsToString(src.Modifiers)).Append(")")
              .Append("  ->  ").Append(tgt.GridPosition)
              .Append(tgt.Shifted ? " (shifted)" : "")
              .Append('\n');
        }
        return CommandResult.Ok(sb.ToString().TrimEnd('\n'));
    }

    private static CommandResult Bind(TDVKeyBindingConfiguration config, CommandArgs args)
    {
        if (!args.Contains("vk"))
        {
            return CommandResult.Fail("KEYBIND bind: 'vk=' (virtual-key code) is required");
        }
        var grid = args.GetString("grid");
        if (string.IsNullOrWhiteSpace(grid))
        {
            return CommandResult.Fail("KEYBIND bind: 'grid=' (target grid position, e.g. G53) is required");
        }

        int vk = args.GetInt("vk", 0);
        if (!TryParseMods(args.GetString("mods", "")!, out var mods, out var modErr))
        {
            return CommandResult.Fail("KEYBIND bind: " + modErr);
        }

        var source = new KeyBindingSource(vk, mods);
        if (!source.IsValid())
        {
            return CommandResult.Fail(
                $"KEYBIND bind: vk={vk} mods={ModsToString(mods)} is not a valid binding source " +
                "(a bare text key with no modifier, or a modifier key alone, cannot be a binding)");
        }

        var target = new KeyBindingTarget(grid!, args.GetBool("shifted", false));
        if (!config.SetBinding(source, target))
        {
            return CommandResult.Fail("KEYBIND bind: the binding was rejected (invalid source or empty grid)");
        }
        if (args.GetBool("save", true)) config.Save();
        return CommandResult.Ok($"bound {TDVKeyBindingConfiguration.GetBindingLabel(source)} -> {grid}{(target.Shifted ? " (shifted)" : "")}");
    }

    private static CommandResult Unbind(TDVKeyBindingConfiguration config, CommandArgs args)
    {
        if (!args.Contains("vk"))
        {
            return CommandResult.Fail("KEYBIND unbind: 'vk=' (virtual-key code) is required");
        }
        int vk = args.GetInt("vk", 0);
        if (!TryParseMods(args.GetString("mods", "")!, out var mods, out var modErr))
        {
            return CommandResult.Fail("KEYBIND unbind: " + modErr);
        }

        var source = new KeyBindingSource(vk, mods);
        bool removed = config.RemoveBinding(source);
        if (!removed)
        {
            return CommandResult.Fail($"KEYBIND unbind: nothing bound to {TDVKeyBindingConfiguration.GetBindingLabel(source)}");
        }
        if (args.GetBool("save", true)) config.Save();
        return CommandResult.Ok($"unbound {TDVKeyBindingConfiguration.GetBindingLabel(source)}");
    }

    /// <summary>
    /// Parses "alt,ctrl" / "alt+ctrl" / "" into KeyModifiers. Unknown token = error.
    /// </summary>
    private static bool TryParseMods(string text, out KeyModifiers mods, out string error)
    {
        mods = KeyModifiers.None;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        var parts = text.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "shift": mods |= KeyModifiers.Shift; break;
                case "ctrl": case "control": mods |= KeyModifiers.Ctrl; break;
                case "alt": mods |= KeyModifiers.Alt; break;
                case "meta": case "win": case "cmd": mods |= KeyModifiers.Meta; break;
                default:
                    error = $"unknown modifier '{parts[i]}' — use shift, ctrl, alt, meta";
                    return false;
            }
        }
        return true;
    }

    private static string ModsToString(KeyModifiers mods)
    {
        if (mods == KeyModifiers.None) return "none";
        var sb = new StringBuilder(16);
        if ((mods & KeyModifiers.Ctrl) != 0) sb.Append("ctrl+");
        if ((mods & KeyModifiers.Alt) != 0) sb.Append("alt+");
        if ((mods & KeyModifiers.Shift) != 0) sb.Append("shift+");
        if ((mods & KeyModifiers.Meta) != 0) sb.Append("meta+");
        return sb.ToString().TrimEnd('+');
    }
}
