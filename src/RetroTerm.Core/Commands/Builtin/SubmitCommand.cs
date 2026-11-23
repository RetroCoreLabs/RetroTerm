//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// The card reader that SUBMIT sends a job to: a Hercules 3505 card reader that listens on a TCP
/// port ("sockdev"). Everything here follows what the Hercules source does with the bytes, read
/// in hyperion's cardrdr.c and sockdev.c on 23 November 2026.
/// </summary>
/// <remarks>
/// <para>
/// TK5 configures it in conf/tk5.cnf line 63 as "000C 3505 ${RDRPORT:=3505} sockdev ascii trunc
/// eof". What those words do, from cardrdr.c:
/// </para>
/// <para>
/// ascii (read_ascii, lines 683-805): each byte is one column, converted to EBCDIC with
/// host_to_guest. A line feed ends a card; a carriage return is skipped wherever it is, so CR LF
/// and LF both work. A tab moves to the next multiple of 8 columns. The card is filled with
/// EBCDIC blanks first, so a short line is a card with blanks after it, and an empty line is a
/// blank card. The byte 1A (Ctrl-Z) is taken as the end of the deck. At the end of the data a
/// last line without a line feed is still a card when it has anything in it.
/// </para>
/// <para>
/// trunc (lines 785-789): the columns after 80 are dropped without a word. Without it Hercules
/// stops the read with a data check. SUBMIT refuses such a line itself, with its number, before
/// anything is sent: a cut card is a different statement from the one in the file, and MVS then
/// fails it with a message about something else (guide 08, section 2, "Column 72 limit", shows
/// three different JCL errors from cut lines). Blanks past column 80 are allowed, because cutting
/// them changes nothing on the card.
/// </para>
/// <para>
/// eof (lines 724-750): the end of the data is the end of the deck. On a socket reader the end of
/// the data is the client closing its side. Hercules then closes the socket
/// (cardrdr_close_device, line 423, and message HHC01206I "disconnected"), and it gets there only
/// when MVS has read every card up to that end. So SUBMIT closes its sending side after the last
/// byte and waits for the reader to close the connection: that close is what it reports.
/// </para>
/// <para>
/// One client at a time (sockdev.c lines 304-328): while another client is connected, or the
/// device is busy, Hercules accepts the connection and closes it at once (HHC01037E, HHC01038E).
/// From this side that looks like a connection that works and is then closed or reset. A reset
/// is reported as a failure. A plain close that comes before Hercules has read anything cannot
/// be told apart from the close after the last card; that case is not detected.
/// </para>
/// <para>
/// What cannot be seen from here: the JES2 job number. JES2 writes "$HASP100 jobname ON READER1"
/// to the operator console, not to this connection. SUBMIT reports only what it measured: the
/// cards and bytes sent, and how the connection ended.
/// </para>
/// </remarks>
public static class CardReader
{
    /// <summary>
    /// The columns on one card (cardrdr.c line 70, CARD_SIZE).
    /// </summary>
    public const int CardColumns = 80;

    /// <summary>
    /// How long SUBMIT waits for the reader to close the connection unless told otherwise.
    /// </summary>
    public const int DefaultTimeoutMs = 30_000;

    /// <summary>
    /// What SUBMIT says when the connection has no card reader port set.
    /// </summary>
    public const string NoReaderMessage = "this connection has no card reader port. Set it in Manage Connections, " +
        "Protocol tab, \"Card reader port\" (on TK5 it is the 3505 sockdev line in conf/tk5.cnf)";

    /// <summary>
    /// Checks a deck the way the Hercules ascii reader will read it, and counts its cards.
    /// </summary>
    /// <param name="deck">
    /// The file's bytes.
    /// </param>
    /// <param name="cards">
    /// The number of cards the reader will make of it; 0 when it is refused.
    /// </param>
    /// <returns>
    /// Null when the deck can be sent unchanged; otherwise what is wrong, with the line number.
    /// </returns>
    public static string? Check(ReadOnlySpan<byte> deck, out int cards)
    {
        cards = 0;
        int line = 1;
        int column = 0; // the next column to fill, counted from 0, as i in read_ascii
        for (int n = 0; n < deck.Length; n++)
        {
            byte c = deck[n];
            if (c == 0x1A)
            {
                cards = 0;
                return "line " + line.ToString(CultureInfo.InvariantCulture) + " has the byte 1A (Ctrl-Z), which the card reader takes as the end of the deck";
            }

            // read_ascii: a carriage return is skipped, a line feed ends the card.
            if (c == (byte)'\r')
            {
                continue;
            }

            if (c == (byte)'\n')
            {
                cards++;
                line++;
                column = 0;
                continue;
            }

            // read_ascii: a tab moves to the next multiple of 8, never past column 80.
            if (c == (byte)'\t')
            {
                do
                {
                    column++;
                }
                while ((column & 7) != 0 && column < CardColumns);

                continue;
            }

            // Only plain ASCII text: host_to_guest would turn any other byte into some EBCDIC
            // byte, and a UTF-8 letter such as ø is two bytes, so two columns of something else.
            if (c < 0x20 || c > 0x7E)
            {
                cards = 0;
                return "line " + line.ToString(CultureInfo.InvariantCulture) + " has the byte " + c.ToString("X2", CultureInfo.InvariantCulture) +
                    ", which is not plain ASCII text; a card holds letters, digits, blanks and the usual signs";
            }

            if (column >= CardColumns)
            {
                // Blanks past column 80 change nothing: the card is blank there anyway.
                if (c == (byte)' ')
                {
                    continue;
                }

                cards = 0;
                return "line " + line.ToString(CultureInfo.InvariantCulture) + " is longer than " + CardColumns.ToString(CultureInfo.InvariantCulture) +
                    " columns; the card reader would cut it off. Break it after a comma and go on in a line starting // and a blank";
            }

            column++;
        }

        // read_ascii at the end of the data: a last line without a line feed is a card when it
        // has anything in it ("if (i > 0) break").
        if (column > 0)
        {
            cards++;
        }

        if (cards == 0)
        {
            return "the file has no cards in it";
        }

        return null;
    }

    /// <summary>
    /// Sends a checked deck to the card reader, closes the sending side, and waits for the reader
    /// to close the connection.
    /// </summary>
    /// <param name="host">
    /// The card reader's host.
    /// </param>
    /// <param name="port">
    /// Its TCP port.
    /// </param>
    /// <param name="deck">
    /// The bytes, unchanged from the file.
    /// </param>
    /// <param name="cards">
    /// The number of cards, from <see cref="Check"/>, for the message.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait for the connection and for the reader to close it.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the wait.
    /// </param>
    /// <returns>
    /// True and what was sent, or false and what went wrong; TimedOut when the reader had not
    /// closed the connection in time.
    /// </returns>
    public static async Task<(bool Success, string Message, bool TimedOut)> SendAsync(string host, int port, byte[] deck, int cards, int timeoutMs, CancellationToken cancellationToken)
    {
        string where = host + ":" + port.ToString(CultureInfo.InvariantCulture);
        string what = cards.ToString(CultureInfo.InvariantCulture) + (cards == 1 ? " card" : " cards") +
            " (" + deck.Length.ToString(CultureInfo.InvariantCulture) + " bytes)";

        string after = " after " + timeoutMs.ToString(CultureInfo.InvariantCulture) + " ms";

        // The deadline covers the whole exchange. TcpConnector honours it while connecting (it
        // closes every attempt still running); after that the deadline closes the socket, which
        // ends a write or a read that is still waiting. Which of the three it was is in stage:
        // 0 connecting, 1 sending, 2 waiting for the reader to close.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeoutMs);
        int stage = 0;
        Socket? client = null;
        CancellationTokenRegistration closeOnDeadline = default;
        try
        {
            // TcpConnector tries IPv6 and IPv4 together (RFC 8305): "localhost" against Hercules,
            // which listens on IPv4 only, took about 2 s here before (BUGS.md B12).
            client = await TcpConnector.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            Socket connected = client;
            closeOnDeadline = deadline.Token.Register(connected.Dispose);
            stage = 1;
            using var stream = new NetworkStream(client, ownsSocket: false);
            await stream.WriteAsync(deck, 0, deck.Length, deadline.Token).ConfigureAwait(false);
            await stream.FlushAsync(deadline.Token).ConfigureAwait(false);

            // The end of the deck (the reader's "eof" option): close our sending side only, so
            // the reader's close can still be seen.
            client.Shutdown(SocketShutdown.Send);
            stage = 2;

            // The reader never sends anything; the read ends when it closes the connection,
            // which Hercules does after MVS has read the deck to its end.
            byte[] buffer = new byte[64];
            while (await stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false) > 0)
            {
            }

            return (true, "sent " + what + " to the card reader at " + where + "; the reader closed the connection after the last card", false);
        }
        catch (Exception e) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested &&
            (e is OperationCanceledException || e is ObjectDisposedException || e is IOException || e is SocketException))
        {
            switch (stage)
            {
                case 0:
                    return (false, "no answer from the card reader at " + where + after, true);
                case 1:
                    return (false, "the card reader at " + where + " had not taken " + what + after, true);
                default:
                    return (false, "sent " + what + " to the card reader at " + where + ", but it had not read them all" + after +
                        "; MVS may still read them later", true);
            }
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested && (e is IOException || e is SocketException))
        {
            switch (stage)
            {
                case 0:
                    return (false, "could not connect to the card reader at " + where + ": " + e.Message, false);
                case 1:
                    return (false, "the card reader at " + where + " closed the connection while " + what + " were being sent: " + e.Message +
                        ". Hercules does this when another program is still connected to the reader", false);
                default:
                    return (false, "sent " + what + " to the card reader at " + where + ", but it reset the connection instead of closing it: " + e.Message +
                        ". Hercules does this when another program is still connected to the reader", false);
            }
        }
        finally
        {
            closeOnDeadline.Dispose();
            client?.Dispose();
        }
    }
}

/// <summary>
/// SUBMIT — send a JCL file from the PC to the host's card reader, which MVS runs as a batch job.
/// </summary>
/// <remarks>
/// Where the reader is comes from the connection's settings (CardReaderHost and CardReaderPort in
/// the connection profile), never from the command: a script that submits on one system works on
/// another whose reader is on another port. See <see cref="CardReader"/> for what Hercules does
/// with the bytes and why a line over 80 columns is refused.
/// </remarks>
public sealed class SubmitCommand : ISessionCommand
{
    public string Name => "SUBMIT";
    public string Summary => "Send a JCL file to the host's card reader to run it as a batch job (the reader is set in the connection's settings)";
    public string Example => "SUBMIT \"C:\\\\temp\\\\hello.jcl\""; // \\ in the DSL — \ starts an escape
    public bool CanTimeOut => true;
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("path", CommandParameterType.String, required: true, defaultValue: null,
            "Full path of the JCL text file on the PC; at most 80 columns a line"),
        new CommandParameter("timeout", CommandParameterType.Int, required: false, defaultValue: "30000",
            "Give up waiting for the card reader after this many milliseconds"),
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        string path = args.GetString("path") ?? string.Empty;
        if (session.CardReaderPort is not int port || string.IsNullOrWhiteSpace(session.CardReaderHost))
        {
            return CommandResult.Fail(CardReader.NoReaderMessage);
        }

        if (!File.Exists(path))
        {
            return CommandResult.Fail("file not found: " + path);
        }

        byte[] deck = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string? problem = CardReader.Check(deck, out int cards);
        if (problem != null)
        {
            return CommandResult.Fail(path + ": " + problem + "; nothing was sent");
        }

        var outcome = await CardReader.SendAsync(session.CardReaderHost, port, deck, cards, args.GetInt("timeout", CardReader.DefaultTimeoutMs), cancellationToken).ConfigureAwait(false);
        CommandResult result = outcome.Success ? CommandResult.Ok(outcome.Message) : CommandResult.Fail(outcome.Message);
        result.TimedOut = outcome.TimedOut;
        result.Elapsed = clock.Elapsed;
        return result;
    }
}
