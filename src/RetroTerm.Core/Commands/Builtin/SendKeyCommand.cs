using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// SENDKEY — send a named TDV function key (HJELP, ANGRE, F1..F8, SLUTT, ...) without
/// the script having to know the escape sequence. The name resolves through
/// TDV2200KeyRegistry (the single source of truth the real keyboard uses) and the
/// sequence is mode-aware: extended control mode vs 2115-compatibility simple ASCII.
/// </summary>
public sealed class SendKeyCommand : ISessionCommand
{
    public string Name => "SENDKEY";
    public string Summary => "Send a named TDV function key (e.g. HJELP, ANGRE, F7) — resolves the right escape sequence for the current mode";
    public string Example => "SENDKEY HJELP";

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("key", CommandParameterType.String, required: true, defaultValue: null,
            "Key legend printed on the TDV2200 keyboard (HJELP, ANGRE, F1..F8, ...) or a grid " +
            "position like G53 or F51. A legend and a grid position can name the SAME key: F1 and " +
            "F51 are one key, because F51 is where the key labelled F1 sits."),
        new CommandParameter("shift", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Send the shifted variant when the key has one")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (session.Emulator is not TDVEmulatorBase)
        {
            return CommandResult.Fail("SENDKEY needs a TDV emulator (current: "
                + session.Emulator.GetType().Name + ") — use SENDRAW for other terminals");
        }

        var keySpec = args.GetString("key")!;

        // Name → grid position; a grid position itself (G53) also works.
        var grid = TDV2200KeyRegistry.GetGridForName(keySpec);
        if (grid == null && TDV2200KeyRegistry.TryGetKey(keySpec, out _))
        {
            grid = keySpec;
        }
        if (grid == null)
        {
            return CommandResult.Fail($"unknown TDV key '{keySpec}' — key names are the keyboard legends (HJELP, ANGRE, F1..) or grid positions (G53)");
        }

        // Extended control mode unless the emulator runs in 2115 compatibility,
        // which uses the simple ASCII (C0) codes.
        bool extendedMode = session.Emulator switch
        {
            TDV1200Emulator e1200 => !e1200.Is2115CompatibilityMode,
            TDV2215Emulator e2215 => !e2215.Is2115CompatibilityMode,
            TDV2200Emulator e2200 => !e2200.Is2115CompatibilityMode,
            _ => true
        };

        var sequence = TDV2200KeyRegistry.GetSequence(grid, extendedMode,
            numPadFuncMode: false, shift: args.GetBool("shift", false));
        if (sequence == null)
        {
            return CommandResult.Fail($"key '{keySpec}' has no sequence in the current mode "
                + $"({(extendedMode ? "extended" : "2115 simple ASCII")}) — programmable PUSH keys have no fixed sequence");
        }

        await session.SendInputAsync(sequence, cancellationToken).ConfigureAwait(false);

        // Show what actually went out, control bytes escaped.
        var sb = new StringBuilder(64);
        sb.Append("sent ").Append(keySpec).Append(" (");
        for (int i = 0; i < sequence.Length; i++)
        {
            char c = sequence[i];
            if (c < 0x20)
            {
                sb.Append(c == '\x1B' ? "ESC" : $"<{(int)c:X2}>");
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append(')');
        return CommandResult.Ok(sb.ToString());
    }
}
