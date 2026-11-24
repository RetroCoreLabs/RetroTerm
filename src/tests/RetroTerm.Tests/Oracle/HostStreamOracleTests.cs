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

using Xunit;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// The offline 3270 oracle: a session recorded once from TK5 (Hercules\HostStreamRecorderTests)
    /// is replayed byte for byte to RetroTerm and to s3270, and after every host record the two
    /// screens are compared position by position: text, buffer codes, field attributes, Graphic
    /// Escape, highlight, cursor and keyboard lock. The records each client sent back and their
    /// telnet answers are compared too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No host is needed, only s3270 (its path is the "s3270" entry of
    /// %USERPROFILE%\.retroterm\tk5-tests.json). Category Oracle: kept out of the routine and full
    /// gates and of the GitHub run, which has no s3270, and run on its own with
    /// <c>--filter "Category=Oracle"</c>. When s3270 is missing the tests FAIL and say what is
    /// missing; they never skip.
    /// </para>
    /// <para>
    /// Both clients get the very same bytes, so nothing is masked, and every difference is a
    /// difference in how the two terminals handle the data stream.
    /// </para>
    /// <para>
    /// An IND$FILE transfer (the file-transfer recording) is started in each client by its own
    /// means: RetroTerm's HOSTSEND or HOSTRECEIVE, s3270's Transfer() action. Compared: the
    /// IND$FILE command each one types, every file transfer record each sends back (Open, Data,
    /// Get, Close and message acknowledgements, byte for byte), the bytes of the PC file each
    /// writes for a download, and the screen once the transfer has ended. Not compared: the
    /// screen during a transfer (s3270 takes no other command until Transfer() ends), and the
    /// query reply the host asks for mid-transfer, as everywhere (each terminal lists its own
    /// features, so its DDM buffer size too). The recorded file is 49 bytes, one Data record, so
    /// how a client splits a file larger than the buffer is not covered.
    /// </para>
    /// </remarks>
    [Trait("Category", "Oracle")]
    public class HostStreamOracleTests
    {
        private readonly ITestOutputHelper _output;

        public HostStreamOracleTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("logon-panel")]
        [InlineData("tso-ready")]
        [InlineData("ispf-boxes")]
        [InlineData("ispf-editor")]
        [InlineData("model5")]
        [InlineData("oversize")]
        [InlineData("termtest")]
        [InlineData("ispf-3278t")]
        [InlineData("file-transfer")]
        [InlineData("file-transfer-large")]
        [InlineData("file-transfer-binary")]
        public async Task RetroTermShowsWhatS3270ShowsForTheRecordedSession(string name)
        {
            // s3270 first: a missing s3270 fails here, before anything else, by name.
            S3270OracleClient.S3270Path();
            HostRecording recording = HostRecording.Load(Path.Combine(AppContext.BaseDirectory, "Oracle", "Recordings", name + ".txt"));

            ReplayResult reference;
            S3270OracleClient s3270 = S3270OracleClient.Start(recording);
            await using (s3270)
            {
                reference = await HostStreamReplay.RunAsync(recording, s3270);
            }

            ReplayResult ours;
            await using (var retroTerm = new RetroTermOracleClient(recording))
            {
                ours = await HostStreamReplay.RunAsync(recording, retroTerm);
            }

            string report = Compare(ours, reference);
            for (int i = 0; i < ours.Screens.Count; i++)
            {
                _output.WriteLine("--- RetroTerm, " + ours.Labels[i] + ":\n" + ours.Screens[i].Text());
            }

            Assert.True(report.Length == 0, "RetroTerm and s3270 differ on the replayed '" + name + "' session:\n" + report);
        }

        [Fact]
        public void NoRecordingHoldsATk5Password()
        {
            // The passwords are read from the same private file the live tests use, and the
            // stored recordings are searched for each in every form (HostRecording.AssertHolds).
            // Without the file there is nothing to search for, which is a failure, not a pass.
            string json = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".retroterm", "tk5-tests.json");
            Assert.True(File.Exists(json), "the password check needs " + json);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
            var secrets = new List<string>();
            string[] keys = { "password", "comparePassword", "jrpPassword" };
            for (int k = 0; k < keys.Length; k++)
            {
                if (document.RootElement.TryGetProperty(keys[k], out System.Text.Json.JsonElement value) && !string.IsNullOrEmpty(value.GetString()))
                {
                    secrets.Add(value.GetString()!);
                }
            }

            Assert.NotEmpty(secrets);
            string[] files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Oracle", "Recordings"), "*.txt");
            Assert.Equal(11, files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                HostRecording.AssertHolds(files[i], secrets.ToArray());
            }
        }

        private static string Compare(ReplayResult ours, ReplayResult reference)
        {
            var report = new StringBuilder();
            for (int i = 0; i < reference.OutOfStep.Count; i++)
            {
                report.Append("s3270 out of step with the recording: ").Append(reference.OutOfStep[i]).Append('\n');
            }

            for (int i = 0; i < ours.OutOfStep.Count; i++)
            {
                report.Append("RetroTerm out of step with the recording: ").Append(ours.OutOfStep[i]).Append('\n');
            }

            if (ours.Screens.Count != reference.Screens.Count)
            {
                report.Append("screens read: RetroTerm ").Append(ours.Screens.Count).Append(", s3270 ").Append(reference.Screens.Count).Append('\n');
            }

            int count = Math.Min(ours.Screens.Count, reference.Screens.Count);
            for (int i = 0; i < count; i++)
            {
                List<string> differences = ours.Screens[i].DifferencesFrom(reference.Screens[i], "s3270");
                for (int d = 0; d < differences.Count; d++)
                {
                    report.Append(ours.Labels[i]).Append(": ").Append(differences[d]).Append('\n');
                }
            }

            if (ours.Records.Count != reference.Records.Count)
            {
                report.Append("records sent to the host: RetroTerm ").Append(ours.Records.Count).Append(", s3270 ").Append(reference.Records.Count).Append('\n');
            }

            int records = Math.Min(ours.Records.Count, reference.Records.Count);
            for (int i = 0; i < records; i++)
            {
                string mine = TelnetUnit.Hex(ours.Records[i]);
                string theirs = TelnetUnit.Hex(reference.Records[i]);

                // A query reply (AID X'88') lists the terminal's OWN features, and s3270's list is
                // a different one by design (it has partitions, RPQ names and a longer colour
                // list), so the two are not compared byte for byte; that both answered at the
                // same point is still checked by the counts and the AID. What RetroTerm's reply
                // should hold is checked against GA23-0059-4 by Ibm3270QueryReplyTests (BUGS.md
                // B15, fixed in RetroIO on 24 November 2026).
                //
                // Only a query reply: AID X'88' starts EVERY inbound structured field record, the
                // IND$FILE replies (X'D0') among them, so the first structured field's ID is read
                // too: X'81', Query Reply (AID, two length bytes, then the ID; GA23-0059-4
                // chapter 6). Until 24 November 2026 the AID alone was checked, and every file
                // transfer record went uncompared; found when a one-byte upload stayed green.
                bool bothQueryReplies = IsQueryReply(ours.Records[i]) && IsQueryReply(reference.Records[i]);
                if (mine != theirs && !bothQueryReplies)
                {
                    report.Append("record ").Append(i + 1).Append(" sent to the host:\n    RetroTerm: ").Append(mine)
                        .Append("\n    s3270:     ").Append(theirs).Append('\n');
                }
            }

            // What each IND$FILE transfer gave: the same file must reach the PC in both.
            string mineFt = string.Join(" | ", ours.Transfers);
            string theirFt = string.Join(" | ", reference.Transfers);
            if (mineFt != theirFt)
            {
                report.Append("IND$FILE transfers:\n    RetroTerm: ").Append(mineFt).Append("\n    s3270:     ").Append(theirFt).Append('\n');
            }

            // The timing-mark answers are the replay's own and not compared.
            List<string> mineTelnet = WithoutTimingMarks(ours.Telnet);
            List<string> theirTelnet = WithoutTimingMarks(reference.Telnet);
            string a = string.Join(" | ", mineTelnet);
            string b = string.Join(" | ", theirTelnet);
            if (a != b)
            {
                report.Append("telnet sent to the host:\n    RetroTerm: ").Append(a).Append("\n    s3270:     ").Append(b).Append('\n');
            }

            // Masked like every print of what a client sent (see PasswordMask). The records were
            // compared above unmasked; only the printed report is masked.
            return PasswordMask.Text(report.ToString());
        }

        // AID X'88' (structured field) and a first structured field of ID X'81', Query Reply.
        private static bool IsQueryReply(byte[] record) => record.Length >= 4 && record[0] == 0x88 && record[3] == 0x81;

        private static List<string> WithoutTimingMarks(List<string> units)
        {
            var kept = new List<string>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] != "FF FC 06" && units[i] != "FF FB 06")
                {
                    kept.Add(units[i]);
                }
            }

            return kept;
        }
    }
}
