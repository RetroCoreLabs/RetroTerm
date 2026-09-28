using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// Every command that exists has a row in the command table of docs\MCP-AND-SCRIPTING.md.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, 27 August 2026</b></para>
/// The table had gone stale without anybody noticing: <c>SCREENSHOT</c> shipped and was not in it.
/// That is the same failure this repository has had three times over - a document that describes the
/// code, drifting away from the code, and being believed anyway. The registry generates the tool
/// list and the built-in help, so those cannot drift; the prose table is written by hand and can.
/// <para><b>Why the SOURCE and not the registry</b></para>
/// Reading the registry would need every command's injected dependency stood up - the renderer, the
/// gateway, the Kermit options, the window. A command that exists but is registered only from the
/// desktop would then be invisible to this check, which is exactly the kind of gap that lets one
/// slip out undocumented. Walking the source finds every command whatever wires it.
/// <para><b>What it does not check</b></para>
/// That the row is CORRECT. Only that it is there. A wrong description is a thing a reader can
/// report; a missing one is a thing nobody knows to look for.
/// </remarks>
public class CommandsAreDocumentedTests
{
    /// <summary>
    /// The repository root, resolved from the assembly rather than the working directory.
    /// </summary>
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(CommandsAreDocumentedTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", ".."));

    /// <summary>
    /// The marker every command class carries: its name, as both surfaces spell it.
    /// </summary>
    private const string NameMarker = "public string Name => \"";

    /// <summary>
    /// Finds every command name declared anywhere in the source tree.
    /// </summary>
    /// <returns>
    /// The names, in no particular order.
    /// </returns>
    private static List<string> FindEveryCommandName()
    {
        var found = new List<string>();
        string source = Path.Combine(RepositoryRoot, "src");

        Assert.True(Directory.Exists(source), "could not find the source tree at " + source);

        string[] files = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            // bin and obj hold generated copies of the same code.
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
                int at = lines[line].IndexOf(NameMarker, StringComparison.Ordinal);
                if (at < 0) continue;

                int start = at + NameMarker.Length;
                int close = lines[line].IndexOf('"', start);
                if (close <= start) continue;

                string name = lines[line].Substring(start, close - start);

                // A command name is upper case with no spaces. Anything else on this pattern is
                // some other class that happens to have a Name property.
                bool looksLikeACommand = true;
                for (int c = 0; c < name.Length; c++)
                {
                    char ch = name[c];
                    if (ch >= 'A' && ch <= 'Z') continue;
                    if (ch == '_') continue;
                    looksLikeACommand = false;
                    break;
                }

                if (looksLikeACommand && name.Length > 1 && !found.Contains(name))
                {
                    found.Add(name);
                }
            }
        }

        return found;
    }

    [Fact]
    public void EveryCommandHasARowInTheScriptingDocument()
    {
        string doc = Path.Combine(RepositoryRoot, "docs", "MCP-AND-SCRIPTING.md");
        Assert.True(File.Exists(doc), "could not find " + doc);

        string text = File.ReadAllText(doc);
        var names = FindEveryCommandName();

        // A sanity floor. If the source walk found almost nothing, the marker changed and this test
        // would pass by finding no work to do - the shape of a green test that checks nothing.
        Assert.True(names.Count >= 25,
            "only found " + names.Count + " commands in the source - the search pattern has probably "
            + "gone stale, and a passing result here would mean nothing");

        var undocumented = new List<string>();
        for (int i = 0; i < names.Count; i++)
        {
            // The table spells each command in backticks, so that is what is looked for rather than
            // the bare word - "RESET" appears in ordinary prose all over the document.
            if (text.IndexOf("`" + names[i] + "`", StringComparison.Ordinal) < 0)
            {
                undocumented.Add(names[i]);
            }
        }

        Assert.True(undocumented.Count == 0,
            "these commands exist but are not in the command table of docs\\MCP-AND-SCRIPTING.md: "
            + string.Join(", ", undocumented));
    }
}
