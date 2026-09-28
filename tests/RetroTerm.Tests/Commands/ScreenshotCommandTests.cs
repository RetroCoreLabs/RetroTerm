using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// SCREENSHOT - saving the screen as a picture rather than as text.
/// </summary>
/// <remarks>
/// <para><b>Why the command exists</b></para>
/// <c>READSCREEN</c> and <c>SNAPSHOT</c> return TEXT. On a session carrying Sixel, ReGIS or
/// Tektronix graphics that is a fraction of the screen, so anything driving the terminal is blind to
/// exactly the part of this program where every defect has been found by looking. Written 27 August
/// 2026 at Ronny's instruction: "why dont you fucking do screenshots ... so you can validate this
/// shit yourself."
/// <para><b>What these tests cover, and what they cannot</b></para>
/// The command's own behaviour: refusing a missing directory, reporting a renderer's complaint, and
/// - the one that matters - refusing to call an EMPTY file a success. Whether the picture looks
/// right is not a thing an assertion can say, which is the whole reason the command exists.
/// The renderer itself lives in the desktop layer and is exercised by the headless UI tests.
/// </remarks>
public class ScreenshotCommandTests
{
    /// <summary>
    /// A session to hang the command off. Nothing is drawn - the capture is a stand-in.
    /// </summary>
    /// <returns>
    /// The session.
    /// </returns>
    private static TerminalSession NewSession()
        => new TerminalSession(new VT100Emulator(80, 24), "ScreenshotTest");

    /// <summary>
    /// Runs the command with a given stand-in renderer.
    /// </summary>
    /// <param name="capture">
    /// The stand-in.
    /// </param>
    /// <param name="file">
    /// The path to pass as the file argument.
    /// </param>
    /// <returns>
    /// What the command reported.
    /// </returns>
    private static CommandResult Run(TerminalScreenCapture capture, string file)
    {
        var command = new ScreenshotCommand(capture);
        var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, string>
        {
            ["file"] = file
        });

        return command.ExecuteAsync(NewSession(), args, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void ARendererThatWritesNothingIsAFailureAndNotASuccess()
    {
        // THE ONE THAT MATTERS. A render that silently produces nothing is the exact shape this
        // command exists to catch; reporting "saved" for an empty file would make the tool worse
        // than useless, because the caller would believe it had looked.
        var path = Path.Combine(Path.GetTempPath(), "retroterm-empty-shot.png");
        File.WriteAllBytes(path, System.Array.Empty<byte>());

        try
        {
            var result = Run((session, p) => null, path);

            Assert.False(result.Success);
            Assert.Contains("empty", result.Error!, System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AFileWithPixelsInItIsReportedWithItsSize()
    {
        // The size is reported deliberately: it is the cheapest signal that something was actually
        // drawn, and a caller reading the transcript can see it without opening the file.
        var path = Path.Combine(Path.GetTempPath(), "retroterm-real-shot.png");

        try
        {
            var result = Run((session, p) =>
            {
                File.WriteAllBytes(p, new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 });
                return null;
            }, path);

            Assert.True(result.Success, result.Error);
            Assert.Contains("8 bytes", result.Output!);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TheRenderersOwnComplaintIsPassedStraightBack()
    {
        // A tab that has never been laid out has no pixels, and saying so beats a stack trace or a
        // zero-by-zero picture.
        var path = Path.Combine(Path.GetTempPath(), "retroterm-never-written.png");

        var result = Run((session, p) => "that tab has not been laid out yet", path);

        Assert.False(result.Success);
        Assert.Contains("laid out", result.Error!);
    }

    [Fact]
    public void AMissingDirectoryIsRefusedBeforeAnythingIsRendered()
    {
        bool rendered = false;

        var result = Run((session, p) => { rendered = true; return null; },
            Path.Combine(Path.GetTempPath(), "no-such-folder-here", "shot.png"));

        Assert.False(result.Success);
        Assert.Contains("directory not found", result.Error!);
        Assert.False(rendered, "the renderer must not run when the destination cannot exist");
    }

    [Fact]
    public void RegisteringItGivesBothTheScriptDslAndMcpTheSameCommand()
    {
        // The two-surface rule. One registration, and the script parser and the MCP tool list both
        // read this same declaration - so SCREENSHOT cannot exist on one surface and not the other.
        var registry = new CommandRegistry();
        ScreenshotCommand.RegisterAll(registry, (session, p) => null);

        Assert.True(registry.TryGet("SCREENSHOT", out var command));

        Assert.NotNull(command);
        Assert.Equal(2, command!.Parameters.Count);

        Assert.Equal("file", command.Parameters[0].Name);
        Assert.True(command.Parameters[0].Required);

        // settle is the guard against a picture that is one change out of date. It is optional,
        // so a caller who says nothing gets the safe behaviour rather than the fast one.
        Assert.Equal("settle", command.Parameters[1].Name);
        Assert.False(command.Parameters[1].Required);
        Assert.Equal("250", command.Parameters[1].DefaultValue);
    }

    [Fact]
    public async Task SettleZeroTakesTheFrameStraightAwayAndStillRenders()
    {
        // The escape hatch. A caller taking a rapid series of pictures, or one that has already
        // waited itself, must be able to say "now" - otherwise every shot costs a quarter second.
        var path = Path.Combine(Path.GetTempPath(), "retroterm-settle-zero.png");

        try
        {
            var command = new ScreenshotCommand((session, p) =>
            {
                File.WriteAllBytes(p, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                return null;
            });

            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, string>
            {
                ["file"] = path,
                ["settle"] = "0"
            });

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await command.ExecuteAsync(NewSession(), args, CancellationToken.None);
            watch.Stop();

            Assert.True(result.Success, result.Error);

            // Nowhere near the 250 ms default. Timing is a weak assertion, so the bound is loose
            // enough that a slow machine cannot fail it while still catching a wait that ran.
            Assert.True(watch.ElapsedMilliseconds < 200,
                "settle=0 waited " + watch.ElapsedMilliseconds + " ms, so the wait was not skipped");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ANeverConnectedSessionIsStillPhotographedRatherThanRefused()
    {
        // The settle wait is a CHANCE to be current, never a precondition. A session with no host
        // has nothing to go quiet, and refusing to draw it would break the commonest case of all -
        // photographing a terminal after the connection dropped.
        var path = Path.Combine(Path.GetTempPath(), "retroterm-never-connected.png");

        try
        {
            var result = Run((session, p) =>
            {
                File.WriteAllBytes(p, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                return null;
            }, path);

            Assert.True(result.Success, result.Error);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
