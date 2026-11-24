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
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;

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
    public string Summary => "Press a named terminal key: TDV.HJELP, IBM.PF3, IBM.ENTER, VT.PF1, VT.UP (a bare name like HJELP uses the tab's own terminal) — the family must match the tab";
    public string Example => "SENDKEY HJELP";

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("key", CommandParameterType.String, required: true, defaultValue: null,
            "Key name, optionally with its family: TDV.HJELP, IBM.PF3, IBM.CLEAR, VT.PF1, VT.FIND. Without a family it is looked up in the tab's own terminal. TDV keys are the legends printed on the TDV2200 keyboard (HJELP, ANGRE, F1..F8, ...) or a grid " +
            "position like G53 or F51. A legend and a grid position can name the SAME key: F1 and " +
            "F51 are one key, because F51 is where the key labelled F1 sits."),
        new CommandParameter("shift", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Send the shifted variant when the key has one")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        // A key's full name is FAMILY.KEY (TDV.HJELP, IBM.PF3, VT.PF1). The family is checked
        // against the tab's terminal, so a script written for one terminal cannot press a
        // same-named key on another. A bare name is looked up in the tab's own terminal, which
        // keeps every existing SENDKEY HJELP working. Ronny's choice, 22 November 2026.
        string family = FamilyOf(session.Emulator);
        var keySpec = args.GetString("key")!;
        int dot = keySpec.IndexOf('.');
        if (dot > 0)
        {
            string wanted = keySpec.Substring(0, dot).ToUpperInvariant();
            if (wanted != "TDV" && wanted != "IBM" && wanted != "VT")
            {
                return CommandResult.Fail("unknown key family '" + keySpec.Substring(0, dot)
                    + "' — the families are TDV, IBM and VT (for example IBM.PF3)");
            }

            if (wanted != family)
            {
                return CommandResult.Fail("'" + keySpec + "' is a " + wanted + " key, but this tab is a "
                    + family + " terminal (" + session.Emulator.GetType().Name + ") — nothing was sent");
            }

            keySpec = keySpec.Substring(dot + 1);
        }

        if (session.Emulator is INamedKeyTerminal named)
        {
            switch (named.PressNamedKey(keySpec, out string? reason))
            {
                case NamedKeyResult.Pressed:
                    // The key ran on the session thread; wait so a WAITFOR after it sees the result.
                    await session.FlushAsync().ConfigureAwait(false);
                    return CommandResult.Ok("pressed " + family + "." + keySpec.ToUpperInvariant());
                case NamedKeyResult.NotBuilt:
                    return CommandResult.Fail(family + "." + keySpec.ToUpperInvariant()
                        + " is a real key that RetroTerm does not do yet (" + reason + ") — nothing was sent");
                default:
                    return CommandResult.Fail("unknown " + family + " key '" + keySpec + "' — the keys are: "
                        + string.Join(" ", named.KeyNames));
            }
        }

        if (session.Emulator is not TDVEmulatorBase)
        {
            return await PressVtKeyAsync(session, keySpec, args.GetBool("shift", false), cancellationToken).ConfigureAwait(false);
        }

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

    /// <summary>
    /// A VT key by name (VT.PF1, VT.UP, VT.FIND, ...). The name only picks the key; the bytes come
    /// from VtNamedKeys.Encode, the same path a real key press in the terminal window takes, with
    /// the emulator's live modes (DECCKM, DECKPAM/DECKPNM, 7- or 8-bit controls) and any DECUDK
    /// definition the host loaded.
    /// </summary>
    private static async Task<CommandResult> PressVtKeyAsync(TerminalSession session, string keySpec, bool shift, CancellationToken cancellationToken)
    {
        if (!VtNamedKeys.TryGetKeyCode(keySpec, out int keyCode))
        {
            return CommandResult.Fail("unknown VT key '" + keySpec + "' — the keys are: "
                + string.Join(" ", VtNamedKeys.Names));
        }

        // The mapper the profile types on, built the same way TerminalCanvas builds its own.
        IKeyboardMapper mapper = KeyboardMapperFactory.CreateMapper(session.Emulator.Profile);

        // The modes are read on the session thread, where the emulator lives (SessionPump rule).
        KeyModifiers modifiers = shift ? KeyModifiers.Shift : KeyModifiers.None;
        string? text = await session.RunOnSessionThreadAsync(
            () => VtNamedKeys.Encode(session.Emulator, mapper, keyCode, modifiers)).ConfigureAwait(false);

        if (text == null)
        {
            return CommandResult.Fail("VT." + keySpec.ToUpperInvariant() + " sends nothing on this terminal in its current mode — nothing was sent");
        }

        await session.SendInputAsync(text, cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder(64);
        sb.Append("sent VT.").Append(keySpec.ToUpperInvariant()).Append(" (");
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\x1B')
            {
                sb.Append("ESC");
            }
            else if (c < 0x20 || (c >= 0x80 && c <= 0x9F))
            {
                sb.Append('<').Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)).Append('>');
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append(')');
        return CommandResult.Ok(sb.ToString());
    }

    /// <summary>
    /// The key family of a terminal: TDV for the Tandberg terminals, the terminal's own family
    /// when it names its keys (IBM for the 3270), and VT for every other stream terminal.
    /// </summary>
    /// <param name="emulator">
    /// The tab's terminal.
    /// </param>
    /// <returns>
    /// The family, upper case, without the dot.
    /// </returns>
    public static string FamilyOf(TerminalEmulatorBase emulator)
    {
        if (emulator is TDVEmulatorBase)
        {
            return "TDV";
        }

        if (emulator is INamedKeyTerminal named)
        {
            return named.KeyFamily;
        }

        return "VT";
    }
}
