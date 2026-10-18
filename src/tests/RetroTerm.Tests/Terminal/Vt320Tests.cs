using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The VT320 - the VT300-series text terminal.
///
/// Its identity comes from DEC's own table in the VT330/VT340 Programmer Reference
/// (EK-VT3XX-TP-001): service class 61 is level 1, the VT100 family; 63 is level 3, the VT200 or
/// VT300 family. The extension numbers are DEC's too - 1 for 132 columns, 2 printer port, 3 ReGIS,
/// 4 Sixel, 6 selective erase, 7 soft character set, 8 user-defined keys, 9 national replacement
/// sets.
///
/// This terminal was NOT built until that document was available. The family code is not something
/// to guess at, and a DA reply is the one thing a host believes without checking.
/// </summary>
public class Vt320Tests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT320", 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Ask(TerminalEmulatorBase emulator, string request)
    {
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));
        Feed(emulator, request);
        return replies.ToString();
    }

    [Fact]
    public void ItAnswersAsAVt300FamilyTerminal()
    {
        // 63 is the service class code for the VT200/VT300 family, from the manual's own table.
        Assert.Equal(Esc + "[?63;1;6;7;8c", Ask(Build(), Esc + "[c"));
    }

    [Fact]
    public void ItClaimsOnlyTheExtensionsItActuallyHas()
    {
        // 132 columns, selective erase, soft character set, user-defined keys - all four are built
        // and tested. The printer port (2) and national replacement sets (9) are not built, so
        // they are not claimed, and neither is ReGIS (3) or Sixel (4).
        var reply = Ask(Build(), Esc + "[c");

        Assert.DoesNotContain(";2;", reply);
        Assert.DoesNotContain(";3;", reply);
        Assert.DoesNotContain(";4;", reply);
        Assert.DoesNotContain(";9", reply);
    }

    [Fact]
    public void AndTheGraphicsThatSeparateItFromAVt340AreAbsent()
    {
        var emulator = Build();

        Feed(emulator, Esc + "Pq#1~" + Esc + "\\");        // a Sixel image

        Assert.False(emulator.Graphics?.HasAnythingToDraw() ?? false);
    }

    [Fact]
    public void TheFourExtensionsItClaimsAllWork()
    {
        // A DA reply is a promise. This is the test that it is kept.
        var emulator = Build();

        // 132 columns (DECCOLM)
        Feed(emulator, Esc + "[?3h");
        Assert.Equal(132, emulator.Width);

        // Selective erase: DECSCA protects, and the selective erase spares it
        Feed(emulator, Esc + "[?3l");
        Feed(emulator, Esc + "[1\"q" + Esc + "[1;1H" + "KEEP");
        Feed(emulator, Esc + "[1;1H" + Esc + "[?2J");
        emulator.GetBuffer().TryGetCell(0, 0, out var kept);
        Assert.Equal('K', (char)kept.Codepoint);

        // User-defined keys
        Feed(emulator, Esc + "P1|17/6c73" + Esc + "\\");
        Assert.True(emulator.UserKeys.Count > 0);

        // Soft character set
        Feed(emulator, Esc + "P1;1;1;8;0;0;12;0{ @~" + Esc + "\\");
        Assert.True(emulator.SoftFont.Count > 0);
    }

    [Fact]
    public void ItHasPageMemoryLikeTheRestOfTheThreeHundredSeries()
    {
        var emulator = Build();

        Feed(emulator, Esc + "[24t");          // DECSLPP: six pages of 24 lines

        Assert.Equal(6, emulator.PageCount);
    }

    [Fact]
    public void TheSecondaryReplyNamesTheTerminalAsWell()
    {
        // Terminal id 24 is the VT320's.
        Assert.Equal(Esc + "[>24;10;0c", Ask(Build(), Esc + "[>c"));
    }
}
