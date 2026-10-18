using System.Collections.Generic;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// How the test server gathers one ReGIS graphics input report.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, 27 August 2026</b></para>
/// Driving M6.5 by hand showed every round printing the PREVIOUS round's answer: round one's
/// "Host received:" was empty, round two carried round one's report, and so on to the end. It looked
/// like the terminal was answering late. It was not - the terminal was right the whole time.
///
/// Chapter 10 of the VT330/VT340 Programmer Reference: "When the terminal receives R(I), it returns
/// a carriage return (CR). Applications can use the CR for synchronization." That CR arrives the
/// instant graphics input is entered, before anybody has pressed anything, and the collector was
/// reading it as the operator's answer.
///
/// <para><b>Why it is worth a test at all, in a test server</b></para>
/// Because it made CORRECT behaviour look broken, which is the most expensive kind of defect this
/// repository has. The suite is the only place a person looks at graphics without writing a program,
/// so a lie told here is believed.
/// </remarks>
public class GraphicsInputReportCollectionTests
{
    /// <summary>
    /// Builds a reader that hands back a scripted sequence, then nothing.
    /// </summary>
    /// <param name="pieces">
    /// What the terminal sends, in order. A null entry means the line went quiet for that read.
    /// </param>
    /// <returns>
    /// A reader the collector can be driven with.
    /// </returns>
    /// <remarks>
    /// The timeout is deliberately ignored. What is under test is which pieces are kept and which
    /// are discarded, not how long anything waits - a test that slept would be slow and would prove
    /// nothing extra.
    /// </remarks>
    private static TestServerApp.ReadInputWithTimeout Reader(List<ParsedInput?> pieces)
    {
        int at = 0;
        return timeoutMs =>
        {
            if (at >= pieces.Count) return Task.FromResult<ParsedInput?>(null);
            return Task.FromResult(pieces[at++]);
        };
    }

    /// <summary>
    /// One ordinary character, as the parser delivers it.
    /// </summary>
    /// <param name="text">
    /// The character or characters.
    /// </param>
    /// <returns>
    /// The parsed piece.
    /// </returns>
    private static ParsedInput? Char(string text)
        => new ParsedInput(InputType.Character, text, isComplete: true);

    /// <summary>
    /// A carriage return, as the parser delivers it.
    /// </summary>
    /// <returns>
    /// The parsed piece.
    /// </returns>
    /// <remarks>
    /// The parser gives Enter the value "\r\n", which is why an unwanted one showed up as a blank
    /// line rather than as a visible stray character.
    /// </remarks>
    private static ParsedInput? Enter()
        => new ParsedInput(InputType.Enter, "\r\n", isComplete: true);

    [Fact]
    public async Task TheCarriageReturnThatEnteringTheModeSendsIsNotMistakenForTheAnswer()
    {
        // THE ONE THAT MATTERS. The sync CR arrives, then a long quiet while the operator aims, then
        // the real report. Before the fix this returned the CR alone and every later round was one
        // behind.
        var report = await TestServerApp.CollectGraphicsInputReportAsync(Reader(new List<ParsedInput?>
        {
            Enter(),        // R(I) acknowledged
            null,           // the operator is aiming - nothing follows the CR
            Char("A"),
            Char("[410,240]"),
            Enter()
        }));

        Assert.Equal("A[410,240]\r\n", report);
    }

    [Fact]
    public async Task AnAnswerThatArrivesWithoutAnySyncCarriageReturnIsUnharmed()
    {
        // The fix must not need the CR to be there. Nothing in the manual promises the collector
        // will always see it - a slow line, a dropped read, and it is simply absent.
        var report = await TestServerApp.CollectGraphicsInputReportAsync(Reader(new List<ParsedInput?>
        {
            Char("X"),
            Char("[401,239]"),
            Enter()
        }));

        Assert.Equal("X[401,239]\r\n", report);
    }

    [Fact]
    public async Task EnterItselfCanBeTheAnsweringKeyAndIsKept()
    {
        // Enter is a legal answering key, and then the report genuinely STARTS with a carriage
        // return. Discarding every leading CR would throw that answer away and hang the round.
        //
        // What tells the two apart is only what comes NEXT: after the sync CR the line goes quiet
        // while the operator aims; after an answering CR the position is already arriving.
        var report = await TestServerApp.CollectGraphicsInputReportAsync(Reader(new List<ParsedInput?>
        {
            Enter(),            // this one IS the answer
            Char("[400,240]"),  // and it is followed immediately, which is how we know
            Enter()
        }));

        Assert.Equal("\r\n[400,240]\r\n", report);
    }

    [Fact]
    public async Task ARoundNobodyAnswersSaysSoRatherThanReturningNothing()
    {
        // An empty string would be printed as "Host received:" with a blank after it, which is
        // exactly what the defect looked like. Saying the round timed out cannot be mistaken for an
        // answer.
        var report = await TestServerApp.CollectGraphicsInputReportAsync(Reader(new List<ParsedInput?>()));

        Assert.Contains("timed out", report);
    }

    [Fact]
    public async Task SeveralSyncCarriageReturnsInARowAreAllSkipped()
    {
        // Defensive: nothing says only one can arrive, and a collector that skipped exactly one
        // would be back to being one behind the moment a second appeared.
        var report = await TestServerApp.CollectGraphicsInputReportAsync(Reader(new List<ParsedInput?>
        {
            Enter(),
            null,
            Enter(),
            null,
            Char("B"),
            Char("[300,200]"),
            Enter()
        }));

        Assert.Equal("B[300,200]\r\n", report);
    }
}
