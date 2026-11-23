//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// A TCP server on the loopback that plays the host's side of a recorded TN3270 session to
    /// one client: the test sends each recorded host unit when its turn comes, and waits for the
    /// client's units in between. It takes one client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything the client sends is split into telnet units as it arrives and kept, so a test
    /// can wait for "the next record" or "a WILL TERMINAL-TYPE" and afterwards compare what two
    /// clients sent.
    /// </para>
    /// <para>
    /// <see cref="SyncAsync"/> is how the test knows the client has READ what was sent: it sends
    /// IAC DO TIMING-MARK and waits for the answer. RFC 860 lets a client answer WILL or WONT;
    /// either one is sent only after the client has dealt with every byte before it, because a
    /// telnet client reads its stream in order.
    /// </para>
    /// </remarks>
    internal sealed class ReplayHost : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly List<TelnetUnit> _received = new List<TelnetUnit>();
        private readonly SemaphoreSlim _arrived = new SemaphoreSlim(0);
        private readonly Task<TcpClient> _accept;

        // How far the waits have read in _received: negotiations are matched anywhere after the
        // last match (a client may answer in any order), records strictly in order.
        private int _nextRecord;
        private readonly List<bool> _negotiationUsed = new List<bool>();

        private TcpClient? _client;
        private NetworkStream? _stream;
        private Task? _reader;

        private ReplayHost()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            _accept = _listener.AcceptTcpClientAsync(_stop.Token).AsTask();
        }

        /// <summary>
        /// Gets the loopback port the client connects to.
        /// </summary>
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>
        /// Gets how many 3270 records this host has sent.
        /// </summary>
        public int RecordsSent { get; private set; }

        public static ReplayHost Start() => new ReplayHost();

        /// <summary>
        /// Waits for the client to connect.
        /// </summary>
        /// <param name="timeoutMs">
        /// How long to wait.
        /// </param>
        /// <returns>
        /// A task that completes when it has.
        /// </returns>
        public async Task AcceptAsync(int timeoutMs)
        {
            Task done = await Task.WhenAny(_accept, Task.Delay(timeoutMs, _stop.Token));
            if (done != _accept)
            {
                throw new Xunit.Sdk.XunitException("The client did not connect to the replay host within " + timeoutMs + " ms.");
            }

            _client = await _accept;
            _client.NoDelay = true;
            _stream = _client.GetStream();
            _reader = ReadAsync(_stream);
        }

        /// <summary>
        /// Sends one recorded host unit, exactly as the host sent it.
        /// </summary>
        /// <param name="unit">
        /// The unit.
        /// </param>
        /// <returns>
        /// A task that completes when it is written.
        /// </returns>
        public async Task SendAsync(TelnetUnit unit)
        {
            await _stream!.WriteAsync(unit.Wire, _stop.Token);
            if (unit.Kind == TelnetUnitKind.Record)
            {
                RecordsSent++;
            }
        }

        /// <summary>
        /// Sends IAC DO TIMING-MARK and waits for the client's WILL or WONT TIMING-MARK, which it
        /// sends only once it has read everything before it.
        /// </summary>
        /// <param name="timeoutMs">
        /// How long to wait.
        /// </param>
        /// <returns>
        /// A task that completes when the answer has come.
        /// </returns>
        public async Task SyncAsync(int timeoutMs)
        {
            await _stream!.WriteAsync(new byte[] { TelnetUnit.Iac, TelnetUnit.Do, TelnetUnit.TimingMark }, _stop.Token);
            await WaitForAsync(u => u.Kind == TelnetUnitKind.Negotiation && u.Payload[1] == TelnetUnit.TimingMark
                && (u.Payload[0] == TelnetUnit.Will || u.Payload[0] == TelnetUnit.Wont), "an answer to IAC DO TIMING-MARK", timeoutMs);
        }

        /// <summary>
        /// Waits until the client has sent a unit like the recorded one: the same negotiation, a
        /// subnegotiation for the same option and of the same kind (the TERMINAL-TYPE name itself
        /// may differ, and is compared afterwards), or the next record.
        /// </summary>
        /// <param name="recorded">
        /// The unit the recording client sent.
        /// </param>
        /// <param name="timeoutMs">
        /// How long to wait.
        /// </param>
        /// <returns>
        /// The unit this client sent.
        /// </returns>
        public Task<TelnetUnit> WaitForLikeAsync(TelnetUnit recorded, int timeoutMs)
        {
            switch (recorded.Kind)
            {
                case TelnetUnitKind.Negotiation:
                    return WaitForAsync(u => u.Kind == TelnetUnitKind.Negotiation && u.Payload[0] == recorded.Payload[0] && u.Payload[1] == recorded.Payload[1],
                        "IAC " + TelnetUnit.Hex(recorded.Payload), timeoutMs);
                case TelnetUnitKind.Subnegotiation:
                    return WaitForAsync(u => u.Kind == TelnetUnitKind.Subnegotiation && u.Payload.Length >= 2 && recorded.Payload.Length >= 2
                        && u.Payload[0] == recorded.Payload[0] && u.Payload[1] == recorded.Payload[1],
                        "IAC SB " + TelnetUnit.Hex(new[] { recorded.Payload[0], recorded.Payload[1] }) + " ...", timeoutMs);
                case TelnetUnitKind.Record:
                    return WaitForRecordAsync(timeoutMs);
                default:
                    return WaitForAsync(u => u.Kind == TelnetUnitKind.Command && u.Payload[0] == recorded.Payload[0],
                        "IAC " + TelnetUnit.Hex(recorded.Payload), timeoutMs);
            }
        }

        /// <summary>
        /// Every record the client sent, in order (3270 bytes, telnet layer removed).
        /// </summary>
        /// <returns>
        /// The records.
        /// </returns>
        public List<byte[]> RecordsReceived()
        {
            var records = new List<byte[]>();
            lock (_received)
            {
                for (int i = 0; i < _received.Count; i++)
                {
                    if (_received[i].Kind == TelnetUnitKind.Record)
                    {
                        records.Add(_received[i].Payload);
                    }
                }
            }

            return records;
        }

        /// <summary>
        /// Every unit the client sent that is not a record, in order, as hex.
        /// </summary>
        /// <returns>
        /// The units.
        /// </returns>
        public List<string> TelnetReceived()
        {
            var units = new List<string>();
            lock (_received)
            {
                for (int i = 0; i < _received.Count; i++)
                {
                    if (_received[i].Kind != TelnetUnitKind.Record)
                    {
                        units.Add(_received[i].ToHex());
                    }
                }
            }

            return units;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            _client?.Dispose();
            try
            {
                await _accept;
            }
            catch (OperationCanceledException)
            {
                // No client came, and the wait was stopped on purpose.
            }
            catch (SocketException)
            {
                // The listener was stopped before a client came.
            }
            catch (ObjectDisposedException)
            {
                // The same, on another path through the runtime.
            }

            if (_reader != null)
            {
                await _reader;
            }

            _stop.Dispose();
            _arrived.Dispose();
        }

        private async Task<TelnetUnit> WaitForRecordAsync(int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                lock (_received)
                {
                    int seen = 0;
                    for (int i = 0; i < _received.Count; i++)
                    {
                        if (_received[i].Kind == TelnetUnitKind.Record)
                        {
                            if (seen == _nextRecord)
                            {
                                _nextRecord++;
                                return _received[i];
                            }

                            seen++;
                        }
                    }
                }

                await WaitForMoreAsync(clock, timeoutMs, "the client's next 3270 record (record " + (_nextRecord + 1) + ")");
            }
        }

        // The first unit not matched before that passes the test.
        private async Task<TelnetUnit> WaitForAsync(Func<TelnetUnit, bool> wanted, string what, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                lock (_received)
                {
                    while (_negotiationUsed.Count < _received.Count)
                    {
                        _negotiationUsed.Add(false);
                    }

                    for (int i = 0; i < _received.Count; i++)
                    {
                        if (!_negotiationUsed[i] && _received[i].Kind != TelnetUnitKind.Record && wanted(_received[i]))
                        {
                            _negotiationUsed[i] = true;
                            return _received[i];
                        }
                    }
                }

                await WaitForMoreAsync(clock, timeoutMs, what);
            }
        }

        // Blocks until the client sends something more, or fails with what was awaited and
        // everything the client has sent.
        private async Task WaitForMoreAsync(Stopwatch clock, int timeoutMs, string what)
        {
            long left = timeoutMs - clock.ElapsedMilliseconds;
            if (left <= 0 || !await _arrived.WaitAsync((int)left, _stop.Token))
            {
                // Masked like every print of what a client sent (see PasswordMask); a replay types
                // only the placeholder, but the rule is one rule everywhere.
                throw new Xunit.Sdk.XunitException(PasswordMask.Text("The client never sent " + what + " within " + timeoutMs + " ms. It sent, in order:\n  " +
                    string.Join("\n  ", Describe())));
            }
        }

        private List<string> Describe()
        {
            var lines = new List<string>();
            lock (_received)
            {
                for (int i = 0; i < _received.Count; i++)
                {
                    string hex = _received[i].ToHex();
                    lines.Add(_received[i].Kind + ": " + (hex.Length > 120 ? hex.Substring(0, 120) + " ..." : hex));
                }
            }

            return lines;
        }

        private async Task ReadAsync(NetworkStream stream)
        {
            byte[] buffer = new byte[8192];
            byte[] rest = Array.Empty<byte>();
            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        return;
                    }

                    byte[] joined = new byte[rest.Length + read];
                    rest.CopyTo(joined, 0);
                    buffer.AsSpan(0, read).CopyTo(joined.AsSpan(rest.Length));
                    var units = new List<TelnetUnit>();
                    TelnetUnit.Split(joined, units, out rest);
                    lock (_received)
                    {
                        _received.AddRange(units);
                    }

                    _arrived.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped on purpose.
            }
            catch (System.IO.IOException)
            {
                // The client closed the connection.
            }
            catch (ObjectDisposedException)
            {
                // The socket was closed under the read on purpose.
            }
        }
    }
}
