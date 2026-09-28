using System;
using System.Buffers;
using System.Collections.Generic;
using RetroTerm.Core.Terminal.Graphics;

namespace RetroTerm.Core.Terminal.Printing;

/// <summary>
/// Turns a graphics surface back into a sixel stream, the way a VT dumped its own bitmap to the
/// printer port.
/// </summary>
/// <remarks>
/// <para><b>The other direction from SixelDecoder, and deliberately not inside it</b></para>
/// Decoding and encoding share no code and no state: one walks a byte stream and paints, the other
/// walks pixels and writes bytes. Putting both in one class would give it two reasons to change.
/// What they DO share is the format, and every rule below is quoted from the manual so the two
/// cannot drift apart on a reading.
///
/// <para><b>The format, from the Level 2 Sixel Programming Reference</b></para>
/// <c>spec\DEC\EK-PPLV2-PM.B01_Level_2_Sixel_Programming_Reference.pdf</c>, chapter 5:
///  - The envelope is <c>DCS Ps1 ; Ps2 ; Pn3 q picture-data ST</c> (figure 5-2).
///  - Ps1 is a MACRO parameter selecting horizontal grid size and pixel aspect ratio together.
///    Table 5-1: 0 and 1 both mean aspect 200:100, which is the 2:1 a level 1 printer is fixed at.
///  - Ps2 "selects a background color. The device ignores this parameter. Software can include Ps2
///    for compatibility with video devices." So on a printer it carries no meaning at all.
///  - Pn3 sets the horizontal grid size explicitly in decipoints, and any non-zero value overrides
///    the Ps1 grid.
///  - A printable character is 3/15 to 7/14; subtract 3F and the six low bits are the six pixels,
///    bit 0 at the TOP and bit 5 at the bottom (section 5.5.1).
///  - The control codes (table 5-3) are <c>"</c> DECGRA raster attributes, <c>!</c> DECGRI repeat,
///    <c>$</c> DECGCR carriage return, <c>-</c> DECGNL next line, <c>#</c> DECGCI colour.
///  - "Monochrome devices ignore DECGCI. All sixel data is printed in black."
///
/// <para><b>Rotation is done on the way IN, not on the way out</b></para>
/// A rotated print reads the same encoder through a transposed accessor rather than through a
/// second emit path. One encoder, one set of run-length rules, one place for a bug to live.
/// </remarks>
public static class SixelEncoder
{
    /// <summary>
    /// The offset added to a six-bit pattern to make a printable sixel character.
    /// </summary>
    /// <remarks>
    /// "The device subtracts the offset (3F hexadecimal) from the received code", section 5.5.1.
    /// </remarks>
    private const byte SixelOffset = 0x3F;

    /// <summary>
    /// How many pixel rows one sixel character covers.
    /// </summary>
    private const int BandHeight = 6;

    /// <summary>
    /// The shortest run worth writing as a DECGRI repeat rather than as repeated characters.
    /// </summary>
    /// <remarks>
    /// <c>!4~</c> is three characters plus the count against four literal ones, so four is where a
    /// repeat starts paying. Below that the repeat is longer than what it replaces.
    /// </remarks>
    private const int MinimumRepeatRun = 4;

    /// <summary>
    /// How many colour registers a picture may use.
    /// </summary>
    private const int MaxColours = GraphicsColorMap.RegisterCount;

    /// <summary>
    /// Encodes a surface as a sixel print job and writes it to a sink.
    /// </summary>
    /// <param name="surface">
    /// The bitmap to print.
    /// </param>
    /// <param name="options">
    /// Level, colour, and the expanded and rotated print modes.
    /// </param>
    /// <param name="sink">
    /// Where the bytes go.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="surface"/>, <paramref name="options"/> or
    /// <paramref name="sink"/> is null.
    /// </exception>
    public static void Encode(IGraphicsSurface surface, SixelPrintOptions options, IPrintSink sink)
    {
        if (surface == null) throw new ArgumentNullException(nameof(surface));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (sink == null) throw new ArgumentNullException(nameof(sink));

        // Rotating swaps the picture's width and height. Everything below works in PRINTED
        // coordinates and reads through Read, so nothing else has to know.
        int width = options.Rotated ? surface.Height : surface.Width;
        int height = options.Rotated ? surface.Width : surface.Height;

        // "Rotated images are always expanded" - DEC STD 070.
        bool expanded = options.Expanded || options.Rotated;
        int horizontalRepeat = expanded ? 2 : 1;

        var writer = new SixelWriter(sink);

        try
        {
            WriteHeader(ref writer, options, width * horizontalRepeat, height);

            if (options.Colour)
            {
                EncodeColour(ref writer, surface, options, width, height, horizontalRepeat);
            }
            else
            {
                EncodeMonochrome(ref writer, surface, options, width, height, horizontalRepeat);
            }

            // ST, seven bit, matching the seven-bit introducer above.
            writer.WriteAscii("\x1b\\");
        }
        finally
        {
            writer.Flush();
        }
    }

    /// <summary>
    /// Writes the DCS envelope and, at level 2, the raster attributes.
    /// </summary>
    /// <param name="writer">
    /// The output.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="width">
    /// Printed width in pixels, after any expansion.
    /// </param>
    /// <param name="height">
    /// Printed height in pixels.
    /// </param>
    private static void WriteHeader(ref SixelWriter writer, SixelPrintOptions options, int width,
        int height)
    {
        if (options.Level < 2)
        {
            // "The control string is always the bare 7-bit ESC P 1 q" for a level 1 printer, and
            // the documented VT240 screen dump is exactly this. Ps1 = 1 is aspect 200:100, which
            // is the 2:1 such a printer is fixed at anyway.
            writer.WriteAscii("\x1bP1q");
            return;
        }

        // Ps1 = 0 - the default macro, aspect 200:100. Ps2 is written as 1 out of habit and means
        // nothing to a printer: "the device ignores this parameter". Pn3 = 0 leaves the horizontal
        // grid to Ps1 rather than overriding it with a decipoint size this code cannot know.
        writer.WriteAscii("\x1bP0;1;0q");

        // DECGRA - Pan;Pad;Ph;Pv. The aspect is written as 1:1 because the surface is already
        // square pixels; the printer's own grid supplies the rest.
        writer.WriteAscii("\"1;1;");
        writer.WriteNumber(width);
        writer.WriteByte((byte)';');
        writer.WriteNumber(height);
    }

    /// <summary>
    /// Encodes as black and white - the logical OR of every plane.
    /// </summary>
    /// <remarks>
    /// "Monochrome devices ignore DECGCI", so no colour is selected at all: any pixel that is not
    /// transparent is ink. With <see cref="SixelPrintOptions.PrintBackground"/> the test is
    /// inverted, which puts the paper where the picture is not.
    /// </remarks>
    /// <param name="writer">
    /// The output.
    /// </param>
    /// <param name="surface">
    /// The bitmap.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="width">
    /// Printed width in pixels, before expansion.
    /// </param>
    /// <param name="height">
    /// Printed height in pixels.
    /// </param>
    /// <param name="horizontalRepeat">
    /// How many times each column is emitted.
    /// </param>
    private static void EncodeMonochrome(ref SixelWriter writer, IGraphicsSurface surface,
        SixelPrintOptions options, int width, int height, int horizontalRepeat)
    {
        for (int bandTop = 0; bandTop < height; bandTop += BandHeight)
        {
            if (bandTop != 0) writer.WriteByte((byte)'-');      // DECGNL

            WriteBand(ref writer, surface, options, width, height, bandTop, horizontalRepeat,
                colourIndex: -1, palette: null);
        }
    }

    /// <summary>
    /// Encodes in colour, one pass over the band per colour used in it.
    /// </summary>
    /// <param name="writer">
    /// The output.
    /// </param>
    /// <param name="surface">
    /// The bitmap.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="width">
    /// Printed width in pixels, before expansion.
    /// </param>
    /// <param name="height">
    /// Printed height in pixels.
    /// </param>
    /// <param name="horizontalRepeat">
    /// How many times each column is emitted.
    /// </param>
    private static void EncodeColour(ref SixelWriter writer, IGraphicsSurface surface,
        SixelPrintOptions options, int width, int height, int horizontalRepeat)
    {
        // The palette is built from the picture rather than from the terminal's colour map: the
        // surface is the composite of every plane and a pixel on it may be a colour no single
        // register holds. Capped, and anything past the cap is dropped rather than mis-coloured.
        var palette = BuildPalette(surface, options, width, height);

        WriteColourDefinitions(ref writer, palette);

        for (int bandTop = 0; bandTop < height; bandTop += BandHeight)
        {
            if (bandTop != 0) writer.WriteByte((byte)'-');      // DECGNL

            bool first = true;
            for (int index = 0; index < palette.Count; index++)
            {
                if (!BandUsesColour(surface, options, width, height, bandTop, palette, index))
                {
                    continue;
                }

                // DECGCR between colours in the same band: each pass starts again at the left
                // margin and overlays the one before it.
                if (!first) writer.WriteByte((byte)'$');
                first = false;

                writer.WriteByte((byte)'#');
                writer.WriteNumber(index);

                WriteBand(ref writer, surface, options, width, height, bandTop, horizontalRepeat,
                    index, palette);
            }
        }
    }

    /// <summary>
    /// Writes one band as run-length encoded sixel characters.
    /// </summary>
    /// <remarks>
    /// Trailing empty columns are dropped: a run of <c>?</c> at the end of a line paints nothing,
    /// and the next DECGCR or DECGNL returns to the margin regardless.
    /// </remarks>
    /// <param name="writer">
    /// The output.
    /// </param>
    /// <param name="surface">
    /// The bitmap.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="width">
    /// Printed width in pixels, before expansion.
    /// </param>
    /// <param name="height">
    /// Printed height in pixels.
    /// </param>
    /// <param name="bandTop">
    /// First pixel row of this band.
    /// </param>
    /// <param name="horizontalRepeat">
    /// How many times each column is emitted.
    /// </param>
    /// <param name="colourIndex">
    /// Which palette entry contributes bits, or -1 for a monochrome pass.
    /// </param>
    /// <param name="palette">
    /// The palette, or null for a monochrome pass.
    /// </param>
    private static void WriteBand(ref SixelWriter writer, IGraphicsSurface surface,
        SixelPrintOptions options, int width, int height, int bandTop, int horizontalRepeat,
        int colourIndex, List<uint>? palette)
    {
        int runLength = 0;
        byte runPattern = 0;
        bool anyRun = false;

        for (int x = 0; x < width; x++)
        {
            byte pattern = ColumnPattern(surface, options, width, height, x, bandTop, colourIndex,
                palette);

            if (anyRun && pattern == runPattern)
            {
                runLength += horizontalRepeat;
                continue;
            }

            if (anyRun) FlushRun(ref writer, runPattern, runLength);

            runPattern = pattern;
            runLength = horizontalRepeat;
            anyRun = true;
        }

        // The final run only earns its bytes if it paints something.
        if (anyRun && runPattern != 0) FlushRun(ref writer, runPattern, runLength);
    }

    /// <summary>
    /// Writes one run, as a DECGRI repeat when that is shorter than the characters it replaces.
    /// </summary>
    /// <param name="writer">
    /// The output.
    /// </param>
    /// <param name="pattern">
    /// The six-bit column pattern.
    /// </param>
    /// <param name="count">
    /// How many columns.
    /// </param>
    private static void FlushRun(ref SixelWriter writer, byte pattern, int count)
    {
        if (count <= 0) return;

        byte character = (byte)(SixelOffset + pattern);

        if (count >= MinimumRepeatRun)
        {
            writer.WriteByte((byte)'!');       // DECGRI
            writer.WriteNumber(count);
            writer.WriteByte(character);
            return;
        }

        for (int i = 0; i < count; i++) writer.WriteByte(character);
    }

    /// <summary>
    /// Builds the six-bit pattern for one column of one band.
    /// </summary>
    /// <remarks>
    /// Bit 0 is the TOP pixel and bit 5 the bottom, from section 5.5.1. A band that runs past the
    /// bottom of the picture leaves its missing rows clear rather than wrapping.
    /// </remarks>
    /// <param name="surface">
    /// The bitmap.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="width">
    /// Printed width in pixels.
    /// </param>
    /// <param name="height">
    /// Printed height in pixels.
    /// </param>
    /// <param name="x">
    /// Printed column.
    /// </param>
    /// <param name="bandTop">
    /// First pixel row of the band.
    /// </param>
    /// <param name="colourIndex">
    /// Which palette entry contributes bits, or -1 for a monochrome pass.
    /// </param>
    /// <param name="palette">
    /// The palette, or null for a monochrome pass.
    /// </param>
    /// <returns>
    /// The six-bit pattern.
    /// </returns>
    private static byte ColumnPattern(IGraphicsSurface surface, SixelPrintOptions options,
        int width, int height, int x, int bandTop, int colourIndex, List<uint>? palette)
    {
        byte pattern = 0;

        for (int bit = 0; bit < BandHeight; bit++)
        {
            int y = bandTop + bit;
            if (y >= height) break;

            var pixel = Read(surface, options, x, y);

            bool ink;
            if (colourIndex < 0)
            {
                // Monochrome: the logical OR of every plane.
                ink = options.PrintBackground ? pixel.IsTransparent : !pixel.IsTransparent;
            }
            else
            {
                ink = !pixel.IsTransparent
                    && palette != null
                    && colourIndex < palette.Count
                    && palette[colourIndex] == pixel.Value;
            }

            if (ink) pattern |= (byte)(1 << bit);
        }

        return pattern;
    }

    /// <summary>
    /// True when any pixel of the band is in the given palette entry.
    /// </summary>
    /// <remarks>
    /// Checked first so a band that never uses a colour costs no <c>#n</c> selector and no run of
    /// empty characters. On a picture with many colours this is most of the output.
    /// </remarks>
    /// <param name="surface">
    /// The bitmap.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="width">
    /// Printed width in pixels.
    /// </param>
    /// <param name="height">
    /// Printed height in pixels.
    /// </param>
    /// <param name="bandTop">
    /// First pixel row of the band.
    /// </param>
    /// <param name="palette">
    /// The palette.
    /// </param>
    /// <param name="index">
    /// Which palette entry.
    /// </param>
    /// <returns>
    /// True when the colour appears in the band.
    /// </returns>
    private static bool BandUsesColour(IGraphicsSurface surface, SixelPrintOptions options,
        int width, int height, int bandTop, List<uint> palette, int index)
    {
        uint wanted = palette[index];
        int bandBottom = Math.Min(bandTop + BandHeight, height);

        for (int y = bandTop; y < bandBottom; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (Read(surface, options, x, y).Value == wanted) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Collects the distinct colours in the picture, in the order they are first met.
    /// </summary>
    /// <param name="surface">
    /// The bitmap.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="width">
    /// Printed width in pixels.
    /// </param>
    /// <param name="height">
    /// Printed height in pixels.
    /// </param>
    /// <returns>
    /// The palette, at most <see cref="MaxColours"/> entries.
    /// </returns>
    private static List<uint> BuildPalette(IGraphicsSurface surface, SixelPrintOptions options,
        int width, int height)
    {
        var palette = new List<uint>();
        var seen = new HashSet<uint>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = Read(surface, options, x, y);
                if (pixel.IsTransparent) continue;
                if (palette.Count >= MaxColours) return palette;
                if (seen.Add(pixel.Value)) palette.Add(pixel.Value);
            }
        }

        return palette;
    }

    /// <summary>
    /// Writes the DECGCI definitions for the whole palette, at the head of the picture.
    /// </summary>
    /// <remarks>
    /// "Colour printing sends the colour map at the head of the dump" - DEC STD 070. The <c>2</c>
    /// selects RGB, and the components are PERCENTAGES, which is why they are scaled from 0 to 255
    /// here rather than sent raw.
    /// </remarks>
    /// <param name="writer">
    /// The output.
    /// </param>
    /// <param name="palette">
    /// The colours.
    /// </param>
    private static void WriteColourDefinitions(ref SixelWriter writer, List<uint> palette)
    {
        for (int i = 0; i < palette.Count; i++)
        {
            var colour = new GraphicsColor(palette[i]);

            writer.WriteByte((byte)'#');
            writer.WriteNumber(i);
            writer.WriteAscii(";2;");
            writer.WriteNumber(ToPercent(colour.R));
            writer.WriteByte((byte)';');
            writer.WriteNumber(ToPercent(colour.G));
            writer.WriteByte((byte)';');
            writer.WriteNumber(ToPercent(colour.B));
        }
    }

    /// <summary>
    /// Scales a 0 to 255 component to the 0 to 100 percent a sixel colour carries.
    /// </summary>
    /// <param name="component">
    /// The component.
    /// </param>
    /// <returns>
    /// The percentage, rounded.
    /// </returns>
    private static int ToPercent(byte component)
        => (component * 100 + 127) / 255;

    /// <summary>
    /// Reads a pixel in PRINTED coordinates, applying rotation on the way in.
    /// </summary>
    /// <remarks>
    /// <para><b>The rotation, worked out rather than guessed</b></para>
    /// Ninety degrees counter-clockwise sends a source pixel at (sx, sy) of a W by H picture to
    /// (sy, W - 1 - sx) of an H by W one. Reading it the other way round, printed (x, y) comes from
    /// source (W - 1 - y, x) - where W here is the SOURCE width, which is the printed height.
    ///
    /// The check that it is counter-clockwise and not clockwise: the source's top-left pixel lands
    /// at the bottom-left of the page, so the top of the screen runs down the left edge of the
    /// paper. That is what DEC STD 070 describes, and the reason is that the left edge is where the
    /// holes get punched.
    /// </remarks>
    /// <param name="surface">
    /// The bitmap.
    /// </param>
    /// <param name="options">
    /// Print options.
    /// </param>
    /// <param name="x">
    /// Printed column.
    /// </param>
    /// <param name="y">
    /// Printed row.
    /// </param>
    /// <returns>
    /// The pixel.
    /// </returns>
    private static GraphicsColor Read(IGraphicsSurface surface, SixelPrintOptions options,
        int x, int y)
    {
        if (!options.Rotated) return surface.GetPixel(x, y);

        return surface.GetPixel(surface.Width - 1 - y, x);
    }

    /// <summary>
    /// Buffers bytes on their way to the sink.
    /// </summary>
    /// <remarks>
    /// A page of sixel is hundreds of thousands of characters and a call per character would be
    /// hundreds of thousands of interface calls. One rented buffer, flushed when it fills.
    /// </remarks>
    private ref struct SixelWriter
    {
        private readonly IPrintSink _sink;
        private readonly byte[] _buffer;
        private int _count;

        /// <param name="sink">
        /// Where the bytes go.
        /// </param>
        public SixelWriter(IPrintSink sink)
        {
            _sink = sink;
            _buffer = ArrayPool<byte>.Shared.Rent(8192);
            _count = 0;
        }

        /// <param name="b">
        /// The byte.
        /// </param>
        public void WriteByte(byte b)
        {
            if (_count == _buffer.Length) Send();
            _buffer[_count++] = b;
        }

        /// <param name="text">
        /// ASCII text; every character must be below 128.
        /// </param>
        public void WriteAscii(string text)
        {
            for (int i = 0; i < text.Length; i++) WriteByte((byte)text[i]);
        }

        /// <summary>
        /// Writes a non-negative number as decimal digits, without allocating a string.
        /// </summary>
        /// <param name="value">
        /// The number.
        /// </param>
        public void WriteNumber(int value)
        {
            if (value < 0) value = 0;

            if (value == 0)
            {
                WriteByte((byte)'0');
                return;
            }

            // Ten digits covers int.MaxValue, and the digits come out backwards.
            Span<byte> digits = stackalloc byte[10];
            int at = 0;
            while (value > 0)
            {
                digits[at++] = (byte)('0' + (value % 10));
                value /= 10;
            }

            while (at > 0) WriteByte(digits[--at]);
        }

        /// <summary>
        /// Sends whatever is buffered and returns the buffer to the pool.
        /// </summary>
        public void Flush()
        {
            Send();
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        /// <summary>
        /// Sends whatever is buffered, keeping the buffer.
        /// </summary>
        private void Send()
        {
            if (_count == 0) return;

            _sink.Write(new ReadOnlySpan<byte>(_buffer, 0, _count));
            _count = 0;
        }
    }
}
