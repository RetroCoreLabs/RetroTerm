using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroTerm.Core.Terminal.Graphics;
using RetroTerm.Core.Terminal.Printing;
using SkiaSharp;

namespace RetroTerm.Desktop.Printing;

/// <summary>
/// A print sink that writes a PDF - the modern stand-in for the paper that came out of a VT's
/// printer port.
/// </summary>
/// <remarks>
/// <para><b>Why PDF and not PNG</b></para>
/// A print job has a size in the real world. The sixel protocol says how big a dot is, so a page
/// has a true width in inches and the dots have a true spacing; a PNG would throw that away and
/// leave the picture as "some pixels". PDF carries both the image and the physical page rectangle,
/// so what comes out of a real printer is the size DEC intended.
///
/// <para><b>Where the numbers come from</b></para>
/// All four are quoted, not chosen. From
/// <c>spec\DEC\EK-PPLV2-PM.B01_Level_2_Sixel_Programming_Reference.pdf</c>:
///  - Sixel horizontal grid, table 5-1 with Ps1 = 0: 50 centipoints per pixel, which is half a
///    point, because a centipoint is 1/7200 inch and a point is 1/72 inch.
///  - Sixel vertical grid: the same 50 centipoints, because the decoder has ALREADY applied the
///    aspect ratio. See the remarks on <see cref="SixelPointsPerPixelY"/> - this one is worth
///    reading, because the obvious answer is wrong and it shows up as stretched circles.
///  - Text pitch, DECSHORP with Ps = 0: "720 centipoints, 10 characters/inch" - 7.2 points.
///  - Line pitch, DECVERP with Ps = 0: "1200 centipoints, 6 lines/inch" - 12 points.
///
/// <para><b>No resampling</b></para>
/// The bitmap is drawn at its native pixel size under a scale transform, never resized into a
/// smaller bitmap first. No resampling adds information; it only turns crisp dots into grey
/// smears. The printer's own resolution does the final sharpening.
///
/// <para><b>It MUST be disposed</b></para>
/// Not for tidiness. The document writes through a managed stream wrapper, and one left to the
/// finalizer unrefs a wrapper that has already been collected - which throws inside SkiaSharp on
/// the finalizer thread, where nothing can catch it, and takes the process down. Not a failed
/// operation: a crash. Disposing is what closes the PDF; <see cref="EndJob"/> only flushes a page.
///
/// <para><b>Which SkiaSharp</b></para>
/// Only API present in BOTH SkiaSharp 2.88.9, which Avalonia 11.3 brings into the desktop app, and
/// 3.119.1, which the test project references. Those two really do both get loaded, so an overload
/// that exists in only one would compile here and throw in the tests. Checked against both
/// assemblies rather than assumed: <c>SKDocument.CreatePdf</c>,
/// <c>SKCanvas.DrawText(string, float, float, SKFont, SKPaint)</c> and
/// <c>SKCanvas.DrawBitmap(SKBitmap, float, float, SKPaint)</c> are in both.
/// </remarks>
public sealed class PdfPrintSink : IPrintSink, IDisposable
{
    /// <summary>
    /// Points per sixel pixel across - 50 centipoints at the default macro parameter.
    /// </summary>
    public const float SixelPointsPerPixelX = 0.5f;

    /// <summary>
    /// Points per sixel pixel down - the SAME as across, and that is not an oversight.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is not one point</b></para>
    /// The obvious reading of table 5-1 is that the vertical grid is the horizontal grid times the
    /// aspect ratio, so at Ps1 = 0 a pixel is 50 centipoints across and 100 down - twice as tall as
    /// it is wide. Written that way, and the first PDF this produced turned a circle into an
    /// ellipse and a square into a rectangle. Found by opening the file, not by an assertion.
    ///
    /// The reason is in section 5.4.1.3: "The pixel aspect ratio may be defined by Ps1 or the Set
    /// Raster Attributes sixel control code." DECGRA WINS when it is present, and
    /// <see cref="RetroTerm.Core.Terminal.Graphics.SixelDecoder"/> already honours it - it paints
    /// Pan/Pad rows per sixel bit, so the aspect is baked into the bitmap by the time it gets here.
    /// Applying it a second time squares the distortion.
    ///
    /// So the page uses SQUARE pixels and lets the decoder own the aspect. One place decides it,
    /// which is also why the two halves cannot drift apart.
    /// </remarks>
    public const float SixelPointsPerPixelY = 0.5f;

    /// <summary>
    /// Points per character cell across - DECSHORP default, 10 characters per inch.
    /// </summary>
    public const float TextPointsPerColumn = 7.2f;

    /// <summary>
    /// Points per line down - DECVERP default, 6 lines per inch.
    /// </summary>
    public const float TextPointsPerLine = 12.0f;

    /// <summary>
    /// US Letter, in points. The default sheet when nothing bigger is printed on it.
    /// </summary>
    public const float DefaultPageWidth = 612f;

    /// <summary>
    /// See <see cref="DefaultPageWidth"/>.
    /// </summary>
    public const float DefaultPageHeight = 792f;

    /// <summary>
    /// Margin on every edge, in points - half an inch.
    /// </summary>
    private const float Margin = 36f;

    private readonly Stream _output;
    private readonly bool _ownsStream;
    private readonly MemoryStream _page = new MemoryStream();

    private SKDocument? _document;
    private bool _closed;

    /// <summary>
    /// How many pages have been written.
    /// </summary>
    public int PageCount { get; private set; }

    /// <summary>
    /// Sheet width in points. Grows to fit anything wider.
    /// </summary>
    public float PageWidth { get; set; } = DefaultPageWidth;

    /// <summary>
    /// Sheet height in points. Grows to fit anything taller.
    /// </summary>
    public float PageHeight { get; set; } = DefaultPageHeight;

    /// <summary>
    /// Writes a PDF to a file.
    /// </summary>
    /// <param name="path">
    /// Where the PDF goes. An existing file is replaced.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> is null.
    /// </exception>
    public PdfPrintSink(string path)
        : this(new FileStream(path ?? throw new ArgumentNullException(nameof(path)),
            FileMode.Create, FileAccess.Write), ownsStream: true)
    {
    }

    /// <summary>
    /// Writes a PDF to a stream.
    /// </summary>
    /// <param name="output">
    /// Where the PDF goes.
    /// </param>
    /// <param name="ownsStream">
    /// True to close <paramref name="output"/> when this sink is disposed.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/> is null.
    /// </exception>
    public PdfPrintSink(Stream output, bool ownsStream = false)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _ownsStream = ownsStream;
    }

    /// <param name="data">
    /// The bytes, as received.
    /// </param>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (_closed || data.Length == 0) return;

        for (int i = 0; i < data.Length; i++) _page.WriteByte(data[i]);
    }

    /// <summary>
    /// Ends the page. A page with nothing on it produces no sheet.
    /// </summary>
    public void FormFeed()
    {
        if (_closed || _page.Length == 0) return;

        ComposePage(_page.ToArray());
        _page.SetLength(0);
    }

    /// <summary>
    /// Ends the current print job by flushing the open page. Does NOT close the document.
    /// </summary>
    /// <remarks>
    /// <para><b>This method used to close the PDF, and that was wrong</b></para>
    /// The terminal calls it every time printer controller mode ends - once per print job, and a
    /// terminal makes many. Closing here meant the second job onwards was written to a closed
    /// document and vanished. It went unnoticed while only one thing was ever printed; sending
    /// sixteen corpus pictures in a row produced a single page.
    ///
    /// The document is closed by <see cref="Dispose"/>, which is the only thing that means "this
    /// sink is finished".
    /// </remarks>
    public void EndJob()
    {
        if (_closed) return;

        FormFeed();
    }

    /// <summary>
    /// Finishes the document and closes the output.
    /// </summary>
    public void Dispose()
    {
        if (_closed) return;

        FormFeed();

        if (_document != null)
        {
            _document.Close();
            _document.Dispose();
            _document = null;
        }

        _closed = true;

        if (_ownsStream) _output.Dispose();
    }

    /// <summary>
    /// Splits one page's bytes into pictures and text, and draws them down the sheet.
    /// </summary>
    /// <param name="page">
    /// The page's bytes.
    /// </param>
    private void ComposePage(byte[] page)
    {
        var pictures = new List<SKBitmap>();
        var text = new List<string>();

        Split(page, pictures, text);

        // The sheet grows rather than cropping. A plotter sheet is bigger than Letter and losing
        // its edges would be worse than an unusual page size.
        float contentWidth = 0f;
        float contentHeight = 0f;

        for (int i = 0; i < pictures.Count; i++)
        {
            contentWidth = Math.Max(contentWidth, pictures[i].Width * SixelPointsPerPixelX);
            contentHeight += pictures[i].Height * SixelPointsPerPixelY;
        }

        int widestLine = 0;
        for (int i = 0; i < text.Count; i++) widestLine = Math.Max(widestLine, text[i].Length);

        contentWidth = Math.Max(contentWidth, widestLine * TextPointsPerColumn);
        contentHeight += text.Count * TextPointsPerLine;

        float sheetWidth = Math.Max(PageWidth, contentWidth + 2 * Margin);
        float sheetHeight = Math.Max(PageHeight, contentHeight + 2 * Margin);

        _document ??= SKDocument.CreatePdf(_output);

        var canvas = _document.BeginPage(sheetWidth, sheetHeight);
        try
        {
            Draw(canvas, pictures, text);
        }
        finally
        {
            _document.EndPage();
            for (int i = 0; i < pictures.Count; i++) pictures[i].Dispose();
        }

        PageCount++;
    }

    /// <summary>
    /// Draws one composed page.
    /// </summary>
    /// <param name="canvas">
    /// The page canvas.
    /// </param>
    /// <param name="pictures">
    /// Decoded sixel images, in the order they arrived.
    /// </param>
    /// <param name="text">
    /// Text lines, in the order they arrived.
    /// </param>
    private static void Draw(SKCanvas canvas, List<SKBitmap> pictures, List<string> text)
    {
        canvas.Clear(SKColors.White);

        float y = Margin;

        using var paint = new SKPaint { IsAntialias = false };

        for (int i = 0; i < pictures.Count; i++)
        {
            var picture = pictures[i];

            // Scale rather than resize. The bitmap goes in at its native pixel size and the page
            // transform gives it its real-world size, so no dot is ever resampled away.
            canvas.Save();
            canvas.Translate(Margin, y);
            canvas.Scale(SixelPointsPerPixelX, SixelPointsPerPixelY);
            canvas.DrawBitmap(picture, 0, 0, paint);
            canvas.Restore();

            y += picture.Height * SixelPointsPerPixelY;
        }

        if (text.Count == 0) return;

        // A monospaced face, because a print job from a terminal is column-aligned and a
        // proportional font would take the alignment away. The size is the line pitch less the
        // leading a 6 lines-per-inch page carries.
        using var typeface = SKTypeface.FromFamilyName("Courier New");
        using var font = new SKFont(typeface ?? SKTypeface.Default, TextPointsPerLine * 0.8f);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        for (int i = 0; i < text.Count; i++)
        {
            y += TextPointsPerLine;
            canvas.DrawText(text[i], Margin, y, font, ink);
        }
    }

    /// <summary>
    /// Separates the sixel pictures in a print stream from the plain text around them.
    /// </summary>
    /// <remarks>
    /// A print job may be either or both: a screen print is text, a graphics hard copy is one
    /// picture, and a page streamed through the terminal in printer controller mode can carry both.
    /// Anything that is not inside a sixel device control string is treated as text.
    /// </remarks>
    /// <param name="page">
    /// The page's bytes.
    /// </param>
    /// <param name="pictures">
    /// Receives the decoded pictures.
    /// </param>
    /// <param name="text">
    /// Receives the text lines.
    /// </param>
    private static void Split(byte[] page, List<SKBitmap> pictures, List<string> text)
    {
        var line = new StringBuilder();
        int i = 0;

        while (i < page.Length)
        {
            // A sixel picture opens with DCS and closes with ST - and BOTH have a seven-bit form
            // and an eight-bit one. ESC P / ESC \ is the seven-bit pair; 0x90 / 0x9C are the same
            // controls as single C1 bytes.
            //
            // Only the seven-bit form was recognised at first, and the corpus fixture named for
            // this - 8bit.six - came out as two thousand characters of garbage text on a page
            // eighteen feet wide. It exists to catch exactly that.
            bool sevenBitDcs = page[i] == 0x1B && i + 1 < page.Length && page[i + 1] == (byte)'P';
            if (sevenBitDcs || page[i] == 0x90)
            {
                int body = i + (sevenBitDcs ? 2 : 1);
                int end = FindStringTerminator(page, body);
                int q = FindProtocolSelector(page, body, end);

                if (q >= 0)
                {
                    var picture = DecodePicture(page, q + 1, end);
                    if (picture != null) pictures.Add(picture);
                }

                // Step past the terminator: two bytes for ESC \ , one for a bare 0x9C.
                i = end < page.Length && page[end] == 0x1B ? end + 2 : end + 1;
                continue;
            }

            // ANY OTHER ESCAPE SEQUENCE IS SKIPPED, NOT PRINTED.
            //
            // A print-through job is a stream aimed at a printer, and it carries the printer's own
            // control sequences - page size, margins, unit selection. They are commands, not text.
            // Printing them as characters put "[7 I[?20 J[1;66r" along the bottom of the first
            // real plotter sheet this produced, which is how it was found.
            if (page[i] == 0x1B)
            {
                i = SkipEscapeSequence(page, i);
                continue;
            }

            // An eight-bit CSI, and the rest of the C1 range. None of them is text.
            if (page[i] == 0x9B)
            {
                i++;
                while (i < page.Length && page[i] >= 0x20 && page[i] <= 0x3F) i++;
                if (i < page.Length) i++;
                continue;
            }

            if (page[i] >= 0x80 && page[i] <= 0x9F)
            {
                i++;
                continue;
            }

            if (page[i] == (byte)'\n')
            {
                text.Add(line.ToString());
                line.Clear();
                i++;
                continue;
            }

            // Other C0 controls are not text either. A form feed already ended the page before it
            // got here, and the rest would come out as boxes or nothing at all.
            if (page[i] >= 0x20 && page[i] != 0x7F) line.Append((char)page[i]);
            i++;
        }

        if (line.Length != 0) text.Add(line.ToString());
    }

    /// <summary>
    /// Steps over an escape sequence that is not a sixel picture.
    /// </summary>
    /// <remarks>
    /// Enough structure to know where a sequence ENDS, and no interpretation at all - this is a
    /// print sink, not a terminal. A CSI runs to its final byte in 4/0 to 7/14; a two-character
    /// escape ends at the byte after the ESC.
    /// </remarks>
    /// <param name="page">
    /// The page's bytes.
    /// </param>
    /// <param name="at">
    /// Index of the ESC.
    /// </param>
    /// <returns>
    /// Index of the first byte after the sequence.
    /// </returns>
    private static int SkipEscapeSequence(byte[] page, int at)
    {
        int i = at + 1;
        if (i >= page.Length) return page.Length;

        if (page[i] == (byte)'[')
        {
            // CSI: parameters and intermediates, then a final byte.
            i++;
            while (i < page.Length && page[i] >= 0x20 && page[i] <= 0x3F) i++;
            return i < page.Length ? i + 1 : page.Length;
        }

        // Everything else - ESC \ , ESC 7, a character-set designation - ends at the final byte,
        // with any intermediates in between.
        while (i < page.Length && page[i] >= 0x20 && page[i] <= 0x2F) i++;
        return i < page.Length ? i + 1 : page.Length;
    }

    /// <summary>
    /// Finds the ESC that starts the string terminator.
    /// </summary>
    /// <param name="page">
    /// The page's bytes.
    /// </param>
    /// <param name="from">
    /// Where to start looking.
    /// </param>
    /// <returns>
    /// The index of the ESC, or the end of the data when the stream was truncated.
    /// </returns>
    private static int FindStringTerminator(byte[] page, int from)
    {
        for (int i = from; i < page.Length; i++)
        {
            // ST in both its forms: the C1 byte, and the seven-bit ESC \ pair.
            if (page[i] == 0x9C) return i;
            if (page[i] == 0x1B && i + 1 < page.Length && page[i + 1] == (byte)'\\') return i;
        }

        return page.Length;
    }

    /// <summary>
    /// Finds the <c>q</c> that ends the sixel protocol selector.
    /// </summary>
    /// <param name="page">
    /// The page's bytes.
    /// </param>
    /// <param name="from">
    /// Where to start looking.
    /// </param>
    /// <param name="end">
    /// Where the control string ends.
    /// </param>
    /// <returns>
    /// The index of the <c>q</c>, or -1 when this control string is not a sixel one.
    /// </returns>
    private static int FindProtocolSelector(byte[] page, int from, int end)
    {
        for (int i = from; i < end && i < page.Length; i++)
        {
            if (page[i] == (byte)'q') return i;

            // Only parameters and separators may come before the selector. Anything else means
            // this DCS is some other protocol, and guessing at it would draw nonsense.
            bool isParameter = (page[i] >= (byte)'0' && page[i] <= (byte)'9') || page[i] == (byte)';';
            if (!isParameter) return -1;
        }

        return -1;
    }

    /// <summary>
    /// Decodes one sixel picture into a bitmap.
    /// </summary>
    /// <remarks>
    /// <para><b>Two passes, on purpose</b></para>
    /// A surface has to be allocated before the decoder can paint on it, and how big it should be
    /// is inside the stream. So the payload is decoded once onto a one-pixel surface - everything
    /// clips, nothing is kept - purely to read back the size, and then again for real. The decoder
    /// is the only thing that knows the format either time, which is the point: a second parser
    /// here to peek at the raster attributes would be the duplication trap this repository has
    /// already been bitten by.
    /// </remarks>
    /// <param name="page">
    /// The page's bytes.
    /// </param>
    /// <param name="from">
    /// First byte of the picture data.
    /// </param>
    /// <param name="end">
    /// One past the last byte of the picture data.
    /// </param>
    /// <returns>
    /// The bitmap, or null when the picture was empty or its pixel memory could not be got.
    /// </returns>
    private static SKBitmap? DecodePicture(byte[] page, int from, int end)
    {
        if (end > page.Length) end = page.Length;
        if (from >= end) return null;

        var payload = new char[end - from];
        for (int i = 0; i < payload.Length; i++) payload[i] = (char)page[from + i];

        var measure = new SixelDecoder();
        measure.Decode(payload, new InMemoryGraphicsSurface(1, 1));

        int width = measure.DeclaredWidth > 0 ? measure.DeclaredWidth : measure.Width;
        int height = measure.DeclaredHeight > 0 ? measure.DeclaredHeight : measure.Height;

        if (width <= 0 || height <= 0) return null;

        var surface = new InMemoryGraphicsSurface(width, height);
        new SixelDecoder().Decode(payload, surface);

        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // THE CONSTRUCTOR DOES NOT THROW WHEN THE PIXEL MEMORY COULD NOT BE GOT. It hands back a
        // bitmap with no pixels, and the failure then surfaces much later inside SKCanvas.DrawBitmap
        // with nothing in the message to say which picture, which page, or that memory was the
        // problem. Ask here, where the answer is still cheap: a page that quietly loses one picture
        // beats a print job that dies with an unreadable error.
        //
        // NOT PROVEN TO BE THE CAUSE OF ANYTHING. TheTektronixStreamsPrintAsHardCopies failed twice
        // inside Draw on 30 August 2026 and then passed six consecutive full runs; both failures
        // fell in the window when RetroCore's Emulated.Tests and a second build were loading this
        // machine, which fits an allocation failure but does not demonstrate one - the exception
        // type was never captured. This check is here because the gap is real on its own merits,
        // not because it was measured closing that hole.
        //
        // ReadyToDraw rather than a null test: it is the property that reports whether the pixels
        // are actually there, and it exists in BOTH SkiaSharp 2.88.9 (which Avalonia loads) and
        // 3.119.1 (which the tests load) - checked against both assemblies, per the rule at the top
        // of this file.
        if (!bitmap.ReadyToDraw)
        {
            bitmap.Dispose();
            return null;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = surface.GetPixel(x, y);

                // Transparent is PAPER, not black. A drawing prints as ink on a white sheet; a
                // black background would use most of a toner cartridge and hide the picture.
                bitmap.SetPixel(x, y, pixel.IsTransparent
                    ? SKColors.White
                    : new SKColor(pixel.R, pixel.G, pixel.B));
            }
        }

        return bitmap;
    }
}
