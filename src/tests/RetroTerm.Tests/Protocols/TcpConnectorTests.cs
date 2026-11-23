//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using Xunit;

namespace RetroTerm.Tests.Protocols;

/// <summary>
/// TcpConnector, the one outgoing TCP connect: every address a name resolves to is tried, IPv6
/// and IPv4 together with a short head start each (RFC 8305), and the first that answers is kept.
/// </summary>
/// <remarks>
/// <para>
/// BUGS.md B12: "localhost" resolves to ::1 first, Hercules listens on IPv4 only, and Windows
/// reports the refused ::1 connect only after about 2 seconds. A connect that tries the addresses
/// one after the other therefore took about 2 s every time. These tests use real listeners on the
/// loopback addresses, so they see what the operating system really does.
/// </para>
/// <para>
/// The timing asserts are NOT sleep-then-assert: each one awaits the connect itself to its end and
/// then reads a stopwatch. The bound (500 ms) is a quarter of the 2 s the old connect took, and
/// five times the 100 ms head start the new one gives ::1 before it tries 127.0.0.1, so a slow
/// machine has room and the old behaviour cannot pass.
/// </para>
/// </remarks>
public class TcpConnectorTests
{
    private const int FastEnoughMs = 500;

    [Fact]
    public async Task LocalhostResolvesToBothIPv6AndIPv4OnThisMachine()
    {
        // The other tests here lean on this. Measured 23 November 2026: ::1 first, then 127.0.0.1.
        IPAddress[] addresses = await Dns.GetHostAddressesAsync("localhost", TestContext.Current.CancellationToken);

        Assert.True(Array.IndexOf(addresses, IPAddress.IPv6Loopback) >= 0, "localhost does not resolve to ::1 here");
        Assert.True(Array.IndexOf(addresses, IPAddress.Loopback) >= 0, "localhost does not resolve to 127.0.0.1 here");
    }

    [Fact]
    public async Task LocalhostConnectsFastWhenOnlyIPv4Listens()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var clock = Stopwatch.StartNew();
            using Socket socket = await TcpConnector.ConnectAsync("localhost", port, TestContext.Current.CancellationToken);
            long elapsed = clock.ElapsedMilliseconds;

            Assert.True(elapsed < FastEnoughMs, "connecting to localhost took " + elapsed + " ms");
            Assert.Equal(IPAddress.Loopback, ((IPEndPoint)socket.RemoteEndPoint!).Address);

            // Exactly one connection reached the listener.
            using Socket accepted = await listener.AcceptSocketAsync(TestContext.Current.CancellationToken);
            Assert.False(listener.Pending(), "a second connection reached the listener");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task IPv6IsKeptWhenBothFamiliesListenAndTheOtherAttemptLeavesNothingOpen()
    {
        (TcpListener v6, TcpListener v4, int port) = StartOnBothLoopbacks();
        try
        {
            using Socket socket = await TcpConnector.ConnectAsync("localhost", port, TestContext.Current.CancellationToken);

            // RFC 8305 section 4: ::1 is first in the system's order, so it gets the head start
            // and wins when it answers inside it.
            Assert.Equal(IPAddress.IPv6Loopback, ((IPEndPoint)socket.RemoteEndPoint!).Address);

            using Socket accepted = await v6.AcceptSocketAsync(TestContext.Current.CancellationToken);
            Assert.False(v6.Pending(), "a second connection reached the IPv6 listener");

            // Every other attempt is cancelled before ConnectAsync returns, so nothing can arrive
            // on the IPv4 listener afterwards; if one had been made, it must already be closed.
            if (v4.Pending())
            {
                using Socket loser = v4.AcceptSocket();
                loser.ReceiveTimeout = 2000;
                byte[] buffer = new byte[1];
                Assert.Equal(0, loser.Receive(buffer));
            }
        }
        finally
        {
            v6.Stop();
            v4.Stop();
        }
    }

    [Fact]
    public async Task WhenNothingAnswersTheErrorNamesEveryAddressTried()
    {
        int port = FreePort();

        TcpConnectException error = await Assert.ThrowsAsync<TcpConnectException>(
            () => TcpConnector.ConnectAsync("localhost", port, TestContext.Current.CancellationToken));

        Assert.Contains("[::1]:" + port, error.Message);
        Assert.Contains("127.0.0.1:" + port, error.Message);
    }

    [Fact]
    public async Task ANameThatDoesNotResolveSaysSo()
    {
        TcpConnectException error = await Assert.ThrowsAsync<TcpConnectException>(
            () => TcpConnector.ConnectAsync("no-such-host.invalid", 23, TestContext.Current.CancellationToken));

        Assert.Contains("did not resolve", error.Message);
    }

    [Fact]
    public async Task CancellingStopsTheConnectAtOnce()
    {
        // Nothing listens, and on Windows a refused loopback connect takes about 2 s to report, so
        // both attempts are still running when the token fires at 100 ms.
        int port = FreePort();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(100);

        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TcpConnector.ConnectAsync("localhost", port, cancel.Token));
        long elapsed = clock.ElapsedMilliseconds;

        Assert.True(elapsed < FastEnoughMs, "the cancelled connect returned after " + elapsed + " ms");
    }

    [Fact]
    public void TheFamiliesAreInterleavedStartingWithTheFirst()
    {
        IPAddress v6a = IPAddress.Parse("2001:db8::1");
        IPAddress v6b = IPAddress.Parse("2001:db8::2");
        IPAddress v4a = IPAddress.Parse("192.0.2.1");
        IPAddress v4b = IPAddress.Parse("192.0.2.2");

        IPAddress[] order = TcpConnector.Interleave(new[] { v6a, v6b, v4a, v4b });

        Assert.Equal(new[] { v6a, v4a, v6b, v4b }, order);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static (TcpListener V6, TcpListener V4, int Port) StartOnBothLoopbacks()
    {
        // Take a free IPv4 port and open ::1 on the same number. Another program can take the
        // number in between, so try a few.
        for (int tries = 0; tries < 10; tries++)
        {
            var v4 = new TcpListener(IPAddress.Loopback, 0);
            v4.Start();
            int port = ((IPEndPoint)v4.LocalEndpoint).Port;
            var v6 = new TcpListener(IPAddress.IPv6Loopback, port);
            try
            {
                v6.Start();
                return (v6, v4, port);
            }
            catch (SocketException)
            {
                v4.Stop();
            }
        }

        throw new InvalidOperationException("could not open ::1 and 127.0.0.1 on the same port");
    }
}
