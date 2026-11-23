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

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Guide 08 of the Hercules TK5 guides, "Moving files between the PC and MVS", section 1:
    /// IND$FILE. A text file goes up with HOSTSEND, LISTDS shows the data set, the same data set
    /// comes back with HOSTRECEIVE, and the bytes must be the ones sent.
    /// </summary>
    /// <remarks>
    /// Runs the registry commands, the same code a script and MCP run. Needs TK5 running and the
    /// settings in <see cref="Tk5Settings"/>; outside the routine gate (Category Hercules), and
    /// FAILS when the host does not answer. The data set is user.RTTFT.TEXT, deleted first in
    /// case an earlier run stopped half-way, and deleted again at the end. Not compared with
    /// s3270: the screens carry the transfer's byte count and the guide's s3270 run used its own
    /// Transfer action, not these commands.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Scenario08FileTransferTests
    {
        // Two lines with CR LF, as a Windows text file is. With ASCII CRLF the host ends a record
        // at each CR LF and adds one after each record on the way back (IND-FILE-TRANSFER.md 2.4).
        private const string Text = "HELLO FROM RETROTERM\r\nLINE TWO OF THE TEST\r\n";

        private readonly ITestOutputHelper _output;

        public Scenario08FileTransferTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ATextFileGoesUpAndComesBackUnchanged()
        {
            Tk5Settings settings = Tk5Settings.Load();
            string up = Path.Combine(Path.GetTempPath(), "rtt-ft-up-" + Guid.NewGuid().ToString("N") + ".txt");
            string down = Path.Combine(Path.GetTempPath(), "rtt-ft-down-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(up, Text, Encoding.ASCII);
            var results = new List<CommandResult>();
            try
            {
                List<Tk5Step> steps = Steps(settings, up, down, results);
                await using RetroTermTk5Terminal retroTerm = await RetroTermTk5Terminal.OpenAsync(settings);
                List<Tk5Screen> screens = await Tk5Scenario.RunAsync(retroTerm, steps, _output);

                Assert.Equal(steps.Count, screens.Count);
                Assert.Equal(2, results.Count);
                Assert.True(results[0].Success, "HOSTSEND: " + results[0].Error);
                Assert.StartsWith("TRANS03", results[0].Output, StringComparison.Ordinal);
                Assert.True(results[1].Success, "HOSTRECEIVE: " + results[1].Error);
                Assert.Equal(Text, File.ReadAllText(down, Encoding.ASCII));
                Assert.False(File.Exists(down + ".part"), "the .part file was left behind");
            }
            finally
            {
                File.Delete(up);
                File.Delete(down);
                File.Delete(down + ".part");
            }
        }

        // A transfer step: Clear, then the command, which types IND$FILE at READY and waits for
        // the host's TRANSxx message. That message comes inside the transfer, not on the screen,
        // which shows READY again afterwards (measured on TK5 23 November 2026).
        private Tk5Step Transfer(string name, ISessionCommand command, CommandArgs args, List<CommandResult> results)
        {
            return new Tk5Step(name, async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                var terminal = (RetroTermTk5Terminal)t;
                CommandResult result = await command.ExecuteAsync(terminal.Session, args, TestContext.Current.CancellationToken);
                results.Add(result);
                _output.WriteLine(name + " answered: " + (result.Success ? "ok, " + result.Output : "FAILED, " + result.Error + "`nRecords, oldest first:`n" + terminal.WireText()));
                Assert.True(result.Success, name + ": " + result.Error + "`nRecords, oldest first:`n" + terminal.WireText());
            }, s => Tk5CommonSteps.LastLineIsReady(s), "READY after the transfer", false);
        }

        private List<Tk5Step> Steps(Tk5Settings settings, string up, string down, List<CommandResult> results)
        {
            string user = settings.User;
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);

            // Guide 08 section 1 step 1: IND$FILE needs the plain READY prompt, not ISPF.
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", true));
            steps.Add(Tk5CommonSteps.Tso("clean up RTTFT.TEXT", "DELETE RTTFT.TEXT", "", false));

            steps.Add(Transfer("HOSTSEND", new HostSendCommand(),
                new CommandArgs().Set("path", up).Set("dataset", "RTTFT.TEXT").Set("mode", "ascii").Set("options", "").Set("timeout", "60000"), results));
            steps.Add(Tk5CommonSteps.Tso("LISTDS", "LISTDS RTTFT.TEXT", user + ".RTTFT.TEXT", false));
            steps.Add(Transfer("HOSTRECEIVE", new HostReceiveCommand(),
                new CommandArgs().Set("path", down).Set("dataset", "RTTFT.TEXT").Set("mode", "ascii").Set("options", "").Set("timeout", "60000"), results));

            steps.Add(Tk5CommonSteps.Tso("delete the data set", "DELETE RTTFT.TEXT", "READY", false));
            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }
    }
}
