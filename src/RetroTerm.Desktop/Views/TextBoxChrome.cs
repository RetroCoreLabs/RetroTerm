using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Pins a TextBox's colors so they do NOT flip when the box gains or loses focus.
///
/// Why this exists: Fluent's pseudo-class styles (:pointerover, :focus) restyle the
/// TextBox's TEMPLATE border directly (PART_BorderElement), so a locally-set or
/// theme-bound Background loses the fight the moment the box is focused or hovered —
/// the text area visibly changes color when "selected"/"unselected". This helper adds
/// per-instance styles for those states that re-assert the themed brushes, giving one
/// stable look in every state.
/// </summary>
public static class TextBoxChrome
{
    private static readonly string[] States = { ":pointerover", ":focus", ":focus-within" };

    /// <summary>
    /// Locks the box's template chrome to the given theme brushes in every
    /// interaction state. Brushes come from the window's resource observables, so
    /// live theme switches still restyle.
    /// </summary>
    public static void Freeze(TextBox box, Window themeSource, string backgroundKey, string borderKey = "BorderBrush")
    {
        for (int i = 0; i < States.Length; i++)
        {
            // NOTE: x.Nesting() is only legal INSIDE another style — at the top level
            // of a control's Styles collection it throws when the style attaches
            // (this crashed the script editor window on open, 2026-08-05). A style
            // in box.Styles applies to the box and its descendants, so OfType<TextBox>
            // targets the box itself.
            var style = new Style(x => x.OfType<TextBox>().Class(States[i]).Template().OfType<Border>().Name("PART_BorderElement"));
            style.Setters.Add(new Setter(Border.BackgroundProperty, themeSource.GetResourceObservable(backgroundKey).ToBinding()));
            style.Setters.Add(new Setter(Border.BorderBrushProperty, themeSource.GetResourceObservable(borderKey).ToBinding()));
            box.Styles.Add(style);
        }
    }
}
