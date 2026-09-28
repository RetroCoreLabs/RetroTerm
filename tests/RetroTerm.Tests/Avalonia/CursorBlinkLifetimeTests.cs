using System;
using System.Reflection;
using System.Threading;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Rendering;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Phase 5 part 2: the cursor blink timer (verified live bug 8).
///
/// Two defects, one cause — nothing owned the renderer's lifetime:
///
///  1. LEAK. TerminalCanvas.SetEmulator built a new TerminalRenderer on every terminal-type change
///     and simply overwrote the field. The old renderer's 500 ms System.Timers.Timer kept running
///     for the life of the process, holding that renderer — and through it the emulator, the
///     256-brush palette and the selection manager — alive. TerminalRenderer.Dispose existed the
///     whole time and had no callers.
///
///  2. BLINK NEVER BLINKED. The timer flipped a bool and stopped there. Nothing asked for a
///     repaint, so a blinking cursor only changed state when something ELSE redrew the screen —
///     i.e. never, on an idle session, which is exactly when a user looks at the cursor.
///
/// Both are UI-lifetime behaviour, so these tests drive the real controls rather than a mock.
/// </summary>
[Collection("Avalonia")]
public class CursorBlinkLifetimeTests
{
    /// <summary>
    /// Reads a private field. Used because the whole point of these tests is lifetime — whether an
    /// object was released — which has no public surface by design.
    /// </summary>
    private static object? PrivateField(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    // ─────────────────────────────────────────────────────────────
    // The leak
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SwappingTheEmulatorReplacesTheRenderer()
    {
        var canvas = new TerminalCanvas();
        canvas.SetEmulator(new VT100Emulator(80, 24));
        var first = PrivateField(canvas, "_renderer");

        canvas.SetEmulator(new VT100Emulator(80, 24));
        var second = PrivateField(canvas, "_renderer");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [AvaloniaFact]
    public void TheReplacedRenderersTimerIsStopped()
    {
        // THE leak, stated directly: after a swap the old renderer's timer must not still be
        // running. A disposed System.Timers.Timer reports Enabled == false.
        var canvas = new TerminalCanvas();
        canvas.SetEmulator(new VT100Emulator(80, 24));
        var first = PrivateField(canvas, "_renderer");
        Assert.NotNull(first);

        var timer = (System.Timers.Timer?)PrivateField(first!, "_cursorBlinkTimer");
        Assert.NotNull(timer);
        Assert.True(timer!.Enabled, "the live renderer's blink timer should be running");

        canvas.SetEmulator(new VT100Emulator(80, 24));

        Assert.False(timer.Enabled, "the replaced renderer's blink timer is still running - it leaked");
    }

    [AvaloniaFact]
    public void DisposingTwiceIsSafe()
    {
        // The canvas disposes on swap; nothing stops an owner disposing again on teardown.
        var renderer = new TerminalRenderer(new VT100Emulator(80, 24));

        renderer.Dispose();
        renderer.Dispose();
    }

    [AvaloniaFact]
    public void SwappingDoesNotStackInvalidatedHandlers()
    {
        // The same re-entrancy that leaked the renderer also stacked Invalidated subscriptions:
        // SetEmulator used += with no matching -=, so calling it twice for the SAME emulator meant
        // two repaints per change. The Bell subscription right below it already guarded for this.
        var emulator = new VT100Emulator(80, 24);
        var canvas = new TerminalCanvas();

        canvas.SetEmulator(emulator);
        canvas.SetEmulator(emulator);

        var field = typeof(TerminalEmulatorBase).GetField("Invalidated", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var handler = (Delegate?)field!.GetValue(emulator);

        Assert.NotNull(handler);
        Assert.Single(handler!.GetInvocationList());
    }

    // ─────────────────────────────────────────────────────────────
    // The blink
    // ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheTimerAnnouncesTheFlipWhenTheCursorBlinks()
    {
        // The renderer cannot repaint itself; it tells its owner. Driving the timer callback
        // directly rather than sleeping 500 ms — a test that waits on a wall clock is a flaky test.
        var renderer = new TerminalRenderer(new VT100Emulator(80, 24));
        try
        {
            SetPrivate(renderer, "_cursorIsBlinking", true);

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
    public void ASteadyCursorCostsNoRepaints()
    {
        // The flip still happens, but a non-blinking cursor gains nothing from a full repaint twice
        // a second per tab, so the event stays silent.
        var renderer = new TerminalRenderer(new VT100Emulator(80, 24));
        try
        {
            SetPrivate(renderer, "_cursorIsBlinking", false);

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
    public void TheVisibilityFlagActuallyAlternates()
    {
        var renderer = new TerminalRenderer(new VT100Emulator(80, 24));
        try
        {
            bool before = (bool)PrivateField(renderer, "_cursorVisible")!;
            InvokeBlinkTick(renderer);
            bool after = (bool)PrivateField(renderer, "_cursorVisible")!;
            InvokeBlinkTick(renderer);
            bool andBack = (bool)PrivateField(renderer, "_cursorVisible")!;

            Assert.NotEqual(before, after);
            Assert.Equal(before, andBack);
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [AvaloniaFact]
    public void DisposeDropsBlinkSubscribers()
    {
        // A stopped timer cannot fire, but a live event keeps the subscriber reachable from the
        // dead renderer — which is the shape of the leak this whole change is about.
        var renderer = new TerminalRenderer(new VT100Emulator(80, 24));
        SetPrivate(renderer, "_cursorIsBlinking", true);

        int raised = 0;
        renderer.BlinkStateChanged += () => raised++;

        renderer.Dispose();
        InvokeBlinkTick(renderer);

        Assert.Equal(0, raised);
    }

    private static void SetPrivate(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static void InvokeBlinkTick(TerminalRenderer renderer)
    {
        var method = typeof(TerminalRenderer).GetMethod("OnCursorBlinkElapsed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(renderer, new object?[] { null, null });
    }
}
