using RetroTerm.Core;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The welcome screen a new tab shows tells the truth about this build and this program.
/// </summary>
/// <remarks>
/// Ronny, 28 September 2026, on seeing it: it named four terminals of the fourteen, promised
/// "TN3270 coming soon" for a terminal that is not started, and printed "1.0.0-alpha (Phase 4 -
/// Canvas Rendering)" and "2025" from strings typed into the source, while the assembly said
/// 1.10.26.9. These tests hold the text to the build stamp and to the factory's real list.
/// </remarks>
public class WelcomeScreenTests
{
    [Fact]
    public void TheVersionIsTheAssemblysNotATypedString()
    {
        var text = MainWindow.WelcomeText();
        Assert.Contains("RetroTerm " + BuildIdentity.Version, text);
        Assert.DoesNotContain("alpha", text);
        Assert.DoesNotContain("Phase", text);
    }

    [Fact]
    public void TheCopyrightAndBuildComeFromTheStamp()
    {
        var text = MainWindow.WelcomeText();
        Assert.Contains(BuildIdentity.Copyright, text);
        Assert.Contains(BuildIdentity.BuildDate, text);
        Assert.Contains(BuildIdentity.Commit, text);
        Assert.DoesNotContain("2025 RetroCore Labs", text);
    }

    [Fact]
    public void EverySelectableTerminalIsNamedAndTheTdv2200ComesFirst()
    {
        var text = MainWindow.WelcomeText();
        var types = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < types.Length; i++)
        {
            // The screen writes the names the way people say them; the factory's keys are upper
            // case. TEK4014 is "Tektronix 4014" on screen, XTERM-256COLOR is "xterm-256color".
            string shown = types[i] switch
            {
                "TEK4014" => "Tektronix 4014",
                "XTERM" => "xterm",
                "XTERM-256COLOR" => "xterm-256color",
                _ => types[i]
            };
            Assert.Contains(shown, text);
        }

        Assert.True(text.IndexOf("TDV2200") < text.IndexOf("VT100"),
            "the TDV2200 is the terminal this program exists for and is named before the VT100");
    }

    [Fact]
    public void NothingIsPromisedThatIsNotBuilt()
    {
        var text = MainWindow.WelcomeText();
        Assert.DoesNotContain("3270", text);
        Assert.DoesNotContain("coming soon", text);
    }
}
