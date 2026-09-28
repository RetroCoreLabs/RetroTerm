using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Every key a PC keyboard can actually reach sends what the registry says it should.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b></para>
/// M4.2c checked ONE key and was named for a claim it never tested; the F1 key turned out to send
/// something different from what three documents said, and only a live measurement on 2 September
/// 2026 found it. A per-key test is a poor guard against that class of thing - the next wrong key
/// is by definition the one nobody wrote a test for. This walks the whole registry instead.
///
/// <para><b>What it does NOT claim</b></para>
/// It compares the mapper against the registry, so it proves they AGREE - not that either matches
/// real hardware. The registry is the source of truth by decision, and the only things measured on
/// a live machine so far are the four arrows, HOME and F1. This guard's job is to stop the mapper
/// and the registry drifting apart silently; the manual pass is still what checks them against a
/// real host.
/// </remarks>
public class EveryReachableKeyAgreesWithTheRegistryTests
{
    /// <summary>
    /// Every registry entry that a PC keyboard can send, with the sequence the registry gives it.
    /// </summary>
    /// <param name="extendedMode">
    /// True for the default extended mode, false for 2115 compatibility.
    /// </param>
    /// <returns>
    /// One case per reachable key.
    /// </returns>
    private static List<(string Grid, string Name, int Vk, string Expected)> ReachableKeys(bool extendedMode)
    {
        var result = new List<(string, string, int, string)>();

        foreach (var pair in TDV2200KeyRegistry.AllKeys)
        {
            var def = pair.Value;

            // A virtual key code of 0 means no PC key reaches this position - HJELP is the one
            // that cost a day. Those are reachable only through a binding, so the mapper is not
            // expected to produce them from a bare keypress.
            if (def.VirtualKeyCode == 0) continue;

            string? expected = TDV2200KeyRegistry.GetSequence(pair.Key, extendedMode, numPadFuncMode: false);
            if (string.IsNullOrEmpty(expected)) continue;

            result.Add((pair.Key, def.Name, def.VirtualKeyCode, expected!));
        }

        return result;
    }

    /// <remarks>
    /// <para><b>Both modes, and the 2115 half found a real bug</b></para>
    /// This walk originally asserted extended mode only, because in 2115 compatibility mode twelve
    /// keys disagreed and it was not clear which side was wrong - four of the registry values look
    /// like keypad digits rather than control codes.
    /// <para>
    /// <c>spec\Keyboards\keyboard-spec.md</c> §6.8.3 settled it, citing the TDV-2200/9 User's Guide
    /// section 7.2: the REGISTRY was right, mapper wrong, and the odd-looking four are documented
    /// explicitly as multi-byte and not C0 - F5 sends "000", F6 "00", F7 "0", F8 "+". The mapper
    /// had a hardcoded list of five keys where §6.8.3 has a table of thirty, so everything else
    /// fell through and sent its extended sequence while the terminal was in 2115 mode.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheMapperSendsWhatTheRegistrySaysForEveryReachableKey(bool extendedMode)
    {
        var mapper = new TDV2200KeyboardMapper();
        var modes = extendedMode ? TerminalModes.None : TerminalModes.TDV2115Mode;

        var keys = ReachableKeys(extendedMode);
        Assert.True(keys.Count > 20,
            "only " + keys.Count + " reachable keys were found - the registry or its accessors "
            + "must have changed shape, and this guard would be checking almost nothing");

        var mismatches = new StringBuilder();
        int checkedCount = 0;

        for (int i = 0; i < keys.Count; i++)
        {
            var (grid, name, vk, expected) = keys[i];
            string? actual = mapper.MapKey(vk, KeyModifiers.None, modes);
            checkedCount++;

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                mismatches.Append("  ").Append(grid).Append(' ').Append(name)
                          .Append(" (VK ").Append(vk).Append("): registry says ")
                          .Append(Readable(expected)).Append(", mapper sends ")
                          .Append(actual == null ? "nothing" : Readable(actual))
                          .Append('\n');
            }
        }

        Assert.True(mismatches.Length == 0,
            "in " + (extendedMode ? "extended" : "2115 compatibility") + " mode, " + checkedCount
            + " reachable keys were checked and these disagree with the registry:\n" + mismatches);
    }

    /// <summary>
    /// Turns a sequence into something readable in a failure message.
    /// </summary>
    /// <param name="sequence">
    /// The bytes the key sends.
    /// </param>
    /// <returns>
    /// The same text with control characters named.
    /// </returns>
    private static string Readable(string sequence)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < sequence.Length; i++)
        {
            char ch = sequence[i];
            if (ch == (char)0x1B) sb.Append("ESC");
            else if (ch < 0x20) sb.Append("0x").Append(((int)ch).ToString("X2"));
            else sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The virtual key codes that legitimately sit on two grid positions.
    /// </summary>
    /// <remarks>
    /// Windows reports the main and keypad versions of these with the same code, so the mapper
    /// cannot tell them apart and both grid positions genuinely answer to one key. Listed rather
    /// than derived so that a NEW clash still fails.
    /// </remarks>
    private static readonly int[] KnownSharedVirtualKeys =
    {
        13,  // RETURN and keypad ENTER - Windows reports both as VK_RETURN
        32,  // SPACE and keypad SPACE
        20,  // CAPS and LOCK

        // KNOWN WRONG, and recorded in docs\PLAN.md rather than silently accepted. 189 is
        // VK_OEM_MINUS and sits on both E11 PLUS and B10 MINUS, so one of the two cannot be
        // reached. E11 should be VK_OEM_PLUS (187) on the evidence - Microsoft define that as the
        // "+" key for any region, the spec photographs E11 as ? over +, and the registry's own
        // labels agree - but making that change only MOVES the clash, because E12 already holds
        // 187 and is the @ / backslash key. What E12 should be is not settled by anything in
        // spec\, so the pair is left alone and listed here to keep this guard useful for NEW
        // clashes.
        189,

    };

    [Fact]
    public void NoUnexpectedPairOfKeysClaimsTheSameVirtualKeyCode()
    {
        // Two grid positions on the same PC key means one of them is unreachable and nothing says
        // which. That is the shape of the F1/HJELP confusion, where the document put VK 112 on the
        // key the registry gives VK 0.
        var seen = new Dictionary<int, string>();
        var clashes = new StringBuilder();

        foreach (var pair in TDV2200KeyRegistry.AllKeys)
        {
            var def = pair.Value;
            if (def.VirtualKeyCode == 0) continue;

            bool known = false;
            for (int k = 0; k < KnownSharedVirtualKeys.Length; k++)
            {
                if (KnownSharedVirtualKeys[k] == def.VirtualKeyCode) { known = true; break; }
            }

            if (known) continue;

            if (seen.TryGetValue(def.VirtualKeyCode, out var already))
            {
                clashes.Append("  VK ").Append(def.VirtualKeyCode).Append(" is on both ")
                       .Append(already).Append(" and ").Append(pair.Key)
                       .Append(' ').Append(def.Name).Append('\n');
            }
            else
            {
                seen[def.VirtualKeyCode] = pair.Key + " " + def.Name;
            }
        }

        Assert.True(clashes.Length == 0,
            "two grid positions cannot share one PC key - one of them would be unreachable:\n"
            + clashes);
    }
}
