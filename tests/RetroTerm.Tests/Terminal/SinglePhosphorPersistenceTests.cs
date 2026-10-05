using System;
using System.Collections.Generic;
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
    public void SinglePhosphorIsASwitchNotAPresetOfItsOwn()
    {
        // It used to be two extra presets ("Amber (single phosphor)" and "Green Phosphor (single
        // phosphor)") that repeated Amber and Green Phosphor byte for byte. That made the list look
        // duplicated, and because a saved connection was matched back to a preset by colour alone,
        // a single-phosphor connection reloaded as the plain preset. Now the flag is its own
        // checkbox that applies to ANY pair, and no two presets share their colours.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in TerminalColourPresets.BuiltIn)
        {
            Assert.DoesNotContain("single phosphor", preset.Name, StringComparison.OrdinalIgnoreCase);
            Assert.True(seen.Add(preset.Foreground + "/" + preset.Background),
                $"{preset.Name} repeats the colours of another preset");
        }
    }

    [AvaloniaFact]
    public void ASavedSinglePhosphorConnectionIsMatchedBackToItsPresetWithoutLosingTheFlag()
    {
        // The colours match "Amber"; the flag is separate and stays with the connection.
        var matched = TerminalColourPresets.FindByColours("#FFBF00", "#0A0800");
        Assert.Equal("Amber", matched?.Name);

        var clone = new HostConfiguration
        {
            ForegroundColor = "#FFBF00",
            BackgroundColor = "#0A0800",
            SinglePhosphor = true,
        }.Clone();
        Assert.True(clone.SinglePhosphor);
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
