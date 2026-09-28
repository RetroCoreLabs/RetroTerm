using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// The position stack - <c>P(B)...(E)</c> bounded and <c>P(S)...(E)</c> unbounded.
/// </summary>
/// <remarks>
/// <para><b>Chapter 4, Position Stack Options</b></para>
/// "A position stack is a set of coordinate positions that ReGIS uses in sequence. These options let
/// you move the cursor to several positions in a single command."
///  - Bounded: "(B) saves the current active position. (Pushes the position onto the stack.)" and
///    "(E) returns the active position to the coordinates saved by the last (B) option. (Pops the
///    position off the stack.)"
///  - Unbounded: "The (S) pushes a dummy, or nonexistent position onto the position stack. The (E)
///    pops this nonexistent position off the stack, leaving the active position at the position
///    specified before the (E) option."
///  - The limit: "You can save up to 16 positions in a stack ... The maximum number of unended,
///    saved positions (including all save commands) is 16."
/// <para><b>None of this existed until 2026-08-20</b></para>
/// The decoder read <c>(B)</c>, <c>(S)</c> and <c>(E)</c> as option groups it did not understand and
/// dropped them, so a bounded stack drew in the right places and simply never came back - every
/// coordinate after the <c>(E)</c> was measured from the wrong point. The plan had this listed only
/// as "error codes 7 and 8 are not raised", which was the symptom rather than the gap: the codes are
/// the stack's overflow and underflow, and there was no stack to overflow.
/// </remarks>
public class RegisPositionStackTests
{
    /// <summary>
    /// Plays a stream and hands back the decoder, so the drawing point can be read.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to play.
    /// </param>
    /// <returns>
    /// The decoder, after the stream.
    /// </returns>
    private static RegisDecoder Play(string commands)
    {
        var surface = new InMemoryGraphicsSurface(800, 480);
        var decoder = new RegisDecoder();
        decoder.Decode(commands, surface);
        return decoder;
    }

    /// <summary>
    /// Plays a stream and returns what the decoder owes the host.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to play.
    /// </param>
    /// <returns>
    /// The reports.
    /// </returns>
    private static string Reports(string commands)
    {
        var surface = new InMemoryGraphicsSurface(800, 480);
        var decoder = new RegisDecoder();
        decoder.Decode(commands, surface);
        return decoder.HasReports ? decoder.TakeReports() : string.Empty;
    }

    [Fact]
    public void ABoundedStackPutsTheDrawingPointBackWhereItStarted()
    {
        // Push at 400,250, wander to 250,50, then pop. R(P) reports the drawing point, so the
        // terminal itself says where it ended up.
        Assert.Equal("[400,250]\r", Reports("P[400,250](B)P[250,50]P(E)R(P)"));
    }

    [Fact]
    public void WithoutThePopTheDrawingPointStaysWhereItWandered()
    {
        // The other half of the same measurement. Without it, a test could pass because nothing
        // ever moved rather than because the pop worked.
        Assert.Equal("[250,50]\r", Reports("P[400,250](B)P[250,50]R(P)"));
    }

    [Fact]
    public void AnUnboundedStackLeavesTheDrawingPointWhereItIs()
    {
        // "(E) pops the dummy position off the stack. The active position does not move." This is
        // the ONLY difference between (S) and (B), so the two cases sit side by side.
        Assert.Equal("[250,50]\r", Reports("P[400,250](S)P[250,50]P(E)R(P)"));
    }

    [Fact]
    public void StacksNestAndPopInTurn()
    {
        // Three deep, popped one at a time, reporting after each - so a stack that restored only
        // the outermost, or always the innermost, is caught.
        var reports = Reports(
            "P[100,100](B)P[200,200](B)P[300,300](B)P[400,400]" +
            "P(E)R(P)P(E)R(P)P(E)R(P)");

        Assert.Equal("[300,300]\r[200,200]\r[100,100]\r", reports);
    }

    [Fact]
    public void AVectorCommandCanCarryAStackToo()
    {
        // Chapter 4: "The terminal saves position values during bounded and unbounded stack options
        // for position (P) commands and vector (V) commands". So the same option group has to work
        // inside a V - which it does because both read their options through the same code.
        Assert.Equal("[400,250]\r", Reports("P[400,250]V(B)[500,300]V(E)R(P)"));
    }

    // ─────────────────────────────────────────────────────────────
    // Table 10-1 codes 7 and 8 - the stack's own errors
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SeventeenPushesIsAnOverflowAndNamesTheOption()
    {
        // "You can save up to 16 positions in a stack." The seventeenth is code 7, and M is the
        // decimal ASCII code of the option blamed - 'B' is 66.
        var stream = "P[10,10]";
        for (int i = 0; i < 17; i++) stream += "(B)";
        stream += "R(E)";

        Assert.Equal("\"7,66\"\r", Reports(stream));
    }

    [Fact]
    public void SixteenPushesIsFine()
    {
        // The boundary from the other side. Without this, an off-by-one that rejected the sixteenth
        // would pass the overflow test above and be wrong.
        var stream = "P[10,10]";
        for (int i = 0; i < 16; i++) stream += "(B)";
        stream += "R(E)";

        Assert.Equal("\"0,0\"\r", Reports(stream));
    }

    [Fact]
    public void AnUnboundedOverflowBlamesTheStartOption()
    {
        // "M is (B) or (S)" - so the code alone is not enough, the reply has to say which kind of
        // push overflowed. 'S' is 83.
        var stream = "P[10,10]";
        for (int i = 0; i < 17; i++) stream += "(S)";
        stream += "R(E)";

        Assert.Equal("\"7,83\"\r", Reports(stream));
    }

    [Fact]
    public void AnEndWithNoBeginIsAnUnderflow()
    {
        // Code 8, "An (E) with no matching (B)", and M is (E) - 69.
        Assert.Equal("\"8,69\"\r", Reports("P[10,10]P(E)R(E)"));
    }

    [Fact]
    public void AnOverflowedPushIsNotPoppedByTheMatchingEnd()
    {
        // The seventeenth (B) was refused, so the seventeenth (E) has nothing of its own to pop and
        // takes the sixteenth instead. Sixteen pops later the stack is empty and the seventeenth
        // (E) underflows. What matters is that the DRAWING POINT is still right at the end: the
        // outermost saved position is the one that survived, which is why an overflow drops the
        // newest push rather than the oldest.
        var stream = "P[10,10]";
        for (int i = 0; i < 17; i++) stream += "(B)P[500,300]";
        for (int i = 0; i < 17; i++) stream += "P(E)";
        stream += "R(P)";

        Assert.Equal("[10,10]\r", Reports(stream));
    }

    // ─────────────────────────────────────────────────────────────
    // What a screen erase does to it
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AScreenEraseThrowsTheStacksAway()
    {
        // Chapter 4's list for the screen erase command: "Clears all position stacks." So the (E)
        // after the erase has nothing to pop and underflows, rather than restoring a position from
        // before the screen was cleared.
        Assert.Equal("\"8,69\"\r", Reports("P[400,250](B)P[250,50]S(E)P(E)R(E)"));
    }

    [Fact]
    public void AScreenEraseDoesNotMoveTheDrawingPoint()
    {
        // Second bullet of the same list: "Does not change the cursor position."
        //
        // This was WRONG in the decoder until 2026-08-20 - the erase homed the point to 0,0, which
        // is some other terminal's behaviour. It never showed because every fixture we hold opens
        // with S(E) while the point is already at 0,0; the two only disagree when a stream erases
        // after drawing.
        Assert.Equal("[250,50]\r", Reports("P[250,50]S(E)R(P)"));
    }

    [Fact]
    public void TheStackSurvivesOtherCommandsBetweenThePushAndThePop()
    {
        // "You can embed other commands between pairs of start and end commands. For example, you
        // can embed several vector (V) commands between the start and end commands."
        var decoder = Play("P[400,250](B)V[500,300]V[600,200]T'hello'P(E)R(P)");

        Assert.Equal("[400,250]\r", decoder.TakeReports());
    }
}
