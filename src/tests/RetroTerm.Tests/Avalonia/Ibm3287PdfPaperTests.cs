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

using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Protocols;
using RetroTerm.Desktop.Printing;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.Ibm3270;

using SkiaSharp;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The 3287 PDF's sheets: "Lines per page" (where the sheets break) and the tractor paper look
/// (how a sheet is drawn). Neither may change the text file.
/// </summary>
public sealed class Ibm3287PdfPaperTests : IDisposable
{
    // Pixels per point for the rendered sheet: 2 is 144 dots an inch, enough to see a hole.
    private const float Scale = 2f;

    // A folder of its own in %TEMP%, removed afterwards; never the user's Documents.
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RetroTerm.Tests.Printer3287Paper." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static List<IReadOnlyList<string>> Pages(params int[] lineCounts)
    {
        var pages = new List<IReadOnlyList<string>>();
        for (int p = 0; p < lineCounts.Length; p++)
        {
            var lines = new List<string>();
            for (int i = 0; i < lineCounts[p]; i++)
            {
                lines.Add("P" + p + " L" + i);
            }

            pages.Add(lines);
        }

        return pages;
    }

    private static int[] Counts(List<IReadOnlyList<string>> sheets)
    {
        var counts = new int[sheets.Count];
        for (int i = 0; i < sheets.Count; i++)
        {
            counts[i] = sheets[i].Count;
        }

        return counts;
    }

    [Fact]
    public void ANewSheetStartsAfterEveryNLines()
    {
        var sheets = PdfSheets.Split(Pages(7), 3);
        Assert.Equal(new[] { 3, 3, 1 }, Counts(sheets));
        Assert.Equal("P0 L3", sheets[1][0]);
        Assert.Equal("P0 L6", sheets[2][0]);
    }

    [Fact]
    public void AFormFeedStartsTheCountAgain()
    {
        // 5 then FF then 5, at 3 a sheet: 3 2 | 3 2. Counting on through the FF would give 3 2 | 1 3 1.
        Assert.Equal(new[] { 3, 2, 3, 2 }, Counts(PdfSheets.Split(Pages(5, 5), 3)));
    }

    [Fact]
    public void ExactlyNLinesThenAFormFeedGivesNoEmptySheet()
    {
        Assert.Equal(new[] { 3, 2 }, Counts(PdfSheets.Split(Pages(3, 2), 3)));

        // And exactly N times two at the end of the job: two sheets, not three.
        Assert.Equal(new[] { 3, 3 }, Counts(PdfSheets.Split(Pages(6), 3)));
    }

    [Fact]
    public void NoLinesPerPageKeepsTheFormFeedPagesAsTheyAre()
    {
        Assert.Equal(new[] { 7, 0, 2 }, Counts(PdfSheets.Split(Pages(7, 0, 2), null)));
        Assert.Equal(new[] { 7, 0, 2 }, Counts(PdfSheets.Split(Pages(7, 0, 2), 0)));

        // An empty page the host ejected is still one empty sheet with N set.
        Assert.Equal(new[] { 3, 1, 0 }, Counts(PdfSheets.Split(Pages(4, 0), 3)));
    }

    // A TK5 JRP job as measured (docs\IBM3270-INTRODUCTION.md section 11): one record, 112
    // New Line ended lines, two pages of 56, and no Form Feed.
    private static PrintJob Tk5TwoPageJob()
    {
        var parts = new List<object>();
        for (int i = 0; i < 112; i++)
        {
            parts.Add(i % 56 == 0 ? "PAGE " + (i / 56 + 1) : "LINE " + i);
            parts.Add((byte)0x15);
        }

        parts.Add((byte)0x19);
        var printer = new Printer3287();
        printer.ProcessRecord(Printer3287Tests.Record(Command3270.EraseWrite, 0x08, parts.ToArray()));
        PrintJob? job = printer.TakeJob();
        Assert.NotNull(job);
        Assert.Equal(1, job.PageCount);
        return job;
    }

    [Fact]
    public void ATk5TwoPageJobIsTwoSheetsAt56LinesAndOneWithout()
    {
        PrintJob job = Tk5TwoPageJob();
        string plain = new PdfPrintJobOutput(_folder).Write(job, 1, DateTime.Now);
        string at56 = new PdfPrintJobOutput(_folder, new PdfPaperOptions(56, false, null)).Write(job, 2, DateTime.Now);
        string tractor = new PdfPrintJobOutput(_folder, new PdfPaperOptions(56, true, null)).Write(job, 3, DateTime.Now);

        Assert.Equal(1, PdfPageCount(File.ReadAllBytes(plain)));
        Assert.Equal(2, PdfPageCount(File.ReadAllBytes(at56)));

        // The tractor PDF: two sheets of 9 1/2 x 11 inches (684 x 792 points), the job is narrow.
        byte[] pdf = File.ReadAllBytes(tractor);
        Assert.Equal(2, PdfPageCount(pdf));
        Assert.Contains("/MediaBox [0 0 684 792]", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextFileIsNotChangedByThePdfSettings()
    {
        PrintJob job = Tk5TwoPageJob();
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            PrinterFolder = _folder,
            PrinterOutput = ConnectionFactory.PrinterOutputs.Text,
            PrinterLinesPerPage = 56,
            PrinterTractorPaper = true,
        };
        IPrintJobOutput output = PrinterJobOutputFactory.Create(parameters);
        Assert.IsType<TextPrintJobOutput>(output);
        string path = output.Write(job, 1, DateTime.Now);
        string text = File.ReadAllText(path);
        Assert.Equal(job.ToText(Environment.NewLine), text);
        Assert.DoesNotContain('\f', text);

        // The PDF outputs take the settings.
        parameters.PrinterOutput = ConnectionFactory.PrinterOutputs.Pdf;
        var pdf = Assert.IsType<PdfPrintJobOutput>(PrinterJobOutputFactory.Create(parameters));
        Assert.Equal(56, pdf.Paper.LinesPerPage);
        Assert.True(pdf.Paper.TractorPaper);
    }

    [Fact]
    public void TheSheetIsNineAndAHalfInchesFor80ColumnsAndFourteenSevenEighthsFor132()
    {
        Assert.Equal((684f, 792f), TractorPaper.SheetSize(new[] { new string('X', 80) }));
        Assert.Equal((684f, 792f), TractorPaper.SheetSize(new[] { new string('X', 84) }));
        Assert.Equal((1071f, 792f), TractorPaper.SheetSize(new[] { new string('X', 85) }));
        Assert.Equal((1071f, 792f), TractorPaper.SheetSize(new[] { new string('X', 132) }));

        // 60 lines fit an 11 inch sheet below the top band and above the bottom one; 61 grow it
        // by a whole band.
        Assert.Equal(792f, TractorPaper.SheetSize(Filled(60)).Height);
        Assert.Equal(828f, TractorPaper.SheetSize(Filled(61)).Height);
    }

    private static string[] Filled(int count)
    {
        var lines = new string[count];
        for (int i = 0; i < count; i++)
        {
            lines[i] = "X";
        }

        return lines;
    }

    [Theory]
    [InlineData(null, "ibm3287-tractor-paper-blue")]
    [InlineData(ConnectionFactory.TractorPaperColours.Green, "ibm3287-tractor-paper-green")]
    public void ATractorSheetHasBandsOnTheLineGridAndHolesDownBothEdges(string? colour, string name)
    {
        // A known 132-column page: a ruler line, then numbered lines.
        var lines = new List<string>();
        var ruler = new StringBuilder();
        for (int c = 0; c < 132; c++)
        {
            ruler.Append((char)('0' + (c + 1) % 10));
        }

        lines.Add(ruler.ToString());
        for (int i = 1; i < 56; i++)
        {
            lines.Add("LINE " + (i + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "  THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG gjpqy");
        }

        (float width, float height) = TractorPaper.SheetSize(lines);
        Assert.Equal(1071f, width);
        using SKBitmap sheet = Render(lines, width, height, colour);
        SavePng(sheet, name);

        SKColor pale = TractorPaper.BandColour(colour);

        // Bands: 1/2 inch each, white first, sampled at the right-hand side where no text is.
        float x = width - TractorPaper.StripWidth - 10f;
        for (int band = 0; band < 22; band++)
        {
            float middle = band * TractorPaper.BandHeight + TractorPaper.BandHeight / 2f;
            Assert.Equal(band % 2 == 1 ? pale : SKColors.White, At(sheet, x, middle));
        }

        // A band edge falls exactly on a print line edge: just above and just below 72 points
        // (the line 3 / line 4 boundary) are different bands.
        Assert.Equal(pale, At(sheet, x, 71f));
        Assert.Equal(SKColors.White, At(sheet, x, 73f));

        // The strips are white, apart from the holes.
        Assert.Equal(SKColors.White, At(sheet, 18f, 36f));
        Assert.Equal(SKColors.White, At(sheet, 30f, 54f));

        // Holes: 22 down each side, 1/4 inch in, 1/4 inch from the top, 1/2 inch apart.
        for (int hole = 0; hole < 22; hole++)
        {
            float y = 18f + hole * 36f;
            Assert.Equal(TractorPaper.HoleColour, At(sheet, 18f, y));
            Assert.Equal(TractorPaper.HoleColour, At(sheet, width - 18f, y));

            // Inside the 5/32 inch hole near its edge, and outside it just past the edge.
            Assert.Equal(TractorPaper.HoleColour, At(sheet, 18f + 4.5f, y));
            Assert.Equal(SKColors.White, At(sheet, 18f + 7.5f, y));
        }

        // The text sits on the grid: line 0's ink is inside its slot (36 to 48 points), the top
        // band above it (0 to 36) has none, and line 1's slot has ink of its own.
        Assert.False(RowHasInk(sheet, 0f, 35f, width));
        Assert.True(RowHasInk(sheet, 37f, 47f, width));
        Assert.True(RowHasInk(sheet, 49f, 59f, width));

        // The bottom line (55) ends in its slot; the band below the last line is clear.
        float lastBottom = TractorPaper.LineTop(55) + TractorPaper.LineHeight;
        Assert.True(RowHasInk(sheet, lastBottom - 11f, lastBottom - 1f, width));
        Assert.False(RowHasInk(sheet, lastBottom + 1f, height - 1f, width));
    }

    // Renders one sheet through TractorPaper.Draw, the same call the PDF writer makes on each PDF
    // page, onto a bitmap canvas scaled from points to pixels.
    private static SKBitmap Render(IReadOnlyList<string> lines, float width, float height, string? colour)
    {
        var bitmap = new SKBitmap((int)(width * Scale), (int)(height * Scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(Scale);
        TractorPaper.Draw(canvas, width, height, lines, colour);
        return bitmap;
    }

    private static SKColor At(SKBitmap sheet, float xPoints, float yPoints)
    {
        SKColor c = sheet.GetPixel((int)(xPoints * Scale), (int)(yPoints * Scale));
        return new SKColor(c.Red, c.Green, c.Blue);
    }

    // Any dark pixel (ink, not band, hole rim or perforation) between two heights, across the area
    // between the strips.
    private static bool RowHasInk(SKBitmap sheet, float fromPoints, float toPoints, float width)
    {
        int left = (int)((TractorPaper.StripWidth + 2f) * Scale);
        int right = (int)((width - TractorPaper.StripWidth - 2f) * Scale);
        int top = (int)(fromPoints * Scale);
        int bottom = Math.Min(sheet.Height - 1, (int)(toPoints * Scale));
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                SKColor c = sheet.GetPixel(x, y);
                if (c.Red < 100 && c.Green < 100 && c.Blue < 100)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void SavePng(SKBitmap sheet, string name)
    {
        Directory.CreateDirectory(RenderedScreenshot.ImagesFolder);
        using SKImage image = SKImage.FromBitmap(sheet);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(Path.Combine(RenderedScreenshot.ImagesFolder, name + ".png"));
        data.SaveTo(file);
    }

    // "/Type /Page" not followed by "s": one per sheet.
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
}
