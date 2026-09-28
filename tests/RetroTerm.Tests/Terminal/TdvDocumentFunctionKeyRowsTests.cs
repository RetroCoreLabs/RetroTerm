using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The keyboard document's ND function-key rows must agree with the registry.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, and what it would have caught</b></para>
/// <c>TdvKeyboardDocumentMatchesTheRegistryTests</c> guards the same document, but only for the
/// arrows and HOME. On 2 September 2026, measuring F1 against a live D100, two function-key rows
/// turned out to have been wrong the whole time and to contradict the document's own summary line:
/// G53 HJELP was listed at VK 112 sending <c>ESC[28~</c>, and F51 as "PUSH SI" at VK 0. The
/// registry has HJELP at VK 0 sending <c>ESC[46_</c>, and F51 as the terminal's own F1 at VK 112
/// sending <c>ESC[50_</c> - which is what the live machine received.
/// <para>
/// <c>ESC[28~</c> is the VT220 <c>ESC[nn~</c> shape the document's header says was swept out on
/// 27 August, and it is character for character the VT220 F15 row in this document's own
/// comparison table. A copied row, in other words, and nothing was watching these positions.
/// </para>
///
/// <para><b>Why it does not simply ban the VT220 shape</b></para>
/// The document carries a legitimate VT220 table for contrast, so <c>ESC[nn~</c> is expected in
/// it. The check has to be per GRID POSITION: a row naming a grid the registry knows must give
/// that grid's sequence, whatever else the document says elsewhere.
/// </remarks>
public class TdvDocumentFunctionKeyRowsTests
{
    private static string DocumentPath => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(TdvDocumentFunctionKeyRowsTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", "..", "docs", "TDV-KEYBOARD-COMPLETE-REFERENCE.md"));

    /// <summary>
    /// Removes spaces and backticks so <c>ESC [ 46 _</c> and <c>ESC[46_</c> compare equal.
    /// </summary>
    /// <param name="text">
    /// The text to flatten.
    /// </param>
    /// <returns>
    /// The text with spaces and backticks gone.
    /// </returns>
    private static string Flatten(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch != ' ' && ch != '`')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void EveryNdFunctionKeyRowInTheDocumentGivesTheRegistrysSequence()
    {
        if (!File.Exists(DocumentPath))
        {
            return;
        }

        string[] lines = File.ReadAllLines(DocumentPath);
        var failures = new List<string>();
        int rowsChecked = 0;

        var registry = TDV2200KeyRegistry.AllKeys;
        foreach (var pair in registry)
        {
            string grid = pair.Key;
            string? extended = TDV2200KeyRegistry.GetSequence(grid, extendedMode: true, numPadFuncMode: false);

            // Only the ND function-key form, which is what this document kept getting wrong.
            if (string.IsNullOrEmpty(extended) || extended!.Length < 3) continue;
            if (extended[0] != (char)0x1B || extended[1] != '[' || extended[extended.Length - 1] != '_') continue;

            // "ESC[46_" as the document would write it, spaces removed.
            string wanted = "ESC" + extended.Substring(1);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // A table row naming this grid in its FIRST column. Anything else - prose, the
                // VT220 comparison table - is not this grid's row and is left alone.
                if (!line.StartsWith("| " + grid + " |", StringComparison.Ordinal)) continue;

                // Only rows that actually STATE an escape sequence. The document's tables are not
                // one shape: several carry a plain character in the last column ("0", "="), or a
                // word like "national", or a note. Those rows describe something other than the
                // sequence and comparing them would be nonsense.
                if (line.IndexOf("ESC", StringComparison.Ordinal) < 0) continue;

                rowsChecked++;
                string flat = Flatten(line);
                if (!flat.Contains(Flatten(wanted), StringComparison.Ordinal))
                {
                    failures.Add("line " + (i + 1) + ": grid " + grid + " should give " + wanted
                        + " but the row reads: " + line.Trim());
                }
            }
        }

        Assert.True(rowsChecked > 0,
            "no grid rows were found at all - the document's table shape must have changed, and "
            + "this guard is silently checking nothing");

        Assert.True(failures.Count == 0,
            failures.Count + " document row(s) disagree with the registry, which is the source of "
            + "truth:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void HjelpAndF1AreTheTwoDifferentKeysTheyActuallyAre()
    {
        // Written out rather than read from the registry on both sides, so that changing the
        // REGISTRY also fails here and has to be a deliberate act. Both values were measured on a
        // live D100 on 1 September 2026, F1 through the real key path.
        Assert.True(TDV2200KeyRegistry.TryGetKey("G53", out var hjelp));
        Assert.Equal("HJELP", hjelp.Name);
        Assert.Equal(0, hjelp.VirtualKeyCode);

        Assert.True(TDV2200KeyRegistry.TryGetKey("F51", out var f1));
        Assert.Equal("F1", f1.Name);
        Assert.Equal(112, f1.VirtualKeyCode);
    }
}
