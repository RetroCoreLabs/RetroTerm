using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RetroTerm.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Renders the whole OPCOM window to a PNG so the styling can be LOOKED AT.
///
/// This repo's rule, learned the hard way: an assertion only catches what it was told to
/// expect, and the interesting defects are the ones nobody thought to expect. Moving the
/// window's 23 buttons from hand-set brushes to the theme's Secondary and Primary classes
/// changes their border and padding, and no assertion about class names would notice a
/// toolbar that had wrapped or a button that had grown into its neighbour.
///
/// The picture lands in tests\RetroTerm.Tests\Avalonia\images\rendered\.
/// </summary>
[Collection("Avalonia")]
public class OpcomWindowRenderTests
{
    private readonly ITestOutputHelper _output;

    public OpcomWindowRenderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AvaloniaFact]
    public void TheWholeWindowRendersAndIsSaved()
    {
        var window = new OpcomDebugWindow();
        window.Show();

        var shot = RenderedScreenshot.CaptureWindow(window, "opcom-window-styled");
        Assert.NotNull(shot);
        using (shot!)
        {
            _output.WriteLine($"OPCOM window {shot.Width}x{shot.Height} saved to {shot.SavedPath}");
            Assert.True(shot.Width > 400 && shot.Height > 300,
                $"the window rendered at {shot.Width}x{shot.Height}, which is too small to be the real layout");
        }
    }

    /// <summary>
    /// The toolbar buttons must still fit on one row after taking the theme's border and
    /// padding. If the row wraps or overflows, the window has got wider than its own
    /// minimum and the compact metrics need revisiting.
    /// </summary>
    [AvaloniaFact]
    public void TheToolbarButtonsStillFitOnOneRow()
    {
        var window = new OpcomDebugWindow();
        window.Width = 920;   // the declared default width
        window.Show();
        window.Measure(new global::Avalonia.Size(920, 720));
        window.Arrange(new global::Avalonia.Rect(0, 0, 920, 720));

        string[] toolbar = { "StopButton", "MclButton", "StepButton", "StartButton", "BreakpointButton", "EscButton" };
        double total = 0;
        double widest = 0;

        for (int i = 0; i < toolbar.Length; i++)
        {
            var b = window.FindControl<Button>(toolbar[i]);
            Assert.True(b != null, "the toolbar has no button called " + toolbar[i]);
            Assert.True(b!.Bounds.Height > 0 && b.Bounds.Width > 0,
                toolbar[i] + " rendered with no size");

            total += b.Bounds.Width;
            if (b.Bounds.Width > widest) widest = b.Bounds.Width;
        }

        // The row these six sit in also carries the step count box, the address box and
        // the pass-through checkbox, so the buttons alone must stay well inside the
        // window. Taking the theme's border and padding must not have doubled them.
        _output.WriteLine($"six toolbar buttons total {total:F1}px, widest {widest:F1}px");
        Assert.True(total < 520,
            $"the six toolbar buttons now need {total:F0}px, which crowds out the boxes beside them"
            + " - the theme's padding is too generous for this toolbar");
    }
}
