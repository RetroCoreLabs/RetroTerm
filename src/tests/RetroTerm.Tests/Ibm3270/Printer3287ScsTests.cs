//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroTerm.Core.Protocols;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The 3287's SCS printing (TN3270E SCS-DATA), against GA23-0061-2 pp. 2-25 to 2-30, the only
    /// SCS source held. EBCDIC (code page 037): A C1, B C2, C C3, H C8, I C9, blank 40.
    /// </summary>
    public class Printer3287ScsTests
    {
        private static (Printer3287 Printer, List<string> Errors) Create()
        {
            var printer = new Printer3287();
            var errors = new List<string>();
            printer.ProtocolError += errors.Add;
            return (printer, errors);
        }

        private static string[] Lines(PrintJob job, int page)
        {
            IReadOnlyList<string> lines = job.Page(page);
            string[] all = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                all[i] = lines[i];
            }

            return all;
        }

        private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

        [Fact]
        public void NewLineEndsALineAndCarriageReturnPrintsOverIt()
        {
            var (p, errors) = Create();

            // "AB" NL, then "C" CR, blank, "B": CR returns to the left margin, the blank over the
            // C leaves it (as the 3270 printout does with CR), and B goes into column 2.
            p.ProcessScs(Hex("C1 C2 15 C3 0D 40 C2 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "AB", "CB" }, Lines(job, 0));
            Assert.Equal(HostRecordOutcome.Applied, p.LastOutcome);
            Assert.Empty(errors);
        }

        [Fact]
        public void LineFeedKeepsTheColumnAndBackSpaceStopsAtColumnOne()
        {
            var (p, errors) = Create();

            // "A" LF "B": LF "moves the print position vertically down" and leaves the column, so
            // B prints in column 2. BS BS BS "C" from column 3: two steps back to column 1, the
            // third "is inoperative".
            p.ProcessScs(Hex("C1 25 C2 16 16 16 C3 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "A", "CB" }, Lines(job, 0));
            Assert.Empty(errors);
        }

        [Fact]
        public void HorizontalTabGoesToTheStopsSetByShfAndIsASpaceWithoutOne()
        {
            var (p, errors) = Create();

            // SHF count 5: MPP 40, LM 1, RM 40, one stop at 10 (p. 2-26). "A" HT "B" HT "C": the
            // second tab has no stop to its right, so it is a space.
            p.ProcessScs(Hex("2B C1 05 28 01 28 0A C1 05 C2 05 C3 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "A        B C" }, Lines(job, 0));
            Assert.Equal(40, p.ScsMaximumPosition);
            Assert.Empty(errors);
        }

        [Fact]
        public void PastTheMaximumPrintPositionTheNextLineStarts()
        {
            var (p, _) = Create();

            // SHF MPP 3, then "ABCA": the fourth character goes to the next line.
            p.ProcessScs(Hex("2B C1 03 03 01 C1 C2 C3 C1 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "ABC", "A" }, Lines(job, 0));
        }

        [Fact]
        public void WithoutAFormLengthFormFeedIsANewLine()
        {
            var (p, errors) = Create();

            // The 3287's MPL defaults to 1, and then "the print position moves to the left margin
            // of the next line" (p. 2-26).
            p.ProcessScs(Hex("C1 0C C2 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(1, job.PageCount);
            Assert.Equal(new[] { "A", "B" }, Lines(job, 0));
            Assert.Empty(errors);
        }

        [Fact]
        public void WithAFormLengthFormFeedStartsAPageAtTheTopMargin()
        {
            var (p, errors) = Create();

            // SVF count 4: MPL 10, TM 3, BM 10 (p. 2-28). FF: the next page, lines 1 and 2 blank.
            p.ProcessScs(Hex("2B C2 04 0A 03 0A C1 0C C2 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(2, job.PageCount);
            Assert.Equal(new[] { "A" }, Lines(job, 0));
            Assert.Equal(new[] { string.Empty, string.Empty, "B" }, Lines(job, 1));
            Assert.Empty(errors);
        }

        [Fact]
        public void PassingTheBottomMarginSkipsToANewPage()
        {
            var (p, _) = Create();

            // MPL 3, TM 1, BM 2: the third New Line passes line 2.
            p.ProcessScs(Hex("2B C2 04 03 01 02 C1 15 C2 15 C3 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "A", "B" }, Lines(job, 0));
            Assert.Equal(new[] { "C" }, Lines(job, 1));
        }

        [Fact]
        public void TransparentDataIsNotScannedAndControlBytesPrintAsHyphens()
        {
            var (p, errors) = Create();

            // TRN length 3: "A", X'15' (would be NL), "B". Not scanned: the 15 is an invalid
            // graphic, printed as a hyphen (p. 2-29).
            p.ProcessScs(Hex("35 03 C1 15 C2 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "A-B" }, Lines(job, 0));
            Assert.Empty(errors);
        }

        [Fact]
        public void AnUnknownCodeIsReportedRejectedAndStopsTheRecord()
        {
            var (p, errors) = Create();

            // X'07' is not in the list on p. 2-25.
            p.ProcessScs(Hex("C1 07 C2 15"));

            Assert.Single(errors);
            Assert.Contains("X'07'", errors[0], StringComparison.Ordinal);
            Assert.Equal(HostRecordOutcome.CommandReject, p.LastOutcome);
            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "A" }, Lines(job, 0));
        }

        [Fact]
        public void ADensityThe3287DoesNotHaveIsAParameterError()
        {
            var (p, errors) = Create();

            // SLD 18 points (4 lpi) is "3289 only"; 9 points is 8 lpi (p. 2-27).
            p.ProcessScs(Hex("2B C6 02 09"));
            Assert.Equal(8, p.ScsLinesPerInch);
            Assert.Equal(HostRecordOutcome.Applied, p.LastOutcome);

            p.ProcessScs(Hex("2B C6 02 12"));
            Assert.Equal(HostRecordOutcome.OperationCheck, p.LastOutcome);
            Assert.Equal(8, p.ScsLinesPerInch);
            Assert.Single(errors);
        }

        [Fact]
        public void BellIsNamedButNotDefinedSoItIsReported()
        {
            var (p, errors) = Create();

            p.ProcessScs(Hex("2F"));

            Assert.Single(errors);
            Assert.Equal(HostRecordOutcome.CommandReject, p.LastOutcome);
        }

        [Fact]
        public void PresentationControlsAreAcceptedWithoutEffect()
        {
            var (p, errors) = Create();

            // ENP and INP "performs no function on the LU type 1 device, but it is accepted".
            p.ProcessScs(Hex("14 C1 24 15"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "A" }, Lines(job, 0));
            Assert.Empty(errors);
        }

        [Fact]
        public void ALineStillBeingPrintedBelongsToTheJob()
        {
            var (p, _) = Create();

            p.ProcessScs(Hex("C1 C2"));

            PrintJob job = Assert.IsType<PrintJob>(p.TakeJob());
            Assert.Equal(new[] { "AB" }, Lines(job, 0));
        }
    }
}
