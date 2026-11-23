//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// STATUSLINE — the status line (the 3270's line 25) and whether the keyboard is locked.
/// </summary>
public sealed class StatusLineCommand : ISessionCommand
{
    public string Name => "STATUSLINE";
    public string Summary => "Read the status line: the 3270's line 25 with the keyboard lock, or the line a VT host writes";
    public string Example => "STATUSLINE";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = System.Array.Empty<CommandParameter>();

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (session.Emulator is not IStatusLineTerminal terminal)
        {
            // A stream terminal's status line is the one the host writes into (DEC DECSSDT).
            string hostLine = await session.RunOnSessionThreadAsync(() => session.Emulator.StatusLineText).ConfigureAwait(false);
            return CommandResult.Ok(hostLine);
        }

        (string line, string? reason) = await session.RunOnSessionThreadAsync(() => (terminal.DrawnStatusLine, terminal.KeyboardLockReason)).ConfigureAwait(false);
        return CommandResult.Ok(line.TrimEnd() + "\nkeyboard: " + (reason == null ? "unlocked" : "locked, " + reason));
    }
}

/// <summary>
/// WAITUNLOCK — wait until the host unlocks the keyboard: the 3270 way of knowing it answered.
/// </summary>
/// <remarks>
/// A 3270 host answers an Enter with a Write that restores the keyboard (GA23-0059-4 p. 3-3, WCC
/// bit 6). Waiting on the text is not enough: the screen can be drawn in several records and the
/// keyboard only unlocks on the last. An operator-error lock (typing on a protected field) is never
/// lifted by the host; it needs Reset, so the wait reports it at once instead of timing out.
/// </remarks>
public sealed class WaitUnlockCommand : ISessionCommand
{
    public string Name => "WAITUNLOCK";
    public string Summary => "Wait until the host unlocks the 3270 keyboard (it has answered)";
    public string Example => "WAITUNLOCK 10000   # give up after 10 s";
    public bool CanTimeOut => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("timeout", CommandParameterType.Int, required: false, defaultValue: "30000",
            "Give up after this many milliseconds"),
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (session.Emulator is not IStatusLineTerminal terminal)
        {
            return CommandResult.Fail("this tab's terminal (" + session.Emulator.GetType().Name + ") has no keyboard lock to wait for");
        }

        int timeoutMs = args.GetInt("timeout", 30_000);
        var clock = Stopwatch.StartNew();
        while (true)
        {
            string? reason = await session.RunOnSessionThreadAsync(() => terminal.KeyboardLockReason).ConfigureAwait(false);
            if (reason == null)
            {
                CommandResult ok = CommandResult.Ok("keyboard unlocked");
                ok.Elapsed = clock.Elapsed;
                return ok;
            }

            if (reason == Ibm3270LockReasons.OperatorError)
            {
                return CommandResult.Fail("the keyboard is locked by an operator error, which only Reset clears (SENDKEY IBM.RESET)");
            }

            if (!session.IsConnected)
            {
                CommandResult lost = CommandResult.Fail("Connection lost while waiting");
                lost.ConnectionLost = true;
                return lost;
            }

            if (clock.ElapsedMilliseconds >= timeoutMs)
            {
                CommandResult late = CommandResult.Fail("the keyboard was still locked (" + reason + ") after " + timeoutMs + " ms");
                late.TimedOut = true;
                late.Elapsed = clock.Elapsed;
                return late;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The lock reasons an <see cref="IStatusLineTerminal"/> reports, shared so the command can tell
/// a lock the host will lift from one only Reset lifts.
/// </summary>
public static class Ibm3270LockReasons
{
    /// <summary>
    /// The keyboard waits for the host after an attention key (X Wait).
    /// </summary>
    public const string WaitingForHost = "waiting for the host";

    /// <summary>
    /// The host wrote without restoring the keyboard (X SYSTEM).
    /// </summary>
    public const string SystemLock = "System Lock";

    /// <summary>
    /// The operator did something not allowed (X Protected, X Overflow, ...); Reset clears it.
    /// </summary>
    public const string OperatorError = "operator error";
}
