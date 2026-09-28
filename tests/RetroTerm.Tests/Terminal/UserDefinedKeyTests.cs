using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECUDK - the strings a host loads into the function keys.
///
/// A host loads a key once and a single keypress then sends a whole command, without the host
/// having to be told about it again. It arrives as <c>DCS Pc ; Pl | key/hex ; key/hex ST</c>.
///
/// The lock is the part worth the most care. Both parameters default to 0, and in BOTH cases 0 is
/// the destructive reading: clear every key first, and lock them afterwards. That is DEC's choice,
/// and a terminal that ignored the lock would let any text on the wire redefine what a keypress
/// sends.
/// </summary>
public class UserDefinedKeyTests
{
    private static TerminalEmulatorBase Build(string type = "VT220")
        => EmulatorFactory.CreateEmulator(type, 80, 24, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string KeyText(TerminalEmulatorBase emulator, int keyCode)
    {
        Assert.True(emulator.UserKeys.TryGet(keyCode, out var value), $"key {keyCode} is not defined");
        return Encoding.ASCII.GetString(value);
    }

    [Fact]
    public void AKeyCanBeLoaded()
    {
        // F6 is key 17. "6c73" is "ls".
        var emulator = Build();

        Feed(emulator, "\x1bP1;1|17/6c73\x1b\\");

        Assert.Equal("ls", KeyText(emulator, 17));
    }

    [Fact]
    public void SeveralKeysArriveInOneSequence()
    {
        var emulator = Build();

        Feed(emulator, "\x1bP1;1|17/6c73;18/707764\x1b\\");

        Assert.Equal("ls", KeyText(emulator, 17));
        Assert.Equal("pwd", KeyText(emulator, 18));
        Assert.Equal(2, emulator.UserKeys.Count);
    }

    [Fact]
    public void TheDefaultIsToClearEveryOtherKeyFirst()
    {
        // Pc omitted means 0 means "clear all". A host sending the short form gets a clean slate,
        // which is what it asked for even though it did not say so.
        var emulator = Build();
        Feed(emulator, "\x1bP1;1|17/6c73\x1b\\");

        Feed(emulator, "\x1bP;1|18/707764\x1b\\");

        Assert.False(emulator.UserKeys.TryGet(17, out _));
        Assert.Equal("pwd", KeyText(emulator, 18));
    }

    [Fact]
    public void AndTheOtherFormLeavesThemAlone()
    {
        var emulator = Build();
        Feed(emulator, "\x1bP1;1|17/6c73\x1b\\");

        Feed(emulator, "\x1bP1;1|18/707764\x1b\\");

        Assert.Equal("ls", KeyText(emulator, 17));
        Assert.Equal("pwd", KeyText(emulator, 18));
    }

    [Fact]
    public void TheDefaultIsAlsoToLockTheKeysAfterwards()
    {
        // THE test. Pl omitted means 0 means "lock", and once locked nothing on the wire may
        // redefine a key - which is exactly what the lock is for.
        var emulator = Build();

        Feed(emulator, "\x1bP1|17/6c73\x1b\\");
        Assert.True(emulator.UserKeys.IsLocked);

        Feed(emulator, "\x1bP1;1|17/726d202d726620\x1b\\");

        Assert.Equal("ls", KeyText(emulator, 17));
    }

    [Fact]
    public void OnlyAHardResetUnlocksThem()
    {
        // If a host could unlock by asking, the lock would stop nothing.
        var emulator = Build();
        Feed(emulator, "\x1bP1|17/6c73\x1b\\");
        Assert.True(emulator.UserKeys.IsLocked);

        emulator.Reset();

        Assert.False(emulator.UserKeys.IsLocked);
        Assert.Equal(0, emulator.UserKeys.Count);

        Feed(emulator, "\x1bP1;1|17/707764\x1b\\");
        Assert.Equal("pwd", KeyText(emulator, 17));
    }

    [Fact]
    public void AnEmptyStringTakesTheKeyBack()
    {
        var emulator = Build();
        Feed(emulator, "\x1bP1;1|17/6c73\x1b\\");

        Feed(emulator, "\x1bP1;1|17/\x1b\\");

        Assert.False(emulator.UserKeys.TryGet(17, out _));
    }

    [Fact]
    public void OneBadEntryDoesNotCostTheGoodOnes()
    {
        // Odd-length hex is not a sequence of bytes, so that entry is dropped rather than having
        // its last half-byte guessed at. The rest of the payload is still read.
        var emulator = Build();

        Feed(emulator, "\x1bP1;1|17/6c7;18/707764;19/6c73\x1b\\");

        Assert.False(emulator.UserKeys.TryGet(17, out _));
        Assert.Equal("pwd", KeyText(emulator, 18));
        Assert.Equal("ls", KeyText(emulator, 19));
    }

    [Fact]
    public void RubbishInTheHexIsRejectedRatherThanDecodedAsZero()
    {
        var emulator = Build();

        Feed(emulator, "\x1bP1;1|17/zzzz\x1b\\");

        Assert.False(emulator.UserKeys.TryGet(17, out _));
    }

    [Fact]
    public void TheStoreHasACeiling()
    {
        // Nothing arriving from a host may make this terminal allocate without end.
        var emulator = Build();
        var payload = new StringBuilder("\x1bP1;1|");
        for (int key = 1; key <= 40; key++)
        {
            if (key > 1) payload.Append(';');
            payload.Append(key).Append('/');
            // 256 bytes each - the per-key maximum - so forty of them cannot all fit.
            payload.Append('4', 512);
        }
        payload.Append("\x1b\\");

        Feed(emulator, payload.ToString());

        Assert.True(emulator.UserKeys.Count > 0, "the definitions that fit should still be there");
        Assert.True(emulator.UserKeys.Count < 40, "and the ones past the ceiling should not");
    }

    [Fact]
    public void ATerminalWithoutTheFeatureIgnoresTheWholeThing()
    {
        // A VT100 has no user-defined keys. Accepting them there would be inventing a machine.
        var emulator = Build("VT100");

        Feed(emulator, "\x1bP1;1|17/6c73\x1b\\");

        Assert.Equal(0, emulator.UserKeys.Count);
    }

    [Fact]
    public void ASixelSequenceIsNotMistakenForKeys()
    {
        // Sixel and ReGIS arrive through the same three hooks and are told apart by the final
        // byte. A DCS q payload reaching the key store would be nonsense.
        var emulator = Build();

        Feed(emulator, "\x1bP0;0;0q#0;2;0;0;0\x1b\\");

        Assert.Equal(0, emulator.UserKeys.Count);
    }
}
