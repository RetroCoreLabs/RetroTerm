//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Ibm3270;

/// <summary>
/// The 3270 inside a real TerminalSession: host records go in through the pump, and key presses
/// change the screen on the pump too (the session thread rule), not on the UI thread.
/// </summary>
public class Ibm3270SessionTests
{
    private static readonly byte[] Screen =
    {
        Command3270.EraseWrite, 0x02,
        Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
        Order3270.StartField, 0x40,
        Order3270.InsertCursor,
        Order3270.SetBufferAddress, 0x40, 0xD4,
        Order3270.StartField, 0xF0,
    };

    [Fact]
    public async Task KeysRunOnTheSessionThreadAndEnterReachesTheConnection()
    {
        var emulator = new Ibm3270Emulator();
        var session = new TerminalSession(emulator, "Test");
        var connection = new InMemoryConnection();
        await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

        // The session must hand the emulator its pump.
        Assert.NotNull(emulator.SessionThread);

        // A TN3270 connection delivers one whole record per receive.
        connection.SimulateReceive(Screen);
        await session.FlushAsync();
        Assert.Equal(6, emulator.Display.CursorAddress);
        connection.ClearSentData();

        Assert.True(emulator.HandleText("hi"));
        Assert.True(emulator.HandleKey("Enter", false, false, false));
        await session.FlushAsync();

        var sent = connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal(new byte[] { 0x7D, 0x40, 0xC8, 0x11, 0x40, 0xC6, 0x88, 0x89 }, sent[0]);
        Assert.Equal(KeyboardLock.WaitingForHost, emulator.Display.Lock);
    }
}
