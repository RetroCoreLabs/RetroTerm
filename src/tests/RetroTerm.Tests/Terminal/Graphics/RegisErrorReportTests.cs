using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// <c>R(E)</c> - the report command's error condition option.
/// </summary>
/// <remarks>
/// <para><b>Table 10-1, read off the rendered page</b></para>
/// The reply is <c>"N,M"</c> in double quotes, where N is the code and M is "the decimal ASCII code
/// of the character flagged as the cause of the error or 0, as noted for each error code". The ten
/// codes are 0 no error, 1 ignore character, 2 extra option coordinates, 3 extra coordinate values,
/// 4 alphabet out of range, 5 and 6 reserved, 7 begin/start overflow, 8 begin/start underflow, and
/// 9 text standard size error.
/// <para><b>All eight a terminal can raise are now wired</b></para>
/// 1, 2, 3, 4, 7, 8 and 9, plus 0 for a clean stream. 5 and 6 are reserved by DEC and nothing sets
/// them. Codes 7 and 8 belong to the position stack and are exercised in
/// <c>RegisPositionStackTests</c>, beside the feature that raises them.
/// </remarks>
public class RegisErrorReportTests
{
    /// <summary>
    /// Plays a stream and returns what the decoder owes the host.
    /// </summary>
    /// <param name="commands">
    /// The ReGIS to play.
    /// </param>
    /// <returns>
    /// The reports, with their trailing carriage returns.
    /// </returns>
    private static string Play(string commands)
    {
        var surface = new InMemoryGraphicsSurface(64, 64);
        var decoder = new RegisDecoder();
        decoder.Decode(commands, surface);
        return decoder.HasReports ? decoder.TakeReports() : string.Empty;
    }

    [Fact]
    public void ACleanStreamReportsNoError()
    {
        Assert.Equal("\"0,0\"\r", Play("P[10,10]V[20,20]R(E)"));
    }

    [Fact]
    public void AnIgnoredCharacterIsCode1AndNamesTheCharacter()
    {
        // "An unexpected character was found and ignored", and M is the ignored character rather
        // than 0 - the only code in the table where M carries a character.
        Assert.Equal("\"1,33\"\r", Play("P[10,10]!R(E)"));
    }

    [Fact]
    public void TheResynchronizationCharacterClearsTheError()
    {
        // "You can use the resynchronization character (;) to clear errors."
        Assert.Equal("\"0,0\"\r", Play("P[10,10]!;R(E)"));
    }

    [Fact]
    public void OnlyTheLastErrorIsKept()
    {
        // "This option tells ReGIS to report the LAST error detected by the parser."
        Assert.Equal("\"1,35\"\r", Play("!#R(E)"));
    }

    [Fact]
    public void ATextSizeAboveSixteenIsCode9()
    {
        // "A text command selected a standard character size number of less than 0 or greater
        // than 16."
        Assert.Equal("\"9,0\"\r", Play("T(S17)R(E)"));
    }

    [Fact]
    public void ATextSizeInsideTheRangeIsNotAnError()
    {
        Assert.Equal("\"0,0\"\r", Play("T(S16)R(E)"));
    }

    [Fact]
    public void AnAlphabetAboveThreeIsCode4()
    {
        // "The syntax L(A<0 to 3>) contained a number less than 0 or greater than 3."
        Assert.Equal("\"4,0\"\r", Play("L(A9)R(E)"));
    }

    [Fact]
    public void AnAlphabetInsideTheRangeIsNotAnError()
    {
        Assert.Equal("\"0,0\"\r", Play("L(A2)R(E)"));
    }

    [Fact]
    public void AThirdValueInACoordinateIsCode3()
    {
        // "The syntax [X,Y] contained more than two coordinate values. The extra values were
        // ignored." M is always 0 - unlike code 1, no character is blamed.
        Assert.Equal("\"3,0\"\r", Play("P[10,20,30]R(E)"));
    }

    [Fact]
    public void AnOrdinaryCoordinateIsNotAnError()
    {
        // The boundary from the other side: two values is the normal case and must stay silent, or
        // every stream ever written would report an error.
        Assert.Equal("\"0,0\"\r", Play("P[10,20]R(E)"));
    }

    [Fact]
    public void TheExtraValueIsIgnoredRatherThanUsed()
    {
        // "The extra values were ignored" - so the point is 10,20 and the 30 goes nowhere. An
        // implementation that let the third value reach Y would report the error and still draw in
        // the wrong place.
        Assert.Equal("[10,20]\r", Play("P[10,20,30]R(P)"));
    }

    [Fact]
    public void AThirdCoordinatePairInAHardCopyIsCode2()
    {
        // "The syntax S(H[X,Y][X,Y]) contained more than two coordinate pairs. The extra pairs were
        // ignored." Chapter 2 gives the hard copy control three legal forms: no position, one, or
        // two opposing corners.
        Assert.Equal("\"2,0\"\r", Play("S(H[0,0][10,10][20,20])R(E)"));
    }

    [Fact]
    public void TwoCoordinatePairsInAHardCopyIsTheLegalMaximum()
    {
        Assert.Equal("\"0,0\"\r", Play("S(H[0,0][10,10])R(E)"));
    }

    [Fact]
    public void AHardCopyDoesNotMoveTheDrawingPoint()
    {
        // Naming an area to PRINT is not a drawing move. Putting the pairs through the ordinary
        // coordinate reader would have dragged the point to the corner of the print area.
        Assert.Equal("[400,250]\r", Play("P[400,250]S(H[0,0][10,10])R(P)"));
    }

    [Fact]
    public void AskingTwiceReportsTheSameThingBecauseAskingIsNotClearing()
    {
        // Only ";" clears. A report that cleared its own error would make two consecutive asks
        // disagree, and the manual gives the clearing job to the resynchronization character alone.
        Assert.Equal("\"1,33\"\r\"1,33\"\r", Play("!R(E)R(E)"));
    }
}
