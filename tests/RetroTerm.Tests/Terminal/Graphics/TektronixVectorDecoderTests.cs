using System.Collections.Generic;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// Decoding the Tektronix coordinate stream — the direction a host DRAWS in, and the mirror of the
/// GIN encoder.
///
/// The byte layout is the same one the encoder uses and is quoted in
/// <c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>. What the spec does NOT document is which
/// bytes a host may leave out when they have not changed, which is the whole reason this is a
/// decoder rather than a four-byte read: a long horizontal line sends two bytes per point, not
/// four. That rule is standard Tektronix 4010/4014 behaviour, applied here because a decoder
/// demanding all four bytes would drop most real drawing traffic — derived, not quoted, and not
/// confirmed against hardware. These tests pin the derived behaviour so the day someone checks it
/// on a real terminal, the disagreement is visible.
/// </summary>
public class TektronixVectorDecoderTests
{
    private static (TektronixVectorDecoder Decoder, List<(int X, int Y)> Points) Build()
    {
        var decoder = new TektronixVectorDecoder();
        var points = new List<(int, int)>();
        decoder.PointReady += (x, y) => points.Add((x, y));
        return (decoder, points);
    }

    private static void Feed(TektronixVectorDecoder decoder, params byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            decoder.Consume(bytes[i]);
        }
    }

    private const byte Gs = 0x1D;
    private const byte Fs = 0x1C;
    private const byte Us = 0x1F;
    private const byte Can = 0x18;
    private const byte Rs = 0x1E;

    /// <summary>
    /// The four bytes for a point, in the order a host sends them.
    /// </summary>
    private static byte[] Coordinate(int x, int y) => new[]
    {
        (byte)(0x20 | ((y >> 5) & 0x1F)),
        (byte)(0x60 | (y & 0x1F)),
        (byte)(0x20 | ((x >> 5) & 0x1F)),
        (byte)(0x40 | (x & 0x1F)),
    };

    // ─────────────────────────────────────────────────────────────
    // Modes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ATerminalStartsInText()
    {
        // Bytes are characters until a host says otherwise. A decoder that started in graph mode
        // would eat the first line of every ordinary session.
        var (decoder, _) = Build();

        Assert.Equal(TektronixMode.Alpha, decoder.Mode);
        Assert.False(decoder.Consume((byte)'A'), "text must be left for the terminal to print");
    }

    [Fact]
    public void GsEntersGraphModeAndUsLeavesIt()
    {
        var (decoder, _) = Build();

        Feed(decoder, Gs);
        Assert.Equal(TektronixMode.Graph, decoder.Mode);

        Feed(decoder, Us);
        Assert.Equal(TektronixMode.Alpha, decoder.Mode);
    }

    [Fact]
    public void FsEntersPointPlot()
    {
        // The spec's own trace shows FS followed by NUL at ram:c794, entering point plot.
        var (decoder, _) = Build();

        Feed(decoder, Fs);

        Assert.Equal(TektronixMode.PointPlot, decoder.Mode);
    }

    [Fact]
    public void TextIsNotSwallowedWhileInAlphaMode()
    {
        var (decoder, points) = Build();

        Feed(decoder, Gs);
        Feed(decoder, Us);
        Assert.False(decoder.Consume((byte)'H'));
        Assert.Empty(points);
    }

    [Fact]
    public void CancelDropsBackToText()
    {
        // CAN appears mid-stream in the real command traces. Swallowing it would strand the
        // terminal in graph mode with the host talking text at it.
        var (decoder, _) = Build();
        Feed(decoder, Gs);

        Feed(decoder, Can);

        Assert.Equal(TektronixMode.Alpha, decoder.Mode);
    }

    // ─────────────────────────────────────────────────────────────
    // Coordinates
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AFullCoordinateDecodesToItsPoint()
    {
        var (decoder, points) = Build();
        Feed(decoder, Gs);

        Feed(decoder, Coordinate(512, 390));

        Assert.Equal(new[] { (512, 390) }, points);
    }

    [Fact]
    public void EveryPointInTheSpaceSurvivesTheRoundTrip()
    {
        // Sweeping the range catches a tag bit that only collides on particular values - the same
        // property the GIN encoder is held to, from the other side.
        var (decoder, points) = Build();
        Feed(decoder, Gs);

        var expected = new List<(int, int)>();
        for (int x = 0; x < 1024; x += 37)
        {
            for (int y = 0; y < 780; y += 53)
            {
                Feed(decoder, Coordinate(x, y));
                expected.Add((x, y));
            }
        }

        Assert.Equal(expected, points);
    }

    [Fact]
    public void OnlyTheLowXEndsACoordinate()
    {
        // Three bytes in and nothing has been drawn yet. A decoder that emitted early would put a
        // point at a half-read position.
        var (decoder, points) = Build();
        Feed(decoder, Gs);

        var bytes = Coordinate(512, 390);
        Feed(decoder, bytes[0], bytes[1], bytes[2]);

        Assert.Empty(points);

        Feed(decoder, bytes[3]);
        Assert.Single(points);
    }

    // ─────────────────────────────────────────────────────────────
    // The omitted bytes — the part that is derived rather than quoted
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AHostMayLeaveOutBytesThatHaveNotChanged()
    {
        // THE reason this is a decoder. A long horizontal line changes only the low X, so a host
        // sends ONE byte per point after the first. Demanding all four would drop most real
        // drawing traffic.
        var (decoder, points) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(100, 390));

        // Same high X, same Y: just a new low X.
        Feed(decoder, (byte)(0x40 | 5));

        Assert.Equal(2, points.Count);
        Assert.Equal((100 & ~0x1F) | 5, points[1].X);
        Assert.Equal(390, points[1].Y);
    }

    [Fact]
    public void AHighByteBeforeTheLowYIsTheHighY()
    {
        // The positional rule, stated as its own test because the byte ranges alone do not give
        // it: high Y and high X are the SAME range, and only their position separates them.
        var (decoder, points) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(0, 0));

        // High byte, then low Y, then low X - no high X at all, so it keeps the previous one.
        Feed(decoder, (byte)(0x20 | 12), (byte)(0x60 | 6), (byte)(0x40 | 0));

        Assert.Equal(2, points.Count);
        Assert.Equal(390, points[1].Y);      // (12 << 5) | 6
    }

    [Fact]
    public void AHighByteAfterTheLowYIsTheHighX()
    {
        var (decoder, points) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(0, 0));

        // Low Y first, THEN a high byte: that one belongs to X.
        Feed(decoder, (byte)(0x60 | 6), (byte)(0x20 | 16), (byte)(0x40 | 0));

        Assert.Equal(2, points.Count);
        Assert.Equal(512, points[1].X);      // (16 << 5) | 0
        Assert.Equal(6, points[1].Y);
    }

    // ─────────────────────────────────────────────────────────────
    // Moves versus draws
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheFirstPointAfterEnteringGraphModeIsAMove()
    {
        // Otherwise a host entering graph mode draws a line from wherever the last drawing ended -
        // a stray diagonal across the picture that looks like a corrupted vector rather than the
        // bookkeeping error it is.
        var (decoder, _) = Build();

        Feed(decoder, Gs);
        Assert.True(decoder.NextPointStartsAFigure);

        Feed(decoder, Coordinate(100, 100));
        Assert.False(decoder.NextPointStartsAFigure);
    }

    [Fact]
    public void LeavingAndReenteringGraphModeStartsAFreshFigure()
    {
        var (decoder, _) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(100, 100));

        Feed(decoder, Us);
        Feed(decoder, Gs);

        Assert.True(decoder.NextPointStartsAFigure);
    }

    [Fact]
    public void AModeChangeDropsAHalfReadCoordinate()
    {
        // A coordinate interrupted by a mode switch is not a coordinate. Carrying its bytes into
        // the next one would put a point somewhere nobody asked for.
        var (decoder, points) = Build();
        Feed(decoder, Gs);

        var bytes = Coordinate(512, 390);
        Feed(decoder, bytes[0], bytes[1]);      // half a coordinate
        Feed(decoder, Us);
        Feed(decoder, Gs);
        Feed(decoder, bytes[2], bytes[3]);      // high X, low X

        // The low Y from before the mode change must not have survived into this point.
        Assert.Single(points);
        Assert.NotEqual(390, points[0].Y);
    }

    // ─────────────────────────────────────────────────────────────
    // The 4014's five-byte address
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AFiveByteFourZeroOneFourAddressDecodesToTheRightPoint()
    {
        // A 4014 with the Enhanced Graphics Module addresses 4096x4096 and sends FIVE bytes, not
        // four. Table F-2 of the manual:
        //
        //   High Order Y   tag 01    5 MSB of Y
        //   Extra Byte     tag 11    the two least significant bits of Y and of X
        //   Low Order Y    tag 11    5 intermediate bits of Y
        //   High Order X   tag 01    5 MSB of X
        //   Low Order X    tag 10    5 intermediate bits of X
        //
        // The Extra Byte shares its tag range with Low Order Y, so the two are told apart by
        // position: when two of them arrive in a row, the first is the Extra.
        //
        // This decoder keeps 10-bit precision, which is what a 1024x780 surface can show. Reading
        // the Extra Byte as if it were the Low Order Y and then being overwritten by the real one
        // lands on exactly the right 10-bit point, discarding only the two extra bits of
        // precision. That is the correct DEGRADATION rather than a bug - a 4010 program and a 4014
        // program both plot in the right place - and it is asserted here rather than assumed,
        // because "it happens to work" and "it works" look identical until someone changes it.
        var (decoder, points) = Build();
        Feed(decoder, Gs);

        int x = 512, y = 390;
        Feed(decoder,
            (byte)(0x20 | ((y >> 5) & 0x1F)),   // High Order Y
            (byte)(0x60 | 0x03),                // Extra Byte - low bits of both axes
            (byte)(0x60 | (y & 0x1F)),          // Low Order Y
            (byte)(0x20 | ((x >> 5) & 0x1F)),   // High Order X
            (byte)(0x40 | (x & 0x1F)));         // Low Order X

        Assert.Equal(new[] { (512, 390) }, points);
    }

    // ─────────────────────────────────────────────────────────────
    // Point plot, and the mode that is deliberately not implemented
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PointPlotStillDecodesCoordinates()
    {
        // FS uses the SAME four-byte coordinate as GS. The difference is what a terminal does with
        // the result - light one dot rather than join it to the last - so the decoding is shared
        // and only the drawing differs.
        var (decoder, points) = Build();
        Feed(decoder, Fs);

        Feed(decoder, Coordinate(300, 200));

        Assert.Equal(new[] { (300, 200) }, points);
    }

    [Theory]
    // Straight from Table F-5 of the 4014 manual, Appendix F. North is +Y because this is the
    // terminal's own space, where Y increases UPWARD.
    [InlineData('D', 0, 1)]     // N
    [InlineData('E', 1, 1)]     // NE
    [InlineData('A', 1, 0)]     // E
    [InlineData('I', 1, -1)]    // SE
    [InlineData('H', 0, -1)]    // S
    [InlineData('J', -1, -1)]   // SW
    [InlineData('B', -1, 0)]    // W
    [InlineData('F', -1, 1)]    // NW
    public void EachIncrementalDirectionMovesOneStep(char direction, int expectedX, int expectedY)
    {
        var (decoder, points) = Build();

        // Position first in graph mode, as the manual says to: "the display beam may be addressed
        // to the desired starting point in Graph Vector Mode; then placed in Incremental Plot Mode
        // by RS".
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(500, 400));
        Feed(decoder, Rs, (byte)'P', (byte)direction);

        Assert.Equal(500 + expectedX, points[points.Count - 1].X);
        Assert.Equal(400 + expectedY, points[points.Count - 1].Y);
    }

    [Fact]
    public void TheBeamSettingIsStickyAcrossSteps()
    {
        // "The write status does not change until a different write command is received." So one
        // P covers every step after it, which is what makes an incremental plot compact.
        var (decoder, _) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(500, 400));

        Feed(decoder, Rs, (byte)'P', (byte)'A', (byte)'A', (byte)'A');

        Assert.True(decoder.IncrementalBeamOn);
        Assert.Equal(503, decoder.LastX);
    }

    [Fact]
    public void APenUpStepMovesWithoutDrawing()
    {
        // Reporting a pen-up step as a draw joins every repositioning with a line, which is how an
        // incremental plot turns into a scribble.
        var (decoder, _) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(500, 400));

        Feed(decoder, Rs, (byte)' ', (byte)'A');

        Assert.False(decoder.IncrementalBeamOn);
        Assert.True(decoder.NextPointStartsAFigure, "a pen-up step must not draw");
        Assert.Equal(501, decoder.LastX);
    }

    [Fact]
    public void APenDownStepDraws()
    {
        var (decoder, _) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(500, 400));

        Feed(decoder, Rs, (byte)'P', (byte)'A');

        Assert.False(decoder.NextPointStartsAFigure, "a pen-down step must draw");
    }

    [Fact]
    public void AnUnknownIncrementalByteIsCountedNotActedOn()
    {
        var (decoder, points) = Build();
        Feed(decoder, Rs);

        Feed(decoder, (byte)'Z', (byte)'?');

        Assert.Empty(points);
        Assert.Equal(2, decoder.IncrementalBytesIgnored);
    }

    [Fact]
    public void LeavingIncrementalModeResumesNormalDecoding()
    {
        // Swallowing must be confined to the mode. A terminal that kept dropping bytes after
        // leaving RS would go silent for the rest of the session.
        var (decoder, points) = Build();
        Feed(decoder, Rs);
        Feed(decoder, Coordinate(512, 390));

        Feed(decoder, Gs);
        Feed(decoder, Coordinate(100, 200));

        Assert.Equal(new[] { (100, 200) }, points);
    }

    [Fact]
    public void ResetPutsEverythingBack()
    {
        var (decoder, _) = Build();
        Feed(decoder, Gs);
        Feed(decoder, Coordinate(512, 390));

        decoder.Reset();

        Assert.Equal(TektronixMode.Alpha, decoder.Mode);
        Assert.True(decoder.NextPointStartsAFigure);
        Assert.Equal(0, decoder.LastX);
        Assert.Equal(0, decoder.LastY);
    }
}
