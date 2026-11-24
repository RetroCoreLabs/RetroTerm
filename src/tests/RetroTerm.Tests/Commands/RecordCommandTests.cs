//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// RECORDSTART and RECORDSTOP as commands: their declared parameters, what the script parser
/// refuses before anything runs, and the checks the commands make before asking the desktop to
/// record. The recording itself is tested on a real window in
/// RetroTerm.Tests.Avalonia.RecordCommandOnARealWindowTests.
/// </summary>
public class RecordCommandTests
{
    private static TerminalSession NewSession()
        => new TerminalSession(new VT100Emulator(80, 24), "RecordTest");

    /// <summary>
    /// A registry holding the two commands with stand-ins that count their calls.
    /// </summary>
    private static CommandRegistry Registry(Func<int>? onStart = null)
    {
        var registry = new CommandRegistry();
        RecordCommands.RegisterAll(registry,
            (session, area, path, interval) => { onStart?.Invoke(); return null; },
            session => ScreenRecordingOutcome.Refused("no recording is running for this session - RECORDSTART first"),
            () => new RetroTerm.Core.Recording.FfmpegChoice { Problem = "stub: never asked, the stop is refused" },
            (ffmpeg, film, width, height, token) => throw new InvalidOperationException("stub: never run, the stop is refused"));
        return registry;
    }

    [Fact]
    public void RecordStartTakesTheSameAreaWordsAsScreenshot()
    {
        Assert.True(Registry().TryGet("RECORDSTART", out var command));
        Assert.Equal(3, command!.Parameters.Count);

        Assert.Equal("file", command.Parameters[0].Name);
        Assert.True(command.Parameters[0].Required);

        var area = command.Parameters[1];
        var shot = ScreenCaptureAreaParameter.CreateParameter();
        Assert.Equal(shot.Name, area.Name);
        Assert.Equal(shot.DefaultValue, area.DefaultValue);
        Assert.Equal(shot.AllowedValues, area.AllowedValues);

        Assert.Equal("interval", command.Parameters[2].Name);
        Assert.Equal(CommandParameterType.Int, command.Parameters[2].Type);
        Assert.Equal("100", command.Parameters[2].DefaultValue);

        Assert.True(Registry().TryGet("RECORDSTOP", out var stop));
        Assert.Empty(stop!.Parameters);
    }

    [Theory]
    [InlineData("RECORDSTART \"film.png\" fps=10", "RECORDSTART has no parameter 'fps'")]
    [InlineData("RECORDSTART \"film.png\" area=screen", "'area' must be one of terminal, window, got 'screen'")]
    [InlineData("RECORDSTART \"film.png\" interval=fast", "'interval' must be a whole number, got 'fast'")]
    [InlineData("RECORDSTART", "missing required parameter 'file'")]
    [InlineData("RECORDSTOP now=1", "RECORDSTOP has no parameter 'now'")]
    public void TheScriptParserRefusesABadLineByName(string line, string expected)
    {
        var parsed = new ScriptParser(Registry()).Parse(line);

        Assert.NotEmpty(parsed.Errors);
        Assert.Contains(expected, parsed.Errors[0].ToString());
    }

    [Fact]
    public void AGoodLineParses()
    {
        var parsed = new ScriptParser(Registry()).Parse("RECORDSTART \"film.png\" area=window interval=50\nRECORDSTOP");
        Assert.Empty(parsed.Errors);
    }

    [Theory]
    [InlineData("19")]
    [InlineData("10001")]
    public async Task AnIntervalOutOfRangeIsRefusedWithoutStarting(string interval)
    {
        int started = 0;
        var registry = Registry(() => started++);
        var args = new CommandArgs(new Dictionary<string, string>
        {
            ["file"] = Path.Combine(Path.GetTempPath(), "retroterm-never-film.png"),
            ["interval"] = interval
        });

        var result = await registry.ExecuteAsync("RECORDSTART", NewSession(), args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("interval=" + interval + " is outside 20 to 10000 milliseconds", result.Error!);
        Assert.Equal(0, started);
    }

    [Fact]
    public async Task AMissingDirectoryIsRefusedWithoutStarting()
    {
        int started = 0;
        var registry = Registry(() => started++);
        var dir = Path.Combine(Path.GetTempPath(), "retroterm-no-such-dir-" + Guid.NewGuid().ToString("N"));
        var args = new CommandArgs(new Dictionary<string, string> { ["file"] = Path.Combine(dir, "film.png") });

        var result = await registry.ExecuteAsync("RECORDSTART", NewSession(), args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("directory not found", result.Error!);
        Assert.Equal(0, started);
    }

    [Fact]
    public async Task RecordStopPassesTheRefusalOn()
    {
        var result = await Registry().ExecuteAsync("RECORDSTOP", NewSession(), CommandArgs.Empty, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("RECORDSTART first", result.Error!);
    }
}
