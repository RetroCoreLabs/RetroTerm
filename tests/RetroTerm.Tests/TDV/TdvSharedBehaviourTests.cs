using System;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Phase 3 part 8: the TDV models share what they always shared (problem B.3/B.5).
///
/// TDV1200, TDV2215 and TDV2200 each declared the same four components, built them with
/// byte-identical constructor code, and then implemented the same five members on top of them:
/// CharacterSetVariant, GetAvailableCharacterSetVariants, GetISO646LanguageCode, KeyboardLights
/// and ProcessInput. The base already declared all five as virtual — as do-nothing stubs whose
/// own comments said "derived classes override this".
///
/// Checked line by line before changing anything: the three copies had NOT diverged. That is
/// worth stating, because it is the reason this is a safe move rather than a bug fix — but three
/// copies is still three places a future edit can land in only one of, which is what this suite
/// now prevents.
///
/// These tests assert the models AGREE, rather than re-asserting each model's behaviour
/// separately. Per-model tests would have passed just as happily with three implementations.
/// </summary>
public class TdvSharedBehaviourTests
{
    private static TDVEmulatorBase[] AllModels() => new TDVEmulatorBase[]
    {
        new TDV1200Emulator(80, 24),
        new TDV2215Emulator(80, 24),
        new TDV2200Emulator(80, 24),
    };

    private static void Feed(TerminalEmulatorBase emulator, string s)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(s));
    }

    // ─────────────────────────────────────────────────────────────
    // The national character set variants
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryModelOffersTheSameNationalVariants()
    {
        var models = AllModels();
        var reference = models[0].GetAvailableCharacterSetVariants();

        // Four per TDV2115 spec section 9.1: International, Norwegian, Swedish, German.
        Assert.Equal(4, reference.Length);

        for (int i = 1; i < models.Length; i++)
        {
            var variants = models[i].GetAvailableCharacterSetVariants();
            Assert.Equal(reference.Length, variants.Length);

            for (int v = 0; v < reference.Length; v++)
            {
                Assert.Equal(reference[v].Value, variants[v].Value);
                Assert.Equal(reference[v].Description, variants[v].Description);
            }
        }
    }

    [Fact]
    public void EveryModelStartsOnTheInternationalVariant()
    {
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            Assert.Null(models[i].GetISO646LanguageCode());
        }
    }

    [Theory]
    [InlineData(1, "no")]   // Norwegian
    [InlineData(2, "sv")]   // Swedish
    [InlineData(3, "de")]   // German
    public void EveryModelMapsAVariantToTheSameLanguageCode(int variant, string expected)
    {
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            models[i].CharacterSetVariant = variant;

            Assert.Equal(variant, models[i].CharacterSetVariant);
            Assert.Equal(expected, models[i].GetISO646LanguageCode());
        }
    }

    [Fact]
    public void SettingTheVariantRoundTripsOnEveryModel()
    {
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            var variants = models[i].GetAvailableCharacterSetVariants();

            for (int v = 0; v < variants.Length; v++)
            {
                models[i].CharacterSetVariant = variants[v].Value;
                Assert.Equal(variants[v].Value, models[i].CharacterSetVariant);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Keyboard lamps
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryModelStartsWithItsLampsOff()
    {
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            Assert.Equal((false, false, false), models[i].KeyboardLights);
        }
    }

    [Fact]
    public void EveryModelLightsTheSameLampForTheSameControlCode()
    {
        // ENQ lights L1, ACK lights L2, NAK lights L3, SYN clears them — the C0 codes the host
        // uses to drive the TDV keyboard's indicator lamps.
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            Feed(models[i], "\x05");   // ENQ
            Assert.True(models[i].KeyboardLights.L1, $"{models[i].GetTerminalType()}: ENQ should light L1");

            Feed(models[i], "\x16");   // SYN
            Assert.Equal((false, false, false), models[i].KeyboardLights);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The 2115 compatibility flag
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryModelStartsOutsideCompatibilityMode()
    {
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            Assert.False(models[i].Is2115CompatibilityMode);
        }
    }

    [Fact]
    public void EveryModelEntersAndLeavesCompatibilityModeTheSameWay()
    {
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            Feed(models[i], "\x1b[66l");
            Assert.True(models[i].Is2115CompatibilityMode, $"{models[i].GetTerminalType()} did not enter 2115 mode");

            Feed(models[i], "\x1b[66h");
            Assert.False(models[i].Is2115CompatibilityMode, $"{models[i].GetTerminalType()} did not leave 2115 mode");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Input still goes through the TDV pre-parser filter
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryModelStillFiltersHostBytesBeforeParsing()
    {
        // ProcessInput moved to the base; this checks the filter is still in the path rather than
        // bytes going straight to the escape parser. Plain text must reach the screen on all three.
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            models[i].ProcessInput(Encoding.ASCII.GetBytes("HELLO"));

            var row = RetroTerm.Core.Terminal.Buffer.ScreenReader.GetRowText(models[i].Buffer, 0);
            Assert.Equal("HELLO", row);
        }
    }

    [Fact]
    public void EveryModelRedrawsAfterProcessingInput()
    {
        // ProcessInput raises Invalidated (via OnInvalidated) so the UI repaints. Losing that in
        // the move would have left the screen stale with no test noticing.
        var models = AllModels();

        for (int i = 0; i < models.Length; i++)
        {
            int invalidations = 0;
            models[i].Invalidated += () => invalidations++;

            models[i].ProcessInput(Encoding.ASCII.GetBytes("X"));

            Assert.True(invalidations > 0, $"{models[i].GetTerminalType()} did not raise Invalidated");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // What the models are still allowed to differ on
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheModelsStillDifferWhereTheyShould()
    {
        // Guard against over-sharing: pulling the common members up must not have flattened the
        // models into one another. Identity and scrollback depth are genuinely per-model.
        var tdv1200 = new TDV1200Emulator(80, 24);
        var tdv2215 = new TDV2215Emulator(80, 24);
        var tdv2200 = new TDV2200Emulator(80, 24);

        Assert.Equal("TDV1200", tdv1200.GetTerminalType());
        Assert.Equal("TDV2215", tdv2215.GetTerminalType());
        Assert.StartsWith("TDV2200", tdv2200.GetTerminalType());

        Assert.Equal(1500, tdv1200.MaxScrollback);
        Assert.Equal(10000, tdv2215.MaxScrollback);
        Assert.Equal(10000, tdv2200.MaxScrollback);
    }

    [Fact]
    public void TheFactoryBuildsEachModelWithItsOwnProfile()
    {
        Assert.Equal("TDV1200", EmulatorFactory.CreateEmulator("TDV1200", 80, 24).Profile.Name);
        Assert.Equal("TDV2215", EmulatorFactory.CreateEmulator("TDV2215", 80, 24).Profile.Name);
        Assert.Equal("TDV2200", EmulatorFactory.CreateEmulator("TDV2200", 80, 24).Profile.Name);
    }
}
