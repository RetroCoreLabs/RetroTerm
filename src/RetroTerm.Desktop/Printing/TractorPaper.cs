//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;

using RetroTerm.Core.Protocols;

using SkiaSharp;

namespace RetroTerm.Desktop.Printing;

/// <summary>
/// How a 3287 job is laid out in its PDF: where the sheets break, and whether each sheet is drawn
/// as tractor feed (continuous form) paper. None of it changes the text file output.
/// </summary>
/// <param name="LinesPerPage">
/// A new sheet after this many lines as well as on every Form Feed; null or 0 or less for a new
/// sheet only on a Form Feed.
/// </param>
/// <param name="TractorPaper">
/// True to draw each sheet as tractor feed paper (the TractorPaper class draws it).
/// </param>
/// <param name="TractorColour">
/// One of <see cref="ConnectionFactory.TractorPaperColours"/>; anything else is blue.
/// </param>
public sealed record PdfPaperOptions(int? LinesPerPage, bool TractorPaper, string? TractorColour)
{
    /// <summary>
    /// Plain sheets that break only on a Form Feed, the behaviour before these options existed.
    /// </summary>
    public static readonly PdfPaperOptions Plain = new PdfPaperOptions(null, false, null);

    /// <summary>
    /// The options a connection's printer settings ask for.
    /// </summary>
    /// <param name="parameters">
    /// The connection.
    /// </param>
    /// <returns>
    /// The options.
    /// </returns>
    public static PdfPaperOptions From(ConnectionFactory.ConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new PdfPaperOptions(parameters.PrinterLinesPerPage, parameters.PrinterTractorPaper == true, parameters.PrinterTractorColour);
    }
}

/// <summary>
/// Splits a 3287 job's pages into PDF sheets by the "Lines per page" setting.
/// </summary>
/// <remarks>
/// <para><b>The rules (decided 23 November 2026, with Ronny's two answers)</b></para>
/// A Form Feed always starts a new sheet, and the line count starts again from 0 on it: the FF is
/// the host saying "new sheet", so the lines counted before it belong to the sheet it ended.
///
/// With N lines per page, a new sheet starts BEFORE line N + 1, never after line N. So a page whose
/// lines run to exactly N and are then followed by a Form Feed (or by the end of the job) gives one
/// sheet, not one full sheet and an empty one. The empty sheet would be paper the real printer never
/// fed: on a 3287 the N-th line leaves the paper at the top of the next form, and the Form Feed that
/// follows moves to the top of form it is already at.
///
/// A page that is empty (the host sent two Form Feeds in a row) stays one empty sheet, as before.
///
/// Why it is needed: TK5's JRP pads each page to 56 lines with New Lines and never sends a Form Feed
/// (measured, docs\IBM3270-INTRODUCTION.md section 11), so without it a two-page TK5 job is one tall
/// sheet.
/// </remarks>
public static class PdfSheets
{
    /// <summary>
    /// The sheets of a job.
    /// </summary>
    /// <param name="pages">
    /// The job's pages as the host broke them with Form Feeds, each a list of lines.
    /// </param>
    /// <param name="linesPerPage">
    /// N, or null or 0 or less for "break only on a Form Feed".
    /// </param>
    /// <returns>
    /// The sheets, each a list of lines, in print order.
    /// </returns>
    public static List<IReadOnlyList<string>> Split(IReadOnlyList<IReadOnlyList<string>> pages, int? linesPerPage)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var sheets = new List<IReadOnlyList<string>>();
        int n = linesPerPage ?? 0;
        for (int page = 0; page < pages.Count; page++)
        {
            IReadOnlyList<string> lines = pages[page];
            if (n <= 0 || lines.Count <= n)
            {
                sheets.Add(lines);
                continue;
            }

            // The count restarts at every Form Feed, because each page is counted on its own.
            for (int first = 0; first < lines.Count; first += n)
            {
                int count = Math.Min(n, lines.Count - first);
                var sheet = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    sheet.Add(lines[first + i]);
                }

                sheets.Add(sheet);
            }
        }

        return sheets;
    }
}

/// <summary>
/// Draws one PDF sheet of a 3287 job as continuous form paper: white and pale bands behind the
/// print lines, a tear-off strip down each side with round sprocket holes, and the faint perforation
/// between the strip and the sheet. The holes are drawn, not cut.
/// </summary>
/// <remarks>
/// <para><b>Where the measurements come from (looked up 23 November 2026)</b></para>
///  - Sheet 14 7/8 x 11 inches for 132 columns at 10 characters an inch, "including tractor feed
///    holes on the edges"; bands 1/2 inch, "3 lines at 6 lines per inch":
///    https://www.pdp8online.com/images/greenbar.shtml . The same 14 7/8 x 11 size with 1/2 inch
///    green bar is still sold (Staples item 177162, Grand and Toy catalogue).
///  - Sprocket holes 5/32 inch across at 1/2 inch centre to centre:
///    https://en.wikipedia.org/wiki/Continuous_stationery . The Univac form specification UP-7749
///    (bitsavers, univac/system_80_and_series_90) gives the hole as 0.155 inch, the pitch as
///    0.500 inch and the hole centre line 0.250 inch from the edge of the form; a Printronix P5000
///    manual gives 0.156 inch and 0.236 inch. 5/32 inch, 1/2 inch and 1/4 inch are used here.
///  - The 9 1/2 x 11 inch sheet with a 1/2 inch strip on each side, which leaves 8 1/2 x 11 when
///    the strips are torn off: the 9.5 x 11 size is listed in the Wikipedia page above, and a state
///    tender for 9 1/2 x 11 forms asks for "a vertical perforation 1/2 inch from left side and 1/2
///    inch from right side for removal of pin feed holes". The 1/2 inch strip is used on both sheet
///    widths (NOT measured on a 14 7/8 inch sheet; it is the same tractor, so it is assumed to be the
///    same strip).
///
/// <para><b>Which sheet</b></para>
/// 9 1/2 inches wide when the widest line fits in the 8 1/2 inches between the strips, otherwise
/// 14 7/8 inches. Text starts one column in from the perforation, so the narrow sheet takes up to
/// 84 columns (every 80-column job) and the wide one up to 137 (every 132-column job). A line wider still makes the sheet wider, as the plain PDF does, rather than
/// being cut. 11 inches tall, 66 lines at 6 an inch; a sheet with more lines than fit grows taller
/// in whole bands.
///
/// <para><b>The grid</b></para>
/// Everything is on the 6 lines an inch grid (12 points a line): the bands are 3 lines (36 points)
/// from the top edge of the sheet, the first band white, and print line i sits in the line slot that
/// starts half an inch (one band) down, so every band holds exactly three print lines.
///
/// <para><b>One drawing for the PDF and the tests</b></para>
/// <see cref="Draw"/> is what the PDF writer calls on each page's canvas, and the tests call the
/// same method on a bitmap canvas to look at the pixels. There is no second copy of it.
///
/// Only SkiaSharp calls present in both 2.88.9 (Avalonia's) and 3.119.1 (the tests') are used; see
/// <see cref="PdfPrintSink"/> for why that matters.
/// </remarks>
public static class TractorPaper
{
    /// <summary>
    /// Points in an inch.
    /// </summary>
    public const float Inch = 72f;

    /// <summary>
    /// One print line at 6 lines an inch, in points.
    /// </summary>
    public const float LineHeight = Inch / 6f;

    /// <summary>
    /// One column at 10 characters an inch, in points.
    /// </summary>
    public const float ColumnWidth = Inch / 10f;

    /// <summary>
    /// A band: 1/2 inch, three print lines.
    /// </summary>
    public const float BandHeight = Inch / 2f;

    /// <summary>
    /// The tear-off strip down each side, 1/2 inch.
    /// </summary>
    public const float StripWidth = Inch / 2f;

    /// <summary>
    /// The sprocket hole centre's distance from the sheet edge, 1/4 inch.
    /// </summary>
    public const float HoleCentreFromEdge = Inch / 4f;

    /// <summary>
    /// The sprocket hole's diameter, 5/32 inch.
    /// </summary>
    public const float HoleDiameter = Inch * 5f / 32f;

    /// <summary>
    /// Sprocket holes centre to centre, 1/2 inch; the first is 1/4 inch from the top.
    /// </summary>
    public const float HolePitch = Inch / 2f;

    /// <summary>
    /// The narrow sheet, 9 1/2 inches.
    /// </summary>
    public const float NarrowWidth = Inch * 9.5f;

    /// <summary>
    /// The wide sheet, 14 7/8 inches.
    /// </summary>
    public const float WideWidth = Inch * (14f + 7f / 8f);

    /// <summary>
    /// The sheet height, 11 inches.
    /// </summary>
    public const float SheetHeight = Inch * 11f;

    /// <summary>
    /// The first print line's slot starts one band down.
    /// </summary>
    public const float TopMargin = BandHeight;

    /// <summary>
    /// Text starts one column in from the perforation.
    /// </summary>
    public const float TextLeft = StripWidth + ColumnWidth;

    /// <summary>
    /// The pale blue band.
    /// </summary>
    public static readonly SKColor PaleBlue = new SKColor(0xD6, 0xE6, 0xF7);

    /// <summary>
    /// The pale green band.
    /// </summary>
    public static readonly SKColor PaleGreen = new SKColor(0xD5, 0xEE, 0xD5);

    /// <summary>
    /// The inside of a drawn hole: a light grey, as if the table showed through.
    /// </summary>
    public static readonly SKColor HoleColour = new SKColor(0xB4, 0xB4, 0xB4);

    /// <summary>
    /// The band colour for a setting.
    /// </summary>
    /// <param name="colour">
    /// One of <see cref="ConnectionFactory.TractorPaperColours"/>; anything else is blue.
    /// </param>
    /// <returns>
    /// The pale colour.
    /// </returns>
    public static SKColor BandColour(string? colour) =>
        ConnectionFactory.TractorPaperColours.Normalise(colour) == ConnectionFactory.TractorPaperColours.Green ? PaleGreen : PaleBlue;

    /// <summary>
    /// The sheet size for a page of lines.
    /// </summary>
    /// <param name="lines">
    /// The page's lines.
    /// </param>
    /// <returns>
    /// The width and height in points.
    /// </returns>
    public static (float Width, float Height) SheetSize(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int widest = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            widest = Math.Max(widest, lines[i].Length);
        }

        float textRight = TextLeft + widest * ColumnWidth;
        float width = textRight + StripWidth <= NarrowWidth ? NarrowWidth : WideWidth;
        if (textRight + StripWidth > width)
        {
            width = textRight + ColumnWidth + StripWidth;
        }

        float height = SheetHeight;
        float needed = TopMargin + lines.Count * LineHeight + BandHeight;
        if (needed > height)
        {
            // Grow in whole bands so the band pattern stays whole.
            height = MathF.Ceiling(needed / BandHeight) * BandHeight;
        }

        return (width, height);
    }

    /// <summary>
    /// The top of print line <paramref name="line"/>'s slot, in points from the top of the sheet.
    /// </summary>
    /// <param name="line">
    /// The line, from 0.
    /// </param>
    /// <returns>
    /// The slot's top edge.
    /// </returns>
    public static float LineTop(int line) => TopMargin + line * LineHeight;

    /// <summary>
    /// Draws one sheet: the paper, then the print lines on it.
    /// </summary>
    /// <param name="canvas">
    /// The page canvas, in points, as big as <see cref="SheetSize"/> said.
    /// </param>
    /// <param name="width">
    /// The sheet width in points.
    /// </param>
    /// <param name="height">
    /// The sheet height in points.
    /// </param>
    /// <param name="lines">
    /// The print lines, top to bottom.
    /// </param>
    /// <param name="colour">
    /// One of <see cref="ConnectionFactory.TractorPaperColours"/>.
    /// </param>
    public static void Draw(SKCanvas canvas, float width, float height, IReadOnlyList<string> lines, string? colour)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(lines);
        canvas.Clear(SKColors.White);

        // The bands run across the sheet between the two strips; the strips stay plain white.
        using (var band = new SKPaint { Color = BandColour(colour), IsAntialias = false, Style = SKPaintStyle.Fill })
        {
            for (float top = BandHeight; top < height; top += 2 * BandHeight)
            {
                canvas.DrawRect(new SKRect(StripWidth, top, width - StripWidth, Math.Min(top + BandHeight, height)), band);
            }
        }

        // The perforation between each strip and the sheet: a faint dashed line.
        using (var perforation = new SKPaint
        {
            Color = new SKColor(0xA0, 0xA0, 0xA0),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.5f,
            PathEffect = SKPathEffect.CreateDash(new[] { 2f, 2f }, 0f),
        })
        {
            canvas.DrawLine(StripWidth, 0, StripWidth, height, perforation);
            canvas.DrawLine(width - StripWidth, 0, width - StripWidth, height, perforation);
        }

        // The sprocket holes, 1/4 inch in from each edge, the first 1/4 inch from the top so the
        // holes repeat the same way on every 11 inch sheet (22 of them per side).
        using (var hole = new SKPaint { Color = HoleColour, IsAntialias = true, Style = SKPaintStyle.Fill })
        using (var rim = new SKPaint { Color = new SKColor(0x80, 0x80, 0x80), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f })
        {
            float radius = HoleDiameter / 2f;
            for (float y = HolePitch / 2f; y < height; y += HolePitch)
            {
                canvas.DrawCircle(HoleCentreFromEdge, y, radius, hole);
                canvas.DrawCircle(HoleCentreFromEdge, y, radius, rim);
                canvas.DrawCircle(width - HoleCentreFromEdge, y, radius, hole);
                canvas.DrawCircle(width - HoleCentreFromEdge, y, radius, rim);
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        // The same face and size as the plain PDF (PdfPrintSink): a monospaced face, the line pitch
        // less its leading. The baseline sits 3/4 of the way down the line's slot, so the capitals
        // and the descenders both stay inside the slot and so inside its band.
        using var typeface = SKTypeface.FromFamilyName("Courier New");
        using var font = new SKFont(typeface ?? SKTypeface.Default, LineHeight * 0.8f);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        for (int i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], TextLeft, LineTop(i) + LineHeight * 0.75f, font, ink);
        }
    }
}
