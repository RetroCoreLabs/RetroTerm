using System;
using System.Reflection;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Rendering;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The SGR blink CELL attribute — SGR 5 (slow) and SGR 6 (rapid) — actually blinking.
///
/// WHAT WAS WRONG. Three separate halves of one feature were missing:
///
///  1. SGR 6 had no case in the emulator's SGR switch at all, so the RapidBlink flag that
///     CharacterAttributes has always carried could never be set by anything. A host asking
///     for rapid blink got no blink whatsoever.
///  2. SGR 25 ("not blinking") cleared only Blink, so it could not switch rapid blink off.
///  3. TerminalRenderer read NEITHER flag. Cells the emulator correctly marked as blinking —
///     via SGR 5 or via the TDV rectangle operations — rendered permanently steady.
///
/// Cursor blink was already handled (appendix 0t) and is a DIFFERENT thing on a different
/// phase: a cursor flashing in lockstep with the text under it is how no real terminal
/// behaved.
///
/// These tests drive the production renderer and read real pixels, because "did the glyph
/// disappear" is a question about what was drawn, not about what a flag says.
/// </summary>
[Collection("Avalonia")]
public class BlinkAttributeRenderingTests
{
    // ─────────────────────────────────────────────────────────────
    // The emulator half
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Sgr5_SetsSlowBlink()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[5mA");

        Assert.True(CellAt(emulator, 0, 0).Attributes.HasAttribute(CharacterAttributes.Blink));
        Assert.False(CellAt(emulator, 0, 0).Attributes.HasAttribute(CharacterAttributes.RapidBlink));
    }

    [AvaloniaFact]
    public void Sgr6_SetsRapidBlink()
    {
        // This is the case that did not exist. Before the fix nothing anywhere set RapidBlink.
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[6mA");

        Assert.True(CellAt(emulator, 0, 0).Attributes.HasAttribute(CharacterAttributes.RapidBlink));
    }

    [AvaloniaFact]
    public void Sgr25_ClearsBothBlinkRates()
    {
        // ECMA-48 SGR 25 is "steady (not blinking)" — it ends both rates, not just the slow
        // one. Clearing only Blink left a rapid-blinking run with no way to stop.
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[5m\u001b[6m\u001b[25mA");

        var attributes = CellAt(emulator, 0, 0).Attributes;
        Assert.False(attributes.HasAttribute(CharacterAttributes.Blink));
        Assert.False(attributes.HasAttribute(CharacterAttributes.RapidBlink));
    }

    // ─────────────────────────────────────────────────────────────
    // The renderer half — real pixels
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void BlinkingText_IsDrawnInTheOnPhase()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[5mAAAA");

        using var shot = RenderedScreenshot.Capture(emulator, "blink-slow-on",
            configureRenderer: r => SetBlinkPhases(r, slowOn: true, rapidOn: true));

        Assert.True(shot.CellHasInk(0, 0), "a blinking cell in its ON phase must still draw its glyph");
    }

    [AvaloniaFact]
    public void BlinkingText_DisappearsInTheOffPhase()
    {
        // THE test. Before the fix this cell drew identically in both phases.
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[5mAAAA");

        using var shot = RenderedScreenshot.Capture(emulator, "blink-slow-off",
            configureRenderer: r => SetBlinkPhases(r, slowOn: false, rapidOn: true));

        Assert.False(shot.CellHasInk(0, 0), "a blinking cell in its OFF phase must not draw its glyph");
    }

    [AvaloniaFact]
    public void NonBlinkingText_IsUnaffectedByThePhase()
    {
        // The guard against overreach: turning both phases off must not blank the screen.
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "AAAA");

        using var shot = RenderedScreenshot.Capture(emulator, "blink-steady-text",
            configureRenderer: r => SetBlinkPhases(r, slowOn: false, rapidOn: false));

        Assert.True(shot.CellHasInk(0, 0), "ordinary text must never be affected by the blink phase");
    }

    [AvaloniaFact]
    public void TheTwoRatesAreIndependent()
    {
        // A slow-blinking cell must not follow the rapid phase, and the reverse. If both read
        // one flag the feature looks fine until a screen carries both.
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[5mS\u001b[0m\u001b[6mR");

        using var shot = RenderedScreenshot.Capture(emulator, "blink-rates-independent",
            configureRenderer: r => SetBlinkPhases(r, slowOn: true, rapidOn: false));

        Assert.True(shot.CellHasInk(0, 0), "slow-blinking cell should be ON");
        Assert.False(shot.CellHasInk(0, 1), "rapid-blinking cell should be OFF");
    }

    [AvaloniaFact]
    public void ABlinkedOffCellKeepsItsBackground()
    {
        // Blink removes the GLYPH, not the cell. A reverse-video blinking cell that lost its
        // background too would flash a hole in the line rather than a blinking character.
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[5m\u001b[41mAAAA");

        using var onShot = RenderedScreenshot.Capture(emulator, null,
            configureRenderer: r => SetBlinkPhases(r, slowOn: true, rapidOn: true));
        var background = onShot.DominantColorInCell(0, 0);

        using var offShot = RenderedScreenshot.Capture(emulator, "blink-keeps-background",
            configureRenderer: r => SetBlinkPhases(r, slowOn: false, rapidOn: true));

        Assert.True(
            RenderedScreenshot.ApproximatelyEqual(background, offShot.DominantColorInCell(0, 0), 8),
            "a blinked-off cell must keep its background colour");
    }

    // ─────────────────────────────────────────────────────────────
    // Repaint economy — the same rule the cursor blink follows
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AScreenWithBlinkingText_AsksForRepaints()
    {
        var emulator = new VT100Emulator(20, 3);
        Feed(emulator, "\u001b[5mAAAA");

        // Render once so the renderer observes the frame and records what it saw.
        using (RenderedScreenshot.Capture(emulator)) { }

        var renderer = new TerminalRenderer(emulator);
        try
        {
            SetPrivate(renderer, "_frameHasBlinkingText", true);
            int raised = 0;
            renderer.BlinkStateChanged += () => raised++;

            InvokeBlinkTick(renderer);
            InvokeBlinkTick(renderer);

            Assert.Equal(2, raised);
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void AScreenWithNothingBlinking_CostsNoRepaints()
    {
        // Both the cursor flag and the text flag are false: the phases still flip, but asking
        // for a full repaint twice a second per tab would buy nothing.
        var renderer = new TerminalRenderer(new VT100Emulator(20, 3));
        try
        {
            SetPrivate(renderer, "_cursorIsBlinking", false);
            SetPrivate(renderer, "_frameHasBlinkingText", false);

            int raised = 0;
            renderer.BlinkStateChanged += () => raised++;

            InvokeBlinkTick(renderer);
            InvokeBlinkTick(renderer);

            Assert.Equal(0, raised);
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void TheRapidPhaseFlipsTwiceAsOftenAsTheSlowOne()
    {
        // 500 ms per tick: rapid every tick, slow every second tick — 120 and 60 flashes per
        // minute. Driving the callback rather than waiting on a wall clock, because a test
        // that sleeps is a test that goes flaky.
        var renderer = new TerminalRenderer(new VT100Emulator(20, 3));
        try
        {
            SetBlinkPhases(renderer, slowOn: true, rapidOn: true);
            SetPrivate(renderer, "_blinkTickCount", 0);

            InvokeBlinkTick(renderer);
            Assert.False(GetPrivateBool(renderer, "_rapidBlinkOn"), "rapid should flip on tick 1");
            Assert.True(GetPrivateBool(renderer, "_slowBlinkOn"), "slow should NOT flip on tick 1");

            InvokeBlinkTick(renderer);
            Assert.True(GetPrivateBool(renderer, "_rapidBlinkOn"), "rapid should flip back on tick 2");
            Assert.False(GetPrivateBool(renderer, "_slowBlinkOn"), "slow should flip on tick 2");
        }
        finally
        {
            renderer.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static void Feed(TerminalEmulatorBase emulator, string text)
    {
        emulator.ProcessData(Encoding.UTF8.GetBytes(text));
    }

    private static TerminalCell CellAt(TerminalEmulatorBase emulator, int row, int col)
    {
        Assert.True(emulator.GetBuffer().TryGetCell(row, col, out var cell), $"no cell at {row},{col}");
        return cell;
    }

    private static void SetBlinkPhases(TerminalRenderer renderer, bool slowOn, bool rapidOn)
    {
        SetPrivate(renderer, "_slowBlinkOn", slowOn);
        SetPrivate(renderer, "_rapidBlinkOn", rapidOn);
    }

    private static void SetPrivate(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static bool GetPrivateBool(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (bool)field!.GetValue(target)!;
    }

    private static void InvokeBlinkTick(TerminalRenderer renderer)
    {
        var method = typeof(TerminalRenderer).GetMethod("OnCursorBlinkElapsed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(renderer, new object?[] { null, null });
    }
}
