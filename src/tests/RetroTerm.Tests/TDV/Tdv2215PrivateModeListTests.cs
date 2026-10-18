using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// A TDV2215 mode sequence carrying more than one mode number.
/// </summary>
/// <remarks>
/// <para><b>The fault this file exists for</b></para>
/// <c>TDVEmulatorBase.HandleModeList</c> was written on 28 August 2026 after a real capture from
/// D100 showed PED&#39;s exit sending <c>CSI 66;62;80 l</c> and only the first number being acted
/// on. Sweeping for the shape - read <c>parameters[0]</c>, act, then claim the whole sequence -
/// found a second site, the TDV2215&#39;s own private-mode handler.
///
/// <para><b>Rewritten 11 September 2026, because it was testing invented modes</b></para>
/// It used to drive <c>CSI ? 1;2 h</c> and call the two numbers "extended mode" and "transparent
/// mode". Neither is true: 1 is DECCKM, 2 is among the numbers the ND-1200 mode table says the
/// terminal ignores, and the 2215&#39;s real extended control is ANSI mode 66. The handler that
/// claimed them is gone, so the list behaviour is now tested with numbers that mean something.
///
/// <para><b>Why a host would send a list at all</b></para>
/// Because it is shorter, and because every terminal implementing SM and RM accepts it. A real
/// TDV2200 termcap&#39;s initialisation string is <c>ESC [ 62;36;66 l</c> - three numbers in one
/// sequence - so the form is not hypothetical on this family.
/// </remarks>
public class Tdv2215PrivateModeListTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Builds a TDV2215 and feeds it a sequence.
    /// </summary>
    /// <param name="sequence">
    /// The bytes to feed, without the leading escape.
    /// </param>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TDV2215Emulator Feed(string sequence)
    {
        var emulator = new TDV2215Emulator(80, 25);
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + sequence));
        return emulator;
    }

    /// <summary>
    /// Every DEC private number in a list is acted on, not just the first.
    /// </summary>
    /// <remarks>
    /// 1 is DECCKM and 8 is DECARM, auto repeat. Both are in the ND-1200 section 5.64 table of
    /// DEC-compatible modes, so both are sequences a real host would send to this terminal.
    /// </remarks>
    [Fact]
    public void EveryPrivateNumberInATwoModeSetIsActedOn()
    {
        var emulator = Feed("[?1;8l");

        var modes = emulator.GetActiveModes();
        Assert.False(modes.HasFlag(TerminalModes.ApplicationCursorKeys));
        Assert.True(modes.HasFlag(TerminalModes.AutoRepeatDisabled),
            "DECARM was second in the list and was dropped");
    }

    /// <summary>
    /// And the set direction, back again.
    /// </summary>
    [Fact]
    public void EveryPrivateNumberInATwoModeResetIsActedOn()
    {
        var emulator = Feed("[?1;8l");
        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.AutoRepeatDisabled));

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "[?1;8h"));

        var modes = emulator.GetActiveModes();
        Assert.True(modes.HasFlag(TerminalModes.ApplicationCursorKeys),
            "DECCKM, first in the list, was not set");
        Assert.False(modes.HasFlag(TerminalModes.AutoRepeatDisabled),
            "DECARM, second in the list, was not set");
    }

    /// <summary>
    /// The 2115 switch reaches its handler from the END of an ANSI list.
    /// </summary>
    /// <remarks>
    /// This is the real capture&#39;s own shape: <c>CSI 62;36;66 l</c> from a TDV2200 termcap, with
    /// the number that matters last. Mode 66 is EC, and its RESET is the 2115 side - TDV 2215
    /// section 3.1, ND-1200 section 8.1.
    /// </remarks>
    [Fact]
    public void TheTwoOneOneFiveSwitchIsReachedFromTheEndOfAList()
    {
        var emulator = Feed("[62;36;66l");

        Assert.True(emulator.Is2115CompatibilityMode,
            "mode 66 was last in the list and was dropped");
        Assert.False(emulator.IsExtendedMode,
            "extended operation is 'not 2115-compatible', so it must be off here");
    }

    /// <summary>
    /// One mode per sequence, the overwhelmingly common form, is unchanged.
    /// </summary>
    [Fact]
    public void ASingleModeStillWorksExactlyAsBefore()
    {
        var emulator = Feed("[?1h");

        Assert.True(emulator.GetActiveModes().HasFlag(TerminalModes.ApplicationCursorKeys));
    }
}
