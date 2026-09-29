using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Session;

/// <summary>
/// How a wait pattern is interpreted.
/// </summary>
public enum ScreenMatchType
{
    /// <summary>
    /// Plain text, ordinal comparison.
    /// </summary>
    Literal,

    /// <summary>
    /// .NET regular expression.
    /// </summary>
    Regex
}

/// <summary>
/// Where on the rendered output a wait pattern is searched.
/// </summary>
public enum ScreenMatchWhere
{
    /// <summary>
    /// Only the last non-blank row(s) of the screen (see ScreenWaitOptions.TailRows).
    /// This is the prompt-matching mode: a prompt echoed in the MIDDLE of a listing
    /// does not end the wait, only a prompt at the current end of output does.
    /// </summary>
    ScreenTail,

    /// <summary>
    /// Anywhere on the visible screen.
    /// </summary>
    AnywhereOnScreen,

    /// <summary>
    /// The visible screen first, then the scrollback (newest line first).
    /// </summary>
    ScreenAndScrollback
}

/// <summary>
/// Parameters for <see cref="TerminalSession.WaitForScreenAsync"/>.
/// </summary>
public sealed class ScreenWaitOptions
{
    /// <summary>
    /// The pattern to wait for. null (with IdleMs > 0) means "wait until the screen
    /// has stopped changing" instead of waiting for specific text.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// How Pattern is interpreted. Default: literal text.
    /// </summary>
    public ScreenMatchType MatchType { get; set; } = ScreenMatchType.Literal;

    /// <summary>
    /// Where Pattern is searched. Default: the screen tail (prompt matching).
    /// </summary>
    public ScreenMatchWhere Where { get; set; } = ScreenMatchWhere.ScreenTail;

    /// <summary>
    /// How many trailing non-blank rows form the "screen tail" for ScreenTail matching.
    /// Default 1: the row the prompt (and cursor) is on.
    /// </summary>
    public int TailRows { get; set; } = 1;

    /// <summary>
    /// Give up after this long. The result still carries the screen at that moment and
    /// the elapsed time — a timeout must return what the machine said (rule 4 of the MCP
    /// terminal-control rules in docs\MCP-AND-SCRIPTING.md).
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// With Pattern == null: how long the screen must stay unchanged to count as idle.
    /// Ignored when Pattern is set.
    /// </summary>
    public int IdleMs { get; set; }
}

/// <summary>
/// Outcome of <see cref="TerminalSession.WaitForScreenAsync"/>. Always carries the
/// screen and the elapsed wait time, whatever happened — partial output on a timeout
/// is the most valuable thing the caller has, and knowing the wait took 23 s instead
/// of 200 ms is often the whole diagnosis (rules 4 and 5 of the MCP terminal-control
/// rules in docs\MCP-AND-SCRIPTING.md).
/// </summary>
public sealed class ScreenWaitResult
{
    /// <summary>
    /// True when the pattern matched (or the idle condition was reached).
    /// </summary>
    public bool Matched { get; }

    /// <summary>
    /// True when the wait gave up after TimeoutMs.
    /// </summary>
    public bool TimedOut { get; }

    /// <summary>
    /// True when the connection dropped while waiting (rule 6: say so).
    /// </summary>
    public bool Disconnected { get; }

    /// <summary>
    /// The text that matched (regex: the match value; literal: the pattern). Null for idle waits.
    /// </summary>
    public string? MatchedText { get; }

    /// <summary>
    /// The rendered screen at the moment the wait ended.
    /// </summary>
    public ScreenSnapshot Screen { get; }

    /// <summary>
    /// How long the wait actually took.
    /// </summary>
    public TimeSpan Elapsed { get; }

    public ScreenWaitResult(bool matched, bool timedOut, bool disconnected,
        string? matchedText, ScreenSnapshot screen, TimeSpan elapsed)
    {
        Matched = matched;
        TimedOut = timedOut;
        Disconnected = disconnected;
        MatchedText = matchedText;
        Screen = screen;
        Elapsed = elapsed;
    }
}

public partial class TerminalSession
{
    /// <summary>
    /// Waits until the RENDERED SCREEN satisfies the given condition — pattern text
    /// appearing (at the tail, anywhere, or including scrollback) or, with a null
    /// pattern, the screen going quiet for IdleMs.
    ///
    /// This matches against the emulated screen buffer, NOT the raw byte stream:
    /// escape sequences, cursor moves and repaints have already been applied, so
    /// "does the screen end with X-C:" has an exact answer here (the whole reason
    /// this lives in RetroTerm — matching raw bytes is guesswork, the screen is the truth;
    /// see the intro of docs\MCP-AND-SCRIPTING.md and ScreenMatchWhere.ScreenTail above).
    ///
    /// Evaluation runs on the session pump thread (race-free); the wait itself does
    /// not block the pump. Never throws on timeout or disconnect — the result says
    /// what happened and still carries the screen.
    /// </summary>
    public async Task<ScreenWaitResult> WaitForScreenAsync(ScreenWaitOptions options, CancellationToken cancellationToken = default)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (options.Pattern == null && options.IdleMs <= 0)
        {
            throw new ArgumentException("Either Pattern or IdleMs (> 0) must be set", nameof(options));
        }

        // Precompile the regex once; a bad pattern should throw here, not in the loop.
        Regex? regex = null;
        if (options.Pattern != null && options.MatchType == ScreenMatchType.Regex)
        {
            regex = new Regex(options.Pattern);
        }

        var stopwatch = Stopwatch.StartNew();

        // Signal for "something happened": display changed or connection dropped.
        // SemaphoreSlim as an async auto-reset event; the CurrentCount check keeps a
        // burst of invalidations from stacking up thousands of releases.
        using var signal = new SemaphoreSlim(0, int.MaxValue);
        long lastChangeTicks = stopwatch.ElapsedTicks;
        bool disconnected = false;

        void OnInvalidated()
        {
            Volatile.Write(ref lastChangeTicks, stopwatch.ElapsedTicks);
            if (signal.CurrentCount == 0)
            {
                try { signal.Release(); } catch (ObjectDisposedException) { /* wait already finished */ }
            }
        }

        void OnStatus(ConnectionStatus status)
        {
            if (status == ConnectionStatus.Disconnected)
            {
                Volatile.Write(ref disconnected, true);
                try { signal.Release(); } catch (ObjectDisposedException) { /* wait already finished */ }
            }
        }

        DisplayInvalidated += OnInvalidated;
        StatusChanged += OnStatus;
        try
        {
            while (true)
            {
                // Evaluate on the pump: serialized with data processing, so the check
                // sees a consistent screen and never races the emulator.
                string? matchedText = null;
                bool matched = false;
                if (options.Pattern != null)
                {
                    matched = await RunOnSessionThreadAsync(() =>
                    {
                        return TryMatch(options, regex, out matchedText);
                    }).ConfigureAwait(false);
                }

                if (matched)
                {
                    var screen = await ReadScreenAsync().ConfigureAwait(false);
                    return new ScreenWaitResult(true, false, false, matchedText, screen, stopwatch.Elapsed);
                }

                if (Volatile.Read(ref disconnected))
                {
                    var screen = await ReadScreenAsync().ConfigureAwait(false);
                    return new ScreenWaitResult(false, false, true, null, screen, stopwatch.Elapsed);
                }

                var remaining = TimeSpan.FromMilliseconds(options.TimeoutMs) - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    var screen = await ReadScreenAsync().ConfigureAwait(false);
                    return new ScreenWaitResult(false, true, false, null, screen, stopwatch.Elapsed);
                }

                var waitFor = remaining;

                if (options.Pattern == null)
                {
                    // Idle mode: matched when nothing has changed for IdleMs.
                    var sinceChange = TimeSpan.FromTicks(stopwatch.ElapsedTicks - Volatile.Read(ref lastChangeTicks));
                    var untilIdle = TimeSpan.FromMilliseconds(options.IdleMs) - sinceChange;
                    if (untilIdle <= TimeSpan.Zero)
                    {
                        var screen = await ReadScreenAsync().ConfigureAwait(false);
                        return new ScreenWaitResult(true, false, false, null, screen, stopwatch.Elapsed);
                    }
                    if (untilIdle < waitFor)
                    {
                        waitFor = untilIdle;
                    }
                }

                await signal.WaitAsync(waitFor, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            DisplayInvalidated -= OnInvalidated;
            StatusChanged -= OnStatus;
        }
    }

    /// <summary>
    /// One match check — MUST run on the pump thread. Reads the buffer directly.
    /// </summary>
    private bool TryMatch(ScreenWaitOptions options, Regex? regex, out string? matchedText)
    {
        matchedText = null;
        var buffer = Emulator.GetBuffer();

        string haystack = options.Where switch
        {
            ScreenMatchWhere.ScreenTail => ScreenReader.GetTailText(buffer, options.TailRows),
            _ => ScreenReader.GetScreenText(buffer, stripTrailingBlanks: true)
        };

        if (MatchIn(haystack, options, regex, out matchedText))
        {
            return true;
        }

        if (options.Where == ScreenMatchWhere.ScreenAndScrollback)
        {
            // Newest scrollback line first — recent output matches sooner.
            for (int i = buffer.ScrollbackLineCount - 1; i >= 0; i--)
            {
                var line = ScreenReader.GetScrollbackLineText(buffer, i);
                if (line != null && MatchIn(line, options, regex, out matchedText))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MatchIn(string haystack, ScreenWaitOptions options, Regex? regex, out string? matchedText)
    {
        matchedText = null;

        if (options.MatchType == ScreenMatchType.Regex)
        {
            var m = regex!.Match(haystack);
            if (m.Success)
            {
                // With a capture group the group is the interesting part — this is how
                // scripts extract a value off the screen into a variable (into=var).
                matchedText = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                return true;
            }
            return false;
        }

        if (haystack.IndexOf(options.Pattern!, StringComparison.Ordinal) >= 0)
        {
            matchedText = options.Pattern;
            return true;
        }
        return false;
    }
}
