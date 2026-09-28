using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Profiles;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Phase 3 part 2: one way for a terminal to answer its host.
///
/// There used to be two. The generic one (TerminalEmulatorBase.DataToSend) carried bytes; the TDV
/// one (TDVEmulatorBase.OnResponseReady) carried a string that TerminalSession encoded as UTF-8.
/// That was wrong twice over:
///
///  - a reply is a BYTE string, and UTF-8 turns any byte above 0x7F into two bytes on the wire.
///    Latent rather than live — no current TDV reply contains such a byte — but nothing prevented
///    one, and the failure would have appeared as a host mysteriously rejecting a valid answer;
///  - two channels meant two near-identical send paths in the session to keep in step, and a
///    caller watching the wrong one saw SILENCE rather than an error. That is not hypothetical: it
///    happened while writing TerminalProfileTests, where subscribing to DataToSend on a TDV
///    emulator produced an empty collection that read like a broken emulator.
///
/// TDV replies now go out through DataToSend like everything else. OnResponseReady still fires,
/// as an observation point.
/// </summary>
public class TdvReplyChannelUnificationTests
{
    /// <summary>
    /// Lets a test push an arbitrary reply through the TDV reply path.
    /// </summary>
    private sealed class ReplyProbeEmulator : TDVEmulatorBase
    {
        public ReplyProbeEmulator() : base(20, 5) { }

        public override TerminalProfile Profile => TerminalProfile.ForTdv("PROBE", "\x1b[?1;2c");

        public void Reply(string response) => SendResponse(response);
    }

    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    // ─────────────────────────────────────────────────────────────
    // One channel
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ATdvReplyArrivesOnTheByteChannel()
    {
        var emulator = new TDV2215Emulator(80, 24);
        var sent = new List<byte[]>();
        emulator.DataToSend += bytes => sent.Add(bytes);

        Feed(emulator, "\x1b[c");

        Assert.Single(sent);
        Assert.Equal("\x1b[?1;2c", Encoding.ASCII.GetString(sent[0]));
    }

    [Fact]
    public void TheObservationEventStillFires()
    {
        // Tests, the capability checker and the protocol tooling all watch this; unifying the
        // send path must not take the notification away.
        var emulator = new TDV2215Emulator(80, 24);
        var observed = new List<string>();
        emulator.OnResponseReady += r => observed.Add(r);

        Feed(emulator, "\x1b[c");

        Assert.Single(observed);
        Assert.Equal("\x1b[?1;2c", observed[0]);
    }

    [Fact]
    public void AReplyGoesOutExactlyOnce_NotOncePerChannel()
    {
        // The obvious way to get this wrong is to keep both paths connected to the host, which
        // sends every answer twice and confuses any host that counts replies.
        var emulator = new TDV2215Emulator(80, 24);
        int byteSends = 0;
        emulator.DataToSend += _ => byteSends++;

        Feed(emulator, "\x1b[c");

        Assert.Equal(1, byteSends);
    }

    [Fact]
    public void TheTwoChannelsAgreeOnTheContent()
    {
        var emulator = new TDV2215Emulator(80, 24);
        string? observed = null;
        byte[]? sent = null;
        emulator.OnResponseReady += r => observed = r;
        emulator.DataToSend += b => sent = b;

        Feed(emulator, "\x1b[>c");

        Assert.NotNull(observed);
        Assert.NotNull(sent);
        Assert.Equal(observed!.Length, sent!.Length);
        for (int i = 0; i < sent.Length; i++)
        {
            Assert.Equal((byte)observed[i], sent[i]);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The encoding bug that is now impossible
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AReplyByteAboveSevenBitsSurvivesAsOneByte()
    {
        // Under the old UTF-8 path 0x9C (8-bit ST) went out as 0xC2 0x9C — two bytes where the
        // protocol expects one. This is the test that would have caught it.
        var emulator = new ReplyProbeEmulator();
        byte[]? sent = null;
        emulator.DataToSend += b => sent = b;

        var reply = new string(new[] { (char)0x1B, '[', '0', 'n', (char)0x9C });
        emulator.Reply(reply);

        Assert.NotNull(sent);
        Assert.Equal(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'n', 0x9C }, sent);
    }

    [Fact]
    public void EveryHighByteRoundTripsUnchanged()
    {
        // Sweep the whole upper half rather than trusting one example.
        for (int value = 0x80; value <= 0xFF; value++)
        {
            var emulator = new ReplyProbeEmulator();
            byte[]? sent = null;
            emulator.DataToSend += b => sent = b;

            emulator.Reply(((char)value).ToString());

            Assert.NotNull(sent);
            Assert.Single(sent!);
            Assert.Equal((byte)value, sent![0]);
        }
    }

    [Fact]
    public void AnEmptyReplySendsNothing()
    {
        var emulator = new ReplyProbeEmulator();
        int sends = 0;
        emulator.DataToSend += _ => sends++;

        emulator.Reply("");

        Assert.Equal(0, sends);
    }

    // ─────────────────────────────────────────────────────────────
    // Wiring stays idempotent
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RepeatedWiringDoesNotDuplicateTheSendPath()
    {
        // WireTDVQueryResponse now delegates to WireEmulatorResponse, and both are called from the
        // constructor and again after connecting. If either stopped removing before adding, every
        // reply would go out two or three times.
        var emulator = new TDV2215Emulator(80, 24);
        using var session = new TerminalSession(emulator);

        session.WireTDVQueryResponse();
        session.WireEmulatorResponse();
        session.WireTDVQueryResponse();

        int sends = 0;
        emulator.DataToSend += _ => sends++;

        Feed(emulator, "\x1b[c");

        Assert.Equal(1, sends);
    }
}
