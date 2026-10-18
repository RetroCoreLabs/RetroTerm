using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The ReGIS report command, <c>R</c> - chapter 10. The tenth and last ReGIS command.
/// </summary>
/// <remarks>
/// <para><b>The one ReGIS command with no picture to judge it</b></para>
/// Everything else in ReGIS can be looked at. This one answers the host, so the only way to know it
/// is right is to read the manual's format and check the bytes. Every report ends with a carriage
/// return - "All information returned by the VT300 ends with a carriage return (CR)".
///
/// Graphics input mode, the sixth thing chapter 10 covers, is not a report and lives in
/// <c>RegisGraphicsInputTests</c> beside the emulator half that suspends the host's data.
/// </remarks>
public class RegisReportTests
{
    /// <summary>
    /// Runs some ReGIS and returns whatever the decoder owes the host.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to run.
    /// </param>
    /// <returns>
    /// The reports, carriage returns and all.
    /// </returns>
    private static string ReportsFrom(string commands)
    {
        var surface = new InMemoryGraphicsSurface(100, 60);
        var decoder = new RegisDecoder();

        decoder.Decode(commands, surface);
        return decoder.TakeReports();
    }

    [Fact]
    public void TheCursorPositionIsReportedAsABracketedExtent()
    {
        // "The report format is as an absolute, bracketed extent in screen coordinates."
        Assert.Equal("[40,25]\r", ReportsFrom("P[40,25]R(P)"));
    }

    [Fact]
    public void TheCursorPositionFollowsTheDrawing()
    {
        // A vector leaves the cursor at its end, so the report has to be taken after it and not
        // from wherever the last explicit position command put it.
        Assert.Equal("[70,25]\r", ReportsFrom("P[40,25]V[70,25]R(P)"));
    }

    [Fact]
    public void AMacrographIsReportedInsideItsIndicatorAndTerminator()
    {
        // "The macrograph contents report starts with a macrograph report indicator, @=<call
        // letter>... The report ends with a macrograph terminator and a carriage return, @;"
        Assert.Equal("@=AP[10,10]V[20,20]@;\r",
            ReportsFrom("@:AP[10,10]V[20,20]@;R(M(A))"));
    }

    [Fact]
    public void AnUndefinedMacrographReportsAnEmptyBody()
    {
        // "If there is no macrograph defined for <call letter>, the terminal reports a null
        // macrograph (no characters) enclosed in the indicator and terminator." So the shape is the
        // same and only the body is missing - a host can tell "empty" from "no answer".
        Assert.Equal("@=Z@;\r", ReportsFrom("R(M(Z))"));
    }

    [Fact]
    public void TheCallLetterIsNotCaseSensitive()
    {
        // "The call letter is not case sensitive. For example, 'a' and 'A' identify the same
        // macrograph." The letter comes back as the host wrote it.
        Assert.Equal("@=aP[1,1]@;\r", ReportsFrom("@:AP[1,1]@;R(M(a))"));
    }

    [Fact]
    public void MacrographStorageIsReportedAsAvailableThenTotal()
    {
        // "The terminal reports this information as two integer strings, separated by a comma and
        // enclosed in double quotes" - available first, total second. Nothing is stored yet, so all
        // of it is free.
        Assert.Equal("\"10000,10000\"\r", ReportsFrom("R(M(=))"));
    }

    [Fact]
    public void StoringAMacrographReducesTheSpaceAvailable()
    {
        // "You can find the amount of storage space in current use by subtracting the available
        // space from the total allocated." A host uses this to decide whether its macrograph fits,
        // so the number has to move when something is stored.
        string reports = ReportsFrom("@:AP[10,10]V[20,20]@;R(M(=))");

        Assert.Equal("\"9984,10000\"\r", reports);
    }

    [Fact]
    public void TheCharacterSetNameIsReported()
    {
        // "The terminal reports the name of the character set in the following format. A'<name>'"
        Assert.Equal("A'ARROWS'\r", ReportsFrom("L(A1)L(A'ARROWS')R(L)"));
    }

    [Fact]
    public void AnUnnamedCharacterSetReportsAnEmptyName()
    {
        Assert.Equal("A''\r", ReportsFrom("R(L)"));
    }

    [Fact]
    public void TheErrorConditionIsAnsweredEvenThoughNothingFlagsErrors()
    {
        // Nothing in this decoder flags parse errors yet, so the honest answer is "no error". It
        // still has to be SENT: a host that asks and hears nothing waits for a reply that never
        // comes, which is worse than a truthful zero.
        Assert.Equal("\"0,0\"\r", ReportsFrom("R(E)"));
    }

    [Fact]
    public void SeveralReportsInOneCommandFollowOneAnother()
    {
        // Each carries its own terminator, so they simply run on.
        Assert.Equal("[5,5]\r\"0,0\"\r", ReportsFrom("P[5,5]R(P)R(E)"));
    }

    [Fact]
    public void TakingTheReportsClearsThem()
    {
        var surface = new InMemoryGraphicsSurface(100, 60);
        var decoder = new RegisDecoder();

        decoder.Decode("P[5,5]R(P)", surface);

        Assert.True(decoder.HasReports);
        Assert.Equal("[5,5]\r", decoder.TakeReports());
        Assert.False(decoder.HasReports);
        Assert.Equal("", decoder.TakeReports());
    }

    [Fact]
    public void AReportFormThisDecoderCannotAnswerIsCounted()
    {
        // A host asking for something this decoder cannot answer must show up in the counter rather
        // than pass silently. Chapter 10 defines no report option Q, so it stands in for any form a
        // later manual might add.
        var surface = new InMemoryGraphicsSurface(100, 60);
        var decoder = new RegisDecoder();

        decoder.Decode("R(Q)", surface);

        Assert.Equal(1, decoder.UnhandledCommands['R']);
    }

    [Fact]
    public void GraphicsInputModeIsNoLongerCounted()
    {
        // It used to be, and that was honest at the time. Now that R(I0), R(I1) and R(P(I)) are all
        // built, counting them would tell a host the terminal ignored something it acted on. See
        // RegisGraphicsInputTests for the feature itself.
        var surface = new InMemoryGraphicsSurface(100, 60);
        var decoder = new RegisDecoder();

        decoder.Decode("R(I1)R(P(I))", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('R'));
    }

    [Fact]
    public void AnAnsweredReportIsNotCounted()
    {
        var surface = new InMemoryGraphicsSurface(100, 60);
        var decoder = new RegisDecoder();

        decoder.Decode("R(P)", surface);

        Assert.False(decoder.UnhandledCommands.ContainsKey('R'));
    }

    [Fact]
    public void TheReportReachesTheHostThroughTheEmulator()
    {
        // The whole point. The decoder owns no connection, so the emulator has to notice it owes a
        // reply and put it on the wire - and until this test existed, nothing checked that it did.
        var emulator = EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);
        var sent = new List<byte[]>();

        emulator.DataToSend += data => sent.Add(data);

        // A ReGIS device control string carrying a position and a report request.
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1bP1pP[40,25]R(P)\x1b\\"));

        Assert.Single(sent);
        Assert.Equal("[40,25]\r", Encoding.ASCII.GetString(sent[0]));
    }
}
