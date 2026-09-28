using System;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// GIN — a pointer on the screen turned into the bytes a host expects back. The last block of the
/// graphics foundation, and the reverse of the keyboard path.
///
/// The encoding is pinned to a primary source: <c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>
/// carries the bit tags AND a worked example taken from a disassembled ND test program, and
/// <see cref="TheWorkedExampleFromTheSpecEncodesByteForByte"/> reproduces that example exactly. A
/// test that only checked the encoder against my own reading of the tags would agree with a wrong
/// encoder as happily as with a right one.
/// </summary>
public class GinRoutingTests
{
    // The real ND / Tek 4014 addressable space.
    private const int TekWidth = 1024;
    private const int TekHeight = 780;

    private static GraphicsViewport Tek(int surfaceWidth = 1024, int surfaceHeight = 780)
        => new GraphicsViewport(TekWidth, TekHeight, surfaceWidth, surfaceHeight,
            logicalYIncreasesUpward: true, preserveAspectRatio: false);

    // ─────────────────────────────────────────────────────────────
    // The encoding, against the spec's own example
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheWorkedExampleFromTheSpecEncodesByteForByte()
    {
        // Straight out of the spec: crosshair at X=512, Y=390, status byte 0x68, and the ND's two
        // extension bytes both 0x40.
        //
        //   Response: 0x68 0x2C 0x66 0x30 0x40 0x40 0x40
        //
        // If this ever fails, the encoder is wrong - not the test.
        Span<byte> report = stackalloc byte[TektronixGinEncoder.NorskDataReportLength];

        int length = TektronixGinEncoder.EncodeNorskData(report, 0x68, 512, 390, 0x40, 0x40);

        Assert.Equal(7, length);
        Assert.Equal(0x68, report[0]);
        Assert.Equal(0x2C, report[1]);
        Assert.Equal(0x66, report[2]);
        Assert.Equal(0x30, report[3]);
        Assert.Equal(0x40, report[4]);
        Assert.Equal(0x40, report[5]);
        Assert.Equal(0x40, report[6]);
    }

    [Fact]
    public void ARealTektronixSendsFiveBytesAndAnNdSendsSeven()
    {
        // THE difference a host detects on. Both answer the same standard ESC ENQ; the ND simply
        // answers with two more bytes, and those two say which ND it is.
        Span<byte> tek = stackalloc byte[16];
        Span<byte> nd = stackalloc byte[16];

        int tekLength = TektronixGinEncoder.Encode(tek, 0x68, 512, 390);
        int ndLength = TektronixGinEncoder.EncodeNorskData(nd, 0x68, 512, 390, 0x41, 0x42);

        Assert.Equal(5, tekLength);
        Assert.Equal(7, ndLength);

        // The first five are identical - the ND is 4014-compatible, not a different protocol.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(tek[i], nd[i]);
        }
    }

    [Fact]
    public void TheModelBytesAreMaskedToSevenBits()
    {
        // The receiving end ANDs each with 0x7F, so anything in the top bit is lost in transit.
        // Masking here means what we send is what a host reads back.
        Span<byte> report = stackalloc byte[TektronixGinEncoder.NorskDataReportLength];

        TektronixGinEncoder.EncodeNorskData(report, 0x68, 0, 0, 0xC1, 0xFF);

        Assert.Equal(0x41, report[5]);
        Assert.Equal(0x7F, report[6]);
    }

    [Fact]
    public void EveryCoordinateSurvivesTheRoundTrip()
    {
        // Decoding lives beside the encoder so the bit layout is stated once. Sweeping the whole
        // ten-bit range catches a tag bit that only collides on particular values.
        Span<byte> report = stackalloc byte[TektronixGinEncoder.StandardReportLength];

        for (int x = 0; x < 1024; x += 7)
        {
            for (int y = 0; y < 1024; y += 11)
            {
                TektronixGinEncoder.Encode(report, 0x68, x, y);

                Assert.True(TektronixGinEncoder.TryDecode(report, out int backX, out int backY));
                Assert.Equal(x, backX);
                Assert.Equal(y, backY);
            }
        }
    }

    [Fact]
    public void TheTagBitsSeparateTheFourCoordinateBytes()
    {
        // The tags are the only thing telling a host which byte is which. Getting X's and Y's low
        // tags the same way round would put the crosshair on the diagonal and look almost right.
        Span<byte> report = stackalloc byte[TektronixGinEncoder.StandardReportLength];
        TektronixGinEncoder.Encode(report, 0x68, 512, 390);

        Assert.Equal(0x20, report[1] & 0xE0);   // Hi Y
        Assert.Equal(0x60, report[2] & 0xE0);   // Lo Y
        Assert.Equal(0x20, report[3] & 0xE0);   // Hi X
        Assert.Equal(0x40, report[4] & 0xE0);   // Lo X
    }

    [Fact]
    public void SomethingThatIsNotAReportIsRejected()
    {
        Assert.False(TektronixGinEncoder.TryDecode(new byte[] { 0x68, 0x2C }, out _, out _));
        Assert.False(TektronixGinEncoder.TryDecode(
            new byte[] { 0x68, 0x00, 0x00, 0x00, 0x00 }, out _, out _));
    }

    [Fact]
    public void ACoordinateTooBigForTenBitsIsClampedNotWrapped()
    {
        // Wrapping would put the crosshair somewhere else entirely on screen and look like a
        // plausible position, which is far worse than pinning it to the edge.
        Span<byte> report = stackalloc byte[TektronixGinEncoder.StandardReportLength];
        TektronixGinEncoder.Encode(report, 0x68, 5000, -3);

        Assert.True(TektronixGinEncoder.TryDecode(report, out int x, out int y));
        Assert.Equal(1023, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void ATooSmallBufferIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var tooSmall = new byte[4];
            TektronixGinEncoder.Encode(tooSmall, 0x68, 0, 0);
        });
    }

    // ─────────────────────────────────────────────────────────────
    // The routing — modal, and it never invents a coordinate
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ATerminalDoesNotReportUntilTheHostAsks()
    {
        // GIN is modal, and this is the guard that matters: a terminal reporting pointer movement
        // unasked would corrupt every session that never wanted graphics at all.
        var router = new GinRouter(Tek());
        router.MoveCrosshair(100, 100);

        Span<byte> report = stackalloc byte[router.MaximumReportLength];

        Assert.False(router.IsArmed);
        Assert.False(router.TryBuildReport(0x41, report, out int length));
        Assert.Equal(0, length);
    }

    [Fact]
    public void AnArmedTerminalReportsWhereTheCrosshairIs()
    {
        var router = new GinRouter(Tek());
        router.Arm();
        router.MoveCrosshair(512, 389);      // surface pixels, top-left origin

        Span<byte> report = stackalloc byte[router.MaximumReportLength];
        Assert.True(router.TryBuildReport(0x41, report, out int length));

        Assert.Equal(5, length);
        Assert.Equal(0x41, report[0]);       // the key the user pressed

        Assert.True(TektronixGinEncoder.TryDecode(report, out int x, out int y));
        Assert.Equal(512, x);
        Assert.Equal(390, y);                // 779 - 389: the Y flip, done by the viewport
    }

    [Fact]
    public void TheReportUsesTheTerminalsOwnSpaceNotTheScreens()
    {
        // A pointer at the BOTTOM of the screen is the ORIGIN to a Tek host. Reporting the surface
        // coordinate instead would put every pick upside down, and a lot of vector work looks
        // plausible upside down.
        var router = new GinRouter(Tek());
        router.Arm();
        router.MoveCrosshair(0, 779);        // bottom-left pixel of the surface

        Span<byte> report = stackalloc byte[router.MaximumReportLength];
        router.TryBuildReport(0x41, report, out _);

        Assert.True(TektronixGinEncoder.TryDecode(report, out int x, out int y));
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void OneArmingGivesOneReport()
    {
        // A real terminal leaves GIN mode when it reports. Staying armed would send a second,
        // unasked-for report the next time the user touched anything.
        var router = new GinRouter(Tek());
        router.Arm();
        router.MoveCrosshair(100, 100);

        Span<byte> report = stackalloc byte[router.MaximumReportLength];

        Assert.True(router.TryBuildReport(0x41, report, out _));
        Assert.False(router.IsArmed);
        Assert.False(router.TryBuildReport(0x41, report, out _));
    }

    [Fact]
    public void MovingTheCrosshairDoesNotReport()
    {
        // A real terminal reports when the user ACTS, not on every twitch of the pointer.
        // Streaming a report per mouse move would flood the line.
        var router = new GinRouter(Tek());
        router.Arm();

        router.MoveCrosshair(10, 10);
        router.MoveCrosshair(20, 20);
        router.MoveCrosshair(30, 30);

        Assert.True(router.IsArmed);          // still waiting for the user to do something

        Span<byte> report = stackalloc byte[router.MaximumReportLength];
        router.TryBuildReport(0x41, report, out _);
        TektronixGinEncoder.TryDecode(report, out int x, out _);
        Assert.Equal(30, x);                  // and it reports where the pointer ended up
    }

    [Fact]
    public void DisarmingCancelsWithoutReporting()
    {
        var router = new GinRouter(Tek());
        router.Arm();
        router.Disarm();

        Span<byte> report = stackalloc byte[router.MaximumReportLength];
        Assert.False(router.TryBuildReport(0x41, report, out _));
    }

    [Fact]
    public void AnNdRouterAnswersWithItsModelBytes()
    {
        var router = new GinRouter(Tek())
        {
            ReportAsNorskData = true,
            NorskDataModelByte1 = 0x41,
            NorskDataModelByte2 = 0x31,
        };
        router.Arm();
        router.MoveCrosshair(512, 389);

        Span<byte> report = stackalloc byte[router.MaximumReportLength];
        Assert.True(router.TryBuildReport(0x68, report, out int length));

        Assert.Equal(7, length);
        Assert.Equal(0x41, report[5]);
        Assert.Equal(0x31, report[6]);
    }

    [Fact]
    public void APointerInTheLetterboxStillReportsAValidPick()
    {
        // With the aspect ratio preserved the pointer can sit in the margin. A crosshair report of
        // "-3" is not something a host can act on, so the viewport clamps it to the edge.
        var viewport = new GraphicsViewport(TekWidth, TekHeight, 2000, 780,
            logicalYIncreasesUpward: true, preserveAspectRatio: true);
        var router = new GinRouter(viewport);
        router.Arm();
        router.MoveCrosshair(0, 0);          // top-left, inside the left-hand letterbox

        Span<byte> report = stackalloc byte[router.MaximumReportLength];
        router.TryBuildReport(0x41, report, out _);

        Assert.True(TektronixGinEncoder.TryDecode(report, out int x, out int y));
        Assert.InRange(x, 0, TekWidth - 1);
        Assert.InRange(y, 0, TekHeight - 1);
    }
}
