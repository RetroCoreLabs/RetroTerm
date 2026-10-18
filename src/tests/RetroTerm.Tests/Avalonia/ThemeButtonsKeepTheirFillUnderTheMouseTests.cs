using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Every filled button class in the app's two stylesheets keeps its fill with the pointer over
/// it.
/// </summary>
/// <remarks>
/// <para><b>Why a button can vanish under the mouse</b></para>
/// The Fluent Button template paints its fill on PART_ContentPresenter, and Fluent's own
/// :pointerover style sets that presenter's Background to its stock hover resource. A class
/// that only sets Background on the Button, or only restyles the Button on :pointerover, never
/// reaches the presenter - so under the mouse the stock hover fill wins, and on a dark window
/// that fill is close enough to the background that the button seems to disappear. Ronny hit it
/// on the Close RetroTerm dialog on 27 September 2026; the audit that followed found the same
/// hole in seven classes of DarkTheme.axaml, used by 57 buttons. FluentTheme.axaml's dialog
/// classes had the fix all along and say so in a comment; this test makes the rule hold for
/// every class rather than trusting each one to remember.
/// <para><b>What is measured</b></para>
/// One button per class in a themed window, sampled six pixels in from its left edge at mid
/// height, at rest and with :pointerover set. The hovered fill may be a shade darker or
/// lighter (a few classes dim through Opacity), but it must stay the same colour: no channel
/// may move by more than 64.
/// </remarks>
[Collection("Avalonia")]
public class ThemeButtonsKeepTheirFillUnderTheMouseTests
{
    private static async Task Pump()
    {
        for (int i = 0; i < 8; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(15);
        }
    }

    private static SKColor FillOf(Button button, Window window, RenderedScreenshot shot)
    {
        var point = button.TranslatePoint(new Point(6, button.Bounds.Height / 2), window);
        Assert.True(point.HasValue, "the button must be laid out inside the window");
        return shot.PixelAt((int)point!.Value.X, (int)point.Value.Y);
    }

    private static int Distance(SKColor a, SKColor b)
    {
        int r = Math.Abs(a.Red - b.Red);
        int g = Math.Abs(a.Green - b.Green);
        int bl = Math.Abs(a.Blue - b.Blue);
        return Math.Max(r, Math.Max(g, bl));
    }

    [AvaloniaTheory]
    [InlineData("Primary")]
    [InlineData("Secondary")]
    [InlineData("Danger")]
    [InlineData("AddAction")]
    // SaveAction is not here: its rules turn the green fill accent BLUE on hover on purpose
    // (DarkTheme.axaml, Button.SaveAction:pointerover). It stays a filled button, so it does not
    // have the defect; it just changes colour. Covered by ASaveActionStaysFilledUnderThePointer.
    [InlineData("dialog-primary")]
    [InlineData("dialog-secondary")]
    [InlineData("dialog-danger")]
    public async Task AFilledButtonKeepsItsColourWithThePointerOverIt(string buttonClass)
    {
        var window = new Window { Width = 240, Height = 120, CanResize = false };
        window[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");
        var button = new Button
        {
            Content = "Sample",
            Classes = { buttonClass },
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            MinWidth = 120
        };
        window.Content = button;
        window.Show();
        await Pump();

        var rest = RenderedScreenshot.CaptureWindow(window, "button-" + buttonClass);
        Assert.NotNull(rest);
        var background = rest!.PixelAt(4, 4);
        var restFill = FillOf(button, window, rest);
        // Only a sanity check that the sample landed on the button: a neutral class such as
        // dialog-secondary sits deliberately close to the window (#252526 on #1E1E1E measured).
        Assert.True(Distance(restFill, background) > 4,
            $"{buttonClass} at rest must differ from the window ({restFill} on {background})");

        ((IPseudoClasses)button.Classes).Set(":pointerover", true);
        await Pump();
        var hover = RenderedScreenshot.CaptureWindow(window, "button-" + buttonClass + "-hover");
        Assert.NotNull(hover);
        var hoverFill = FillOf(button, window, hover!);

        // 64: Primary hovers from #0A84FF to its AccentHoverBrush #3D9BFF (distance 51, measured),
        // while the stock grey it used to fall to is 203 away. The defect cannot hide inside this.
        Assert.True(Distance(hoverFill, restFill) <= 64,
            $"{buttonClass} lost its fill under the pointer: rest {restFill}, hover {hoverFill}");

        ((IPseudoClasses)button.Classes).Set(":pointerover", false);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ASaveActionStaysFilledUnderThePointer()
    {
        // Green at rest, accent blue under the pointer, by its own rules - but never the stock
        // grey, and never close to the window.
        var window = new Window { Width = 240, Height = 120, CanResize = false };
        window[!Window.BackgroundProperty] = new DynamicResourceExtension("WindowBackgroundBrush");
        var button = new Button
        {
            Content = "Save",
            Classes = { "SaveAction" },
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            MinWidth = 120
        };
        window.Content = button;
        window.Show();
        await Pump();

        var rest = RenderedScreenshot.CaptureWindow(window, "button-SaveAction");
        Assert.NotNull(rest);
        var background = rest!.PixelAt(4, 4);

        ((IPseudoClasses)button.Classes).Set(":pointerover", true);
        await Pump();
        var hover = RenderedScreenshot.CaptureWindow(window, "button-SaveAction-hover");
        Assert.NotNull(hover);
        var hoverFill = FillOf(button, window, hover!);

        Assert.True(Distance(hoverFill, background) > 48,
            $"SaveAction must still be a filled button under the pointer: {hoverFill} on {background}");
        Assert.True(hoverFill.Blue > hoverFill.Red + 40,
            $"SaveAction hovers to the accent blue by its own rules, was {hoverFill}");

        ((IPseudoClasses)button.Classes).Set(":pointerover", false);
        window.Close();
    }
}
