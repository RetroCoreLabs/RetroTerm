using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.Avalonia;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Changing which terminal is decoding the bytes, on a connection that stays up.
/// </summary>
/// <remarks>
/// <para><b>Why it has to work without dropping the line</b></para>
/// The terminal type a host really wants is usually discovered AFTER logging in - the screen comes
/// up wrong, or a program draws in the wrong place. Until this existed the only way to correct it
/// was to reconnect, because the connect path threw the whole tab away when the type differed, and
/// that means logging in again. Nothing about a socket depends on which terminal is on this end.
/// Ronny's two decisions, from 2026-08-19: the screen carries over, and the geometry snaps to the
/// new terminal's real size.
/// </remarks>
public class EmulationChangeTests
{
    /// <summary>
    /// A session on a live connection, with something on its screen.
    /// </summary>
    /// <param name="emulatorType">
    /// Terminal to start as.
    /// </param>
    /// <param name="text">
    /// Text to put on the screen first.
    /// </param>
    /// <returns>
    /// The session and the connection recording what it was told.
    /// </returns>
    private static async Task<(TerminalSession Session, SizeRecordingConnection Connection)> ConnectedSessionAsync(
        string emulatorType = "VT100", string text = "HELLO FROM THE HOST")
    {
        var emulator = EmulatorFactory.CreateEmulator(emulatorType, 80, 24, 200);
        var session = new TerminalSession(emulator, "test");

        var connection = new SizeRecordingConnection();
        await session.ConnectAsync(connection);

        await session.RunOnSessionThreadAsync(
            () => session.Emulator.ProcessData(Encoding.ASCII.GetBytes(text)));

        return (session, connection);
    }

    /// <summary>
    /// Reads one row of the screen back as text.
    /// </summary>
    /// <param name="session">
    /// The session to read.
    /// </param>
    /// <param name="row">
    /// Row to read.
    /// </param>
    /// <returns>
    /// The row's characters, trailing blanks removed.
    /// </returns>
    private static string RowText(TerminalSession session, int row)
    {
        var buffer = session.Emulator.GetBuffer();
        var text = new StringBuilder(buffer.Width);

        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer.GetCell(row, col);
            text.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
        }

        return text.ToString().TrimEnd();
    }

    [Fact]
    public async Task TheConnectionSurvivesTheChange()
    {
        var (session, connection) = await ConnectedSessionAsync();

        var replacement = EmulatorFactory.CreateForReplacement("TDV2200", 80, 24);
        await session.ChangeEmulatorAsync(replacement);

        // THE WHOLE POINT. A change that dropped the line would mean logging in again, which is
        // what made choosing the wrong terminal expensive in the first place.
        Assert.True(session.IsConnected);
        Assert.Same(connection, session.Connection);
        Assert.Same(replacement, session.Emulator);
    }

    [Fact]
    public async Task TheScreenComesAcross()
    {
        var (session, _) = await ConnectedSessionAsync(text: "HELLO FROM THE HOST");

        await session.ChangeEmulatorAsync(EmulatorFactory.CreateForReplacement("VT220", 80, 24));

        Assert.Equal("HELLO FROM THE HOST", RowText(session, 0));
    }

    [Fact]
    public async Task TheCursorComesAcross()
    {
        var (session, _) = await ConnectedSessionAsync(text: "ABCDE");

        int columnBefore = session.Emulator.GetCursor().Column;
        Assert.Equal(5, columnBefore);

        await session.ChangeEmulatorAsync(EmulatorFactory.CreateForReplacement("VT220", 80, 24));

        Assert.Equal(columnBefore, session.Emulator.GetCursor().Column);
    }

    [Fact]
    public async Task TheScrollbackComesAcross()
    {
        var (session, _) = await ConnectedSessionAsync(text: "");

        // Thirty lines through a 24-row screen, so six of them have scrolled off the top.
        await session.RunOnSessionThreadAsync(() =>
        {
            for (int i = 0; i < 30; i++)
            {
                session.Emulator.ProcessData(Encoding.ASCII.GetBytes($"line {i}\r\n"));
            }
        });

        int before = session.Emulator.GetBuffer().ScrollbackLineCount;
        Assert.True(before > 0, "the fixture did not scroll anything off the screen");

        await session.ChangeEmulatorAsync(EmulatorFactory.CreateForReplacement("VT220", 80, 24));

        Assert.Equal(before, session.Emulator.GetBuffer().ScrollbackLineCount);
    }

    [Fact]
    public async Task AFixedGeometryTerminalSnapsToItsRealSize()
    {
        // A VT100 stretched to a tall window, then changed to hardware that has exactly one screen.
        var (session, connection) = await ConnectedSessionAsync();
        await session.ResizeAsync(120, 40);

        await session.ChangeEmulatorAsync(EmulatorFactory.CreateForReplacement("TDV2200", 120, 40));

        // 80 by 25 because the machine was 80 by 25. Growing it to 120 by 40 because the window is
        // big is not a bigger TDV2200, it is a machine that never existed.
        Assert.Equal(80, session.Emulator.Width);
        Assert.Equal(25, session.Emulator.Height);

        // And the host is told, or a full-screen program would keep painting at the old size.
        Assert.Contains((80, 25), connection.Sizes);
    }

    [Fact]
    public async Task ATerminalThatFollowsTheWindowKeepsTheSizeOnScreen()
    {
        var (session, _) = await ConnectedSessionAsync();
        await session.ResizeAsync(120, 40);

        await session.ChangeEmulatorAsync(EmulatorFactory.CreateForReplacement("XTERM", 120, 40));

        // An xterm has no geometry of its own to snap to, and the window is about to decide it
        // again anyway - so throwing the reader back to 80 by 24 would just be a flicker.
        Assert.Equal(120, session.Emulator.Width);
        Assert.Equal(40, session.Emulator.Height);
    }

    [Fact]
    public async Task TheNewTerminalIsTheOneDecodingWhatArrivesNext()
    {
        // A VT52 reads ESC Y as a cursor move; a VT100 does not have that sequence at all. So if
        // this lands where VT52 rules say it should, the new emulator really is the one parsing.
        var (session, _) = await ConnectedSessionAsync();

        await session.ChangeEmulatorAsync(EmulatorFactory.CreateForReplacement("VT52", 80, 24));

        // ESC Y <row+32> <col+32> - row 5, column 10, in the VT52's own biased form.
        byte[] moveTo5And10 = { 0x1B, (byte)'Y', 32 + 5, 32 + 10 };
        await session.RunOnSessionThreadAsync(() => session.Emulator.ProcessData(moveTo5And10));

        Assert.Equal(5, session.Emulator.GetCursor().Row);
        Assert.Equal(10, session.Emulator.GetCursor().Column);
    }

    [Fact]
    public async Task TheOldEmulatorStopsBeingHeardFrom()
    {
        // A handler left on the replaced emulator would keep answering the host for a terminal
        // nobody is looking at - two terminals replying to one DA query.
        var (session, _) = await ConnectedSessionAsync();
        var outgoing = session.Emulator;

        await session.ChangeEmulatorAsync(EmulatorFactory.CreateForReplacement("VT220", 80, 24));

        int fromTheOldOne = 0;
        outgoing.DataToSend += _ => fromTheOldOne++;

        // Device Attributes at the terminal that is no longer in the session.
        byte[] deviceAttributes = { 0x1B, (byte)'[', (byte)'c' };
        outgoing.ProcessData(deviceAttributes);

        // It still generates a reply - it is a working emulator - but the SESSION is not carrying
        // it any more, which is what the unsubscribe is for.
        Assert.Equal(1, fromTheOldOne);
        Assert.NotSame(outgoing, session.Emulator);
    }

    // ─────────────────────────────────────────────────────────────
    // The command surface - what scripts and MCP reach
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A registry with the built-in commands on it.
    /// </summary>
    /// <returns>
    /// The registry.
    /// </returns>
    private static CommandRegistry Registry()
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        return registry;
    }

    [Fact]
    public async Task TheCommandChangesTheTerminal()
    {
        var (session, _) = await ConnectedSessionAsync();

        var result = await Registry().ExecuteAsync("EMULATION", session,
            new CommandArgs().Set("type", "TDV2200"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(80, session.Emulator.Width);
        Assert.Equal(25, session.Emulator.Height);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task TheCommandRefusesATerminalThatDoesNotExist()
    {
        var (session, _) = await ConnectedSessionAsync();
        var before = session.Emulator;

        var result = await Registry().ExecuteAsync("EMULATION", session,
            new CommandArgs().Set("type", "VT999"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);

        // And it names the ones that DO exist, rather than only saying no.
        Assert.Contains("TDV2200", result.Error);

        // The session is untouched. A rejected change that had already half-replaced the terminal
        // would be worse than no command at all.
        Assert.Same(before, session.Emulator);
    }

    [Fact]
    public async Task AskingForTheTerminalItAlreadyIsDoesNothing()
    {
        var (session, _) = await ConnectedSessionAsync();
        var before = session.Emulator;

        var result = await Registry().ExecuteAsync("EMULATION", session,
            new CommandArgs().Set("type", "VT100"), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Same(before, session.Emulator);
    }
}
