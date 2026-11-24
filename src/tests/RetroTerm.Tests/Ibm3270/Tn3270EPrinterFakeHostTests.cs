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
using System.Threading.Tasks;

using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// A whole 3287 printer session over TN3270E against a host in this process: the real
    /// <see cref="Tn3270Connection"/> on a loopback socket, a <see cref="TerminalSession"/> and the
    /// <see cref="Ibm3287PrinterEmulator"/>, from DO TN3270E to the written job.
    /// </summary>
    /// <remarks>
    /// The host follows RFC 2355 step by step, waiting for each client message before sending the
    /// next, as section 13.4's partner-printer example does: DO TN3270E, SEND DEVICE-TYPE, DEVICE-TYPE
    /// IS IBM-3287-1 CONNECT with the printer's name, FUNCTIONS IS with the list the client asked
    /// for, one SCS-DATA record asking for a response (10.4), then PRINT-EOJ (10.1). It is a test
    /// host written from the RFC; no real TN3270E server has been talked to.
    /// </remarks>
    public class Tn3270EPrinterFakeHostTests
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task APartnerPrinterPrintsAnScsJobAndAnswersTheHost()
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var printer = new Ibm3287PrinterEmulator();
            var output = new CapturingOutput();
            printer.Configure(output, 0);
            var session = new TerminalSession(printer, "Test");
            var connection = new Tn3270Connection("127.0.0.1", port, Tn3270Protocol.PrinterDeviceType, "TERM0001");

            Task<Socket> accept = listener.AcceptSocketAsync(token).AsTask();
            await session.ConnectAsync(connection, token);
            using Socket host = await accept.WaitAsync(Deadline, token);
            var reader = new HostReader(host);

            await host.SendAsync(Tn3270EPrinterTests.Bytes("FF FD 28"), token);
            await reader.WaitForAsync(Tn3270EPrinterTests.Bytes("FF FB 28"), token);

            await host.SendAsync(Tn3270EPrinterTests.Bytes("FF FA 28 08 02 FF F0"), token);
            await reader.WaitForAsync(Tn3270EPrinterTests.Bytes("FF FA 28 02 07", "'IBM-3287-1", "00", "'TERM0001", "FF F0"), token);

            await host.SendAsync(Tn3270EPrinterTests.Bytes("FF FA 28 02 04", "'IBM-3287-1", "01", "'PRT0001", "FF F0"), token);
            byte[] functions = Tn3270EPrinterTests.Bytes("FF FA 28 03 07 01 02 03 FF F0");
            await reader.WaitForAsync(functions, token);

            // FUNCTIONS IS with the list exactly as received (7.2.1).
            byte[] accepted = (byte[])functions.Clone();
            accepted[4] = 0x04;
            await host.SendAsync(accepted, token);

            // SCS-DATA, ALWAYS-RESPONSE, SEQ-NUMBER 0: "HI" NL "HI".
            await host.SendAsync(Tn3270EPrinterTests.Bytes("01 00 02 00 00 C8 C9 15 C8 C9 FF EF"), token);
            await reader.WaitForAsync(Tn3270EPrinterTests.Bytes("02 00 00 00 00 00 FF EF"), token);

            // PRINT-EOJ.
            await host.SendAsync(Tn3270EPrinterTests.Bytes("08 00 00 00 00 FF EF"), token);
            PrintJob job = await output.Written.Task.WaitAsync(Deadline, token);
            await session.FlushAsync();

            Assert.Equal("HI" + "\n" + "HI" + "\n", job.ToText("\n"));
            Assert.True(printer.InTn3270E);
            Assert.Equal("the host (TN3270E PRINT-EOJ)", Assert.Single(printer.Jobs).EndedBy);
            Assert.Null(printer.LastProblem);

            await session.DisconnectAsync();
        }

        [Fact]
        public async Task ANotReadyPrinterAnswersInterventionRequiredThenSendsErrCondCleared()
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            DateTime now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            var printer = new Ibm3287PrinterEmulator(() => now);
            var output = new FailingThenCapturingOutput();
            printer.Configure(output, 0);
            var session = new TerminalSession(printer, "Test");
            var connection = new Tn3270Connection("127.0.0.1", port, Tn3270Protocol.PrinterDeviceType, null);

            Task<Socket> accept = listener.AcceptSocketAsync(token).AsTask();
            await session.ConnectAsync(connection, token);
            using Socket host = await accept.WaitAsync(Deadline, token);
            var reader = new HostReader(host);

            await host.SendAsync(Tn3270EPrinterTests.Bytes("FF FD 28"), token);
            await reader.WaitForAsync(Tn3270EPrinterTests.Bytes("FF FB 28"), token);
            await host.SendAsync(Tn3270EPrinterTests.Bytes("FF FA 28 08 02 FF F0"), token);
            await reader.WaitForAsync(Tn3270EPrinterTests.Bytes("FF FA 28 02 07", "'IBM-3287-1", "FF F0"), token);
            await host.SendAsync(Tn3270EPrinterTests.Bytes("FF FA 28 02 04", "'IBM-3287-1", "01", "'PRT0001", "FF F0"), token);
            byte[] functions = Tn3270EPrinterTests.Bytes("FF FA 28 03 07 01 02 03 FF F0");
            await reader.WaitForAsync(functions, token);
            byte[] accepted = (byte[])functions.Clone();
            accepted[4] = 0x04;
            await host.SendAsync(accepted, token);

            // A job, ERROR-RESPONSE (nothing comes back when it goes well), then PRINT-EOJ. The
            // output refuses it: the printer is now not ready.
            await host.SendAsync(Tn3270EPrinterTests.Bytes("01 00 01 00 00 C8 C9 FF EF"), token);
            await host.SendAsync(Tn3270EPrinterTests.Bytes("08 00 00 00 00 FF EF"), token);
            await output.FirstAttempt.Task.WaitAsync(Deadline, token);
            await session.FlushAsync();
            Assert.False(printer.IsReady);

            // The next record, ALWAYS-RESPONSE with SEQ-NUMBER 1: NEGATIVE-RESPONSE, status X'01'
            // Intervention Required (RFC 2355 10.4.1).
            await host.SendAsync(Tn3270EPrinterTests.Bytes("01 00 02 00 01 C1 FF EF"), token);
            await reader.WaitForAsync(Tn3270EPrinterTests.Bytes("02 00 01 00 01 01 FF EF"), token);

            // The folder works again; the next retry writes the job and the client sends REQUEST
            // ERR-COND-CLEARED: header 06 00 00 00 00, no data portion, IAC EOR.
            output.Fails = false;
            now = now.AddSeconds(Ibm3287PrinterEmulator.NotReadyRetrySeconds);
            await session.RunOnSessionThreadAsync(() => printer.RetryWhenNotReady(now));
            await reader.WaitForAsync(Tn3270EPrinterTests.Bytes("06 00 00 00 00 FF EF"), token);

            PrintJob job = await output.Written.Task.WaitAsync(Deadline, token);
            Assert.Equal("HI" + "\n", job.ToText("\n"));
            Assert.True(printer.IsReady);

            await session.DisconnectAsync();
        }

        // Collects what the client sends and waits, with a deadline, until a byte sequence has
        // arrived. Never a sleep: each wait ends when the bytes are there.
        private sealed class HostReader
        {
            private readonly Socket _socket;
            private readonly List<byte> _received = new List<byte>();
            private readonly byte[] _buffer = new byte[4096];

            public HostReader(Socket socket)
            {
                _socket = socket;
            }

            public async Task WaitForAsync(byte[] expected, CancellationToken token)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(Deadline);
                while (IndexOf(expected) < 0)
                {
                    int read = await _socket.ReceiveAsync(_buffer, SocketFlags.None, timeout.Token);
                    if (read == 0)
                    {
                        throw new InvalidOperationException("The client closed before sending " + Convert.ToHexString(expected));
                    }

                    for (int i = 0; i < read; i++)
                    {
                        _received.Add(_buffer[i]);
                    }
                }

                // Consume up to and including the match, so the next wait looks at new bytes.
                _received.RemoveRange(0, IndexOf(expected) + expected.Length);
            }

            private int IndexOf(byte[] expected)
            {
                for (int start = 0; start + expected.Length <= _received.Count; start++)
                {
                    int k = 0;
                    while (k < expected.Length && _received[start + k] == expected[k])
                    {
                        k++;
                    }

                    if (k == expected.Length)
                    {
                        return start;
                    }
                }

                return -1;
            }
        }

        // Refuses jobs while Fails is set (a read-only folder), then keeps the first one written.
        private sealed class FailingThenCapturingOutput : IPrintJobOutput
        {
            public volatile bool Fails = true;

            public TaskCompletionSource FirstAttempt { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<PrintJob> Written { get; } = new TaskCompletionSource<PrintJob>(TaskCreationOptions.RunContinuationsAsynchronously);

            public string Description => "a test";

            public string Write(PrintJob job, int number, DateTime localTime)
            {
                FirstAttempt.TrySetResult();
                if (Fails)
                {
                    throw new UnauthorizedAccessException("folder is read-only");
                }

                Written.TrySetResult(job);
                return "job " + number;
            }
        }

        private sealed class CapturingOutput : IPrintJobOutput
        {
            public TaskCompletionSource<PrintJob> Written { get; } = new TaskCompletionSource<PrintJob>(TaskCreationOptions.RunContinuationsAsynchronously);

            public string Description => "a test";

            public string Write(PrintJob job, int number, DateTime localTime)
            {
                Written.TrySetResult(job);
                return "job " + number;
            }
        }
    }
}
