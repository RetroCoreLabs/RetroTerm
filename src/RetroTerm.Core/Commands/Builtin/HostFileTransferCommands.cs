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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
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
    /// How long a transfer waits for the host's closing message unless told otherwise: the
    /// default of HOSTSEND and HOSTRECEIVE, and what the IBM Host Files window uses.
    /// </summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// What the IBM Host Files window says when the terminal is not at TSO's READY prompt.
    /// </summary>
    public const string NotAtReadyMessage = "Go to the READY prompt first (press F3 until you see READY), then try again.";

    // How long the window's flows wait for TSO's READY after a transfer, and for the host's
    // answer to the Clear pressed then.
    // TK5 answered at once when measured on 23 November 2026; this is only a deadline.
    private const int ClearAnswerTimeoutMs = 10_000;

    /// <summary>
    /// Whether a screen is TSO waiting for a command at READY, so that typing an IND$FILE command
    /// there runs it and damages nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two screens count, both measured on TK5 on 23 November 2026:
    /// </para>
    /// <para>
    /// 1. READY: the last line with anything on it, above the cursor's row, reads READY. TSO
    /// writes READY on one line and puts the cursor at the start of the next (after F3 from ISPF:
    /// READY on row 0, cursor on row 1, column 0). Text the user has typed on the cursor's row
    /// does not matter: the transfer empties that field before it types (Ibm3270Emulator,
    /// StartHostFileTransfer).
    /// </para>
    /// <para>
    /// 2. A blank screen: what TSO shows after Clear at READY (cursor at row 0, column 0, nothing
    /// written). TSO is still at READY then, and the guide 08 live test types IND$FILE on exactly
    /// that screen (Scenario08FileTransferTests). The window presses Clear after every transfer,
    /// so without this the second transfer would be refused.
    /// </para>
    /// <para>
    /// In both cases the keyboard must be unlocked: locked, the host is still busy or the user
    /// made an error that only Reset clears. ISPF and other full-screen programs never show READY
    /// last above the cursor, and they repaint themselves after Clear, so they are refused.
    /// </para>
    /// </remarks>
    /// <param name="rows">
    /// The host's rows, top first, without the status line.
    /// </param>
    /// <param name="cursorRow">
    /// The cursor's row, counted from 0.
    /// </param>
    /// <param name="keyboardLocked">
    /// True when the keyboard is locked.
    /// </param>
    /// <returns>
    /// True when an IND$FILE command may be typed.
    /// </returns>
    public static bool IsAtReady(IReadOnlyList<string> rows, int cursorRow, bool keyboardLocked)
    {
        return IsAtReady(rows, cursorRow, keyboardLocked, acceptBlank: true);
    }

    // acceptBlank false: only a screen that shows READY. That is what ends a transfer: TK5
    // erases the screen when the transfer starts (its first record is an Erase/Write with
    // nothing in it) and writes READY after the TRANS message, so in between the screen is
    // blank and the keyboard unlocked, and a blank screen there does NOT mean TSO is back.
    private static bool IsAtReady(IReadOnlyList<string> rows, int cursorRow, bool keyboardLocked, bool acceptBlank)
    {
        if (keyboardLocked)
        {
            return false;
        }

        bool blank = true;
        for (int r = 0; r < rows.Count; r++)
        {
            if (rows[r].Trim().Length > 0)
            {
                blank = false;
                break;
            }
        }

        if (blank)
        {
            return acceptBlank;
        }

        int last = cursorRow > rows.Count ? rows.Count : cursorRow;
        for (int r = last - 1; r >= 0; r--)
        {
            string line = rows[r].Trim();
            if (line.Length > 0)
            {
                return line == "READY";
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the session's screen and keyboard on the session thread and applies
    /// <see cref="IsAtReady(IReadOnlyList{string}, int, bool)"/>.
    /// </summary>
    /// <param name="session">
    /// The session.
    /// </param>
    /// <returns>
    /// True when the terminal is a 3270 at TSO's READY prompt.
    /// </returns>
    public static Task<bool> IsAtReadyAsync(TerminalSession session)
    {
        return IsAtReadyAsync(session, acceptBlank: true);
    }

    private static async Task<bool> IsAtReadyAsync(TerminalSession session, bool acceptBlank)
    {
        if (session.Emulator is not IHostFileTransfer || session.Emulator is not IStatusLineTerminal terminal)
        {
            return false;
        }

        ScreenSnapshot snapshot = await session.ReadScreenAsync(stripTrailingBlanks: false).ConfigureAwait(false);
        (bool locked, int statusRow) = await session.RunOnSessionThreadAsync(() =>
            (terminal.KeyboardLockReason != null, session.Emulator is IRuledRowTerminal ruled ? ruled.RuleAboveRow : -1)).ConfigureAwait(false);

        // The status line (the 3270's line 25) is in the buffer below the host's rows; it is not
        // the host's, and its "X SYSTEM" or LU name must not count as a line of text.
        string[] all = snapshot.Text.Split('\n');
        int count = statusRow >= 0 && statusRow < all.Length ? statusRow : all.Length;
        var rows = new List<string>(count);
        for (int r = 0; r < count; r++)
        {
            rows.Add(all[r]);
        }

        return IsAtReady(rows, snapshot.CursorRow, locked, acceptBlank);
    }

    /// <summary>
    /// Whether a data set name names a member of a partitioned data set: "LIB(MEMBER)" or
    /// "'HERC01.DEMO.CNTL(HELLO)'".
    /// </summary>
    /// <param name="dataSet">
    /// The name as typed.
    /// </param>
    /// <returns>
    /// True when it has a member in brackets.
    /// </returns>
    public static bool IsMember(string dataSet)
    {
        return dataSet.IndexOf('(') >= 0;
    }

    /// <summary>
    /// Checks a data set name typed by a person, quoted or not.
    /// </summary>
    /// <param name="dataSet">
    /// The name as typed.
    /// </param>
    /// <param name="name">
    /// The name without surrounding blanks, as it goes into the command.
    /// </param>
    /// <returns>
    /// Null when it can be used, otherwise what is wrong.
    /// </returns>
    public static string? CheckDataSetName(string? dataSet, out string name)
    {
        name = (dataSet ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return "Give the host data set name.";
        }

        // A blank would end the name on the host and make the rest IND$FILE options.
        if (name.IndexOf(' ') >= 0)
        {
            return "A data set name has no blanks in it: " + name;
        }

        return null;
    }

    /// <summary>
    /// The record options of an upload, from the three fields of the IBM Host Files window.
    /// </summary>
    /// <remarks>
    /// RECFM, LRECL and BLKSIZE are PUT options only, and a PDS member takes none of them: only
    /// ASCII and CRLF are valid for a member (IND-FILE-TRANSFER.md section 2.1). An empty field
    /// leaves the choice to the host. The numbers are checked against 1 to 32760, the largest
    /// MVS record and block (an MVS limit, not from the IND$FILE manual; the manual's 1 to 132
    /// for LRECL is contradicted by TK5, which made LRECL 255 itself, section 2.1).
    /// </remarks>
    /// <param name="dataSet">
    /// The data set name, to see whether it is a member.
    /// </param>
    /// <param name="recfm">
    /// F, V or U, or empty.
    /// </param>
    /// <param name="lrecl">
    /// The record length, or empty.
    /// </param>
    /// <param name="blksize">
    /// The block size, or empty.
    /// </param>
    /// <param name="options">
    /// The options for <see cref="Command"/>, for example "RECFM(F) LRECL(80) BLKSIZE(3120)".
    /// </param>
    /// <returns>
    /// Null when they can be used, otherwise what is wrong.
    /// </returns>
    public static string? PutOptions(string dataSet, string? recfm, string? lrecl, string? blksize, out string options)
    {
        options = string.Empty;
        string f = (recfm ?? string.Empty).Trim().ToUpperInvariant();
        string l = (lrecl ?? string.Empty).Trim();
        string b = (blksize ?? string.Empty).Trim();
        if (f.Length == 0 && l.Length == 0 && b.Length == 0)
        {
            return null;
        }

        if (IsMember(dataSet))
        {
            return "A member takes no RECFM, LRECL or BLKSIZE; leave them empty.";
        }

        if (f.Length > 0 && f != "F" && f != "V" && f != "U")
        {
            return "RECFM must be F, V or U, not " + f + ".";
        }

        string? bad = CheckNumber("LRECL", l) ?? CheckNumber("BLKSIZE", b);
        if (bad != null)
        {
            return bad;
        }

        if (f.Length > 0)
        {
            options = "RECFM(" + f + ")";
        }

        if (l.Length > 0)
        {
            options += (options.Length > 0 ? " " : string.Empty) + "LRECL(" + l + ")";
        }

        if (b.Length > 0)
        {
            options += (options.Length > 0 ? " " : string.Empty) + "BLKSIZE(" + b + ")";
        }

        return null;
    }

    /// <summary>
    /// Sends a PC file: opens it and runs <see cref="RunAsync"/>. HOSTSEND and the IBM Host
    /// Files window both come here.
    /// </summary>
    /// <param name="session">
    /// The session.
    /// </param>
    /// <param name="path">
    /// The PC file; it must exist.
    /// </param>
    /// <param name="command">
    /// The host command, from <see cref="Command"/>.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait for the host's message.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the wait.
    /// </param>
    /// <returns>
    /// What <see cref="RunAsync"/> returned.
    /// </returns>
    public static Task<(bool Success, string Message, bool TimedOut)> SendFileAsync(TerminalSession session, string path, string command, int timeoutMs, CancellationToken cancellationToken)
    {
        Stream file = File.OpenRead(path);
        return RunAsync(session, true, file, command, timeoutMs, cancellationToken);
    }

    /// <summary>
    /// Receives into a PC file. HOSTRECEIVE and the IBM Host Files window both come here.
    /// </summary>
    /// <remarks>
    /// The bytes go to a ".part" file next to the target, which becomes the target only when the
    /// host says the transfer is complete; a failed transfer never leaves half a file under the
    /// real name, nor touches a file that was already there. After a timeout the .part file is
    /// left: the host may still be writing it, and it is closed when the host's message comes.
    /// </remarks>
    /// <param name="session">
    /// The session.
    /// </param>
    /// <param name="path">
    /// The PC file to write; its folder must exist.
    /// </param>
    /// <param name="command">
    /// The host command, from <see cref="Command"/>.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait for the host's message.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the wait.
    /// </param>
    /// <returns>
    /// What <see cref="RunAsync"/> returned.
    /// </returns>
    public static async Task<(bool Success, string Message, bool TimedOut)> ReceiveFileAsync(TerminalSession session, string path, string command, int timeoutMs, CancellationToken cancellationToken)
    {
        string part = path + ".part";
        Stream file = new FileStream(part, FileMode.Create, FileAccess.Write);
        var outcome = await RunAsync(session, false, file, command, timeoutMs, cancellationToken).ConfigureAwait(false);
        if (outcome.TimedOut)
        {
            return outcome;
        }

        if (outcome.Success)
        {
            // netstandard2.1 has no File.Move with overwrite.
            File.Copy(part, path, overwrite: true);
        }

        File.Delete(part);
        return outcome;
    }

    /// <summary>
    /// IBM Host Files window, View: receives a data set as text and returns it.
    /// </summary>
    /// <remarks>
    /// Always ASCII CRLF, whatever the window's Text/Binary choice: View shows text. The bytes
    /// are received into memory, not into a temporary file, so nothing is left on the PC when
    /// the host fails or stops. They are read as ISO 8859-1, one character per byte, so no byte
    /// is lost or replaced.
    /// </remarks>
    /// <param name="session">
    /// The 3270 tab's session.
    /// </param>
    /// <param name="dataSet">
    /// The data set name as typed.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait for the host's message.
    /// </param>
    /// <param name="cancellationToken">
    /// The window's Cancel.
    /// </param>
    /// <returns>
    /// The outcome; on success its Text holds the data set.
    /// </returns>
    public static async Task<HostFilesOutcome> ViewAsync(TerminalSession session, string? dataSet, int timeoutMs, CancellationToken cancellationToken)
    {
        string? problem = CheckDataSetName(dataSet, out string name);
        if (problem != null)
        {
            return new HostFilesOutcome(false, problem, null);
        }

        var memory = new MemoryStream();
        string command = Command(false, name, "ascii", string.Empty);
        HostFilesOutcome outcome = await AtReadyAsync(session, () => RunAsync(session, false, memory, command, timeoutMs, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!outcome.Success)
        {
            return outcome;
        }

        // ToArray still works after RunAsync has closed the stream.
        string text = Encoding.GetEncoding(28591).GetString(memory.ToArray());
        return new HostFilesOutcome(true, outcome.Message, text);
    }

    /// <summary>
    /// IBM Host Files window, Download: receives a data set into a PC file.
    /// </summary>
    /// <param name="session">
    /// The 3270 tab's session.
    /// </param>
    /// <param name="dataSet">
    /// The data set name as typed.
    /// </param>
    /// <param name="binary">
    /// True for bytes unchanged, false for text.
    /// </param>
    /// <param name="path">
    /// The PC file, replaced only when the transfer is complete.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait for the host's message.
    /// </param>
    /// <param name="cancellationToken">
    /// The window's Cancel.
    /// </param>
    /// <returns>
    /// The outcome.
    /// </returns>
    public static Task<HostFilesOutcome> DownloadAsync(TerminalSession session, string? dataSet, bool binary, string path, int timeoutMs, CancellationToken cancellationToken)
    {
        string? problem = CheckDataSetName(dataSet, out string name) ?? MemberMode(name, binary);
        if (problem != null)
        {
            return Task.FromResult(new HostFilesOutcome(false, problem, null));
        }

        string command = Command(false, name, binary ? "binary" : "ascii", string.Empty);
        return AtReadyAsync(session, () => ReceiveFileAsync(session, path, command, timeoutMs, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// IBM Host Files window, Upload: sends a PC file to a data set.
    /// </summary>
    /// <param name="session">
    /// The 3270 tab's session.
    /// </param>
    /// <param name="dataSet">
    /// The data set name as typed.
    /// </param>
    /// <param name="binary">
    /// True for bytes unchanged, false for text.
    /// </param>
    /// <param name="path">
    /// The PC file to send.
    /// </param>
    /// <param name="recfm">
    /// F, V or U, or empty for the host's choice.
    /// </param>
    /// <param name="lrecl">
    /// The record length, or empty.
    /// </param>
    /// <param name="blksize">
    /// The block size, or empty.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait for the host's message.
    /// </param>
    /// <param name="cancellationToken">
    /// The window's Cancel.
    /// </param>
    /// <returns>
    /// The outcome.
    /// </returns>
    public static Task<HostFilesOutcome> UploadAsync(TerminalSession session, string? dataSet, bool binary, string path, string? recfm, string? lrecl, string? blksize, int timeoutMs, CancellationToken cancellationToken)
    {
        string? problem = CheckDataSetName(dataSet, out string name) ?? MemberMode(name, binary);
        string options = string.Empty;
        problem ??= PutOptions(name, recfm, lrecl, blksize, out options);
        if (problem == null && !File.Exists(path))
        {
            problem = "File not found: " + path;
        }

        if (problem != null)
        {
            return Task.FromResult(new HostFilesOutcome(false, problem, null));
        }

        string command = Command(true, name, binary ? "binary" : "ascii", options);
        return AtReadyAsync(session, () => SendFileAsync(session, path, command, timeoutMs, cancellationToken), cancellationToken);
    }

    // A member takes only ASCII and CRLF (IND-FILE-TRANSFER.md section 2.1).
    private static string? MemberMode(string name, bool binary)
    {
        return binary && IsMember(name) ? "A member can only be moved as Text." : null;
    }

    private static string? CheckNumber(string what, string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                return what + " must be a number, not " + value + ".";
            }
        }

        int number = value.Length > 5 ? int.MaxValue : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        if (number < 1 || number > 32760)
        {
            return what + " must be from 1 to 32760, not " + value + ".";
        }

        return null;
    }

    // The window's rule around every transfer: refuse unless at READY, run it, wait for TSO's
    // READY, then press Clear so the user gets an empty READY back (the blank screen IsAtReady
    // accepts), and wait for the host to answer the Clear so the next check sees an unlocked
    // keyboard. After a timeout
    // nothing is pressed: the host may still be moving the file.
    private static async Task<HostFilesOutcome> AtReadyAsync(TerminalSession session, Func<Task<(bool Success, string Message, bool TimedOut)>> transfer, CancellationToken cancellationToken)
    {
        if (!await IsAtReadyAsync(session).ConfigureAwait(false))
        {
            return new HostFilesOutcome(false, NotAtReadyMessage, null);
        }

        // Cancelled while the READY check ran: nothing has been typed yet, so nothing is. Until
        // 23 November 2026 the command went out anyway and the transfer was given up only after.
        if (cancellationToken.IsCancellationRequested)
        {
            return new HostFilesOutcome(false, "Cancelled. Nothing was sent to the host.", null);
        }

        (bool success, string message, bool timedOut) = await transfer().ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            message = "Cancelled. " + message;
        }

        if (timedOut)
        {
            return new HostFilesOutcome(false, message, null);
        }

        // Clear only once TSO has shown READY again. The host's TRANS03 message comes inside
        // the transfer and TSO writes READY after it; on TK5 (23 November 2026) a Clear sent
        // straight after the message went out BEFORE that READY, which then landed on top of the
        // cleared screen. Waiting for a screen that shows READY - not a blank one, which is what
        // the transfer itself leaves - with the keyboard unlocked (a 3270 takes no attention key
        // while it is locked) puts the Clear after it, every time.
        if (!await WaitForAsync(session, () => IsAtReadyAsync(session, acceptBlank: false)).ConfigureAwait(false))
        {
            message += " (TSO did not show READY again, so Clear was not pressed)";
        }
        else if (session.Emulator is INamedKeyTerminal keys && keys.PressNamedKey("CLEAR", out _) == NamedKeyResult.Pressed)
        {
            await session.FlushAsync().ConfigureAwait(false);
            if (!await WaitForAsync(session, () => session.RunOnSessionThreadAsync(() => session.Emulator is IStatusLineTerminal t && t.KeyboardLockReason == null)).ConfigureAwait(false))
            {
                message += " (the host did not answer Clear within " + ClearAnswerTimeoutMs + " ms)";
            }
        }

        return new HostFilesOutcome(success, message, null);
    }

    // Polls a condition on the session until it holds, with a deadline: a poll on the thing
    // waited for, never a sleep and hope. False when it still does not hold at the end.
    private static async Task<bool> WaitForAsync(TerminalSession session, Func<Task<bool>> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition().ConfigureAwait(false))
        {
            if (clock.ElapsedMilliseconds > ClearAnswerTimeoutMs || !session.IsConnected)
            {
                return false;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return true;
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
/// What one action of the IBM Host Files window came to: the line for the window's status
/// line, and for View the data set's text.
/// </summary>
public sealed class HostFilesOutcome
{
    /// <summary>
    /// Builds an outcome.
    /// </summary>
    /// <param name="success">
    /// True when the host said the transfer was complete.
    /// </param>
    /// <param name="message">
    /// The host's message (TRANS03, TRANS04) or what went wrong.
    /// </param>
    /// <param name="text">
    /// For View, the data set as text; otherwise null.
    /// </param>
    public HostFilesOutcome(bool success, string message, string? text)
    {
        Success = success;
        Message = message;
        Text = text;
    }

    /// <summary>
    /// Gets whether the host said the transfer was complete.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the host's message or what went wrong, for the status line.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the data set's text after a View, otherwise null.
    /// </summary>
    public string? Text { get; }
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
        var outcome = await HostFileTransfer.SendFileAsync(session, path, command, args.GetInt("timeout", HostFileTransfer.DefaultTimeoutMs), cancellationToken).ConfigureAwait(false);
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

        string command = HostFileTransfer.Command(false, args.GetString("dataset") ?? string.Empty, mode, (args.GetString("options") ?? string.Empty).Trim());
        var outcome = await HostFileTransfer.ReceiveFileAsync(session, path, command, args.GetInt("timeout", HostFileTransfer.DefaultTimeoutMs), cancellationToken).ConfigureAwait(false);
        return HostFileTransfer.Result(outcome, clock);
    }
}
