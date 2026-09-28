using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Profiles;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Models;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// A terminal whose geometry is part of what it is does not grow with the window.
/// </summary>
/// <remarks>
/// <para><b>The rule, and where it comes from</b></para>
/// A TDV2200 has one screen, 80 by 25, wired that way. Growing it to 137 columns because a window
/// is wide is not a bigger TDV2200, it is a machine that never existed. A VT100 or an xterm has no
/// such constraint and should use the whole window.
/// That line is NOT drawn again here. <c>TerminalFeatures.HostResize</c> already draws it, already
/// carries the reason beside it, and already withholds itself from the TDV profiles - so the
/// default is read from the terminal's own profile. A second list would be a second thing to keep
/// in step, which is how these things drift apart.
/// A connection may override it either way: some people want a VT100 held at 80 by 24, and a TDV
/// can be configured for 132 columns in SINTRAN.
/// </remarks>
[Collection("Avalonia")]
public class FixedGeometryTerminalTests
{
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

    /// <summary>
    /// Builds a canvas around one emulator type.
    /// </summary>
    /// <param name="emulatorType">
    /// Profile name, e.g. "VT100" or "TDV2200".
    /// </param>
    /// <returns>
    /// The canvas.
    /// </returns>
    private static TerminalCanvas CanvasFor(string emulatorType)
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator(emulatorType, 80, 24, 100);
        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);
        return canvas;
    }

    [AvaloniaFact]
    public void TheProfilesReallyDisagreeAboutThis()
    {
        // The whole design rests on this being true. If HostResize were on every profile, reading
        // the default from it would silently mean "everything follows the window" and the TDV cases
        // below would be passing for the wrong reason.
        var vt = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        var tdv = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("TDV2200", 80, 24, 100);

        Assert.True(vt.Profile.Supports(TerminalFeatures.HostResize));
        Assert.False(tdv.Profile.Supports(TerminalFeatures.HostResize));
    }

    [AvaloniaFact]
    public void AVtFollowsTheWindowByDefault()
    {
        var canvas = CanvasFor("VT100");

        Assert.True(canvas.FollowsWindowSize());
        Assert.NotEmpty(ArrangeAt(canvas, 1600, 900));
    }

    [AvaloniaFact]
    public void ATdvKeepsItsOwnGeometryByDefault()
    {
        var canvas = CanvasFor("TDV2200");

        Assert.False(canvas.FollowsWindowSize());

        // A window twice the size asks for nothing. The picture is scaled instead, which is what
        // the fit path already draws.
        Assert.Empty(ArrangeAt(canvas, 1600, 900));
    }

    [AvaloniaFact]
    public void ATektronixKeepsItsOwnGeometryToo()
    {
        var canvas = CanvasFor("TEK4014");

        Assert.False(canvas.FollowsWindowSize());
        Assert.Empty(ArrangeAt(canvas, 1600, 900));
    }

    [AvaloniaFact]
    public void AConnectionCanOverrideItEitherWay()
    {
        // A TDV told to follow the window does.
        var tdv = CanvasFor("TDV2200");
        tdv.FollowWindowSize = true;
        Assert.True(tdv.FollowsWindowSize());
        Assert.NotEmpty(ArrangeAt(tdv, 1600, 900));

        // And a VT told to hold still does that.
        var vt = CanvasFor("VT100");
        vt.FollowWindowSize = false;
        Assert.False(vt.FollowsWindowSize());
        Assert.Empty(ArrangeAt(vt, 1600, 900));
    }

    [AvaloniaFact]
    public void ClearingTheOverrideGoesBackToTheProfilesAnswer()
    {
        var canvas = CanvasFor("TDV2200");

        canvas.FollowWindowSize = true;
        Assert.True(canvas.FollowsWindowSize());

        canvas.FollowWindowSize = null;
        Assert.False(canvas.FollowsWindowSize());
    }

    [AvaloniaFact]
    public void TheConnectionParametersReachTheControlWithoutAnyoneRememberingTo()
    {
        // Five different paths assign LastConnectionParameters. This checks the setting travels
        // with it, so none of them has to remember - which is the point of doing it in the setter.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        using var session = new TerminalSession(emulator, "geometry");

        var control = new TerminalControl();
        control.SetEmulator(emulator);

        using var tab = new TabSession(session, control);

        Assert.True(control.FollowsWindowSize());   // VT100's own answer

        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            EmulatorType = "VT100",
            Width = 80,
            Height = 24,
            FollowWindowSize = false,
        };

        Assert.False(control.FollowsWindowSize());

        // And a connection that says nothing about it puts the profile back in charge.
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            EmulatorType = "VT100",
            Width = 80,
            Height = 24,
        };

        Assert.True(control.FollowsWindowSize());
    }

    [AvaloniaFact]
    public void TheSettingSurvivesBeingSavedAndLoadedAgain()
    {
        // A new field on a saved connection is the classic thing to add everywhere except the
        // round trip, where it then silently reverts to the default on the next launch.
        var editing = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();

        editing.ScreenSizeMode = RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel.FixedSizeLabel;
        editing.Size = "132x25";

        var saved = editing.ToHostConfiguration();
        Assert.False(saved.FollowWindowSize);
        Assert.Equal(132, saved.Width);

        var reloaded = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();
        reloaded.LoadFromConfiguration(saved);

        Assert.Equal(
            RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel.FixedSizeLabel,
            reloaded.ScreenSizeMode);

        // And it reaches the thing that actually connects.
        Assert.False(reloaded.ToConnectionParameters().FollowWindowSize);
    }

    [AvaloniaFact]
    public void SayingNothingStaysNullRatherThanBecomingFalse()
    {
        // "Default for terminal" must not save as "follow the window", or every existing saved
        // connection would quietly stop honouring its profile.
        var editing = new RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel();

        Assert.Equal(
            RetroTerm.Desktop.ViewModels.ConnectionProfileViewModel.DefaultSizeLabel,
            editing.ScreenSizeMode);

        Assert.Null(editing.ToHostConfiguration().FollowWindowSize);
        Assert.Null(editing.ToConnectionParameters().FollowWindowSize);
    }

    [AvaloniaFact]
    public void AFixedTerminalCanStillBeZoomed()
    {
        // Zoom is how you make a fixed-geometry terminal readable, so the two must not conflict.
        var canvas = CanvasFor("TDV2200");

        // 100 is the normal view and the first step up is the next rung on the ladder.
        Assert.Equal(100, canvas.ZoomPercent);

        canvas.StepZoom(1);
        Assert.Equal(125, canvas.ZoomPercent);

        Assert.Empty(ArrangeAt(canvas, 1600, 900));
    }
}
