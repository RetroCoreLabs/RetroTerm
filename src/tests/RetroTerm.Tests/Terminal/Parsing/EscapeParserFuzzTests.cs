using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests.Terminal.Parsing;

/// <summary>
/// Hostile input against the escape parser and against every emulator built on it.
///
/// WHY THIS EXISTS. Every other test in this repo asserts a behaviour someone thought
/// of. This one asserts that nothing someone did NOT think of can break the machine.
/// The parser is the hottest and most defect-prone code here — it is where the DLE
/// cursor-addressing class of bug lived — and a real terminal line carries whatever the
/// host happens to emit: half-written sequences when a program is killed, binary from a
/// file dumped to the console, a UTF-8 character split across two TCP reads.
///
/// The invariants are total, which is what makes fuzzing worth doing at all:
///
///   * ProcessBytes / ProcessData never throw, for any byte sequence, at any chunking
///   * the parser's collectors are bounded (32 parameters, 2 intermediates) no matter
///     how many the input supplies
///   * the cursor always sits inside the buffer
///   * the buffer never changes size on its own
///   * scrollback never exceeds the cap it was constructed with
///   * Reset() always returns the parser to Ground
///
/// SEEDS ARE FIXED. A fuzz test that cannot be re-run identically after a failure is a
/// bug report nobody can act on. Every case here derives its bytes from a stated seed,
/// so a failure names the exact input that produced it.
/// </summary>
public class EscapeParserFuzzTests
{
    /// <summary>
    /// Bounds the parser documents for itself; asserted against, never assumed.
    /// </summary>
    private const int MaxParams = 32;

    private const int MaxIntermediates = 2;

    /// <summary>
    /// The emulator types the factory can build. "ANSI" is handled by CreateEmulator but is
    /// not in AvailableEmulators, so it is named explicitly — a gap worth fuzzing rather
    /// than skipping.
    /// </summary>
    public static IEnumerable<object[]> EmulatorTypes()
    {
        var types = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < types.Length; i++)
        {
            yield return new object[] { types[i] };
        }
        yield return new object[] { "ANSI" };
    }

    /// <summary>
    /// Seeds swept by the random cases. Fixed, so any failure reproduces exactly.
    /// </summary>
    public static IEnumerable<object[]> Seeds()
    {
        int[] seeds = { 1, 7, 42, 99, 1234, 20260809 };
        for (int i = 0; i < seeds.Length; i++)
        {
            yield return new object[] { seeds[i] };
        }
    }

    public static IEnumerable<object[]> EmulatorTypesAndSeeds()
    {
        var types = EmulatorFactory.AvailableEmulators;
        int[] seeds = { 3, 17, 20260809 };
        for (int i = 0; i < types.Length; i++)
        {
            for (int j = 0; j < seeds.Length; j++)
            {
                yield return new object[] { types[i], seeds[j] };
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Parser level
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure random bytes, fed one byte at a time. Byte-at-a-time is the harshest chunking:
    /// every state transition happens on a separate call, so any state the parser keeps
    /// only within one ProcessBytes call is exposed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomBytes_OneAtATime_NeverBreakTheParser(int seed)
    {
        var parser = new EscapeSequenceParser();
        var random = new Random(seed);
        var one = new byte[1];

        for (int i = 0; i < 200_000; i++)
        {
            one[0] = (byte)random.Next(256);
            parser.ProcessBytes(one);
            AssertParserInvariants(parser, seed, i);
        }
    }

    /// <summary>
    /// Random bytes in random-sized chunks — the shape a real socket delivers. A sequence
    /// straddling a chunk boundary is the case that broke the UTF-8 path once already.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomBytes_RandomChunks_NeverBreakTheParser(int seed)
    {
        var parser = new EscapeSequenceParser();
        var random = new Random(seed);
        var buffer = new byte[512];

        for (int round = 0; round < 400; round++)
        {
            int length = random.Next(1, buffer.Length + 1);
            for (int i = 0; i < length; i++)
            {
                buffer[i] = (byte)random.Next(256);
            }
            parser.ProcessBytes(new ReadOnlySpan<byte>(buffer, 0, length));
            AssertParserInvariants(parser, seed, round);
        }
    }

    /// <summary>
    /// Bytes weighted towards the ones that MEAN something — ESC, CSI, DCS, OSC, ST,
    /// digits, semicolons, colons, final letters. Uniform random almost never produces a
    /// syntactically interesting sequence; this reaches the deep states on purpose.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void StructuredRandomBytes_ReachTheDeepStates_WithoutBreaking(int seed)
    {
        // ESC, CSI/DCS/OSC/ST introducers and terminators, parameter punctuation, common
        // final bytes, plus a few plain characters so Ground is exercised too.
        byte[] alphabet =
        {
            0x1B, 0x9B, 0x90, 0x9D, 0x9C, 0x07, 0x5B, 0x5D, 0x50,
            (byte)'0', (byte)'1', (byte)'9', (byte)';', (byte)':', (byte)'?', (byte)'>', (byte)'!',
            (byte)'H', (byte)'m', (byte)'J', (byte)'q', (byte)'p', (byte)'\\',
            (byte)' ', (byte)'A', 0x0A, 0x0D, 0x00, 0xFF, 0xC3, 0xA9
        };

        var parser = new EscapeSequenceParser();
        var random = new Random(seed);
        var buffer = new byte[256];

        for (int round = 0; round < 2_000; round++)
        {
            int length = random.Next(1, buffer.Length + 1);
            for (int i = 0; i < length; i++)
            {
                buffer[i] = alphabet[random.Next(alphabet.Length)];
            }
            parser.ProcessBytes(new ReadOnlySpan<byte>(buffer, 0, length));
            AssertParserInvariants(parser, seed, round);
        }
    }

    /// <summary>
    /// Both settings of DecodeUtf8. With it off, bytes 0xA0 and above are data rather than
    /// the start of a multi-byte character — the 8-bit TDV line. The parser must survive
    /// either way, and flipping the flag mid-stream (which happens when a session's
    /// profile changes) must not wedge it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void FlippingDecodeUtf8MidStream_DoesNotWedgeTheParser(int seed)
    {
        var parser = new EscapeSequenceParser();
        var random = new Random(seed);
        var buffer = new byte[64];

        for (int round = 0; round < 2_000; round++)
        {
            parser.DecodeUtf8 = (round & 1) == 0;
            int length = random.Next(1, buffer.Length + 1);
            for (int i = 0; i < length; i++)
            {
                // Skew high so the UTF-8 lead/continuation paths are actually reached.
                buffer[i] = (byte)(random.Next(2) == 0 ? random.Next(0x80, 0x100) : random.Next(256));
            }
            parser.ProcessBytes(new ReadOnlySpan<byte>(buffer, 0, length));
            AssertParserInvariants(parser, seed, round);
        }
    }

    /// <summary>
    /// The pathological cases by hand — the ones random input will not find in any
    /// reasonable number of rounds because they need a long, specific shape.
    /// </summary>
    [Fact]
    public void PathologicalSequences_AreSurvivedAndBounded()
    {
        var parser = new EscapeSequenceParser();

        // 1. A CSI with far more parameters than the parser can hold. It must clamp, not
        //    write past its array.
        var manyParams = new StringBuilder("\u001b[");
        for (int i = 0; i < 500; i++)
        {
            if (i > 0) manyParams.Append(';');
            manyParams.Append(i);
        }
        manyParams.Append('m');
        Feed(parser, manyParams.ToString());
        Assert.True(parser.Parameters.Length <= MaxParams);

        // 2. A parameter value far beyond int range — digit accumulation must not overflow
        //    into nonsense or throw.
        Feed(parser, "\u001b[99999999999999999999m");
        Assert.True(parser.Parameters.Length <= MaxParams);

        // 3. More intermediates than the two the parser keeps.
        Feed(parser, "\u001b[!!!!!!!!!!!!!!!!!!!!p");
        Assert.True(parser.Intermediates.Length <= MaxIntermediates);

        // 4. A DCS whose payload never terminates — 1 MB of it. The collector must not
        //    grow without bound just because the host forgot the ST.
        var hugeDcs = new StringBuilder("\u001bP");
        hugeDcs.Append('q', 1_000_000);
        Feed(parser, hugeDcs.ToString());

        // 5. Likewise an OSC with no BEL and no ST.
        var hugeOsc = new StringBuilder("\u001b]0;");
        hugeOsc.Append('t', 1_000_000);
        Feed(parser, hugeOsc.ToString());

        // 6. ESC as the very last byte of a chunk, over and over — the classic
        //    split-sequence case.
        var esc = new byte[] { 0x1B };
        var bracket = new byte[] { (byte)'[' };
        var final = new byte[] { (byte)'H' };
        for (int i = 0; i < 1000; i++)
        {
            parser.ProcessBytes(esc);
            parser.ProcessBytes(bracket);
            parser.ProcessBytes(final);
        }

        // 7. A UTF-8 character split across three single-byte calls, both ways round.
        for (int pass = 0; pass < 2; pass++)
        {
            parser.DecodeUtf8 = pass == 0;
            parser.ProcessBytes(new byte[] { 0xE4 });
            parser.ProcessBytes(new byte[] { 0xB8 });
            parser.ProcessBytes(new byte[] { 0x96 });
        }
        parser.DecodeUtf8 = true;

        // 8. A truncated UTF-8 lead followed immediately by an ESC: the continuation the
        //    parser is waiting for never arrives and a new sequence starts instead.
        parser.ProcessBytes(new byte[] { 0xE4, 0x1B, (byte)'[', (byte)'2', (byte)'J' });

        AssertParserInvariants(parser, seed: 0, round: 0);

        // Reset must always work, whatever state the abuse above left behind.
        parser.Reset();
        Assert.Equal(ParserState.Ground, parser.State);
    }

    // ─────────────────────────────────────────────────────────────
    // Emulator level
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The same hostile input through a whole emulator, for every terminal type the
    /// factory builds. A parser that survives is not enough: the handlers it calls are
    /// what move the cursor and resize the screen.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this one carries a Slow trait and its neighbours do not</b></para>
    /// Measured 26 August 2026 by ranking all 6,460 recorded durations from a trx run: three cases
    /// of THIS theory are 295 seconds of the suite's 580 - 51% of the whole thing - while the other
    /// 6,457 tests share the rest. The cost is inherent, not a defect: every case builds a whole
    /// emulator and drives 500 rounds of random bytes through it, for every terminal type the
    /// factory can build.
    ///
    /// The trait is on the METHOD, not the class, so the parser-level fuzz theories above - which
    /// are cheap - stay in the routine gate. Nothing here is skipped by default: the routine gate is
    /// <c>--filter "Category!=Slow"</c> and the full suite still runs before a commit. See
    /// <c>CLAUDE.md</c>, "Finishing a change".
    ///
    /// Do not tag anything else Slow without measuring it first. The obvious suspect - the headless
    /// Avalonia tests - was measured at 1 m 45 s and is innocent, and it is the only part of the
    /// suite that looks at pixels.
    /// </remarks>
    [Theory]
    [Trait("Category", "Slow")]
    [MemberData(nameof(EmulatorTypesAndSeeds))]
    public void RandomBytes_LeaveEveryEmulatorConsistent(string emulatorType, int seed)
    {
        const int scrollback = 200;
        var emulator = EmulatorFactory.CreateEmulator(emulatorType, 80, 24, scrollback);

        int width = emulator.Width;
        int height = emulator.Height;

        var random = new Random(seed);
        var buffer = new byte[256];

        for (int round = 0; round < 500; round++)
        {
            int length = random.Next(1, buffer.Length + 1);
            for (int i = 0; i < length; i++)
            {
                buffer[i] = (byte)random.Next(256);
            }

            emulator.ProcessData(new ReadOnlySpan<byte>(buffer, 0, length));

            AssertEmulatorInvariants(emulator, emulatorType, seed, round, width, height, scrollback);
        }
    }

    /// <summary>
    /// Structured hostile input through every emulator: sequences that are nearly valid
    /// are the ones that reach the handlers, and the handlers are where a bad parameter
    /// becomes a bad cursor position.
    /// </summary>
    [Theory]
    [MemberData(nameof(EmulatorTypes))]
    public void HostileSequences_LeaveTheCursorInsideTheBuffer(string emulatorType)
    {
        const int scrollback = 200;
        var emulator = EmulatorFactory.CreateEmulator(emulatorType, 80, 24, scrollback);
        int width = emulator.Width;
        int height = emulator.Height;

        // Every one of these is a real thing a host can send, and every one of them is a
        // plausible way to put the cursor somewhere that does not exist.
        string[] hostile =
        {
            "\u001b[999;999H",            // cursor far past the screen
            "\u001b[0;0H",                // row/column zero — one-based in the spec
            "\u001b[-5;-5H",              // negative, via the '-' that is not a parameter byte
            "\u001b[999B\u001b[999C",     // walk down and right past the edge
            "\u001b[999A\u001b[999D",     // and back past the top-left
            "\u001b[99;1r",               // scrolling region taller than the screen
            "\u001b[24;1r\u001b[1;99r",   // region set twice, second one impossible
            "\u001b[?6h\u001b[999;999H",  // origin mode plus an impossible position
            "\u001b[999S\u001b[999T",     // scroll up/down by more than the screen
            "\u001b[999L\u001b[999M",     // insert/delete more lines than exist
            "\u001b[999P\u001b[999X",     // delete/erase more characters than fit
            "\u001b[999@",                // insert more blanks than the line holds
            "\u001b[3J\u001b[2J\u001b[1J",// every erase-display form
            "\u001bM\u001bD\u001bE",      // reverse index, index, next line
            "\u001b#8",                   // DECALN — fills the screen with E
            "\u001b[999d\u001b[999G",     // absolute row / column, out of range
            "\u001b[?1049h\u001b[?1049l", // alternate screen in and out
            "\u001b[999;999;999;999;999m",// nonsense SGR
            "\u001b[38;5;999m\u001b[48;2;999;999;999m", // out-of-range colours
        };

        for (int repeat = 0; repeat < 20; repeat++)
        {
            for (int i = 0; i < hostile.Length; i++)
            {
                var bytes = Encoding.ASCII.GetBytes(hostile[i]);
                emulator.ProcessData(bytes);
                AssertEmulatorInvariants(emulator, emulatorType, seed: 0, round: i, width, height, scrollback);
            }

            // Plenty of plain text between rounds so scrolling actually happens and the
            // scrollback cap is genuinely under pressure.
            var text = Encoding.ASCII.GetBytes(new string('X', 500) + "\r\n");
            for (int i = 0; i < 10; i++)
            {
                emulator.ProcessData(text);
                AssertEmulatorInvariants(emulator, emulatorType, seed: 0, round: i, width, height, scrollback);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Invariants
    // ─────────────────────────────────────────────────────────────

    private static void AssertParserInvariants(EscapeSequenceParser parser, int seed, int round)
    {
        Assert.True(parser.Parameters.Length <= MaxParams,
            $"seed {seed} round {round}: parameter collector grew past {MaxParams}");
        Assert.True(parser.Intermediates.Length <= MaxIntermediates,
            $"seed {seed} round {round}: intermediate collector grew past {MaxIntermediates}");
        Assert.Equal(parser.Parameters.Length, parser.SubParameterFlags.Length);
        Assert.True(Enum.IsDefined(typeof(ParserState), parser.State),
            $"seed {seed} round {round}: parser reached undefined state {(int)parser.State}");
    }

    private static void AssertEmulatorInvariants(TerminalEmulatorBase emulator, string emulatorType,
        int seed, int round, int expectedWidth, int expectedHeight, int maxScrollback)
    {
        var where = $"{emulatorType} seed {seed} round {round}";

        // The screen must not resize itself. A host CAN resize via DECCOLM, but nothing in
        // these inputs asks for it, and a silent resize is how a renderer ends up indexing
        // a buffer that changed shape underneath it.
        //
        // THE 4014 IS THE EXCEPTION, and it is a real one rather than a loophole. Its four
        // character sizes are single escapes - ESC 8, ESC 9, ESC :, ESC ; and the four Digital
        // discourages - and each one re-lays the text grid. Random bytes hit them, so "the screen
        // never resizes" is simply false for this terminal. What still has to hold is that any
        // size it lands on is one of the sizes the manual gives, which catches a resize to
        // something nobody asked for just as well.
        if (emulatorType == "TEK4014")
        {
            AssertOneOfTheFourCharacterSizes(emulator, where, expectedWidth, expectedHeight);
        }
        else
        {
            Assert.Equal(expectedWidth, emulator.Width);
            Assert.Equal(expectedHeight, emulator.Height);
        }

        // Whatever the size is, the buffer and the emulator must agree about it, and the cursor
        // must be inside it. That is the invariant that actually protects the renderer, and it
        // holds for every terminal including the 4014.
        expectedWidth = emulator.Width;
        expectedHeight = emulator.Height;

        var cursor = emulator.GetCursor();
        Assert.True(cursor.Row >= 0 && cursor.Row < emulator.Height,
            $"{where}: cursor row {cursor.Row} outside 0..{emulator.Height - 1}");
        Assert.True(cursor.Column >= 0 && cursor.Column < emulator.Width,
            $"{where}: cursor column {cursor.Column} outside 0..{emulator.Width - 1}");

        var buffer = emulator.GetBuffer();
        Assert.Equal(expectedWidth, buffer.Width);
        Assert.Equal(expectedHeight, buffer.Height);
        Assert.True(buffer.ScrollbackLineCount <= maxScrollback,
            $"{where}: scrollback holds {buffer.ScrollbackLineCount} lines, cap is {maxScrollback}");
        Assert.True(buffer.ScrollbackLineCount >= 0, $"{where}: negative scrollback count");
    }

    /// <summary>
    /// Checks a 4014 is at one of the four grids its manual defines.
    /// </summary>
    /// <remarks>
    /// From the "4010/4014 Mode" chapter, aligned mode: 74 by 35, 81 by 38, 121 by 58 and 133 by
    /// 64. A size outside that set means something resized the terminal that was not a character
    /// size sequence, which is exactly what this fuzz run is looking for.
    /// </remarks>
    /// <param name="emulator">
    /// The terminal under test.
    /// </param>
    /// <param name="where">
    /// Seed and round, for the failure message.
    /// </param>
    /// <param name="startWidth">
    /// The width the terminal was built at. This harness builds every emulator at the same size
    /// rather than at each one's native geometry, so an untouched 4014 is 80 by 24 and that has to
    /// count as legal.
    /// </param>
    /// <param name="startHeight">
    /// The height the terminal was built at.
    /// </param>
    private static void AssertOneOfTheFourCharacterSizes(TerminalEmulatorBase emulator, string where,
        int startWidth, int startHeight)
    {
        int width = emulator.Width;
        int height = emulator.Height;

        bool known =
            (width == startWidth && height == startHeight) ||
            (width == 74 && height == 35) ||
            (width == 81 && height == 38) ||
            (width == 121 && height == 58) ||
            (width == 133 && height == 64);

        Assert.True(known,
            $"{where}: a 4014 is {width} by {height}, which is neither the size it started at nor "
            + "one of the four character sizes");
    }

    private static void Feed(EscapeSequenceParser parser, string text)
    {
        // Latin-1 rather than UTF-8: these strings are byte patterns, and encoding them as
        // UTF-8 would turn a deliberate 0x9B into two bytes and test something else.
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            bytes[i] = (byte)text[i];
        }
        parser.ProcessBytes(bytes);
    }
}
