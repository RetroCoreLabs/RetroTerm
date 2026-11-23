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
    public sealed class Tn3270Connection : IConnection, IBreakConnection, ITn3270EConnection
    {
        private readonly string _host;
        private readonly int _port;
        private readonly object _gate = new object();
        private readonly Tn3270Protocol _protocol;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _receiveCts;
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
            _protocol = new Tn3270Protocol(WriteToSocket, terminalType);
            _protocol.RecordReceived += record => DataReceived?.Invoke(record.ToArray());
            _protocol.Tn3270EChanged += on => Tn3270EChanged?.Invoke(on);
            _protocol.ProtocolError += message =>
            {
                ApplicationLogger.Log(LogCategory.Network, LogLevel.Error, "Tn3270Connection", message);
                ErrorOccurred?.Invoke(new InvalidOperationException(message));
            };
        }

        /// <summary>
        /// Raised when the connection status changes.
        /// </summary>
        public event Action<ConnectionStatus>? StatusChanged;

        /// <summary>
        /// Raised when TN3270E comes into force or ends, on the receive thread.
        /// </summary>
        public event Action<bool>? Tn3270EChanged;

        /// <summary>
        /// Gets a value indicating whether TN3270E is in force now.
        /// </summary>
        public bool InTn3270E => _protocol.InTn3270E;

        /// <summary>
        /// Gets the LU name the host gave in TN3270E, or null (plain TN3270, or not agreed yet).
        /// </summary>
        public string? GrantedLuName => _protocol.GrantedLuName;

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
                _client = new TcpClient();
                await _client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
                _stream = _client.GetStream();
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
            _receiveCts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _receiveCts?.Dispose();
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
