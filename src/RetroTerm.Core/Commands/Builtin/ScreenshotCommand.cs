using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Renders one session's screen - text, Sixel, ReGIS and vectors together - to a PNG file.
/// </summary>
/// <param name="session">
/// The session whose screen to draw.
/// </param>
/// <param name="path">
/// Full path of the PNG to write.
/// </param>
/// <returns>
/// Null when the file was written, or a sentence saying why it was not.
/// </returns>
/// <remarks>
/// A named delegate rather than a bare <c>Func</c>, and supplied by the desktop layer, because only
/// the desktop owns a renderer. Core stays free of any dependency on Avalonia.
/// </remarks>
public delegate string? TerminalScreenCapture(TerminalSession session, string path);

/// <summary>
/// SCREENSHOT - save the screen as a PNG, graphics included.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, 27 August 2026</b></para>
/// <c>SNAPSHOT</c> and <c>READSCREEN</c> return the TEXT of the screen, which is the whole screen
/// only when nothing has been drawn. The moment a session carries Sixel, ReGIS or Tektronix
/// graphics, an agent driving the terminal is blind: it can see a menu, and it cannot see whether a
/// box was drawn, whether a crosshair is visible, or whether the picture came out in the wrong
/// colours. Every graphics defect this project has found was found by a person looking at a picture.
/// <para><b>What it changes</b></para>
/// Ronny's words, the day it was written: "why dont you fucking do screenshots ... so you can
/// validate this shit yourself". The point is not convenience - it is that a case which previously
/// had to be handed to a person can now be checked by the thing driving the terminal, and only the
/// genuinely subjective half needs his eyes.
/// <para><b>One command, both surfaces</b></para>
/// Registered into the same <see cref="CommandRegistry"/> as everything else, so the script DSL gets
/// <c>SCREENSHOT</c> and the MCP server gets <c>terminal_screenshot</c> from this one declaration.
/// That is the rule this repository keeps having to relearn.
/// </remarks>
public sealed class ScreenshotCommand : ISessionCommand
{
    private readonly TerminalScreenCapture _capture;

    /// <summary>
    /// Builds the command around the desktop's renderer.
    /// </summary>
    /// <param name="capture">
    /// What actually draws the screen into a file.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="capture"/> is null.
    /// </exception>
    public ScreenshotCommand(TerminalScreenCapture capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    /// <summary>
    /// The command's name in the script DSL.
    /// </summary>
    public string Name => "SCREENSHOT";

    /// <summary>
    /// One line for the generated help.
    /// </summary>
    public string Summary => "Save the screen as a PNG, with Sixel, ReGIS and vector graphics drawn in";

    /// <summary>
    /// A usable example for the generated help.
    /// </summary>
    public string Example => "SCREENSHOT \"C:\\\\shots\\\\after-regis.png\""; // \\ in the DSL - \ starts an escape

    /// <summary>
    /// Whether the command writes a line of output.
    /// </summary>
    public bool ProducesOutput => true;

    /// <summary>
    /// The parameters, read by the script parser and the MCP tool list alike.
    /// </summary>
    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("file", CommandParameterType.String, required: true, defaultValue: null,
            "PNG file to write (directory must exist)"),
        new CommandParameter("settle", CommandParameterType.Int, required: false, defaultValue: "250",
            "Wait for the screen to be quiet this many milliseconds before drawing, so the picture is "
            + "not one change out of date; 0 takes the frame immediately")
    };

    /// <summary>
    /// Renders the screen and writes the file.
    /// </summary>
    /// <param name="session">
    /// The session to draw.
    /// </param>
    /// <param name="args">
    /// The parsed arguments.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the wait for the screen to settle.
    /// </param>
    /// <returns>
    /// Where the file went and how big the picture is, or why it could not be written.
    /// </returns>
    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(args.GetString("file")!);

        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
        {
            return CommandResult.Fail($"directory not found: {dir}");
        }

        await SettleBeforeDrawingAsync(session, args.GetInt("settle", DefaultSettleMs), cancellationToken)
            .ConfigureAwait(false);

        var problem = _capture(session, path);
        if (problem != null)
        {
            return CommandResult.Fail(problem);
        }

        // Report the SIZE as well as the path. A zero-byte or absurdly small file is the shape a
        // failed render takes, and a caller that only sees "saved" would call that a pass.
        long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (bytes == 0)
        {
            return CommandResult.Fail(
                $"the render wrote nothing to {path} - the file is empty");
        }

        return CommandResult.Ok($"screen saved to {path} ({bytes} bytes)");
    }

    /// <summary>
    /// How long the screen must be quiet before the picture is taken, when the caller says nothing.
    /// </summary>
    /// <remarks>
    /// Long enough for the data behind one screenful to finish arriving and be composited, short
    /// enough that taking a series of pictures is not painful. A caller who wants the frame as it
    /// stands right now passes zero.
    /// </remarks>
    private const int DefaultSettleMs = 250;

    /// <summary>
    /// Waits for the session to stop changing, then publishes the graphics, so the picture that
    /// follows is current.
    /// </summary>
    /// <param name="session">
    /// The session about to be drawn.
    /// </param>
    /// <param name="settleMs">
    /// How long the screen must be quiet. Zero or less skips the wait entirely.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the wait.
    /// </param>
    /// <returns>
    /// A task that finishes when it is safe to draw.
    /// </returns>
    /// <remarks>
    /// <para><b>Why this is inside the command</b></para>
    /// FOUND BY DRIVING M6.5 ON 27 AUGUST 2026. The renderer draws the last PUBLISHED graphics
    /// composite, so a picture taken before the session had composited showed the previous one. On a
    /// brand new tab that meant a completely EMPTY picture - no box, no circle, no cursor - which
    /// reads as a missing feature and is nothing of the kind. Sixteen arrow presses sent in one
    /// script looked lost the same way, and the host's report afterwards proved every one had
    /// landed.
    ///
    /// The rule "put WAITIDLE before SCREENSHOT" would work and would be forgotten, by a person and
    /// by an agent alike. A command whose whole purpose is to be BELIEVED must not hand back a
    /// picture that is one change out of date, so the waiting belongs here.
    ///
    /// <para><b>Best effort, never a failure</b></para>
    /// A screen that will not go quiet - an animation, a host printing steadily - still deserves a
    /// picture, and a session that was never connected has nothing to wait for. So the outcome of
    /// the wait is deliberately ignored: it is a chance to be current, not a precondition.
    ///
    /// <para><b>The composite runs on the session thread</b></para>
    /// Compositing writes the back buffer and publishes it with one reference write, and that is
    /// the pump's work - see GraphicsCompositor. Calling it from here would race the pump.
    /// </remarks>
    private static async Task SettleBeforeDrawingAsync(TerminalSession session, int settleMs,
        CancellationToken cancellationToken)
    {
        if (settleMs <= 0) return;

        try
        {
            var options = new ScreenWaitOptions
            {
                Pattern = null,
                IdleMs = settleMs,

                // Generous, because it is never fatal. A screen that keeps changing simply gets
                // photographed as it is once this gives up.
                TimeoutMs = settleMs * 8
            };

            await session.WaitForScreenAsync(options, cancellationToken).ConfigureAwait(false);

            // Publish whatever the graphics planes now hold. Without this a picture that changed
            // only the GRAPHICS - a ReGIS cursor moving, with no text touched - could still be read
            // from a composite published before the move.
            await session.RunOnSessionThreadAsync(() => session.Emulator?.Graphics?.Composite())
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller gave up waiting. Take the picture anyway - a late picture beats none.
        }
    }

    /// <summary>
    /// Adds the command to a registry, which gives both the script DSL and MCP the same thing.
    /// </summary>
    /// <param name="registry">
    /// The registry to add to.
    /// </param>
    /// <param name="capture">
    /// The desktop's renderer.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="registry"/> is null.
    /// </exception>
    public static void RegisterAll(CommandRegistry registry, TerminalScreenCapture capture)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new ScreenshotCommand(capture));
    }
}
