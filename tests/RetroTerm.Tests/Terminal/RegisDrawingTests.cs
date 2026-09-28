using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// ReGIS drawings arriving at a terminal: DCS p, onto a plane of their own.
///
/// The decoder is tested against a bare surface. This is the wiring — which terminals accept a
/// drawing, what it costs one that never sees one, and the separation from Sixel that keeps one
/// protocol's erase from wiping the other's picture.
/// </summary>
public class RegisDrawingTests
{
    private static TerminalEmulatorBase Build(string type = "VT340")
        => EmulatorFactory.CreateEmulator(type, 80, 24, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void ADrawingReachesTheScreen()
    {
        var emulator = Build();

        Feed(emulator, "\x1bPpP[10,10]V[100,10]\x1b\\");

        Assert.NotNull(emulator.Graphics);
        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }

    [Fact]
    public void ATerminalThatNeverSeesOnePaysNothing()
    {
        var emulator = Build();

        Feed(emulator, "just text");

        Assert.Null(emulator.Graphics);
    }

    [Fact]
    public void ATerminalWithoutTheFeatureIgnoresIt()
    {
        // A VT220 has no ReGIS. Accepting one there would be inventing a machine.
        var emulator = Build("VT220");

        Feed(emulator, "\x1bPpP[10,10]V[100,10]\x1b\\");

        Assert.Null(emulator.Graphics);
    }

    [Fact]
    public void TheDrawingPointSurvivesBetweenSequences()
    {
        // A real terminal keeps its pen where it was: a host is entitled to position in one DCS and
        // draw in the next, and several do exactly that to keep each sequence short.
        var emulator = Build();

        Feed(emulator, "\x1bPpP[10,10]\x1b\\");
        Feed(emulator, "\x1bPpV[100,10]\x1b\\");

        emulator.Graphics!.Composite();
        Assert.False(emulator.Graphics.Output.GetPixel(50, 10).IsTransparent);
    }

    [Fact]
    public void ReGisEraseDoesNotTakeASixelImageWithIt()
    {
        // THE reason the two protocols have separate planes. S(E) is ReGIS's erase; a Sixel image
        // is not part of a ReGIS drawing and must survive it.
        var emulator = Build();

        Feed(emulator, "\x1b[1;1H");
        Feed(emulator, "\x1bPq#1~\x1b\\");                       // a Sixel image at the top left
        Feed(emulator, "\x1bPpP[10,10]V[100,10]\x1b\\");         // and a ReGIS line
        Feed(emulator, "\x1bPpS(E)\x1b\\");                      // erase the ReGIS drawing only

        emulator.Graphics!.Composite();
        var output = emulator.Graphics.Output;

        Assert.False(output.GetPixel(0, 0).IsTransparent, "the Sixel image should still be there");
        Assert.True(output.GetPixel(50, 10).IsTransparent, "the ReGIS line should be gone");
    }

    [Fact]
    public void TheCounterIsReadableFromTheTerminal()
    {
        // Pointed at a real host this says which ReGIS commands actually matter, which beats
        // working down the manual in order.
        var emulator = Build();

        // All ten ReGIS commands are implemented as of 2026-08-18, so what the counter reports now
        // is the sub-options inside them that are not. This used to use R(I1), graphics input mode,
        // which was built on 2026-08-20 - so it counts nothing any more and a report option chapter
        // 10 does not define stands in for it.
        Feed(emulator, "\x1bPpP[0,0]R(Q)\x1b\\");

        Assert.NotNull(emulator.Regis);
        Assert.Equal(1, emulator.Regis!.UnhandledCommands['R']);
    }

    [Fact]
    public void TheVt340NowClaimsReGis()
    {
        // 3 is ReGIS, and it went into this list last - only once a drawing actually reached the
        // screen.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, "\x1b[c");

        Assert.Equal("\x1b[?63;1;3;4;6;7;8c", replies.ToString());
    }

    [Fact]
    public void AResetTakesTheDrawingAway()
    {
        var emulator = Build();
        Feed(emulator, "\x1bPpP[10,10]V[100,10]\x1b\\");

        emulator.Reset();
        emulator.Graphics?.Composite();

        Assert.False(emulator.Graphics?.HasAnythingToDraw() ?? false);
    }
}
