using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Renci.SshNet;
using Xunit;

namespace RetroTerm.Tests.Protocols;

/// <summary>
/// What SSH.NET's ShellStream does with a CancellationToken on a read.
/// </summary>
/// <remarks>
/// <c>SSHConnection.DisconnectAsync</c> cancels a token while the receive loop sits in
/// <c>ShellStream.ReadAsync(buffer, 0, count, token)</c>. That only ends if the stream honours the
/// token while a read is blocked. In the pinned SSH.NET (2026.0.0) ShellStream does NOT declare
/// that overload, so it is <c>System.IO.Stream</c>'s, which looks at the token once, before it
/// starts: an idle session's read never returns on cancel. That is why the connection closes the
/// stream first and only then waits (<c>ReceiveLoopShutdown</c>), and why an idle SSH tab used to
/// stop the main window from closing (9 October 2026).
/// <para></para>
/// This pins the fact. If an SSH.NET upgrade makes it fail, ShellStream has learned to honour the
/// token and the close-first order in <c>SSHConnection.DisconnectAsync</c> can be looked at again;
/// it does no harm either way.
/// </remarks>
public class SshShellStreamReadCancellationTests
{
    [Fact]
    public void ShellStreamsTokenReadAsyncIsStreamsOwnAndIgnoresTheTokenWhileBlocked()
    {
        MethodInfo? read = typeof(ShellStream).GetMethod(
            "ReadAsync",
            new[] { typeof(byte[]), typeof(int), typeof(int), typeof(CancellationToken) });

        Assert.NotNull(read);
        Assert.True(
            read!.DeclaringType == typeof(Stream),
            "ReadAsync(byte[], int, int, CancellationToken) is now declared by " + read.DeclaringType
            + ". SSH.NET may honour the token now; see the remarks on this class.");
    }
}
