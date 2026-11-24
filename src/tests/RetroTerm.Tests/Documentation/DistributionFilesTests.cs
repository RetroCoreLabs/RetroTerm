//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace RetroTerm.Tests.Documentation;

/// <summary>
/// The three files a RetroTerm distribution carries beside the program - README.md (from
/// docs/DISTRIBUTION-README.md), .mcp.json (from docs/distribution-mcp.json) and
/// add-mcp-to-claude-code.ps1 - and the two things that publish them.
/// </summary>
/// <remarks>
/// <para><b>BUGS.md B18</b></para>
/// The script ran <c>claude mcp add</c> without <c>--scope user</c>, so it registered the server
/// for the one folder it ran in. Claude Code's own help (<c>claude mcp add --help</c>, 2.1.331)
/// says the default scope is "local".
/// <para><b>BUGS.md B19</b></para>
/// The README promised a folder holding .mcp.json and the script; neither build-release.bat nor
/// the release workflow copied them, or the README. These tests read the two publishers' text
/// because neither can be run from a test: the bat kills every RetroTerm by name, and the
/// workflow runs on GitHub.
/// </remarks>
public class DistributionFilesTests
{
    /// <summary>
    /// The src folder, resolved from the assembly rather than the working directory.
    /// </summary>
    private static string SourceRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(DistributionFilesTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", ".."));

    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(SourceRoot, ".."));

    private static string ReadRepoFile(params string[] parts)
    {
        var path = Path.Combine(RepositoryRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), "missing: " + path);
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheScriptRegistersTheServerForEveryProject()
    {
        var script = ReadRepoFile("src", "tools", "add-mcp-to-claude-code.ps1");
        var lines = script.Split('\n');
        int adds = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("claude mcp add", StringComparison.Ordinal))
            {
                adds++;
                Assert.Contains("--scope user", line, StringComparison.Ordinal);
                Assert.Contains("--transport http", line, StringComparison.Ordinal);
            }
        }
        Assert.Equal(1, adds);
        Assert.Contains("\"http://127.0.0.1:5715/mcp\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMcpJsonPointsAtTheDefaultServer()
    {
        using var doc = JsonDocument.Parse(ReadRepoFile("docs", "distribution-mcp.json"));
        var server = doc.RootElement.GetProperty("mcpServers").GetProperty("retroterm");
        Assert.Equal("http", server.GetProperty("type").GetString());
        Assert.Equal("http://127.0.0.1:5715/mcp", server.GetProperty("url").GetString());
    }

    [Fact]
    public void TheLocalReleaseScriptCopiesTheThreeFiles()
    {
        var bat = ReadRepoFile("src", "tools", "build-release.bat");
        Assert.Contains("copy /Y \"..\\docs\\DISTRIBUTION-README.md\" \"%PUBLISH_DIR%\\README.md\"", bat, StringComparison.Ordinal);
        Assert.Contains("copy /Y \"..\\docs\\distribution-mcp.json\" \"%PUBLISH_DIR%\\.mcp.json\"", bat, StringComparison.Ordinal);
        Assert.Contains("copy /Y \".\\tools\\add-mcp-to-claude-code.ps1\" \"%PUBLISH_DIR%\\add-mcp-to-claude-code.ps1\"", bat, StringComparison.Ordinal);
        // No control byte - a heredoc once turned \b and \a in these lines into BS and BEL.
        for (int i = 0; i < bat.Length; i++)
        {
            char c = bat[i];
            Assert.False(c < ' ' && c != '\r' && c != '\n' && c != '\t', "control byte " + (int)c + " at offset " + i);
        }
    }

    [Fact]
    public void TheReleaseWorkflowPutsTheThreeFilesInEveryDownload()
    {
        var yml = ReadRepoFile(".github", "workflows", "build.yml");
        Assert.Contains("cp docs/DISTRIBUTION-README.md out/dist/README.md", yml, StringComparison.Ordinal);
        Assert.Contains("cp docs/distribution-mcp.json out/dist/.mcp.json", yml, StringComparison.Ordinal);
        Assert.Contains("cp src/tools/add-mcp-to-claude-code.ps1 out/dist/add-mcp-to-claude-code.ps1", yml, StringComparison.Ordinal);
        // The check step that fails the job when a download lacks one of them.
        Assert.Contains("for f in README.md .mcp.json add-mcp-to-claude-code.ps1; do", yml, StringComparison.Ordinal);
        Assert.Contains("artifacts/RetroTerm-win-x64/RetroTerm-win-x64.zip", yml, StringComparison.Ordinal);
    }
}
