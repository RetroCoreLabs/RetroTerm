using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace RetroTerm.Tests.Configuration;

/// <summary>
/// No part of the shipping program may take its configuration from an environment variable.
/// </summary>
/// <remarks>
/// <para><b>Ronny's instruction, 27 August 2026</b></para>
/// "dont use environment variables for anything inside the app. if you need configuration, we have a
/// standard configuration window."
/// <para><b>What was actually there</b></para>
/// <c>RETROTERM_MCP_PORT</c> overrode the MCP port set in Preferences. So the Preferences dialog could
/// show one port while the server listened on another, with nothing on screen to explain the
/// difference - and the person who set the variable is rarely the person reading the dialog. Deleted
/// the same day.
/// <para><b>Why a test and not a note</b></para>
/// A note is read by whoever goes looking. Reading an environment variable is one line, it always
/// looks harmless at the moment it is written, and nothing else in the build would ever object.
/// </remarks>
public class NoEnvironmentVariableConfigurationTests
{
    /// <summary>
    /// The source tree, resolved from the assembly rather than the working directory.
    /// </summary>
    private static string SourceFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(NoEnvironmentVariableConfigurationTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", "..", "src"));

    /// <summary>
    /// The ways a program can read an environment variable.
    /// </summary>
    private static readonly string[] Readers =
    {
        "GetEnvironmentVariable",
        "GetEnvironmentVariables",
        "ExpandEnvironmentVariables",
    };

    [Fact]
    public void NothingInTheShippingCodeReadsAnEnvironmentVariable()
    {
        Assert.True(Directory.Exists(SourceFolder),
            "could not find the source tree at " + SourceFolder);

        string[] files = Directory.GetFiles(SourceFolder, "*.cs", SearchOption.AllDirectories);
        var offenders = new List<string>();

        for (int i = 0; i < files.Length; i++)
        {
            // bin and obj hold generated copies of the same code; counting them would report every
            // finding twice and point at a path nobody edits.
            if (files[i].Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                || files[i].Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(files[i]);
            for (int line = 0; line < lines.Length; line++)
            {
                for (int r = 0; r < Readers.Length; r++)
                {
                    if (lines[line].IndexOf(Readers[r], StringComparison.Ordinal) < 0) continue;

                    // A comment saying one was REMOVED is the point of the comment, not a breach.
                    string trimmed = lines[line].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    offenders.Add(files[i].Substring(SourceFolder.Length + 1)
                        + ":" + (line + 1) + "  " + trimmed);
                }
            }
        }

        var message = new StringBuilder();
        message.Append("configuration belongs in Preferences, not in the environment. Found:\n");
        for (int i = 0; i < offenders.Count; i++)
        {
            message.Append("  ").Append(offenders[i]).Append('\n');
        }

        Assert.True(offenders.Count == 0, message.ToString());
    }
}
