using System;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.Net;

/// <summary>
/// Stops a connection's receive loop in the one order that cannot hang: take the thing it is
/// blocked on away first, then wait, and never wait for ever.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (9 October 2026)</b></para>
/// Closing the main window with an idle SSH tab open, answering "Close All" in the confirmation
/// dialog, closed the dialog and left the window and the connection alone. The cause: the receive
/// loop sits in <c>ShellStream.ReadAsync(buffer, 0, count, token)</c>. SSH.NET's ShellStream does
/// not declare that overload, so it is <c>System.IO.Stream</c>'s, which looks at the token once,
/// before it starts, and a read on a session with nothing to say never returns when the token is
/// cancelled. <c>SSHConnection.DisconnectAsync</c> cancelled and then waited for the loop BEFORE it
/// disposed the stream, and so waited for something only the next line could cause.
/// <c>MainWindow</c> was awaiting that disconnect on its way to <c>Close()</c>, so it never got there.
/// <para>
/// <c>SerialConnection</c> was fixed for the same shape of problem and does it in this order; this
/// is that order as a piece that can be tested without a server.
/// </para>
/// </remarks>
public static class ReceiveLoopShutdown
{
    /// <summary>
    /// Releases whatever the loop is blocked on, then waits for the loop for at most
    /// <paramref name="timeoutMilliseconds"/>.
    /// </summary>
    /// <param name="loop">
    /// The receive loop's task, or null when there is none.
    /// </param>
    /// <param name="unblock">
    /// Closes the stream or handle the loop is reading from, so a pending read fails or returns.
    /// It runs first and always, and an exception from it is swallowed: the connection is going
    /// away either way.
    /// </param>
    /// <param name="timeoutMilliseconds">
    /// The longest to wait for the loop to end after <paramref name="unblock"/> has run.
    /// </param>
    /// <returns>
    /// True when the loop had ended, false when the wait ran out. False is not an error to report:
    /// the thing it was blocked on has been released by then.
    /// </returns>
    public static async Task<bool> StopAsync(Task? loop, Action unblock, int timeoutMilliseconds)
    {
        try
        {
            unblock();
        }
        catch (Exception)
        {
            // Closing a half-dead stream may throw; the point is that it has been closed.
        }

        if (loop == null)
        {
            return true;
        }

        Task finished = await Task.WhenAny(loop, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, loop))
        {
            return false;
        }

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The loop failing on its way out (a read on a stream that was just disposed) is the
            // expected way for it to end here.
        }

        return true;
    }
}
