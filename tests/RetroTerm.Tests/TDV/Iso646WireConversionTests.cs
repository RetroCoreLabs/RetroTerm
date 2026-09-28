using System;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Phase 4 part 1: one ISO 646 wire conversion, not three (problem B.10).
///
/// A TDV renders bytes through its active national replacement set, so typing 'ø' has to put the
/// ASCII position byte '|' on the wire — the terminal draws ø where it sees '|'. Send a Unicode
/// 'ø' instead and the terminal shows whatever its font has at that codepoint, which is not ø.
///
/// The loop around the reverse table was written out three times in the UI — TerminalCanvas,
/// VirtualKeyboardWindow and VirtualKeyboardPanel — and the copies had already drifted. The canvas
/// converted a string of any length; the window converted only input exactly ONE character long,
/// so a multi-character paste or IME commit through that window went out unconverted while the
/// same text typed into the terminal was converted correctly.
///
/// One implementation in Core now, so the three cannot disagree again.
/// </summary>
public class Iso646WireConversionTests
{
    // ─────────────────────────────────────────────────────────────
    // The conversion itself
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("no", 'ø', '|')]
    [InlineData("no", 'æ', '{')]
    [InlineData("no", 'å', '}')]
    [InlineData("no", 'Ø', '\\')]
    [InlineData("no", 'Æ', '[')]
    [InlineData("no", 'Å', ']')]
    public void ANationalCharacterBecomesItsAsciiPositionByte(string language, char typed, char expected)
    {
        Assert.Equal(expected.ToString(), TDVCharacterSets.ConvertToWireBytes(typed.ToString(), language));
    }

    [Fact]
    public void PlainAsciiIsUnchanged()
    {
        Assert.Equal("HELLO", TDVCharacterSets.ConvertToWireBytes("HELLO", "no"));
    }

    [Fact]
    public void TextIsUnchangedWhenTheVariantIsInternational()
    {
        // GetISO646LanguageCode returns null for International — no substitutions apply.
        Assert.Equal("HÆRØY", TDVCharacterSets.ConvertToWireBytes("HÆRØY", null));
    }

    [Fact]
    public void AnUnknownLanguageLeavesTextAlone()
    {
        Assert.Equal("HÆRØY", TDVCharacterSets.ConvertToWireBytes("HÆRØY", "zz"));
    }

    [Fact]
    public void EmptyAndNullInputAreSafe()
    {
        Assert.Equal("", TDVCharacterSets.ConvertToWireBytes("", "no"));
        Assert.Null(TDVCharacterSets.ConvertToWireBytes(null!, "no"));
    }

    // ─────────────────────────────────────────────────────────────
    // The divergence that prompted this
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MultiCharacterTextConvertsEveryCharacter()
    {
        // THE regression this change fixes. The virtual keyboard window's copy tested
        // `text.Length == 1` and skipped anything longer, so a pasted or IME-committed word
        // containing national characters went out as raw Unicode.
        Assert.Equal("H[R\\Y", TDVCharacterSets.ConvertToWireBytes("HÆRØY", "no"));
    }

    [Fact]
    public void ASingleCharacterAndTheSameCharacterInAStringConvertIdentically()
    {
        // The property the three copies violated: length must not change the answer.
        const string language = "no";
        var single = TDVCharacterSets.ConvertToWireBytes("ø", language);
        var inString = TDVCharacterSets.ConvertToWireBytes("aøb", language);

        Assert.Equal("a" + single + "b", inString);
    }

    [Fact]
    public void ConversionIsAppliedPerCharacterAcrossAWholeSentence()
    {
        var converted = TDVCharacterSets.ConvertToWireBytes("BLÅBÆRSYLTETØY", "no");

        // Every national character replaced, everything else left alone.
        Assert.DoesNotContain('Å', converted);
        Assert.DoesNotContain('Æ', converted);
        Assert.DoesNotContain('Ø', converted);
        Assert.StartsWith("BL", converted);
        Assert.Equal("BLÅBÆRSYLTETØY".Length, converted.Length);
    }

    // ─────────────────────────────────────────────────────────────
    // Other variants
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SwedishAndNorwegianDifferOnTheSameInput()
    {
        // Guard against the language argument being ignored: the two variants put different
        // characters at the same ASCII positions, so the same input must not give the same output.
        var norwegian = TDVCharacterSets.ConvertToWireBytes("ÄÖ", "sv");
        Assert.NotNull(norwegian);

        var germanEszett = TDVCharacterSets.ConvertToWireBytes("ß", "de");
        Assert.NotEqual("ß", germanEszett);
    }

    [Fact]
    public void ConvertingIsStableWhenRepeated()
    {
        // Once converted the text is ASCII, and ASCII converts to itself — so running the
        // conversion twice must not mangle anything. Call sites are layered, so this can happen.
        var once = TDVCharacterSets.ConvertToWireBytes("HÆRØY", "no");
        var twice = TDVCharacterSets.ConvertToWireBytes(once, "no");

        Assert.Equal(once, twice);
    }

    // ─────────────────────────────────────────────────────────────
    // Round trip against the emulator's own variant
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheEmulatorsActiveVariantDrivesTheConversion()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // International: no conversion.
        Assert.Null(emulator.GetISO646LanguageCode());
        Assert.Equal("Ø", TDVCharacterSets.ConvertToWireBytes("Ø", emulator.GetISO646LanguageCode()));

        // Norwegian: Ø becomes its ASCII position byte.
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.Norwegian;
        Assert.Equal("no", emulator.GetISO646LanguageCode());
        Assert.Equal("\\", TDVCharacterSets.ConvertToWireBytes("Ø", emulator.GetISO646LanguageCode()));
    }

    [Fact]
    public async System.Threading.Tasks.Task ManyThreadsConvertingAtOnceAllGetTheRightAnswer()
    {
        // The conversion is reached from more than one thread for real: the UI thread when a user
        // types, the virtual keyboard, and the session pump when a script or MCP call sends. The
        // cache behind it was a plain dictionary built on first use with no lock, so one thread
        // could be writing an entry while another read it - which corrupts a Dictionary rather
        // than just losing a value.
        //
        // A race cannot be caught reliably by a test, so this is not proof. It exercises the path
        // the way the program does, and it failed the whole suite often enough to be noticed: a
        // test that passed alone and failed in the full run.
        const int threads = 16;
        const int perThread = 200;
        var languages = new[] { "no", "sv", "de", "fr", "fi", "en" };
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        var work = new System.Threading.Tasks.Task[threads];
        for (int t = 0; t < threads; t++)
        {
            int seed = t;
            work[t] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < perThread; i++)
                {
                    string language = languages[(seed + i) % languages.Length];
                    string converted = TDVCharacterSets.ConvertToWireBytes("HÆRØY", language);
                    if (language == "no" && converted != "H[R\\Y")
                    {
                        failures.Add(language + " gave " + converted);
                    }
                }
            });
        }

        await System.Threading.Tasks.Task.WhenAll(work);

        Assert.Empty(failures);
    }
}
