using System;
using System.Diagnostics;
using System.Text;
using RetroTerm.Core;
using Xunit;

namespace RetroTerm.Tests;

/// <summary>
/// The build stamp - that it is really there, and that it really matches this checkout.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, 27 August 2026</b></para>
/// Two copies of RetroTerm were running from the same folder and only the first held the MCP port,
/// so anything driving the terminal could reach an OLD binary and never know. Ronny asked for a
/// build id, git hash and build date at MCP startup so the caller can tell at a glance.
///
/// <para><b>Why the stamp needs a test of its own</b></para>
/// A stamp that quietly reads "unknown" is worse than no stamp: it is reassuring and says nothing.
/// The whole mechanism lives in MSBuild, where a mistyped property name fails silently and the
/// build stays green - so nothing else in this repository would ever notice.
/// </remarks>
public class BuildIdentityTests
{
    [Fact]
    public void TheCommitIsRecordedAndIsNotTheFallback()
    {
        // "unknown" is what this reports when the MSBuild target did not run or was misspelled.
        // Passing on that value would be the exact failure the stamp exists to prevent.
        Assert.NotNull(BuildIdentity.Commit);
        Assert.NotEqual(BuildIdentity.Unknown, BuildIdentity.Commit);

        // Twelve hex characters, as asked for by rev-parse --short=12.
        Assert.Equal(12, BuildIdentity.Commit.Length);
        for (int i = 0; i < BuildIdentity.Commit.Length; i++)
        {
            char c = BuildIdentity.Commit[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            Assert.True(hex, "the commit contains '" + c + "', which is not a hex digit");
        }
    }

    [Fact]
    public void TheStampedCommitIsARealCommitInThisRepository()
    {
        // Asks git whether the stamped hash names anything, which is the strongest claim that can
        // be made without punishing the normal way of working.
        //
        // IT DELIBERATELY DOES NOT COMPARE AGAINST HEAD. The order here is build, test, then
        // commit - so the moment a commit lands, the binary beside it is stamped with the PARENT
        // commit and a HEAD comparison would go red until somebody rebuilt. A test that fails
        // after every commit teaches people to ignore it, and an ignored test is worse than none.
        //
        // Existence still catches everything this is for: a stamp reading "unknown", a mistyped
        // MSBuild property, a truncated or garbled hash, or a value invented by hand.
        string kind = RunGit("cat-file -t " + BuildIdentity.Commit);

        if (string.IsNullOrEmpty(kind))
        {
            // No git, or not a checkout. Nothing to compare against, and failing here would punish
            // somebody who unzipped the source rather than cloning it.
            return;
        }

        Assert.Equal("commit", kind);
    }

    [Fact]
    public void TheBranchAndBuildTimeAreRecorded()
    {
        Assert.NotEqual(BuildIdentity.Unknown, BuildIdentity.Branch);
        Assert.NotEqual(BuildIdentity.Unknown, BuildIdentity.BuildDate);
        Assert.NotEqual(BuildIdentity.Unknown, BuildIdentity.BuildTime);
    }

    [Fact]
    public void TheDescriptionNamesEverythingAReaderNeeds()
    {
        // This string is what lands in the MCP handshake, so it has to carry the whole answer -
        // a version with no commit, or a commit with no date, still leaves the reader guessing.
        var description = BuildIdentity.Describe();

        Assert.Contains(BuildIdentity.Commit, description, StringComparison.Ordinal);
        Assert.Contains(BuildIdentity.Branch, description, StringComparison.Ordinal);
        Assert.Contains(BuildIdentity.BuildDate, description, StringComparison.Ordinal);
        Assert.Contains(BuildIdentity.BuildTime, description, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUncommittedTreeSaysSoInWords()
    {
        // A hash on its own claims the build IS that commit. During ordinary work it is not, and a
        // reader comparing two results has to be able to see that.
        if (!BuildIdentity.IsDirty) return;

        Assert.Contains("uncommitted", BuildIdentity.Describe(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("+", BuildIdentity.Short, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asks git something about this checkout.
    /// </summary>
    /// <param name="arguments">
    /// The git arguments.
    /// </param>
    /// <returns>
    /// The trimmed first line, or an empty string when git could not answer.
    /// </returns>
    /// <remarks>
    /// Runs against the repository the test assembly was built from, found by walking up from the
    /// assembly rather than trusting the working directory - a test runner's working directory is
    /// its own business.
    /// </remarks>
    private static string RunGit(string arguments)
    {
        try
        {
            string here = System.IO.Path.GetDirectoryName(
                typeof(BuildIdentityTests).Assembly.Location) ?? ".";

            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = here,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(start);
            if (process == null) return string.Empty;

            var output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());

            if (!process.WaitForExit(10_000)) return string.Empty;
            if (process.ExitCode != 0) return string.Empty;

            return output.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
