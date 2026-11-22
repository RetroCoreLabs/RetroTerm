//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using RetroTerm.Core.Terminal.Input;
using RetroTerm.Core.Terminal.Input.Keyboards;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270;

/// <summary>
/// The two IBM on-screen keyboards, checked against docs\spec\IBM3270\KEYBOARD.md.
/// </summary>
public class Ibm3270KeyboardLayoutTests
{
    [Fact]
    public void The3278HasItsEightySevenKeys()
    {
        // GA27-2890-4 Figure 3-2 note: 87-key keyboard (75 + the PF13-PF24 block).
        Assert.Equal(87, Ibm3270Keyboards.Ibm3278.Keys.Count);
    }

    [Fact]
    public void TheKb3270PlusHasItsHundredAndTwentyTwoKeys()
    {
        Assert.Equal(122, Ibm3270Keyboards.Kb3270Plus.Keys.Count);
    }

    [Theory]
    [InlineData("IBM3278")]
    [InlineData("KB3270PLUS")]
    public void NoTwoKeysOverlap(string id)
    {
        var keys = Find(id).Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            for (int j = i + 1; j < keys.Count; j++)
            {
                var a = keys[i];
                var b = keys[j];
                bool overlap = a.X < b.X + b.Width - 0.001 && b.X < a.X + a.Width - 0.001
                    && a.Y < b.Y + b.Height - 0.001 && b.Y < a.Y + a.Height - 0.001;
                Assert.False(overlap, "'" + a.Legend + "' at " + a.X + "," + a.Y + " overlaps '" + b.Legend + "' at " + b.X + "," + b.Y);
            }
        }
    }

    [Theory]
    [InlineData("IBM3278")]
    [InlineData("KB3270PLUS")]
    public void EveryKeyNameOnACapIsInTheKeyTable(string id)
    {
        // A cap naming a key the table does not know would do nothing when clicked.
        var emulator = new Ibm3270Emulator();
        var keys = Find(id).Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            Check(emulator, keys[i].Press);
            Check(emulator, keys[i].ShiftPress);
            Check(emulator, keys[i].AltPress);
        }
    }

    [Fact]
    public void Alt1On3278IsPf1AndLettersTypeBothCases()
    {
        var one = FindCap(Ibm3270Keyboards.Ibm3278, "1");
        Assert.True(one.ResolvePress(shift: false, alt: true, out string? name, out _));
        Assert.Equal("PF1", name);

        var a = FindCap(Ibm3270Keyboards.Ibm3278, "A");
        a.ResolvePress(false, false, out _, out string? plain);
        a.ResolvePress(true, false, out _, out string? shifted);
        Assert.Equal("a", plain);
        Assert.Equal("A", shifted);
    }

    [Fact]
    public void AltOnAKeyWithoutAnAltFunctionDoesNothing()
    {
        var q = FindCap(Ibm3270Keyboards.Ibm3278, "Q");
        Assert.False(q.ResolvePress(false, true, out string? name, out string? text));
        Assert.Null(name);
        Assert.Null(text);
    }

    [Fact]
    public void TheEmulatorOffersBothKeyboardsThe3278First()
    {
        var keyboards = new Ibm3270Emulator().OnScreenKeyboards;
        Assert.Equal(2, keyboards.Count);
        Assert.Equal("IBM3278", keyboards[0].Id);
        Assert.Equal("KB3270PLUS", keyboards[1].Id);
        Assert.Equal("IBM", keyboards[0].Family);
    }

    private static void Check(Ibm3270Emulator emulator, string? name)
    {
        if (name != null)
        {
            Assert.NotEqual(NamedKeyResult.UnknownName, emulator.PressNamedKey(name, out _));
        }
    }

    private static KeyboardDescription Find(string id)
    {
        return id == "IBM3278" ? Ibm3270Keyboards.Ibm3278 : Ibm3270Keyboards.Kb3270Plus;
    }

    private static KeyCap FindCap(KeyboardDescription keyboard, string legend)
    {
        for (int i = 0; i < keyboard.Keys.Count; i++)
        {
            if (keyboard.Keys[i].Legend == legend)
            {
                return keyboard.Keys[i];
            }
        }

        throw new Xunit.Sdk.XunitException("no key '" + legend + "'");
    }
}
