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
        /// The typed text or the key name; empty for a telnet unit.
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
    /// <c>type HERC03</c> and <c>key ENTER</c>. Lines starting with # are comments.
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
        public const string PasswordPlaceholder = "PASSWORD";

        public HostRecording(int model, List<RecordingLine> lines)
        {
            Model = model;
            Lines = lines;
        }

        public int Model { get; }

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

            return new HostRecording(model, lines);
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
            for (int i = 0; i < Lines.Count; i++)
            {
                RecordingLine line = Lines[i];
                switch (line.Kind)
                {
                    case RecordingLineKind.Host: text.Append("host ").Append(line.Unit!.ToHex()); break;
                    case RecordingLineKind.Client: text.Append("client ").Append(line.Unit!.ToHex()); break;
                    case RecordingLineKind.Type: text.Append("type ").Append(line.Text); break;
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

                string[] forms =
                {
                    secret.ToUpperInvariant(),
                    TelnetUnit.Hex(Encoding.ASCII.GetBytes(secret)),
                    TelnetUnit.Hex(Encoding.ASCII.GetBytes(secret.ToUpperInvariant())),
                    TelnetUnit.Hex(Ebcdic(secret)),
                    TelnetUnit.Hex(Ebcdic(secret.ToUpperInvariant())),
                };
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

        // The text in EBCDIC code page 037, the code page the recording sessions ran in. A
        // character the code page does not have cannot have been sent, so it is left out.
        private static byte[] Ebcdic(string text)
        {
            var bytes = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (CodePage.Cp037.TryFromUnicode(text[i], out byte b))
                {
                    bytes.Add(b);
                }
            }

            return bytes.ToArray();
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

            byte[] wire = unit.Wire;
            for (int s = 0; s < secrets.Length; s++)
            {
                if (secrets[s].Length == 0)
                {
                    continue;
                }

                wire = Replace(wire, Ebcdic(secrets[s]));
                wire = Replace(wire, Ebcdic(secrets[s].ToUpperInvariant()));
            }

            var units = new List<TelnetUnit>();
            TelnetUnit.Split(wire, units, out _);
            return units[0];
        }

        private static byte[] Replace(byte[] wire, byte[] secret)
        {
            byte[] placeholder = Ebcdic(PasswordPlaceholder);
            var result = new List<byte>(wire.Length);
            int i = 0;
            while (i < wire.Length)
            {
                if (i + secret.Length <= wire.Length && wire.AsSpan(i, secret.Length).SequenceEqual(secret))
                {
                    result.AddRange(placeholder);
                    i += secret.Length;
                }
                else
                {
                    result.Add(wire[i]);
                    i++;
                }
            }

            return result.ToArray();
        }
    }
}
