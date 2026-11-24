//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Threading.Tasks;

using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// A screen larger than the model's on TK5: a model 2 with a 100 x 50 alternate screen,
    /// announced as IBM-DYNAMIC (RFC 2355 section 7.1), against s3270 started with
    /// "-oversize 100x50" on the same host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured 24 November 2026, the records in order: the Hercules panel and the VTAM logon
    /// panel come as Erase/Write (X'F5'), on the default 24 x 80. After the user id the host sends
    /// Read Partition Query (F3 00 05 01 FF 02); RetroTerm answers with Usable Area 100 x 50
    /// (00 17 81 81 ... 00 64 00 32) and Implicit Partition default 80 x 24, alternate 100 x 50
    /// (00 11 81 A6 00 00 0B 01 00 00 50 00 18 00 64 00 32). From the password prompt on, TSO
    /// writes with Erase/Write Alternate (X'7E') and 14-bit addresses: the prompt's SBA is 13 87,
    /// address 4999, the last cell of 100 x 50. ISPF's menu is also Erase/Write Alternate, its
    /// title line padded to 100 columns (the next SBA is C1 E4, address 100).
    /// </para>
    /// <para>
    /// Needs TK5 running and s3270 (the s3270 entry in tk5-tests.json); category Hercules, outside
    /// the routine gate. Only the top-left 24 x 80 is compared (Tk5Screen), plus the screen size
    /// each terminal is on at the ISPF menu.
    /// </para>
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Tk5OversizeTests
    {
        private readonly ITestOutputHelper _output;

        public Tk5OversizeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ADynamicScreenOf100By50RunsIspfOn100By50AsS3270Does()
        {
            Tk5Settings settings = Tk5Settings.Load();
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);
            int ispf = steps.Count - 1;
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", false));
            steps.Add(Tk5CommonSteps.Logoff());

            string retroTermSize = string.Empty;
            List<Tk5Screen> retroTerm;
            await using (RetroTermTk5Terminal terminal = await RetroTermTk5Terminal.OpenAsync(settings, new Ibm3270Emulator(2, 100, 50)))
            {
                Assert.Equal(Ibm3270Emulator.DynamicTerminalType, terminal.Emulator.TerminalType);
                var withSize = new List<Tk5Step>(steps);
                withSize.Insert(ispf + 1, new Tk5Step("read the size", async t =>
                {
                    (int rows, int columns) = await terminal.Session.RunOnSessionThreadAsync(() => (terminal.Emulator.Display.Rows, terminal.Emulator.Display.Columns));
                    retroTermSize = columns + "x" + rows;
                }, s => true, "the size", false));
                retroTerm = await Tk5Scenario.RunAsync(terminal, withSize, _output);
                retroTerm.RemoveAt(ispf + 1);
                _output.WriteLine(terminal.WireText());
            }

            List<Tk5Screen> reference;
            await using (S3270Terminal s3270 = await S3270Terminal.OpenAsync(settings, "3278-2", "-oversize 100x50"))
            {
                reference = await Tk5Scenario.RunAsync(s3270, steps, _output);
            }

            // s3270's status fields 7 and 8 are the rows and columns of the screen in use.
            string[] status = reference[ispf].Status.Split(' ');
            string s3270Size = status[7] + "x" + status[6];
            _output.WriteLine("ISPF: RetroTerm on " + retroTermSize + ", s3270 on " + s3270Size);
            Assert.Equal("100x50", s3270Size);
            Assert.Equal("100x50", retroTermSize);

            // The text of the top-left 24 x 80 of every compared screen.
            var report = new System.Text.StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Compare)
                {
                    continue;
                }

                for (int r = 0; r < Tk5Screen.Rows; r++)
                {
                    string mine = retroTerm[i].MaskedLine(r);
                    string theirs = reference[i].MaskedLine(r);
                    if (mine != theirs)
                    {
                        report.Append("step '").Append(steps[i].Name).Append("' row ").Append(r + 1).Append(":\n  RetroTerm: [")
                            .Append(mine.TrimEnd()).Append("]\n  s3270: [").Append(theirs.TrimEnd()).Append("]\n");
                    }
                }
            }

            Assert.True(report.Length == 0, "RetroTerm and s3270 differ:\n" + report);
        }
    }
}
