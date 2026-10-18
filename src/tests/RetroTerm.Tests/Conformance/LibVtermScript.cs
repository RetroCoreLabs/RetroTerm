using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetroTerm.Tests.Conformance;

/// <summary>
/// Parser for libvterm's <c>t/*.test</c> script format.
///
/// WHY THIS EXISTS. Our terminal tests were all written by us, against our own reading of the
/// specs — so a shared misreading is invisible. libvterm's corpus is an INDEPENDENT description
/// of what a VT/ECMA-48 terminal does, written by Paul Evans and refined by the neovim and vim
/// projects over a decade of real bug reports. Eight of the files (<c>90vttest_*</c>) are DEC's
/// own vttest screens converted from "a human looks at it" into machine-checkable assertions,
/// which is the only way that famous suite can be run unattended.
///
/// Source: https://github.com/neovim/libvterm (MIT, Copyright (c) 2008 Paul Evans).
/// The corpus is vendored under <c>Conformance\libvterm\</c> with its licence, so the tests
/// never need the network.
///
/// THE FORMAT (from libvterm's own <c>t/run-test.pl</c>):
/// <code>
///   INIT                       create the terminal
///   RESET                      soft reset
///   RESIZE rows,cols           resize
///   WANTSTATE p / WANTSCREEN a which callback layer the ORIGINAL harness listens to
///   PUSH "\e[2;2H"             feed bytes to the terminal
///   PUSH "\n"x24               ...with a Perl repeat count
///   PUSH "e" . "\xCC\x81" x 10 ...and Perl concatenation
///     ?cursor = 1,1            an assertion about the resulting state (indented, by convention)
///     movecursor 1,1           an expected CALLBACK — see the note on unsupported lines below
///   $REP 22: PUSH "..."        repeat the rest of the line N times
///   $SEQ 10 16: PUSH "\e[\#H"  run the rest of the line once per value, substituted for \#
///   !text                      a section heading
///   #text                      a comment
/// </code>
///
/// WHAT WE DELIBERATELY DO NOT RUN. libvterm emits callbacks (<c>putglyph</c>, <c>damage</c>,
/// <c>erase</c>, <c>scrollrect</c>, <c>output</c>, <c>settermprop</c>…) and much of the corpus
/// asserts on the exact callback stream. RetroTerm has no such stream — it writes a buffer and
/// raises one Invalidated event — so those lines describe an interface we do not have, not a
/// behaviour we get wrong. They are COUNTED AND REPORTED rather than silently dropped, because a
/// runner that quietly skips two thirds of a corpus reads like full coverage.
/// </summary>
public static class LibVtermScript
{
    /// <summary>
    /// One executable line, already expanded from $REP / $SEQ.
    /// </summary>
    public readonly struct Step
    {
        /// <summary>
        /// Line number in the source file, for failure messages.
        /// </summary>
        public readonly int LineNumber;

        /// <summary>
        /// The verb: PUSH, RESET, RESIZE, ?cursor, movecursor, …
        /// </summary>
        public readonly string Verb;

        /// <summary>
        /// Everything after the verb, trimmed. Empty when there is nothing.
        /// </summary>
        public readonly string Argument;

        public Step(int lineNumber, string verb, string argument)
        {
            LineNumber = lineNumber;
            Verb = verb;
            Argument = argument;
        }
    }

    /// <summary>
    /// Reads a .test file into a flat list of steps, with $REP and $SEQ already expanded so the
    /// runner never has to think about control flow.
    /// </summary>
    public static List<Step> Parse(string[] lines)
    {
        if (lines == null) throw new ArgumentNullException(nameof(lines));

        var steps = new List<Step>(lines.Length * 2);

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i].Trim();
            if (raw.Length == 0) continue;
            if (raw[0] == '!' || raw[0] == '#') continue;   // heading or comment

            int lineNumber = i + 1;

            if (raw[0] == '$')
            {
                ExpandControlLine(raw, lineNumber, steps);
                continue;
            }

            AddStep(raw, lineNumber, steps);
        }

        return steps;
    }

    /// <summary>
    /// Handles the two repetition forms, both of which end in a colon then the line text.
    /// </summary>
    private static void ExpandControlLine(string raw, int lineNumber, List<Step> steps)
    {
        int colon = raw.IndexOf(':');
        if (colon < 0) return;                                  // malformed; nothing to run

        string head = raw.Substring(1, colon - 1).Trim();        // e.g. "REP 22" or "SEQ 10 16"
        string body = raw.Substring(colon + 1).Trim();

        // RemoveEmptyEntries matters: the corpus aligns these lines by eye, so "$SEQ  2  7:" has
        // DOUBLE spaces. Splitting on a single space produced empty entries, low parsed as 0 and
        // high as 2 — so that line silently checked rows 0, 1 and 2 instead of 2 through 7, and
        // five assertions never ran while two ran against the wrong rows.
        string[] parts = head.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        if (string.Equals(parts[0], "REP", StringComparison.Ordinal) && parts.Length >= 2)
        {
            int count = ParseInt(parts[1], 0);
            for (int r = 0; r < count; r++)
            {
                AddStep(body, lineNumber, steps);
            }
            return;
        }

        if (string.Equals(parts[0], "SEQ", StringComparison.Ordinal) && parts.Length >= 3)
        {
            int low = ParseInt(parts[1], 0);
            int high = ParseInt(parts[2], -1);
            for (int v = low; v <= high; v++)
            {
                // \# is the placeholder. Substituting BEFORE escape decoding is deliberate:
                // the number lands inside the quoted string as ordinary text.
                string expanded = body.Replace("\\#", v.ToString(CultureInfo.InvariantCulture));
                AddStep(expanded, lineNumber, steps);
            }
        }
    }

    private static void AddStep(string raw, int lineNumber, List<Step> steps)
    {
        int space = raw.IndexOf(' ');
        string verb = space < 0 ? raw : raw.Substring(0, space);
        string arg = space < 0 ? string.Empty : raw.Substring(space + 1).Trim();
        steps.Add(new Step(lineNumber, verb, arg));
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    // ─────────────────────────────────────────────────────────────
    // String arguments
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes a PUSH argument into the raw bytes to feed the terminal.
    ///
    /// The grammar is a slice of Perl: quoted strings joined by <c>.</c>, each optionally followed
    /// by <c>x N</c> to repeat it. Escapes are <c>\e \n \r \t \b \a \0 \\ \" \$</c> and
    /// <c>\xHH</c>. Everything is byte-oriented — <c>\xCC\x81</c> is two bytes of UTF-8, NOT two
    /// characters, so the terminal's own UTF-8 decoder is the thing under test.
    /// </summary>
    public static byte[] DecodeStringArgument(string argument)
    {
        if (argument == null) throw new ArgumentNullException(nameof(argument));

        var output = new List<byte>(argument.Length);
        int i = 0;

        while (i < argument.Length)
        {
            // Skip separators and whitespace between terms.
            char c = argument[i];
            if (c == ' ' || c == '.' || c == '\t') { i++; continue; }

            if (c != '"')
            {
                // Trailing junk (a comment, a stray token). Nothing more to decode.
                break;
            }

            int termStart = output.Count;
            i++;                                                 // step over the opening quote

            while (i < argument.Length && argument[i] != '"')
            {
                if (argument[i] == '\\' && i + 1 < argument.Length)
                {
                    i = DecodeEscape(argument, i, output);
                }
                else
                {
                    // Source files are ASCII; anything non-ASCII would already be written as \xHH.
                    output.Add((byte)argument[i]);
                    i++;
                }
            }

            if (i < argument.Length) i++;                         // step over the closing quote

            // Optional Perl repeat: `x 10` or `x10`, possibly after spaces.
            int save = i;
            while (i < argument.Length && argument[i] == ' ') i++;
            if (i < argument.Length && (argument[i] == 'x' || argument[i] == 'X'))
            {
                i++;
                while (i < argument.Length && argument[i] == ' ') i++;

                int digitsStart = i;
                while (i < argument.Length && argument[i] >= '0' && argument[i] <= '9') i++;

                if (i > digitsStart)
                {
                    int count = ParseInt(argument.Substring(digitsStart, i - digitsStart), 1);
                    RepeatTail(output, termStart, count);
                }
                else
                {
                    i = save;                                    // an 'x' that was not a repeat
                }
            }
            else
            {
                i = save;
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Repeats the bytes added since <paramref name="from"/> to a total of N copies.
    /// </summary>
    private static void RepeatTail(List<byte> output, int from, int count)
    {
        int length = output.Count - from;
        if (length <= 0) return;

        if (count <= 0)
        {
            output.RemoveRange(from, length);                    // `x 0` means the empty string
            return;
        }

        // Snapshot first: appending to `output` while reading it would read the copies too.
        var unit = new byte[length];
        output.CopyTo(from, unit, 0, length);

        for (int copy = 1; copy < count; copy++)
        {
            for (int b = 0; b < length; b++)
            {
                output.Add(unit[b]);
            }
        }
    }

    /// <summary>
    /// Decodes one backslash escape at <paramref name="i"/>; returns the next index.
    /// </summary>
    private static int DecodeEscape(string s, int i, List<byte> output)
    {
        char e = s[i + 1];
        switch (e)
        {
            case 'e': output.Add(0x1B); return i + 2;
            case 'n': output.Add(0x0A); return i + 2;
            case 'r': output.Add(0x0D); return i + 2;
            case 't': output.Add(0x09); return i + 2;
            case 'b': output.Add(0x08); return i + 2;
            case 'a': output.Add(0x07); return i + 2;
            case 'f': output.Add(0x0C); return i + 2;
            case '0': output.Add(0x00); return i + 2;
            case 'x':
            {
                // Exactly two hex digits — libvterm never writes the \x{...} long form.
                if (i + 3 < s.Length && TryHex(s[i + 2], out int hi) && TryHex(s[i + 3], out int lo))
                {
                    output.Add((byte)((hi << 4) | lo));
                    return i + 4;
                }
                output.Add((byte)'x');
                return i + 2;
            }
            default:
                // \\ \" \$ \# and anything else: the character itself.
                output.Add((byte)e);
                return i + 2;
        }
    }

    private static bool TryHex(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }

    /// <summary>
    /// Strips the surrounding quotes from an EXPECTED value and decodes its escapes into text.
    /// Used by ?screen_row and ?screen_chars, whose expected values are plain quoted strings.
    /// </summary>
    public static string DecodeExpectedText(string argument)
    {
        byte[] bytes = DecodeStringArgument(argument);
        return Encoding.UTF8.GetString(bytes);
    }
}
