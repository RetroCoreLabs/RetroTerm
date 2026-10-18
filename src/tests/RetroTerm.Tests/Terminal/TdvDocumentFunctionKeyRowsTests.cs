using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Every table row in the keyboard document that names a grid position must agree with the registry.
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
/// 27 August, and it was character for character the VT220 F15 row in a comparison table the
/// document used to carry. A copied row, in other words, and nothing was watching these positions.
/// </para>
///
/// <para><b>Widened on 29 September 2026 from the ND function keys to every grid row</b></para>
/// The first version checked only rows stating an <c>ESC[nn_</c> sequence, and only that the key's
/// normal sequence appeared. Going through the document row by row against the registry that day
/// found some thirty more rows it could not see: KOPI and FLYTT at VK 67 and 88 (registry: 0), SLUTT
/// at VK 0 (registry: 35, the PC End key), D13 as "DEL, VK 8" (registry: LF, VK 10), A47 as numpad 0
/// (registry: TABLEFT, <c>ESC[38_</c>), C47 and C48 as ERASE PAGE and ERASE LINE (registry:
/// FIELDLEFT and the UP arrow), F52 and F53 as "HEX SO" and "CLEAR" (registry: F2 and F3), a numeric
/// pad with a plus key it does not have, and <c>ESC[D</c> written beside the real <c>0x08</c> for
/// the left arrow. So the check now reads the VK column and every <c>0xNN</c> byte as well.
///
/// <para><b>How it finds the rows</b></para>
/// A Markdown table's header row is the line before a <c>|---|</c> separator. The header names the
/// grid column ("Grid Pos" or "Position") and the VK column ("VK" or "VK Code"), so the check does
/// not depend on every table having the same shape - the document's tables never did. A table with
/// no grid column (the C0 control codes, the xterm modifier rows, the soft keys) is not about grid
/// positions and is left alone.
///
/// <para><b>Why it does not simply ban the VT220 shape</b></para>
/// The document carries a legitimate xterm modifier table for contrast, so <c>ESC[1;2A</c> is
/// expected in it. The check is per GRID POSITION: a row naming a grid the registry knows must give
/// that grid's VK code and only that grid's sequences, whatever else the document says elsewhere.
///
/// <para><b>Why the registry is read on one side only</b></para>
/// The document is the other side, so this cannot agree with itself the way a test reading the
/// registry for both sides would. Changing the registry fails here until the document is updated,
/// which is the point: the document exists to be read by people building other terminals.
/// </remarks>
public class TdvDocumentFunctionKeyRowsTests
{
    private static string DocumentPath => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(TdvDocumentFunctionKeyRowsTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", "..", "docs", "TDV-KEYBOARD-COMPLETE-REFERENCE.md"));

    /// <summary>
    /// A grid position as the registry writes it: one row letter A to G and one or two digits.
    /// </summary>
    private static readonly Regex GridId = new Regex(@"^[A-G]\d{1,2}$", RegexOptions.Compiled);

    /// <summary>
    /// A TDV key sequence as the document writes it once spaces and backticks are gone.
    /// </summary>
    private static readonly Regex TdvSequence = new Regex(@"ESC\[(\d+)_", RegexOptions.Compiled);

    /// <summary>
    /// A single byte written as hex, the way the document writes the C0 codes.
    /// </summary>
    private static readonly Regex HexByte = new Regex(@"0x([0-9A-Fa-f]{2})", RegexOptions.Compiled);

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

    /// <summary>
    /// Splits a Markdown table row into its trimmed cells, dropping the empty ends.
    /// </summary>
    /// <param name="line">
    /// The row, starting and ending with a pipe.
    /// </param>
    /// <returns>
    /// The cells in order.
    /// </returns>
    private static List<string> Cells(string line)
    {
        string[] raw = line.Split('|');
        var cells = new List<string>(raw.Length);

        // The first and last entries are the empty text outside the outer pipes.
        for (int i = 1; i < raw.Length - 1; i++)
        {
            cells.Add(raw[i].Trim());
        }

        return cells;
    }

    /// <summary>
    /// Whether a line is the <c>|---|---|</c> separator under a table header.
    /// </summary>
    /// <param name="line">
    /// The line to test.
    /// </param>
    /// <returns>
    /// True when it is made of nothing but pipes, dashes, colons and spaces.
    /// </returns>
    private static bool IsSeparator(string line)
    {
        if (line.IndexOf("---", StringComparison.Ordinal) < 0) return false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch != '|' && ch != '-' && ch != ':' && ch != ' ')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The index of the first header cell containing one of the given words, or -1.
    /// </summary>
    /// <param name="header">
    /// The header cells.
    /// </param>
    /// <param name="words">
    /// The words that mark the column wanted.
    /// </param>
    /// <returns>
    /// The column index, or -1 when no header cell names it.
    /// </returns>
    private static int ColumnNamed(List<string> header, string[] words)
    {
        for (int c = 0; c < header.Count; c++)
        {
            for (int w = 0; w < words.Length; w++)
            {
                if (header[c].IndexOf(words[w], StringComparison.Ordinal) >= 0)
                {
                    return c;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The leading integer of a cell such as <c>112 (VK_F1)</c>, or -1 when it does not start with digits.
    /// </summary>
    /// <param name="cell">
    /// The VK cell.
    /// </param>
    /// <returns>
    /// The number, or -1.
    /// </returns>
    private static int LeadingNumber(string cell)
    {
        int value = 0;
        int digits = 0;
        for (int i = 0; i < cell.Length; i++)
        {
            char ch = cell[i];
            if (ch < '0' || ch > '9') break;
            value = value * 10 + (ch - '0');
            digits++;
        }

        return digits == 0 ? -1 : value;
    }

    /// <summary>
    /// Turns a registry sequence such as ESC [ 4 6 _ into the <c>ESC[46_</c> form the document uses.
    /// </summary>
    /// <param name="sequence">
    /// The registry's sequence, or null.
    /// </param>
    /// <returns>
    /// The document form, or null when the sequence is null or is not a CSI-underscore sequence.
    /// </returns>
    private static string? AsDocumentSequence(string? sequence)
    {
        if (sequence == null || sequence.Length < 3) return null;
        if (sequence[0] != (char)0x1B || sequence[1] != '[' || sequence[sequence.Length - 1] != '_') return null;

        return "ESC" + sequence.Substring(1);
    }

    /// <summary>
    /// Adds a one-byte registry value to the set of bytes a row may state as <c>0xNN</c>.
    /// </summary>
    /// <param name="allowed">
    /// The set being built.
    /// </param>
    /// <param name="sequence">
    /// A registry sequence, or null.
    /// </param>
    private static void AddByte(HashSet<int> allowed, string? sequence)
    {
        if (sequence != null && sequence.Length == 1)
        {
            allowed.Add(sequence[0]);
        }
    }

    [Fact]
    public void EveryGridRowInTheDocumentAgreesWithTheRegistry()
    {
        Assert.True(File.Exists(DocumentPath), "could not find " + DocumentPath);

        string[] lines = File.ReadAllLines(DocumentPath);
        var failures = new List<string>();
        int rowsChecked = 0;
        int vkCodesChecked = 0;
        int sequencesChecked = 0;
        int bytesChecked = 0;

        // The columns of the table currently being read. -1 outside a table, or in a table that
        // has no grid column and is therefore not about grid positions.
        int gridColumn = -1;
        int vkColumn = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                // Any non-table line ends the table.
                gridColumn = -1;
                vkColumn = -1;
                continue;
            }

            if (IsSeparator(line)) continue;

            bool isHeader = i + 1 < lines.Length && IsSeparator(lines[i + 1]);
            var cells = Cells(line);

            if (isHeader)
            {
                gridColumn = ColumnNamed(cells, new[] { "Grid", "Position" });
                vkColumn = ColumnNamed(cells, new[] { "VK" });
                continue;
            }

            if (gridColumn < 0 || gridColumn >= cells.Count) continue;

            string grid = cells[gridColumn];
            if (!GridId.IsMatch(grid)) continue;

            if (!TDV2200KeyRegistry.TryGetKey(grid, out var key))
            {
                failures.Add("line " + (i + 1) + ": grid " + grid + " is not in the registry: " + line.Trim());
                continue;
            }

            rowsChecked++;

            // The VK code, when the table has a VK column and the cell starts with a number.
            // Cells such as "varies" or an em dash make no claim and are left alone.
            if (vkColumn >= 0 && vkColumn < cells.Count)
            {
                int statedVk = LeadingNumber(cells[vkColumn]);
                if (statedVk >= 0)
                {
                    vkCodesChecked++;
                    if (statedVk != key!.VirtualKeyCode)
                    {
                        failures.Add("line " + (i + 1) + ": grid " + grid + " (" + key.Name + ") has VK "
                            + key.VirtualKeyCode + " in the registry but the row says " + statedVk + ": " + line.Trim());
                    }
                }
            }

            string flat = Flatten(line);

            // Every ESC[nn_ the row states must be one of this key's sequences, and if the row
            // states any, the key's normal one (or its pad-function one, for the numeric pad) must
            // be among them. A programmable PUSH key has no sequences at all, so any ESC[nn_ on
            // its row is wrong by construction.
            var allowedSequences = new HashSet<string>(StringComparer.Ordinal);
            string? normal = AsDocumentSequence(key!.ExtNormal);
            string? shift = AsDocumentSequence(key.ExtShift);
            string? ctrl = AsDocumentSequence(key.ExtCtrl);
            string? pad = AsDocumentSequence(key.NumPadFunc);
            if (normal != null) allowedSequences.Add(normal);
            if (shift != null) allowedSequences.Add(shift);
            if (ctrl != null) allowedSequences.Add(ctrl);
            if (pad != null) allowedSequences.Add(pad);

            var stated = TdvSequence.Matches(flat);
            bool normalPresent = false;
            for (int m = 0; m < stated.Count; m++)
            {
                sequencesChecked++;
                string token = stated[m].Value;

                if ((key.Flags & TDVKeyFlags.IsProgrammable) != 0)
                {
                    failures.Add("line " + (i + 1) + ": grid " + grid + " is a programmable PUSH key with no fixed "
                        + "sequence, but the row states " + token + ": " + line.Trim());
                    continue;
                }

                if (!allowedSequences.Contains(token))
                {
                    failures.Add("line " + (i + 1) + ": grid " + grid + " (" + key.Name + ") never sends " + token
                        + "; the registry gives " + (normal ?? pad ?? "no CSI sequence") + ": " + line.Trim());
                }

                if (token == normal || (normal == null && token == pad)) normalPresent = true;
            }

            if (stated.Count > 0 && !normalPresent && (key.Flags & TDVKeyFlags.IsProgrammable) == 0)
            {
                failures.Add("line " + (i + 1) + ": grid " + grid + " (" + key.Name + ") states a sequence but not "
                    + "its own, which is " + (normal ?? pad) + ": " + line.Trim());
            }

            // Every "ESC[" on a grid row must be the start of a TDV sequence. A TDV key never sends
            // any other CSI shape, so ESC[D beside the left arrow's real 0x08, or ESC[28~ on a
            // function key, is wrong whatever else the row says. Proven necessary on 29 September
            // 2026: with only the ESC[nn_ check, "| B47 | 37 | Navigation | <- | `ESC[D` / `0x08` |"
            // - the exact row the nd-120 session was misled by - passed.
            int escapesStated = 0;
            int at = 0;
            while ((at = flat.IndexOf("ESC[", at, StringComparison.Ordinal)) >= 0)
            {
                escapesStated++;
                at += 4;
            }

            if (escapesStated > stated.Count)
            {
                failures.Add("line " + (i + 1) + ": grid " + grid + " (" + key.Name + ") states an escape sequence "
                    + "that is not a TDV ESC[nn_ sequence - a TDV key never sends any other CSI shape: " + line.Trim());
            }

            // Every 0xNN the row states must be a byte this key really sends: its simple-ASCII
            // (2115 mode) byte, or its one-byte extended sequence for the AlwaysSameCode keys.
            var allowedBytes = new HashSet<int>();
            AddByte(allowedBytes, key.SimpleAscii);
            AddByte(allowedBytes, key.ExtNormal);
            AddByte(allowedBytes, key.ExtShift);

            var hex = HexByte.Matches(flat);
            for (int m = 0; m < hex.Count; m++)
            {
                bytesChecked++;
                int value = Convert.ToInt32(hex[m].Groups[1].Value, 16);
                if (!allowedBytes.Contains(value))
                {
                    failures.Add("line " + (i + 1) + ": grid " + grid + " (" + key.Name + ") never sends byte "
                        + hex[m].Value + "; the registry gives "
                        + (key.SimpleAscii == null ? "no 2115-mode byte" : "0x" + ((int)key.SimpleAscii[0]).ToString("X2"))
                        + ": " + line.Trim());
                }
            }
        }

        // The document has well over a hundred grid rows. A count anywhere near zero means the
        // table shape changed and this guard is silently checking nothing.
        Assert.True(rowsChecked >= 100,
            "only " + rowsChecked + " grid rows were found - the document's table shape must have changed, and "
            + "this guard is silently checking nothing");
        Assert.True(vkCodesChecked >= 100, "only " + vkCodesChecked + " VK codes were found to check");
        Assert.True(sequencesChecked >= 60, "only " + sequencesChecked + " ESC[nn_ sequences were found to check");
        Assert.True(bytesChecked >= 30, "only " + bytesChecked + " 0xNN bytes were found to check");

        Assert.True(failures.Count == 0,
            failures.Count + " document row(s) disagree with the registry, which is the source of "
            + "truth:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryRegistryKeyWithASequenceHasARowInTheDocument()
    {
        // The other direction: a key the registry knows and the document never mentions is a gap a
        // reader cannot see. F47 TAB, F48 SEARCH, F49 REPLACE, E47 to E49, E51 to E54 (F5 to F8)
        // and D54 KPSPACE were all missing from the grid reference until 29 September 2026.
        Assert.True(File.Exists(DocumentPath), "could not find " + DocumentPath);

        string text = File.ReadAllText(DocumentPath);
        var missing = new List<string>();

        var grids = new List<string>(TDV2200KeyRegistry.AllKeys.Keys);
        for (int i = 0; i < grids.Count; i++)
        {
            Assert.True(TDV2200KeyRegistry.TryGetKey(grids[i], out var key));

            bool hasSequence = key!.ExtNormal != null || key.NumPadFunc != null || key.SimpleAscii != null;
            if (!hasSequence) continue;

            if (text.IndexOf("| " + grids[i] + " |", StringComparison.Ordinal) < 0)
            {
                missing.Add(grids[i] + " (" + key.Name + ")");
            }
        }

        Assert.True(missing.Count == 0,
            "registry keys with a sequence that have no row in the keyboard reference: "
            + string.Join(", ", missing));
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
