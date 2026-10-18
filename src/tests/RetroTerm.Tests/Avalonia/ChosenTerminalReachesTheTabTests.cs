using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.ViewModels;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Picking a terminal has to produce that terminal.
///
/// The dropdown being right is only half of it. This walks the same path the program does when
/// somebody saves a connection and presses Connect - dropdown value into the profile, profile into
/// the saved configuration, configuration back out into connection parameters, parameters into the
/// factory - and asks whether a VT420 comes out the other end.
///
/// It is deliberately end to end rather than a unit test of any one step: every step here was
/// individually correct while the feature as a whole was unreachable, because nothing walked the
/// whole path.
/// </summary>
public class ChosenTerminalReachesTheTabTests
{
    /// <summary>
    /// Runs one terminal name through the whole save-and-connect path.
    /// </summary>
    private static TerminalEmulatorBase BuildThroughTheRealPath(string chosen)
    {
        // What the dialog does when the user picks from the dropdown.
        var profile = new ConnectionProfileViewModel();
        profile.Name = "test";
        profile.Host = "localhost";
        profile.EmulatorType = chosen;
        profile.Size = "80x24";

        // What Save does.
        var config = profile.ToHostConfiguration();

        // What re-opening the saved connection does.
        var reloaded = new ConnectionProfileViewModel();
        reloaded.LoadFromConfiguration(config);

        // What Connect does.
        var parameters = reloaded.ToConnectionParameters();
        return EmulatorFactory.CreateEmulator(parameters.EmulatorType, parameters.Width, parameters.Height, 100);
    }

    [Theory]
    [InlineData("VT52")]
    [InlineData("VT100")]
    [InlineData("VT102")]
    [InlineData("VT220")]
    [InlineData("VT240")]
    [InlineData("VT320")]
    [InlineData("VT340")]
    [InlineData("VT420")]
    [InlineData("XTERM")]
    [InlineData("XTERM-256COLOR")]
    [InlineData("TDV1200")]
    [InlineData("TDV2215")]
    [InlineData("TDV2200")]
    [InlineData("TEK4014")]
    public void EveryTerminalSurvivesBeingSavedAndConnected(string chosen)
    {
        var emulator = BuildThroughTheRealPath(chosen);

        Assert.NotNull(emulator);
        Assert.Equal(chosen, emulator.Profile.Name, ignoreCase: true);
    }

    [Fact]
    public void AndTheListInTheFactoryIsTheListThisTestCovers()
    {
        // Stops a new terminal being added to the factory without anyone checking it survives the
        // trip - the test above would still pass while quietly not covering it.
        var known = new[]
        {
            "VT52", "VT100", "VT102", "VT220", "VT240", "VT320", "VT340", "VT420",
            "XTERM", "XTERM-256COLOR", "TDV1200", "TDV2215", "TDV2200", "TEK4014",
        };

        var available = EmulatorFactory.AvailableEmulators;
        Assert.Equal(known.Length, available.Length);

        for (int i = 0; i < available.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < known.Length; j++)
            {
                if (string.Equals(known[j], available[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, available[i] + " is offered but not covered by the round-trip test above");
        }
    }
}
