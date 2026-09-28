using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using RetroTerm.Core.Configuration;
using RetroTerm.Desktop.Views;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The Save button in Manage Connections has to LOOK different once there is something to save.
/// </summary>
/// <remarks>
/// <para><b>What was wrong, 31 August 2026</b></para>
/// Save carried SecondaryButtonBrush inline - the same dark grey as the Cancel button beside it -
/// so becoming enabled changed almost nothing on screen. Avalonia's stock disabled state only dims
/// a control slightly, and Ronny's words on the first attempt were that the change was "too
/// little". There was no way to tell a connection had unsaved edits.
/// <para><b>Why this test renders instead of reading properties</b></para>
/// Asking the button for its Background would have passed all along: the inline value was set and
/// readable, it simply was not what got painted, and a style that fails to apply looks exactly like
/// one that works. These tests read the PIXELS the two states actually produce and require them to
/// be far apart, which is the only claim worth making here.
/// </remarks>
[Collection("Avalonia")]
public class SaveButtonVisibilityTests
{
    /// <summary>
    /// How far apart the enabled and disabled buttons must be, summed over R, G and B.
    /// </summary>
    /// <remarks>
    /// 150 is a deliberately blunt number. Avalonia's stock disabled state moves a colour by a few
    /// units, which is what "too little" looked like; the green-against-flat-grey pair this style
    /// produces moves it by several hundred. Anything in between means somebody has quietly put an
    /// inline brush back on the button.
    /// </remarks>
    private const int MinimumChannelDistance = 150;

    [AvaloniaFact]
    public void TheSaveButtonLooksVeryDifferentOnceThereIsSomethingToSave()
    {
        var manager = NewConfigManager(out var path);
        try
        {
            var window = new ManageConnectionsWindow(manager);
            window.Show();
            var save = FindSaveButton(window);
            Assert.NotNull(save);

            // Disabled is the state a freshly opened dialog is in: nothing edited yet.
            save!.IsEnabled = false;
            var quiet = RenderButton(window, save, "save-button-clean");

            save.IsEnabled = true;
            var loud = RenderButton(window, save, "save-button-dirty");

            int distance = Math.Abs(quiet.Red - loud.Red)
                + Math.Abs(quiet.Green - loud.Green)
                + Math.Abs(quiet.Blue - loud.Blue);

            Assert.True(distance >= MinimumChannelDistance,
                $"Save looks nearly the same enabled and disabled - "
                + $"clean {Describe(quiet)}, dirty {Describe(loud)}, distance {distance}. "
                + "An inline Background on the button would do this.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void TheEnabledSaveButtonIsGreenRatherThanGrey()
    {
        // Green specifically, because Connect two buttons away is already the accent blue and
        // Cancel is grey. Three buttons in a row need three different answers.
        var manager = NewConfigManager(out var path);
        try
        {
            var window = new ManageConnectionsWindow(manager);
            window.Show();
            var save = FindSaveButton(window);
            Assert.NotNull(save);

            save!.IsEnabled = true;
            var colour = RenderButton(window, save, "save-button-green");

            Assert.True(colour.Green > colour.Red + 40,
                $"expected a green Save button, got {Describe(colour)}");
            Assert.True(colour.Green > colour.Blue + 40,
                $"expected a green Save button, got {Describe(colour)}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void TheSaveButtonCarriesNoInlineBrushes()
    {
        // The trap this whole style fell into: an inline value beats a style selector, so a
        // Background set in the axaml would silently make the two states identical again. Read
        // from the axaml itself - the rendered tests above would catch it, but this says WHY.
        var axaml = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(SaveButtonVisibilityTests).Assembly.Location) ?? "",
            "..", "..", "..", "..", "..",
            "src", "RetroTerm.Desktop", "Views", "ManageConnectionsWindow.axaml"));

        if (!File.Exists(axaml)) return;   // not run from a checkout

        var text = File.ReadAllText(axaml);
        int saveAt = text.IndexOf("Name=\"SaveBtn\"", StringComparison.Ordinal);
        Assert.True(saveAt > 0, "SaveBtn not found in the axaml");

        int end = text.IndexOf("/>", saveAt, StringComparison.Ordinal);
        Assert.True(end > saveAt, "could not find the end of the SaveBtn element");

        var element = text.Substring(saveAt, end - saveAt);
        Assert.DoesNotContain("Background=", element);
        Assert.DoesNotContain("Foreground=", element);
        Assert.Contains("SaveAction", element);
    }

    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the shown window and reports the colour at the middle of the button.
    /// </summary>
    /// <remarks>
    /// Uses the project's own <c>RenderedScreenshot.CaptureWindow</c>, which goes through
    /// Avalonia's headless CaptureRenderedFrame. A plain RenderTargetBitmap over a window that has
    /// not been shown returns a black frame, and the first version of this test did exactly that -
    /// it reported #000000 for both states and would have "passed" the moment the threshold was
    /// loosened, while measuring nothing at all.
    /// </remarks>
    private static SKColor RenderButton(Window window, Button button, string name)
    {
        window.UpdateLayout();

        var shot = RenderedScreenshot.CaptureWindow(window, saveAs: name, saveZoom: 1);
        Assert.NotNull(shot);

        // The middle of the button, in window coordinates.
        var origin = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.True(origin.HasValue, "the Save button is not in the window's visual tree");

        int x = Math.Clamp((int)Math.Round(origin!.Value.X), 0, shot!.Width - 1);
        int y = Math.Clamp((int)Math.Round(origin.Value.Y), 0, shot.Height - 1);

        return shot.PixelAt(x, y);
    }

    private static string Describe(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

    private static Button? FindSaveButton(Window window)
    {
        window.Measure(new Size(850, 620));
        window.Arrange(new Rect(0, 0, 850, 620));
        return window.FindControl<Button>("SaveBtn");
    }

    private static ConfigurationManager NewConfigManager(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "RetroTermSaveBtn-" + Guid.NewGuid().ToString("N") + ".json");
        return new ConfigurationManager(path);
    }
}
