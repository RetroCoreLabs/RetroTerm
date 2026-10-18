using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// DECSCA and the selective erases — the mechanism behind a fill-in form.
///
/// <c>CSI Ps " q</c> (DECSCA) marks what is typed next as protected (Ps=1) or erasable (Ps=0 or
/// 2). <c>CSI ? J</c> and <c>CSI ? K</c> (DECSED / DECSEL) then erase only the unprotected
/// characters. That pair is how a host draws a form once and afterwards clears what the user
/// typed without wiping the labels.
///
/// THE DEFECT. DECSCA was not implemented at all, and the private forms of the erases fell into
/// the DEC private-MODE switch — which knows about mode numbers, not about 'J' and 'K' — so they
/// matched nothing and returned. A host asking to clear the input fields of a form cleared
/// NOTHING, and the screen simply stopped responding to it.
///
/// Found by libvterm's 65screen_protect script — see tests\RetroTerm.Tests\Conformance.
/// </summary>
public class SelectiveEraseTests
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

    private static TerminalEmulatorBase Build(string type) => EmulatorFactory.CreateEmulator(type, 20, 4, 100);

    private static void Feed(TerminalEmulatorBase emulator, string text)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(text));

    private static string Row(TerminalEmulatorBase emulator, int row, int width)
    {
        var text = new StringBuilder(width);
        for (int col = 0; col < width; col++)
        {
            emulator.GetBuffer().TryGetCell(row, col, out var cell);
            uint cp = cell.Codepoint;
            text.Append(cp == 0 ? ' ' : (char)cp);
        }
        return text.ToString();
    }

    /// <summary>
    /// Writes A unprotected, B protected, C unprotected — the corpus's own setup.
    /// </summary>
    private static TerminalEmulatorBase BuildAbc(string type)
    {
        var emulator = Build(type);
        Feed(emulator, "A\u001b[1\"qB\u001b[\"qC");
        return emulator;
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecscaMarksOnlyWhatIsTypedWhileItIsOn(string emulatorType)
    {
        var emulator = BuildAbc(emulatorType);

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var a));
        Assert.True(emulator.GetBuffer().TryGetCell(0, 1, out var b));
        Assert.True(emulator.GetBuffer().TryGetCell(0, 2, out var c));

        Assert.False(a.Attributes.HasAttribute(CharacterAttributes.Protected));
        Assert.True(b.Attributes.HasAttribute(CharacterAttributes.Protected));
        Assert.False(c.Attributes.HasAttribute(CharacterAttributes.Protected));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void SelectiveEraseSparesProtectedCharacters(string emulatorType)
    {
        // The exact case from the corpus: erase the display selectively and B must survive.
        var emulator = BuildAbc(emulatorType);
        Feed(emulator, "\u001b[G\u001b[?J");

        Assert.Equal(" B", Row(emulator, 0, 3).TrimEnd());
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void PlainEraseTakesEverything(string emulatorType)
    {
        // The control. Without the '?' it is an ordinary ED and protection means nothing.
        var emulator = BuildAbc(emulatorType);
        Feed(emulator, "\u001b[G\u001b[J");

        Assert.Equal("", Row(emulator, 0, 3).TrimEnd());
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void SelectiveEraseInLineSparesProtectedCharacters(string emulatorType)
    {
        var emulator = BuildAbc(emulatorType);
        Feed(emulator, "\u001b[G\u001b[?K");

        Assert.Equal(" B", Row(emulator, 0, 3).TrimEnd());
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void ErasingAFieldDoesNotUnprotectIt(string emulatorType)
    {
        // A form is cleared and refilled many times, so the flag has to outlive the content.
        var emulator = BuildAbc(emulatorType);
        Feed(emulator, "\u001b[G\u001b[?J");

        Assert.True(emulator.GetBuffer().TryGetCell(0, 1, out var b));
        Assert.True(b.Attributes.HasAttribute(CharacterAttributes.Protected));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void DecscaZeroAndTwoBothMeanErasable(string emulatorType)
    {
        // Ps=0 and Ps=2 are the same instruction spelled two ways; only Ps=1 protects.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1\"qP\u001b[0\"qQ\u001b[2\"qR");

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var p));
        Assert.True(emulator.GetBuffer().TryGetCell(0, 1, out var q));
        Assert.True(emulator.GetBuffer().TryGetCell(0, 2, out var r));

        Assert.True(p.Attributes.HasAttribute(CharacterAttributes.Protected));
        Assert.False(q.Attributes.HasAttribute(CharacterAttributes.Protected));
        Assert.False(r.Attributes.HasAttribute(CharacterAttributes.Protected));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void SgrResetDoesNotUnprotect(string emulatorType)
    {
        // DECSCA is not a rendition. Holding it in CurrentAttributes would let SGR 0 clear it,
        // and a form would lose its protection the moment the host reset colours.
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1\"q\u001b[0mX");

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var x));
        Assert.True(x.Attributes.HasAttribute(CharacterAttributes.Protected));
    }

    [Theory]
    [MemberData(nameof(AllEmulators))]
    public void AResetClearsDecsca(string emulatorType)
    {
        var emulator = Build(emulatorType);
        Feed(emulator, "\u001b[1\"q");
        emulator.Reset();
        Feed(emulator, "Y");

        Assert.True(emulator.GetBuffer().TryGetCell(0, 0, out var y));
        Assert.False(y.Attributes.HasAttribute(CharacterAttributes.Protected));
    }
}
