using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The OPCOM window must use the theme's button classes rather than setting brushes on
/// each button by hand.
///
/// Ronny asked on 8 September 2026 whether there is a standard for buttons and tabs, and
/// whether this window follows it. There is one - DarkTheme.axaml defines Button.Primary,
/// Button.Secondary and Button.Danger - and this window did not use it: 29 buttons, none
/// carrying a style class, and 34 brushes set inline.
///
/// A LOCALLY SET brush WINS over a style, which is the trap already recorded in this repo
/// for ComboBox. Setting only the background by hand gets the colour roughly right while
/// silently dropping the border, the hover colour and the hand cursor the style also
/// carries, so the button looks nearly right and stops following the theme.
/// </summary>
[Collection("Avalonia")]
public class OpcomButtonStyleTests
{
    private readonly ITestOutputHelper _output;

    public OpcomButtonStyleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Every push button in the window. Checkboxes are excluded: in Avalonia a CheckBox
    /// IS a Button by inheritance, but it is not a push button and the Primary/Secondary
    /// classes are not meant for it.
    /// </summary>
    private static List<Button> PushButtonsOf(Window window)
    {
        var found = new List<Button>();
        foreach (var child in window.GetLogicalDescendants())
        {
            if (child is ToggleButton) continue;
            if (child is Button b) found.Add(b);
        }
        return found;
    }

    /// <summary>
    /// True when a button carries one of the theme's button classes. The tab buttons have
    /// their own dedicated class, Button.OpcomTab, and count as styled.
    /// </summary>
    private static bool IsStyled(Button b)
    {
        foreach (string c in b.Classes)
        {
            if (c == "Primary" || c == "Secondary" || c == "Danger" || c == "OpcomTab") return true;
        }
        return false;
    }

    private static string Describe(Button b) => b.Name ?? (b.Content?.ToString() ?? "(unnamed)");

    [AvaloniaFact]
    public void EveryButtonInTheWindowUsesAThemeClass()
    {
        var window = new OpcomDebugWindow();
        var buttons = PushButtonsOf(window);

        Assert.True(buttons.Count > 20,
            "expected the OPCOM window to have its full set of buttons, found " + buttons.Count);

        var bare = new List<string>();
        foreach (var b in buttons)
        {
            if (!IsStyled(b)) bare.Add(Describe(b));
        }

        _output.WriteLine($"{buttons.Count} push buttons, {bare.Count} without a theme class");
        Assert.True(bare.Count == 0,
            "these buttons set their look by hand instead of using a theme class, so they miss its"
            + " border, hover colour and cursor: " + string.Join(", ", bare));
    }

    [AvaloniaFact]
    public void NoButtonOverridesTheThemeWithALocalBrush()
    {
        // The priority is what matters, not merely whether the property has a value: a
        // styled button HAS a background, it just comes from the style. Only a value set
        // on the element itself outranks the style, and that is the thing being banned.
        var window = new OpcomDebugWindow();
        var offenders = new List<string>();

        foreach (var b in PushButtonsOf(window))
        {
            if (IsLocal(b, Button.BackgroundProperty) || IsLocal(b, Button.ForegroundProperty))
            {
                offenders.Add(Describe(b));
            }
        }

        _output.WriteLine($"{offenders.Count} buttons carry a local brush");
        Assert.True(offenders.Count == 0,
            "these buttons set a brush on the element, which outranks the theme: "
            + string.Join(", ", offenders));
    }

    private static bool IsLocal(Button b, global::Avalonia.AvaloniaProperty property)
    {
        var diagnostic = b.GetDiagnostic(property);
        return diagnostic.Priority == BindingPriority.LocalValue;
    }
}
