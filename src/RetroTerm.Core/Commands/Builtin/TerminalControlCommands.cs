using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Terminal control and logging commands: RESET, CLEAR, ECHO, SNAPSHOT,
/// LOGSTART/LOGSTOP. Registered by BuiltinCommands.RegisterAll.
/// </summary>
public static class TerminalControlCommands
{
    public static void RegisterAll(CommandRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new EmulationCommand());
        registry.Register(new ResetCommand());
        registry.Register(new ClearCommand());
        registry.Register(new EchoCommand());
        registry.Register(new SnapshotCommand());
        registry.Register(new LogStartCommand());
        registry.Register(new LogStopCommand());
    }
}

/// <summary>
/// EMULATION — change which terminal is decoding the bytes, without dropping the connection.
/// </summary>
/// <remarks>
/// The type is usually discovered to be wrong AFTER logging in, and nothing about the socket cares
/// which terminal is on this end - so this changes the terminal and leaves the line alone.
/// The screen, cursor and scrollback come across. Graphics do not, because the terminal being
/// switched to may have no bitmap at all; the reply says so when there were any.
/// </remarks>
public sealed class EmulationCommand : ISessionCommand
{
    public string Name => "EMULATION";
    public string Summary => "Change the terminal type on a live connection, keeping the screen and the connection";
    public string Example => "EMULATION type=TDV2200";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("type", CommandParameterType.String, required: true, defaultValue: null,
            "Terminal type: VT100, VT220, VT320, VT340, VT420, VT52, VT102, VT240, XTERM, XTERM-256COLOR, TDV1200, TDV2215, TDV2200, TEK4014")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var type = args.GetString("type")!;

        if (!Configuration.EmulatorFactory.IsSupported(type))
        {
            return CommandResult.Fail(
                $"'{type}' is not a terminal type this program has — try one of: " +
                string.Join(", ", Configuration.EmulatorFactory.AvailableEmulators));
        }

        // Already that terminal: say so plainly rather than rebuilding it and throwing the screen
        // through a copy for nothing.
        if (string.Equals(session.Emulator.Profile.Name, type, StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Ok($"already {type}");
        }

        bool hadGraphics = session.Emulator.Graphics?.HasAnythingToDraw() == true;

        var replacement = Configuration.EmulatorFactory.CreateForReplacement(
            type, session.Emulator.Width, session.Emulator.Height);

        await session.ChangeEmulatorAsync(replacement).ConfigureAwait(false);

        var message = $"terminal is now {type} ({replacement.Width}x{replacement.Height})";
        if (hadGraphics)
        {
            // Said out loud, because a picture disappearing with no explanation reads as a fault.
            message += " — the graphics were dropped, they cannot follow to a different terminal";
        }

        return CommandResult.Ok(message);
    }
}

/// <summary>
/// RESET — full emulator reset, recovers a garbled screen.
/// </summary>
public sealed class ResetCommand : ISessionCommand
{
    public string Name => "RESET";
    public string Summary => "Reset the terminal emulator (modes, character sets, screen) — recovers a garbled display";
    public string Example => "RESET";

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        // Emulator mutation belongs on the session pump, like all of it.
        await session.RunOnSessionThreadAsync(() => session.Emulator.Reset()).ConfigureAwait(false);
        return CommandResult.Ok("emulator reset");
    }
}

/// <summary>
/// CLEAR — clear the visible screen and the scrollback.
/// </summary>
public sealed class ClearCommand : ISessionCommand
{
    public string Name => "CLEAR";
    public string Summary => "Clear the screen and the scrollback buffer";
    public string Example => "CLEAR";

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        await session.RunOnSessionThreadAsync(() =>
        {
            var buffer = session.Emulator.GetBuffer();
            buffer.Clear();
            buffer.ClearScrollback();
            session.Emulator.GetCursor().Row = 0;
            session.Emulator.GetCursor().Column = 0;
        }).ConfigureAwait(false);
        return CommandResult.Ok("screen and scrollback cleared");
    }
}

/// <summary>
/// ECHO — write a local note into the terminal (never goes on the wire).
/// </summary>
public sealed class EchoCommand : ISessionCommand
{
    public string Name => "ECHO";
    public string Summary => "Write a local note onto the terminal screen — nothing is sent to the host";
    public string Example => "ECHO \"--- starting backup ---\"";
    public bool ProducesOutput => true; // the note also lands in the transcript

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        // DecodeEscapes, because ECHO's text goes through WriteToTerminal and is therefore
        // PARSED by the emulator, exactly like bytes off the wire. Without the flag the two
        // surfaces disagreed: the script tokenizer decodes escapes in every quoted string, so
        // ECHO "\x1b P 1 p ... " drew a ReGIS picture from a .rts script, while the same value
        // over MCP arrived as literal backslash-x-1-b and printed as text. Found 31 August 2026
        // while trying to put a ReGIS drawing on screen for M6.5, and it is the same shape of
        // fault as SEND swallowing \r on one surface only.
        new CommandParameter("text", CommandParameterType.String, required: true, defaultValue: null,
            "The text to show locally. Escapes: \\r \\n \\t \\e (ESC) \\xHH (hex) \\NNN (octal)",
            decodeEscapes: true)
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var text = args.GetString("text")!;
        session.WriteToTerminal("\r\n" + text + "\r\n");
        await session.FlushAsync().ConfigureAwait(false); // visible before the next step runs
        return CommandResult.Ok(text);
    }
}

/// <summary>
/// SNAPSHOT — save the rendered screen to a text file.
/// </summary>
public sealed class SnapshotCommand : ISessionCommand
{
    public string Name => "SNAPSHOT";
    public string Summary => "Save the rendered screen as plain text to a file";
    public string Example => "SNAPSHOT \"C:\\\\logs\\\\after-login.txt\""; // \\ in the DSL — \ starts an escape
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("file", CommandParameterType.String, required: true, defaultValue: null,
            "File to write the screen text to (directory must exist)")
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var path = args.GetString("file")!;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir != null && !Directory.Exists(dir))
        {
            return CommandResult.Fail($"directory not found: {dir}");
        }

        var screen = await session.ReadScreenAsync().ConfigureAwait(false);
        File.WriteAllText(path, screen.Text);
        return CommandResult.Ok($"screen saved to {Path.GetFullPath(path)} ({screen.Text.Length} chars)");
    }
}

/// <summary>
/// LOGSTART — record the raw session traffic to a file.
/// </summary>
public sealed class LogStartCommand : ISessionCommand
{
    public string Name => "LOGSTART";
    public string Summary => "Start logging the raw session traffic (both directions) to a file";
    public string Example => "LOGSTART \"C:\\\\logs\\\\session.txt\""; // \\ in the DSL — \ starts an escape
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("file", CommandParameterType.String, required: true, defaultValue: null,
            "Log file path (directory must exist)"),
        new CommandParameter("format", CommandParameterType.String, required: false, defaultValue: "text",
            "text (decoded), hex (hex dump) or raw (binary with direction framing)")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (session.DataLogger != null)
        {
            return Task.FromResult(CommandResult.Fail("a session log is already running — LOGSTOP first"));
        }

        var path = args.GetString("file")!;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir != null && !Directory.Exists(dir))
        {
            return Task.FromResult(CommandResult.Fail($"directory not found: {dir}"));
        }

        var formatText = args.GetString("format", "text")!;
        SessionLogFormat format;
        if (string.Equals(formatText, "text", StringComparison.OrdinalIgnoreCase))
        {
            format = SessionLogFormat.DecodedText;
        }
        else if (string.Equals(formatText, "hex", StringComparison.OrdinalIgnoreCase))
        {
            format = SessionLogFormat.HexDump;
        }
        else if (string.Equals(formatText, "raw", StringComparison.OrdinalIgnoreCase))
        {
            format = SessionLogFormat.RawBinary;
        }
        else
        {
            return Task.FromResult(CommandResult.Fail($"format '{formatText}' is not valid — use text, hex or raw"));
        }

        var logger = new FileSessionDataLogger();
        logger.Start(path, format);
        session.DataLogger = logger;
        return Task.FromResult(CommandResult.Ok($"logging to {Path.GetFullPath(path)} ({formatText})"));
    }
}

/// <summary>
/// LOGSTOP — stop the session log started with LOGSTART.
/// </summary>
public sealed class LogStopCommand : ISessionCommand
{
    public string Name => "LOGSTOP";
    public string Summary => "Stop the running session log and close the file";
    public string Example => "LOGSTOP";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = Array.Empty<CommandParameter>();

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var logger = session.DataLogger;
        if (logger == null)
        {
            return Task.FromResult(CommandResult.Ok("no session log running"));
        }
        session.DataLogger = null;
        (logger as IDisposable)?.Dispose();
        return Task.FromResult(CommandResult.Ok("session log stopped"));
    }
}
