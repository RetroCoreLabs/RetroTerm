//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using RetroTerm.Tests.Oracle;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Which DFT buffer sizes TK5's IND$FILE takes (IND-FILE-TRANSFER.md open question 2): a
    /// 100,000-byte text file up and back with s3270, which tells the host the size given to its
    /// Transfer action as LIMIN and LIMOUT, through <see cref="Tk5WireTap"/> so the Query Reply,
    /// the Open forms and every data block can be read from the bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// s3270, not RetroTerm, because RetroTerm's size is fixed at 16384 (Ibm3270Emulator,
    /// FileTransferBufferSize) and the question is what the HOST does with each size. RetroTerm at
    /// 16384 is measured by the file-transfer-large recording (HostStreamRecorderTests).
    /// </para>
    /// <para>
    /// Needs TK5 running (category Hercules). The data set is user.RTTBS.TEXT, deleted first in
    /// case an earlier run stopped half-way, and deleted again at the end.
    /// </para>
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Tk5TransferBufferSizeTests
    {
        private const string DataSet = "RTTBS.TEXT";

        private readonly ITestOutputHelper _output;

        public Tk5TransferBufferSizeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(16384)]
        [InlineData(32639)]
        [InlineData(32767)]
        public async Task ATextFileGoesUpAndComesBackWithThisBufferSize(int bufferSize)
        {
            Tk5Settings settings = Tk5Settings.Load();
            byte[] file = TransferTestFiles.Text(100000);
            string up = Path.Combine(Path.GetTempPath(), "rtt-bs-up-" + Guid.NewGuid().ToString("N") + ".txt");
            string down = Path.Combine(Path.GetTempPath(), "rtt-bs-down-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllBytes(up, file);
            var answers = new List<string>();
            List<Tk5WireChunk> chunks;
            try
            {
                await using (Tk5WireTap tap = Tk5WireTap.Start(settings.Host, settings.Port))
                {
                    await using (S3270Terminal s3270 = await S3270Terminal.OpenAsync(settings.Through("127.0.0.1", tap.Port)))
                    {
                        var steps = new List<Tk5Step>();
                        Tk5CommonSteps.AddLogon(steps, settings);
                        steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
                        steps.Add(Tk5CommonSteps.Tso("clean up " + DataSet, "DELETE " + DataSet, string.Empty, false));
                        steps.Add(Transfer(true, up, bufferSize, answers));
                        steps.Add(Tk5CommonSteps.Tso("LISTDS", "LISTDS " + DataSet, settings.User + "." + DataSet, false));
                        steps.Add(Transfer(false, down, bufferSize, answers));
                        steps.Add(Tk5CommonSteps.Tso("delete the data set", "DELETE " + DataSet, "READY", false));
                        steps.Add(Tk5CommonSteps.Logoff());
                        await Tk5Scenario.RunAsync(s3270, steps, _output);
                    }

                    chunks = tap.Chunks();
                }

                List<string> wire = TransferWire.Summary(chunks);
                for (int i = 0; i < wire.Count; i++)
                {
                    _output.WriteLine(wire[i]);
                }

                Assert.Equal(2, answers.Count);
                Assert.Equal(Convert.ToHexString(file), Convert.ToHexString(File.ReadAllBytes(down)));
            }
            finally
            {
                File.Delete(up);
                File.Delete(down);
            }
        }

        // Clear, then s3270's Transfer action, which types IND$FILE at READY itself.
        private Tk5Step Transfer(bool upload, string path, int bufferSize, List<string> answers)
        {
            return new Tk5Step(upload ? "Transfer send" : "Transfer receive", async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                string answer = await ((S3270Terminal)t).TransferAsync("Direction=" + (upload ? "send" : "receive") +
                    ",HostFile=" + DataSet + ",\"LocalFile=" + path.Replace('\\', '/') + "\",Host=tso,Mode=ascii,Exist=replace,BufferSize=" +
                    bufferSize.ToString(CultureInfo.InvariantCulture));
                _output.WriteLine("Transfer answered: " + answer);
                answers.Add(answer);
            }, s => Tk5CommonSteps.LastLineIsReady(s), "READY after the transfer", false);
        }
    }

    /// <summary>
    /// The file transfer part of a tapped session, one line per 3270 record that carries a DDM
    /// query reply or an X'D0' structured field: who sent it, and each field's ID and length.
    /// </summary>
    internal static class TransferWire
    {
        public static List<string> Summary(List<Tk5WireChunk> chunks)
        {
            var lines = new List<string>();
            var hostUnits = new List<TelnetUnit>();
            var clientUnits = new List<TelnetUnit>();
            byte[] hostRest = Array.Empty<byte>();
            byte[] clientRest = Array.Empty<byte>();
            for (int c = 0; c < chunks.Count; c++)
            {
                Tk5WireChunk chunk = chunks[c];
                if (chunk.Note != null)
                {
                    continue;
                }

                var units = new List<TelnetUnit>();
                byte[] rest = chunk.FromHost ? hostRest : clientRest;
                var joined = new byte[rest.Length + chunk.Bytes.Length];
                rest.CopyTo(joined, 0);
                chunk.Bytes.CopyTo(joined, rest.Length);
                TelnetUnit.Split(joined, units, out byte[] left);
                if (chunk.FromHost)
                {
                    hostRest = left;
                }
                else
                {
                    clientRest = left;
                }

                for (int u = 0; u < units.Count; u++)
                {
                    if (units[u].Kind == TelnetUnitKind.Record)
                    {
                        string line = Describe(chunk.FromHost, units[u].Payload);
                        if (line.Length > 0)
                        {
                            lines.Add(line);
                        }
                    }
                }
            }

            return lines;
        }

        // A host Write Structured Field (X'F3') or an inbound X'88' record: each field's length
        // and its first bytes. Empty when no field is a file transfer field or the DDM reply.
        private static string Describe(bool fromHost, byte[] record)
        {
            if (record.Length < 3 || (fromHost && record[0] != 0xF3) || (!fromHost && record[0] != 0x88))
            {
                // Every other record too, by its FIRST BYTE ONLY (the host command or the AID), so
                // a Read Modified or a key the transfer did not expect shows between the transfer
                // records. Never more: an inbound record carries what was typed, and one of those
                // is the password (measured 24 November 2026: twelve bytes of the logon record
                // were enough to show part of it).
                return (fromHost ? "host  " : "client ") + record[0].ToString("X2", CultureInfo.InvariantCulture) + "  record " + record.Length + " bytes";
            }

            var text = new StringBuilder(fromHost ? "host  WSF" : "client 88");
            bool wanted = false;
            int pos = 1;
            while (pos + 2 <= record.Length)
            {
                int length = (record[pos] << 8) | record[pos + 1];
                if (length < 3 || pos + length > record.Length)
                {
                    text.Append(" [field length ").Append(length).Append(" at ").Append(pos).Append(" does not fit the record]");
                    wanted = true;
                    break;
                }

                bool ft = record[pos + 2] == 0xD0;
                bool ddm = !fromHost && length >= 4 && record[pos + 2] == 0x81 && record[pos + 3] == 0x95;
                if (ft || ddm)
                {
                    wanted = true;
                    int head = Math.Min(length, ddm ? length : 16);
                    text.Append("  [").Append(length).Append(": ").Append(Convert.ToHexString(record, pos, head))
                        .Append(head < length ? " ..." : string.Empty).Append(']');
                }

                pos += length;
            }

            text.Append("  record ").Append(record.Length).Append(" bytes");
            if (!wanted)
            {
                // An AID X'88' record with no file transfer field, or a WSF with none: its length
                // only, never its bytes (a query reply is the usual one).
                return (fromHost ? "host  WSF" : "client 88") + " with no file transfer field, record " + record.Length + " bytes";
            }

            return text.ToString();
        }
    }
}
