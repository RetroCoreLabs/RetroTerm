using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Every language a user can pick has to do something.
///
/// The connection dialog offered six languages and the mapping that turns one into a character set
/// knew four of them. Finnish was offered, had no case, and fell through to US ASCII - so someone
/// choosing Finnish lost every letter they chose it for and nothing said why.
/// </summary>
public class LanguageOptionsTests
{
    [Fact]
    public void FinnishSelectsTheSwedishVariantBecauseTheyAreTheSameSet()
    {
        // THE defect. SFS 4017 and SEN 850200 register the same set, ISO-IR-10, so a Finnish user
        // needs the Swedish variant - not International, which substitutes nothing at all.
        Assert.Equal(TDV2200ISO646Variant.Swedish, TDVLanguageOptions.ToVariant("Finnish"));
        Assert.Equal(TDV2200ISO646Variant.Swedish, TDVLanguageOptions.ToVariant("fi"));
    }

    [Fact]
    public void EveryLanguageOfferedHasAnAnswerAndSaysWhy()
    {
        // The guard that keeps the list and the mapping the same data. A language that resolves to
        // International must be one that MEANS International, not one nobody wrote a case for.
        var all = TDVLanguageOptions.All;
        Assert.NotEmpty(all);

        for (int i = 0; i < all.Length; i++)
        {
            var option = all[i];
            Assert.Equal(option.Variant, TDVLanguageOptions.ToVariant(option.DisplayName));
            Assert.Equal(option.Variant, TDVLanguageOptions.ToVariant(option.Code));
            Assert.False(string.IsNullOrWhiteSpace(option.Note),
                option.DisplayName + " must say which standard it maps to and why");
        }
    }

    [Fact]
    public void TheDropdownNamesAreExactlyTheListedLanguages()
    {
        var names = TDVLanguageOptions.DisplayNames();
        var all = TDVLanguageOptions.All;

        Assert.Equal(all.Length, names.Length);
        for (int i = 0; i < all.Length; i++)
        {
            Assert.Equal(all[i].DisplayName, names[i]);
        }
    }

    [Fact]
    public void TheOtherNordicLanguagesStillLandWhereTheyDid()
    {
        Assert.Equal(TDV2200ISO646Variant.Norwegian, TDVLanguageOptions.ToVariant("Norwegian"));
        Assert.Equal(TDV2200ISO646Variant.Swedish, TDVLanguageOptions.ToVariant("Swedish"));
        Assert.Equal(TDV2200ISO646Variant.German, TDVLanguageOptions.ToVariant("German"));
        Assert.Equal(TDV2200ISO646Variant.International, TDVLanguageOptions.ToVariant("English"));
    }

    [Fact]
    public void DanishTakesTheNorwegianVariantAsTheClosestTheHardwareHas()
    {
        // NOT because the sets are identical - DS 2089 is its own registration - but because a TDV
        // has no Danish variant and this is the nearest it can produce.
        Assert.Equal(TDV2200ISO646Variant.Norwegian, TDVLanguageOptions.ToVariant("Danish"));
        Assert.Equal(TDV2200ISO646Variant.Norwegian, TDVLanguageOptions.ToVariant("da"));
    }

    [Fact]
    public void TheOldDanishCodeStillWorksForConnectionsAlreadySaved()
    {
        // "dk" is what the previous mapping accepted, and those connections are on disk.
        Assert.Equal(TDV2200ISO646Variant.Norwegian, TDVLanguageOptions.ToVariant("dk"));
    }

    [Fact]
    public void NothingAndNonsenseBothMeanPlainAscii()
    {
        Assert.Equal(TDV2200ISO646Variant.International, TDVLanguageOptions.ToVariant(null));
        Assert.Equal(TDV2200ISO646Variant.International, TDVLanguageOptions.ToVariant(""));
        Assert.Equal(TDV2200ISO646Variant.International, TDVLanguageOptions.ToVariant("Klingon"));
    }
}
