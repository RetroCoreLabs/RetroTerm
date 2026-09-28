using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;

namespace RetroTerm.TestServer.App;

/// <summary>
/// The graphics half of the test server - Sixel, ReGIS and Tektronix vectors driven from a host.
/// </summary>
/// <remarks>
/// This is the only place a person can look at what the graphics decoders draw without writing a
/// program. It matters more than the text suites, because three defects have already reached a
/// build here that no assertion caught and one glance would have:
///
///  - Red and blue swapped in every graphic. Invisible while only one colour was ever drawn.
///  - Axis labels in a diagonal cascade, while every label was printed correctly as text.
///  - Vectors drawn in green on an amber screen, which no single-phosphor machine can do.
///
/// So the Sixel test uses THREE colours, the Tektronix test labels its own axes, and there is a
/// phosphor test that puts text and vectors on the screen together.
/// </remarks>
public partial class TestServerApp
{
    #region Graphics menu routing

    /// <summary>
    /// Runs one entry of the graphics tests menu.
    /// </summary>
    private async Task HandleGraphicsTestsMenuAsync(TelnetSession session, char key)
    {
        switch (key)
        {
            case '1':
                await RunGfx_SixelThreeColoursAsync(session);
                await WriteMenuAsync(session);
                break;
            case '2':
                await RunGfx_SixelGreyRampAsync(session);
                await WriteMenuAsync(session);
                break;
            case '3':
                await RunGfx_SixelWithTextAsync(session);
                await WriteMenuAsync(session);
                break;
            case '4':
                await RunGfx_RegisAsync(session);
                await WriteMenuAsync(session);
                break;
            case '5':
                await RunGfx_TektronixVectorsAsync(session);
                await WriteMenuAsync(session);
                break;
            case '6':
                await RunGfx_TektronixLabelledAxesAsync(session);
                await WriteMenuAsync(session);
                break;
            case '7':
                await RunGfx_PhosphorAsync(session);
                await WriteMenuAsync(session);
                break;
            case '8':
                await RunGfx_RegisGraphicsInputAsync(session);
                await WriteMenuAsync(session);
                break;
        }
    }

    /// <summary>
    /// Prints the graphics tests menu.
    /// </summary>
    internal void WriteGraphicsTestsMenu(StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== Graphics Tests (Sixel, ReGIS, Tektronix) ===\x1b[0m");
        writer.AppendLine("1. Sixel - THREE colour bars (this is what catches a channel swap)");
        writer.AppendLine("2. Sixel - grey ramp and aspect ratio");
        writer.AppendLine("3. Sixel - an image with text around it");
        writer.AppendLine("4. ReGIS - lines, a circle and text");
        writer.AppendLine("5. Tektronix - a box, a grid and diagonals");
        writer.AppendLine("6. Tektronix - axes with labels AT their ticks");
        writer.AppendLine("7. Phosphor - text and vectors together, for a theme change");
        writer.AppendLine("8. ReGIS - graphics input mode, all four cursor shapes (you drive)");
        writer.AppendLine("0/B. Back to Main Menu");
    }

    #endregion

    #region Sixel helpers

    /// <summary>
    /// Builds one horizontal band of a sixel image in a single colour.
    /// </summary>
    /// <param name="colorIndex">
    /// The colour register to select before the run.
    /// </param>
    /// <param name="width">
    /// How many pixel columns the run covers.
    /// </param>
    /// <returns>
    /// The sixel text for that run, using the repeat introducer rather than one character per
    /// column.
    /// </returns>
    internal static string SixelRun(int colorIndex, int width)
    {
        // '~' is 0x7E, which is the sixel with all six pixels lit. '!' repeats the character after
        // it, so a 20-wide run is four characters rather than twenty.
        return $"#{colorIndex}!{width}~";
    }

    #endregion

    #region Sixel tests

    /// <summary>
    /// Three coloured bars, in an order that makes a swapped red and blue channel obvious.
    /// </summary>
    /// <remarks>
    /// A two-colour image cannot show this defect and a one-colour image certainly cannot: the last
    /// time the channels were reversed, the only thing being drawn was a green that stayed green
    /// when its red and blue were exchanged. Three saturated bars in a stated order cannot hide it.
    /// </remarks>
    private async Task RunGfx_SixelThreeColoursAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Sixel - Three Colour Bars");

        await session.WriteAsync("Drawing three bars. Left to right they must be:\r\n");
        await session.WriteAsync("  \x1b[1;31mRED\x1b[0m   \x1b[1;32mGREEN\x1b[0m   \x1b[1;34mBLUE\x1b[0m\r\n\r\n");
        await session.WriteAsync("\x1b[33mIf the first bar is blue and the last is red, the colour channels\r\n");
        await session.WriteAsync("are the wrong way round.\x1b[0m\r\n\r\n");

        var image = new StringBuilder();
        image.Append("\x1bPq");                                   // DCS q - start of sixel data

        // Colour registers, given as percentages: #<reg>;2;<red>;<green>;<blue>
        image.Append("#0;2;100;0;0");                             // red
        image.Append("#1;2;0;100;0");                             // green
        image.Append("#2;2;0;0;100");                             // blue

        // Six bands of six pixels each is 36 pixels tall; each bar is 40 pixels wide.
        for (int band = 0; band < 6; band++)
        {
            image.Append(SixelRun(0, 40));
            image.Append(SixelRun(1, 40));
            image.Append(SixelRun(2, 40));
            image.Append('-');                                    // end of band, move down six pixels
        }

        image.Append("\x1b\\");                                   // ST - end of sixel data
        await session.WriteAsync(image.ToString());

        await session.WriteAsync("\r\n\r\n\x1b[1;37mAlso judge:\x1b[0m the bars are the same width, the edges are straight,\r\n");
        await session.WriteAsync("and the text below is not sitting on top of the image.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// A grey ramp, which shows banding and aspect ratio in one picture.
    /// </summary>
    private async Task RunGfx_SixelGreyRampAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Sixel - Grey Ramp and Aspect");

        await session.WriteAsync("Sixteen grey steps from black to white, then a square.\r\n\r\n");

        var image = new StringBuilder();
        image.Append("\x1bPq");

        // Sixteen registers, evenly spaced from black to white.
        for (int i = 0; i < 16; i++)
        {
            int level = i * 100 / 15;
            image.Append($"#{i};2;{level};{level};{level}");
        }

        for (int band = 0; band < 4; band++)
        {
            for (int i = 0; i < 16; i++)
            {
                image.Append(SixelRun(i, 12));
            }
            image.Append('-');
        }

        image.Append("\x1b\\");
        await session.WriteAsync(image.ToString());

        await session.WriteAsync("\r\n\r\nAnd a square - 60 by 60 pixels, in white:\r\n");
        var square = new StringBuilder();
        square.Append("\x1bPq");
        square.Append("#0;2;100;100;100");
        for (int band = 0; band < 10; band++)
        {
            square.Append(SixelRun(0, 60));
            square.Append('-');
        }
        square.Append("\x1b\\");
        await session.WriteAsync(square.ToString());

        await session.WriteAsync("\r\n\r\n\x1b[1;37mWhat to judge:\x1b[0m the ramp goes black to white in even steps, and\r\n");
        await session.WriteAsync("the square is SQUARE. A tall or wide square means the pixel aspect is wrong.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// An image with text above and below it, which is where a wrong cursor position after an
    /// image shows itself.
    /// </summary>
    private async Task RunGfx_SixelWithTextAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Sixel Mixed with Text");

        await session.WriteAsync("TEXT ABOVE THE IMAGE - this line must stay put.\r\n");

        var image = new StringBuilder();
        image.Append("\x1bPq");
        image.Append("#0;2;100;80;0");                            // amber, so it reads on either theme
        for (int band = 0; band < 3; band++)
        {
            image.Append(SixelRun(0, 100));
            image.Append('-');
        }
        image.Append("\x1b\\");
        await session.WriteAsync(image.ToString());

        await session.WriteAsync("TEXT BELOW THE IMAGE - this must start on a clear row.\r\n");
        await session.WriteAsync("\r\nAnd here is a second image on the same screen:\r\n");
        await session.WriteAsync(image.ToString());
        await session.WriteAsync("Text after the second image.\r\n");

        await session.WriteAsync("\r\n\x1b[1;37mWhat to judge:\x1b[0m neither image erased the text, the text after each\r\n");
        await session.WriteAsync("image starts below it rather than through it, and scrolling the screen\r\n");
        await session.WriteAsync("moves the images with the text.\r\n");

        await EndTestAsync(session);
    }

    #endregion

    #region ReGIS test

    /// <summary>
    /// A ReGIS drawing - lines, a circle and text.
    /// </summary>
    /// <remarks>
    /// How complete the ReGIS decoder is has NOT been written down, so this test is a way of
    /// finding out rather than a pass-or-fail. Anything that does not appear is worth recording
    /// against P2.4 of the finish plan.
    /// </remarks>
    private async Task RunGfx_RegisAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "ReGIS Drawing");

        await session.WriteAsync("Drawing a box, two diagonals, a circle and a word.\r\n");
        await session.WriteAsync("\x1b[33mAnything missing here is a gap in the ReGIS decoder, which has not\r\n");
        await session.WriteAsync("been surveyed yet.\x1b[0m\r\n\r\n");

        var regis = new StringBuilder();
        regis.Append("\x1bPp");                                   // DCS p - start of ReGIS

        regis.Append("S(E)");                                     // erase the drawing surface
        regis.Append("W(I3)");                                    // write with colour register 3

        // A box: move to a corner, then draw the four sides as one vector chain.
        regis.Append("P[100,100]");
        regis.Append("V[500,100][500,400][100,400][100,100]");

        // The two diagonals, which show whether a vector list restarts correctly.
        regis.Append("P[100,100]V[500,400]");
        regis.Append("P[500,100]V[100,400]");

        // A circle centred in the box. C[+r] draws a circle of that radius about the current point.
        regis.Append("P[300,250]C[+120]");

        // Text at a stated position.
        regis.Append("P[150,450]T(S1)\"REGIS\"");

        regis.Append("\x1b\\");                                   // ST
        await session.WriteAsync(regis.ToString());

        await session.WriteAsync("\r\n\r\n\x1b[1;37mWhat to judge:\x1b[0m four straight sides that meet at the corners, two\r\n");
        await session.WriteAsync("diagonals crossing in the middle, a round circle that is not an ellipse,\r\n");
        await session.WriteAsync("and the word REGIS below the box.\r\n");

        await EndTestAsync(session);
    }

    #endregion

    #region ReGIS graphics input test

    /// <summary>
    /// The four ReGIS cursor shapes, in the order this test walks them.
    /// </summary>
    /// <remarks>
    /// The numbers are the argument to <c>S(C(I n ))</c>. 0 and 2 are both the crosshair, so only 0
    /// is walked here; 1 is the diamond, 3 the rubber band line and 4 the rubber band rectangle.
    /// </remarks>
    private static readonly int[] GraphicsInputCursorStyles = new[] { 0, 1, 3, 4 };

    /// <summary>
    /// What each entry of <see cref="GraphicsInputCursorStyles"/> is called, for the prompt.
    /// </summary>
    private static readonly string[] GraphicsInputCursorNames = new[]
    {
        "crosshair - two lines crossing the whole screen",
        "diamond - a small lozenge, 21 pixels across",
        "rubber band LINE - stretches from the drawing point to the cursor",
        "rubber band RECTANGLE - a box between the drawing point and the cursor",
    };

    /// <summary>
    /// ReGIS graphics input mode, driven by hand, with all four cursor shapes in turn.
    /// </summary>
    /// <param name="session">
    /// The client to drive.
    /// </param>
    /// <remarks>
    /// <para><b>What this exists for</b></para>
    /// Case M6.5 of the by-hand pass. Every part of it is a judgement no test can make: whether the
    /// crosshair is easy enough to AIM with, whether one pixel per arrow press is usable at all, and
    /// whether the diamond can still be found on a busy drawing.
    /// <para><b>Why the drawing is busy on purpose</b></para>
    /// A cursor is trivial to see on an empty screen. The box, diagonals and circle are here so the
    /// cursor has to compete with something, which is the only condition under which "can you find
    /// it" means anything.
    /// <para><b>Why the style is selected BEFORE the mode is entered</b></para>
    /// One-shot graphics input buffers everything the host sends until the operator answers, so a
    /// style selected afterwards would not arrive until the mode had already ended. That is the
    /// manual's behaviour rather than a defect, and selecting it first is the only order that works.
    /// <para><b>The held-output line is deliberate</b></para>
    /// A line is written immediately AFTER the mode is entered. It must not appear until the
    /// operator answers, and then it must appear in the right order. That is the whole of M6.5c, and
    /// writing it here is the only way to see it happen.
    /// </remarks>
    private async Task RunGfx_RegisGraphicsInputAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "ReGIS Graphics Input Mode");

        await session.WriteAsync("Four rounds, one per cursor shape. In each one:\r\n\r\n");
        await session.WriteAsync("  \x1b[1;37marrow keys\x1b[0m        move the cursor one pixel\r\n");
        await session.WriteAsync("  \x1b[1;37mshift+arrow\x1b[0m       moves it ten\r\n");
        await session.WriteAsync("  \x1b[1;37many normal key\x1b[0m    answers, and ends the round\r\n\r\n");
        await session.WriteAsync("\x1b[33mWhile a round is running the host is held - nothing new can appear on\r\n");
        await session.WriteAsync("the screen until you answer. That is the point of the test, not a hang.\x1b[0m\r\n\r\n");

        for (int i = 0; i < GraphicsInputCursorStyles.Length; i++)
        {
            int style = GraphicsInputCursorStyles[i];

            await session.WriteAsync("\x1b[1;36m--- Round " + (i + 1) + " of 4: " + GraphicsInputCursorNames[i] + "\x1b[0m\r\n");
            await session.WriteAsync("Move the cursor, then press any letter to answer.\r\n");

            var regis = new StringBuilder();
            regis.Append("\x1bPp");                                // DCS p - start of ReGIS

            regis.Append("S(E)");                                  // erase the drawing surface
            regis.Append("W(I3)");                                 // write with colour register 3

            // Something for the cursor to compete with - a box, its two diagonals and a circle.
            regis.Append("P[100,100]");
            regis.Append("V[500,100][500,400][100,400][100,100]");
            regis.Append("P[100,100]V[500,400]");
            regis.Append("P[500,100]V[100,400]");
            regis.Append("P[300,250]C[+120]");

            // The cursor shape, BEFORE the mode is entered. See the remarks above for why.
            regis.Append("S(C(I" + style + "))");

            // The drawing point. The two rubber band shapes stretch from HERE to the cursor, so it
            // has to be somewhere the operator can see the band move away from.
            regis.Append("P[400,240]");

            // One-shot graphics input, then report the position when it ends.
            regis.Append("R(I0)R(P(I))");

            regis.Append("\x1b\\");                                // ST
            await session.WriteAsync(regis.ToString());

            // Written while the mode is running, so it MUST be held. If this line appears before the
            // operator answers, output is not being held and M6.5c fails.
            await session.WriteAsync("\x1b[32mThis line was sent while the cursor was up. If you are reading it\r\n");
            await session.WriteAsync("only now, after answering, holding and replay both work.\x1b[0m\r\n");

            string report = await CollectGraphicsInputReportAsync(session);
            await session.WriteAsync("Host received: \x1b[1;37m" + report + "\x1b[0m\r\n\r\n");
        }

        await session.WriteAsync("\x1b[1;37mWhat to judge:\x1b[0m\r\n");
        await session.WriteAsync(" a. both arms of the crosshair visible against the drawing\r\n");
        await session.WriteAsync(" b. one press moves one pixel, shift moves ten, edges wrap\r\n");
        await session.WriteAsync(" c. the green line above arrived only after you answered\r\n");
        await session.WriteAsync(" d. all four shapes usable - the diamond especially\r\n");
        await session.WriteAsync(" e. 'Host received' shows your key followed by a position\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Reads the next thing the terminal sent, or null when nothing arrives in time.
    /// </summary>
    /// <param name="timeoutMs">
    /// How long to wait.
    /// </param>
    /// <returns>
    /// What arrived, or null.
    /// </returns>
    /// <remarks>
    /// A delegate rather than the session itself, so the gathering below can be tested without a
    /// socket. <c>TelnetSession.TryReadInputAsync</c> is not virtual, and making it so to suit a test
    /// would be the tail wagging the dog.
    /// </remarks>
    internal delegate Task<ParsedInput?> ReadInputWithTimeout(int timeoutMs);

    /// <summary>
    /// Gathers what the terminal sends back when graphics input mode ends.
    /// </summary>
    /// <param name="session">
    /// The client to read from.
    /// </param>
    /// <returns>
    /// The report as readable text, or a note saying nothing arrived.
    /// </returns>
    private static Task<string> CollectGraphicsInputReportAsync(TelnetSession session)
        => CollectGraphicsInputReportAsync(timeoutMs => session.TryReadInputAsync(timeoutMs));

    /// <summary>
    /// Gathers one graphics input report, skipping the carriage return that entering the mode sends.
    /// </summary>
    /// <param name="read">
    /// Where the terminal's bytes come from.
    /// </param>
    /// <returns>
    /// The report as readable text, or a note saying nothing arrived.
    /// </returns>
    /// <remarks>
    /// <para><b>The report arrives in pieces</b></para>
    /// The reply is the answering character followed by the position in square brackets, and it
    /// reaches the input pump as SEVERAL separate reads rather than one - the character, then the
    /// bracket, then the digits. So this reads until the line has been quiet for a moment rather
    /// than reading a fixed number of times.
    ///
    /// The first read waits a long time on purpose: the operator is aiming, and how long that takes
    /// is up to them. Every read after it waits briefly, because by then the rest of the report is
    /// already on its way.
    ///
    /// <para><b>The synchronisation carriage return is NOT the answer</b></para>
    /// FOUND 27 AUGUST 2026, and it had made a correct terminal look broken for as long as this
    /// suite has existed. Chapter 10 of the VT330/VT340 Programmer Reference: "When the terminal
    /// receives R(I), it returns a carriage return (CR). Applications can use the CR for
    /// synchronization." So a CR arrives the instant the mode is entered, long before anybody has
    /// pressed anything.
    ///
    /// Reading that CR as the operator's answer put every round one behind: round one printed an
    /// empty "Host received:", round two printed round one's answer, and so on to the end. The
    /// reports themselves were right the whole time - only where they were PRINTED was wrong.
    ///
    /// <para><b>Telling it apart from an Enter that really was the answer</b></para>
    /// Enter is a legal answering key, and then the report itself STARTS with a CR. The difference
    /// is what follows: after the synchronisation CR the terminal goes quiet while the operator
    /// aims, and after an answering CR the position is already on its way. So a leading CR is
    /// followed up briefly, and only discarded when nothing comes after it.
    /// </remarks>
    internal static async Task<string> CollectGraphicsInputReportAsync(ReadInputWithTimeout read)
    {
        var report = new StringBuilder();

        while (true)
        {
            // No timeout worth naming for the first one - the operator is still aiming.
            ParsedInput? first = await read(600000);
            if (first == null) return "(nothing - the round timed out)";

            if (first.Value.Type != InputType.Enter)
            {
                report.Append(first.Value.Value);
                break;
            }

            // A carriage return, so far ambiguous. Whatever follows settles it.
            ParsedInput? follows = await read(400);
            if (follows == null)
            {
                // Nothing came, so that was the synchronisation CR. Wait again for the real answer.
                continue;
            }

            report.Append(first.Value.Value);
            report.Append(follows.Value.Value);
            break;
        }

        while (true)
        {
            ParsedInput? next = await read(400);
            if (next == null) break;
            report.Append(next.Value.Value);
        }

        return report.ToString();
    }

    #endregion

    #region Tektronix helpers

    /// <summary>
    /// Encodes one Tektronix coordinate as its four bytes.
    /// </summary>
    /// <param name="x">
    /// Horizontal position, 0 to 1023, with 0 at the left.
    /// </param>
    /// <param name="y">
    /// Vertical position, 0 to 1023, with 0 at the BOTTOM.
    /// </param>
    /// <returns>
    /// High Y, low Y, high X, low X - in that order, which is what ends the coordinate.
    /// </returns>
    /// <remarks>
    /// TEN bits per axis, five in each byte, which is the 4010 form this program decodes:
    /// TektronixVectorDecoder builds each axis as (high shifted left 5) or low. It is NOT the
    /// 12-bit 4014 form, where two more bits ride in a fifth byte - writing that here would have
    /// divided every coordinate by four and drawn the picture into one corner. Verified against
    /// the decoder on 2026-08-17 rather than taken from the usual description of a 4014.
    ///
    /// The tag bits are 0x20 for a high byte, 0x60 for the low Y, and 0x40 for the low X, which is
    /// also what ENDS the coordinate. All four bytes are sent every time here; a real host may
    /// leave out any that has not changed.
    /// </remarks>
    internal static string TekPoint(int x, int y)
    {
        char highY = (char)(0x20 | ((y >> 5) & 0x1F));
        char lowY = (char)(0x60 | (y & 0x1F));
        char highX = (char)(0x20 | ((x >> 5) & 0x1F));
        char lowX = (char)(0x40 | (x & 0x1F));
        return new string(new[] { highY, lowY, highX, lowX });
    }

    /// <summary>
    /// GS - enter graph mode. The first point after it moves without drawing.
    /// </summary>
    private const string TekGraphMode = "\x1d";

    /// <summary>
    /// US - back to alpha mode, where bytes are text again.
    /// </summary>
    private const string TekAlphaMode = "\x1f";

    /// <summary>
    /// ESC FF - erase the screen and start again at the top.
    /// </summary>
    private const string TekPageErase = "\x1b\x0c";

    #endregion

    #region Tektronix tests

    /// <summary>
    /// A box, a grid and two diagonals - enough geometry that a wrong axis direction or a stray
    /// line from the last drawing is obvious.
    /// </summary>
    private async Task RunGfx_TektronixVectorsAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Tektronix - Box, Grid and Diagonals");

        await session.WriteAsync("Entering graph mode with GS. On a TDV2200 this is the same byte.\r\n");
        await session.WriteAsync("Press Enter to draw...");
        await session.WaitForEnterAsync();

        var plot = new StringBuilder();
        plot.Append(TekPageErase);

        // The border. The first point after GS moves; the rest draw.
        // Coordinates are the 4010's 1024-wide space, and 780 is the top of a 4010 screen.
        plot.Append(TekGraphMode);
        plot.Append(TekPoint(50, 50));
        plot.Append(TekPoint(970, 50));
        plot.Append(TekPoint(970, 730));
        plot.Append(TekPoint(50, 730));
        plot.Append(TekPoint(50, 50));

        // Vertical grid lines. Each one re-enters graph mode so it starts as a MOVE - without that
        // there is a stray line back across the picture from where the last one ended.
        for (int i = 1; i < 8; i++)
        {
            int x = 50 + i * 115;
            plot.Append(TekGraphMode);
            plot.Append(TekPoint(x, 50));
            plot.Append(TekPoint(x, 730));
        }

        // Horizontal grid lines.
        for (int i = 1; i < 6; i++)
        {
            int y = 50 + i * 113;
            plot.Append(TekGraphMode);
            plot.Append(TekPoint(50, y));
            plot.Append(TekPoint(970, y));
        }

        // The diagonals, which cross in the middle if the axes run the way they should.
        plot.Append(TekGraphMode);
        plot.Append(TekPoint(50, 50));
        plot.Append(TekPoint(970, 730));
        plot.Append(TekGraphMode);
        plot.Append(TekPoint(50, 730));
        plot.Append(TekPoint(970, 50));

        plot.Append(TekAlphaMode);
        await session.WriteAsync(plot.ToString());

        await session.WriteAsync("\r\n\x1b[1;37mWhat to judge:\x1b[0m a closed box, an even grid, the diagonals crossing\r\n");
        await session.WriteAsync("in the CENTRE, and no stray line running back across the picture.\r\n");
        await session.WriteAsync("Tektronix Y runs upwards, so a picture that is upside down is a defect.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Axes with a label at every tick - the shape of the defect that shipped once.
    /// </summary>
    /// <remarks>
    /// Labels are written in alpha mode after a move in graph mode, which is how gnuplot does it.
    /// That only works if leaving graph mode parks the text cursor at the last plotted point; when
    /// it did not, every label was correct as text and the whole set marched diagonally down the
    /// screen. This test also leaves out coordinate bytes that have not changed, which is the part
    /// of the encoding derived from Tektronix practice rather than quoted from the ND spec.
    /// </remarks>
    private async Task RunGfx_TektronixLabelledAxesAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Tektronix - Labelled Axes");

        await session.WriteAsync("Each label is written where the tick is. Press Enter to draw...");
        await session.WaitForEnterAsync();

        var plot = new StringBuilder();
        plot.Append(TekPageErase);

        // The two axes.
        plot.Append(TekGraphMode);
        plot.Append(TekPoint(100, 80));
        plot.Append(TekPoint(970, 80));
        plot.Append(TekGraphMode);
        plot.Append(TekPoint(100, 80));
        plot.Append(TekPoint(100, 730));

        await session.WriteAsync(plot.ToString());

        // X labels: move in graph mode, drop to alpha, print, and the next label must NOT inherit
        // the last one's position.
        for (int i = 0; i <= 6; i++)
        {
            int x = 100 + i * 140;
            var label = new StringBuilder();
            label.Append(TekGraphMode);
            label.Append(TekPoint(x, 40));
            label.Append(TekAlphaMode);
            label.Append((i * 10).ToString());
            await session.WriteAsync(label.ToString());
        }

        // Y labels, up the left side.
        for (int i = 1; i <= 5; i++)
        {
            int y = 80 + i * 128;
            var label = new StringBuilder();
            label.Append(TekGraphMode);
            label.Append(TekPoint(30, y));
            label.Append(TekAlphaMode);
            label.Append((i * 20).ToString());
            await session.WriteAsync(label.ToString());
        }

        // A curve, drawn the way a plotting program draws one - and using the SHORT form of the
        // coordinate, where a byte that has not changed is left out.
        var curve = new StringBuilder();
        curve.Append(TekGraphMode);
        curve.Append(TekPoint(100, 150));
        for (int i = 1; i <= 40; i++)
        {
            int x = 100 + i * 21;
            // A simple rising curve. Integer arithmetic only - this is drawing, not mathematics.
            int y = 150 + (i * i) / 3;
            if (y > 720)
            {
                y = 720;
            }
            curve.Append(TekPoint(x, y));
        }
        curve.Append(TekAlphaMode);
        await session.WriteAsync(curve.ToString());

        await session.WriteAsync("\r\n\x1b[1;37mWhat to judge:\x1b[0m every number sits AT its tick. Labels that step\r\n");
        await session.WriteAsync("diagonally down the screen is the exact defect this test exists for.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Text and vectors on one screen, so a theme change can be judged.
    /// </summary>
    /// <remarks>
    /// No assertion can catch this one. A real terminal has ONE phosphor: if the text goes amber
    /// and the lines stay green, the drawing colour is a constant somewhere instead of the theme's
    /// foreground.
    /// </remarks>
    private async Task RunGfx_PhosphorAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Phosphor - Text and Vectors Together");

        await session.WriteAsync("Draw this, then change the terminal's phosphor theme - green to amber,\r\n");
        await session.WriteAsync("or amber to white - WITHOUT redrawing.\r\n\r\n");
        await session.WriteAsync("Press Enter to draw...");
        await session.WaitForEnterAsync();

        var plot = new StringBuilder();
        plot.Append(TekPageErase);

        // A simple shape with plenty of line in it.
        plot.Append(TekGraphMode);
        plot.Append(TekPoint(130, 130));
        plot.Append(TekPoint(890, 130));
        plot.Append(TekPoint(510, 650));
        plot.Append(TekPoint(130, 130));

        plot.Append(TekGraphMode);
        plot.Append(TekPoint(300, 260));
        plot.Append(TekPoint(720, 260));
        plot.Append(TekPoint(510, 520));
        plot.Append(TekPoint(300, 260));

        plot.Append(TekAlphaMode);
        await session.WriteAsync(plot.ToString());

        await session.WriteAsync("\r\nTEXT AND VECTORS ARE BOTH ON THIS SCREEN.\r\n");
        await session.WriteAsync("\r\n\x1b[1;37mWhat to judge:\x1b[0m after a theme change, the lines and the letters are\r\n");
        await session.WriteAsync("the SAME colour. A machine with one phosphor cannot show two.\r\n");

        await EndTestAsync(session);
    }

    #endregion
}
