//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using RetroTerm.Tests.Oracle;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Records the TK5 sessions the offline oracle (Oracle\HostStreamOracleTests) replays: each
    /// one run in RetroTerm through <see cref="Tk5WireTap"/>, with every key and every typed text
    /// marked in the stream, then written as a <see cref="HostRecording"/> with the password
    /// replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needs TK5 running (category Hercules). The files go to the test's output folder,
    /// <c>Oracle\new-recordings\</c>, never straight into the repository: a person copies them to
    /// <c>src\tests\RetroTerm.Tests\Oracle\Recordings\</c> when a new recording is wanted, so a
    /// routine Hercules run cannot replace the stored ones.
    /// </para>
    /// <para>
    /// Each file is checked for every password in tk5-tests.json, in every form it could take
    /// (<see cref="HostRecording.AssertHolds"/>), before the test passes.
    /// </para>
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class HostStreamRecorderTests
    {
        private readonly ITestOutputHelper _output;

        public HostStreamRecorderTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("logon-panel")]
        [InlineData("tso-ready")]
        [InlineData("ispf-boxes")]
        [InlineData("ispf-editor")]
        [InlineData("model5")]
        [InlineData("termtest")]
        [InlineData("file-transfer")]
        [InlineData("file-transfer-large")]
        [InlineData("file-transfer-binary")]
        public async Task RecordTheSession(string name)
        {
            Tk5Settings settings = Tk5Settings.Load();
            int model = name == "model5" ? 5 : 2;
            List<Tk5WireChunk> chunks;
            await using (Tk5WireTap tap = Tk5WireTap.Start(settings.Host, settings.Port))
            {
                try
                {
                    await using (RetroTermTk5Terminal terminal = await RetroTermTk5Terminal.OpenAsync(settings.Through("127.0.0.1", tap.Port), model))
                    {
                        List<Tk5Step> steps = Steps(name, settings, terminal, tap);
                        await Tk5Scenario.RunAsync(new MarkingTerminal(terminal, tap), steps, _output);
                    }
                }
                finally
                {
                    // The transfer records as they crossed, passed or failed: the evidence a
                    // failed transfer needs (no recording is written then).
                    if (name.StartsWith("file-transfer", StringComparison.Ordinal))
                    {
                        List<string> wire = TransferWire.Summary(tap.Chunks());
                        for (int i = 0; i < wire.Count; i++)
                        {
                            _output.WriteLine(wire[i]);
                        }
                    }
                }

                chunks = tap.Chunks();
            }

            string[] secrets = { settings.Password, settings.ComparePassword, settings.JrpPassword };
            HostRecording recording = HostRecording.FromChunks(model, chunks, secrets);
            string folder = Path.Combine(AppContext.BaseDirectory, "Oracle", "new-recordings");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".txt");
            recording.Save(path, new[]
            {
                "Recorded from TK5 (MVS 3.8J Turnkey 5 on Hercules) by HostStreamRecorderTests, " + DateTime.Now.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + ".",
                "Client: RetroTerm as an IBM-3278-" + model + ", plain TN3270. Passwords are replaced by " + HostRecording.PasswordPlaceholder + ".",
                "Replayed by Oracle\\HostStreamOracleTests to RetroTerm and to s3270; see Oracle\\HostRecording.cs for the format.",
            });
            HostRecording.AssertHolds(path, secrets);
            _output.WriteLine("written: " + path + " (" + recording.Lines.Count + " lines)");
        }

        private List<Tk5Step> Steps(string name, Tk5Settings settings, RetroTermTk5Terminal terminal, Tk5WireTap tap)
        {
            var steps = new List<Tk5Step>();
            if (name == "logon-panel")
            {
                steps.Add(new Tk5Step("connect", t => Task.CompletedTask,
                    s => s.Contains("Logon ===>"), "the logon panel", false));
                return steps;
            }

            Tk5CommonSteps.AddLogon(steps, settings);
            switch (name)
            {
                case "tso-ready":
                    steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
                    steps.Add(Tk5CommonSteps.Tso("TIME", "TIME", "READY", false));
                    steps.Add(Tk5CommonSteps.Tso("LISTCAT", "LISTCAT", "READY", false));
                    break;
                case "ispf-boxes":
                    // Option 3 and option M: the utility menu and the extra applications menu.
                    AddOption(steps, "3", "Utility selection menu");
                    AddOption(steps, "M", "Additional applications");
                    AddOption(steps, "C", "SUMMARY OF CHANGES");
                    steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
                    break;
                case "ispf-editor":
                    AddEditor(steps, settings.User);
                    break;
                case "model5":
                    AddOption(steps, "3", "Utility selection menu");
                    steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
                    steps.Add(Tk5CommonSteps.Tso("LISTCAT", "LISTCAT", "READY", false));
                    break;
                case "termtest":
                    AddTermtest(steps);
                    break;
                case "file-transfer":
                    AddFileTransfer(steps, settings.User, terminal, tap, TransferDataSet, "ascii", System.Text.Encoding.ASCII.GetBytes(TransferText), string.Empty);
                    break;
                case "file-transfer-large":
                    // Larger than the 16384 bytes RetroTerm gives as LIMIN and LIMOUT, so the file
                    // goes in several data blocks each way (IND-FILE-TRANSFER.md open question 2).
                    AddFileTransfer(steps, settings.User, terminal, tap, "RTTFL.TEXT", "ascii", TransferTestFiles.Text(LargeTextBytes), string.Empty);
                    break;
                case "file-transfer-binary":
                    AddFileTransfer(steps, settings.User, terminal, tap, "RTTFB.DATA", "binary", TransferTestFiles.Binary(LargeBinaryBytes), BinaryOptions);
                    break;
                default:
                    throw new ArgumentException("no such recording: " + name);
            }

            steps.Add(Tk5CommonSteps.Logoff());
            return steps;
        }

        private static void AddOption(List<Tk5Step> steps, string option, string expect)
        {
            steps.Add(new Tk5Step("option " + option, async t =>
            {
                await t.TypeAsync(option);
                await t.PressAsync("ENTER");
            }, s => s.Contains(expect) && !s.Contains("ISPF primary option menu"), "'" + expect + "'", false));
            steps.Add(new Tk5Step("PF3 from " + option, t => t.PressAsync("PF3"),
                s => s.Contains("ISPF primary option menu"), "the primary menu again", false));
        }

        // Guide 03 in short: a PDS, a new member typed into in REVEDIT, SAVE, and the PDS deleted.
        // The same keys as Scenario03EditingTests.
        private static void AddEditor(List<Tk5Step> steps, string user)
        {
            static bool Has(Tk5Screen s, string text) => s.Text().ToUpperInvariant().Contains(text.ToUpperInvariant(), StringComparison.Ordinal);
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
            steps.Add(Tk5CommonSteps.Tso("clean up RTO.CNTL", "DELETE RTO.CNTL", string.Empty, false));
            // MVS 3.8 ALLOC has no RECFM: the attributes go through an attribute list, as in
            // Scenario03EditingTests.
            steps.Add(Tk5CommonSteps.Tso("ATTRIB", "ATTRIB FB80 RECFM(F B) LRECL(80) BLKSIZE(3120)", "READY", false));
            steps.Add(Tk5CommonSteps.Tso("ALLOC PDS", "ALLOC DA(RTO.CNTL) NEW CATALOG SPACE(2,1) TRACKS DIR(5) USING(FB80)", "READY", false));
            steps.Add(Tk5CommonSteps.Tso("FREE", "FREE DA(RTO.CNTL)", "READY", false));
            steps.Add(Tk5CommonSteps.Tso("FREE ATTRLIST", "FREE ATTRLIST(FB80)", "READY", false));
            steps.Add(new Tk5Step("ISPF", async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                await t.TypeAsync("ISPF");
                await t.PressAsync("ENTER");
            }, s => s.Contains("ISPF primary option menu"), "the ISPF menu", false));
            steps.Add(new Tk5Step("option 2", async t =>
            {
                await t.TypeAsync("2");
                await t.PressAsync("ENTER");
            }, s => Has(s, "REVEDIT- ENTRY PANEL"), "the REVEDIT entry panel", false));
            steps.Add(new Tk5Step("open a new member", async t =>
            {
                for (int i = 0; i < 4; i++)
                {
                    await t.PressAsync("ERASEEOF");
                    await t.PressAsync("TAB");
                }

                await t.PressAsync("ERASEEOF");
                await t.TypeAsync("'" + user + ".RTO.CNTL(HELLO)'");
                await t.PressAsync("TAB");
                await t.PressAsync("ERASEEOF");
                await t.PressAsync("ENTER");
            }, s => Has(s, "REVEDIT  ") && Has(s, "TOP OF DATA"), "the empty member", false));
            steps.Add(new Tk5Step("type lines", async t =>
            {
                await t.PressAsync("NEWLINE");
                await t.PressAsync("NEWLINE");
                await t.PressAsync("TAB");
                await t.TypeAsync("LINE ONE");
                await t.PressAsync("NEWLINE");
                await t.PressAsync("TAB");
                await t.TypeAsync("LINE TWO");
                await t.PressAsync("ENTER");
            }, s => s.Contains("000002 LINE TWO"), "two numbered lines", false));
            steps.Add(new Tk5Step("SAVE", async t =>
            {
                await t.PressAsync("HOME");
                await t.PressAsync("ERASEEOF");
                await t.TypeAsync("SAVE");
                await t.PressAsync("ENTER");
            }, s => Has(s, "DATA SAVED"), "DATA SAVED", false));
            steps.Add(new Tk5Step("PF3 from the editor", t => t.PressAsync("PF3"), s => Has(s, "REVEDIT- ENTRY PANEL"), "the entry panel", false));
            steps.Add(new Tk5Step("PF3 to the menu", t => t.PressAsync("PF3"), s => s.Contains("ISPF primary option menu"), "the ISPF menu", false));
            steps.Add(new Tk5Step("PF3 back to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
            steps.Add(Tk5CommonSteps.Tso("DELETE", "DELETE RTO.CNTL", "DELETED", false));
        }

        // M.7 TERMTEST and one of its field-test PF keys. Its title line names them: "FIELD TEST
        // PFKS: 10-12, 1-7, 8(GE)". A modified field and PF2 is the first field test of
        // Scenario10TermtestTests. Left with CAN, which saves nothing to its profile; never Z.
        private static void AddTermtest(List<Tk5Step> steps)
        {
            steps.Add(new Tk5Step("option M.7", async t =>
            {
                await t.TypeAsync("M.7");
                await t.PressAsync("ENTER");
            }, s => s.Contains("TSO 3270 VDU TEST") && s.Contains("QUERY-REPLY-FIELD-DETAILS"), "the TERMTEST screen", false));
            steps.Add(new Tk5Step("X and PF2", async t =>
            {
                await t.TypeAsync("X");
                await t.PressAsync("PF2");
            }, s => s.Contains("AID=F2(2)"), "the PF2 field test", false));
            steps.Add(new Tk5Step("CAN", async t =>
            {
                await t.TypeAsync("CAN");
                await t.PressAsync("ENTER");
            }, s => s.Contains("Additional applications"), "the M menu, which CAN returns to", false));
            steps.Add(new Tk5Step("PF3 from M", t => t.PressAsync("PF3"), s => s.Contains("ISPF primary option menu"), "the primary menu again", false));
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
        }

        // Guide 08 section 1 in short, as Scenario08FileTransferTests: a file up with IND$FILE
        // PUT, the same data set back with IND$FILE GET, and the data set deleted.
        private const string TransferDataSet = "RTTFT.TEXT";

        private const string TransferText = "HELLO FROM RETROTERM\r\nLINE TWO OF THE TEST\r\n";

        // The large files: over 16384 bytes, so several data blocks each way, and small enough
        // that the recording (every byte as hex, both ways) stays well under a megabyte.
        private const int LargeTextBytes = 50000;

        private const int LargeBinaryBytes = 20000;

        // Fixed 80-byte records for the binary file, 250 of them exactly, so no record is padded
        // and the file comes back byte for byte. Given on the PUT only; GET takes the data set as
        // it is.
        private const string BinaryOptions = "RECFM(F) LRECL(80) BLKSIZE(3120)";

        private void AddFileTransfer(List<Tk5Step> steps, string user, RetroTermTk5Terminal terminal, Tk5WireTap tap, string dataSet, string mode, byte[] file, string options)
        {
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
            steps.Add(Tk5CommonSteps.Tso("clean up " + dataSet, "DELETE " + dataSet, string.Empty, false));
            steps.Add(Transfer(true, terminal, tap, dataSet, mode, file, options));
            steps.Add(Tk5CommonSteps.Tso("LISTDS", "LISTDS " + dataSet, user + "." + dataSet, false));
            steps.Add(Transfer(false, terminal, tap, dataSet, mode, file, string.Empty));
            steps.Add(Tk5CommonSteps.Tso("delete the data set", "DELETE " + dataSet, "READY", false));
        }

        // Clear, then HOSTSEND or HOSTRECEIVE on the real session: the command types IND$FILE at
        // READY and answers the host's transfer records. The transfer is marked in the stream
        // first, with the file that went up, so the replay can start the same transfer. A
        // download must give back the very bytes that went up.
        private Tk5Step Transfer(bool upload, RetroTermTk5Terminal terminal, Tk5WireTap tap, string dataSet, string mode, byte[] file, string options)
        {
            string name = upload ? "HOSTSEND" : "HOSTRECEIVE";
            return new Tk5Step(name, async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                byte[] bytes = upload ? file : Array.Empty<byte>();
                string path = Path.Combine(Path.GetTempPath(), "rtt-rec-" + Guid.NewGuid().ToString("N") + ".txt");
                try
                {
                    if (upload)
                    {
                        File.WriteAllBytes(path, bytes);
                    }

                    tap.Mark("transfer " + new TransferLine(upload, mode, dataSet, bytes, options).Format());
                    var args = new RetroTerm.Core.Commands.CommandArgs().Set("path", path).Set("dataset", dataSet)
                        .Set("mode", mode).Set("options", options).Set("timeout", "60000");
                    RetroTerm.Core.Commands.ISessionCommand command = upload
                        ? new RetroTerm.Core.Commands.Builtin.HostSendCommand()
                        : new RetroTerm.Core.Commands.Builtin.HostReceiveCommand();
                    RetroTerm.Core.Commands.CommandResult result = await command.ExecuteAsync(terminal.Session, args, TestContext.Current.CancellationToken);
                    _output.WriteLine(name + " answered: " + (result.Success ? "ok, " + result.Output : "FAILED, " + result.Error));
                    Assert.True(result.Success, name + ": " + result.Error);
                    if (!upload)
                    {
                        Assert.Equal(Convert.ToHexString(file), Convert.ToHexString(File.ReadAllBytes(path)));
                    }
                }
                finally
                {
                    File.Delete(path);
                    File.Delete(path + ".part");
                }
            }, s => Tk5CommonSteps.LastLineIsReady(s), "READY after the transfer", false);
        }

        // Passes every key and text on to the real terminal, and marks each in the tapped stream
        // first, so the recording knows what the person did between the bytes.
        private sealed class MarkingTerminal : ITk5Terminal
        {
            private readonly ITk5Terminal _inner;
            private readonly Tk5WireTap _tap;

            public MarkingTerminal(ITk5Terminal inner, Tk5WireTap tap)
            {
                _inner = inner;
                _tap = tap;
            }

            public string Name => _inner.Name;

            public Task TypeAsync(string text)
            {
                _tap.Mark("type " + text);
                return _inner.TypeAsync(text);
            }

            public Task PressAsync(string key)
            {
                _tap.Mark("key " + key.ToUpperInvariant());
                return _inner.PressAsync(key);
            }

            public Task<Tk5Screen> WaitUntilAsync(Tk5ScreenTest done, string what, int timeoutMs) => _inner.WaitUntilAsync(done, what, timeoutMs);

            public Task<Tk5Screen> ReadAsync() => _inner.ReadAsync();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
