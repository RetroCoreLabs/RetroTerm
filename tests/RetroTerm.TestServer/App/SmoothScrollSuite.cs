using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;

namespace RetroTerm.TestServer.App;

/// <summary>
/// A side-by-side demonstration of DECSCLM, the smooth scroll mode.
/// </summary>
/// <remarks>
/// <para><b>Why this exists as its own file</b></para>
/// Whether a scroll speed is comfortable is a judgement, and nothing in the test suite can make it.
/// The measurable half of smooth scrolling is covered by SmoothScrollTests; this is the half that
/// needs eyes on a running terminal.
/// <para><b>Why it runs the same lines more than once</b></para>
/// Nobody can judge a speed from one sample. The text is sent paced, then in bursts, then with the
/// mode switched off, so each part differs from the last in exactly one way.
/// <para><b>Why real prose and full-width lines</b></para>
/// The first version printed forty short numbered lines and Ronny's verdict was that it needed to
/// "run longer with more lines and longer lines". He was right: a short line leaves most of the
/// screen still, so the eye has nothing to follow, and forty lines is over before anybody has
/// settled into watching. Text that fills the width moves the whole screen, which is what is
/// actually being judged.
/// <para><b>Why there is a paced part AND a burst part</b></para>
/// They answer different questions. Paced output shows whether the slide itself is a speed worth
/// reading at, with nothing else going on. Bursts show what a REAL host does, because no real host
/// waits for a terminal: twenty lines land at once, far faster than the screen can follow, and the
/// picture has to run behind and walk its way forward. Only the burst part can show that nothing is
/// dropped on the way.
/// </remarks>
public partial class TestServerApp
{
    /// <summary>
    /// How many lines the paced part prints.
    /// </summary>
    /// <remarks>
    /// Sixty at roughly three a second is about twenty seconds, which is long enough to settle into
    /// watching and short enough that the burst part still gets looked at. It was 150 when this was
    /// the only part there was.
    /// </remarks>
    private const int SmoothScrollDemoLines = 60;

    /// <summary>
    /// How many lines the comparison half prints.
    /// </summary>
    /// <remarks>
    /// Shorter than the sliding half on purpose. It is there to remind the eye what jump scrolling
    /// looks like, not to be studied.
    /// </remarks>
    private const int SmoothScrollComparisonLines = 40;

    /// <summary>
    /// The gap between lines, in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para><b>This number is not a taste setting, it is a consequence</b></para>
    /// Longer than one slide, so this part never builds a backlog at all and every line is animated
    /// the moment it arrives. That is the whole point of the paced part: one thing happening, so
    /// there is nothing to blame the look of the slide on but the slide.
    ///
    /// A slide cannot be made much shorter. A character cell is about nineteen pixels, and smooth
    /// means moving about one pixel per displayed frame, so a line takes roughly nineteen frames -
    /// about 260ms at 60Hz. That puts the ceiling near four lines a second. A real VT100 ran six and
    /// held the host back with XOFF to keep it there.
    ///
    /// We cannot throttle a host, so faster output is handled the other way: the picture runs behind
    /// and catches up. That is what the burst part is for, and why this number does not have to be a
    /// limit on anything except this one paced run.
    /// </remarks>
    private const int SmoothScrollDemoLineGap = 290;

    /// <summary>
    /// How wide the generated lines are.
    /// </summary>
    /// <remarks>
    /// Just short of eighty so nothing wraps. A wrapped line scrolls TWICE for one line of text and
    /// would make the pacing look uneven for a reason that has nothing to do with the mode.
    /// </remarks>
    private const int SmoothScrollLineWidth = 76;

    /// <summary>
    /// The word pool the filler prose is built from.
    /// </summary>
    /// <remarks>
    /// Plain lorem ipsum. Real words rather than repeated characters, because a line of identical
    /// characters slides without the eye being able to tell it moved - which is the one thing this
    /// demonstration must not do.
    /// </remarks>
    private static readonly string[] LoremWords =
    {
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do",
        "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", "enim",
        "ad", "minim", "veniam", "quis", "nostrud", "exercitation", "ullamco", "laboris", "nisi",
        "aliquip", "ex", "ea", "commodo", "consequat", "duis", "aute", "irure", "in", "reprehenderit",
        "voluptate", "velit", "esse", "cillum", "eu", "fugiat", "nulla", "pariatur", "excepteur",
        "sint", "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
        "deserunt", "mollit", "anim", "id", "est", "laborum",
    };

    /// <summary>
    /// How many lines each burst dumps at once.
    /// </summary>
    /// <remarks>
    /// Just inside the limit on how far the picture is allowed to fall behind, which is a screenful.
    /// A burst bigger than that would hit the limit and start jumping part way through, which is
    /// honest behaviour but not what this part is trying to show.
    /// </remarks>
    private const int SmoothScrollBurstLines = 20;

    /// <summary>
    /// How many bursts to send.
    /// </summary>
    private const int SmoothScrollBurstCount = 4;

    /// <summary>
    /// How long one line takes to slide into place, in milliseconds.
    /// </summary>
    /// <remarks>
    /// A MIRROR of SmoothScrollDurationMilliseconds in TerminalCanvas, which this project cannot
    /// reference - the test server talks to any terminal over a socket and knows nothing about the
    /// program at the other end. It is used only to work out how long to wait, so a client that
    /// slides at a different speed makes the pauses slightly wrong and nothing else.
    /// </remarks>
    private const int SmoothScrollSlideMilliseconds = 260;

    /// <summary>
    /// Prints a long run of prose three ways: paced, in bursts, and jumping.
    /// </summary>
    /// <param name="session">
    /// The client to drive.
    /// </param>
    private async Task RunSmoothScrollAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Smooth Scroll (DECSCLM, private mode 4)");

        await session.WriteAsync("The same text three ways.\r\n\r\n");
        await session.WriteAsync("  1. \x1b[1;37mPACED\x1b[0m  - " + SmoothScrollDemoLines
            + " lines, slow enough that every one slides\r\n");
        await session.WriteAsync("  2. \x1b[1;37mBURST\x1b[0m  - " + SmoothScrollBurstCount
            + " dumps of " + SmoothScrollBurstLines + " lines, sent all at once\r\n");
        await session.WriteAsync("  3. \x1b[1;37mJUMP\x1b[0m   - " + SmoothScrollComparisonLines
            + " lines the way it has always worked\r\n\r\n");
        await session.WriteAsync("\x1b[1;37mWhat to judge in 1:\x1b[0m whether the slide is a speed you\r\n");
        await session.WriteAsync("would want to read at. Too slow is as wrong as too fast.\r\n\r\n");
        await session.WriteAsync("\x1b[1;37mWhat to judge in 2:\x1b[0m the host sends far faster than the\r\n");
        await session.WriteAsync("screen can slide, so the picture runs behind and walks its way\r\n");
        await session.WriteAsync("forward. Nothing should be skipped, and nothing should jump.\r\n\r\n");
        await session.WriteAsync("Press Enter to start...");
        await session.WaitForEnterAsync();

        // FILL THE SCREEN FIRST, instantly. On an EMPTY screen the first twenty-four lines do not
        // scroll at all, they merely fill it - so a demonstration that clears and then paces its
        // output spends its first seconds with nothing moving, which reads as having hung.
        await session.WriteAsync("\x1b[2J\x1b[H");
        await FillScreenAsync(session);

        await session.WriteAsync("\x1b[?4h");
        await SendProseAsync(session, "PACED ", SmoothScrollDemoLines, 1);

        await Task.Delay(1200);
        await SendBurstsAsync(session);

        // Back to jump scroll for the comparison.
        await session.WriteAsync("\x1b[?4l");
        await session.WriteAsync("\x1b[1;33m--- same text again, smooth scrolling OFF ---\x1b[0m\r\n");
        await Task.Delay(1500);
        await SendProseAsync(session, "JUMP  ", SmoothScrollComparisonLines, 7);

        await session.WriteAsync("\r\n\x1b[1;37mIf the slide was the wrong speed, say faster or slower.\x1b[0m\r\n");
        await session.WriteAsync("It is one number - SmoothScrollDurationMilliseconds in\r\n");
        await session.WriteAsync("TerminalCanvas, 260 ms today. Much below that the slide runs out of\r\n");
        await session.WriteAsync("frames to move in and jumps several pixels at a time instead.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Dumps several blocks of lines at once, pausing long enough between them to catch up.
    /// </summary>
    /// <param name="session">
    /// The client to write to.
    /// </param>
    /// <remarks>
    /// <para><b>What this half is for</b></para>
    /// The paced half proves the slide looks right. It cannot prove anything about a REAL host,
    /// because no real host waits for a terminal. This one sends a block faster than the screen can
    /// possibly follow and then stops, so the picture is left owing twenty lines and has to walk
    /// forward through them on its own.
    /// <para><b>Why the pause between bursts is longer than the catching up takes</b></para>
    /// Twenty lines at about a quarter second each is around five seconds of climbing. The gap is
    /// longer so the picture reaches the end of one burst and visibly stops before the next arrives.
    /// Overlapping them would only show one long climb, which says nothing about whether it ever
    /// actually catches up.
    /// </remarks>
    private async Task SendBurstsAsync(TelnetSession session)
    {
        for (int burst = 1; burst <= SmoothScrollBurstCount; burst++)
        {
            var block = new StringBuilder();
            for (int i = 1; i <= SmoothScrollBurstLines; i++)
            {
                block.Append("BURST ").Append(burst).Append('.').Append(i.ToString("D2")).Append("  ")
                    .Append(BuildProseLine((burst * 31) + (i * 5))).Append("\r\n");
            }

            // ONE WRITE. Building the whole block first is the point - handing it over in pieces
            // would let the animation keep up between them and there would be no backlog to show.
            await session.WriteAsync(block.ToString());
            await session.FlushAsync();

            // Long enough for every owed line to be walked off, plus a breath. Anything shorter and
            // the next burst lands while the picture is still climbing out of the last one.
            await Task.Delay((SmoothScrollBurstLines * SmoothScrollSlideMilliseconds) + 1500);
        }
    }

    /// <summary>
    /// Fills the screen so that the very first paced line already has to scroll.
    /// </summary>
    /// <param name="session">
    /// The client to write to.
    /// </param>
    /// <remarks>
    /// Sent as one write with no pacing at all. This is scaffolding rather than part of what is
    /// being judged, and the point is for it to be finished before the reader has focused on the
    /// window.
    /// </remarks>
    private async Task FillScreenAsync(TelnetSession session)
    {
        var filler = new StringBuilder();
        for (int i = 0; i < 24; i++)
        {
            filler.Append("\x1b[2m").Append(BuildProseLine(i * 3)).Append("\x1b[0m\r\n");
        }

        await session.WriteAsync(filler.ToString());
        await session.FlushAsync();
        await Task.Delay(400);
    }

    /// <summary>
    /// Sends numbered lines of prose at a pace a person would read at.
    /// </summary>
    /// <param name="session">
    /// The client to write to.
    /// </param>
    /// <param name="tag">
    /// A label printed on every line, so the two halves cannot be confused.
    /// </param>
    /// <param name="lines">
    /// How many lines to send.
    /// </param>
    /// <param name="wordSeed">
    /// Where in the word pool to start, so the two halves do not read identically.
    /// </param>
    private async Task SendProseAsync(TelnetSession session, string tag, int lines, int wordSeed)
    {
        for (int i = 1; i <= lines; i++)
        {
            await session.WriteAsync(tag + " " + i.ToString("D3") + "  "
                + BuildProseLine(wordSeed + i * 5) + "\r\n");
            await session.FlushAsync();
            await Task.Delay(SmoothScrollDemoLineGap);
        }
    }

    /// <summary>
    /// Builds one line of lorem ipsum of roughly the screen's width.
    /// </summary>
    /// <param name="start">
    /// Where in the word pool to begin, so consecutive lines differ.
    /// </param>
    /// <returns>
    /// The line, without a terminator.
    /// </returns>
    /// <remarks>
    /// Deterministic rather than random: the same demonstration run twice reads the same, so a
    /// second look at something odd shows the same thing. Nothing here needs randomness, and a
    /// random generator would only make a report of "the line about X looked wrong" unrepeatable.
    /// </remarks>
    private static string BuildProseLine(int start)
    {
        var line = new StringBuilder(SmoothScrollLineWidth);
        int word = start;

        while (line.Length < SmoothScrollLineWidth - 12)
        {
            if (line.Length > 0) line.Append(' ');
            line.Append(LoremWords[((word % LoremWords.Length) + LoremWords.Length) % LoremWords.Length]);
            word++;
        }

        return line.ToString();
    }
}
