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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// One read from one side of the tapped connection, as it came off the socket.
    /// </summary>
    /// <param name="Milliseconds">
    /// When it arrived, from the moment the tap started.
    /// </param>
    /// <param name="FromHost">
    /// True for host to client, false for client to host.
    /// </param>
    /// <param name="Bytes">
    /// The bytes, telnet layer and all.
    /// </param>
    internal sealed record Tk5WireChunk(long Milliseconds, bool FromHost, byte[] Bytes);

    /// <summary>
    /// A TCP relay between a test client and the TK5 host that keeps every byte both ways, telnet
    /// layer included, so a test can read what the host really sent rather than what the client
    /// made of it. It takes one client: the test connects to <see cref="Port"/> on the loopback.
    /// </summary>
    internal sealed class Tk5WireTap : IAsyncDisposable
    {
        private const byte Iac = 0xFF;
        private const byte Se = 0xF0;
        private const byte Sb = 0xFA;

        private readonly TcpListener _listener;
        private readonly string _host;
        private readonly int _hostPort;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<Tk5WireChunk> _chunks = new List<Tk5WireChunk>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        private TcpClient? _client;
        private TcpClient? _server;
        private Task? _relay;

        private Tk5WireTap(string host, int hostPort)
        {
            _host = host;
            _hostPort = hostPort;
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        /// <summary>
        /// Gets the loopback port the client connects to.
        /// </summary>
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>
        /// Starts listening; the relay to the host opens when the client connects.
        /// </summary>
        /// <param name="host">
        /// The host to relay to.
        /// </param>
        /// <param name="port">
        /// Its port.
        /// </param>
        /// <returns>
        /// The tap.
        /// </returns>
        public static Tk5WireTap Start(string host, int port)
        {
            var tap = new Tk5WireTap(host, port);
            tap._listener.Start(1);
            tap._relay = tap.RunAsync();
            return tap;
        }

        /// <summary>
        /// Gets how many bytes the host has sent so far.
        /// </summary>
        public long BytesFromHost
        {
            get
            {
                long total = 0;
                lock (_chunks)
                {
                    for (int i = 0; i < _chunks.Count; i++)
                    {
                        if (_chunks[i].FromHost)
                        {
                            total += _chunks[i].Bytes.Length;
                        }
                    }
                }

                return total;
            }
        }

        /// <summary>
        /// Gets the time in milliseconds since the host last sent anything, or since the tap
        /// started when it has sent nothing.
        /// </summary>
        public long MillisecondsSinceHostSpoke
        {
            get
            {
                long last = 0;
                lock (_chunks)
                {
                    for (int i = 0; i < _chunks.Count; i++)
                    {
                        if (_chunks[i].FromHost)
                        {
                            last = _chunks[i].Milliseconds;
                        }
                    }
                }

                return _clock.ElapsedMilliseconds - last;
            }
        }

        /// <summary>
        /// A copy of every chunk so far, oldest first.
        /// </summary>
        /// <returns>
        /// The chunks.
        /// </returns>
        public List<Tk5WireChunk> Chunks()
        {
            lock (_chunks)
            {
                return new List<Tk5WireChunk>(_chunks);
            }
        }

        /// <summary>
        /// Every byte the host sent, in order, run together.
        /// </summary>
        /// <returns>
        /// The bytes.
        /// </returns>
        public byte[] HostBytes()
        {
            var all = new List<byte>();
            lock (_chunks)
            {
                for (int i = 0; i < _chunks.Count; i++)
                {
                    if (_chunks[i].FromHost)
                    {
                        all.AddRange(_chunks[i].Bytes);
                    }
                }
            }

            return all.ToArray();
        }

        /// <summary>
        /// Takes one direction's bytes apart at the telnet layer: each negotiation, subnegotiation
        /// and telnet command on a line of its own, and between them the 3270 records, each ended
        /// by IAC EOR and decoded by <see cref="DecodeRecord"/>. Each chunk starts with its time,
        /// so a gap between two jobs shows.
        /// </summary>
        /// <param name="chunks">
        /// The chunks, from <see cref="Chunks"/>.
        /// </param>
        /// <param name="fromHost">
        /// Which direction to decode.
        /// </param>
        /// <returns>
        /// The decoded text, one item per line.
        /// </returns>
        public static string Decode(List<Tk5WireChunk> chunks, bool fromHost)
        {
            var text = new StringBuilder();
            var record = new List<byte>();
            int state = 0; // 0 data, 1 after IAC, 2 inside SB, 3 IAC inside SB, 4 waiting for the option of a verb
            byte verb = 0;
            var sub = new List<byte>();
            for (int c = 0; c < chunks.Count; c++)
            {
                if (chunks[c].FromHost != fromHost)
                {
                    continue;
                }

                byte[] bytes = chunks[c].Bytes;
                text.Append("[").Append(chunks[c].Milliseconds.ToString(CultureInfo.InvariantCulture)).Append(" ms] chunk of ")
                    .Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append(" bytes\n");
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    switch (state)
                    {
                        case 0:
                            if (b == Iac)
                            {
                                state = 1;
                            }
                            else
                            {
                                record.Add(b);
                            }

                            break;
                        case 1:
                            state = 0;
                            if (b == Iac)
                            {
                                record.Add(Iac);
                            }
                            else if (b == 0xEF)
                            {
                                text.Append("  IAC EOR ends a record of ").Append(record.Count.ToString(CultureInfo.InvariantCulture)).Append(" bytes:\n");
                                text.Append(DecodeRecord(record.ToArray()));
                                record.Clear();
                            }
                            else if (b == Sb)
                            {
                                sub.Clear();
                                state = 2;
                            }
                            else if (b >= 0xFB && b <= 0xFE)
                            {
                                verb = b;
                                state = 4;
                            }
                            else
                            {
                                text.Append("  IAC ").Append(CommandName(b)).Append(" (X'FF ").Append(b.ToString("X2", CultureInfo.InvariantCulture))
                                    .Append("'), with ").Append(record.Count.ToString(CultureInfo.InvariantCulture)).Append(" record bytes not yet ended by EOR\n");
                            }

                            break;
                        case 2:
                            if (b == Iac)
                            {
                                state = 3;
                            }
                            else
                            {
                                sub.Add(b);
                            }

                            break;
                        case 3:
                            if (b == Se)
                            {
                                text.Append("  IAC SB ").Append(Hex(sub.ToArray(), 0, sub.Count)).Append(" IAC SE");
                                if (sub.Count > 1 && sub[0] == 0x18)
                                {
                                    text.Append(sub[1] == 1 ? "  (TERMINAL-TYPE SEND)" : "  (TERMINAL-TYPE IS \"" + Encoding.ASCII.GetString(sub.ToArray(), 2, sub.Count - 2) + "\")");
                                }

                                text.Append('\n');
                                state = 0;
                            }
                            else
                            {
                                sub.Add(b);
                                state = 2;
                            }

                            break;
                        default:
                            text.Append("  IAC ").Append(CommandName(verb)).Append(' ').Append(OptionName(b)).Append('\n');
                            state = 0;
                            break;
                    }
                }
            }

            if (record.Count > 0)
            {
                text.Append("  ").Append(record.Count.ToString(CultureInfo.InvariantCulture)).Append(" bytes after the last EOR, never ended: ")
                    .Append(Hex(record.ToArray(), 0, record.Count)).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// Decodes one 3270 record from the host: the command, the WCC bit by bit, then every
        /// order and every run of characters in order, with NL, CR, FF and EM named where they
        /// stand (GA23-0059-4 chapters 3, 4 and 8).
        /// </summary>
        /// <param name="record">
        /// The record, command byte first.
        /// </param>
        /// <returns>
        /// The decoded text, indented, one item per line.
        /// </returns>
        public static string DecodeRecord(byte[] record)
        {
            var text = new StringBuilder();
            if (record.Length == 0)
            {
                return "    (empty)\n";
            }

            byte command = record[0];
            text.Append("    command X'").Append(command.ToString("X2", CultureInfo.InvariantCulture)).Append("' ").Append(Command3270Name(command)).Append('\n');
            int i = 1;
            if (IsWrite(command) && record.Length > 1)
            {
                byte wcc = record[1];
                text.Append("    WCC X'").Append(wcc.ToString("X2", CultureInfo.InvariantCulture)).Append("': ")
                    .Append("print format ").Append(((wcc >> 4) & 3) switch { 1 => "40", 2 => "64", 3 => "80", _ => "by NL/EM (unformatted)" })
                    .Append((wcc & 0x08) != 0 ? ", START PRINT" : ", no start print")
                    .Append((wcc & 0x04) != 0 ? ", sound alarm" : string.Empty)
                    .Append((wcc & 0x02) != 0 ? ", keyboard restore" : string.Empty)
                    .Append((wcc & 0x40) != 0 ? ", reset" : string.Empty)
                    .Append((wcc & 0x01) != 0 ? ", reset MDT" : string.Empty)
                    .Append('\n');
                i = 2;
            }

            var run = new StringBuilder();
            while (i < record.Length)
            {
                byte b = record[i];
                string? item = null;
                int step = 1;
                if (b == Order3270.SetBufferAddress && i + 2 < record.Length)
                {
                    item = BufferAddress.TryDecode(record[i + 1], record[i + 2], out int a) ? "SBA " + a.ToString(CultureInfo.InvariantCulture) : "SBA (bad address)";
                    step = 3;
                }
                else if (b == Order3270.StartField && i + 1 < record.Length)
                {
                    item = "SF X'" + record[i + 1].ToString("X2", CultureInfo.InvariantCulture) + "'";
                    step = 2;
                }
                else if (b == Order3270.InsertCursor)
                {
                    item = "IC";
                }
                else if (b == Order3270.ProgramTab)
                {
                    item = "PT";
                }
                else if (b == Order3270.RepeatToAddress && i + 3 < record.Length)
                {
                    BufferAddress.TryDecode(record[i + 1], record[i + 2], out int a);
                    item = "RA to " + a.ToString(CultureInfo.InvariantCulture) + " of X'" + record[i + 3].ToString("X2", CultureInfo.InvariantCulture) + "'";
                    step = 4;
                }
                else if (b == Order3270.EraseUnprotectedToAddress && i + 2 < record.Length)
                {
                    BufferAddress.TryDecode(record[i + 1], record[i + 2], out int a);
                    item = "EUA to " + a.ToString(CultureInfo.InvariantCulture);
                    step = 3;
                }
                else if (b == Order3270.StartFieldExtended || b == Order3270.ModifyField)
                {
                    int pairs = i + 1 < record.Length ? record[i + 1] : 0;
                    item = (b == Order3270.ModifyField ? "MF " : "SFE ") + Hex(record, i + 1, Math.Min(1 + (pairs * 2), record.Length - i - 1));
                    step = 2 + (pairs * 2);
                }
                else if (b == Order3270.SetAttribute && i + 2 < record.Length)
                {
                    item = "SA " + Hex(record, i + 1, 2);
                    step = 3;
                }
                else if (b == Order3270.GraphicEscape && i + 1 < record.Length)
                {
                    item = "GE X'" + record[i + 1].ToString("X2", CultureInfo.InvariantCulture) + "'";
                    step = 2;
                }
                else if (b == FormatControl.NewLine)
                {
                    item = "NL";
                }
                else if (b == FormatControl.FormFeed)
                {
                    item = "FF";
                }
                else if (b == FormatControl.CarriageReturn)
                {
                    item = "CR";
                }
                else if (b == FormatControl.EndOfMedium)
                {
                    item = "EM";
                }
                else if (b == FormatControl.Null)
                {
                    item = "NUL";
                }
                else if (b < 0x40)
                {
                    item = "X'" + b.ToString("X2", CultureInfo.InvariantCulture) + "' (not an order or print control)";
                }

                if (item == null)
                {
                    run.Append(CodePage.Cp037.ToUnicode(b));
                    i++;
                    continue;
                }

                if (run.Length > 0)
                {
                    text.Append("    \"").Append(run).Append("\"\n");
                    run.Clear();
                }

                text.Append("    ").Append(item).Append('\n');
                i += step;
            }

            if (run.Length > 0)
            {
                text.Append("    \"").Append(run).Append("\"\n");
            }

            return text.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            _client?.Dispose();
            _server?.Dispose();
            if (_relay != null)
            {
                try
                {
                    await _relay;
                }
                catch (OperationCanceledException)
                {
                    // The relay was stopped on purpose.
                }
                catch (SocketException)
                {
                    // The listener was stopped before a client came.
                }
                catch (ObjectDisposedException)
                {
                    // The sockets were closed under the pumps on purpose.
                }
            }

            _stop.Dispose();
        }

        private static bool IsWrite(byte command) =>
            command == Command3270.Write || command == Command3270.EraseWrite || command == Command3270.EraseWriteAlternate
            || command == 0x01 || command == 0x05 || command == 0x0D;

        private static string Command3270Name(byte command) => command switch
        {
            0xF1 or 0x01 => "Write",
            0xF5 or 0x05 => "Erase/Write",
            0x7E or 0x0D => "Erase/Write Alternate",
            0x6F or 0x0F => "Erase All Unprotected",
            0xF3 or 0x11 => "Write Structured Field",
            0xF2 or 0x02 => "Read Buffer",
            0xF6 or 0x06 => "Read Modified",
            0x6E => "Read Modified All",
            0x03 => "No Operation",
            _ => "(not a 3270 command)",
        };

        private static string CommandName(byte b) => b switch
        {
            0xF1 => "NOP",
            0xF2 => "DM",
            0xF3 => "BRK",
            0xF4 => "IP",
            0xF5 => "AO (Abort Output)",
            0xF6 => "AYT",
            0xF7 => "EC",
            0xF8 => "EL",
            0xF9 => "GA",
            0xFB => "WILL",
            0xFC => "WONT",
            0xFD => "DO",
            0xFE => "DONT",
            _ => "X'" + b.ToString("X2", CultureInfo.InvariantCulture) + "'",
        };

        private static string OptionName(byte option) => option switch
        {
            0x00 => "BINARY",
            0x18 => "TERMINAL-TYPE",
            0x19 => "END-OF-RECORD",
            0x28 => "TN3270E",
            _ => "option " + option.ToString(CultureInfo.InvariantCulture),
        };

        private static string Hex(byte[] bytes, int start, int count)
        {
            var text = new StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    text.Append(' ');
                }

                text.Append(bytes[start + i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        private async Task RunAsync()
        {
            _client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _server = new TcpClient();
            await _server.ConnectAsync(_host, _hostPort, _stop.Token);
            Task up = PumpAsync(_client.GetStream(), _server.GetStream(), false);
            Task down = PumpAsync(_server.GetStream(), _client.GetStream(), true);
            await Task.WhenAny(up, down);
        }

        private async Task PumpAsync(NetworkStream from, NetworkStream to, bool fromHost)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    int read = await from.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    byte[] copy = buffer.AsSpan(0, read).ToArray();
                    lock (_chunks)
                    {
                        _chunks.Add(new Tk5WireChunk(_clock.ElapsedMilliseconds, fromHost, copy));
                    }

                    await to.WriteAsync(copy, _stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped on purpose.
            }
            catch (System.IO.IOException)
            {
                // One side closed: the other is closed below, as the real connection would be.
            }

            // One side has gone: close the other, so the client sees the host close and the host
            // sees the client go, as on a direct connection.
            to.Socket.Shutdown(SocketShutdown.Both);
        }
    }
}
