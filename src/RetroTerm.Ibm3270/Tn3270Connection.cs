//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// A TN3270 connection over TCP. Each <see cref="DataReceived"/> is ONE complete 3270 record, and
    /// each <see cref="SendAsync"/> sends ONE record; the telnet framing is done by
    /// <see cref="Tn3270Protocol"/>. Pair it with <see cref="Ibm3270Emulator"/>, which expects exactly that.
    /// </summary>
    public sealed class Tn3270Connection : IConnection, IBreakConnection, ITn3270EConnection, IPrintJobEndConnection, IHostRecordResponseConnection
    {
        private readonly string _host;
        private readonly int _port;
        private readonly object _gate = new object();

        // A new protocol object for every connection (CreateProtocol, called from ConnectAsync).
        // One object used to live for the whole life of this class, so a second ConnectAsync
        // started with the first connection's telnet option states, TN3270E and its agreed
        // functions, its sequence counter, half a record and the records still waiting for a
        // response. Measured 23 November 2026 by Tn3270ReconnectTests: after a TN3270E first
        // connection that ended mid-record, a plain TN3270 second connection still reported
        // TN3270E in force and handed up its first record with the stray byte from the first.
        // A fresh object is the whole state of one telnet conversation, and nothing can be
        // missed the way resetting fields one by one could miss a field added later.
        private Tn3270Protocol _protocol;

        // Events the protocol raised while the receive loop held _gate, raised in order once it
        // has let go. The session's handlers post to its pump, which blocks when the pump queue
        // is full, while the pump itself takes _gate for every record (HostRecordStarting,
        // HostRecordProcessed) and for every reply it sends. Raising them under the lock would
        // let a full queue hang both threads. Only the receive loop touches this list.
        private readonly System.Collections.Generic.List<Action> _raiseAfterFeed = new System.Collections.Generic.List<Action>();

        // A plain socket rather than a TcpClient: TcpConnector picks it from every address the host
        // name resolves to (BUGS.md B12), and TcpClient cannot adopt a socket connected elsewhere.
        private Socket? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _receiveCts;

        // 1 once Dispose has run (Interlocked, Dispose can be called from two threads at once).
        private int _disposed;
        private Task? _receiveTask;

        /// <summary>
        /// Gets the TERMINAL-TYPE name this connection answers the host with, e.g. "IBM-3278-4-E".
        /// </summary>
        public string TerminalType { get; }

        /// <summary>
        /// Creates a connection; nothing happens until <see cref="ConnectAsync"/>.
        /// </summary>
        /// <param name="host">
        /// Host name or address.
        /// </param>
        /// <param name="port">
        /// TCP port; TK5 listens on 3270.
        /// </param>
        /// <param name="terminalType">
        /// The TERMINAL-TYPE name sent to the host.
        /// </param>
        public Tn3270Connection(string host, int port = 23, string terminalType = Tn3270Protocol.DefaultTerminalType)
            : this(host, port, terminalType, associateLu: null)
        {
        }

        /// <summary>
        /// Creates a printer connection that asks TN3270E for the partner printer of a display LU
        /// (RFC 2355 section 7.1.3, ASSOCIATE); nothing happens until <see cref="ConnectAsync"/>.
        /// </summary>
        /// <param name="host">
        /// Host name or address.
        /// </param>
        /// <param name="port">
        /// TCP port.
        /// </param>
        /// <param name="terminalType">
        /// The TERMINAL-TYPE name; IBM-3287-1 when <paramref name="associateLu"/> is given.
        /// </param>
        /// <param name="associateLu">
        /// The display's LU name, or null for any printer.
        /// </param>
        public Tn3270Connection(string host, int port, string terminalType, string? associateLu)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Host cannot be empty", nameof(host));
            }

            if (port <= 0 || port > 65535)
            {
                throw new ArgumentException("Port must be between 1 and 65535", nameof(port));
            }

            _host = host;
            _port = port;
            Description = host + ":" + port;
            TerminalType = terminalType;
            AssociateLu = string.IsNullOrWhiteSpace(associateLu) ? null : associateLu.Trim();

            // Built here as well as on every connect, so a bad ASSOCIATE (a display device type,
            // or with an "@" name) is refused when the connection is made, not at the first connect.
            _protocol = CreateProtocol();
        }

        /// <summary>
        /// Gets the display LU whose partner printer this connection asks for (TN3270E ASSOCIATE),
        /// or null.
        /// </summary>
        public string? AssociateLu { get; }

        /// <summary>
        /// Raised when the connection status changes.
        /// </summary>
        public event Action<ConnectionStatus>? StatusChanged;

        /// <summary>
        /// Raised when TN3270E comes into force or ends, on the receive thread.
        /// </summary>
        public event Action<bool>? Tn3270EChanged;

        /// <summary>
        /// Raised when the host ends a 3287 print job: telnet Abort Output in plain TN3270 (see
        /// <see cref="Tn3270Protocol.AbortOutputReceived"/>), or a TN3270E PRINT-EOJ message (RFC
        /// 2355 sections 10.1, 10.2). On the receive thread.
        /// </summary>
        public event Action? EndOfJobReceived;

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force now.
        /// </summary>
        public bool InTn3270E => _protocol.InTn3270E;

        /// <summary>
        /// Gets the LU name the host gave in TN3270E, or null (plain TN3270, or not agreed yet).
        /// </summary>
        public string? GrantedLuName => _protocol.GrantedLuName;

        /// <summary>
        /// Gets a value indicating whether TN3270E runs with the BIND-IMAGE function.
        /// </summary>
        public bool BindImageAgreed => _protocol.BindImageAgreed;

        /// <summary>
        /// Gets a value indicating whether TN3270E runs with the SYSREQ function.
        /// </summary>
        public bool SysReqAgreed => _protocol.SysReqAgreed;

        /// <summary>
        /// Raised for a TN3270E BIND image, UNBIND, SSCP-LU text or NVT text, on the receive
        /// thread, in order with <see cref="DataReceived"/>. The memory is a copy.
        /// </summary>
        public event Action<Tn3270EHostMessage, ReadOnlyMemory<byte>>? HostMessageReceived;

        /// <summary>
        /// Raised with each complete 3270 record from the host, without telnet framing.
        /// </summary>
        public event Action<ReadOnlyMemory<byte>>? DataReceived;

        /// <summary>
        /// Raised when the connection fails.
        /// </summary>
        public event Action<Exception>? ErrorOccurred;

        /// <summary>
        /// The connection status now.
        /// </summary>
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        /// <summary>
        /// The protocol name shown in the UI: TN3270.
        /// </summary>
        public string ConnectionType => "TN3270";

        /// <summary>
        /// The host and port, for the UI.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Opens the TCP connection and starts the TN3270 negotiation.
        /// </summary>
        /// <param name="cancellationToken">
        /// Stops the connect attempt.
        /// </param>
        /// <returns>
        /// A task that completes when the socket is open.
        /// </returns>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (Status != ConnectionStatus.Disconnected)
            {
                throw new InvalidOperationException("Cannot connect when status is " + Status);
            }

            try
            {
                SetStatus(ConnectionStatus.Connecting);

                // A host that closed the last connection left its socket here (the receive loop
                // only stops reading); free it before the new one.
                _stream?.Dispose();
                _client?.Dispose();
                _receiveCts?.Dispose();
                _stream = null;
                _client = null;
                _raiseAfterFeed.Clear();
                lock (_gate)
                {
                    _protocol = CreateProtocol();
                }

                // TcpConnector tries IPv6 and IPv4 together (RFC 8305), so "localhost" against
                // Hercules, which listens on IPv4 only, no longer waits ~2 s for ::1 (B12).
                _client = await TcpConnector.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
                _stream = new NetworkStream(_client, ownsSocket: true);
                _receiveCts = new CancellationTokenSource();
                _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
                SetStatus(ConnectionStatus.Connected);
            }
            catch (Exception ex)
            {
                SetStatus(ConnectionStatus.Error);
                ErrorOccurred?.Invoke(ex);
                throw;
            }
        }

        /// <summary>
        /// Sends one inbound 3270 record (from <see cref="Ibm3270Emulator"/>); IAC EOR is added here.
        /// </summary>
        /// <param name="data">
        /// The record.
        /// </param>
        /// <param name="cancellationToken">
        /// Not used: the write is synchronous under the connection's lock.
        /// </param>
        /// <returns>
        /// A completed task.
        /// </returns>
        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (Status != ConnectionStatus.Connected || _stream == null)
            {
                throw new InvalidOperationException("Cannot send when status is " + Status);
            }

            lock (_gate)
            {
                _protocol.SendRecord(data.Span);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends IAC BRK, which is what the Attn key does in plain TN3270.
        /// </summary>
        public void SendBreak()
        {
            lock (_gate)
            {
                _protocol.SendBreak();
            }
        }

        /// <summary>
        /// Sends the Sys Req key as telnet Abort Output (RFC 2355 10.5.2), when SYSREQ is agreed.
        /// </summary>
        public void SendSysReq()
        {
            bool sent;
            lock (_gate)
            {
                sent = _protocol.SendSysReq();
            }

            if (!sent)
            {
                // x3270 net_abort does nothing in this case either; the log says why.
                ApplicationLogger.Log(LogCategory.Network, LogLevel.Info, "Tn3270Connection",
                    "Sys Req not sent: the TN3270E SYSREQ function is not agreed, or the connection is not in 3270 or SSCP-LU mode (RFC 2355 section 10.5.2)");
            }
        }

        /// <summary>
        /// Sends the Attn key in TN3270E as telnet Interrupt Process (RFC 2355 section 11).
        /// </summary>
        public void SendAttention()
        {
            lock (_gate)
            {
                _protocol.SendInterruptProcess();
            }
        }

        /// <summary>
        /// Gets a value indicating whether TN3270E runs with the RESPONSES function.
        /// </summary>
        public bool ResponsesAgreed => _protocol.ResponsesAgreed;

        /// <summary>
        /// Sends REQUEST ERR-COND-CLEARED (RFC 2355 10.4.1), when the host was told Intervention
        /// Required or Component Disconnected before.
        /// </summary>
        public void SendErrorConditionCleared()
        {
            bool sent;
            lock (_gate)
            {
                sent = _protocol.SendErrorConditionCleared();
            }

            ApplicationLogger.Log(LogCategory.Network, LogLevel.Info, "Tn3270Connection", sent
                ? "Printer ready again: TN3270E REQUEST ERR-COND-CLEARED sent (RFC 2355 section 10.4.1)"
                : "Printer ready again, but no ERR-COND-CLEARED sent: the host was never told Intervention Required or Component Disconnected (RFC 2355 section 10.4.1)");
        }

        /// <summary>
        /// The session starts applying the next host record (TN3270E RESPONSES).
        /// </summary>
        /// <returns>
        /// What the host asked to hear about the record (its RESPONSE-FLAG).
        /// </returns>
        public HostRecordResponseRequest HostRecordStarting()
        {
            lock (_gate)
            {
                return _protocol.HostRecordStarting();
            }
        }

        /// <summary>
        /// The session has applied the record; sends the TN3270E response it asked for, if any.
        /// </summary>
        /// <param name="outcome">
        /// How it went, or null when nothing could say.
        /// </param>
        public void HostRecordProcessed(HostRecordOutcome? outcome)
        {
            if (_stream == null)
            {
                // Gone already (a record still on the pump after the socket closed): no one to
                // answer.
                return;
            }

            lock (_gate)
            {
                _protocol.HostRecordProcessed(outcome);
            }
        }

        /// <summary>
        /// Closes the connection.
        /// </summary>
        /// <returns>
        /// A task that completes when the socket is closed.
        /// </returns>
        public async Task DisconnectAsync()
        {
            if (Status == ConnectionStatus.Disconnected)
            {
                return;
            }

            SetStatus(ConnectionStatus.Disconnecting);
            _receiveCts?.Cancel();
            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected: the loop was cancelled on purpose.
                }
            }

            _stream?.Close();
            _client?.Close();
            _stream = null;
            _client = null;
            SetStatus(ConnectionStatus.Disconnected);
        }

        /// <summary>
        /// Closes the connection and frees the socket.
        /// </summary>
        public void Dispose()
        {
            // Called twice, and from two threads at once, when the host closes the connection
            // while the session is being disconnected: TerminalSession.CleanupConnection runs on
            // the remote-close status event while TerminalSession.DisconnectAsync disposes the
            // same connection in its finally. The second call cancelled a disposed token source
            // and threw ObjectDisposedException (the oracle's ispf-boxes case, two full-suite runs
            // of 24 November 2026). IDisposable says a second Dispose must do nothing, so only the
            // first call gets past here.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _receiveCts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _receiveCts?.Dispose();
        }

        // One protocol object, wired to this connection's events, for one connection.
        private Tn3270Protocol CreateProtocol()
        {
            var protocol = new Tn3270Protocol(WriteToSocket, TerminalType, AssociateLu);
            protocol.RecordReceived += record =>
            {
                byte[] copy = record.ToArray();
                _raiseAfterFeed.Add(() => DataReceived?.Invoke(copy));
            };
            protocol.Tn3270EChanged += on => _raiseAfterFeed.Add(() => Tn3270EChanged?.Invoke(on));
            protocol.AbortOutputReceived += () => _raiseAfterFeed.Add(() => EndOfJobReceived?.Invoke());
            protocol.PrintEndOfJobReceived += () => _raiseAfterFeed.Add(() => EndOfJobReceived?.Invoke());
            protocol.HostMessageReceived += OnProtocolHostMessage;
            protocol.ProtocolError += message =>
            {
                ApplicationLogger.Log(LogCategory.Network, LogLevel.Error, "Tn3270Connection", message);
                ErrorOccurred?.Invoke(new InvalidOperationException(message));
            };
            return protocol;
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (!cancellationToken.IsCancellationRequested && _stream != null)
                {
                    int read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        ApplicationLogger.Log(LogCategory.Network, LogLevel.Info, "Tn3270Connection",
                            "Read 0 bytes - connection closed by remote host");
                        break;
                    }

                    lock (_gate)
                    {
                        _protocol.Feed(buffer.AsSpan(0, read));
                    }

                    RaiseEventsFromFeed();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                SetStatus(ConnectionStatus.Disconnected);
            }
        }

        // A host message other than 3270 data: copied, because the protocol's memory is only valid
        // during the call, and raised after the feed with the records, in order.
        private void OnProtocolHostMessage(Tn3270EHostMessage kind, ReadOnlyMemory<byte> data)
        {
            byte[] copy = data.ToArray();
            _raiseAfterFeed.Add(() => HostMessageReceived?.Invoke(kind, copy));
        }

        // Raises what the last Feed produced, in the order it was produced, outside _gate.
        private void RaiseEventsFromFeed()
        {
            for (int i = 0; i < _raiseAfterFeed.Count; i++)
            {
                _raiseAfterFeed[i]();
            }

            _raiseAfterFeed.Clear();
        }

        private void WriteToSocket(ReadOnlyMemory<byte> bytes)
        {
            // Called with _gate held, from the receive loop (negotiation replies) or SendAsync.
            NetworkStream? stream = _stream;
            if (stream == null)
            {
                throw new InvalidOperationException("Not connected.");
            }

            stream.Write(bytes.Span);
        }

        private void SetStatus(ConnectionStatus status)
        {
            if (Status == status)
            {
                return;
            }

            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
