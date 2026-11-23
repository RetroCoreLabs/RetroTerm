//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Desktop.Printing;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// RetroTerm's 3287 printer session against the live TK5 host, end to end: a printer session
    /// (IBM-3287-1, which Hercules attaches to device 00C7) through a relay that keeps every byte,
    /// the JRP console started as the JRP user (logon applid=cjrp), and two IEBGENER jobs whose
    /// SYSUT2 goes to output class G, which JRP prints on PRT4, the LU at 00C7. Each job must
    /// arrive as its own text file and its own PDF, with exactly the lines JES2 had.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The JRP steps are from the MVS TK4- Users Manual, "Operating JRP" (pages 16-17, in the TK5
    /// distribution's doc folder): logon applid=cjrp at the logon panel, a user with read access to
    /// FACILITY JRPAUTH (HERC01 and HERC02 as shipped; HERC03 gets JRP005T NOT AUTHORIZED), then
    /// PF12, PF1 and PF8. PF12 (the alternate screen size) is not pressed here: this console is a
    /// model 2, which has none, and nothing below needs it. PF7 shows PRT4's state ($DPRT4); PF9
    /// drains it again ($PPRT4), which is how the printer is left as it was found.
    /// </para>
    /// <para>
    /// <b>What TK5 sends, measured 23 November 2026</b> (also docs\IBM3270-INTRODUCTION.md,
    /// "Printing"). Plain TN3270, no TN3270E. Each JRP job of a few lines is ONE record: Erase/Write
    /// Alternate (X'7E'), WCC X'C8' (Start Print, unformatted: the lines are ended by NL orders),
    /// the lines with one NL after each and one more NL for every blank line the ASA control of the
    /// next line asks for, NLs to the end of JRP's 56-line page where an ASA '1' starts a new page
    /// and at the end of the job, and EM. No Form Feed, no telnet Abort Output, nothing at all
    /// between two jobs: the quiet time is the only thing that ends a job on TK5.
    /// </para>
    /// <para>
    /// Needs TK5 and the settings in <see cref="Tk5Settings"/>, including jrpUser and jrpPassword;
    /// outside the routine gate (Category Hercules). Jobs are submitted on the card reader port
    /// 3505 as the test user. The output folder is under the system temp folder and is deleted at
    /// the end, also when the test fails.
    /// </para>
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Tk5PrinterTests
    {
        // JRP's page length for PRT4: "LPP=056" in its $DPRT4 answer.
        private const int JrpLinesPerPage = 56;

        // The card reader TK5 reads ASCII JCL decks on (TK5 guide 13, section 6).
        private const int CardReaderPort = 3505;

        // Seconds without data that end a job: the app's default.
        private const int QuietSeconds = Ibm3287PrinterEmulator.DefaultQuietSeconds;

        // How long one job may take from SUBMIT to its file: JES2, then JRP, which looks at the
        // spool every few seconds (measured: 4 to 8 seconds), then the quiet time.
        private const int JobTimeoutMs = 120000;

        private static readonly string[] JobOneLines =
        {
            " RTT PRINTER JOB ONE LINE 1",
            " RTT PRINTER JOB ONE LINE 2",
            "0RTT PRINTER JOB ONE LINE 3 AFTER ONE BLANK LINE",
            "-RTT PRINTER JOB ONE LINE 4 AFTER TWO BLANK LINES",
            "1RTT PRINTER JOB ONE PAGE 2 LINE 1",
            " RTT PRINTER JOB ONE PAGE 2 LINE 2",
        };

        private static readonly string[] JobTwoLines =
        {
            " RTT PRINTER JOB TWO LINE 1",
            " RTT PRINTER JOB TWO LINE 2",
        };

        private readonly ITestOutputHelper _output;

        // The relay in front of the JRP console terminal, whose records say what JRP answered.
        private Tk5WireTap? _consoleTap;

        public Tk5PrinterTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TwoJesJobsOnClassGArriveAsTwoPrintFilesWithTheJobsLines()
        {
            Tk5Settings settings = Tk5Settings.Load();
            if (settings.JrpUser.Length == 0 || settings.JrpPassword.Length == 0)
            {
                throw new Xunit.Sdk.XunitException("This test needs jrpUser and jrpPassword in the TK5 settings file (a user allowed on the JRP console, HERC01 or HERC02). See Tk5Settings.cs.");
            }

            string folder = Path.Combine(Path.GetTempPath(), "retroterm-tk5-printer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                await RunAsync(settings, folder);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // The ASA carriage control in column 1 as JRP turns it into lines on its 56-line page
        // (measured, see the class remarks): ' ' next line, '0' one blank line first, '-' two,
        // '1' the rest of the page blank first; the job's last page is filled with blank lines.
        private static List<string> ExpectedLines(string[] asaLines)
        {
            var lines = new List<string>();
            for (int i = 0; i < asaLines.Length; i++)
            {
                char control = asaLines[i][0];
                if (i > 0)
                {
                    if (control == '1')
                    {
                        while (lines.Count % JrpLinesPerPage != 0)
                        {
                            lines.Add(string.Empty);
                        }
                    }
                    else
                    {
                        int blanks = control == '0' ? 1 : control == '-' ? 2 : 0;
                        for (int b = 0; b < blanks; b++)
                        {
                            lines.Add(string.Empty);
                        }
                    }
                }

                lines.Add(asaLines[i].Substring(1));
            }

            while (lines.Count % JrpLinesPerPage != 0)
            {
                lines.Add(string.Empty);
            }

            return lines;
        }

        private static void Submit(Tk5Settings settings, string job, string[] lines)
        {
            // MSGCLASS=A and SYSPRINT to A: only SYSUT2 goes to class G, so the printer gets the
            // job's own lines and nothing else. RECFM=FBA makes column 1 the ASA control.
            var jcl = new StringBuilder();
            jcl.Append("//").Append(job).Append(" JOB (ACCT),'RTT 3287',CLASS=A,MSGCLASS=A,MSGLEVEL=(1,1),\n");
            jcl.Append("//         USER=").Append(settings.User).Append(",PASSWORD=").Append(settings.Password).Append('\n');
            jcl.Append("//PRINT    EXEC PGM=IEBGENER\n");
            jcl.Append("//SYSPRINT DD SYSOUT=A\n");
            jcl.Append("//SYSIN    DD DUMMY\n");
            jcl.Append("//SYSUT2   DD SYSOUT=G,DCB=(RECFM=FBA,LRECL=80,BLKSIZE=80)\n");
            jcl.Append("//SYSUT1   DD *\n");
            for (int i = 0; i < lines.Length; i++)
            {
                jcl.Append(lines[i]).Append('\n');
            }

            // The last line must end with a newline, and closing the connection ends the deck.
            jcl.Append("/*\n//\n");
            using var client = new TcpClient(settings.Host, CardReaderPort);
            byte[] bytes = Encoding.ASCII.GetBytes(jcl.ToString());
            client.GetStream().Write(bytes, 0, bytes.Length);
            client.GetStream().Flush();
        }

        // PRT4's state in the newest JRPI101 answer in a decoded piece of the console's wire:
        // "JRPI101 PRT4     LU=CUU0C7   DRAINED ...".
        private static string Prt4State(string answer)
        {
            int at = answer.LastIndexOf("JRPI101 PRT4", StringComparison.Ordinal);
            if (at < 0)
            {
                return string.Empty;
            }

            int end = answer.IndexOf('\n', at);
            string[] words = answer.Substring(at, (end < 0 ? answer.Length : end) - at).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 3 ? words[3] : string.Empty;
        }

        // Presses a JRP console key and waits for the host's whole answer, read from the console's
        // own wire rather than its screen: JRP writes one record per message line, round a
        // wrapping message area, so where the newest line is on the screen depends on everything
        // before it (measured 23 November 2026; a first version that read the screen took the
        // command's echo for the answer). The answer ends with "JRP100I OK".
        private async Task<string> JrpKeyAsync(ITk5Terminal t, string key)
        {
            int from = _consoleTap!.Chunks().Count;
            await t.PressAsync(key);
            var clock = Stopwatch.StartNew();
            while (true)
            {
                List<Tk5WireChunk> chunks = _consoleTap.Chunks();
                string answer = Tk5WireTap.Decode(chunks.GetRange(from, chunks.Count - from), true);
                if (answer.Contains("JRP100I OK", StringComparison.Ordinal) || answer.Contains("JRP100I NO COMMAND", StringComparison.Ordinal))
                {
                    await t.WaitUntilAsync(s => true, "the keyboard after " + key, Tk5Scenario.HostTimeoutMs);
                    return answer;
                }

                if (clock.ElapsedMilliseconds > Tk5Scenario.HostTimeoutMs)
                {
                    throw new Xunit.Sdk.XunitException("No JRP100I OK from the JRP console after " + key + ". It sent:\n" + answer);
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        private async Task RunAsync(Tk5Settings settings, string folder)
        {
            await using Tk5WireTap tap = Tk5WireTap.Start(settings.Host, settings.Port);
            var output = new TextAndPdfOutput(folder);
            var printer = new Ibm3287PrinterEmulator();
            printer.Configure(output, QuietSeconds);
            using var session = new TerminalSession(printer, "TK5 printer");
            var connection = new Tn3270Connection("127.0.0.1", tap.Port, Ibm3287PrinterEmulator.TerminalType);
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

            string startState = string.Empty;
            int leftovers = 0;
            int firstChunk = 0;
            await using Tk5WireTap consoleTap = Tk5WireTap.Start(settings.Host, settings.Port);
            _consoleTap = consoleTap;
            await using (RetroTermTk5Terminal console = await RetroTermTk5Terminal.OpenAsync(settings.Through("127.0.0.1", consoleTap.Port)))
            {
                try
                {
                    startState = await StartPrt4Async(console, settings);
                    leftovers = await PrintLeftoversAsync(session, printer, tap);
                    firstChunk = tap.Chunks().Count;
                    await PrintAsync(settings, session, printer, tap, "1", JobOneLines, leftovers + 1);
                    await PrintAsync(settings, session, printer, tap, "2", JobTwoLines, leftovers + 2);
                }
                finally
                {
                    await LeaveJrpAsync(console, startState != "INACTIVE" && startState != "ACTIVE");
                    _output.WriteLine("JRP console, host to terminal:\n" + Tk5WireTap.Decode(consoleTap.Chunks(), true));
                }
            }

            await session.DisconnectAsync();
            List<Tk5WireChunk> chunks = tap.Chunks();
            _output.WriteLine("PRT4 was " + startState + " before the test.");
            _output.WriteLine("Every byte TK5 sent the printer, decoded:\n" + Tk5WireTap.Decode(chunks, true));
            _output.WriteLine("Every byte the printer sent TK5, decoded:\n" + Tk5WireTap.Decode(chunks, false));

            _output.WriteLine("Jobs left on class G by earlier runs, printed and not checked: " + leftovers);
            AssertTheWire(HostBytesFrom(chunks, firstChunk));
            AssertTheFiles(printer, output, leftovers);
        }

        // Logs on to the JRP console, notes PRT4's state, and starts it when it is not started.
        private async Task<string> StartPrt4Async(ITk5Terminal t, Tk5Settings settings)
        {
            // The logon panel, or Hercules' information panel first, which the host replaces on its
            // own (Tk5CommonSteps). It STAYS when JRP still holds this terminal because an earlier
            // run stopped before logging off the JRP console (seen 23 November 2026, twice, both
            // after a run of this test that failed half-way); Clear then shows that console.
            Tk5Screen first = await t.WaitUntilAsync(s => s.Contains("Logon ===>") || s.Contains("Hercules Version"), "the logon or Hercules panel", Tk5Scenario.HostTimeoutMs);
            if (!first.Contains("Logon ===>"))
            {
                try
                {
                    await t.WaitUntilAsync(s => s.Contains("Logon ===>"), "the logon panel after the Hercules panel", 10000);
                }
                catch (Xunit.Sdk.XunitException)
                {
                    _output.WriteLine("The Hercules panel stayed 10 seconds; pressing Clear.");
                    await t.PressAsync("CLEAR");
                }
            }

            // A run that stopped before its PF3 leaves JRP holding this terminal: Clear on the
            // Hercules panel then shows the JRP console itself, still logged on (seen 23 November
            // 2026). It is used as it is.
            Tk5Screen ready = await t.WaitUntilAsync(s => s.Contains("Logon ===>") || s.Contains("Enter JRP command ==>"), "the logon panel", Tk5Scenario.HostTimeoutMs);
            if (ready.Contains("Enter JRP command ==>"))
            {
                _output.WriteLine("The JRP console was still logged on from an earlier run.");
            }
            else
            {
                await LogOnToJrpAsync(t, settings);
            }

            return await StartPrt4OnTheConsoleAsync(t);
        }

        private static async Task LogOnToJrpAsync(ITk5Terminal t, Tk5Settings settings)
        {
            await t.TypeAsync("LOGON APPLID=CJRP");
            await t.PressAsync("ENTER");
            await t.WaitUntilAsync(s => s.Contains("CONTROL TERMINAL FACILITY") && s.Contains("PASSWORD ===>"),
                "the JRP CONTROL TERMINAL FACILITY logon panel", Tk5Scenario.HostTimeoutMs);
            await t.TypeAsync(settings.JrpUser);
            await t.PressAsync("TAB");
            await t.TypeAsync(settings.JrpPassword);
            await t.PressAsync("ENTER");
            await t.WaitUntilAsync(s => s.Contains("Enter JRP command ==>"), "the JRP console (a JRP005T here means the user lacks JRPAUTH)", Tk5Scenario.HostTimeoutMs);
        }

        private async Task<string> StartPrt4OnTheConsoleAsync(ITk5Terminal t)
        {
            string answer = await JrpKeyAsync(t, "PF7");
            string state = Prt4State(answer);
            _output.WriteLine("$DPRT4 before: " + state);

            // PF1: unsolicited messages, so the console shows each job begin and end on PRT4.
            await JrpKeyAsync(t, "PF1");
            if (state != "INACTIVE" && state != "ACTIVE")
            {
                answer = await JrpKeyAsync(t, "PF8");
                _output.WriteLine("$SPRT4: " + Prt4State(answer));
                Assert.Equal("INACTIVE", Prt4State(answer));
            }

            return state;
        }

        // Class G output an earlier, failed run left on the spool prints as soon as PRT4 starts
        // (seen 23 November 2026: a job JRP had begun when the run gave up came out first in the
        // next run). It is printed here and left out of the checks, and the number is reported.
        // Done when the printer has had no data for 20 seconds: JRP looked at the spool every 10
        // seconds or less in every run measured.
        private async Task<int> PrintLeftoversAsync(TerminalSession session, Ibm3287PrinterEmulator printer, Tk5WireTap tap)
        {
            var clock = Stopwatch.StartNew();
            while (tap.MillisecondsSinceHostSpoke < 20000 || clock.ElapsedMilliseconds < 20000)
            {
                if (clock.ElapsedMilliseconds > JobTimeoutMs)
                {
                    throw new Xunit.Sdk.XunitException("The printer did not go quiet after PRT4 started. Wire:\n" + Tk5WireTap.Decode(tap.Chunks(), true));
                }

                await session.RunOnSessionThreadAsync(() => { printer.CheckQuietTime(DateTime.UtcNow); });
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }

            return await session.RunOnSessionThreadAsync(() =>
            {
                printer.CheckQuietTime(DateTime.UtcNow);
                return printer.Jobs.Count;
            });
        }

        private static byte[] HostBytesFrom(List<Tk5WireChunk> chunks, int first)
        {
            var bytes = new List<byte>();
            for (int i = first; i < chunks.Count; i++)
            {
                if (chunks[i].FromHost)
                {
                    bytes.AddRange(chunks[i].Bytes);
                }
            }

            return bytes.ToArray();
        }

        // Submits one job and waits for its print file: the printer's job count reaching
        // "jobsAfter", with the quiet time checked about twice a second as the app does once.
        private async Task PrintAsync(Tk5Settings settings, TerminalSession session, Ibm3287PrinterEmulator printer, Tk5WireTap tap, string name, string[] lines, int jobsAfter)
        {
            long bytesBefore = tap.BytesFromHost;
            Submit(settings, settings.User + "P" + name, lines);
            var clock = Stopwatch.StartNew();
            while (true)
            {
                int jobs = await session.RunOnSessionThreadAsync(() =>
                {
                    printer.CheckQuietTime(DateTime.UtcNow);
                    return printer.Jobs.Count;
                });
                if (jobs >= jobsAfter)
                {
                    _output.WriteLine("Job " + name + " ended after " + clock.ElapsedMilliseconds + " ms.");
                    return;
                }

                if (clock.ElapsedMilliseconds > JobTimeoutMs)
                {
                    throw new Xunit.Sdk.XunitException("Job " + name + ": no print file after " + JobTimeoutMs + " ms. The host sent "
                        + (tap.BytesFromHost - bytesBefore) + " bytes to the printer since the submit; printer problem: "
                        + (printer.LastProblem ?? "none") + ". Host to printer:\n" + Tk5WireTap.Decode(tap.Chunks(), true)
                        + "Printer to host:\n" + Tk5WireTap.Decode(tap.Chunks(), false));
                }

                await Task.Delay(500, TestContext.Current.CancellationToken);
            }
        }

        // Puts PRT4 back as it was found: drained again when the test started it (TK5 starts it
        // drained: TK4- manual, "PRT4, however, will come up drained"), left alone when it was
        // already started. Then logs off the JRP console. A failure here goes to the output; the
        // original failure, if any, stands.
        private async Task LeaveJrpAsync(ITk5Terminal t, bool drain)
        {
            try
            {
                Tk5Screen screen = await t.ReadAsync();
                if (!screen.Contains("Enter JRP command ==>"))
                {
                    return;
                }

                if (drain)
                {
                    try
                    {
                        string answer = await JrpKeyAsync(t, "PF9");
                        _output.WriteLine("PRT4 after $PPRT4: " + Prt4State(answer));
                        Assert.Equal("DRAINED", Prt4State(answer));
                    }
                    catch (Xunit.Sdk.XunitException e)
                    {
                        // Said, and the logoff below still happens: a console left logged on
                        // holds the terminal for the next run.
                        _output.WriteLine("No complete answer to PF9 ($PPRT4) on the console: " + e.Message);
                    }
                }

                await t.PressAsync("PF3");
                await t.WaitUntilAsync(s => s.Contains("Logon ===>"), "the logon panel after the JRP logoff", Tk5Scenario.HostTimeoutMs);
            }
            catch (Exception e) when (e is Xunit.Sdk.XunitException || e is InvalidOperationException || e is IOException)
            {
                _output.WriteLine("Could not log off the JRP console: " + e.Message);
            }
        }
        // What crossed the wire, measured: the records and nothing else. No Abort Output (IAC AO),
        // no other telnet command, every record an Erase/Write Alternate with Start Print.
        private static void AssertTheWire(byte[] host)
        {
            // Past the negotiation (IAC DO TERMINAL-TYPE ... IAC DO END-OF-RECORD), the host sends
            // only records ended by IAC EOR. Any other IAC is a telnet command and is reported.
            var records = new List<byte[]>();
            var record = new List<byte>();
            int i = 0;
            while (i < host.Length)
            {
                if (host[i] != 0xFF)
                {
                    record.Add(host[i]);
                    i++;
                    continue;
                }

                byte next = i + 1 < host.Length ? host[i + 1] : (byte)0;
                if (next == 0xFF)
                {
                    record.Add(0xFF);
                }
                else if (next == 0xEF)
                {
                    records.Add(record.ToArray());
                    record.Clear();
                }
                else if (next == 0xFA)
                {
                    // A subnegotiation: skip to IAC SE. Only TERMINAL-TYPE SEND is expected.
                    int end = i + 2;
                    while (end + 1 < host.Length && !(host[end] == 0xFF && host[end + 1] == 0xF0))
                    {
                        end++;
                    }

                    Assert.Equal(0x18, host[i + 2]);
                    i = end + 2;
                    continue;
                }
                else if (next >= 0xFB && next <= 0xFE)
                {
                    i += 3;
                    continue;
                }
                else
                {
                    Assert.Fail("TK5 sent telnet command X'FF " + next.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "' to the printer (X'F5' is Abort Output); none was ever measured.");
                }

                i += 2;
            }

            Assert.Empty(record);
            Assert.Equal(2, records.Count);
            for (int r = 0; r < records.Count; r++)
            {
                Assert.True(records[r].Length >= 3, "record " + r + " is too short");
                Assert.Equal(0x7E, records[r][0]);
                Assert.Equal(0xC8, records[r][1]);
                Assert.Equal(0x19, records[r][records[r].Length - 1]);
            }
        }

        private void AssertTheFiles(Ibm3287PrinterEmulator printer, TextAndPdfOutput output, int leftovers)
        {
            Assert.Null(printer.LastProblem);
            Assert.Equal(leftovers + 2, printer.Jobs.Count);
            Assert.Equal(leftovers + 2, output.TextFiles.Count);
            Assert.Equal(leftovers + 2, output.PdfFiles.Count);

            string[][] jobs = { JobOneLines, JobTwoLines };
            for (int n = 0; n < jobs.Length; n++)
            {
                int j = leftovers + n;
                Assert.False(printer.Jobs[j].Failed, printer.Jobs[j].Result);
                Assert.Equal(QuietSeconds + " seconds with no data", printer.Jobs[j].EndedBy);

                // The text file, byte for byte: the expected lines, each ended by the system line
                // end, one page (JRP sends no Form Feed, so there is no page break character).
                List<string> expected = ExpectedLines(jobs[n]);
                var text = new StringBuilder();
                for (int l = 0; l < expected.Count; l++)
                {
                    text.Append(expected[l]).Append(Environment.NewLine);
                }

                string written = File.ReadAllText(output.TextFiles[j]);
                _output.WriteLine("Job " + (n + 1) + " text file " + output.TextFiles[j] + ", " + expected.Count + " lines expected.");
                Assert.Equal(text.ToString(), written);

                // The PDF: a PDF, not empty, and ONE sheet even for the two-page job: JRP marks its
                // page end with New Lines, not a Form Feed, and the PDF breaks only on Form Feeds.
                byte[] pdf = File.ReadAllBytes(output.PdfFiles[j]);
                Assert.True(pdf.Length > 200, "PDF " + output.PdfFiles[j] + " has only " + pdf.Length + " bytes");
                Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
                Assert.Equal(1, printer.Jobs[j].Pages);
                Assert.Equal(1, PdfPageCount(pdf));
            }
        }

        // "/Type /Page" not followed by "s": one per sheet (as Avalonia\Ibm3287PrinterTests counts).
        private static int PdfPageCount(byte[] pdf)
        {
            string text = Encoding.Latin1.GetString(pdf);
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf("/Type /Page", at, StringComparison.Ordinal)) >= 0)
            {
                at += "/Type /Page".Length;
                if (at >= text.Length || text[at] != 's')
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Writes each job both as the text file and as the PDF the app makes, so one run checks
        /// both outputs. Called on the session thread, one job at a time.
        /// </summary>
        private sealed class TextAndPdfOutput : IPrintJobOutput
        {
            private readonly TextPrintJobOutput _text;
            private readonly PdfPrintJobOutput _pdf;

            public TextAndPdfOutput(string folder)
            {
                _text = new TextPrintJobOutput(folder);
                _pdf = new PdfPrintJobOutput(folder);
            }

            public List<string> TextFiles { get; } = new List<string>();

            public List<string> PdfFiles { get; } = new List<string>();

            public string Description => _text.Description + " and " + _pdf.Description;

            public string Write(PrintJob job, int number, DateTime localTime)
            {
                string text = _text.Write(job, number, localTime);
                TextFiles.Add(text);
                PdfFiles.Add(_pdf.Write(job, number, localTime));
                return text;
            }
        }
    }
}
