using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace RetroTerm.Tests.Avalonia.ManualPlan;

/// <summary>
/// The by-hand test documents must not contradict each other.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b></para>
/// Written 27 August 2026, the day both of the faults it checks for cost real time.
/// Ronny was asked to judge case M5.1a and answered it - for the second time. He had already
/// answered the same question on 25 August, and his answer was already written into
/// <c>M5-TEKTRONIX.md</c>, including the note that my prediction had been wrong. The run sheet
/// listed the case as outstanding anyway, and <c>RUN-SHEET.md</c> recorded it as PASS ninety lines
/// further down the SAME file.
/// <para><b>The second fault, found in the same minute</b></para>
/// <c>M6-SIXEL-AND-REGIS.md</c> had two different cases both numbered M6.5 - the phosphor case and
/// the graphics-input case. Anybody following the run sheet to "M6.5" would have found whichever
/// one they reached first.
/// <para><b>What a machine can and cannot say here</b></para>
/// It cannot judge a case. It CAN say that a document claims a case is outstanding while another
/// document records that it passed, and it can say that one case number means two things. Both of
/// those are contradictions inside our own paperwork, and both were paid for by hand today.
/// </remarks>
public class ManualTestDocumentConsistencyTests
{
    /// <summary>
    /// Where the by-hand documents live, resolved from the assembly so it points at the source tree.
    /// </summary>
    private static string DocsFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(ManualTestDocumentConsistencyTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", "..", "docs", "manual-tests"));

    /// <summary>
    /// A case heading and the result recorded under it.
    /// </summary>
    /// <remarks>
    /// A struct rather than a class: there are a few dozen of these and they never outlive the test.
    /// </remarks>
    private readonly struct RecordedCase
    {
        /// <summary>
        /// The case identifier, for example <c>M5.1a</c>.
        /// </summary>
        public readonly string Id;

        /// <summary>
        /// The file it was found in.
        /// </summary>
        public readonly string File;

        /// <summary>
        /// The text of its Result line, or an empty string when there is none.
        /// </summary>
        public readonly string Result;

        /// <summary>
        /// Builds one.
        /// </summary>
        /// <param name="id">
        /// The case identifier.
        /// </param>
        /// <param name="file">
        /// The file it was found in.
        /// </param>
        /// <param name="result">
        /// The recorded result line.
        /// </param>
        public RecordedCase(string id, string file, string result)
        {
            Id = id;
            File = file;
            Result = result;
        }

        /// <summary>
        /// Whether this case is finished and should no longer be asked of anybody.
        /// </summary>
        /// <remarks>
        /// A result of PART, PARTLY or OPEN is deliberately NOT finished. M6.1a is recorded "PART -
        /// background defect FIXED; a colour skew found underneath it" and is genuinely still open;
        /// so is M7.1, which ran against one program out of many. Only an unqualified pass counts.
        /// </remarks>
        public bool IsFinished
        {
            get
            {
                if (Result.Length == 0) return false;

                string upper = Result.ToUpperInvariant();
                if (upper.Contains("PART")) return false;
                if (upper.Contains("OPEN")) return false;

                return upper.Contains("PASS");
            }
        }
    }

    /// <summary>
    /// Reads every case heading and its recorded result out of the M-documents.
    /// </summary>
    /// <returns>
    /// One entry per case heading found, in file order.
    /// </returns>
    private static List<RecordedCase> ReadCases()
    {
        var found = new List<RecordedCase>();
        var heading = new Regex(@"^#{2,3}\s+(M\d+\.\w+)\b", RegexOptions.Compiled);
        var result = new Regex(@"^\*\*Result:\*\*\s*(.*)$", RegexOptions.Compiled);

        string[] files = Directory.GetFiles(DocsFolder, "M*.md");
        for (int f = 0; f < files.Length; f++)
        {
            string[] lines = File.ReadAllLines(files[f]);
            string name = Path.GetFileName(files[f]);

            for (int i = 0; i < lines.Length; i++)
            {
                var m = heading.Match(lines[i]);
                if (!m.Success) continue;

                // Look forward for this case's Result line, stopping at the next case so a section
                // without one cannot borrow the answer belonging to the section below it.
                string text = string.Empty;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (heading.IsMatch(lines[j])) break;

                    var r = result.Match(lines[j]);
                    if (r.Success)
                    {
                        text = r.Groups[1].Value.Replace("_", string.Empty).Trim();
                        break;
                    }
                }

                found.Add(new RecordedCase(m.Groups[1].Value, name, text));
            }
        }

        return found;
    }

    [Fact]
    public void EveryCaseNumberMeansExactlyOneThing()
    {
        // M6-SIXEL-AND-REGIS.md carried TWO cases numbered M6.5 until 27 August 2026 - the phosphor
        // case and the graphics-input case. The run sheet, the plan and FINDINGS all say "M6.5" and
        // all mean the second one.
        var cases = ReadCases();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var clashes = new StringBuilder();

        for (int i = 0; i < cases.Count; i++)
        {
            string key = cases[i].File + " " + cases[i].Id;
            if (seen.ContainsKey(key))
            {
                clashes.Append(cases[i].Id).Append(" appears twice in ").Append(cases[i].File)
                       .Append('\n');
                continue;
            }

            seen[key] = cases[i].File;
        }

        Assert.True(clashes.Length == 0,
            "a case number that means two things sends the reader to whichever it reaches first:\n"
            + clashes);
    }

    [Fact]
    public void NothingAlreadyPassedIsStillListedAsWorkToDo()
    {
        // THE ONE THAT COST A TURN. M5.1a was PASS in M5-TEKTRONIX.md from 25 August and listed as
        // outstanding in the run sheet's session 1 table, so Ronny was asked to judge it twice.
        var cases = ReadCases();

        string runSheet = File.ReadAllText(Path.Combine(DocsFolder, "RUN-SHEET.md"));
        string plan = File.ReadAllText(Path.GetFullPath(
            Path.Combine(DocsFolder, "..", "PLAN.md")));

        // Only the parts that LIST work. The run sheet's own Results table at the bottom is a
        // record and is supposed to name finished cases.
        int resultsAt = runSheet.IndexOf("## Results", StringComparison.Ordinal);
        string runSheetWork = resultsAt < 0 ? runSheet : runSheet.Substring(0, resultsAt);

        int callsAt = plan.IndexOf("## Standing judgement calls", StringComparison.Ordinal);
        string planWork = callsAt < 0 ? plan : plan.Substring(0, callsAt);

        var stale = new StringBuilder();
        for (int i = 0; i < cases.Count; i++)
        {
            if (!cases[i].IsFinished) continue;

            string needle = "**" + cases[i].Id + "**";
            if (runSheetWork.Contains(needle, StringComparison.Ordinal))
            {
                stale.Append(cases[i].Id).Append(" is ").Append(cases[i].Result)
                     .Append(" in ").Append(cases[i].File)
                     .Append(" but RUN-SHEET.md still lists it as work\n");
            }

            if (planWork.Contains(needle, StringComparison.Ordinal))
            {
                stale.Append(cases[i].Id).Append(" is ").Append(cases[i].Result)
                     .Append(" in ").Append(cases[i].File)
                     .Append(" but PLAN.md still lists it as work\n");
            }
        }

        Assert.True(stale.Length == 0,
            "a finished case still listed as work gets asked of Ronny a second time, and he has to "
            + "answer a question he already answered:\n" + stale);
    }
}
