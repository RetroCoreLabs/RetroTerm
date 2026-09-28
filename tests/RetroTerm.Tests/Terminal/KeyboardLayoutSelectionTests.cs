using System;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Core.Terminal.Profiles;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 3 part 7: the keyboard is chosen by the profile, not by a class name (problem B.10).
///
/// The factory was keyed on <c>emulator.GetType().Name</c> with a silent VT100 fallback for
/// anything it did not recognise. Two consequences, neither of them anybody's decision:
///
///  - renaming an emulator class silently downgraded that terminal's keyboard to VT100;
///  - a terminal whose class name was not in the list got VT100 keys without anyone choosing so.
///    Ecma48Emulator — added earlier in Phase 3 for the "ANSI" profile — was exactly that case. It
///    worked, by luck, through the fallback.
///
/// A profile now states which keyboard its terminal types on.
/// </summary>
public class KeyboardLayoutSelectionTests
{
    // ─────────────────────────────────────────────────────────────
    // Every terminal the factory can build gets a real keyboard
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VT100", typeof(VT100KeyboardMapper))]
    [InlineData("ANSI", typeof(VT100KeyboardMapper))]
    [InlineData("TDV1200", typeof(TDV1200KeyboardMapper))]
    [InlineData("TDV2215", typeof(TDV2215KeyboardMapper))]
    [InlineData("TDV2200", typeof(TDV2200KeyboardMapper))]
    public void EveryEmulatorTypeResolvesToItsOwnMapper(string emulatorType, Type expectedMapper)
    {
        var emulator = EmulatorFactory.CreateEmulator(emulatorType, 80, 24);

        var mapper = KeyboardMapperFactory.CreateMapper(emulator.Profile);

        Assert.IsType(expectedMapper, mapper);
    }

    [Fact]
    public void TheAnsiTerminalTypesOnAVt100Keyboard_ByDecisionNotByFallback()
    {
        // The distinction this whole change is about. Before, this passed because the factory fell
        // back to VT100 for the unrecognised class name "Ecma48"; now the profile says so.
        Assert.Equal("VT100", TerminalProfile.Ansi.KeyboardLayout);

        var emulator = EmulatorFactory.CreateEmulator("ANSI", 80, 24);
        Assert.IsType<VT100KeyboardMapper>(KeyboardMapperFactory.CreateMapper(emulator.Profile));
    }

    [Fact]
    public void ATerminalWithNoKeyboardOfItsOwnSaysWhichOneItBorrows()
    {
        // A profile that does not set a layout types on a keyboard named after itself, which is
        // right for a terminal with a keyboard of its own.
        Assert.Equal("VT100", TerminalProfile.VT100.KeyboardLayout);
        Assert.Equal("TDV2200", TerminalProfile.ForTdv("TDV2200", "\x1b[?220;0c").KeyboardLayout);
    }

    // ─────────────────────────────────────────────────────────────
    // An unknown layout is refused, not quietly substituted
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AProfileAskingForAKeyboardThatDoesNotExistIsRefused()
    {
        // "This terminal types on a VT100 keyboard" is a decision for a profile to state, not
        // something for a lookup to assume when it recognises nothing. A profile with a typo in
        // its layout name should say so rather than hand out VT100 keys.
        var broken = new TerminalProfile(
            "BROKEN",
            new byte[] { 0x1B },
            new byte[] { 0x1B },
            TerminalFeatures.None,
            Array.Empty<int>(),
            keyboardLayout: "NoSuchKeyboard");

        var error = Assert.Throws<NotSupportedException>(
            () => KeyboardMapperFactory.CreateMapper(broken));

        // The message has to name both ends, or it sends the reader hunting.
        Assert.Contains("BROKEN", error.Message);
        Assert.Contains("NoSuchKeyboard", error.Message);
    }

    [Fact]
    public void TheLayoutLookupReportsAnUnknownLayoutAsNull()
    {
        Assert.Null(KeyboardMapperFactory.CreateMapperForLayout("NoSuchKeyboard"));
        Assert.NotNull(KeyboardMapperFactory.CreateMapperForLayout("VT100"));
    }

    [Fact]
    public void ANullProfileIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => KeyboardMapperFactory.CreateMapper((TerminalProfile)null!));
    }

    // ─────────────────────────────────────────────────────────────
    // The legacy string lookup still works for the callers that use it
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheStringLookupStillResolvesKnownNames()
    {
        Assert.IsType<VT100KeyboardMapper>(KeyboardMapperFactory.CreateMapper("VT100"));
        Assert.IsType<TDV2200KeyboardMapper>(KeyboardMapperFactory.CreateMapper("TDV2200"));
    }

    [Fact]
    public void TheStringLookupStillStripsTheEmulatorSuffix()
    {
        Assert.IsType<TDV2200KeyboardMapper>(KeyboardMapperFactory.CreateMapper("TDV2200Emulator"));
    }

    [Fact]
    public void TheStringLookupKeepsItsVt100Fallback()
    {
        // Documented rather than endorsed: the string form keeps the old fallback so existing
        // callers behave as before. It is no longer how any emulator gets its keyboard.
        Assert.IsType<VT100KeyboardMapper>(KeyboardMapperFactory.CreateMapper("SomethingUnknown"));
    }

    // ─────────────────────────────────────────────────────────────
    // A mapper that production cannot currently reach
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheVt220KeyboardExistsButNoShippedProfileAsksForIt()
    {
        // DOCUMENTS A GAP. VT220KeyboardMapper is implemented and reachable by layout name, but
        // there is no VT220Emulator class and the factory's "VT220" entry builds a VT100Emulator
        // carrying the VT100 profile — so nothing in the running application ever selects it.
        //
        // This is the keyboard half of the same limitation appendix 0g recorded for DA replies. It
        // is not dead code to delete; it is code waiting for the VT220 profile to exist. When that
        // profile lands, its KeyboardLayout should be "VT220" and this test should be updated.
        Assert.NotNull(KeyboardMapperFactory.CreateMapperForLayout("VT220"));

        var vt220 = EmulatorFactory.CreateEmulator("VT220", 80, 24);
        Assert.Equal("VT100", vt220.Profile.KeyboardLayout);
    }
}
