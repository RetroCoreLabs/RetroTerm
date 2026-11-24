//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// What a step does to the terminal before its screen is awaited.
    /// </summary>
    internal delegate Task Tk5StepAction(ITk5Terminal terminal);

    /// <summary>
    /// One step of a scenario from the Hercules TK5 guides.
    /// </summary>
    internal sealed class Tk5Step
    {
        public Tk5Step(string name, Tk5StepAction act, Tk5ScreenTest done, string what, bool compare)
        {
            Name = name;
            Act = act;
            Done = done;
            What = what;
            Compare = compare;
        }

        /// <summary>
        /// A step compared with s3270 except for the text of some rows, each left out for a
        /// reason the caller states (a row that shows each terminal's own query reply, or a code
        /// page 310 character whose Unicode choice is documented to differ from x3270's).
        /// </summary>
        public Tk5Step(string name, Tk5StepAction act, Tk5ScreenTest done, string what, int[] rowsNotCompared, string whyNotCompared)
            : this(name, act, done, what, true)
        {
            RowsNotCompared = rowsNotCompared;
            WhyNotCompared = whyNotCompared;
        }

        public string Name { get; }

        public Tk5StepAction Act { get; }

        public Tk5ScreenTest Done { get; }

        public string What { get; }

        /// <summary>
        /// True when the screen after this step is compared with s3270. False only for a screen
        /// whose text is different every time (the TSO fortune cookie).
        /// </summary>
        public bool Compare { get; }

        /// <summary>
        /// Rows counted from 0 whose text is not compared, or null for every row.
        /// </summary>
        public int[]? RowsNotCompared { get; }

        /// <summary>
        /// Why <see cref="RowsNotCompared"/> are left out.
        /// </summary>
        public string? WhyNotCompared { get; }
    }

    /// <summary>
    /// Runs a scenario's steps on one terminal and keeps the screen after each step.
    /// </summary>
    internal static class Tk5Scenario
    {
        public const int HostTimeoutMs = 30000;

        public static async Task<List<Tk5Screen>> RunAsync(ITk5Terminal terminal, List<Tk5Step> steps, ITestOutputHelper output)
        {
            var screens = new List<Tk5Screen>(steps.Count);
            bool finished = false;
            try
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    Tk5Step step = steps[i];
                    await step.Act(terminal);
                    Tk5Screen screen = await terminal.WaitUntilAsync(step.Done, step.What + " (step '" + step.Name + "')", HostTimeoutMs);
                    output.WriteLine("--- " + terminal.Name + ", after '" + step.Name + "':\n" + screen.Text());
                    screens.Add(screen);
                }

                finished = true;
            }
            finally
            {
                if (!finished)
                {
                    await LogOffAfterAFailureAsync(terminal, output);
                }
            }

            return screens;
        }

        /// <summary>
        /// Compares two runs of the same steps, screen by screen, and fails with every difference.
        /// </summary>
        public static void AssertSameScreens(List<Tk5Step> steps, List<Tk5Screen> retroTerm, List<Tk5Screen> reference, string referenceName)
        {
            Assert.Equal(steps.Count, retroTerm.Count);
            Assert.Equal(steps.Count, reference.Count);

            var report = new System.Text.StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Compare)
                {
                    continue;
                }

                List<string> differences = retroTerm[i].DifferencesFrom(reference[i], referenceName, steps[i].RowsNotCompared);
                for (int d = 0; d < differences.Count; d++)
                {
                    report.Append("step '").Append(steps[i].Name).Append("', ").Append(differences[d]).Append('\n');
                }
            }

            Assert.True(report.Length == 0, "RetroTerm and " + referenceName + " differ:\n" + report);
        }

        // The last line with anything on it, trimmed: the prompt TSO is waiting at.
        private static string LastNonBlankLine(Tk5Screen screen)
        {
            for (int r = Tk5Screen.Rows - 1; r >= 0; r--)
            {
                string line = screen.Lines[r].Trim();
                if (line.Length > 0)
                {
                    return line;
                }
            }

            return string.Empty;
        }

        // A step failed while logged on. Leaving TSO waiting would refuse the next run's logon
        // ("USERID IN USE", guide 01 section 10), so try to get back to READY and log off. A
        // failure here is written to the test output, and the original failure still stands.
        private static async Task LogOffAfterAFailureAsync(ITk5Terminal terminal, ITestOutputHelper output)
        {
            try
            {
                Tk5Screen screen = await terminal.ReadAsync();
                if (screen.Contains("Logon ===>"))
                {
                    return;
                }

                await terminal.PressAsync("RESET");
                await terminal.PressAsync("PA1");
                string before = string.Empty;
                for (int i = 0; i < 8; i++)
                {
                    screen = await terminal.WaitUntilAsync(s => true, "an unlocked keyboard", HostTimeoutMs);
                    if (screen.Contains("READY"))
                    {
                        break;
                    }

                    // PF3 that changed nothing: REVEDIT will not end while a line command is wrong
                    // or pending (found 23 November 2026, "UNKNOWN LINE COMMAND"; the user stayed
                    // logged on and the next run was refused). CANCEL on the command line leaves
                    // the editor without saving, whatever is pending.
                    string now = screen.Text();
                    string last = LastNonBlankLine(screen);
                    if (last == "EDIT" || last.StartsWith("ENTER SAVE OR END", StringComparison.Ordinal))
                    {
                        // The TSO line editor (guide 03 section 7) ignores PF3 and has no CANCEL:
                        // END leaves it, and END again at its "save or end" question leaves
                        // without saving (found 23 November 2026, guide 04).
                        await terminal.TypeAsync("END");
                        await terminal.PressAsync("ENTER");
                    }
                    else if (now == before)
                    {
                        await terminal.PressAsync("HOME");
                        await terminal.PressAsync("ERASEEOF");
                        await terminal.TypeAsync("CANCEL");
                        await terminal.PressAsync("ENTER");
                    }
                    else
                    {
                        await terminal.PressAsync("PF3");
                    }

                    before = now;
                }

                await terminal.TypeAsync("LOGOFF");
                await terminal.PressAsync("ENTER");
                await terminal.WaitUntilAsync(s => s.Contains("Logon ===>"), "the logon panel after LOGOFF", HostTimeoutMs);
            }
            catch (Exception e)
            {
                output.WriteLine("Could not log " + terminal.Name + " off after the failure; the next logon may say USERID IN USE: " + e.Message);
            }
        }
    }
}
