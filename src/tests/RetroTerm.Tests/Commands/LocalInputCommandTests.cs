using System.Collections.Generic;
using System.Threading;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// LOCALKEY, ZOOM and PASTE - the three commands that act on the terminal WINDOW.
/// </summary>
/// <remarks>
/// <para><b>Why they exist</b></para>
/// Every other command ends at <c>TerminalSession.SendInputAsync</c>, which puts bytes on the
/// connection. Three things the terminal decides for itself could therefore not be driven at all:
/// the ReGIS graphics input cursor, the display zoom, and the paste path. Those were three of the
/// four cases still being handed to a person to judge by eye.
/// <para><b>What is covered here, and what is not</b></para>
/// The commands' own behaviour: what they hand to the desktop, what they refuse, and what they
/// report. Whether the desktop then really moves a crosshair is a question about the canvas, and it
/// is answered by <c>RegisGraphicsInputKeyTests</c> - whose own key helpers now call the same
/// production method the LOCALKEY delegate does.
/// </remarks>
public class LocalInputCommandTests
{
    /// <summary>
    /// A session to hang the commands off. Nothing is drawn - the delegates are stand-ins.
    /// </summary>
    /// <returns>
    /// The session.
    /// </returns>
    private static TerminalSession NewSession()
        => new TerminalSession(new VT100Emulator(80, 24), "LocalInputTest");

    /// <summary>
    /// Runs one command with the given arguments.
    /// </summary>
    /// <param name="command">
    /// The command under test.
    /// </param>
    /// <param name="values">
    /// The arguments, as both surfaces deliver them - text.
    /// </param>
    /// <returns>
    /// What the command reported.
    /// </returns>
    private static CommandResult Run(ISessionCommand command, Dictionary<string, string> values)
        => command.ExecuteAsync(NewSession(), new CommandArgs(values), CancellationToken.None)
            .GetAwaiter().GetResult();

    // ─────────────────────────────────────────────────────────────
    // LOCALKEY
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnArrowKeyIsHandedToTheWindowWithItsShiftState()
    {
        // Shift is not decoration here. Chapter 15 of the VT330/VT340 Programmer Reference: a plain
        // arrow moves the ReGIS input cursor one pixel and a shifted one moves it ten. A command
        // that dropped the shift would move the crosshair a tenth as far and look almost right.
        string? seenKey = null;
        bool seenShift = false;

        var command = new LocalKeyCommand((session, key, shift) =>
        {
            seenKey = key;
            seenShift = shift;
            return null;
        });

        var result = Run(command, new Dictionary<string, string>
        {
            ["key"] = "Left",
            ["shift"] = "true"
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal("Left", seenKey);
        Assert.True(seenShift);
    }

    [Fact]
    public void ShiftDefaultsToNotHeld()
    {
        bool seenShift = true;

        var command = new LocalKeyCommand((session, key, shift) =>
        {
            seenShift = shift;
            return null;
        });

        var result = Run(command, new Dictionary<string, string> { ["key"] = "Right" });

        Assert.True(result.Success, result.Error);
        Assert.False(seenShift);
    }

    [Fact]
    public void TheWindowsOwnComplaintIsPassedStraightBack()
    {
        // An unknown key name is the desktop's to detect, because only the desktop knows what names
        // exist. Saying which name failed beats a generic refusal.
        var command = new LocalKeyCommand((session, key, shift) => "unknown key name 'Wiggle'");

        var result = Run(command, new Dictionary<string, string> { ["key"] = "Wiggle" });

        Assert.False(result.Success);
        Assert.Contains("Wiggle", result.Error!);
    }

    [Fact]
    public void AnEmptyKeyIsRefusedBeforeTheWindowIsTouched()
    {
        bool delivered = false;

        var command = new LocalKeyCommand((session, key, shift) => { delivered = true; return null; });

        var result = Run(command, new Dictionary<string, string> { ["key"] = "" });

        Assert.False(result.Success);
        Assert.False(delivered, "nothing should reach the window when there is no key");
    }

    // ─────────────────────────────────────────────────────────────
    // ZOOM
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ZoomWithNoArgumentsReadsTheMagnificationAndChangesNothing()
    {
        // The reporting case matters as much as the setting case: something driving the terminal has
        // to be able to find out where the zoom IS before deciding anything about the picture.
        int? seenPercent = -1;
        int seenStep = -1;

        var command = new ZoomCommand((TerminalSession session, int? percent, int step, out int now) =>
        {
            seenPercent = percent;
            seenStep = step;
            now = 175;
            return null;
        });

        var result = Run(command, new Dictionary<string, string>());

        Assert.True(result.Success, result.Error);
        Assert.Null(seenPercent);
        Assert.Equal(0, seenStep);
        Assert.Contains("175", result.Output!);
    }

    [Fact]
    public void ZoomReportsThePercentageInForceAfterTheChange()
    {
        var command = new ZoomCommand((TerminalSession session, int? percent, int step, out int now) =>
        {
            now = percent ?? 100;
            return null;
        });

        var result = Run(command, new Dictionary<string, string> { ["percent"] = "300" });

        Assert.True(result.Success, result.Error);
        Assert.Contains("300", result.Output!);
    }

    [Fact]
    public void GivingBothPercentAndStepIsRefusedRatherThanGuessedAt()
    {
        // Letting one silently win would make a mistyped script do the wrong thing quietly, which
        // is worse than failing: the picture would be at some magnification nobody asked for and
        // the transcript would say the step succeeded.
        bool called = false;

        var command = new ZoomCommand((TerminalSession session, int? percent, int step, out int now) =>
        {
            called = true;
            now = 100;
            return null;
        });

        var result = Run(command, new Dictionary<string, string>
        {
            ["percent"] = "200",
            ["step"] = "1"
        });

        Assert.False(result.Success);
        Assert.False(called, "a refused command must not reach the window at all");
    }

    [Fact]
    public void ZoomPassesTheStepThroughWithItsSign()
    {
        int seenStep = 0;

        var command = new ZoomCommand((TerminalSession session, int? percent, int step, out int now) =>
        {
            seenStep = step;
            now = 75;
            return null;
        });

        var result = Run(command, new Dictionary<string, string> { ["step"] = "-2" });

        Assert.True(result.Success, result.Error);
        Assert.Equal(-2, seenStep);
    }

    // ─────────────────────────────────────────────────────────────
    // PASTE
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PasteWithNoTextMeansTheSystemClipboard()
    {
        // Null and empty are different answers. Null says "use the clipboard"; an empty string
        // would say "paste nothing", and a command that folded them together would make the
        // clipboard route unreachable.
        bool called = false;
        string? seenText = "not null";

        var command = new PasteCommand((session, text) =>
        {
            called = true;
            seenText = text;
            return null;
        });

        var result = Run(command, new Dictionary<string, string>());

        Assert.True(result.Success, result.Error);
        Assert.True(called);
        Assert.Null(seenText);
        Assert.Contains("clipboard", result.Output!);
    }

    [Fact]
    public void NamedTextIsPastedAndItsLengthReported()
    {
        string? seenText = null;

        var command = new PasteCommand((session, text) => { seenText = text; return null; });

        var result = Run(command, new Dictionary<string, string> { ["text"] = "LIST-FILES" });

        Assert.True(result.Success, result.Error);
        Assert.Equal("LIST-FILES", seenText);
        Assert.Contains("10", result.Output!);
    }

    [Fact]
    public void ThePasteTextCarriesBackslashEscapes()
    {
        // The wrapped-line paste case is a paste that SPANS lines, so the notation for a line break
        // has to survive the trip. This is the same flag SEND's text carries, and it is declared on
        // the parameter so both surfaces honour it - the trap this repository fell into when SEND
        // swallowed a carriage return over MCP for weeks.
        var registry = new CommandRegistry();
        LocalInputCommands.RegisterAll(registry,
            (session, key, shift) => null,
            (TerminalSession session, int? percent, int step, out int now) => { now = 100; return null; },
            (session, text) => null);

        Assert.True(registry.TryGet("PASTE", out var paste));
        Assert.NotNull(paste);

        var parameter = paste!.Parameters[0];
        Assert.Equal("text", parameter.Name);
        Assert.True(parameter.DecodeEscapes,
            "a paste that spans lines needs the line break to survive the MCP surface");
    }

    // ─────────────────────────────────────────────────────────────
    // Both surfaces
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RegisteringGivesTheScriptDslAndMcpAllThreeCommands()
    {
        // One registration, two front ends. The script parser and the MCP tool list both read this
        // registry, so none of the three can exist on one surface and not the other.
        var registry = new CommandRegistry();

        LocalInputCommands.RegisterAll(registry,
            (session, key, shift) => null,
            (TerminalSession session, int? percent, int step, out int now) => { now = 100; return null; },
            (session, text) => null);

        Assert.True(registry.TryGet("LOCALKEY", out var localKey));
        Assert.True(registry.TryGet("ZOOM", out var zoom));
        Assert.True(registry.TryGet("PASTE", out var paste));

        Assert.NotNull(localKey);
        Assert.NotNull(zoom);
        Assert.NotNull(paste);

        // LOCALKEY's key is the one required argument of the three - a zoom with no arguments reads
        // the value and a paste with none uses the clipboard.
        Assert.True(localKey!.Parameters[0].Required);
        Assert.False(zoom!.Parameters[0].Required);
        Assert.False(paste!.Parameters[0].Required);
    }
}
