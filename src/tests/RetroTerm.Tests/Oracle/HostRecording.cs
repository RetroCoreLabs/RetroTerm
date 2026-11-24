//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using RetroIO.Protocols.Tn3270;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// What one line of a recording is.
    /// </summary>
    internal enum RecordingLineKind
    {
        /// <summary>
        /// A telnet unit the host sent, wire bytes as they were.
        /// </summary>
        Host,

        /// <summary>
        /// A telnet unit the recording client sent; the replay waits for the client to send one
        /// of the same kind before it goes on.
        /// </summary>
        Client,

        /// <summary>
        /// Text the person typed.
        /// </summary>
        Type,

        /// <summary>
        /// A key the person pressed, by its RetroTerm name (ENTER, PF3, TAB, ...).
        /// </summary>
        Key,

        /// <summary>
        /// An IND$FILE transfer the person started (see <see cref="TransferLine"/>): the client
        /// types the IND$FILE command itself and answers the host's file transfer records.
        /// </summary>
        Transfer,
    }

    /// <summary>
    /// What a <c>transfer</c> line of a recording says: the direction, the mode, the host data
    /// set and, for an upload, the bytes of the PC file that went up.
    /// </summary>
    /// <remarks>
    /// The line is <c>transfer send ascii RTTFT.TEXT 48454C4C4F...</c> (the file as hex) or
    /// <c>transfer receive ascii RTTFT.TEXT</c>. The file's bytes are kept in the recording so the
    /// replay can give every client the very file the recording client sent.
    /// </remarks>
    internal sealed class TransferLine
    {
        public TransferLine(bool upload, string mode, string dataSet, byte[] file)
        {
            Upload = upload;
            Mode = mode;
            DataSet = dataSet;
            File = file;
        }

        /// <summary>
        /// Gets a value indicating whether this is a PC to host transfer (IND$FILE PUT).
        /// </summary>
        public bool Upload { get; }

        /// <summary>
        /// Gets the mode, ascii or binary.
        /// </summary>
        public string Mode { get; }

        /// <summary>
        /// Gets the host data set name as typed after IND$FILE PUT or GET.
        /// </summary>
        public string DataSet { get; }

        /// <summary>
        /// Gets the bytes of the PC file sent; empty for a download.
        /// </summary>
        public byte[] File { get; }

        /// <summary>
        /// Reads the text of a transfer line (what follows the word <c>transfer</c>).
        /// </summary>
        /// <param name="text">
        /// The text, for example <c>send ascii RTTFT.TEXT 4845</c>.
        /// </param>
        /// <returns>
        /// The transfer.
        /// </returns>
        public static TransferLine Parse(string text)
        {
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool upload = parts.Length == 4 && parts[0] == "send";
            bool download = parts.Length == 3 && parts[0] == "receive";
            if ((!upload && !download) || (parts[1] != "ascii" && parts[1] != "binary"))
            {
                // Refused by name, never half read (CLAUDE.md: a parser that accepts garbage costs more).
                throw new InvalidDataException("a transfer line is 'send MODE DATASET HEX' or 'receive MODE DATASET', not '" + text + "'");
            }

            return new TransferLine(upload, parts[1], parts[2], upload ? Convert.FromHexString(parts[3]) : Array.Empty<byte>());
        }

        /// <summary>
        /// The text of the line, as <see cref="Parse"/> reads it.
        /// </summary>
        /// <returns>
        /// The text.
        /// </returns>
        public string Format() => Upload
            ? "send " + Mode + " " + DataSet + " " + Convert.ToHexString(File)
            : "receive " + Mode + " " + DataSet;
    }

    /// <summary>
    /// One line of a recording.
    /// </summary>
    internal sealed class RecordingLine
    {
        public RecordingLine(RecordingLineKind kind, string text, TelnetUnit? unit)
        {
            Kind = kind;
            Text = text;
            Unit = unit;
        }

        public RecordingLineKind Kind { get; }

        /// <summary>
        /// The typed text, the key name or the transfer (<see cref="TransferLine"/>); empty for a
        /// telnet unit.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// The telnet unit of a Host or Client line.
        /// </summary>
        public TelnetUnit? Unit { get; }
    }

    /// <summary>
    /// A recorded TN3270 session with a real host, kept as test data so the same host bytes can
    /// be replayed to any client offline: every unit the host sent, every unit the client sent
    /// back, and the keys and text the person gave the client, all in the order they happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file is text, one line each:
    /// <c>model 2</c> (the 3278 model the client was),
    /// <c>host FF FD 18</c> (a host unit, wire hex, IAC doubling and IAC EOR included),
    /// <c>client FF FB 18</c> (a client unit, the same),
    /// <c>type HERC03</c>, <c>key ENTER</c> and <c>transfer send ascii RTTFT.TEXT 4845...</c>
    /// (<see cref="TransferLine"/>). Lines starting with # are comments.
    /// </para>
    /// <para>
    /// No password is ever stored. A password is typed into a non-display field, so the host
    /// never sends it back; it exists only in the client's input. <see cref="FromChunks"/>
    /// replaces it with <see cref="PasswordPlaceholder"/> in the typed text and in the client's
    /// record (as EBCDIC), and <see cref="AssertHolds"/> proves the result has none of it.
    /// </para>
    /// </remarks>
    internal sealed class HostRecording
    {
        /// <summary>
        /// What a password is replaced with. The replay types this, and the host bytes that
        /// follow were the host's answer to the real one.
        /// </summary>
        public const string PasswordPlaceholder = PasswordMask.Placeholder;

        public HostRecording(int model, List<RecordingLine> lines)
        {
            Model = model;
            Lines = lines;
        }

        public int Model { get; }

        /// <summary>
        /// Gets the columns of a screen larger than the model's (an <c>oversize 100x50</c> line,
        /// x3270's -oversize), or 0 for the model's own screen.
        /// </summary>
        public int OversizeColumns { get; private set; }

        /// <summary>
        /// Gets the rows of a screen larger than the model's, or 0.
        /// </summary>
        public int OversizeRows { get; private set; }

        public List<RecordingLine> Lines { get; }

        /// <summary>
        /// Builds a recording from a tapped connection.
        /// </summary>
        /// <param name="model">
        /// The 3278 model the client was.
        /// </param>
        /// <param name="chunks">
        /// The tap's chunks and notes, in order (<see cref="Hercules.Tk5WireTap.Chunks"/>).
        /// </param>
        /// <param name="secrets">
        /// Every password the session typed; each is replaced by the placeholder.
        /// </param>
        /// <returns>
        /// The recording.
        /// </returns>
        public static HostRecording FromChunks(int model, List<Hercules.Tk5WireChunk> chunks, string[] secrets)
        {
            var lines = new List<RecordingLine>();
            byte[] hostRest = Array.Empty<byte>();
            byte[] clientRest = Array.Empty<byte>();
            for (int c = 0; c < chunks.Count; c++)
            {
                Hercules.Tk5WireChunk chunk = chunks[c];
                if (chunk.Note != null)
                {
                    int space = chunk.Note.IndexOf(' ', StringComparison.Ordinal);
                    string verb = chunk.Note.Substring(0, space);
                    string text = chunk.Note.Substring(space + 1);
                    if (verb == "type")
                    {
                        for (int s = 0; s < secrets.Length; s++)
                        {
                            if (secrets[s].Length > 0 && string.Equals(text, secrets[s], StringComparison.OrdinalIgnoreCase))
                            {
                                text = PasswordPlaceholder;
                            }
                        }

                        lines.Add(new RecordingLine(RecordingLineKind.Type, text, null));
                    }
                    else if (verb == "transfer")
                    {
                        lines.Add(new RecordingLine(RecordingLineKind.Transfer, text, null));
                    }
                    else
                    {
                        lines.Add(new RecordingLine(RecordingLineKind.Key, text, null));
                    }

                    continue;
                }

                byte[] joined = Join(chunk.FromHost ? hostRest : clientRest, chunk.Bytes);
                var units = new List<TelnetUnit>();
                TelnetUnit.Split(joined, units, out byte[] rest);
                if (chunk.FromHost)
                {
                    hostRest = rest;
                }
                else
                {
                    clientRest = rest;
                }

                for (int u = 0; u < units.Count; u++)
                {
                    TelnetUnit unit = units[u];
                    if (!chunk.FromHost)
                    {
                        unit = Scrub(unit, secrets);
                    }

                    lines.Add(new RecordingLine(chunk.FromHost ? RecordingLineKind.Host : RecordingLineKind.Client, string.Empty, unit));
                }
            }

            if (hostRest.Length > 0 || clientRest.Length > 0)
            {
                throw new InvalidOperationException("The tapped session ended in the middle of a telnet unit: host " +
                    TelnetUnit.Hex(hostRest) + ", client " + TelnetUnit.Hex(clientRest));
            }

            return new HostRecording(model, lines);
        }

        /// <summary>
        /// Reads a recording file.
        /// </summary>
        /// <param name="path">
        /// The file.
        /// </param>
        /// <returns>
        /// The recording.
        /// </returns>
        public static HostRecording Load(string path)
        {
            string[] text = File.ReadAllLines(path);
            int model = 0;
            int oversizeColumns = 0;
            int oversizeRows = 0;
            var lines = new List<RecordingLine>();
            for (int i = 0; i < text.Length; i++)
            {
                string line = text[i];
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int space = line.IndexOf(' ', StringComparison.Ordinal);
                string verb = space < 0 ? line : line.Substring(0, space);
                string rest = space < 0 ? string.Empty : line.Substring(space + 1);
                switch (verb)
                {
                    case "model":
                        model = int.Parse(rest, CultureInfo.InvariantCulture);
                        break;
                    case "oversize":
                    {
                        // COLUMNSxROWS, as x3270's -oversize takes it.
                        int x = rest.IndexOf('x', StringComparison.Ordinal);
                        if (x <= 0)
                        {
                            throw new InvalidDataException(path + " line " + (i + 1) + ": oversize is COLUMNSxROWS, not '" + rest + "'");
                        }

                        oversizeColumns = int.Parse(rest.AsSpan(0, x), NumberStyles.None, CultureInfo.InvariantCulture);
                        oversizeRows = int.Parse(rest.AsSpan(x + 1), NumberStyles.None, CultureInfo.InvariantCulture);
                        break;
                    }

                    case "host":
                    case "client":
                        lines.Add(new RecordingLine(verb == "host" ? RecordingLineKind.Host : RecordingLineKind.Client, string.Empty, UnitFromHex(rest, path, i + 1)));
                        break;
                    case "type":
                        lines.Add(new RecordingLine(RecordingLineKind.Type, rest, null));
                        break;
                    case "key":
                        lines.Add(new RecordingLine(RecordingLineKind.Key, rest, null));
                        break;
                    case "transfer":
                        // Read once here, so a bad line fails at load and not half-way through a replay.
                        TransferLine.Parse(rest);
                        lines.Add(new RecordingLine(RecordingLineKind.Transfer, rest, null));
                        break;
                    default:
                        // A file this reader does not understand is refused by name, never half
                        // read (CLAUDE.md: a parser that accepts garbage costs more).
                        throw new InvalidDataException(path + " line " + (i + 1) + ": unknown line '" + verb + "'");
                }
            }

            if (model == 0)
            {
                throw new InvalidDataException(path + ": no 'model' line");
            }

            return new HostRecording(model, lines) { OversizeColumns = oversizeColumns, OversizeRows = oversizeRows };
        }

        /// <summary>
        /// Writes the recording in its text form.
        /// </summary>
        /// <param name="path">
        /// The file.
        /// </param>
        /// <param name="header">
        /// Comment lines put at the top: where and when it was recorded.
        /// </param>
        public void Save(string path, string[] header)
        {
            var text = new StringBuilder();
            for (int i = 0; i < header.Length; i++)
            {
                text.Append("# ").Append(header[i]).Append('\n');
            }

            text.Append("model ").Append(Model.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (OversizeColumns > 0)
            {
                text.Append("oversize ").Append(OversizeColumns.ToString(CultureInfo.InvariantCulture)).Append('x')
                    .Append(OversizeRows.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            for (int i = 0; i < Lines.Count; i++)
            {
                RecordingLine line = Lines[i];
                switch (line.Kind)
                {
                    case RecordingLineKind.Host: text.Append("host ").Append(line.Unit!.ToHex()); break;
                    case RecordingLineKind.Client: text.Append("client ").Append(line.Unit!.ToHex()); break;
                    case RecordingLineKind.Type: text.Append("type ").Append(line.Text); break;
                    case RecordingLineKind.Transfer: text.Append("transfer ").Append(line.Text); break;
                    default: text.Append("key ").Append(line.Text); break;
                }

                text.Append('\n');
            }

            File.WriteAllText(path, text.ToString());
        }

        /// <summary>
        /// Fails when the file holds any of the secrets in any form it could take: as typed, in
        /// upper case, as ASCII hex, as EBCDIC (code page 037) hex, upper case or as typed.
        /// </summary>
        /// <param name="path">
        /// The recording file.
        /// </param>
        /// <param name="secrets">
        /// The passwords.
        /// </param>
        public static void AssertHolds(string path, string[] secrets)
        {
            string file = File.ReadAllText(path);
            string upperFile = file.ToUpperInvariant();
            for (int s = 0; s < secrets.Length; s++)
            {
                string secret = secrets[s];
                if (secret.Length == 0)
                {
                    continue;
                }

                // The same forms the failure-message masking replaces (PasswordMask.Forms).
                string[] forms = PasswordMask.Forms(secret);
                for (int f = 0; f < forms.Length; f++)
                {
                    if (upperFile.Contains(forms[f], StringComparison.Ordinal))
                    {
                        // The message says which form, never the password itself.
                        throw new Xunit.Sdk.XunitException(path + " holds password " + (s + 1) + " in form " + (f + 1) + " (1 text, 2-3 ASCII hex, 4-5 EBCDIC hex).");
                    }
                }
            }
        }

        private static TelnetUnit UnitFromHex(string hex, string path, int lineNumber)
        {
            byte[] wire = TelnetUnit.ParseHex(hex);
            var units = new List<TelnetUnit>();
            TelnetUnit.Split(wire, units, out byte[] rest);
            if (units.Count != 1 || rest.Length != 0)
            {
                throw new InvalidDataException(path + " line " + lineNumber + ": not exactly one telnet unit");
            }

            return units[0];
        }

        private static byte[] Join(byte[] a, byte[] b)
        {
            byte[] joined = new byte[a.Length + b.Length];
            a.CopyTo(joined, 0);
            b.CopyTo(joined, a.Length);
            return joined;
        }

        // The client's record with each secret's EBCDIC bytes, as typed and in upper case,
        // replaced by the placeholder's. EBCDIC letters and digits are never X'FF', so the IAC
        // doubling in the wire bytes is not disturbed.
        private static TelnetUnit Scrub(TelnetUnit unit, string[] secrets)
        {
            if (unit.Kind != TelnetUnitKind.Record)
            {
                return unit;
            }

            // One masking step for recordings and failure messages alike (see PasswordMask).
            byte[] wire = PasswordMask.ReplaceKnown(unit.Wire, secrets);

            var units = new List<TelnetUnit>();
            TelnetUnit.Split(wire, units, out _);
            return units[0];
        }
    }
}
