using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// A saved connection remembering that it is a single-phosphor screen.
///
/// WHY THIS NEEDS ITS OWN FIELD. A connection stores its colours as hex, and "Amber" and
/// "Amber (single phosphor)" are byte-for-byte identical in both foreground and background — the
/// only thing separating them is whether the sixteen ANSI colours collapse onto one hue. So the
/// flag cannot be worked out from what was saved; it has to BE saved.
///
/// Until this existed the two single-phosphor presets lasted only for the session they were picked
/// in, and a reconnect silently came back as ordinary Amber.
/// </summary>
[Collection("Avalonia")]
public class SinglePhosphorPersistenceTests
{
    [AvaloniaFact]
    public void TheTwoAmberPresetsAreIndistinguishableByColourAlone()
    {
        // The premise, asserted rather than asserted-in-a-comment. If these ever stop being equal,
        // the separate field is no longer needed and this test says so.
        (string Name, string Fg, string Bg, bool Mono)? colour = null;
        (string Name, string Fg, string Bg, bool Mono)? mono = null;

        var presets = RetroTerm.Desktop.MainWindow.TerminalColorPresets;
        for (int i = 0; i < presets.Length; i++)
        {
            if (presets[i].Name == "Amber") colour = presets[i];
            if (presets[i].Name == "Amber (single phosphor)") mono = presets[i];
        }

        Assert.NotNull(colour);
        Assert.NotNull(mono);
        Assert.Equal(colour!.Value.Fg, mono!.Value.Fg);
        Assert.Equal(colour.Value.Bg, mono.Value.Bg);
        Assert.NotEqual(colour.Value.Mono, mono.Value.Mono);
    }

    [AvaloniaFact]
    public void AConnectionSavedWithoutTheFlagIsAnOrdinaryColourScreen()
    {
        // Every connection saved before this field existed reads back with it false, which is what
        // makes an old file safe without a migration step.
        var configuration = new HostConfiguration();

        Assert.False(configuration.SinglePhosphor);
    }

    [AvaloniaFact]
    public void TheFlagSurvivesBeingCloned()
    {
        // Cloning is how a connection is duplicated in the manager. Dropping the flag there would
        // turn a copy of a single-phosphor connection into an ordinary one.
        var configuration = new HostConfiguration
        {
            Name = "ND amber",
            ForegroundColor = "#FFBF00",
            BackgroundColor = "#0A0800",
            SinglePhosphor = true,
        };

        var clone = configuration.Clone();

        Assert.True(clone.SinglePhosphor);
        Assert.Equal("#FFBF00", clone.ForegroundColor);
    }

    [AvaloniaFact]
    public void ASavedConnectionBuildsAMonochromeTheme()
    {
        // The end of the chain: what the renderer is actually handed. A colour theme declines every
        // palette index and a monochrome one themes the sixteen, so this is the difference that
        // reaches the screen.
        var parameters = new RetroTerm.Core.Protocols.ConnectionFactory.ConnectionParameters
        {
            ForegroundColor = "#FFBF00",
            BackgroundColor = "#0A0800",
            SinglePhosphor = true,
        };

        var theme = RetroTerm.Desktop.MainWindow.BuildSavedConnectionTheme(parameters);

        Assert.True(theme.IsMonochrome);
        Assert.True(theme.TryGetBaseColour(2, out _), "a monochrome theme must theme canonical green");
    }

    [AvaloniaFact]
    public void TheSameColoursWithoutTheFlagBuildAColourTheme()
    {
        // The guard against overreach: identical hex, flag off, and the canonical palette is left
        // alone. This is what every existing Amber connection must keep doing.
        var parameters = new RetroTerm.Core.Protocols.ConnectionFactory.ConnectionParameters
        {
            ForegroundColor = "#FFBF00",
            BackgroundColor = "#0A0800",
            SinglePhosphor = false,
        };

        var theme = RetroTerm.Desktop.MainWindow.BuildSavedConnectionTheme(parameters);

        Assert.False(theme.IsMonochrome);
        Assert.False(theme.TryGetBaseColour(2, out _), "a colour theme must leave the palette alone");
    }
}
