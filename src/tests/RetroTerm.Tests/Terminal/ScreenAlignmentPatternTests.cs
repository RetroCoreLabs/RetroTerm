using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECALN — <c>ESC # 8</c>, Screen Alignment Pattern.
///
/// It fills the whole screen with capital E and homes the cursor. The point on real hardware was
/// to light every cell at once so an engineer could adjust the monitor's geometry, which is why
/// it ignores the scrolling region and the current attributes — the whole tube, in the plain
/// default rendition.
///
/// It was decoded by <c>EscapeSequenceDecoder</c> for the logs and then never implemented, so it
/// looked supported in a trace and did nothing on the screen. DEC's own vttest alignment screen
/// starts with DECALN and then erases regions out of the E's to leave a frame, so the whole
/// middle of that screen came out blank. Found by libvterm's 90vttest_01-movement-1 script.
/// </summary>
public class ScreenAlignmentPatternTests
{
    public static IEnumerable<object[]> AllEmulators()
    {
        var types = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < types.Length; i++)
        {
            yield return new object[] { types[i] };
        }
        yield return new object[] { "ANSI" };
    }

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecalnFillsEveryCellWithE(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b#8");

        for (int row = 0; row < emulator.Height; row++)
        {
            for (int col = 0; col < emulator.Width; col++)
            {
                Assert.True(emulator.GetBuffer().TryGetCell(row, col, out var cell));
                Assert.Equal((uint)'E', cell.Codepoint);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecalnHomesTheCursor(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[4;9H");
        Feed(emulator, "\u001b#8");

        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecalnIgnoresTheScrollingRegion(string emulatorType)
    {
        // It is a hardware alignment aid, not a screen operation — it paints the whole tube.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[2;4r");     // scroll region rows 2..4
        Feed(emulator, "\u001b#8");

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var top));
        Assert.True(emulator.GetBuffer().TryGetCell(emulator.Height - 1, 0, out var bottom));
        Assert.Equal((uint)'E', top.Codepoint);
        Assert.Equal((uint)'E', bottom.Codepoint);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecalnPaintsInThePlainDefaultRendition(string emulatorType)
    {
        // Whatever SGR was in force must not colour the pattern — the engineer is looking at cell
        // geometry, and a bold reverse-video screen would defeat the purpose.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1;7;31m");   // bold, reverse, red
        Feed(emulator, "\u001b#8");

        Assert.True(emulator.GetBuffer().TryGetCell(2, 5, out var cell));
        Assert.Equal(CharacterAttributes.None, cell.Attributes);
        Assert.Equal(TerminalColor.Default, cell.Foreground);
        Assert.Equal(TerminalColor.Default, cell.Background);
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecalnEndsAnyLineContinuation(string emulatorType)
    {
        // The pattern replaces what was there, so a line that used to spill over no longer does.
        var emulator = Build(emulatorType);
        Feed(emulator, new string('X', 25));         // 20 fill row 0, 5 spill onto row 1
        Assert.True(emulator.GetBuffer().IsLineWrapped(0));

        Feed(emulator, "\u001b#8");

        Assert.False(emulator.GetBuffer().IsLineWrapped(0));
    }
}
