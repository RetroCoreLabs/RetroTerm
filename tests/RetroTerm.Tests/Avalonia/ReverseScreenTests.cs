using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// DECSCNM, private mode 5 - light or dark screen.
/// </summary>
/// <remarks>
/// <para><b>The rule, from the VT420 Programmer Reference</b></para>
/// "When DECSCNM is set, the screen displays dark characters on a light background. When DECSCNM
/// is reset, the screen displays light characters on a dark background." And the note that decides
/// where it is implemented: "Screen mode only affects how the data appears on the screen. DECSCNM
/// does not change the data in page memory."
///
/// <para><b>Why these are rendering tests</b></para>
/// A mode that only changes how the screen LOOKS cannot be checked by reading the buffer - the
/// buffer is required to be unchanged. So the pixels are the assertion, and there is a PNG to
/// look at as well, because whether an inverted screen reads well is a question for a person.
/// </remarks>
[Collection("Avalonia")]
public class ReverseScreenTests
{
    /// <summary>
    /// ESC as its own string, never written inline before a character that could be a hex digit.
    /// </summary>
    private static readonly string Esc = ((char)0x1B).ToString();

    private static void Feed(TerminalEmulatorBase emulator, string data)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(data));

    private static TerminalEmulatorBase Build()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT420", 40, 10, 100);
        Feed(emulator, "\x1b[?25l");     // hide the cursor so it cannot colour a sampled cell
        return emulator;
    }

    [AvaloniaFact]
    public void AReversedScreenSwapsTheColoursOfEveryCell()
    {
        var normal = Build();
        Feed(normal, "HELLO");

        var reversed = Build();
        Feed(reversed, "HELLO\x1b[?5h");

        using var plain = RenderedScreenshot.Capture(normal, "decscnm-normal");
        using var inverse = RenderedScreenshot.Capture(reversed, "decscnm-reversed");

        // The cell holding a character: its dominant colour is the BACKGROUND, because a glyph
        // never covers most of its cell. So the dominant colours must have traded places.
        var plainGround = plain.DominantColorInCell(0, 0);
        var inverseGround = inverse.DominantColorInCell(0, 0);

        Assert.NotEqual(plainGround, inverseGround);

        // And an untouched cell far from the text inverts too - this is a SCREEN mode, not a
        // property of the characters.
        Assert.NotEqual(plain.DominantColorInCell(5, 20), inverse.DominantColorInCell(5, 20));
    }

    [AvaloniaFact]
    public void TurningItOffPutsTheScreenBack()
    {
        var emulator = Build();
        Feed(emulator, "HELLO");
        using var before = RenderedScreenshot.Capture(emulator, "decscnm-before");

        Feed(emulator, "\x1b[?5h");
        Feed(emulator, "\x1b[?5l");
        using var after = RenderedScreenshot.Capture(emulator, "decscnm-after");

        Assert.Equal(before.DominantColorInCell(0, 0), after.DominantColorInCell(0, 0));
        Assert.Equal(before.DominantColorInCell(5, 20), after.DominantColorInCell(5, 20));
    }

    [AvaloniaFact]
    public void ACellAlreadyMarkedReverseComesOutNormalOnAReversedScreen()
    {
        // The two swaps cancel. This is what keeps a highlighted menu bar standing out when a host
        // turns the whole screen inverse - and it is why the renderer uses an exclusive-or rather
        // than letting one of the two win.
        var plainCell = Build();
        Feed(plainCell, "AB");

        var doubleReversed = Build();
        Feed(doubleReversed, "\x1b[7mAB\x1b[m\x1b[?5h");

        using var normal = RenderedScreenshot.Capture(plainCell, "decscnm-plain-cell");
        using var both = RenderedScreenshot.Capture(doubleReversed, "decscnm-double-reversed");

        Assert.Equal(normal.DominantColorInCell(0, 0), both.DominantColorInCell(0, 0));
    }

    [AvaloniaFact]
    public void TheBufferIsNotTouched()
    {
        // "DECSCNM does not change the data in page memory." A mode that rewrote the cells would
        // corrupt every report that reads them back.
        var emulator = Build();
        Feed(emulator, "HELLO");

        emulator.GetBuffer().TryGetCell(0, 0, out var before);
        Feed(emulator, "\x1b[?5h");
        emulator.GetBuffer().TryGetCell(0, 0, out var after);

        Assert.Equal(before.Foreground, after.Foreground);
        Assert.Equal(before.Background, after.Background);
        Assert.Equal(before.Attributes, after.Attributes);
    }

    [Fact]
    public void DecrqmAnswersForTheMode()
    {
        // It used to answer "not recognised", because ReverseVideoMode was a field nothing wrote
        // and nothing read. Found while adding DECST8C, whose parameter is the same number.
        var emulator = EmulatorFactory.CreateEmulator("VT420", 20, 10, 100);
        string? reply = null;
        emulator.DataToSend += bytes => reply = Encoding.ASCII.GetString(bytes);

        Feed(emulator, "\x1b[?5$p");
        Assert.Equal("\x1b[?5;2$y", reply);       // reset at power-up: dark background

        Feed(emulator, "\x1b[?5h\x1b[?5$p");
        Assert.Equal("\x1b[?5;1$y", reply);
    }

    [Fact]
    public void TheModeCanBeSavedAndRestored()
    {
        // It is a private mode like any other, so XTSAVE and XTRESTORE must carry it. Before
        // DECSCNM existed, GetPrivateModeState returned null for 5 and the save was skipped.
        var emulator = EmulatorFactory.CreateEmulator("VT420", 20, 10, 100);
        string? reply = null;
        emulator.DataToSend += bytes => reply = Encoding.ASCII.GetString(bytes);

        Feed(emulator, "\x1b[?5h");      // inverse on
        Feed(emulator, "\x1b[?5s");      // remember it
        Feed(emulator, "\x1b[?5l");      // back to normal
        Feed(emulator, "\x1b[?5r");      // restore

        Feed(emulator, "\x1b[?5$p");
        Assert.Equal("\x1b[?5;1$y", reply);
    }

    [Fact]
    public void AHardResetPutsTheScreenBackToDark()
    {
        var emulator = EmulatorFactory.CreateEmulator("VT420", 20, 10, 100);
        string? reply = null;
        emulator.DataToSend += bytes => reply = Encoding.ASCII.GetString(bytes);

        Feed(emulator, "\x1b[?5h");

        // Esc + "c", NOT "\x1bc". C# hex escapes are VARIABLE length, so that literal is the
        // single character U+01BC and the reset never happens. This test caught it by failing;
        // TektronixCharacterSizeTests carries the same warning for the same reason.
        Feed(emulator, Esc + "c");       // RIS

        Feed(emulator, "\x1b[?5$p");
        Assert.Equal("\x1b[?5;2$y", reply);
    }
}
