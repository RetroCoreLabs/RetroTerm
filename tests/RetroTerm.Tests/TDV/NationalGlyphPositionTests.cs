using RetroTerm.Core.Fonts;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Phase 4 part 3: one national mapping, and it is the ROM's (problem B.10 continued).
///
/// BitmapFontRenderer carried its own inline Unicode→ASCII table for the case where a cell holds a
/// national character the font has no glyph for. Compared side by side with the ISO 646 tables in
/// TDVCharacterSets — and with FontTDV2215's own header comment describing its ROM — that table was
/// wrong, not merely duplicated:
///
///   renderer said   Æ→'^'  Ø→'`'  æ→'~'  ø→'`'
///   the ROM says    Æ→'['  Ø→'\'  æ→'{'  ø→'|'
///
/// Ø and ø resolved to the SAME position (0x60), which no character generator does. It was also
/// applied regardless of which variant was active, so a terminal in International mode would draw
/// '[' where a Ä appeared instead of drawing nothing.
///
/// These tests pin the corrected mapping and the variant-awareness the old table lacked.
/// </summary>
public class NationalGlyphPositionTests
{
    // ─────────────────────────────────────────────────────────────
    // The four the old renderer table got wrong
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData('Æ', '[')]
    [InlineData('Ø', '\\')]
    [InlineData('æ', '{')]
    [InlineData('ø', '|')]
    public void TheNorwegianCharactersResolveToTheirRomPositions(char nationalChar, char expected)
    {
        Assert.True(TDVCharacterSets.TryMapUnicodeToRomPosition(nationalChar, TDV2200ISO646Variant.Norwegian, out char position));
        Assert.Equal(expected, position);
    }

    [Fact]
    public void UpperAndLowerCaseNeverShareAPosition()
    {
        // THE defect. The old table sent both Ø and ø to 0x60, so one of them could not render.
        TDVCharacterSets.TryMapUnicodeToRomPosition('Ø', TDV2200ISO646Variant.Norwegian, out char upper);
        TDVCharacterSets.TryMapUnicodeToRomPosition('ø', TDV2200ISO646Variant.Norwegian, out char lower);

        Assert.NotEqual(upper, lower);
    }

    [Fact]
    public void TheMappingAgreesWithTheForwardIso646Table()
    {
        // Round trip: whatever position we resolve to must be the position the forward table draws
        // that character at. This is what stops the two tables drifting apart again.
        var forward = TDVCharacterSets.GetISO646VariantMapping(TDV2200ISO646Variant.Norwegian);

        var enumerator = forward.GetEnumerator();
        while (enumerator.MoveNext())
        {
            char asciiPosition = enumerator.Current.Key;
            char nationalChar = enumerator.Current.Value;
            if (asciiPosition == nationalChar) continue;   // identity entries carry no information

            Assert.True(TDVCharacterSets.TryMapUnicodeToRomPosition(nationalChar, TDV2200ISO646Variant.Norwegian, out char position));
            Assert.Equal(asciiPosition, position);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Variant awareness — the property the old table had none of
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheSameCharacterResolvesDifferentlyUnderDifferentVariants()
    {
        // Ä sits at '@' in the Norwegian set and at '[' in the Swedish one. A variant-blind table
        // has to be wrong for one of them.
        Assert.True(TDVCharacterSets.TryMapUnicodeToRomPosition('Ä', TDV2200ISO646Variant.Norwegian, out char norwegian));
        Assert.True(TDVCharacterSets.TryMapUnicodeToRomPosition('Ä', TDV2200ISO646Variant.Swedish, out char swedish));

        Assert.NotEqual(norwegian, swedish);
    }

    [Fact]
    public void InternationalMapsNothing()
    {
        // International IS US ASCII — it has no national positions, so a Ä must resolve to nothing
        // and be left undrawn rather than drawn as some unrelated ASCII glyph.
        Assert.False(TDVCharacterSets.TryMapUnicodeToRomPosition('Ä', TDV2200ISO646Variant.International, out _));
        Assert.False(TDVCharacterSets.TryMapUnicodeToRomPosition('ø', TDV2200ISO646Variant.International, out _));
    }

    [Fact]
    public void GermanEszettResolvesToItsOwnPosition()
    {
        Assert.True(TDVCharacterSets.TryMapUnicodeToRomPosition('ß', TDV2200ISO646Variant.German, out char position));
        Assert.Equal('~', position);   // the old renderer table said '{', which is ä
    }

    [Fact]
    public void PlainAsciiIsNeverRemapped()
    {
        Assert.False(TDVCharacterSets.TryMapUnicodeToRomPosition('A', TDV2200ISO646Variant.Norwegian, out _));
        Assert.False(TDVCharacterSets.TryMapUnicodeToRomPosition('[', TDV2200ISO646Variant.Norwegian, out _));
    }

    [Fact]
    public void ACharacterTheVariantDoesNotCarryResolvesToNothing()
    {
        // Ö has no position in the Norwegian set (Ø occupies 0x5C there).
        Assert.False(TDVCharacterSets.TryMapUnicodeToRomPosition('Ö', TDV2200ISO646Variant.Norwegian, out _));
    }

    // ─────────────────────────────────────────────────────────────
    // The variant reaches the font, which is what the renderer reads
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheFontExposesItsVariantThroughTheBase()
    {
        // TerminalRenderer.SyncFontVariant pushes the emulator's variant onto the font; the renderer
        // then reads it back through FontBase to pick the right mapping. Both TDV fonts must answer.
        FontBase font2200 = new FontTDV2200();
        FontBase font2215 = new FontTDV2215();

        font2200.CharacterSetVariant = (int)TDV2200ISO646Variant.Norwegian;
        font2215.CharacterSetVariant = (int)TDV2200ISO646Variant.Swedish;

        Assert.Equal((int)TDV2200ISO646Variant.Norwegian, font2200.CharacterSetVariant);
        Assert.Equal((int)TDV2200ISO646Variant.Swedish, font2215.CharacterSetVariant);
    }

    [Fact]
    public void TheFallbackOnlyMattersForFontsThatRefuseHighCodepoints()
    {
        // Honest scope note, held as a test so it cannot quietly stop being true:
        // FontTDV2215 returns null for anything >= 0x80, so the renderer's fallback is what makes a
        // Unicode national character drawable there. FontTDV2200 has no such guard — a high value
        // indexes into its ROM and returns SOME glyph, so the fallback never runs on a 2200 and a
        // stray Unicode character silently draws the wrong glyph. That second half is a separate
        // issue from the mapping table, and is not fixed here.
        var font2215 = new FontTDV2215();
        Assert.Null(font2215.GetFontBits(0x00D8, 0));   // Ø as Unicode: no glyph

        var font2200 = new FontTDV2200();
        Assert.NotNull(font2200.GetFontBits(0x00D8, 0));   // returns something, correct or not
    }

    [Fact]
    public void ResolvingThenLookingUpGivesTheNorwegianGlyphOnA2215()
    {
        // End to end through the path the renderer now takes.
        var font = new FontTDV2215();
        font.CharacterSetVariant = (int)TDV2200ISO646Variant.Norwegian;

        Assert.True(TDVCharacterSets.TryMapUnicodeToRomPosition('Ø', TDV2200ISO646Variant.Norwegian, out char position));
        var viaUnicode = font.GetFontBits(position, 0);
        var direct = font.GetFontBits('\\', 0);      // Ø sits at 0x5C in the Norwegian charset

        Assert.NotNull(viaUnicode);
        Assert.Equal(direct, viaUnicode);
    }

    [Fact]
    public void TheLanguageCodeHelperMatchesWhatTheEmulatorReports()
    {
        // GetISO646LanguageCode now delegates here; pin that the two cannot diverge.
        var emulator = new TDV2200Emulator(80, 24);
        emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.Swedish;

        Assert.Equal(TDVCharacterSets.GetLanguageCodeForVariant(TDV2200ISO646Variant.Swedish), emulator.GetISO646LanguageCode());
        Assert.Equal("sv", emulator.GetISO646LanguageCode());
    }
}
