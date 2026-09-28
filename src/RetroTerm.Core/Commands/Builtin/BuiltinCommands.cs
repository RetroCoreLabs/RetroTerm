using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Registers the built-in command set — thin wrappers over the phase-1 session
/// primitives. Each command is self-contained and self-documenting; the DSL parser,
/// MCP tools and generated help all pick these up through the registry.
/// </summary>
public static class BuiltinCommands
{
    public static void RegisterAll(CommandRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new SendCommand());
        registry.Register(new SendRawCommand());
        registry.Register(new WaitForCommand());
        registry.Register(new WaitIdleCommand());
        registry.Register(new ReadScreenCommand());
        registry.Register(new ReadNewCommand());
        registry.Register(new StatusCommand());
        registry.Register(new UnhandledCommand());
        registry.Register(new SleepCommand());
        registry.Register(new SendKeyCommand());
        TerminalControlCommands.RegisterAll(registry);
        TraceCommands.RegisterAll(registry);
        registry.Register(new OpcomDebugCommand());
        registry.Register(new KeyboardCommand());
        // HELP is an ordinary command holding a reference to its own registry —
        // no special cases in the parser or the runner.
        registry.Register(new HelpCommand(registry));
    }
}

/// <summary>
/// HELP — generated from the live registry; never hand-written.
/// </summary>
public sealed class HelpCommand : ISessionCommand
{
    private readonly CommandRegistry _registry;

    public HelpCommand(CommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string Name => "HELP";
    public string Summary => "List all commands, or show full help for one command";
    public string Example => "HELP WAITFOR";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("command", CommandParameterType.String, required: false, defaultValue: null,
            "Command name to show detailed help for; omit to list all commands")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var name = args.GetString("command");
        var text = name == null
            ? CommandHelpGenerator.GenerateOverview(_registry)
            : CommandHelpGenerator.GenerateFor(_registry, name);
        return Task.FromResult(CommandResult.Ok(text));
    }
}

/// <summary>
/// SEND — send EXACTLY the given text, nothing appended. A line terminator is written
/// explicitly with an escape: SEND "LIST-FILES\r". Escapes in quoted strings
/// (\r \n \t \e \xHH \NNN octal) are decoded by the script parser, so what reaches
/// this command is already the raw text.
/// </summary>
public sealed class SendCommand : ISessionCommand
{
    public string Name => "SEND";
    public string Summary => "Send exactly the given text — nothing is appended; end with \\r yourself for a carriage return";
    public string Example => "SEND \"LIST-FILES\\r\"";

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("text", CommandParameterType.String, required: true, defaultValue: null,
            "The text to send, verbatim. Escapes: \\r \\n \\t \\e (ESC) \\xHH (hex) \\NNN (octal)",
            decodeEscapes: true)
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var text = args.GetString("text")!;
        await session.SendInputAsync(text, cancellationToken).ConfigureAwait(false);
        return CommandResult.Ok();
    }
}

/// <summary>
/// SENDRAW — send bare control bytes; ESC is how you wake a SINTRAN line.
/// </summary>
public sealed class SendRawCommand : ISessionCommand
{
    public string Name => "SENDRAW";
    public string Summary => "Send raw bytes given as hex pairs (e.g. 1B0D) or the keyword ESC";
    public string Example => "SENDRAW ESC";

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("bytes", CommandParameterType.String, required: true, defaultValue: null,
            "Hex byte pairs without separators (1B0D), or ESC for a single escape byte")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var spec = args.GetString("bytes")!;

        byte[] bytes;
        if (string.Equals(spec, "ESC", StringComparison.OrdinalIgnoreCase))
        {
            bytes = new byte[] { 0x1B };
        }
        else
        {
            if (spec.Length == 0 || (spec.Length & 1) != 0)
            {
                return CommandResult.Fail($"'{spec}' is not valid hex — need an even number of hex digits, e.g. 1B0D");
            }
            bytes = new byte[spec.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(spec.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                {
                    return CommandResult.Fail($"'{spec.Substring(i * 2, 2)}' at position {i * 2} is not a hex byte");
                }
            }
        }

        await session.SendBytesAsync(bytes, cancellationToken).ConfigureAwait(false);
        return CommandResult.Ok();
    }
}

/// <summary>
/// WAITFOR — the important one: wait for text on the RENDERED screen.
/// </summary>
public sealed class WaitForCommand : ISessionCommand
{
    public string Name => "WAITFOR";
    public string Summary => "Wait until a pattern appears on the rendered screen (tail = prompt matching)";
    public string Example => "WAITFOR \"X-C:\" timeout=30000   # timeout is in MILLISECONDS: 30000 = 30 s";
    public bool CanTimeOut => true;       // enables ontimeout=label
    public bool ProducesCapture => true;  // into=var stores the match (regex group 1)

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("pattern", CommandParameterType.String, required: true, defaultValue: null,
            "The text (or regex with regex=true) to wait for"),
        new CommandParameter("regex", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Interpret pattern as a .NET regular expression"),
        new CommandParameter("where", CommandParameterType.String, required: false, defaultValue: "tail",
            "Where to look: tail (last non-blank rows), screen (whole screen), scrollback (screen then scrollback)"),
        new CommandParameter("timeout", CommandParameterType.Int, required: false, defaultValue: "30000",
            "Give up after this many milliseconds — the result still returns the screen"),
        new CommandParameter("tailrows", CommandParameterType.Int, required: false, defaultValue: "1",
            "How many trailing non-blank rows form the tail for where=tail")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var whereText = args.GetString("where", "tail")!;
        ScreenMatchWhere where;
        if (string.Equals(whereText, "tail", StringComparison.OrdinalIgnoreCase))
        {
            where = ScreenMatchWhere.ScreenTail;
        }
        else if (string.Equals(whereText, "screen", StringComparison.OrdinalIgnoreCase))
        {
            where = ScreenMatchWhere.AnywhereOnScreen;
        }
        else if (string.Equals(whereText, "scrollback", StringComparison.OrdinalIgnoreCase))
        {
            where = ScreenMatchWhere.ScreenAndScrollback;
        }
        else
        {
            return CommandResult.Fail($"where='{whereText}' is not valid — use tail, screen or scrollback");
        }

        var options = new ScreenWaitOptions
        {
            Pattern = args.GetString("pattern")!,
            MatchType = args.GetBool("regex", false) ? ScreenMatchType.Regex : ScreenMatchType.Literal,
            Where = where,
            TailRows = args.GetInt("tailrows", 1),
            TimeoutMs = args.GetInt("timeout", 30_000)
        };

        var result = await session.WaitForScreenAsync(options, cancellationToken).ConfigureAwait(false);

        CommandResult outcome;
        if (result.Matched)
        {
            outcome = CommandResult.Ok(result.Screen.Text);
            // into=var stores WHAT matched (regex group 1 when present), not the screen.
            outcome.CaptureValue = result.MatchedText;
        }
        else if (result.Disconnected)
        {
            // Rule 6: say what happened, and still show the screen.
            outcome = CommandResult.Fail("Connection lost while waiting", result.Screen.Text);
            outcome.ConnectionLost = true; // the runner reports the drop even if its own event handler races
        }
        else
        {
            // Rule 4: a timeout still returns what the machine said.
            outcome = CommandResult.Fail(
                $"Timeout after {(int)result.Elapsed.TotalMilliseconds} ms waiting for '{options.Pattern}'",
                result.Screen.Text);
            outcome.TimedOut = true; // lets scripts branch with ontimeout=label
        }
        outcome.Elapsed = result.Elapsed;
        return outcome;
    }
}

/// <summary>
/// WAITIDLE — wait until the screen stops changing.
/// </summary>
public sealed class WaitIdleCommand : ISessionCommand
{
    public string Name => "WAITIDLE";
    public string Summary => "Wait until the screen has not changed for the given quiet time";
    public string Example => "WAITIDLE 500   # milliseconds: screen quiet for 0.5 s";
    public bool CanTimeOut => true;       // enables ontimeout=label
    public bool ProducesCapture => true;  // into=var stores the settled screen text

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("ms", CommandParameterType.Int, required: true, defaultValue: null,
            "Quiet time in milliseconds — the screen must stay unchanged this long"),
        new CommandParameter("timeout", CommandParameterType.Int, required: false, defaultValue: "30000",
            "Give up after this many milliseconds")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var options = new ScreenWaitOptions
        {
            Pattern = null,
            IdleMs = args.GetInt("ms", 500),
            TimeoutMs = args.GetInt("timeout", 30_000)
        };

        var result = await session.WaitForScreenAsync(options, cancellationToken).ConfigureAwait(false);

        CommandResult outcome;
        if (result.Matched)
        {
            outcome = CommandResult.Ok(result.Screen.Text);
        }
        else if (result.Disconnected)
        {
            outcome = CommandResult.Fail("Connection lost while waiting", result.Screen.Text);
            outcome.ConnectionLost = true; // the runner reports the drop even if its own event handler races
        }
        else
        {
            outcome = CommandResult.Fail(
                $"Screen never went quiet within {(int)result.Elapsed.TotalMilliseconds} ms",
                result.Screen.Text);
            outcome.TimedOut = true; // lets scripts branch with ontimeout=label
        }
        outcome.Elapsed = result.Elapsed;
        return outcome;
    }
}

/// <summary>
/// READSCREEN — the rendered screen as plain text.
/// </summary>
public sealed class ReadScreenCommand : ISessionCommand
{
    public string Name => "READSCREEN";
    public string Summary => "Read the rendered screen as plain text, with cursor position";
    public string Example => "READSCREEN";
    public bool ProducesOutput => true; // in a script: a checkpoint printed into the transcript

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("strip", CommandParameterType.Bool, required: false, defaultValue: "true",
            "Strip trailing blanks from rows and drop trailing blank rows")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var screen = await session.ReadScreenAsync(args.GetBool("strip", true)).ConfigureAwait(false);
        var sb = new StringBuilder(screen.Text.Length + 48);
        sb.Append(screen.Text);
        sb.Append("\n[cursor ").Append(screen.CursorRow).Append(',').Append(screen.CursorColumn).Append(']');
        return CommandResult.Ok(sb.ToString());
    }
}

/// <summary>
/// STATUS — connection diagnosis: busy or broken?
/// </summary>
public sealed class StatusCommand : ISessionCommand
{
    public string Name => "STATUS";
    public string Summary => "Report connection state, emulator, screen size, byte counters and time since last byte";
    public string Example => "STATUS";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var buffer = session.Emulator.GetBuffer();
        var sinceLast = session.TimeSinceLastReceive;

        var sb = new StringBuilder(256);
        sb.Append("connected: ").Append(session.IsConnected ? "yes" : "no").Append('\n');
        sb.Append("connection: ").Append(session.Connection?.Description ?? "none").Append('\n');
        sb.Append("emulator: ").Append(session.Emulator.GetType().Name).Append('\n');
        sb.Append("size: ").Append(buffer.Width).Append('x').Append(buffer.Height).Append('\n');
        sb.Append("bytes received: ").Append(session.BytesReceived).Append('\n');
        sb.Append("bytes sent: ").Append(session.BytesSent).Append('\n');
        sb.Append("time since last byte: ");
        if (sinceLast.HasValue)
        {
            sb.Append((long)sinceLast.Value.TotalMilliseconds).Append(" ms");
        }
        else
        {
            sb.Append("never received");
        }
        return Task.FromResult(CommandResult.Ok(sb.ToString()));
    }
}

/// <summary>
/// UNHANDLED — what this session was sent and did not act on.
/// </summary>
/// <remarks>
/// <para><b>The instrument, not a diagnostic</b></para>
/// Two counters in this program exist to be pointed at a real host. The Norsk Data graphics module
/// recognises the SHAPE of all thirty <c>ESC "</c> modes and implements the ones the spec pins down
/// exactly; the rest are counted rather than guessed at, because "set polygon/shape drawing mode"
/// with an unexplained mode number cannot be implemented without inventing what it means. The ReGIS
/// decoder keeps the same tally per command letter.
///
/// So this answers "which of the modes does this program ACTUALLY use", which is a far better guide
/// to what to build next than working down a table in order.
///
/// <para><b>Why it had to be a command</b></para>
/// Both counters were readable only from inside the process. Case M7.1 in the by-hand pass is "point
/// a session at a real ND host and read the counter", and there was no way to ask - not over MCP, not
/// from the script DSL, not from the menu. Put on the registry so all three surfaces get it at once,
/// which is the rule in CLAUDE.md and the one that <c>SEND</c> was fixed on only one surface for
/// weeks by ignoring.
/// </remarks>
public sealed class UnhandledCommand : ISessionCommand
{
    public string Name => "UNHANDLED";
    public string Summary => "Report the sequences this session was sent and did not act on — the ND graphics modes and ReGIS commands";
    public string Example => "UNHANDLED";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(256);
        int total = 0;

        // Sequences the parser read and no handler claimed. FIRST, because it is the broadest of the
        // three and the one a real host actually trips: pointed at SINTRAN on 2026-08-20 the other
        // two reported "nothing unhandled" for a whole PED session while PED was sending ESC Q.
        total += AppendCounts(sb, "Escape sequences with no handler", session.Emulator.UnrecognisedSequences);

        // The ND side. Only a TDV2200 has the module at all, and only after something drew.
        var tdv = session.Emulator as Terminal.Emulators.TDV.TDV2200Emulator;
        var module = tdv?.GraphicsModule;
        if (module != null)
        {
            total += AppendCounts(sb, "Norsk Data ESC-quote modes (mode:final)", module.UnhandledSequences);
        }

        // The ReGIS side, keyed by command letter.
        var regis = session.Emulator.Regis;
        if (regis != null)
        {
            total += AppendCounts(sb, "ReGIS commands", regis.UnhandledCommands);
        }

        if (total == 0)
        {
            // Said plainly rather than returning nothing. "Nothing unhandled" and "nothing drew at
            // all" look identical from outside, and the difference decides whether the session was
            // worth running - so both are named.
            // "Nothing arrived" and "everything arrived and was handled" are different answers, and
            // the difference decides whether a session was worth running. A terminal that has
            // received bytes at all can say the stronger one.
            bool anythingArrived = session.Emulator.HasReceivedData;
            return Task.FromResult(CommandResult.Ok(anythingArrived
                ? "nothing unhandled — every sequence this session received was acted on"
                : "nothing has been received on this session, so there is nothing to report"));
        }

        return Task.FromResult(CommandResult.Ok(sb.ToString()));
    }

    /// <summary>
    /// Writes one counter's contents, biggest count first.
    /// </summary>
    /// <param name="sb">
    /// Where the report is being built.
    /// </param>
    /// <param name="heading">
    /// What this counter is.
    /// </param>
    /// <param name="counts">
    /// The counter.
    /// </param>
    /// <returns>
    /// How many distinct entries were written.
    /// </returns>
    /// <remarks>
    /// Sorted by count because the whole point is to choose what to build next, and the thing a host
    /// asked for four hundred times matters more than the one it asked for once. Sorted with an
    /// insertion sort over a plain array rather than with LINQ, which this codebase forbids; these
    /// lists are at most thirty entries long.
    /// </remarks>
    private static int AppendCounts<TKey>(StringBuilder sb, string heading,
        IReadOnlyDictionary<TKey, int> counts)
    {
        if (counts == null || counts.Count == 0) return 0;

        var keys = new TKey[counts.Count];
        var values = new int[counts.Count];

        int n = 0;
        var walker = counts.GetEnumerator();
        while (walker.MoveNext())
        {
            keys[n] = walker.Current.Key;
            values[n] = walker.Current.Value;
            n++;
        }

        for (int i = 1; i < n; i++)
        {
            TKey key = keys[i];
            int value = values[i];

            int j = i - 1;
            while (j >= 0 && values[j] < value)
            {
                keys[j + 1] = keys[j];
                values[j + 1] = values[j];
                j--;
            }

            keys[j + 1] = key;
            values[j + 1] = value;
        }

        if (sb.Length > 0) sb.Append('\n');
        sb.Append(heading).Append(':').Append('\n');

        for (int i = 0; i < n; i++)
        {
            sb.Append("  ").Append(keys[i]!.ToString()).Append(' ').Append('x').Append(values[i]);
            if (i < n - 1) sb.Append('\n');
        }

        return n;
    }
}

/// <summary>
/// SLEEP — the discouraged exception. WAITFOR is the normal way to sequence.
/// </summary>
public sealed class SleepCommand : ISessionCommand
{
    public string Name => "SLEEP";
    public string Summary => "Wait a fixed time. Discouraged — use WAITFOR/WAITIDLE; fixed delays are how logins race";
    public string Example => "SLEEP 1000   # milliseconds: 1000 = 1 s";

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("ms", CommandParameterType.Int, required: true, defaultValue: null,
            "Milliseconds to wait")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        await Task.Delay(args.GetInt("ms", 0), cancellationToken).ConfigureAwait(false);
        return CommandResult.Ok();
    }
}
