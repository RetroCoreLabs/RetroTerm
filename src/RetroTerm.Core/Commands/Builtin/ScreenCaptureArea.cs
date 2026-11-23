//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Which part of the program a picture is taken of.
/// </summary>
/// <remarks>
/// <para><b>Shared by SCREENSHOT and the planned RECORD START</b></para>
/// docs\PLAN.md asks for both commands with "the same whole-window or terminal-only option", so the
/// choice, its spelling in a script and its MCP enum are declared once here and both commands use
/// <see cref="CreateParameter"/> and <see cref="Parse"/>. RECORD can then take its frames through the
/// same desktop renderer SCREENSHOT uses, one per tick, without a second idea of what "window" means.
/// </remarks>
public enum ScreenCaptureArea
{
    /// <summary>
    /// Only the terminal control of the session's tab: the screen text and every graphics plane,
    /// at the control's own size. No tab strip, menu, status bar or window frame.
    /// </summary>
    Terminal,

    /// <summary>
    /// The whole program window that shows the session's tab - menu, tab strip, the terminal,
    /// the status bar - drawn by the program itself. Not a capture of the desktop: the frame and
    /// title bar the operating system draws are not part of it, and nothing on top of the window
    /// (another program, a tooltip) can appear in it.
    /// </summary>
    Window
}

/// <summary>
/// The <c>area</c> parameter both capture commands take, and how its value is read.
/// </summary>
public static class ScreenCaptureAreaParameter
{
    /// <summary>
    /// The parameter's name in a script and in an MCP call.
    /// </summary>
    public const string Name = "area";

    /// <summary>
    /// The word for <see cref="ScreenCaptureArea.Terminal"/>, and the default.
    /// </summary>
    public const string Terminal = "terminal";

    /// <summary>
    /// The word for <see cref="ScreenCaptureArea.Window"/>.
    /// </summary>
    public const string Window = "window";

    /// <summary>
    /// Builds the parameter. Optional, defaulting to the terminal area, which is what SCREENSHOT
    /// captured before the option existed, so every script already written keeps its meaning.
    /// </summary>
    /// <returns>
    /// The parameter, with its two words declared on the metadata.
    /// </returns>
    public static CommandParameter CreateParameter()
        => new CommandParameter(Name, CommandParameterType.String, required: false, defaultValue: Terminal,
            "What to capture: 'terminal' is the terminal area of the session's tab only; 'window' is the "
            + "whole program window showing that tab (menu, tabs, status bar), as the program draws it - "
            + "not the operating system's frame",
            allowedValues: new[] { Terminal, Window });

    /// <summary>
    /// Reads the area from parsed arguments.
    /// </summary>
    /// <param name="args">
    /// The arguments, already checked against the parameter's word list.
    /// </param>
    /// <returns>
    /// The area; <see cref="ScreenCaptureArea.Terminal"/> when none was given.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown for a word outside the list. The shared validation refuses that before any command
    /// runs, so reaching this throw means a surface skipped validation - loud on purpose.
    /// </exception>
    public static ScreenCaptureArea Parse(CommandArgs args)
    {
        var raw = args?.GetString(Name);
        if (raw == null || string.Equals(raw, Terminal, StringComparison.OrdinalIgnoreCase))
        {
            return ScreenCaptureArea.Terminal;
        }

        if (string.Equals(raw, Window, StringComparison.OrdinalIgnoreCase))
        {
            return ScreenCaptureArea.Window;
        }

        throw new ArgumentException($"'{raw}' is not a capture area - use '{Terminal}' or '{Window}'", nameof(args));
    }
}
