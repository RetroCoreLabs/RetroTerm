using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Decodes a Sixel image and paints it onto a surface.
/// </summary>
/// <remarks>
/// <para><b>What a sixel is</b></para>
/// One character carries SIX pixels in a vertical strip: subtract 0x3F and the low bit is the top
/// pixel. Strips advance to the right; a <c>-</c> drops to the next band of six rows and returns
/// to the left; a <c>$</c> returns to the left WITHOUT dropping, so a band can be painted again in
/// another colour. That is the whole geometry, and it is the same shape DECDLD uses for downloaded
/// character shapes - both were designed for hardware that shifted six pixels at a time.
///
/// <para><b>The rest is registers</b></para>
///  - <c>#Pc;Pu;Px;Py;Pz</c> defines colour register Pc, or with no parameters after it, SELECTS
///    that register for what follows.
///  - <c>!Pn</c> repeats the next strip character Pn times, which is how a run of identical
///    columns is sent cheaply.
///  - <c>"Pan;Pad;Ph;Pv</c> gives the aspect ratio and the intended size.
///
/// <para><b>Nothing here decides where the image goes</b></para>
/// It paints at the surface origin and reports how far it got. Placing the image on the screen -
/// which plane, at which text row - belongs to the terminal, and keeping that out means this can
/// be tested by reading pixels back with no terminal at all.
/// </remarks>
public sealed class SixelDecoder
{
    /// <summary>
    /// How many colour registers a Sixel image may use.
    /// </summary>
    /// <remarks>
    /// A VT340 had 16. 256 is the number modern encoders assume, and the registers are a fixed
    /// array either way, so the larger number costs one kilobyte and refuses nothing real.
    /// </remarks>
    public const int ColourRegisterCount = GraphicsColorMap.RegisterCount;

    /// <summary>
    /// The colour map. Shared with ReGIS when a terminal owns both, because a VT340 has ONE.
    /// </summary>
    private readonly GraphicsColorMap _colours;

    private int _currentRegister;
    private int _x;
    private int _bandTop;
    private int _repeat = 1;
    private int _rowsPerPixel = 1;

    /// <summary>
    /// How many screen rows one sixel pixel is tall when the image carries NO raster attributes,
    /// taken from the aspect-ratio parameter of the image's own DCS.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists, 28 August 2026</b></para>
    /// A sixel image can state its aspect twice: in the DCS parameter Pa, and again in the raster
    /// attributes. The raster attributes win when present, and until now they were the ONLY thing
    /// read - so every image written before raster attributes existed rendered at half height.
    ///
    /// Two of hackerb9's photographs prove it. cat-original.six and cat-vt240.six carry no raster
    /// attributes, and on real hardware both stand twice as tall as we drew them. The other
    /// thirteen fixtures all declare raster attributes or ask for 1:1, which is why nothing else
    /// in the corpus moved and why this went unseen.
    ///
    /// <para><b>Where the table comes from</b></para>
    /// The VT330/VT340 Graphics Programming manual, chapter 14, held in spec\DEC. Omitted, 0 and 1
    /// are 2:1; 2 is 5:1; 3 and 4 are 3:1; 5 and 6 are 2:1; 7, 8 and 9 are 1:1.
    ///
    /// Between 28 August 2026 and the manual being read, only 0 and 1 were acted on and everything
    /// else fell back to 1:1 - deliberately, because guessing the rest would have been inventing
    /// behaviour. Nothing in the corpus moved when the full table landed: the only fixtures without
    /// raster attributes use an omitted parameter or 9.
    /// </remarks>
    private int _defaultRowsPerPixel = 1;

    /// <summary>
    /// Records the aspect the image asked for in its DCS, before the payload is decoded.
    /// </summary>
    /// <param name="aspectParameter">
    /// The DCS Pa parameter, or 0 when the image omitted it.
    /// </param>
    /// <remarks>
    /// Call this BEFORE <see cref="Decode"/>. Raster attributes inside the payload still win, so
    /// an image that states its size explicitly is unaffected.
    /// </remarks>
    public void SetAspectFromDcs(int aspectParameter)
    {
        // The VT300's own table, read out of the VT330/VT340 Graphics Programming manual held in
        // spec\DEC (chapter 14, the macro parameter). An OMITTED parameter is the same as 0.
        //
        //   omitted, 0, 1   2:1
        //   2               5:1
        //   3, 4            3:1
        //   5, 6            2:1
        //   7, 8, 9         1:1
        //
        // Until 28 August 2026 only 0 and 1 were implemented, because the table had not been read
        // and guessing at the rest would have been inventing behaviour. The manual settles it.
        //
        // The Level 2 Sixel Programming Reference gives a FINER table for printers - 450:100 for 2,
        // 300:100 for 3, 250:100 for 4 and so on. That one is not used here: this is a terminal,
        // and the terminal's own manual is the authority for it.
        switch (aspectParameter)
        {
            case 2: _defaultRowsPerPixel = 5; break;
            case 3:
            case 4: _defaultRowsPerPixel = 3; break;
            case 7:
            case 8:
            case 9: _defaultRowsPerPixel = 1; break;
            default: _defaultRowsPerPixel = 2; break;   // omitted, 0, 1, 5, 6 and anything unknown
        }
    }

    /// <summary>
    /// Widest pixel column written, plus one.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Lowest pixel row written, plus one.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// How far up the painting is shifted before it reaches the surface, in pixel rows.
    /// </summary>
    /// <remarks>
    /// Zero for every ordinary image, and the caller sets it back to zero afterwards.
    ///
    /// It exists for an image TALLER THAN THE SCREEN. Such an image scrolls the terminal as it
    /// is received, so the part a person ends up looking at is the BOTTOM of it - everything
    /// above has gone off the top. The surface here is only as tall as the screen, so painting
    /// from row zero puts the wrong end of the picture on it and clips the rest away before the
    /// scroll can move it. A negative offset here slides the painting up so the rows that survive
    /// the scroll are the rows that land on the surface.
    ///
    /// <see cref="Width"/>, <see cref="Height"/> and <see cref="SixelCursorTop"/> are measured in
    /// the image's OWN rows and are not affected, so the caller still gets the true size back.
    /// </remarks>
    public int PaintOffsetY { get; set; }

    /// <summary>
    /// Where the SIXEL CURSOR is left standing when the image ends, as a pixel row from the top
    /// of the image.
    /// </summary>
    /// <remarks>
    /// <para><b>This is not the same thing as <see cref="Height"/>, and the difference is the
    /// whole point</b></para>
    /// <c>Height</c> is how far down any PIXEL was written. This is where the CURSOR ended up,
    /// and only a <c>-</c> moves it. Drawing pixels never does, and neither does <c>$</c>.
    ///
    /// The VT340 manual says that when sixel mode is exited the text cursor is set to the sixel
    /// cursor position, so this is the value the terminal needs and the height is not. The two
    /// agree whenever every band ends with a <c>-</c> and one screen row is one pixel row, which
    /// is nearly always - which is exactly why reading the height instead went unnoticed.
    ///
    /// <c>extremeratio.six</c> is the case that tells them apart: it contains no <c>-</c> at all
    /// and paints 480 rows from one band, so the height is 480 and this stays 0. Its own comment
    /// says a real VT340 leaves the text cursor at the top of the screen.
    /// </remarks>
    public int SixelCursorTop { get; private set; }

    /// <summary>
    /// The size the image said it would be, from its raster attributes, or zero when it did not say.
    /// </summary>
    public int DeclaredWidth { get; private set; }

    /// <summary>
    /// See <see cref="DeclaredWidth"/>.
    /// </summary>
    public int DeclaredHeight { get; private set; }

    /// <summary>
    /// How many colours this image has DEFINED so far, counting in the order they were written.
    /// </summary>
    /// <remarks>
    /// Definitions, not selections. <c>#3</c> on its own picks register 3 and is not counted;
    /// <c>#3;1;0;99;0</c> defines it and is. The count is what the two properties below are keyed
    /// on, and it is the reason they cannot simply read registers 6 and 16 of the map.
    /// </remarks>
    public int DefinedColourCount { get; private set; }

    /// <summary>
    /// The SIXTH colour this image defined, which a VT340 adopts as its text foreground.
    /// </summary>
    /// <remarks>
    /// <para><b>The VT340's peculiar ordering scheme</b></para>
    /// A VT340 takes its text colours from a Sixel image's colour definitions, by the ORDER they
    /// were written rather than by the register each was given: the sixth defined becomes the text
    /// foreground and the sixteenth the text background.
    ///
    /// It reads like a bug and it is what the hardware does. hackerb9's <c>cat-vt340.six</c> is
    /// built around it - it defines exactly sixteen colours, in an order that has nothing to do
    /// with their register numbers, and says so in its own comment - and the capture taken from a
    /// real VT340 beside it has the whole screen in the sixteenth colour it defines.
    ///
    /// Null until six colours have been defined. The decoder only REPORTS it; whether a terminal
    /// acts on it is the terminal's business, and only the VT330 and VT340 do.
    /// </remarks>
    public GraphicsColor? ImageTextForeground { get; private set; }

    /// <summary>
    /// The SIXTEENTH colour this image defined, which a VT340 adopts as its text background.
    /// </summary>
    /// <remarks>
    /// See <see cref="ImageTextForeground"/>. Null until sixteen colours have been defined, which
    /// most images never do.
    /// </remarks>
    public GraphicsColor? ImageTextBackground { get; private set; }

    /// <summary>
    /// Builds a decoder with a colour map of its own.
    /// </summary>
    public SixelDecoder()
        : this(new GraphicsColorMap())
    {
    }

    /// <summary>
    /// Builds a decoder sharing a colour map with whoever else holds it.
    /// </summary>
    /// <param name="colours">
    /// The map. A terminal passes the SAME instance here and to its ReGIS decoder, because the
    /// hardware has one map and streams rely on that - see <see cref="GraphicsColorMap"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="colours"/> is null.
    /// </exception>
    public SixelDecoder(GraphicsColorMap colours)
    {
        _colours = colours ?? throw new ArgumentNullException(nameof(colours));
    }

    /// <summary>
    /// Returns the decoder to the state a fresh image starts in.
    /// </summary>
    /// <remarks>
    /// The colour registers are NOT cleared here. A host is entitled to define its palette in one
    /// image and use it in the next, and several encoders do exactly that.
    /// </remarks>
    public void BeginImage()
    {
        _currentRegister = 0;
        _x = 0;
        _bandTop = 0;
        SixelCursorTop = 0;
        _repeat = 1;
        _rowsPerPixel = _defaultRowsPerPixel;
        Width = 0;
        Height = 0;
        DeclaredWidth = 0;
        DeclaredHeight = 0;

        // Per IMAGE, like the size: the ordering rule counts the definitions in one image, so a
        // second image starts counting again rather than carrying on from the first.
        DefinedColourCount = 0;
        ImageTextForeground = null;
        ImageTextBackground = null;
    }

    /// <summary>
    /// The colour map this decoder paints from, so a terminal can hand the same one to ReGIS.
    /// </summary>
    public GraphicsColorMap Colours => _colours;

    /// <summary>
    /// Restores the sixteen colours a terminal powers on with.
    /// </summary>
    /// <remarks>
    /// The map is DEC's table 2-3, not an approximation - see <see cref="GraphicsColorMap"/>. An
    /// image that never defines a colour still has to draw SOMETHING, and leaving the registers
    /// black would make it invisible.
    /// </remarks>
    public void ResetRegisters() => _colours.Reset();

    /// <summary>
    /// Reads one colour register.
    /// </summary>
    /// <param name="index">
    /// Register number.
    /// </param>
    /// <returns>
    /// The colour, or transparent when the number is outside the register file.
    /// </returns>
    public GraphicsColor Register(int index) => _colours.Register(index);

    /// <summary>
    /// Decodes a whole Sixel payload onto a surface.
    /// </summary>
    /// <param name="payload">
    /// The characters between the DCS introducer and its terminator.
    /// </param>
    /// <param name="surface">
    /// Where the pixels go. Everything clips, so an image larger than the surface is not an error.
    /// </param>
    public void Decode(ReadOnlySpan<char> payload, IGraphicsSurface surface)
    {
        if (surface == null) throw new ArgumentNullException(nameof(surface));

        BeginImage();

        int i = 0;
        while (i < payload.Length)
        {
            char c = payload[i];

            switch (c)
            {
                case '#':
                    i = ReadColour(payload, i + 1);
                    continue;

                case '!':
                    i = ReadRepeat(payload, i + 1);
                    continue;

                case '"':
                    i = ReadRaster(payload, i + 1);
                    continue;

                case '-':
                    // Next band, back at the left edge. Six sixel pixels, each as many screen rows
                    // tall as the aspect ratio asked for.
                    _bandTop += 6 * _rowsPerPixel;
                    SixelCursorTop = _bandTop;
                    _x = 0;
                    i++;
                    continue;

                case '$':
                    // Back to the left edge WITHOUT dropping a band, so the same six rows can be
                    // painted again in another colour. This is how a multi-coloured image is built.
                    _x = 0;
                    i++;
                    continue;
            }

            if (c >= '?' && c <= '~')
            {
                PaintStrip(surface, c - '?');
                i++;
                continue;
            }

            // Anything else - a stray newline in a file, a byte from a mangled stream - is skipped
            // rather than guessed at.
            i++;
        }
    }

    /// <summary>
    /// Paints one strip character, repeated as many times as <c>!</c> asked for.
    /// </summary>
    /// <param name="surface">
    /// Where the pixels go.
    /// </param>
    /// <param name="bits">
    /// The six pixels, low bit at the top.
    /// </param>
    private void PaintStrip(IGraphicsSurface surface, int bits)
    {
        var colour = _colours.Register(_currentRegister);

        for (int repeat = 0; repeat < _repeat; repeat++)
        {
            for (int bit = 0; bit < 6; bit++)
            {
                if ((bits & (1 << bit)) == 0) continue;

                // One sixel pixel is _rowsPerPixel screen rows tall. At the usual 1:1 that is the
                // single row it always was; at the 2:1 that DEC's own encoders emit it is two.
                int top = _bandTop + bit * _rowsPerPixel;

                for (int row = 0; row < _rowsPerPixel; row++)
                {
                    // PaintOffsetY is zero for every ordinary image. See its remarks for the one
                    // case that sets it: an image taller than the screen, whose visible part is
                    // the bottom rather than the top.
                    surface.SetPixel(_x, top + row + PaintOffsetY, colour);
                }

                if (top + _rowsPerPixel > Height) Height = top + _rowsPerPixel;
            }

            _x++;
            if (_x > Width) Width = _x;
        }

        // The repeat count applies to ONE strip and then goes back to one. Leaving it set is how a
        // whole image ends up drawn at the width of its first run.
        _repeat = 1;
    }

    /// <summary>
    /// Reads a colour introducer: <c>#Pc</c> to select, or <c>#Pc;Pu;Px;Py;Pz</c> to define.
    /// </summary>
    /// <param name="payload">
    /// The whole payload.
    /// </param>
    /// <param name="start">
    /// First character after the '#'.
    /// </param>
    /// <returns>
    /// The index just past the introducer.
    /// </returns>
    private int ReadColour(ReadOnlySpan<char> payload, int start)
    {
        Span<int> parameters = stackalloc int[5];

        // Cleared explicitly, because the rule below LEANS on the unwritten slots being zero: a
        // definition that stops early means the coordinates it left out are zero, and reading
        // whatever the stack happened to hold would give a different colour every run.
        parameters.Clear();

        int count = ReadParameters(payload, start, parameters, out int next);

        if (count == 0) return next;

        int index = parameters[0];
        if (!GraphicsColorMap.IsRegister(index)) return next;

        _currentRegister = index;

        // ONE number is a selection and nothing else - "Once the assignment has been made, Pc can
        // be specified alone to select a color", VT330/VT340 Programmer Reference Volume 2, DECGCI.
        //
        // TWO OR MORE is a definition, even a short one, and the coordinates left out are ZERO. Both
        // DEC manuals in spec\DEC\ say so in as many words: the Level 2 Sixel reference's DECGCI
        // Error Handling - "if hue, lightness, or saturation is omitted, a value of 0 is assumed",
        // and the same for red, green and blue - and the VT330/VT340 manual's own note on the
        // parameter separator, that a missing number either side of a ';' is taken as 0.
        //
        // Until 26 August 2026 anything shorter than five numbers defined nothing at all. A host
        // sending the legal "#2;1;120;50" got its register left at the power-on colour and its
        // picture painted in the wrong one, with nothing to show that it had happened - a wrong
        // colour still draws.
        if (count < 2) return next;

        int system = parameters[1];
        if (system == 2)
        {
            // RGB, each component 0-100 rather than 0-255.
            _colours.SetFromRgbPercent(index, parameters[2], parameters[3], parameters[4]);
        }
        else if (system == 1)
        {
            // HLS, in degrees and percentages. Converted rather than approximated, because a host
            // that sends HLS gets the colour it asked for or the picture is wrong.
            _colours.SetFromHls(index, parameters[2], parameters[3], parameters[4]);
        }
        else
        {
            // "Other -- Ignore sequence", from the table of Pu values in the Level 2 Sixel
            // reference. So it defines nothing, and it does not count as a definition either - the
            // ordering rule below counts colours the image actually set.
            //
            // The SELECTION above is deliberately kept. Pu governs the assignment, which is what
            // the sentence is about; throwing the colour number away as well would paint into
            // whatever register was current instead of the one named. That reading is ours and is
            // not in the manual - see SixelShortColourIntroducerTests for where it is pinned, and
            // for what the cat-vt240 hardware capture does that we cannot explain.
            //
            // The opposite reading WAS tried and reverted (28 August 2026): ignoring the whole
            // introducer, selection included, whenever Pu is unknown. It went because the cat-vt240
            // fixture re-selects "#1" bare 28 times, and hackerb9's sixelcomments.md records early
            // VT240 firmware mishandling sixel palettes - so the photograph may be a record of that
            // firmware rather than a rule to copy.
            return next;
        }

        DefinedColourCount++;

        // The VT340's ordering rule. See ImageTextForeground for what it is and why it is here.
        if (DefinedColourCount == 6)
        {
            ImageTextForeground = _colours.Register(index);
        }
        else if (DefinedColourCount == 16)
        {
            ImageTextBackground = _colours.Register(index);
        }

        return next;
    }

    /// <summary>
    /// Reads a repeat introducer: <c>!Pn</c>.
    /// </summary>
    /// <param name="payload">
    /// The whole payload.
    /// </param>
    /// <param name="start">
    /// First character after the '!'.
    /// </param>
    /// <returns>
    /// The index just past the count.
    /// </returns>
    private int ReadRepeat(ReadOnlySpan<char> payload, int start)
    {
        Span<int> parameters = stackalloc int[1];
        int count = ReadParameters(payload, start, parameters, out int next);

        // A repeat of zero would paint nothing and silently swallow the strip that follows, so it
        // is treated as one - which is what a count of "none" means.
        _repeat = count > 0 && parameters[0] > 0 ? parameters[0] : 1;
        return next;
    }

    /// <summary>
    /// Reads raster attributes: <c>"Pan;Pad;Ph;Pv</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Pan over Pad is a vertical pixel replication count</b></para>
    /// It is not a hint. A VT340 draws each sixel pixel Pan/Pad screen rows tall, so an image sent
    /// at 2:1 - which is what DEC's own encoders emit - is twice as tall as the number of sixel
    /// rows in it. Reading the numbers and painting square pixels anyway drew every such image at
    /// half its height. hackerb9's vt340test corpus proves the rule twice over: multisize.six
    /// changes the ratio three times inside one image to build a flag out of three differently
    /// scaled bands, and extremeratio.six sends 80:1 with the note that a real VT340 renders each
    /// sixel 480 screen pixels high. Ours drew both as a sliver along the top edge.
    ///
    /// <para><b>The declared size is separate</b></para>
    /// Ph and Pv are already in SCREEN pixels, ratio included, so nothing scales them. A terminal
    /// needs them to size the plane before the pixels arrive.
    ///
    /// <para><b>What is still missing</b></para>
    /// The older VT125 mechanism - the first positional parameter of the DCS itself selecting one
    /// of a fixed set of ratios - is not implemented, because no document held here gives that
    /// table. Raster attributes supersede it and every fixture in the corpus uses them.
    /// </remarks>
    /// <param name="payload">
    /// The whole payload.
    /// </param>
    /// <param name="start">
    /// First character after the '"'.
    /// </param>
    /// <returns>
    /// The index just past the attributes.
    /// </returns>
    private int ReadRaster(ReadOnlySpan<char> payload, int start)
    {
        Span<int> parameters = stackalloc int[4];
        int count = ReadParameters(payload, start, parameters, out int next);

        if (count >= 2)
        {
            int numerator = parameters[0];
            int denominator = parameters[1];

            // A zero or negative denominator is a mangled stream, not a request to divide by zero.
            // Falling back to 1:1 keeps the picture readable rather than dropping the image.
            _rowsPerPixel = denominator > 0 && numerator > 0
                ? Math.Max(1, numerator / denominator)
                : 1;
        }

        if (count >= 3) DeclaredWidth = parameters[2];
        if (count >= 4) DeclaredHeight = parameters[3];

        return next;
    }

    /// <summary>
    /// Reads a run of semicolon-separated numbers.
    /// </summary>
    /// <param name="payload">
    /// The whole payload.
    /// </param>
    /// <param name="start">
    /// Where the numbers begin.
    /// </param>
    /// <param name="parameters">
    /// Receives the numbers, up to its own length.
    /// </param>
    /// <param name="next">
    /// The index just past the last number.
    /// </param>
    /// <returns>
    /// How many numbers were read.
    /// </returns>
    private static int ReadParameters(ReadOnlySpan<char> payload, int start, Span<int> parameters,
        out int next)
    {
        int count = 0;
        int value = 0;
        bool anyDigits = false;
        int i = start;

        for (; i < payload.Length; i++)
        {
            char c = payload[i];

            if (c >= '0' && c <= '9')
            {
                // Bounded so a host cannot overflow the accumulator with a long run of digits.
                if (value < 1_000_000) value = value * 10 + (c - '0');
                anyDigits = true;
                continue;
            }

            if (c == ';')
            {
                if (count < parameters.Length) parameters[count] = value;
                count++;
                value = 0;
                anyDigits = false;
                continue;
            }

            // Whitespace inside the parameters is IGNORED, not an end to them. Sixel data
            // characters run from '?' to '~', so a space or a line break can never be data and a
            // hand-written stream is entitled to lay its numbers out readably. hackerb9's corpus
            // does exactly that - "#1;2; 0;13;28" - and a real VT340 renders those files with the
            // colours defined. Stopping at the space instead left every such register at its
            // power-on default, which is how a navy-blue flag came out bright blue.
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                continue;
            }

            break;
        }

        if (anyDigits || count > 0)
        {
            if (count < parameters.Length) parameters[count] = value;
            count++;
        }

        next = i;
        return count > parameters.Length ? parameters.Length : count;
    }
}
