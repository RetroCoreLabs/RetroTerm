//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using RetroTerm.Core.Commands.Builtin;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// The IBM Host Files window's three flows against a live TK5: Upload a small text file to a
    /// new data set, View it back, compare, all through HostFileTransfer's READY check and the
    /// Clear after each transfer - the code the window runs.
    /// </summary>
    /// <remarks>
    /// Needs TK5 and the settings in <see cref="Tk5Settings"/>; outside the routine gate
    /// (Category Hercules), and FAILS when the host does not answer. The data set is
    /// user.RTTHF.TEXT, deleted first in case an earlier run stopped half-way, and deleted again
    /// at the end.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class HostFilesWindowTk5Tests
    {
        private const string Text = "HELLO FROM THE HOST FILES WINDOW\r\nSECOND LINE\r\n";

        private readonly ITestOutputHelper _output;

        public HostFilesWindowTk5Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        // The screen TSO leaves after Clear at READY: no text in the 24 host rows.
        private static bool IsBlank(Tk5Screen s)
        {
            for (int r = 0; r < Tk5Screen.Rows; r++)
            {
                if (s.Lines[r].Trim().Length > 0)
                {
                    return false;
                }
            }

            return true;
        }

        [Fact]
        public async Task UploadThenViewGivesTheSameText()
        {
            Tk5Settings settings = Tk5Settings.Load();
            string up = Path.Combine(Path.GetTempPath(), "rtt-hf-up-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(up, Text, Encoding.ASCII);
            var outcomes = new List<HostFilesOutcome>();
            try
            {
                var steps = new List<Tk5Step>();
                Tk5CommonSteps.AddLogon(steps, settings);

                // In ISPF the window must refuse, and type nothing.
                steps.Add(new Tk5Step("refused in ISPF", async t =>
                {
                    var terminal = (RetroTermTk5Terminal)t;
                    HostFilesOutcome refused = await HostFileTransfer.ViewAsync(terminal.Session, "RTTHF.TEXT", 60000, TestContext.Current.CancellationToken);
                    Assert.Equal(HostFileTransfer.NotAtReadyMessage, refused.Message);
                }, s => s.Contains("ISPF primary option menu"), "the ISPF menu, untouched", false));

                steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
                steps.Add(Tk5CommonSteps.Tso("clean up RTTHF.TEXT", "DELETE RTTHF.TEXT", "", false));

                steps.Add(new Tk5Step("Upload", async t =>
                {
                    var terminal = (RetroTermTk5Terminal)t;
                    HostFilesOutcome outcome = await HostFileTransfer.UploadAsync(terminal.Session, "RTTHF.TEXT", false, up, "", "", "", 60000, TestContext.Current.CancellationToken);
                    outcomes.Add(outcome);
                    _output.WriteLine("Upload: " + outcome.Success + " " + outcome.Message);
                    Assert.True(outcome.Success, outcome.Message + "\nRecords, oldest first:\n" + terminal.WireText());
                }, IsBlank, "the empty screen after Clear", false));

                steps.Add(new Tk5Step("View", async t =>
                {
                    var terminal = (RetroTermTk5Terminal)t;
                    HostFilesOutcome outcome = await HostFileTransfer.ViewAsync(terminal.Session, "RTTHF.TEXT", 60000, TestContext.Current.CancellationToken);
                    outcomes.Add(outcome);
                    _output.WriteLine("View: " + outcome.Success + " " + outcome.Message);
                    Assert.True(outcome.Success, outcome.Message + "\nRecords, oldest first:\n" + terminal.WireText());
                }, IsBlank, "the empty screen after Clear", false));

                steps.Add(Tk5CommonSteps.Tso("delete the data set", "DELETE RTTHF.TEXT", "READY", false));
                steps.Add(Tk5CommonSteps.Logoff());

                await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
                await Tk5Scenario.RunAsync(retroTerm, steps, _output);

                Assert.Equal(2, outcomes.Count);
                Assert.StartsWith("TRANS03", outcomes[0].Message, StringComparison.Ordinal);
                Assert.Equal(Text, outcomes[1].Text);
            }
            finally
            {
                File.Delete(up);
            }
        }
    }
}
