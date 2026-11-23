//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;

namespace RetroTerm.Core.Recording;

/// <summary>
/// What happened to one picture offered to a <see cref="ScreenFilm"/>.
/// </summary>
public enum ScreenFilmOffer
{
    /// <summary>
    /// The first picture: it opened the file and became the first frame.
    /// </summary>
    First,

    /// <summary>
    /// The same as the frame being shown, so nothing was added; that frame simply lasts longer.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Different from the frame being shown: that frame was written with its real duration and
    /// this picture is now the one being shown.
    /// </summary>
    Changed,

    /// <summary>
    /// A different size from the film. Nothing was added; an APNG has one picture size, so the
    /// caller ends the recording.
    /// </summary>
    SizeChanged
}

/// <summary>
/// A screen recording: pictures go in when they are sampled, and a frame is written only when the
/// picture changes, lasting exactly as long as it was on the screen.
/// </summary>
/// <remarks>
/// <para><b>Why the timing lives here and not in a timer</b></para>
/// A film made at a fixed frame rate from the wall clock cannot be tested: what lands in it
/// depends on how fast the machine running the test is (the CLAUDE.md section on waiting for the
/// clock records three tag pushes lost to exactly that). So this class knows no clock and no
/// timer. Whoever samples the screen passes the time of each sample in, and a test passes made-up
/// times and gets the same film every run.
/// <para><b>Only changes become frames</b></para>
/// A terminal screen is still most of the time. Each frame is held until a different picture
/// arrives; then it is written with duration = (time of the new picture) - (time it first
/// appeared). The film is therefore as long as the recording and no longer, and a quiet minute is
/// one frame, not six hundred.
/// <para><b>How exact the durations are</b></para>
/// A change is seen at the first sample after it happened, so each duration is right to within
/// one sampling interval. The film cannot be more exact than the samples it is given.
/// </remarks>
public sealed class ScreenFilm : IDisposable
{
    private readonly string _path;
    private AnimatedPngWriter? _writer;
    private byte[]? _held;
    private TimeSpan _heldSince;
    private TimeSpan _startedAt;
    private TimeSpan _endedAt;
    private int _changes;
    private bool _finished;

    /// <summary>
    /// Prepares a film. The file is created when the first picture arrives, because only then is
    /// the picture size known.
    /// </summary>
    /// <param name="path">
    /// Full path of the APNG to write.
    /// </param>
    public ScreenFilm(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>
    /// The file the film goes to.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Picture width, or zero before the first picture.
    /// </summary>
    public int Width => _writer?.Width ?? 0;

    /// <summary>
    /// Picture height, or zero before the first picture.
    /// </summary>
    public int Height => _writer?.Height ?? 0;

    /// <summary>
    /// How many different pictures the film holds: the first, plus one per change. Only exact
    /// once <see cref="Finish"/> has run, because the last picture is still being held until then.
    /// </summary>
    public int Pictures => _writer == null ? 0 : _changes + 1;

    /// <summary>
    /// How many frames the file holds. Equal to <see cref="Pictures"/> unless a picture lasted
    /// longer than <see cref="AnimatedPngWriter.MaxFrameDurationMs"/> and had to be split.
    /// </summary>
    public int FramesWritten => _writer?.FramesWritten ?? 0;

    /// <summary>
    /// The time from the first picture to <see cref="Finish"/>, once finished.
    /// </summary>
    public TimeSpan Duration => _endedAt - _startedAt;

    /// <summary>
    /// Offers one sampled picture.
    /// </summary>
    /// <param name="rgba">
    /// The picture, four bytes per pixel (red, green, blue, alpha, not premultiplied), rows from
    /// the top.
    /// </param>
    /// <param name="width">
    /// Its width in pixels.
    /// </param>
    /// <param name="height">
    /// Its height in pixels.
    /// </param>
    /// <param name="now">
    /// When it was sampled, on any clock that only goes forward.
    /// </param>
    /// <returns>
    /// What was done with it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown after <see cref="Finish"/>.
    /// </exception>
    public ScreenFilmOffer Offer(ReadOnlySpan<byte> rgba, int width, int height, TimeSpan now)
    {
        if (_finished) throw new InvalidOperationException("the film is already finished");

        if (_writer == null)
        {
            _writer = new AnimatedPngWriter(_path, width, height);
            _held = rgba.ToArray();
            _heldSince = now;
            _startedAt = now;
            return ScreenFilmOffer.First;
        }

        if (width != _writer.Width || height != _writer.Height)
        {
            return ScreenFilmOffer.SizeChanged;
        }

        if (rgba.SequenceEqual(_held))
        {
            return ScreenFilmOffer.Unchanged;
        }

        _writer.WriteFrame(_held, ToMs(now - _heldSince));
        rgba.CopyTo(_held);
        _heldSince = now;
        _changes++;
        return ScreenFilmOffer.Changed;
    }

    /// <summary>
    /// Writes the picture still being shown, with its duration up to <paramref name="now"/>, and
    /// closes the file. Calling it again does nothing.
    /// </summary>
    /// <param name="now">
    /// When the recording stopped, on the same clock as the pictures.
    /// </param>
    /// <returns>
    /// True when a file was written; false when no picture ever arrived, in which case no file
    /// was created.
    /// </returns>
    public bool Finish(TimeSpan now)
    {
        if (_finished) return _writer != null;
        _finished = true;

        if (_writer == null) return false;

        _writer.WriteFrame(_held, ToMs(now - _heldSince));
        _writer.Finish();
        _endedAt = now;
        _held = null;
        return true;
    }

    /// <summary>
    /// Closes the file without finishing it, if <see cref="Finish"/> was never called.
    /// </summary>
    public void Dispose()
    {
        _writer?.Dispose();
        _finished = true;
        _held = null;
    }

    /// <summary>
    /// A duration in whole milliseconds, rounded to the nearest.
    /// </summary>
    /// <param name="span">
    /// The duration.
    /// </param>
    /// <returns>
    /// Milliseconds; the writer turns zero into one.
    /// </returns>
    private static int ToMs(TimeSpan span)
    {
        double ms = Math.Round(span.TotalMilliseconds);
        if (ms > int.MaxValue) return int.MaxValue;
        return ms < 0 ? 0 : (int)ms;
    }
}
