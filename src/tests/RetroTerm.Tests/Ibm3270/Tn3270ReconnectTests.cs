//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// A <see cref="Tn3270Connection"/> connected a second time must start the second connection
    /// exactly as a new one would: nothing from the first (telnet option states, TN3270E and its
    /// agreed functions, the sequence counter, half a record, records still waiting for a
    /// response) may reach the second.
    /// </summary>
    /// <remarks>
    /// A real socket on 127.0.0.1 plays the host. The first connection agrees TN3270E with
    /// RESPONSES, sends one record that asks for a response, then half a record, then closes. The
    /// second is plain TN3270, the TK5 handshake of docs\spec\IBM3270\TN3270-NEGOTIATION.md section
    /// 3. What the client sends on the second connection is compared byte for byte with what a new
    /// connection sends to the same host bytes.
    /// </remarks>
    public class Tn3270ReconnectTests
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

        // First connection: DO TN3270E, SEND DEVICE-TYPE, DEVICE-TYPE IS, FUNCTIONS IS RESPONSES
        // (RFC 2355 7.1, 7.2), then a 3270-DATA record with RESPONSE-FLAG ALWAYS (X'02') and
        // SEQ-NUMBER 1, carrying Write with WCC X'C3'.
        private static readonly byte[] FirstHost = Join(
            "FF FD 28",
            "FF FA 28 08 02 FF F0",
            "FF FA 28 02 04 49 42 4D 2D 33 32 37 38 2D 32 2D 45 01 54 43 50 30 30 30 33 34 FF F0",
            "FF FA 28 03 04 02 FF F0",
            "00 00 02 00 01 F1 C3 FF EF");

        // Half a record: a header and one data byte, no IAC EOR. The host then closes.
        private static readonly byte[] FirstHostHalfRecord = Join("00 00 00 00 02 F5");

        // Second connection: the plain TN3270 TK5 handshake and an Erase/Write record, the same
        // bytes Tn3270ProtocolTests uses.
        private static readonly byte[] SecondHost = Join(
            "FF FD 18",
            "FF FA 18 01 FF F0",
            "FF FB 00",
            "FF FD 00",
            "FF FB 19 FF FD 19",
            "F5 42 11 40 40 1D 60 C8 85 FF EF");

        private static readonly byte[] SecondRecord = Join("F5 42 11 40 40 1D 60 C8 85");

        private static byte[] Join(params string[] hex)
        {
            var all = new List<byte>();
            for (int i = 0; i < hex.Length; i++)
            {
                all.AddRange(Convert.FromHexString(hex[i].Replace(" ", string.Empty, StringComparison.Ordinal)));
            }

            return all.ToArray();
        }

        [Fact]
        public async Task AReconnectedConnectionSendsWhatAFreshConnectionSends()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // The fresh connection first: what the second host's bytes should get back.
            (byte[] freshBytes, byte[] freshRecord) = await RunSecondConnectionAsync(
                listener, new Tn3270Connection("127.0.0.1", port), firstConnectionFirst: false);

            (byte[] reconnectedBytes, byte[] reconnectedRecord) = await RunSecondConnectionAsync(
                listener, new Tn3270Connection("127.0.0.1", port), firstConnectionFirst: true);

            Assert.Equal(Convert.ToHexString(freshBytes), Convert.ToHexString(reconnectedBytes));
            Assert.Equal(SecondRecord, freshRecord);
            Assert.Equal(SecondRecord, reconnectedRecord);
        }

        [Fact]
        public void AResetBeforeAReconnectUnlocksTheKeyboard()
        {
            // MainWindow.ConnectToHostCore calls Emulator.Reset() when a tab is reconnected with
            // the same emulator. A keyboard left locked by the dropped session (Enter sent, no
            // answer before the line went) must not still be locked on the new one.
            var emulator = new Ibm3270Emulator();
            emulator.ProcessData(Join("F5 C3 11 40 40 1D 40"));
            Assert.Equal(NamedKeyResult.Pressed, emulator.PressNamedKey("ENTER", out _));
            Assert.NotNull(emulator.KeyboardLockReason);

            emulator.Reset();

            Assert.Null(emulator.KeyboardLockReason);
        }

        // Runs the first connection (when asked) and then the second on the same object, and
        // returns what the client sent on the second, and the record it handed up there.
        private static async Task<(byte[] Bytes, byte[] Record)> RunSecondConnectionAsync(
            TcpListener listener, Tn3270Connection connection, bool firstConnectionFirst)
        {
            var records = Channel.CreateUnbounded<byte[]>();
            var statuses = Channel.CreateUnbounded<ConnectionStatus>();
            connection.DataReceived += data => records.Writer.TryWrite(data.ToArray());
            connection.StatusChanged += status => statuses.Writer.TryWrite(status);

            try
            {
                if (firstConnectionFirst)
                {
                    Task<Socket> accept1 = listener.AcceptSocketAsync();
                    await connection.ConnectAsync();
                    using Socket host1 = await accept1.WaitAsync(Deadline);
                    host1.Send(FirstHost);

                    // The record asking for a response arrives; it is left unanswered on purpose,
                    // as a record still on the session's pump would be when the line drops.
                    byte[] first = await records.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
                    Assert.Equal(Join("F1 C3"), first);
                    Assert.True(connection.InTn3270E);

                    host1.Send(FirstHostHalfRecord);
                    host1.Shutdown(SocketShutdown.Send);
                    await WaitForStatusAsync(statuses, ConnectionStatus.Disconnected);
                }

                Task<Socket> accept2 = listener.AcceptSocketAsync();
                await connection.ConnectAsync();
                using Socket host2 = await accept2.WaitAsync(Deadline);
                host2.Send(SecondHost);

                byte[] record = await records.Reader.ReadAsync().AsTask().WaitAsync(Deadline);

                // The second host never offered TN3270E, so it is not in force.
                Assert.False(connection.InTn3270E);
                Assert.Null(connection.GrantedLuName);

                // What the session does with every record (TerminalSession.ProcessHostRecord).
                connection.HostRecordStarting();
                connection.HostRecordProcessed(null);

                await connection.DisconnectAsync();
                byte[] sent = await ReadToEndAsync(host2);
                return (sent, record);
            }
            finally
            {
                connection.Dispose();
            }
        }

        private static async Task WaitForStatusAsync(Channel<ConnectionStatus> statuses, ConnectionStatus wanted)
        {
            while (true)
            {
                ConnectionStatus status = await statuses.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
                if (status == wanted)
                {
                    return;
                }
            }
        }

        private static async Task<byte[]> ReadToEndAsync(Socket socket)
        {
            var all = new List<byte>();
            byte[] buffer = new byte[4096];
            while (true)
            {
                int read = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None).AsTask().WaitAsync(Deadline);
                if (read == 0)
                {
                    return all.ToArray();
                }

                for (int i = 0; i < read; i++)
                {
                    all.Add(buffer[i]);
                }
            }
        }
    }
}
