using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Answering the detection probe — without which a real ND program never sends any graphics.
///
/// The ND test program's detection sends <c>US</c>, <c>ESC "13;10l</c>, <c>ESC "5d</c>,
/// <c>ESC ENQ</c> and then reads bytes back. Its validator FAILS if fewer than seven arrive
/// (<c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>, ram:c6c3-c701). A real 4014 sends five —
/// a status byte and the crosshair position — and an ND sends seven, the same five plus two 7-bit
/// bytes naming the model. So the length is the whole detection, and a terminal that stays silent
/// is simply not a graphics terminal as far as the host is concerned.
///
/// This is also the first thing that could not have been written before the parser stopped
/// abandoning an escape sequence on a C0: <c>ESC ENQ</c> and a bare <c>ENQ</c> were the same byte
/// with no context, and answering both would have sent a screen report to a host that asked for
/// nothing.
/// </summary>
public class NorskDataIdentificationTests
{
    private static (TDV2200Emulator Emulator, List<byte[]> Sent) Build()
    {
        var emulator = new TDV2200Emulator(20, 4);
        var sent = new List<byte[]>();
        emulator.DataToSend += bytes => sent.Add(bytes);
        return (emulator, sent);
    }

    private static void Feed(TDV2200Emulator emulator, params byte[] bytes)
        => emulator.ProcessData(bytes);

    private static void Feed(TDV2200Emulator emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private const byte Esc = 0x1B;
    private const byte Enq = 0x05;

    [Fact]
    public void EscEnqIsAnsweredWithSevenBytes()
    {
        // THE test. Seven, not five: that is what says "this is a Norsk Data terminal".
        var (emulator, sent) = Build();

        Feed(emulator, Esc, Enq);

        Assert.Single(sent);
        Assert.Equal(7, sent[0].Length);
    }

    [Fact]
    public void ABareEnqIsNotAnswered()
    {
        // A lone ENQ is not an identification request, and replying to it would push a screen
        // report at a host that asked for nothing - in the middle of whatever it was reading.
        var (emulator, sent) = Build();

        Feed(emulator, Enq);

        Assert.Empty(sent);
    }

    [Fact]
    public void TheReplyIsAWellFormedTektronixReport()
    {
        // The first five bytes are exactly what a real 4014 sends, because an ND is 4014
        // compatible. A host that only knows Tektronix must be able to read them.
        var (emulator, sent) = Build();

        Feed(emulator, Esc, Enq);

        var reply = sent[0];
        Assert.Equal(0x68, reply[0]);                       // status: bit 7 clear, bit 6 set
        Assert.True(TektronixGinEncoder.TryDecode(reply, out int x, out int y));
        Assert.InRange(x, 0, 1023);
        Assert.InRange(y, 0, 779);
    }

    [Fact]
    public void TheLastTwoBytesAreSevenBitModelBytes()
    {
        // The receiving end ANDs each with 0x7F, so anything with the top bit set is lost in
        // transit. What we send has to survive that.
        var (emulator, sent) = Build();

        Feed(emulator, Esc, Enq);

        var reply = sent[0];
        Assert.Equal(0, reply[5] & 0x80);
        Assert.Equal(0, reply[6] & 0x80);
    }

    [Fact]
    public void TheReplyCarriesWhereTheCrosshairIs()
    {
        // The identification reply doubles as a position report, so it has to say where the
        // crosshair actually is rather than a fixed corner.
        var (emulator, sent) = Build();
        emulator.GraphicsModule!.MoveCrosshair(512, 389);

        Feed(emulator, Esc, Enq);

        Assert.True(TektronixGinEncoder.TryDecode(sent[0], out int x, out int y));
        Assert.Equal(512, x);
        Assert.Equal(390, y);                               // 779 - 389: Y runs upward for the host
    }

    [Fact]
    public void TheWholeDetectionBurstProducesExactlyOneReply()
    {
        // The real sequence a host sends. Seven bytes come back, once - the ND commands ahead of
        // ENQ must not each answer as well, or the host would read a reply it could not parse.
        var (emulator, sent) = Build();

        Feed(emulator, "\u001b\"13;10l");
        Feed(emulator, "\u001b\"5d");
        Feed(emulator, Esc, Enq);

        Assert.Single(sent);
        Assert.Equal(7, sent[0].Length);
    }

    [Fact]
    public void TheDetectionBurstDrawsNothingOnScreen()
    {
        // Before the ground mode existed, ESC "5d printed a 'd' and ESC "13;10l printed "3;10l".
        // A host probing for graphics would have littered the screen with fragments.
        var (emulator, _) = Build();

        Feed(emulator, "\u001b\"13;10l");
        Feed(emulator, "\u001b\"5d");

        var buffer = emulator.GetBuffer();
        for (int col = 0; col < buffer.Width; col++)
        {
            Assert.True(buffer.GetCell(0, col).IsEmpty,
                $"column {col} should be empty; a detection probe must leave no marks");
        }
    }

    [Fact]
    public void AskingTwiceAnswersTwice()
    {
        // A host may probe more than once - on reconnect, or after a reset. Arming GIN internally
        // to build the report must not leave the terminal unable to answer again.
        var (emulator, sent) = Build();

        Feed(emulator, Esc, Enq);
        Feed(emulator, Esc, Enq);

        Assert.Equal(2, sent.Count);
        Assert.Equal(7, sent[0].Length);
        Assert.Equal(7, sent[1].Length);
    }
}
