using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// <c>UNHANDLED</c> - what a session was sent and did not act on.
/// </summary>
/// <remarks>
/// <para><b>Why this command exists</b></para>
/// Case M7.1 of the by-hand pass is "point a session at a real ND host and read the counter", and it
/// is the most valuable case left: it turns 24 guessed ND graphics modes into a real work list. The
/// counter was readable only from inside the process, so the case could not be run at all. Found
/// while writing the run sheet rather than while sitting in front of the machine.
/// </remarks>
public class UnhandledCommandTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a session over a memory connection.
    /// </summary>
    /// <param name="emulatorType">
    /// Which terminal to put on it.
    /// </param>
    /// <returns>
    /// The session.
    /// </returns>
    private static TerminalSession NewSession(string emulatorType)
    {
        var emulator = EmulatorFactory.CreateEmulator(emulatorType, 80, 24, 100);
        return new TerminalSession(emulator, "UnhandledTest");
    }

    /// <summary>
    /// Runs the command and returns what it said.
    /// </summary>
    /// <param name="session">
    /// The session to ask.
    /// </param>
    /// <returns>
    /// The report.
    /// </returns>
    private static string Report(TerminalSession session)
    {
        var result = new UnhandledCommand()
            .ExecuteAsync(session, new CommandArgs(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.Success);
        return result.Output ?? string.Empty;
    }

    /// <summary>
    /// Feeds host bytes straight to the terminal.
    /// </summary>
    /// <param name="session">
    /// The session.
    /// </param>
    /// <param name="text">
    /// What the host sent.
    /// </param>
    private static void Feed(TerminalSession session, string text)
        => session.Emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void ASessionThatHasReceivedNothingSaysSo()
    {
        // "Nothing unhandled" and "nothing arrived at all" look identical from outside - both report
        // no counts - and the difference decides whether the session was worth running. So they are
        // different sentences.
        string report = Report(NewSession("VT100"));

        Assert.Contains("nothing has been received", report);
    }

    [Fact]
    public void ACleanRegisStreamReportsNothingUnhandled()
    {
        var session = NewSession("VT340");
        Feed(session, Escape + "P1pP[10,10]V[20,20]" + Escape + "\\");

        string report = Report(session);

        Assert.Contains("nothing unhandled", report);
    }

    [Fact]
    public void AnUnknownRegisCommandIsReportedWithItsCount()
    {
        // Q is not a ReGIS command letter, so the decoder counts it rather than guessing.
        var session = NewSession("VT340");
        Feed(session, Escape + "P1pQQQ" + Escape + "\\");

        string report = Report(session);

        Assert.Contains("ReGIS", report);
        Assert.Contains("Q x3", report);
    }

    [Fact]
    public void TheBiggestCountComesFirst()
    {
        // The whole point is choosing what to build next, so what a host asked for most often has to
        // be at the top. Sorted output that happened to be in insertion order would read as sorted.
        var session = NewSession("VT340");
        Feed(session, Escape + "P1pQZZZ" + Escape + "\\");

        string report = Report(session);

        int zAt = report.IndexOf("Z x3", System.StringComparison.Ordinal);
        int qAt = report.IndexOf("Q x1", System.StringComparison.Ordinal);

        Assert.True(zAt >= 0 && qAt >= 0, report);
        Assert.True(zAt < qAt, "the bigger count must be listed first:\n" + report);
    }

    [Fact]
    public void ATdvReportsItsNorskDataModesUnderTheirOwnHeading()
    {
        // The ND counter is the reason the command exists. Mode 27 - "set polygon/shape drawing
        // mode" - is one the spec names without explaining its parameters, so it is counted rather
        // than guessed at, and that is exactly what a real host's use of it should surface.
        var session = NewSession("TDV2200");
        Feed(session, Escape + "\"27h");

        string report = Report(session);

        Assert.Contains("Norsk Data", report);
        Assert.Contains("27", report);
    }

    [Fact]
    public void AnEscapeSequenceWithNoHandlerIsReported()
    {
        // THE CASE THIS COMMAND MISSED ON ITS FIRST OUTING. Real PED on real SINTRAN sends ESC Q at
        // startup; nothing here understands it, and the command still said "nothing unhandled",
        // because the two graphics counters only cover ND ESC-quote modes and ReGIS letters. A
        // sequence the parser read and no handler claimed fell out of a default: in silence.
        var session = NewSession("VT100");
        Feed(session, Escape + "Q");

        string report = Report(session);

        Assert.Contains("no handler", report);
        Assert.Contains("ESC Q x1", report);
    }

    [Fact]
    public void AKnownSequenceIsNotReported()
    {
        // The boundary from the other side. ESC c is a full reset and IS handled, so a terminal that
        // counted everything would drown the work list in sequences it implements perfectly.
        var session = NewSession("VT100");
        Feed(session, Escape + "c");

        Assert.Contains("nothing unhandled", Report(session));
    }

    [Fact]
    public void AControlSequenceWithNoHandlerIsReportedWithItsFinal()
    {
        var session = NewSession("VT100");
        Feed(session, Escape + "[1;2");
        Feed(session, "y");

        Assert.Contains("CSI y", Report(session));
    }

    [Fact]
    public void TheParametersAreLeftOutOfTheKey()
    {
        // A host sending CSI 1 y and CSI 2 y is using ONE sequence this terminal does not know, not
        // two. Keying by parameters would turn a one-line work list into a hundred.
        var session = NewSession("VT100");
        Feed(session, Escape + "[1y" + Escape + "[2y" + Escape + "[3y");

        Assert.Contains("CSI y x3", Report(session));
    }

    [Fact]
    public void APrivateSequenceIsKeptApartFromThePlainOne()
    {
        // CSI ? y and CSI y are different sequences with the same final. Merging them would say a
        // host used one thing when it used two.
        var session = NewSession("VT100");
        Feed(session, Escape + "[?9y" + Escape + "[9y");

        string report = Report(session);

        Assert.Contains("CSI ? y", report);
        Assert.Contains("CSI y", report);
    }

    [Fact]
    public void AnUnknownModeNumberIsReportedWithItsNumber()
    {
        // PED's actual shape, read off the trace on 2026-08-20: CSI 62;62 h and CSI 30;7;80 l. The
        // sequence is perfectly well known - it is SM and RM - and the MODE NUMBERS are the unknown
        // part, so this is the one counter that keeps its parameters. "CSI h" would say nothing.
        //
        // Mode 30 is used here rather than PED's 62, which this test drove until 11 September 2026:
        // 62 turned out to be GRM in the 2215's own table and is handled now, so it is no longer an
        // example of an unknown number. Mode 30 is in no manual held in this repository, which is
        // exactly what this test needs.
        var session = NewSession("TDV2200");
        Feed(session, Escape + "[30;7l");

        string report = Report(session);

        Assert.Contains("mode 30 reset", report);
    }

    [Fact]
    public void AKnownModeNumberIsNotReported()
    {
        // The boundary. Mode 4 is IRM, insert/replace, and is implemented - counting it would bury
        // the work list under modes the terminal handles perfectly.
        var session = NewSession("VT100");
        Feed(session, Escape + "[4h");

        Assert.Contains("nothing unhandled", Report(session));
    }

    [Fact]
    public void ADeviceControlStringWithNoHandlerIsReported()
    {
        // Real PED opens by sending four of these - "L10" through "L40" - and until this counted
        // them, four whole payloads vanished without trace. Worse than a lost sequence: a DCS
        // carries DATA, so what is dropped is the content as well as the command.
        var session = NewSession("TDV2200");
        Feed(session, Escape + "PL10" + Escape + "\\" + Escape + "PL20" + Escape + "\\");

        Assert.Contains("DCS L x2", Report(session));
    }

    [Fact]
    public void AKnownDeviceControlStringIsNotReported()
    {
        // The boundary: a ReGIS DCS on a terminal that has ReGIS is claimed, and must not be
        // counted. Otherwise every drawing a VT340 receives would fill the work list.
        var session = NewSession("VT340");
        Feed(session, Escape + "P1pP[10,10]" + Escape + "\\");

        Assert.Contains("nothing unhandled", Report(session));
    }

    [Fact]
    public void TheCommandIsOnTheRegistrySoEverySurfaceHasIt()
    {
        // The two-surface trap, which this codebase has been caught by for real: a behaviour wired at
        // one call site gets fixed on the menu and stays missing over MCP. Registering it means the
        // script DSL and the tool provider both pick it up, and CommandSurfaceEquivalenceTests
        // generates the rest of that check over the whole registry.
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);

        Assert.True(registry.TryGet("UNHANDLED", out var command));
        Assert.Equal("UNHANDLED", command.Name);
    }
}
