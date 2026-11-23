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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols;

/// <summary>
/// The one place RetroTerm opens an outgoing TCP connection to a host name. It tries every
/// address the name resolves to, IPv6 and IPv4 alike, with a short head start for each one, keeps
/// the first that answers and closes the rest ("Happy Eyeballs", RFC 8305).
/// </summary>
/// <remarks>
/// <para>
/// Why this exists (BUGS.md B12, measured 23 November 2026): "localhost" resolves to ::1 first and
/// 127.0.0.1 second on Windows. Hercules (TK5) listens on IPv4 only, and Windows answers a refused
/// connect to ::1 only after about 2 seconds, because it retries the refused attempt. A plain
/// TcpClient.ConnectAsync tries the addresses one after the other, so every connection to
/// "localhost:3270" or "localhost:3505" took about 2 s, while "127.0.0.1" took under a millisecond.
/// </para>
/// <para>
/// What is taken from RFC 8305, by section:
/// </para>
/// <para>
/// Section 3 (resolution): the name is resolved with one Dns.GetHostAddressesAsync call, which
/// returns both families together, so there is no "Resolution Delay" to wait out - every address
/// is known before the first attempt starts.
/// </para>
/// <para>
/// Section 4 (sorting): the operating system's order is kept (it already sorts by RFC 6724 rules)
/// and the families are interleaved, starting with the family of the first address. That is the
/// "First Address Family Count" of 1 that section 8 recommends.
/// </para>
/// <para>
/// Section 5 (attempts): one attempt starts, and the next starts when the attempt delay has passed
/// or when an earlier attempt has failed, whichever is first. The delay is 100 ms, the minimum the
/// RFC recommends (section 8; 250 ms is its default). Ronny's decision on 23 November 2026 was
/// "connect to every address at the same time"; 100 ms keeps that close to "at the same time" for
/// a person waiting on a connect, while it still gives the first address (IPv6, when the system
/// lists it first) the head start the RFC uses to prefer it when both answer. Starting the next
/// attempt early when one fails is not spelled out in RFC 8305; it is done here because waiting
/// after a definite refusal gains nothing. When one attempt succeeds, every other attempt is
/// cancelled and its socket closed, and addresses not yet tried are dropped, as section 5 says.
/// </para>
/// </remarks>
public static class TcpConnector
{
    /// <summary>
    /// The head start each attempt gets before the next address is tried, in milliseconds. RFC 8305
    /// section 8 recommends at least 100 ms and forbids less than 10 ms.
    /// </summary>
    public const int ConnectionAttemptDelayMs = 100;

    /// <summary>
    /// Connects to <paramref name="host"/>:<paramref name="port"/> and returns the connected socket.
    /// </summary>
    /// <param name="host">
    /// A host name or an IP address literal.
    /// </param>
    /// <param name="port">
    /// The TCP port.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the resolve and every attempt still running; the call then throws
    /// OperationCanceledException and no socket is left open.
    /// </param>
    /// <returns>
    /// The connected socket. The caller owns it.
    /// </returns>
    /// <exception cref="TcpConnectException">
    /// Every address failed, or the name did not resolve. The message names each address tried and
    /// why it failed.
    /// </exception>
    public static Task<Socket> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        return ConnectAsync(host, port, ConnectionAttemptDelayMs, cancellationToken);
    }

    /// <summary>
    /// Connects to <paramref name="host"/>:<paramref name="port"/> with a chosen head start per
    /// attempt. Exists for the tests; everything else uses the overload without the delay.
    /// </summary>
    /// <param name="host">
    /// A host name or an IP address literal.
    /// </param>
    /// <param name="port">
    /// The TCP port.
    /// </param>
    /// <param name="attemptDelayMs">
    /// How long each attempt runs alone before the next address is tried. 10 ms or more (RFC 8305
    /// section 5).
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the resolve and every attempt still running.
    /// </param>
    /// <returns>
    /// The connected socket. The caller owns it.
    /// </returns>
    public static async Task<Socket> ConnectAsync(string host, int port, int attemptDelayMs, CancellationToken cancellationToken)
    {
        if (host == null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        if (attemptDelayMs < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptDelayMs), "RFC 8305 section 5: attempts must be at least 10 ms apart");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string where = host + ":" + port.ToString(CultureInfo.InvariantCulture);

        IPAddress[] resolved;
        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            resolved = new[] { literal };
        }
        else
        {
            try
            {
                // netstandard2.1 has no resolve that takes a token, so a cancel stops the wait;
                // the lookup itself finishes in the background and its answer is dropped.
                Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);
                await WaitOrCancelAsync(lookup, cancellationToken).ConfigureAwait(false);
                resolved = await lookup.ConfigureAwait(false);
            }
            catch (SocketException e)
            {
                throw new TcpConnectException("could not connect to " + where + ": the name did not resolve (" + e.Message + ")", e);
            }
        }

        if (resolved.Length == 0)
        {
            throw new TcpConnectException("could not connect to " + where + ": the name resolved to no address", null);
        }

        IPAddress[] order = Interleave(resolved);
        return await RaceAsync(where, order, port, attemptDelayMs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// RFC 8305 section 4: keep the system's order inside each family, and alternate the families,
    /// starting with the family of the first address.
    /// </summary>
    /// <param name="addresses">
    /// The addresses in the order the resolver gave them.
    /// </param>
    /// <returns>
    /// The order the attempts are made in.
    /// </returns>
    internal static IPAddress[] Interleave(IPAddress[] addresses)
    {
        AddressFamily first = addresses[0].AddressFamily;
        var same = new List<IPAddress>(addresses.Length);
        var other = new List<IPAddress>(addresses.Length);
        for (int i = 0; i < addresses.Length; i++)
        {
            if (addresses[i].AddressFamily == first)
            {
                same.Add(addresses[i]);
            }
            else
            {
                other.Add(addresses[i]);
            }
        }

        var result = new IPAddress[addresses.Length];
        int n = 0;
        int a = 0;
        int b = 0;
        while (a < same.Count || b < other.Count)
        {
            if (a < same.Count)
            {
                result[n++] = same[a++];
            }

            if (b < other.Count)
            {
                result[n++] = other[b++];
            }
        }

        return result;
    }

    private static async Task<Socket> RaceAsync(string where, IPAddress[] order, int port, int attemptDelayMs, CancellationToken cancellationToken)
    {
        var sockets = new Socket?[order.Length];
        var attempts = new Task?[order.Length];
        var failures = new string?[order.Length];
        int started = 0;
        int finished = 0;
        Socket? winner = null;

        // The pieces we wait on: every running attempt, the head-start timer and the caller's token.
        var waitOn = new List<Task>(order.Length + 2);
        using var stopTimers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task cancelled = Task.Delay(Timeout.Infinite, stopTimers.Token);

        try
        {
            StartAttempt(order, port, sockets, attempts, started);
            started++;
            Task headStart = Task.Delay(attemptDelayMs, stopTimers.Token);

            while (winner == null)
            {
                waitOn.Clear();
                for (int i = 0; i < started; i++)
                {
                    if (failures[i] == null && attempts[i] != null)
                    {
                        waitOn.Add(attempts[i]!);
                    }
                }

                if (started < order.Length)
                {
                    waitOn.Add(headStart);
                }

                waitOn.Add(cancelled);
                Task done = await Task.WhenAny(waitOn).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (done == headStart)
                {
                    // Section 5: the head start is over, try the next address as well.
                    StartAttempt(order, port, sockets, attempts, started);
                    started++;
                    headStart = Task.Delay(attemptDelayMs, stopTimers.Token);
                    continue;
                }

                int index = Array.IndexOf(attempts, done);
                if (done.Status == TaskStatus.RanToCompletion)
                {
                    winner = sockets[index];
                    sockets[index] = null;
                    break;
                }

                failures[index] = Describe(done.Exception);
                finished++;
                if (started < order.Length)
                {
                    // A definite failure: no reason to let the head start run out.
                    StartAttempt(order, port, sockets, attempts, started);
                    started++;
                    headStart = Task.Delay(attemptDelayMs, stopTimers.Token);
                }
                else if (finished == order.Length)
                {
                    throw new TcpConnectException(BuildFailureMessage(where, order, port, failures), null);
                }
            }

            return winner!;
        }
        finally
        {
            stopTimers.Cancel();

            // Section 5: every attempt that has not won is cancelled. Closing the socket is how a
            // socket connect is cancelled on netstandard2.1; it also closes a loser that happened
            // to connect in the same moment as the winner, so the server sees it close at once.
            for (int i = 0; i < started; i++)
            {
                Socket? loser = sockets[i];
                if (loser != null)
                {
                    loser.Dispose();
                }

                Task? attempt = attempts[i];
                if (attempt != null)
                {
                    // Read the exception of an attempt nobody awaits, so it is not reported as an
                    // unobserved task exception later.
                    _ = attempt.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
    }

    private static void StartAttempt(IPAddress[] order, int port, Socket?[] sockets, Task?[] attempts, int index)
    {
        var socket = new Socket(order[index].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        sockets[index] = socket;
        try
        {
            attempts[index] = socket.ConnectAsync(new IPEndPoint(order[index], port));
        }
        catch (Exception e)
        {
            // A connect that fails before it starts (for example, no IPv6 on this machine) is a
            // failed attempt like any other.
            attempts[index] = Task.FromException(e);
        }
    }

    private static async Task WaitOrCancelAsync(Task task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            await task.ConfigureAwait(false);
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task cancelled = Task.Delay(Timeout.Infinite, stop.Token);
        Task done = await Task.WhenAny(task, cancelled).ConfigureAwait(false);
        stop.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        await done.ConfigureAwait(false);
    }

    private static string Describe(AggregateException? error)
    {
        if (error == null)
        {
            return "failed";
        }

        Exception inner = error.InnerExceptions.Count == 1 ? error.InnerExceptions[0] : error;
        if (inner is SocketException socketError)
        {
            return socketError.Message + " (" + socketError.SocketErrorCode.ToString() + ")";
        }

        return inner.Message;
    }

    private static string BuildFailureMessage(string where, IPAddress[] order, int port, string?[] failures)
    {
        var text = new StringBuilder();
        text.Append("could not connect to ").Append(where).Append(": tried ");
        for (int i = 0; i < order.Length; i++)
        {
            if (i > 0)
            {
                text.Append("; ");
            }

            text.Append(new IPEndPoint(order[i], port).ToString()).Append(" - ").Append(failures[i] ?? "not tried");
        }

        return text.ToString();
    }
}

/// <summary>
/// A TCP connect that failed on every address the host name resolved to, or a name that did not
/// resolve. The message names each address tried and why it failed.
/// </summary>
public sealed class TcpConnectException : IOException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">
    /// What failed, naming every address tried.
    /// </param>
    /// <param name="inner">
    /// The resolver's own error when the name did not resolve; otherwise null.
    /// </param>
    public TcpConnectException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}
