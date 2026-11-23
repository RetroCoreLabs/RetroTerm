//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroIO.Protocols.Tn3270;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The 3287 buffer and its printout, from byte streams, against GA23-0059-4 chapter 8
    /// (pages 8-1 and 8-2, Table 8-1).
    /// </summary>
    public class Printer3287Tests
    {
        // WCC values (Table 8-1): bits 2-3 (X'30') the line length, bit 4 (X'08') Start Print.
        private const byte Unformatted = 0x08;
        private const byte Formatted80 = 0x38;
        private const byte Formatted40 = 0x18;

        private const byte FF = 0x0C;
        private const byte CR = 0x0D;
        private const byte NL = 0x15;
        private const byte EM = 0x19;

        /// <summary>
        /// A record: command, WCC, then the parts; a string part is EBCDIC (code page 037), a byte
        /// part goes in as it is.
        /// </summary>
        internal static byte[] Record(byte command, byte wcc, params object[] parts)
        {
            var bytes = new List<byte> { command, wcc };
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is string text)
                {
                    for (int c = 0; c < text.Length; c++)
                    {
                        Assert.True(CodePage.Cp037.TryFromUnicode(text[c], out byte b));
                        bytes.Add(b);
                    }
                }
                else if (parts[i] is byte one)
                {
                    bytes.Add(one);
                }
                else if (parts[i] is byte[] many)
                {
                    bytes.AddRange(many);
                }
                else
                {
                    throw new ArgumentException("part " + i + " is neither text nor bytes");
                }
            }

            return bytes.ToArray();
        }

        // Set Buffer Address, 12-bit form (GA23-0059-4 appendix D).
        internal static byte[] Sba(int address)
        {
            Span<byte> two = stackalloc byte[2];
            BufferAddress.Encode(address, Printer3287.BufferSize, two);
            return new[] { Order3270.SetBufferAddress, two[0], two[1] };
        }

        private static PrintJob Print(params byte[][] records)
        {
            var printer = new Printer3287();
            var errors = new List<string>();
            printer.ProtocolError += errors.Add;
            for (int i = 0; i < records.Length; i++)
            {
                printer.ProcessRecord(records[i]);
            }

            Assert.Empty(errors);
            PrintJob? job = printer.TakeJob();
            Assert.NotNull(job);
            return job;
        }

        private static string[] Lines(PrintJob job, int page)
        {
            var lines = new string[job.Page(page).Count];
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = job.Page(page)[i];
            }

            return lines;
        }

        [Fact]
        public void AnEightyCharacterFormatCutsTheBufferIntoLinesOfEighty()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Formatted80, new string('A', 100)));

            Assert.Equal(1, job.PageCount);
            Assert.Equal(new[] { new string('A', 80), new string('A', 20) }, Lines(job, 0));
        }

        [Fact]
        public void AFortyCharacterFormatCutsAtForty()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Formatted40, new string('B', 50)));

            Assert.Equal(new[] { new string('B', 40), new string('B', 10) }, Lines(job, 0));
        }

        [Fact]
        public void ALineOfNullsIsLeftOutAndALineWithASpaceIsABlankLine()
        {
            // "If a line contains only null characters and one or more space characters, a line
            // feed is performed"; a line of nothing but nulls is ignored (p. 8-2).
            PrintJob job = Print(Record(Command3270.EraseWrite, Formatted80,
                Sba(0), "ABC", Sba(160), " ", Sba(240), "XYZ"));

            Assert.Equal(new[] { "ABC", string.Empty, "XYZ" }, Lines(job, 0));
        }

        [Fact]
        public void AFormFeedStartsANewPageAndItsOwnPositionIsASpace()
        {
            // "FF is valid in any buffer position"; with VFC "the first print position (the buffer
            // location containing the FF character) is printed as a space character" (p. 8-2).
            PrintJob job = Print(Record(Command3270.EraseWrite, Formatted80, "ABC", Sba(80), FF, "DEF"));

            Assert.Equal(2, job.PageCount);
            Assert.Equal(new[] { "ABC" }, Lines(job, 0));
            Assert.Equal(new[] { " DEF" }, Lines(job, 1));
        }

        [Fact]
        public void NewLineInALineLengthFormatPrintsAsASpace()
        {
            // "NL, CR, and EM are valid only ... during a printout that does not have a line-length
            // format" and "Invalid NL, CR, EM, and FF are not executed and print as spaces" (p. 8-2).
            PrintJob job = Print(Record(Command3270.EraseWrite, Formatted80, "A", NL, "B", EM, "C"));

            Assert.Equal(new[] { "A B C" }, Lines(job, 0));
        }

        [Fact]
        public void ANonDisplayFieldPrintsAsSpacesInALineWithOtherPrintData()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Formatted80,
                Order3270.StartField, (byte)0x0C, "SECRET", Order3270.StartField, (byte)0x60, "OK"));

            Assert.Equal(new[] { new string(' ', 8) + "OK" }, Lines(job, 0));
        }

        [Fact]
        public void UnformattedNewLineAndEndOfMessageShapeTheLines()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, "HELLO", NL, "WORLD", EM, "NOT PRINTED"));

            Assert.Equal(new[] { "HELLO", "WORLD" }, Lines(job, 0));
        }

        [Fact]
        public void EndOfMessageAtTheLeftMarginLeavesOutTheFinalNewLine()
        {
            // "When the print-terminating EM order appears in the first print position of the print
            // line, a final New Line is not executed" (p. 8-2).
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, "HI", NL, EM));

            Assert.Equal(new[] { "HI" }, Lines(job, 0));
        }

        [Fact]
        public void WithoutEndOfMessageTheFinalNewLineIsDoneEvenAtTheLeftMargin()
        {
            // "At the end of each printout, a final New Line is executed" (p. 8-2).
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, "HI", NL));

            Assert.Equal(new[] { "HI", string.Empty }, Lines(job, 0));
        }

        [Fact]
        public void TheCarriageWrapsByItselfAfter132Characters()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, new string('B', 140), EM));

            Assert.Equal(new[] { new string('B', 132), new string('B', 8) }, Lines(job, 0));
        }

        [Fact]
        public void CarriageReturnPrintsOverTheSameLine()
        {
            // A space leaves what is under it; a character replaces it.
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, "AB", CR, " Z", EM));

            Assert.Equal(new[] { "AZ" }, Lines(job, 0));
        }

        [Fact]
        public void AnUnformattedFormFeedStartsANewPage()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, "P1", NL, FF, "P2", EM));

            Assert.Equal(2, job.PageCount);
            Assert.Equal(new[] { "P1" }, Lines(job, 0));
            Assert.Equal(new[] { " P2" }, Lines(job, 1));
        }

        [Fact]
        public void RepeatToAddressFillsUpToTheAddress()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted,
                Order3270.RepeatToAddress, Sba(5)[1], Sba(5)[2], "Z", EM));

            Assert.Equal(new[] { "ZZZZZ" }, Lines(job, 0));
        }

        [Fact]
        public void NothingPrintsWithoutTheStartPrintBit()
        {
            // "Start-printer bit. When set to 1, initiates a printout operation" (Table 8-1, bit 4).
            var printer = new Printer3287();
            printer.ProcessRecord(Record(Command3270.EraseWrite, 0x30, "QUIET"));

            Assert.False(printer.HasOutput);
            Assert.Null(printer.TakeJob());
        }

        [Fact]
        public void AStartPrintWithoutAnErasePrintsTheBufferAgain()
        {
            // Printing does not erase the buffer; only the erase commands do (p. 3-5).
            PrintJob job = Print(
                Record(Command3270.EraseWrite, Unformatted, "X", EM),
                Record(Command3270.Write, Unformatted));

            Assert.Equal(new[] { "X", "X" }, Lines(job, 0));
        }

        [Fact]
        public void TakingTheJobStartsANewOneAndKeepsTheBuffer()
        {
            var printer = new Printer3287();
            printer.ProcessRecord(Record(Command3270.EraseWrite, Unformatted, "ONE", EM));
            PrintJob? first = printer.TakeJob();
            Assert.NotNull(first);
            Assert.Null(printer.TakeJob());

            printer.ProcessRecord(Record(Command3270.Write, Unformatted));
            PrintJob? second = printer.TakeJob();
            Assert.NotNull(second);
            Assert.Equal(new[] { "ONE" }, Lines(second, 0));
        }

        [Fact]
        public void AFormFeedAtTheVeryEndIsNotAnExtraPage()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, "LAST", NL, FF, EM));

            Assert.Equal(1, job.PageCount);
        }

        [Fact]
        public void TheJobAsTextPutsAFormFeedBetweenPages()
        {
            PrintJob job = Print(Record(Command3270.EraseWrite, Unformatted, "P1", NL, FF, "P2", EM));

            Assert.Equal("P1\n\f P2\n", job.ToText("\n"));
        }

        [Fact]
        public void AStructuredFieldIsReportedNotDropped()
        {
            var printer = new Printer3287();
            var errors = new List<string>();
            printer.ProtocolError += errors.Add;

            printer.ProcessRecord(new byte[] { Command3270.WriteStructuredField, 0x00, 0x05, 0x01, 0xFF, 0x02 });

            Assert.Single(errors);
            Assert.Contains("Write Structured Field", errors[0], StringComparison.Ordinal);
        }
    }
}
