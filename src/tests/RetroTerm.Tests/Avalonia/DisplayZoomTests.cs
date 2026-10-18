using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Ctrl+plus, Ctrl+minus and Ctrl+0 change how big the picture is drawn, without changing how many
/// columns and rows the terminal has.
/// </summary>
/// <remarks>
/// <para><b>Why both modes exist</b></para>
/// The window normally decides the size: drag it wider and you get more columns. That is what a
/// modern terminal does, and it is what a host wants to be told about.
/// It is the wrong answer for a terminal whose geometry is part of what it IS. A TDV2200 is 80 by
/// 25 because the hardware was. Making it 137 columns because the window is wide is not a bigger
/// TDV2200, it is a different machine. So an explicit zoom PINS the grid and magnifies instead.
/// The two must never both be active. If they were, a wider window would add columns, which would
/// make the fitted size bigger, which would change what the zoom is a multiple of.
/// <para><b>These tests check the NUMBER. DisplayZoomPixelTests checks the picture.</b></para>
/// Everything here is about which percentage a gesture selects, and that is all it can see. When
/// this file was the ONLY zoom coverage it passed sixteen times over while three faults Ronny found
/// by eye sat in the rendering - a blank screen at 300 percent, a blank screen coming back from full
/// screen, and a ladder measured from the wrong baseline. Any change to what is drawn belongs in
/// DisplayZoomPixelTests, which renders and measures pixels.
/// </remarks>
[Collection("Avalonia")]
public class DisplayZoomTests
{
    /// <summary>
    /// Builds a canvas with an emulator on it.
    /// </summary>
    /// <returns>
    /// The canvas, ready to arrange.
    /// </returns>
    private static TerminalCanvas CanvasWithTerminal()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);
        return canvas;
    }

    /// <summary>
    /// Lays a canvas out and returns every terminal size it asked for.
    /// </summary>
    /// <param name="canvas">
    /// The canvas.
    /// </param>
    /// <param name="width">
    /// Width in pixels.
    /// </param>
    /// <param name="height">
    /// Height in pixels.
    /// </param>
    /// <returns>
    /// The requested sizes, in order.
    /// </returns>
    private static List<(int Columns, int Rows)> ArrangeAt(TerminalCanvas canvas, double width, double height)
    {
        var asked = new List<(int, int)>();
        void Record(int columns, int rows) => asked.Add((columns, rows));

        canvas.TerminalResizeRequested += Record;
        try
        {
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
        }
        finally
        {
            canvas.TerminalResizeRequested -= Record;
        }

        return asked;
    }

    [AvaloniaFact]
    public void ItStartsAtTheNormalView()
    {
        var canvas = CanvasWithTerminal();

        // 100 IS the fitted picture. The two used to be different settings, with 0 meaning fit, and
        // that is what made the ladder read as broken: the default view was about 177 percent, so
        // every step below it made the screen smaller than it had just been.
        Assert.Equal(100, canvas.ZoomPercent);
        Assert.Equal(100, TerminalCanvas.NaturalZoomPercent);
    }

    [AvaloniaFact]
    public void ZeroStillMeansTheNormalView()
    {
        // Zero was "fit" before fit and 100 became the same thing. A saved setting or a script may
        // still say it, and it must not land on a zoom of nothing.
        var canvas = CanvasWithTerminal();
        canvas.ZoomPercent = 250;

        canvas.ZoomPercent = 0;

        Assert.Equal(100, canvas.ZoomPercent);
    }

    [AvaloniaFact]
    public void AZoomPinsTheGridSoTheWindowStopsChangingIt()
    {
        var canvas = CanvasWithTerminal();

        // At the normal view: a big window asks for a bigger terminal.
        Assert.NotEmpty(ArrangeAt(canvas, 1600, 900));

        canvas.ZoomPercent = 150;

        // Zoomed: the same big window asks for nothing at all. This is the whole point - without it
        // the two modes fight, and the zoom is a multiple of a fitted size that keeps moving.
        Assert.Empty(ArrangeAt(canvas, 1600, 900));

        // Back to the normal view, and it starts asking again.
        canvas.ZoomPercent = 100;
        Assert.NotEmpty(ArrangeAt(canvas, 1400, 800));
    }

    [AvaloniaFact]
    public void SteppingWalksTheLadderAndStopsAtBothEnds()
    {
        var canvas = CanvasWithTerminal();

        canvas.StepZoom(1);
        Assert.Equal(125, canvas.ZoomPercent);

        canvas.StepZoom(-1);
        Assert.Equal(100, canvas.ZoomPercent);

        // Walk to the top and try to go past it.
        for (int i = 0; i < 20; i++) canvas.StepZoom(1);
        Assert.Equal(TerminalCanvas.ZoomSteps[TerminalCanvas.ZoomSteps.Length - 1], canvas.ZoomPercent);

        // And to the bottom. It stops there rather than jumping back to the normal view, which
        // would be a surprising thing for a shrink key to do.
        for (int i = 0; i < 20; i++) canvas.StepZoom(-1);
        Assert.Equal(TerminalCanvas.ZoomSteps[0], canvas.ZoomPercent);
    }

    [AvaloniaFact]
    public void TheLadderIsSortedAndHoldsTheNormalView()
    {
        // The dropdown and the shortcuts both read this, so its shape is worth pinning: a step that
        // went backwards would make Ctrl+plus shrink the text.
        for (int i = 1; i < TerminalCanvas.ZoomSteps.Length; i++)
        {
            Assert.True(TerminalCanvas.ZoomSteps[i] > TerminalCanvas.ZoomSteps[i - 1],
                $"step {i} ({TerminalCanvas.ZoomSteps[i]}) is not above the one before it");
        }

        // The normal view has to BE a rung, or stepping down from 125 could not land on it and
        // there would be no way back to the ordinary picture with the keyboard.
        Assert.Contains(TerminalCanvas.NaturalZoomPercent, TerminalCanvas.ZoomSteps);
    }

    [AvaloniaFact]
    public void ChangesAreAnnouncedSoTheStatusBarCanFollow()
    {
        var canvas = CanvasWithTerminal();

        var seen = new List<int>();
        canvas.ZoomChanged += percent => seen.Add(percent);

        canvas.StepZoom(1);
        canvas.StepZoom(1);
        canvas.ZoomPercent = 100;

        Assert.Equal(new List<int> { 125, 150, 100 }, seen);
    }

    [AvaloniaFact]
    public void SettingTheZoomItAlreadyHasAnnouncesNothing()
    {
        var canvas = CanvasWithTerminal();
        canvas.ZoomPercent = 200;

        int announcements = 0;
        canvas.ZoomChanged += _ => announcements++;

        canvas.ZoomPercent = 200;

        Assert.Equal(0, announcements);
    }

    [AvaloniaTheory]
    [InlineData(Key.OemPlus, 125)]
    [InlineData(Key.Add, 125)]
    public void CtrlPlusZoomsInFromEitherKeyboard(Key key, int expected)
    {
        var canvas = CanvasWithTerminal();
        PressWithControl(canvas, key);
        Assert.Equal(expected, canvas.ZoomPercent);
    }

    [AvaloniaTheory]
    [InlineData(Key.OemMinus)]
    [InlineData(Key.Subtract)]
    public void CtrlMinusZoomsOutFromEitherKeyboard(Key key)
    {
        var canvas = CanvasWithTerminal();
        canvas.ZoomPercent = 150;

        PressWithControl(canvas, key);

        Assert.Equal(125, canvas.ZoomPercent);
    }

    [AvaloniaTheory]
    [InlineData(Key.D0)]
    [InlineData(Key.NumPad0)]
    public void CtrlZeroGoesBackToTheNormalView(Key key)
    {
        var canvas = CanvasWithTerminal();
        canvas.ZoomPercent = 300;

        PressWithControl(canvas, key);

        Assert.Equal(100, canvas.ZoomPercent);
    }

    [AvaloniaFact]
    public void APlainMinusIsStillTypedAtTheTerminal()
    {
        var canvas = CanvasWithTerminal();

        // Without Control this is just a character, and stealing it would make the key unusable.
        canvas.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.OemMinus,
            KeyModifiers = global::Avalonia.Input.KeyModifiers.None,
        });

        Assert.Equal(100, canvas.ZoomPercent);
    }

    [AvaloniaFact]
    public void CtrlAndTheWheelZooms()
    {
        var canvas = CanvasWithTerminal();

        Wheel(canvas, 1, global::Avalonia.Input.KeyModifiers.Control);
        Assert.Equal(125, canvas.ZoomPercent);

        Wheel(canvas, 1, global::Avalonia.Input.KeyModifiers.Control);
        Assert.Equal(150, canvas.ZoomPercent);

        Wheel(canvas, -1, global::Avalonia.Input.KeyModifiers.Control);
        Assert.Equal(125, canvas.ZoomPercent);
    }

    [AvaloniaFact]
    public void ThePlainWheelStillScrollsRatherThanZooming()
    {
        var canvas = CanvasWithTerminal();

        Wheel(canvas, 1, global::Avalonia.Input.KeyModifiers.None);

        Assert.Equal(100, canvas.ZoomPercent);
    }

    [AvaloniaFact]
    public void AProgramTrackingTheMouseDoesNotSwallowTheZoom()
    {
        // Mouse reporting on, as vim or htop would have it. The wheel would normally go to the
        // host - but the zoom gesture has to keep working, or it would fail in exactly the place
        // the text is hardest to read.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("xterm", 80, 24, 100);
        // Built from bytes on purpose. A raw ESC character sitting in the source is invisible in
        // a diff and in most editors, and the obvious written form "\x1b[?1000h" is WORSE: \x
        // takes up to four hex digits, so it swallows the bracket and the sequence arrives as one
        // meaningless character. Either way the mode would stay off and this test would pass
        // while checking nothing.
        byte[] enableMouseReporting =
            { 0x1B, (byte)'[', (byte)'?', (byte)'1', (byte)'0', (byte)'0', (byte)'0', (byte)'h' };
        emulator.ProcessData(enableMouseReporting);

        // Proven on rather than assumed. Without this guard the assertion below is decoration.
        Assert.Equal(RetroTerm.Core.Terminal.Input.MouseTrackingMode.PressAndRelease, emulator.Mouse.Mode);

        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);

        Wheel(canvas, 1, global::Avalonia.Input.KeyModifiers.Control);

        Assert.Equal(125, canvas.ZoomPercent);
    }

    /// <summary>
    /// Sends one wheel notch.
    /// </summary>
    /// <param name="canvas">
    /// The canvas.
    /// </param>
    /// <param name="delta">
    /// Positive for up, negative for down.
    /// </param>
    /// <param name="modifiers">
    /// Modifier keys held.
    /// </param>
    private static void Wheel(TerminalCanvas canvas, double delta,
        global::Avalonia.Input.KeyModifiers modifiers)
    {
        canvas.RaiseEvent(new PointerWheelEventArgs(
            canvas,
            null!,
            canvas,
            new Point(10, 10),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            modifiers,
            new Vector(0, delta)));
    }

    /// <summary>
    /// Sends one key press with Control held.
    /// </summary>
    /// <param name="canvas">
    /// The canvas to send it to.
    /// </param>
    /// <param name="key">
    /// The key.
    /// </param>
    private static void PressWithControl(TerminalCanvas canvas, Key key)
    {
        canvas.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = global::Avalonia.Input.KeyModifiers.Control,
        });
    }
}
