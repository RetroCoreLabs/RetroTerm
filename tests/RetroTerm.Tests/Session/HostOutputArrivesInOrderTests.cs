using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Lines a host writes must land on the screen in the order it wrote them.
///
/// WHY THIS EXISTS. On 10 September 2026, while probing something else, the client's screen showed
/// a six-line menu with two of its lines the wrong way round: "4. TDV2200" on the row above
/// "3. TDV2215", where the server had written 3 before 4. Everything else on that screen was in
/// order. It was seen ONCE, in a probe that read the buffer while the connection was still being
/// written to, so the likeliest explanation is the probe rather than the terminal.
///
/// Likeliest is not proven, and an emulator that reorders whole lines would be a serious defect
/// that no existing test would catch - every screen test here writes its content in one go. So
/// this drives the real path, a host writing over a connection into a session, and checks the
/// order that comes out the far end.
///
/// It writes the lines in SEPARATE sends on purpose. One send cannot arrive out of order; several
/// can, and several is what a real host does.
/// </summary>
public class HostOutputArrivesInOrderTests
{
    /// <summary>
    /// Reads one row of the emulator's screen as text.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to read.
    /// </param>
    /// <param name="row">
    /// Which row.
    /// </param>
    /// <returns>
    /// The row, with trailing blanks removed.
    /// </returns>
    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var buffer = emulator.Buffer;
        var sb = new StringBuilder(buffer.Width);
        for (int col = 0; col < buffer.Width; col++)
        {
            // GetString is what the cell SHOWS: an untouched cell reads as a space, not a NUL.
            sb.Append(buffer.GetCell(row, col).GetString());
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Waits until a row holds something, so the check never races the session pump.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to watch.
    /// </param>
    /// <param name="row">
    /// The row that should fill.
    /// </param>
    /// <returns>
    /// A task that completes when the row is no longer blank, or when time runs out.
    /// </returns>
    private static async Task WaitForRowAsync(TerminalEmulatorBase emulator, int row)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Row(emulator, row).Length == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task SixLinesWrittenOneAtATimeLandInTheOrderTheyWereWritten()
    {
        var (client, server) = InMemoryBidirectionalConnection.CreatePair();

        // The server side is read by hand in these tests, not by a receive loop.
        server.DisableReceiveLoop();
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await server.ConnectAsync(TestContext.Current.CancellationToken);

        var emulator = new TDV2200Emulator(80, 24);
        using var session = new TerminalSession(emulator, "order check");
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        // Six separate writes, the shape the menu had when the odd screen was seen.
        const int lines = 6;
        for (int i = 1; i <= lines; i++)
        {
            await server.SendAsync(Encoding.ASCII.GetBytes($"{i}. line number {i}\r\n"), TestContext.Current.CancellationToken);
        }

        await WaitForRowAsync(emulator, lines - 1);

        for (int i = 1; i <= lines; i++)
        {
            string expected = $"{i}. line number {i}";
            string actual = Row(emulator, i - 1);
            Assert.True(expected == actual,
                $"row {i - 1} holds \"{actual}\" where the host wrote \"{expected}\" - the lines "
                + "have arrived out of order or been dropped");
        }
    }

    [Fact]
    public async Task TwentyLinesInOneWriteKeepTheirOrder()
    {
        // The same check for a host that sends everything at once, which is the case a single
        // read cannot get wrong - so a failure here would mean the emulator itself, not the
        // transport.
        var (client, server) = InMemoryBidirectionalConnection.CreatePair();

        server.DisableReceiveLoop();
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await server.ConnectAsync(TestContext.Current.CancellationToken);

        var emulator = new TDV2200Emulator(80, 24);
        using var session = new TerminalSession(emulator, "order check");
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        const int lines = 20;
        var block = new StringBuilder();
        for (int i = 1; i <= lines; i++)
        {
            block.Append(i.ToString()).Append(". line number ").Append(i.ToString()).Append("\r\n");
        }

        await server.SendAsync(Encoding.ASCII.GetBytes(block.ToString()), TestContext.Current.CancellationToken);
        await WaitForRowAsync(emulator, lines - 1);

        for (int i = 1; i <= lines; i++)
        {
            string expected = $"{i}. line number {i}";
            string actual = Row(emulator, i - 1);
            Assert.True(expected == actual,
                $"row {i - 1} holds \"{actual}\" where the host wrote \"{expected}\"");
        }
    }
}
