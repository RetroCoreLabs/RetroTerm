using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Profiles;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The DEC VT102 — a VT100 that can insert and delete.
///
/// All four editing sequences already worked; what was missing was a terminal that SAYS so. A host
/// reads the DA reply and decides whether it may send IL, DL, ICH and DCH, or whether it has to
/// redraw a whole line for every change. Answering as a VT100 meant the second one.
/// </summary>
public class Vt102Tests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEmulatorBase Build()
        => EmulatorFactory.CreateEmulator("VT102", 20, 6, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row)
    {
        var text = new StringBuilder(emulator.Width);
        for (int col = 0; col < emulator.Width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            text.Append(cell.Codepoint == 0 ? ' ' : (char)cell.Codepoint);
        }
        return text.ToString().TrimEnd();
    }

    [Fact]
    public void ItAnswersAsAVt102()
    {
        // CSI ? 6 c is the VT102's own answer - not a list of extensions, because that convention
        // came later.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "[c");

        Assert.Equal(Esc + "[?6c", replies.ToString());
    }

    [Fact]
    public void ItSaysNothingToASecondaryRequest()
    {
        // A judgement call, stated in the profile: I could not confirm whether a VT102 answered
        // secondary DA at all, and silence is the safer of the two wrong answers - a host that gets
        // no reply falls back, while one given a made-up identity acts on it.
        var emulator = Build();
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        Feed(emulator, Esc + "[>c");

        Assert.Equal("", replies.ToString());
    }

    [Fact]
    public void TheEditingSequencesItAdvertisesAllWork()
    {
        // The check that keeps the DA honest: if any of these stops working, the reply above
        // becomes a claim this terminal cannot back up.
        var emulator = Build();

        // ICH - open a gap and the text moves right rather than being overwritten.
        Feed(emulator, Esc + "[H" + "ABCDE" + Esc + "[H" + Esc + "[2@");
        Assert.Equal("  ABCDE", Row(emulator, 0));

        // DCH - close it again.
        Feed(emulator, Esc + "[H" + Esc + "[2P");
        Assert.Equal("ABCDE", Row(emulator, 0));

        // IL - push this line down and leave a blank one.
        Feed(emulator, Esc + "[H" + Esc + "[L");
        Assert.Equal("", Row(emulator, 0));
        Assert.Equal("ABCDE", Row(emulator, 1));

        // DL - take it away again.
        Feed(emulator, Esc + "[H" + Esc + "[M");
        Assert.Equal("ABCDE", Row(emulator, 0));
    }

    [Fact]
    public void ItIsSelectableAndCallsItselfAVt102()
    {
        Assert.True(EmulatorFactory.IsSupported("VT102"));

        var emulator = Build();
        Assert.Same(TerminalProfile.VT102, emulator.Profile);
        Assert.Equal("VT102", emulator.GetTerminalType());
    }

    [Fact]
    public void ItCanStillDropIntoVt52Mode()
    {
        // A VT102 has DECANM like the rest of the family.
        var emulator = Build();

        Feed(emulator, Esc + "[?2l");

        Assert.True(emulator.Vt52Mode);
    }

    [Fact]
    public void ItDoesNotClaimTheThingsAVt220Added()
    {
        // No downloadable character sets, no user-defined keys. A VT102 predates both, and a
        // profile that offered them would be a different machine wearing the name.
        Assert.False(TerminalProfile.VT102.Supports(TerminalFeatures.SoftCharacterSet));
        Assert.False(TerminalProfile.VT102.Supports(TerminalFeatures.UserDefinedKeys));

        var emulator = Build();
        Feed(emulator, Esc + "P1;1|17/6c73" + Esc + "\\");
        Assert.Equal(0, emulator.UserKeys.Count);
    }
}
