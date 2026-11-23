//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using RetroTerm.Core.Recording;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Recording;

/// <summary>
/// The film's timing rules and the APNG file it writes, with made-up times and pictures so every
/// run gives the same film.
/// </summary>
/// <remarks>
/// Each file is read back by <see cref="ApngTestReader"/>, which decodes the pixels with
/// SkiaSharp's PNG decoder rather than with anything written in this repository.
/// </remarks>
public class ScreenFilmTests : IDisposable
{
    private const int W = 7;
    private const int H = 5;

    private readonly string _path = Path.Combine(Path.GetTempPath(), "retroterm-film-" + Guid.NewGuid().ToString("N") + ".png");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    /// <summary>
    /// A picture of one colour, with one pixel of another so the rows are not all the same.
    /// </summary>
    private static byte[] Picture(byte r, byte g, byte b)
    {
        var rgba = new byte[W * H * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = 255;
        }

        // Bottom right pixel white: proves rows land in order and not mirrored.
        int last = (W * H - 1) * 4;
        rgba[last] = 255;
        rgba[last + 1] = 255;
        rgba[last + 2] = 255;
        return rgba;
    }

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void OnlyChangesBecomeFramesAndEachLastsAsLongAsItWasShown()
    {
        var red = Picture(200, 10, 10);
        var green = Picture(10, 200, 10);
        var blue = Picture(10, 10, 200);

        using (var film = new ScreenFilm(_path))
        {
            Assert.Equal(ScreenFilmOffer.First, film.Offer(red, W, H, Ms(1000)));
            Assert.Equal(ScreenFilmOffer.Unchanged, film.Offer(red, W, H, Ms(1100)));
            Assert.Equal(ScreenFilmOffer.Changed, film.Offer(green, W, H, Ms(1300)));
            Assert.Equal(ScreenFilmOffer.Unchanged, film.Offer(green, W, H, Ms(1400)));
            Assert.Equal(ScreenFilmOffer.Changed, film.Offer(blue, W, H, Ms(2000)));
            Assert.True(film.Finish(Ms(2500)));

            Assert.Equal(3, film.Pictures);
            Assert.Equal(3, film.FramesWritten);
            Assert.Equal(Ms(1500), film.Duration);
        }

        var read = ApngTestReader.Read(_path);
        Assert.Equal(W, read.Width);
        Assert.Equal(H, read.Height);
        Assert.Equal(3, read.DeclaredFrames);
        Assert.Equal(1, read.Plays);
        Assert.Equal(3, read.Frames.Count);

        Assert.Equal(300, read.Frames[0].DurationMs);
        Assert.Equal(700, read.Frames[1].DurationMs);
        Assert.Equal(500, read.Frames[2].DurationMs);

        Assert.Equal(new SKColor(200, 10, 10), read.Frames[0].Bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(10, 200, 10), read.Frames[1].Bitmap.GetPixel(3, 2));
        Assert.Equal(new SKColor(10, 10, 200), read.Frames[2].Bitmap.GetPixel(6, 0));
        Assert.Equal(new SKColor(255, 255, 255), read.Frames[2].Bitmap.GetPixel(W - 1, H - 1));

        // A viewer without APNG support shows the first frame.
        Assert.Equal(new SKColor(200, 10, 10), read.AsStill!.GetPixel(0, 0));
    }

    [Fact]
    public void AStillLongerThanOneApngFrameCanHoldIsSplitIntoSeveral()
    {
        // fcTL holds the delay in 16 bits; 70 seconds of one picture is 65535 ms plus the rest.
        using (var film = new ScreenFilm(_path))
        {
            film.Offer(Picture(1, 2, 3), W, H, Ms(0));
            film.Finish(Ms(70000));

            Assert.Equal(1, film.Pictures);
            Assert.Equal(2, film.FramesWritten);
        }

        var read = ApngTestReader.Read(_path);
        Assert.Equal(2, read.DeclaredFrames);
        Assert.Equal(65535, read.Frames[0].DurationMs);
        Assert.Equal(70000 - 65535, read.Frames[1].DurationMs);
    }

    [Fact]
    public void APictureOfAnotherSizeIsNotAddedAndSaysSo()
    {
        using var film = new ScreenFilm(_path);
        film.Offer(Picture(1, 2, 3), W, H, Ms(0));

        var bigger = new byte[(W + 1) * H * 4];
        Assert.Equal(ScreenFilmOffer.SizeChanged, film.Offer(bigger, W + 1, H, Ms(100)));

        film.Finish(Ms(200));
        Assert.Equal(1, film.Pictures);
    }

    [Fact]
    public void FinishingWithoutAnyPictureWritesNoFile()
    {
        using var film = new ScreenFilm(_path);
        Assert.False(film.Finish(Ms(10)));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void AFrameWithTheWrongNumberOfBytesIsRefused()
    {
        using var writer = new AnimatedPngWriter(_path, W, H);
        Assert.Throws<ArgumentException>(() => writer.WriteFrame(new byte[10], 100));
    }
}
