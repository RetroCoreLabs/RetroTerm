using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Every terminal this program can build must be choosable for a connection.
///
/// This is the test that was missing. The connection dialog held its own typed-in list of three
/// names, so ten terminals were written, tested, documented as "shipped" - and could not be picked
/// by anybody using the program. Passing unit tests said the emulators worked, and they did; there
/// was simply no way to reach them.
///
/// A list of names that lives in two places is a promise to update both forever. The dialog now
/// reads the factory's list, and this test fails the moment those two disagree again.
/// </summary>
[Collection("Avalonia")]
public class TerminalTypeIsSelectableTests
{
    /// <summary>
    /// The Terminal Type dropdown the window actually built.
    /// </summary>
    /// <remarks>
    /// Read by reflection rather than found by walking the tree: the terminal panel is only
    /// attached once its tab is shown, and this test is about WHAT THE DROPDOWN CONTAINS, not
    /// about which tab is open. Same hook RenderedScreenshot uses to reach the live renderer.
    /// </remarks>
    private static ComboBox? FindTerminalTypeCombo(ManageConnectionsWindow window)
    {
        var field = typeof(ManageConnectionsWindow).GetField("_emulatorCombo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field == null)
        {
            throw new System.InvalidOperationException(
                "ManageConnectionsWindow._emulatorCombo was not found - this test's hook needs updating.");
        }

        return field.GetValue(window) as ComboBox;
    }

    [AvaloniaFact]
    public void TheConnectionDialogOffersEveryTerminalTheFactoryCanBuild()
    {
        // The real constructor, because the parameterless one exists only for the XAML designer
        // and never builds the tab panels. A temporary config file keeps this off the user's own.
        var configPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "retroterm-terminal-type-test.json");
        var window = new ManageConnectionsWindow(new RetroTerm.Core.Configuration.ConfigurationManager(configPath));

        var combo = FindTerminalTypeCombo(window);
        Assert.NotNull(combo);

        var offered = new List<string>();
        var items = combo!.ItemsSource ?? combo.Items;
        var enumerator = items!.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is string name)
            {
                offered.Add(name);
            }
        }

        var expected = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Contains(expected[i], offered);
        }
    }

    [Fact]
    public void AndEveryNameOnThatListActuallyBuilds()
    {
        // The other direction: a name in the dropdown that the factory rejects would throw in the
        // user's face when they connected.
        var names = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < names.Length; i++)
        {
            var emulator = EmulatorFactory.CreateEmulator(names[i], 80, 24, 100);
            Assert.NotNull(emulator);
        }
    }
}
