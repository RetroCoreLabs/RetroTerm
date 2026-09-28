using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Profiles;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 3 part 1: the terminal profile (problem B.3), plus DECRQM.
///
/// TerminalEmulatorBase secretly WAS the VT100: the Device Attributes reply was a VT100 reply
/// hardcoded in the base, VT100Emulator was twenty lines that overrode ToString, GetTerminalType
/// returned the placeholder "Terminal" for anything that did not override it, and the factory
/// answered a request for a VT220 with a VT100. With ten to fifteen terminals planned, what
/// distinguishes one from another needs somewhere to live that is not a subclass override.
///
/// A profile is data, so a terminal that differs only in identity needs no class at all — which is
/// what Ecma48Emulator demonstrates and what the "ANSI" factory entry uses.
/// </summary>
public class TerminalProfileTests
{
    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    private static List<string> CaptureReplies(TerminalEmulatorBase emulator)
    {
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        return replies;
    }

    // ─────────────────────────────────────────────────────────────
    // Identity comes from the profile
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryEmulatorReportsItsOwnNameInsteadOfThePlaceholder()
    {
        Assert.Equal("VT100", new VT100Emulator(20, 5).GetTerminalType());
        Assert.Equal("ANSI", new Ecma48Emulator(TerminalProfile.Ansi, 20, 5).GetTerminalType());
    }

    [Fact]
    public void PrimaryDeviceAttributesComeFromTheProfile()
    {
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[c");

        Assert.Single(replies);
        Assert.Equal("\x1b[?1;2c", replies[0]);
        Assert.Equal(replies[0], Encoding.ASCII.GetString(emulator.Profile.PrimaryDeviceAttributes));
    }

    [Fact]
    public void SecondaryDeviceAttributesComeFromTheProfile()
    {
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[>c");

        Assert.Single(replies);
        Assert.Equal(replies[0], Encoding.ASCII.GetString(emulator.Profile.SecondaryDeviceAttributes));
    }

    [Fact]
    public void ADeviceAttributesRequestWithAnotherParameterIsIgnored()
    {
        // DEC: a DA request carries no parameter or an explicit 0. Anything else is not a DA
        // request and answering it would put unexpected bytes on the host's input.
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[5c");

        Assert.Empty(replies);
    }

    [Fact]
    public void ChangingProfileChangesWhatTheTerminalAnswers_WithNoNewClass()
    {
        // The whole claim behind the profile object, stated as a test.
        var custom = new TerminalProfile(
            "MADEUP",
            Encoding.ASCII.GetBytes("\x1b[?99;1c"),
            Encoding.ASCII.GetBytes("\x1b[>99;1;0c"),
            TerminalFeatures.AnsiColour,
            new[] { 7 });

        var emulator = new Ecma48Emulator(custom, 20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[c");

        Assert.Equal("MADEUP", emulator.GetTerminalType());
        Assert.Equal("\x1b[?99;1c", replies[0]);
    }

    [Fact]
    public void TheFactoryBuildsAnAnsiTerminalWithTheAnsiProfile()
    {
        var emulator = EmulatorFactory.CreateEmulator("ANSI", 20, 5);

        Assert.Same(TerminalProfile.Ansi, emulator.Profile);
        Assert.Equal("ANSI", emulator.GetTerminalType());
    }

    [Fact]
    public void AskingForAVt220NowGetsOne()
    {
        // This test used to assert the opposite, and said in its own comment that it was the one
        // to fail and update when VT220 was really built. That is what happened: selective erase
        // and DECCOLM landed, so the identity became one this terminal can back up.
        var emulator = EmulatorFactory.CreateEmulator("VT220", 20, 5);

        Assert.Same(TerminalProfile.VT220, emulator.Profile);
        Assert.Equal("VT220", emulator.GetTerminalType());
    }

    [Fact]
    public void TheVt220ClaimsFourExtensionsAndNoMore()
    {
        // A primary DA answer is a family number followed by the extensions the terminal HAS, so
        // it is not a choice between claiming everything and claiming nothing. 62 is the VT200
        // family, 1 is 132 columns, 6 is selective erase, 7 is a downloadable character set and 8
        // is user-defined keys - all four really implemented. What is absent matters as much: no 2
        // for a printer port and no 9 for national replacement character sets, because neither
        // exists here.
        var emulator = EmulatorFactory.CreateEmulator("VT220", 20, 5);
        var replies = new System.Text.StringBuilder();
        emulator.DataToSend += bytes => replies.Append(System.Text.Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1b[c"));

        Assert.Equal("\x1b[?62;1;6;7;8c", replies.ToString());
    }

    [Fact]
    public void AndTheExtensionsItClaimsActuallyWork()
    {
        // The check that keeps the DA string honest. If either of these stops working, the reply
        // above becomes a lie and this is what says so.
        var emulator = EmulatorFactory.CreateEmulator("VT220", 80, 24);

        // 1: 132 columns.
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1b[?3h"));
        Assert.Equal(132, emulator.Width);

        // 6: selective erase spares text marked protected by DECSCA.
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1b[?3l"));
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1b[1\"qKEEP\x1b[0\"qGONE"));
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1b[H\x1b[?2J"));

        emulator.GetBuffer().TryGetCell(0, 0, out var kept);
        emulator.GetBuffer().TryGetCell(0, 4, out var erased);
        Assert.Equal('K', (char)kept.Codepoint);
        Assert.Equal(0u, erased.Codepoint);

        // 7: a downloaded character set holds the shape the host drew, and cells printed from it
        // are marked as coming from it.
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes(
            "\x1bP1;1;1;8;0;0;12;0{ @~\x1b\\"));
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1b[H\x1b( @ "));
        Assert.Equal(1, emulator.SoftFont.Count);
        emulator.GetBuffer().TryGetCell(0, 0, out var downloaded);
        Assert.Equal(TerminalEmulatorBase.SoftFontCharacterSet, downloaded.CharacterSet);

        // 8: a key loaded with DECUDK gives back what the host loaded. F6 is DEC key 17 and
        // Windows virtual key 117; "6c73" is "ls".
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1bP1;1|17/6c73\x1b\\"));
        Assert.True(emulator.TryGetUserDefinedKey(117, out var f6));
        Assert.Equal("ls", System.Text.Encoding.ASCII.GetString(f6));
    }

    [Fact]
    public void TheKeyNumbersHaveTheGapsDecLeftInThem()
    {
        // The DEC numbers are not consecutive: F10 is 21 and F11 is 23, F14 is 26 and F15 is 28.
        // Filling the gaps in would put a host's definition on the wrong key.
        var emulator = EmulatorFactory.CreateEmulator("VT220", 20, 5);

        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes(
            "\x1bP1;1|21/74656e;23/656c6576656e\x1b\\"));

        Assert.True(emulator.TryGetUserDefinedKey(121, out var f10));   // VK_F10
        Assert.True(emulator.TryGetUserDefinedKey(122, out var f11));   // VK_F11
        Assert.Equal("ten", System.Text.Encoding.ASCII.GetString(f10));
        Assert.Equal("eleven", System.Text.Encoding.ASCII.GetString(f11));
    }

    [Fact]
    public void TheFirstFiveFunctionKeysCannotBeDefined()
    {
        // On a real VT220 F1 to F5 are Hold, Print, Set-Up, Data/Talk and Break - local functions
        // the terminal itself acts on, never sent to the host and never definable.
        var emulator = EmulatorFactory.CreateEmulator("VT220", 20, 5);

        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("\x1bP1;1|11/6c73\x1b\\"));

        Assert.False(emulator.TryGetUserDefinedKey(112, out _));   // VK_F1
        Assert.False(emulator.TryGetUserDefinedKey(116, out _));   // VK_F5
    }

    // ─────────────────────────────────────────────────────────────
    // The TDV profiles must match what the TDV emulators actually reply
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TDV1200")]
    [InlineData("TDV2215")]
    [InlineData("TDV2200")]
    public void TdvProfileMatchesEmulatorReplies(string type)
    {
        // The TDV family answers DA through its own query/response path and never reaches the
        // base, so the profile's DA bytes are identification rather than the reply. That makes it
        // easy for the two to drift apart silently — this test is what stops that.
        //
        // NOTE the reply arrives on TDVEmulatorBase.OnResponseReady, not on DataToSend. The two
        // host-reply channels are a real and pre-existing split (review section A.7): TDV has a
        // string channel of its own above the generic byte one. Subscribing to the wrong one here
        // silently observed nothing at all, which is exactly the trap the split sets.
        var emulator = (TDVEmulatorBase)EmulatorFactory.CreateEmulator(type, 80, 24);
        var replies = new List<string>();
        emulator.OnResponseReady += r => replies.Add(r);

        Feed(emulator, "\x1b[c");

        // TDV2200 appends its active options to the type string ("TDV2200+ISO646_International"),
        // so the model name is a prefix rather than the whole thing.
        Assert.StartsWith(type, emulator.GetTerminalType());
        Assert.Single(replies);
        Assert.Equal(Encoding.ASCII.GetString(emulator.Profile.PrimaryDeviceAttributes), replies[0]);
    }

    [Fact]
    public void TdvProfilesDeclareProtectedFields()
    {
        var emulator = EmulatorFactory.CreateEmulator("TDV2200", 80, 24);

        Assert.True(emulator.Profile.Supports(TerminalFeatures.ProtectedFields));
        Assert.True(emulator.Profile.Supports(TerminalFeatures.NationalCharacterSets));
    }

    // ─────────────────────────────────────────────────────────────
    // Capability flags
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SupportsRequiresEveryFlagInTheSet_NotJustOne()
    {
        var profile = new TerminalProfile("X", new byte[] { 0 }, new byte[] { 0 },
            TerminalFeatures.AnsiColour, Array.Empty<int>());

        Assert.True(profile.Supports(TerminalFeatures.AnsiColour));
        Assert.False(profile.Supports(TerminalFeatures.Sixel));
        // Asking about a combination means "both", which is the useful reading for a caller
        // deciding whether it may emit a sequence needing both.
        Assert.False(profile.Supports(TerminalFeatures.AnsiColour | TerminalFeatures.Sixel));
    }

    [Fact]
    public void AProfileRejectsMissingParts()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TerminalProfile(null!, new byte[] { 0 }, new byte[] { 0 }, TerminalFeatures.None, Array.Empty<int>()));
        Assert.Throws<ArgumentNullException>(() =>
            new TerminalProfile("X", null!, new byte[] { 0 }, TerminalFeatures.None, Array.Empty<int>()));
    }

    // ─────────────────────────────────────────────────────────────
    // DECRQM — how a host asks what a mode is doing
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RequestingASetModeReportsItSet()
    {
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?7h");      // DECAWM on
        Feed(emulator, "\x1b[?7$p");     // DECRQM

        Assert.Single(replies);
        Assert.Equal("\x1b[?7;1$y", replies[0]);
    }

    [Fact]
    public void RequestingAResetModeReportsItReset()
    {
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?7l");
        Feed(emulator, "\x1b[?7$p");

        Assert.Equal("\x1b[?7;2$y", replies[0]);
    }

    [Fact]
    public void AnUnknownModeIsReportedAsNotRecognised_NotAsReset()
    {
        // The distinction that makes DECRQM worth implementing: "I know that mode and it is off"
        // and "I have never heard of it" are different answers. Collapsing them into "reset"
        // tells a host it may safely use a mode this terminal will silently ignore.
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?12345$p");

        Assert.Equal("\x1b[?12345;0$y", replies[0]);
    }

    [Fact]
    public void CursorVisibilityIsReportable()
    {
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?25l");     // hide
        Feed(emulator, "\x1b[?25$p");

        Assert.Equal("\x1b[?25;2$y", replies[0]);
    }

    [Fact]
    public void AlternateScreenIsReportable()
    {
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?1049h");
        Feed(emulator, "\x1b[?1049$p");

        Assert.Equal("\x1b[?1049;1$y", replies[0]);
    }

    [Fact]
    public void DecrqmDoesNotChangeTheModeItAsksAbout()
    {
        // It shares the '?' marker with DECSET/DECRST and differs only by the '$' intermediate,
        // so a dispatch that ignored the intermediate would SET the mode instead of reporting it.
        var emulator = new VT100Emulator(20, 5);
        var replies = CaptureReplies(emulator);

        Feed(emulator, "\x1b[?25l");     // hide the cursor
        Feed(emulator, "\x1b[?25$p");    // ask about it

        Assert.False(emulator.Cursor.Visible);
        Assert.Equal("\x1b[?25;2$y", replies[0]);
    }

    [Fact]
    public void OrdinaryDecsetStillWorksAlongsideDecrqm()
    {
        // Guard: adding the intermediate check must not break the mode sequences themselves.
        var emulator = new VT100Emulator(20, 5);

        Feed(emulator, "\x1b[?25l");
        Assert.False(emulator.Cursor.Visible);

        Feed(emulator, "\x1b[?25h");
        Assert.True(emulator.Cursor.Visible);
    }
}
