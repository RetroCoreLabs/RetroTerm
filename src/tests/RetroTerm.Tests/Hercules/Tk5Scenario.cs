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

        public string Name { get; }

        public Tk5StepAction Act { get; }

        public Tk5ScreenTest Done { get; }

        public string What { get; }

        /// <summary>
        /// True when the screen after this step is compared with s3270. False only for a screen
        /// whose text is different every time (the TSO fortune cookie).
        /// </summary>
        public bool Compare { get; }
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

                List<string> differences = retroTerm[i].DifferencesFrom(reference[i], referenceName);
                for (int d = 0; d < differences.Count; d++)
                {
                    report.Append("step '").Append(steps[i].Name).Append("', ").Append(differences[d]).Append('\n');
                }
            }

            Assert.True(report.Length == 0, "RetroTerm and " + referenceName + " differ:\n" + report);
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
                for (int i = 0; i < 6; i++)
                {
                    screen = await terminal.WaitUntilAsync(s => true, "an unlocked keyboard", HostTimeoutMs);
                    if (screen.Contains("READY"))
                    {
                        break;
                    }

                    await terminal.PressAsync("PF3");
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
