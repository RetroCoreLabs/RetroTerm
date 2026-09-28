using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// GRM, the graphic rendition mode switch - 2215 mode 62, three positions.
/// </summary>
/// <remarks>
/// <para><b>Why this one matters more than the other soft switches</b></para>
/// A real PED initialisation string sends it. The capture behind the mode work of
/// 11 September 2026 reads <c>ESC [ 62;36;66 l</c> - three numbers in one sequence, and 62 is the
/// first of them.
///
/// <para><b>Three positions on the 2215, two on the ND-1200</b></para>
/// TDV 2215 section 8.7.1 gives 62 as RM = ATTR, 1.SM = UNDERLINE, 2.SM = SGR. ND Display Terminal
/// 1200 section 5.64 lists the same number with only ATTRIBUTE and UNDERLINE. The models differ,
/// and the three-position walk is what this program keeps.
///
/// <para><b>The switch is held; the semantics are not implemented</b></para>
/// On a real 2215, ATTR makes SGR do nothing and turns SO Y SI into an invisible attribute cell
/// that occupies a screen position, UNDERLINE turns SO and SI into underline on and off, and
/// changing the switch erases the screen. None of that happens here yet - see the tests at the
/// bottom, which pin what this program does TODAY so that building the real behaviour has to
/// change them deliberately.
/// </remarks>
public class TdvGraphicRenditionModeTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Feeds a sequence, supplying the escape.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to feed.
    /// </param>
    /// <param name="sequence">
    /// The sequence after the escape.
    /// </param>
    private static void Feed(TDVEmulatorBase emulator, string sequence)
    {
        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + sequence));
    }

    /// <summary>
    /// It starts on SGR, which is what this program actually does.
    /// </summary>
    /// <remarks>
    /// The 2215's factory switch listing has GRM on ATTR. This program starts on SGR deliberately:
    /// it honours CSI Ps m and treats SO and SI as character-set shifts, so reporting ATTR would
    /// name a position the terminal does not behave like.
    /// </remarks>
    [Fact]
    public void ItStartsOnSgrBecauseThatIsWhatThisTerminalDoes()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Sgr, emulator.GraphicRenditionMode);
    }

    /// <summary>
    /// RM goes to ATTR from any position, which is what PED's initialisation sends.
    /// </summary>
    [Fact]
    public void ResetGoesToAttributeFromAnywhere()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[62l");
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Attribute, emulator.GraphicRenditionMode);

        Feed(emulator, "[62h");
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Underline, emulator.GraphicRenditionMode);

        Feed(emulator, "[62l");
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Attribute, emulator.GraphicRenditionMode);
    }

    /// <summary>
    /// Each SM advances one position and SGR is as far as it goes.
    /// </summary>
    /// <remarks>
    /// Section 8.7.1: "for switches with more than two settings, each SM increments the setting
    /// until it reaches its highest value, where it stays until RM".
    /// </remarks>
    [Fact]
    public void EachSetAdvancesOnePositionAndStopsAtSgr()
    {
        var emulator = new TDV2200Emulator(80, 24);
        Feed(emulator, "[62l");

        Feed(emulator, "[62h");
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Underline, emulator.GraphicRenditionMode);

        Feed(emulator, "[62h");
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Sgr, emulator.GraphicRenditionMode);

        Feed(emulator, "[62h");
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Sgr, emulator.GraphicRenditionMode);
    }

    /// <summary>
    /// The switch survives being the first number of a three-number list, which is how PED sends it.
    /// </summary>
    /// <remarks>
    /// <c>ESC [ 62;36;66 l</c> from a real TDV2200 termcap: GRM to ATTR, end-of-line wrap off, and
    /// the 2115 switch on, in one sequence. Every number has to land.
    /// </remarks>
    [Fact]
    public void PedsOwnThreeNumberInitialisationLandsAllThree()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[62;36;66l");

        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Attribute, emulator.GraphicRenditionMode);
        Assert.True(emulator.Is2115CompatibilityMode, "mode 66 was last in the list and was dropped");
    }

    /// <summary>
    /// A terminal reset puts it back to SGR.
    /// </summary>
    [Fact]
    public void ResetToInitialStateReturnsToSgr()
    {
        var emulator = new TDV2200Emulator(80, 24);
        Feed(emulator, "[62l");
        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Attribute, emulator.GraphicRenditionMode);

        emulator.ResetToInitialState();

        Assert.Equal(TDVEmulatorBase.TDVGraphicRenditionMode.Sgr, emulator.GraphicRenditionMode);
    }

    /// <summary>
    /// TODAY, SGR still works in the ATTR position - and on a real 2215 it would not.
    /// </summary>
    /// <remarks>
    /// THIS TEST PINS A KNOWN SHORTFALL, not a behaviour worth keeping. Section 4.2.3: "In the
    /// ATTR. state the terminal will not react to the SGR code." When that is built, this test
    /// SHOULD fail, and whoever builds it should replace it rather than work around it.
    /// </remarks>
    [Fact]
    public void SgrStillWorksInAttributeMode_WhichARealTerminalWouldNotDo()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Feed(emulator, "[62l");                       // ATTR
        Feed(emulator, "[1m");                        // bold
        emulator.ProcessData(Encoding.ASCII.GetBytes("X"));

        Assert.True((emulator.Buffer.GetCell(0, 0).Attributes & CharacterAttributes.Bold) != 0,
            "bold did not reach the cell at all - this test is about SGR being honoured, so "
            + "something else broke");
    }

    /// <summary>
    /// TODAY, changing the switch does not erase the screen - and on a real 2215 it would.
    /// </summary>
    /// <remarks>
    /// Same kind of test as the one above. Section 4.2.3: "When the GRM switch is changed, the
    /// screen will be erased." Building that will make this test fail, which is the point of
    /// writing it down.
    /// </remarks>
    [Fact]
    public void ChangingTheSwitchDoesNotEraseTheScreen_WhichARealTerminalWouldDo()
    {
        var emulator = new TDV2200Emulator(80, 24);
        emulator.ProcessData(Encoding.ASCII.GetBytes("HELLO"));

        Feed(emulator, "[62l");

        Assert.Equal((uint)'H', emulator.Buffer.GetCell(0, 0).Codepoint);
    }
}
