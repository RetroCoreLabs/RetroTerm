using System;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for the TDV indicator lamps that the virtual keyboard mirrors:
/// the three keyboard lamps driven by ENQ/ACK/NAK/SYN, and the message LEDs
/// driven by NDCLED/NDSLED/NDBLED.
/// </summary>
public class TDVIndicatorLampTests
{
    private static TDV2200Emulator CreateEmulator() => new TDV2200Emulator(80, 24);

    /// <summary>
    /// Feeds a sequence, supplying the escape so no raw control byte lands in this file.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to feed.
    /// </param>
    /// <param name="sequence">
    /// The sequence after the escape, for example "[?4B".
    /// </param>
    private static void Feed(TDV2200Emulator emulator, string sequence)
    {
        var text = (char)0x1B + sequence;
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes(text));
    }

    private static void Send(TDV2200Emulator emulator, params int[] bytes)
    {
        var data = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            data[i] = (byte)bytes[i];

        emulator.ProcessData(new ReadOnlySpan<byte>(data));
    }

    // ---- Keyboard lamps in TDV-NATIVE mode (2115 compatibility OFF) ----
    // Documented in docs/TDV-COMPREHENSIVE-REFERENCE.md under "All Models".

    [Fact]
    public void Lamps_StartAllOff()
    {
        var emulator = CreateEmulator();

        Assert.Equal((false, false, false), emulator.KeyboardLights);
    }

    [Theory]
    [InlineData(0x05, true, false, false)]  // ENQ - Light 1
    [InlineData(0x06, false, true, false)]  // ACK - Light 2
    [InlineData(0x15, false, false, true)]  // NAK - Light 3
    public void Lamp_LitByItsC0Code_InNativeMode(int code, bool l1, bool l2, bool l3)
    {
        var emulator = CreateEmulator();
        Assert.False(emulator.Is2115CompatibilityMode);

        Send(emulator, code);

        Assert.Equal((l1, l2, l3), emulator.KeyboardLights);
    }

    [Fact]
    public void Syn_ClearsAllLamps_InNativeMode()
    {
        var emulator = CreateEmulator();

        Send(emulator, 0x05, 0x06, 0x15);
        Assert.Equal((true, true, true), emulator.KeyboardLights);

        Send(emulator, 0x16); // SYN - clear lamps

        Assert.Equal((false, false, false), emulator.KeyboardLights);
    }

    [Fact]
    public void LampCodes_AreConsumed_AndDoNotReachTheScreen()
    {
        var emulator = CreateEmulator();

        Send(emulator, 0x05, 0x06, 0x15, 0x16);

        Assert.Equal(0, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    // ---- Change notification ----

    [Fact]
    public void LedStateChanged_RaisedWhenALampLights()
    {
        var emulator = CreateEmulator();
        int raised = 0;
        emulator.LedStateChanged += () => raised++;

        Send(emulator, 0x05);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void LedStateChanged_NotRaisedWhenLampStateIsUnchanged()
    {
        // A host that re-asserts its lamps every refresh must not flood the UI thread.
        var emulator = CreateEmulator();
        Send(emulator, 0x05);

        int raised = 0;
        emulator.LedStateChanged += () => raised++;

        Send(emulator, 0x05, 0x05, 0x05);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void LedStateChanged_NotRaisedWhenSynClearsAlreadyDarkLamps()
    {
        var emulator = CreateEmulator();

        int raised = 0;
        emulator.LedStateChanged += () => raised++;

        Send(emulator, 0x16); // SYN with everything already off

        Assert.Equal(0, raised);
    }

    // ---- Message lamps: NDSLED, NDBLED, NDCLED ----
    //
    // Four lamps, three operations - ND Display Terminal 1200 sections 5.52, 5.37 and 5.38:
    //     CSI ? Ps B   light    CSI ? Ps C   blink    CSI ? Ps A   clear
    // with 0 all, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE.
    //
    // These tests used to drive SetMessageLED(TDVMessageLEDType.Set, true) and read back a
    // MessageLEDState.Set flag - the three operations modelled as three lamps, with no way to say
    // WHICH lamp a host meant and no sequence decoding them at all.

    /// <summary>
    /// NDSLED lights the lamp its parameter names, and NDCLED clears it.
    /// </summary>
    [Fact]
    public void Ndsled_LightsTheNamedLamp_AndNdcledClearsIt()
    {
        var emulator = CreateEmulator();

        Feed(emulator, "[?4B");    // light lamp 4, MESSAGE
        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Message);
        Assert.Equal(TDVMessageLampState.Off, emulator.MessageLEDState.Busy);

        Feed(emulator, "[?4A");    // clear lamp 4
        Assert.Equal(TDVMessageLampState.Off, emulator.MessageLEDState.Message);
    }

    /// <summary>
    /// NDBLED makes a lamp blink, and blinking replaces lit rather than joining it.
    /// </summary>
    [Fact]
    public void Ndbled_MakesTheLampBlink_NotBothAtOnce()
    {
        var emulator = CreateEmulator();

        Feed(emulator, "[?2B");    // light lamp 2, APPEND
        Feed(emulator, "[?2C");    // now blink it

        Assert.Equal(TDVMessageLampState.Blinking, emulator.MessageLEDState.Append);
    }

    /// <summary>
    /// Parameter 0 means every lamp.
    /// </summary>
    [Fact]
    public void ParameterZeroMeansAllFourLamps()
    {
        var emulator = CreateEmulator();

        Feed(emulator, "[?0B");

        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Expand);
        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Append);
        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Busy);
        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Message);

        Feed(emulator, "[?0A");

        Assert.False(emulator.MessageLEDs.AnyLampOn);
    }

    /// <summary>
    /// No parameter at all also means every lamp - section 5.52 gives the default as 0.
    /// </summary>
    [Fact]
    public void NoParameterMeansAllFourLamps()
    {
        var emulator = CreateEmulator();

        Feed(emulator, "[?B");

        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Expand);
        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Message);
    }

    /// <summary>
    /// EVERY lamp in a list is acted on, not just the first.
    /// </summary>
    /// <remarks>
    /// The manual's shape is <c>CSI ? n1 ; n2 ... B</c>. Acting on the first number and claiming
    /// the sequence is the exact fault found in the mode handlers on 28 August 2026.
    /// </remarks>
    [Fact]
    public void EveryLampInAListIsActedOn()
    {
        var emulator = CreateEmulator();

        Feed(emulator, "[?1;3;4B");

        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Expand);
        Assert.Equal(TDVMessageLampState.Off, emulator.MessageLEDState.Append);
        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Busy);
        Assert.Equal(TDVMessageLampState.Lit, emulator.MessageLEDState.Message);
    }

    /// <summary>
    /// A lamp number the terminal has no lamp for changes nothing.
    /// </summary>
    [Fact]
    public void ALampNumberThatDoesNotExistChangesNothing()
    {
        var emulator = CreateEmulator();

        Feed(emulator, "[?9B");

        Assert.False(emulator.MessageLEDs.AnyLampOn);
    }

    /// <summary>
    /// A lamp change tells the UI, and a redundant one does not.
    /// </summary>
    [Fact]
    public void ALampChangeRaisesLedStateChangedOnceAndOnlyOnAChange()
    {
        var emulator = CreateEmulator();
        int raised = 0;
        emulator.LedStateChanged += () => raised++;

        Feed(emulator, "[?3B");
        Assert.Equal(1, raised);

        Feed(emulator, "[?3B");
        Assert.Equal(1, raised);
    }

    /// <summary>
    /// The API the UI and scripts use reaches the same lamps as the sequences.
    /// </summary>
    [Fact]
    public void SetMessageLampDoesTheSameAsTheSequence()
    {
        var emulator = CreateEmulator();

        emulator.SetMessageLamp((int)TDVMessageLamp.Busy, TDVMessageLampState.Blinking);

        Assert.Equal(TDVMessageLampState.Blinking, emulator.MessageLEDState.Busy);
    }

    // ---- 2115 compatibility mode must keep working ----

    [Fact]
    public void Lamps_StillWorkIn2115CompatibilityMode()
    {
        var emulator = CreateEmulator();
        emulator.ProcessData("\x1b[66l"u8.ToArray());
        Assert.True(emulator.Is2115CompatibilityMode);

        Send(emulator, 0x06);

        Assert.Equal((false, true, false), emulator.KeyboardLights);
    }
}
