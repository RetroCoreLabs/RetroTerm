using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;

namespace RetroTerm.Tests.Conformance;

/// <summary>
/// Drives a libvterm .test script against a real RetroTerm emulator and reports what happened.
///
/// This deliberately does NOT throw on the first mismatch. The corpus is an outside opinion about
/// correct terminal behaviour, and the useful output is "here are the 9 places we disagree",
/// not "we disagree somewhere". <see cref="LibVtermConformanceTests"/> compares the counts
/// against a checked-in baseline so a regression fails and a fix also fails, loudly, asking for
/// the number to be updated.
/// </summary>
public sealed class LibVtermConformanceRunner
{
    /// <summary>
    /// libvterm's harness default screen. INIT with no size means this.
    /// </summary>
    private const int DefaultRows = 25;
    private const int DefaultCols = 80;

    /// <summary>
    /// Assertions that matched.
    /// </summary>
    public int Passed { get; private set; }

    /// <summary>
    /// One message per assertion that did not match, with file line numbers.
    /// </summary>
    public List<string> Failures { get; } = new();

    /// <summary>
    /// Directive -> how many times it appeared and was not executed. These are the callback-stream
    /// expectations and input-side commands described in <see cref="LibVtermScript"/>; kept so the
    /// tests can state exactly how much of the corpus is NOT being checked.
    /// </summary>
    public Dictionary<string, int> Unsupported { get; } = new(StringComparer.Ordinal);

    private TerminalEmulatorBase? _emulator;
    private readonly string _emulatorType;

    public LibVtermConformanceRunner(string emulatorType)
    {
        _emulatorType = emulatorType;
    }

    /// <summary>
    /// Runs every step of one parsed script.
    /// </summary>
    public void Run(List<LibVtermScript.Step> steps)
    {
        if (steps == null) throw new ArgumentNullException(nameof(steps));

        _emulator = EmulatorFactory.CreateEmulator(_emulatorType, DefaultCols, DefaultRows, 100);

        for (int i = 0; i < steps.Count; i++)
        {
            Execute(steps[i]);
        }
    }

    private void Execute(in LibVtermScript.Step step)
    {
        var emulator = _emulator!;

        switch (step.Verb)
        {
            case "INIT":
                // A fresh terminal at the harness default size.
                _emulator = EmulatorFactory.CreateEmulator(_emulatorType, DefaultCols, DefaultRows, 100);
                return;

            case "RESET":
                emulator.Reset();
                return;

            case "RESIZE":
            {
                // "RESIZE rows,cols" — note our own Resize takes (width, height).
                if (TryParsePair(step.Argument, out int rows, out int cols))
                {
                    emulator.Resize(cols, rows);
                }
                return;
            }

            case "PUSH":
                emulator.ProcessData(LibVtermScript.DecodeStringArgument(step.Argument));
                return;

            // Harness bookkeeping in the ORIGINAL runner: which callback layer it listened to.
            // We read the buffer directly, so there is nothing to switch on and nothing skipped.
            case "WANTSTATE":
            case "WANTSCREEN":
            case "WANTPARSER":
            case "WANTENCODING":
            case "DAMAGEFLUSH":
            case "DAMAGEMERGE":
                return;

            case "?cursor":
                CheckCursor(step);
                return;

            case "?screen_row":
                CheckScreenRow(step);
                return;

            case "?screen_chars":
                CheckScreenChars(step);
                return;

            case "?screen_text":
                CheckScreenText(step);
                return;

            case "?screen_eol":
                CheckScreenEol(step);
                return;

            case "?lineinfo":
                CheckLineInfo(step);
                return;

            default:
                // Everything else: an expected callback, or an input-side command whose observable
                // effect is bytes sent BACK to the host. Counted, never silently dropped.
                Unsupported.TryGetValue(step.Verb, out int seen);
                Unsupported[step.Verb] = seen + 1;
                return;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Assertions
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ?cursor = row,col — both zero-based, same convention as ours.
    /// </summary>
    private void CheckCursor(in LibVtermScript.Step step)
    {
        string expected = ValueAfterEquals(step.Argument);
        if (!TryParsePair(expected, out int row, out int col))
        {
            Skip(step, "?cursor (unparsed)");
            return;
        }

        var cursor = _emulator!.GetCursor();
        Compare(step, $"cursor {row},{col}", $"{row},{col}", $"{cursor.Row},{cursor.Column}");
    }

    /// <summary>
    /// ?screen_row N = "text" — the row's characters with trailing blanks removed.
    /// </summary>
    private void CheckScreenRow(in LibVtermScript.Step step)
    {
        int equals = step.Argument.IndexOf('=');
        if (equals < 0) { Skip(step, "?screen_row (no =)"); return; }

        if (!int.TryParse(step.Argument.Substring(0, equals).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int row))
        {
            Skip(step, "?screen_row (unparsed row)");
            return;
        }

        string expected = DecodeExpectedRowValue(step.Argument.Substring(equals + 1).Trim());
        string actual = ReadRow(row, 0, _emulator!.Width);
        Compare(step, $"screen_row {row}", expected, actual);
    }

    /// <summary>
    /// ?screen_chars r0,c0,r1,c1 = "text" — a rectangle, rows joined end to end.
    /// </summary>
    private void CheckScreenChars(in LibVtermScript.Step step)
    {
        int equals = step.Argument.IndexOf('=');
        if (equals < 0) { Skip(step, "?screen_chars (no =)"); return; }

        if (!TryParseRect(step.Argument.Substring(0, equals), out int r0, out int c0, out int r1, out int c1))
        {
            Skip(step, "?screen_chars (unparsed rect)");
            return;
        }

        string expected = LibVtermScript.DecodeExpectedText(step.Argument.Substring(equals + 1).Trim());
        string actual = ReadRect(r0, c0, r1, c1);
        Compare(step, $"screen_chars {r0},{c0},{r1},{c1}", expected, actual);
    }

    /// <summary>
    /// ?screen_text r0,c0,r1,c1 = 0x41,0x42 — the same rectangle as UTF-8 bytes.
    /// </summary>
    private void CheckScreenText(in LibVtermScript.Step step)
    {
        int equals = step.Argument.IndexOf('=');
        if (equals < 0) { Skip(step, "?screen_text (no =)"); return; }

        if (!TryParseRect(step.Argument.Substring(0, equals), out int r0, out int c0, out int r1, out int c1))
        {
            Skip(step, "?screen_text (unparsed rect)");
            return;
        }

        string expected = NormaliseHexList(step.Argument.Substring(equals + 1).Trim());
        string actual = ToHexList(Encoding.UTF8.GetBytes(ReadRect(r0, c0, r1, c1)));
        Compare(step, $"screen_text {r0},{c0},{r1},{c1}", expected, actual);
    }

    /// <summary>
    /// ?screen_eol r,c = 0|1 — is everything from this cell to the right blank?
    /// </summary>
    private void CheckScreenEol(in LibVtermScript.Step step)
    {
        int equals = step.Argument.IndexOf('=');
        if (equals < 0) { Skip(step, "?screen_eol (no =)"); return; }

        if (!TryParsePair(step.Argument.Substring(0, equals), out int row, out int col))
        {
            Skip(step, "?screen_eol (unparsed)");
            return;
        }

        string expected = step.Argument.Substring(equals + 1).Trim();
        bool blank = ReadRow(row, col, _emulator!.Width).Length == 0;
        Compare(step, $"screen_eol {row},{col}", expected, blank ? "1" : "0");
    }

    /// <summary>
    /// ?lineinfo N = cont — row N is a CONTINUATION of row N-1, i.e. row N-1 wrapped.
    /// An empty expected value means "not a continuation".
    /// </summary>
    private void CheckLineInfo(in LibVtermScript.Step step)
    {
        int equals = step.Argument.IndexOf('=');
        if (equals < 0) { Skip(step, "?lineinfo (no =)"); return; }

        if (!int.TryParse(step.Argument.Substring(0, equals).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int row))
        {
            Skip(step, "?lineinfo (unparsed row)");
            return;
        }

        string rest = step.Argument.Substring(equals + 1).Trim();

        // The corpus only ever uses `cont` here. Anything else (a doublewidth/doubleheight form)
        // would be a new case, so refuse to guess at it.
        if (rest.Length != 0 && !string.Equals(rest, "cont", StringComparison.Ordinal))
        {
            Skip(step, "?lineinfo (unknown flag)");
            return;
        }

        bool expectedCont = rest.Length != 0;
        bool actualCont = row > 0 && _emulator!.GetBuffer().IsLineWrapped(row - 1);
        Compare(step, $"lineinfo {row}", expectedCont ? "cont" : "", actualCont ? "cont" : "");
    }

    // ─────────────────────────────────────────────────────────────
    // Screen reading
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Characters of one row from startCol (inclusive) to endCol (exclusive), right-trimmed.
    /// </summary>
    private string ReadRow(int row, int startCol, int endCol)
    {
        var buffer = _emulator!.GetBuffer();
        var text = new StringBuilder(endCol - startCol);

        int lastWritten = -1;

        for (int col = startCol; col < endCol; col++)
        {
            if (!buffer.TryGetCell(row, col, out var cell)) break;

            // The right-hand half of a wide character is not a character. libvterm reports one
            // cell per CHARACTER, so the trailer is skipped entirely rather than reported as the
            // blank it physically is.
            if (cell.IsWideTrail) continue;

            // Codepoint 0 is a cell that was never written; libvterm reports it as a space.
            uint cp = cell.Codepoint;
            text.Append(cp == 0 ? ' ' : char.ConvertFromUtf32((int)cp));

            if (cp != 0) lastWritten = text.Length;
        }

        // libvterm drops trailing cells that were NEVER WRITTEN, not trailing spaces. A shell
        // prompt "> " keeps its trailing space because that space was typed; the columns after it
        // vanish because nothing ever touched them. Trimming on the character rather than on
        // whether it was written made those two look identical and cost 10 disagreements.
        return lastWritten < 0 ? string.Empty : text.ToString(0, lastWritten);
    }

    /// <summary>
    /// The rectangle rows r0..r1-1, columns c0..c1-1, each row right-trimmed and joined.
    ///
    /// Rows are separated by a newline UNLESS the earlier row wrapped onto the next — that is
    /// libvterm's rule, and it is the whole point of tracking the continuation flag: "Hello" then
    /// CRLF then "World" reads back as "Hello\nWorld", but eighty characters that spilled over
    /// read back as one unbroken run. This runner used to concatenate rows with nothing between
    /// them and blamed the emulator for the missing newline.
    /// </summary>
    private string ReadRect(int r0, int c0, int r1, int c1)
    {
        var buffer = _emulator!.GetBuffer();
        var text = new StringBuilder();

        for (int row = r0; row < r1; row++)
        {
            if (row > r0 && !buffer.IsLineWrapped(row - 1))
            {
                text.Append('\n');
            }
            text.Append(ReadRow(row, c0, c1));
        }

        return text.ToString();
    }

    /// <summary>
    /// Decodes the expected value of a ?screen_row assertion, which comes in TWO forms:
    /// a quoted string (<c>"ABC"</c>) or a comma-separated list of CODEPOINTS
    /// (<c>0xc1,0xe9</c>) — the second is how the corpus writes anything non-ASCII, and
    /// treating it as a string silently produced an empty expectation that never matched.
    /// </summary>
    private static string DecodeExpectedRowValue(string expected)
    {
        if (expected.Length > 0 && expected[0] == '"')
        {
            return LibVtermScript.DecodeExpectedText(expected);
        }

        var text = new StringBuilder();
        string[] parts = expected.Split(',');

        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (p.Length == 0) continue;

            bool hex = p.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hex) p = p.Substring(2);

            if (!int.TryParse(p,
                    hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int codepoint))
            {
                return expected;                    // not a codepoint list after all
            }

            text.Append(char.ConvertFromUtf32(codepoint));
        }

        return text.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // Small helpers
    // ─────────────────────────────────────────────────────────────

    private void Compare(in LibVtermScript.Step step, string what, string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Passed++;
            return;
        }

        // Same text, different Unicode normalisation. libvterm keeps a combining mark alongside
        // its base character; we compose the pair into the single precomposed codepoint, because
        // a cell holds one codepoint. "é" as U+0065 U+0301 and as U+00E9 are the same character
        // and must not be reported as a disagreement — the screen is identical either way.
        if (expected.Length > 0 && actual.Length > 0
            && string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal))
        {
            Passed++;
            return;
        }

        Failures.Add($"line {step.LineNumber}: {what} — expected [{expected}], got [{actual}]");
    }

    private void Skip(in LibVtermScript.Step step, string reason)
    {
        Unsupported.TryGetValue(reason, out int seen);
        Unsupported[reason] = seen + 1;
    }

    /// <summary>
    /// Normalised form for comparison. Hex lists are decoded to text first, so a byte list
    /// spelling out a decomposed sequence compares equal to our composed one.
    /// </summary>
    private static string Normalize(string value)
    {
        string text = value;

        // "65,cc,81,31" is UTF-8 bytes written as hex. Decode it before normalising, or the
        // comparison is between two spellings of a number rather than between two strings.
        if (LooksLikeHexList(value))
        {
            string[] parts = value.Split(',');
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                bytes[i] = byte.Parse(parts[i].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            text = Encoding.UTF8.GetString(bytes);
        }

        try
        {
            return text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static bool LooksLikeHexList(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length < 2) return false;

        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (p.Length == 0 || p.Length > 2) return false;
            if (!byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;
        }

        return true;
    }

    private static string ValueAfterEquals(string argument)
    {
        int equals = argument.IndexOf('=');
        return equals < 0 ? argument.Trim() : argument.Substring(equals + 1).Trim();
    }

    private static bool TryParsePair(string text, out int a, out int b)
    {
        a = 0;
        b = 0;
        if (text == null) return false;

        string trimmed = text.Trim();
        int comma = trimmed.IndexOf(',');
        if (comma < 0) return false;

        return int.TryParse(trimmed.Substring(0, comma).Trim(),
                   NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
            && int.TryParse(trimmed.Substring(comma + 1).Trim(),
                   NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
    }

    private static bool TryParseRect(string text, out int r0, out int c0, out int r1, out int c1)
    {
        r0 = c0 = r1 = c1 = 0;
        string[] parts = text.Trim().Split(',');
        if (parts.Length != 4) return false;

        return int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r0)
            && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out c0)
            && int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r1)
            && int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out c1);
    }

    /// <summary>
    /// Rewrites "0x41,0x42" into a canonical form so formatting differences do not fail.
    /// </summary>
    private static string NormaliseHexList(string text)
    {
        var result = new StringBuilder();
        string[] parts = text.Split(',');

        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (p.Length == 0) continue;
            if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) p = p.Substring(2);
            if (!int.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return text.Trim();

            if (result.Length > 0) result.Append(',');
            result.Append(v.ToString("x2", CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }

    private static string ToHexList(byte[] bytes)
    {
        var result = new StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) result.Append(',');
            result.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }
}
