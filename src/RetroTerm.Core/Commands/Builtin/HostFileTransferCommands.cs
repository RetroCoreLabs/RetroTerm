//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Transfer;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// HOSTSEND and HOSTRECEIVE: one file to or from an IBM host with IND$FILE, the program on TSO
/// that moves files over the 3270 screen connection itself (docs/spec/IBM3270/IND-FILE-TRANSFER.md).
/// </summary>
/// <remarks>
/// <para>
/// The command types "IND$FILE PUT name ..." or "IND$FILE GET name ..." into the input field and
/// presses Enter, the way x3270's Transfer() does, so TSO must be at READY (not in ISPF: guide 08
/// common problems, "IND$FILE does nothing"). It ends on the host's closing message: TRANS03 or
/// TRANS04 is success, anything else is the error to show (section 3.13).
/// </para>
/// <para>
/// ASCII mode adds ASCII and CRLF to the host command; the host translates and adds the line
/// ends (section 2.4 and 6.1). Binary mode adds nothing and moves the bytes unchanged.
/// </para>
/// </remarks>
public static class HostFileTransfer
{
    /// <summary>
    /// The host command for one transfer.
    /// </summary>
    /// <param name="upload">
    /// True for PUT, false for GET.
    /// </param>
    /// <param name="dataSet">
    /// The host data set, as TSO takes it: without quotes the user id goes in front.
    /// </param>
    /// <param name="mode">
    /// "ascii" or "binary".
    /// </param>
    /// <param name="options">
    /// More host options typed after the mode, for example "RECFM(F) LRECL(80)"; may be empty.
    /// </param>
    /// <returns>
    /// The command, for example "IND$FILE PUT HELLO.TEXT ASCII CRLF".
    /// </returns>
    public static string Command(bool upload, string dataSet, string mode, string options)
    {
        string command = "IND$FILE " + (upload ? "PUT " : "GET ") + dataSet;
        if (mode == "ascii")
        {
            command += " ASCII CRLF";
        }

        if (options.Length > 0)
        {
            command += " " + options;
        }

        return command;
    }

    /// <summary>
    /// Checks the mode parameter.
    /// </summary>
    /// <param name="mode">
    /// The mode as given.
    /// </param>
    /// <returns>
    /// "ascii" or "binary", or null when it is neither.
    /// </returns>
    public static string? Mode(string? mode)
    {
        string m = (mode ?? "ascii").Trim().ToLowerInvariant();
        return m == "ascii" || m == "binary" ? m : null;
    }

    /// <summary>
    /// Starts the transfer on the session thread and waits for the host's closing message.
    /// </summary>
    /// <param name="session">
    /// The session whose terminal does the transfer.
    /// </param>
    /// <param name="upload">
    /// True to send the file.
    /// </param>
    /// <param name="file">
    /// The open file. Closed here when the transfer ends, also after a timeout (then later, when
    /// the host's message comes), because the host may still be using it.
    /// </param>
    /// <param name="command">
    /// The host command.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait for the host's message.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the wait.
    /// </param>
    /// <returns>
    /// Null and the host's message on success; otherwise the reason.
    /// </returns>
    public static async Task<(bool Success, string Message, bool TimedOut)> RunAsync(TerminalSession session, bool upload, Stream file, string command, int timeoutMs, CancellationToken cancellationToken)
    {
        if (session.Emulator is not IHostFileTransfer terminal)
        {
            file.Dispose();
            return (false, "this tab's terminal (" + session.Emulator.GetType().Name + ") has no host file transfer", false);
        }

        var done = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        HostFileTransferFinished? onFinished = null;
        onFinished = (success, message) =>
        {
            terminal.HostFileTransferFinished -= onFinished;
            file.Dispose();
            done.TrySetResult((success, message));
        };

        string? refused = await session.RunOnSessionThreadAsync(() =>
        {
            terminal.HostFileTransferFinished += onFinished;
            string? problem = terminal.StartHostFileTransfer(upload, file, command);
            if (problem != null)
            {
                terminal.HostFileTransferFinished -= onFinished;
            }

            return problem;
        }).ConfigureAwait(false);

        if (refused != null)
        {
            file.Dispose();
            return (false, refused, false);
        }

        Task finished = done.Task;
        Task late = Task.Delay(timeoutMs, cancellationToken);
        if (await Task.WhenAny(finished, late).ConfigureAwait(false) != finished)
        {
            // Not started yet: give up cleanly. Started: the host's message still ends it, and
            // the file is closed then.
            await session.RunOnSessionThreadAsync(() => terminal.CancelHostFileTransfer()).ConfigureAwait(false);
            if (!done.Task.IsCompleted)
            {
                return (false, "the host had not finished the transfer after " + timeoutMs + " ms", true);
            }
        }

        (bool ok, string text) = await done.Task.ConfigureAwait(false);
        return (ok, text, false);
    }

    /// <summary>
    /// Turns the outcome into a command result.
    /// </summary>
    /// <param name="outcome">
    /// What <see cref="RunAsync"/> returned.
    /// </param>
    /// <param name="clock">
    /// Started when the command started.
    /// </param>
    /// <returns>
    /// The result.
    /// </returns>
    public static CommandResult Result((bool Success, string Message, bool TimedOut) outcome, Stopwatch clock)
    {
        CommandResult result = outcome.Success ? CommandResult.Ok(outcome.Message) : CommandResult.Fail(outcome.Message);
        result.TimedOut = outcome.TimedOut;
        result.Elapsed = clock.Elapsed;
        return result;
    }
}

/// <summary>
/// HOSTSEND — send one PC file to a host data set with IND$FILE PUT.
/// </summary>
public sealed class HostSendCommand : ISessionCommand
{
    public string Name => "HOSTSEND";
    public string Summary => "Send a PC file to an IBM host data set with IND$FILE PUT (TSO must be at READY)";
    public string Example => "HOSTSEND \"C:\\\\temp\\\\hello.txt\" HELLO.TEXT"; // \\ in the DSL — \ starts an escape
    public bool CanTimeOut => true;
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("path", CommandParameterType.String, required: true, defaultValue: null,
            "Full path of the PC file to send"),
        new CommandParameter("dataset", CommandParameterType.String, required: true, defaultValue: null,
            "The host data set; without quotes TSO puts the user id in front"),
        new CommandParameter("mode", CommandParameterType.String, required: false, defaultValue: "ascii",
            "ascii (text: the host translates it and ends records at CR LF) or binary (bytes unchanged)"),
        new CommandParameter("options", CommandParameterType.String, required: false, defaultValue: "",
            "More IND$FILE options, for example RECFM(F) LRECL(80)"),
        new CommandParameter("timeout", CommandParameterType.Int, required: false, defaultValue: "120000",
            "Give up waiting for the host after this many milliseconds"),
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        string path = args.GetString("path") ?? string.Empty;
        string? mode = HostFileTransfer.Mode(args.GetString("mode"));
        if (mode == null)
        {
            return CommandResult.Fail("mode must be ascii or binary, not '" + args.GetString("mode") + "'");
        }

        if (!File.Exists(path))
        {
            return CommandResult.Fail("file not found: " + path);
        }

        string command = HostFileTransfer.Command(true, args.GetString("dataset") ?? string.Empty, mode, (args.GetString("options") ?? string.Empty).Trim());
        Stream file = File.OpenRead(path);
        var outcome = await HostFileTransfer.RunAsync(session, true, file, command, args.GetInt("timeout", 120_000), cancellationToken).ConfigureAwait(false);
        return HostFileTransfer.Result(outcome, clock);
    }
}

/// <summary>
/// HOSTRECEIVE — receive one host data set into a PC file with IND$FILE GET.
/// </summary>
/// <remarks>
/// The bytes go to a ".part" file next to the target, which becomes the target only when the
/// host says the transfer is complete; a failed transfer never leaves half a file under the
/// real name, nor touches a file that was already there.
/// </remarks>
public sealed class HostReceiveCommand : ISessionCommand
{
    public string Name => "HOSTRECEIVE";
    public string Summary => "Receive an IBM host data set into a PC file with IND$FILE GET (TSO must be at READY)";
    public string Example => "HOSTRECEIVE \"C:\\\\temp\\\\back.txt\" HELLO.TEXT"; // \\ in the DSL — \ starts an escape
    public bool CanTimeOut => true;
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("path", CommandParameterType.String, required: true, defaultValue: null,
            "Full path of the PC file to write; replaced when the transfer is complete"),
        new CommandParameter("dataset", CommandParameterType.String, required: true, defaultValue: null,
            "The host data set; without quotes TSO puts the user id in front"),
        new CommandParameter("mode", CommandParameterType.String, required: false, defaultValue: "ascii",
            "ascii (text: the host translates it and adds CR LF to each record) or binary (bytes unchanged)"),
        new CommandParameter("options", CommandParameterType.String, required: false, defaultValue: "",
            "More IND$FILE options, for example APPEND"),
        new CommandParameter("timeout", CommandParameterType.Int, required: false, defaultValue: "120000",
            "Give up waiting for the host after this many milliseconds"),
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        string path = args.GetString("path") ?? string.Empty;
        string? mode = HostFileTransfer.Mode(args.GetString("mode"));
        if (mode == null)
        {
            return CommandResult.Fail("mode must be ascii or binary, not '" + args.GetString("mode") + "'");
        }

        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (path.Length == 0 || folder == null || !Directory.Exists(folder))
        {
            return CommandResult.Fail("folder not found for: " + path);
        }

        string part = path + ".part";
        string command = HostFileTransfer.Command(false, args.GetString("dataset") ?? string.Empty, mode, (args.GetString("options") ?? string.Empty).Trim());
        Stream file = new FileStream(part, FileMode.Create, FileAccess.Write);
        var outcome = await HostFileTransfer.RunAsync(session, false, file, command, args.GetInt("timeout", 120_000), cancellationToken).ConfigureAwait(false);
        if (outcome.TimedOut)
        {
            // The host may still be writing the .part file; it is left for the host's message.
            return HostFileTransfer.Result(outcome, clock);
        }

        if (outcome.Success)
        {
            // netstandard2.1 has no File.Move with overwrite.
            File.Copy(part, path, overwrite: true);
            File.Delete(part);
        }
        else
        {
            File.Delete(part);
        }

        return HostFileTransfer.Result(outcome, clock);
    }
}
