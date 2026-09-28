using System;
using System.IO;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Conformance;

/// <summary>
/// Real Tektronix output, produced by gnuplot, fed through our decoder.
///
/// WHY IT IS HERE. Every other graphics test in this repo sends bytes that this codebase also
/// wrote — my encoder feeding my decoder — so a misreading shared by both is invisible. These
/// fixtures come from gnuplot's <c>tek40xx</c> driver, which has been emitting Tektronix streams
/// for thirty years and has never heard of RetroTerm. That makes them the same kind of outside
/// opinion the libvterm corpus is for text, and they found a real defect on first contact.
///
/// Regenerate with (inside WSL, gnuplot-nox installed):
/// <code>
///   gnuplot -e "set terminal tek40xx; set output 'sin.tek40xx'; plot [-6:6] sin(x)"
/// </code>
///
/// A NOTE ON WHICH TEKTRONIX. gnuplot's driver is named for the 4010, not the 4014 — its own
/// listing says "Tektronix 4010 and others; most TEK emulators". The 4010 stream is a strict
/// subset of what a 4014 accepts, so it exercises the decoder honestly but does NOT cover the
/// 4014's extra precision byte. gnuplot's <c>xterm</c> driver is the 4014 one, and it writes to a
/// terminal rather than to a file, so it could not be captured this way.
/// </summary>
public class TektronixCorpusTests
{
    private static string CorpusFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(TektronixCorpusTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "tektronix"));

    private static byte[] Load(string name)
    {
        var path = Path.Combine(CorpusFolder, name);
        Assert.True(File.Exists(path), $"fixture missing: {path}");
        return File.ReadAllBytes(path);
    }

    private static string ScreenText(TDV2200Emulator emulator)
    {
        var buffer = emulator.GetBuffer();
        var text = new StringBuilder();
        for (int row = 0; row < buffer.Height; row++)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                uint cp = buffer.GetCell(row, col).Codepoint;
                text.Append(cp == 0 ? ' ' : (char)cp);
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    [Theory]
    [InlineData("sin.tek40xx")]
    [InlineData("box.tek40xx")]
    [InlineData("points.tek40xx")]
    public void TheFixtureIsVendoredAndLooksLikeTektronix(string name)
    {
        // Cheap guard against a fixture that got mangled in transit: a Tek stream is mostly
        // coordinate bytes punctuated by GS, and if the GS bytes are gone it is not one.
        var bytes = Load(name);

        Assert.True(bytes.Length > 500, "a real plot is not a handful of bytes");

        int groupSeparators = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0x1D) groupSeparators++;
        }

        Assert.True(groupSeparators > 10, $"{name} should be full of GS; found {groupSeparators}");
    }

    [Theory]
    [InlineData("sin.tek40xx")]
    [InlineData("box.tek40xx")]
    [InlineData("points.tek40xx")]
    public void ARealPlotDrawsSomething(string name)
    {
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(new byte[] { 0x1B, (byte)'"', (byte)'1', (byte)'7', (byte)'h' });

        emulator.ProcessData(Load(name));

        Assert.True(emulator.Graphics!.HasAnythingToDraw(),
            $"{name} produced no graphics at all");
    }

    [Fact]
    public void TheVeryFirstVectorOfARealStreamIsNotLost()
    {
        // Every gnuplot stream opens ESC FF - the Tektronix ERASE SCREEN command - and goes
        // straight into GS and a coordinate. That opening is worth its own test because the
        // corpus tests above cannot see it fail: a stream with sixty GS in it recovers on the
        // second one, draws a plot that looks right, and quietly loses the first vector.
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(new byte[] { 0x1B, (byte)'"', (byte)'1', (byte)'7', (byte)'h' });

        // ESC FF, GS, then the first coordinate of sin.tek40xx: (91, 50).
        emulator.ProcessData(new byte[] { 0x1B, 0x0C, 0x1D, 0x21, 0x72, 0x22, 0x5B });

        var vectors = emulator.GraphicsModule!.Vectors;

        Assert.Equal(RetroTerm.Core.Terminal.Graphics.TektronixMode.Graph, vectors.Mode);
        Assert.Equal(91, vectors.LastX);
        Assert.Equal(50, vectors.LastY);
    }

    [Fact]
    public void LeavingGraphModePutsTheTextCursorAtTheLastPoint()
    {
        // How a Tektronix host writes a label anywhere on screen: move the beam to where the text
        // belongs, drop back to alpha, print. Without it every label lands wherever the previous
        // one ended, and a plot's axis numbers come out in a diagonal cascade instead of beside
        // their tick marks.
        //
        // Derived rather than quoted - the ND spec does not mention it - but it is standard
        // Tek 4010/4014 behaviour and gnuplot's output plainly depends on it.
        var emulator = new TDV2200Emulator(80, 24);

        // GS, move to the middle of the space, US, then a label.
        emulator.ProcessData(new byte[] { 0x1D, 0x30, 0x60, 0x30, 0x40, 0x1F });
        emulator.ProcessData(Encoding.ASCII.GetBytes("X"));

        var buffer = emulator.GetBuffer();

        // Logical (512, 512) on a 1024x780 space over an 80x24 grid: column 40, and a row near the
        // top because Y runs UP in Tek space and DOWN the screen.
        Assert.Equal('X', (char)buffer.GetCell(8, 40).Codepoint);
        Assert.True(buffer.GetCell(0, 0).IsEmpty, "the label must not land at the home position");
    }

    // A WHOLE-PICTURE TEST FOR LABEL PLACEMENT WAS TRIED AND DELETED, twice.
    //
    // "Labels appear on many rows" passes without cursor placement, because printed sequentially
    // they simply wrap and fill rows anyway. "Rows begin at many distinct columns" passes too, for
    // much the same reason. Both looked like they were checking placement and neither could fail.
    //
    // The precise test above does discriminate, and the picture is what covers the rest: the
    // rendered PNGs in Avalonia\images\rendered\tek-corpus-*.png are there to be looked at, and
    // "do the axis numbers sit beside their tick marks" is a judgement rather than an assertion.
    // A test that cannot fail is worse than no test, so there is no test here.

    [Fact]
    public void TheAxisLabelsArePrintedAsText()
    {
        // gnuplot's output is MIXED: vectors for the curve, and ordinary text for the axis labels
        // and the key. Both have to work at once, which is the sharpest test of the mode handling -
        // a decoder that stays in graph mode eats the labels, and one that never enters it prints
        // the curve as punctuation.
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Load("sin.tek40xx"));

        var screen = ScreenText(emulator);

        Assert.Contains("sin(x)", screen);
    }

    [Fact]
    public void TheCurveIsNotPrintedAsPunctuation()
    {
        // Coordinate bytes are ordinary printable ASCII. A terminal that misses the mode switch
        // fills the screen with them, and the giveaway is a screen far fuller than a few axis
        // labels would ever make it.
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Load("sin.tek40xx"));

        var screen = ScreenText(emulator);

        int printable = 0;
        for (int i = 0; i < screen.Length; i++)
        {
            if (screen[i] > ' ') printable++;
        }

        // The labels of a sin plot are a few dozen characters. Hundreds means the vectors leaked.
        Assert.True(printable < 200,
            $"{printable} characters on screen - the coordinate stream is being printed as text");
    }
}
