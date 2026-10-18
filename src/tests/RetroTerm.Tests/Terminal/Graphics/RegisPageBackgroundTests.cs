using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal.Graphics;

/// <summary>
/// ReGIS output map location 0 is the page background.
/// </summary>
/// <remarks>
/// <para><b>How this was found</b></para>
/// By opening `compare-cat-original.png` on 21 August 2026 and seeing a black background where the
/// hardware capture has dark teal-green. Every assertion covering that sheet passed, because what
/// they check is that both halves carry ink - which was true. A background being entirely the wrong
/// colour is not something "there is ink" can see.
/// <para><b>Why location 0 and not the surface</b></para>
/// Every pixel nothing has drawn holds code 0, so what location 0 looks like is what the page looks
/// like. It is turned into the terminal's default background rather than painted into the surface,
/// because the surface deliberately leaves untouched pixels untouched - hackerb9's bitplane capture
/// proved that one, where a plane-masked write over fresh screen must recolour text without laying
/// an opaque bar over it.
/// <para><b>Why nothing else in the corpus can be disturbed</b></para>
/// `cat-original.six` is the ONLY fixture of the sixteen that writes the colour map through ReGIS
/// at all. That was checked before the change was written, not after - the previous attempt at this
/// defect improved the target fixture four hundredfold and quietly wrecked `cat-vt240`, and only
/// measuring the fixtures it was not aimed at caught it.
/// </remarks>
public class RegisPageBackgroundTests
{
    /// <summary>
    /// The escape character, built from its code so no raw control byte lands in this file.
    /// </summary>
    private const char Escape = (char)0x1B;

    /// <summary>
    /// Sends one ReGIS command string.
    /// </summary>
    /// <param name="emulator">
    /// The terminal.
    /// </param>
    /// <param name="commands">
    /// The ReGIS commands, without the introducer or terminator.
    /// </param>
    private static void Regis(TerminalEmulatorBase emulator, string commands)
        => emulator.ProcessData(Encoding.ASCII.GetBytes(
            Escape + "Pp" + commands + Escape + "\\"));

    /// <summary>
    /// A VT340, which is the profile whose image colours reach the text colours.
    /// </summary>
    /// <returns>
    /// The emulator.
    /// </returns>
    private static TerminalEmulatorBase NewVt340()
        => EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

    [Fact]
    public void WritingMapLocationZeroSetsThePageBackground()
    {
        // THE DEFECT. cat-original.six opens with exactly this - S(M0(H280L35S60)) - and then sends
        // a Sixel image defining no colours of its own. The hardware shows that teal-green across
        // the whole screen; we showed black.
        var emulator = NewVt340();

        Regis(emulator, "S(M0(H280L35S60))");

        Assert.True(emulator.ImageTextBackground.HasValue, "the page background never moved");

        var background = emulator.ImageTextBackground!.Value;
        Assert.True(background.G > background.R && background.G > background.B,
            "expected a green, got " + background.R + "," + background.G + "," + background.B);
    }

    [Fact]
    public void WritingAnyOtherLocationLeavesTheBackgroundAlone()
    {
        // THE BOUNDARY, and the reason this is keyed on location 0 rather than on "the map moved".
        // A drawing that sets only its own drawing colours must not drag the background with it -
        // firing on any write would override the theme with location 0's default black every time
        // a stream set M1 through M3.
        var emulator = NewVt340();

        Regis(emulator, "S(M1(H0L50S100))S(M2(H120L50S100))S(M3(H240L50S100))");

        Assert.False(emulator.ImageTextBackground.HasValue,
            "a write to locations 1 to 3 moved the background");
    }

    [Fact]
    public void TheLastWriteToLocationZeroWins()
    {
        // A stream may set it more than once, and what the page ends up as is the last value - not
        // the first one seen and kept.
        var emulator = NewVt340();

        Regis(emulator, "S(M0(H120L50S100))");
        Regis(emulator, "S(M0(H0L0S0))");

        Assert.True(emulator.ImageTextBackground.HasValue);

        var background = emulator.ImageTextBackground!.Value;
        Assert.Equal(0, background.R);
        Assert.Equal(0, background.G);
        Assert.Equal(0, background.B);
    }

    [Fact]
    public void AHardResetTakesThePageBackgroundWithIt()
    {
        // The background came from a host, and a hard reset leaves the terminal as it was switched
        // on. Without this a drawing's colour would outlive the program that drew it.
        var emulator = NewVt340();

        Regis(emulator, "S(M0(H280L35S60))");
        Assert.True(emulator.ImageTextBackground.HasValue);

        emulator.ProcessData(Encoding.ASCII.GetBytes(Escape + "c"));

        Assert.False(emulator.ImageTextBackground.HasValue,
            "RIS left the host's page background behind");
    }
}
