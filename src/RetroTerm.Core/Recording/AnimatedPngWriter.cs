//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.IO.Compression;

namespace RetroTerm.Core.Recording;

/// <summary>
/// Writes an animated PNG (APNG) file one frame at a time, straight to disk.
/// </summary>
/// <remarks>
/// <para><b>Why APNG and not MP4, 23 November 2026</b></para>
/// docs\PLAN.md asked for MP4 if it could be had without a big dependency. It cannot: MP4 needs an
/// H.264 (or similar) video encoder, and there is none in .NET. The ways to get one are ffmpeg or
/// an operating system encoder (Media Foundation on Windows, AVFoundation on macOS, nothing
/// dependable on Linux) - a native program or library per platform to ship and keep working, and
/// ffmpeg's builds with an H.264 encoder are GPL. An animated PNG needs only what .NET already
/// has: deflate compression (System.IO.Compression) and two small checksums written below.
/// It is lossless, so terminal text stays sharp, every frame carries its own duration, and
/// Chrome, Edge, Firefox and Safari all play it. GIF was the other candidate and was rejected:
/// 256 colours per frame means a colour-reduction step that changes the picture.
/// <para><b>The format, as written here</b></para>
/// From the APNG specification (W3C PNG Third Edition, sections on acTL, fcTL and fdAT) and
/// PNG itself (RFC 2083 / ISO 15948):
/// signature, IHDR (8-bit RGBA, no interlace), acTL (frame count, play count), then for each
/// frame an fcTL with its duration, followed by the pixel data - IDAT for the first frame, which
/// makes it also the still picture a viewer without APNG support shows, and fdAT for the rest.
/// fcTL and fdAT share one sequence number that counts up from zero. Every frame covers the whole
/// picture (offset 0,0, full size, dispose none, blend source), so each frame stands on its own.
/// <para><b>The frame count is written at the end</b></para>
/// acTL comes before the frames, but the count is only known when the recording stops. A
/// placeholder is written and the chunk is rewritten in place by <see cref="Finish"/>, which is
/// why the output must be a seekable file.
/// <para><b>Played once</b></para>
/// The play count is 1, so a viewer stops on the last frame, as a film does, instead of looping.
/// </remarks>
public sealed class AnimatedPngWriter : IDisposable
{
    /// <summary>
    /// The longest one frame can last in a single fcTL: the delay is a 16-bit count of
    /// milliseconds here (delay_den = 1000). A longer still is written as several frames.
    /// </summary>
    public const int MaxFrameDurationMs = ushort.MaxValue;

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly uint[] CrcTable = BuildCrcTable();

    private readonly FileStream _file;
    private readonly int _width;
    private readonly int _height;
    private readonly long _actlPosition;
    private uint _sequence;
    private int _frames;
    private bool _finished;

    /// <summary>
    /// Creates the file and writes everything that comes before the first frame.
    /// </summary>
    /// <param name="path">
    /// Full path of the file. An existing file is replaced.
    /// </param>
    /// <param name="width">
    /// Picture width in pixels; every frame must have it.
    /// </param>
    /// <param name="height">
    /// Picture height in pixels; every frame must have it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a size is zero or negative.
    /// </exception>
    public AnimatedPngWriter(string path, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _width = width;
        _height = height;
        _file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

        _file.Write(Signature, 0, Signature.Length);

        Span<byte> ihdr = stackalloc byte[13];
        WriteUInt32(ihdr, 0, (uint)width);
        WriteUInt32(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bits per sample
        ihdr[9] = 6;   // colour type 6: red, green, blue, alpha
        ihdr[10] = 0;  // compression method 0: deflate
        ihdr[11] = 0;  // filter method 0: the five adaptive filters
        ihdr[12] = 0;  // no interlace
        WriteChunk("IHDR", ihdr);

        _actlPosition = _file.Position;
        WriteActl(0);
    }

    /// <summary>
    /// How many frames are in the file so far. A still longer than
    /// <see cref="MaxFrameDurationMs"/> counts as more than one.
    /// </summary>
    public int FramesWritten => _frames;

    /// <summary>
    /// Picture width in pixels.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Picture height in pixels.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Appends one frame.
    /// </summary>
    /// <param name="rgba">
    /// The pixels, row after row from the top, four bytes each in the order red, green, blue,
    /// alpha, not premultiplied. Exactly width times height times four bytes.
    /// </param>
    /// <param name="durationMs">
    /// How long the frame is shown, in milliseconds. Zero or less is written as one millisecond,
    /// because zero means "as fast as possible" in APNG, which is not what a measured frame is.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the pixel count does not match the picture size.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown after <see cref="Finish"/>.
    /// </exception>
    public void WriteFrame(ReadOnlySpan<byte> rgba, int durationMs)
    {
        if (_finished) throw new InvalidOperationException("the film is already finished");
        if (rgba.Length != _width * _height * 4)
        {
            throw new ArgumentException(
                $"a frame of {_width} by {_height} needs {_width * _height * 4} bytes, got {rgba.Length}",
                nameof(rgba));
        }

        if (durationMs <= 0) durationMs = 1;

        // The pixel data is compressed once and repeated for each piece of a long still.
        byte[] compressed = Compress(rgba);

        int remaining = durationMs;
        while (remaining > 0)
        {
            int piece = remaining > MaxFrameDurationMs ? MaxFrameDurationMs : remaining;
            remaining -= piece;
            WriteOneFrame(compressed, piece);
        }
    }

    /// <summary>
    /// Writes the real frame count into the file, ends it and closes it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no frame was written: an APNG must hold at least one.
    /// </exception>
    public void Finish()
    {
        if (_finished) return;
        if (_frames == 0) throw new InvalidOperationException("a film needs at least one frame");

        WriteChunk("IEND", ReadOnlySpan<byte>.Empty);

        long end = _file.Position;
        _file.Position = _actlPosition;
        WriteActl((uint)_frames);
        _file.Position = end;

        _file.Flush();
        _file.Dispose();
        _finished = true;
    }

    /// <summary>
    /// Closes the file. A film that was never finished is left incomplete on disk - the frame
    /// count still reads zero - so callers finish first.
    /// </summary>
    public void Dispose()
    {
        if (!_finished)
        {
            _file.Dispose();
            _finished = true;
        }
    }

    /// <summary>
    /// Writes the fcTL and the pixel chunk of one frame.
    /// </summary>
    /// <param name="compressed">
    /// The zlib stream of the frame's filtered rows.
    /// </param>
    /// <param name="durationMs">
    /// The frame's duration, 1 to <see cref="MaxFrameDurationMs"/>.
    /// </param>
    private void WriteOneFrame(byte[] compressed, int durationMs)
    {
        Span<byte> fctl = stackalloc byte[26];
        WriteUInt32(fctl, 0, _sequence++);
        WriteUInt32(fctl, 4, (uint)_width);
        WriteUInt32(fctl, 8, (uint)_height);
        WriteUInt32(fctl, 12, 0); // x offset
        WriteUInt32(fctl, 16, 0); // y offset
        fctl[20] = (byte)(durationMs >> 8);
        fctl[21] = (byte)durationMs;
        fctl[22] = 1000 >> 8;      // delay_den = 1000: delay_num is in milliseconds
        fctl[23] = 1000 & 0xFF;
        fctl[24] = 0;              // dispose_op NONE: the next frame replaces this one entirely
        fctl[25] = 0;              // blend_op SOURCE: copy, do not mix with what was there
        WriteChunk("fcTL", fctl);

        if (_frames == 0)
        {
            WriteChunk("IDAT", compressed);
        }
        else
        {
            // fdAT is an IDAT with the sequence number in front.
            byte[] fdat = new byte[4 + compressed.Length];
            WriteUInt32(fdat, 0, _sequence++);
            Buffer.BlockCopy(compressed, 0, fdat, 4, compressed.Length);
            WriteChunk("fdAT", fdat);
        }

        _frames++;
    }

    /// <summary>
    /// Writes the acTL chunk at the current position.
    /// </summary>
    /// <param name="frames">
    /// The frame count to put in it.
    /// </param>
    private void WriteActl(uint frames)
    {
        Span<byte> actl = stackalloc byte[8];
        WriteUInt32(actl, 0, frames);
        WriteUInt32(actl, 4, 1); // play once
        WriteChunk("acTL", actl);
    }

    /// <summary>
    /// Filters and compresses one frame into a zlib stream (RFC 1950), as IDAT and fdAT carry.
    /// </summary>
    /// <param name="rgba">
    /// The frame's pixels.
    /// </param>
    /// <returns>
    /// The zlib stream.
    /// </returns>
    /// <remarks>
    /// Every row gets filter type 0 (none): the screens recorded are large flat areas with text,
    /// which deflate already finds well without a predictor. netstandard2.1 has no zlib stream,
    /// only raw deflate, so the two-byte header and the Adler-32 trailer are written here.
    /// </remarks>
    private byte[] Compress(ReadOnlySpan<byte> rgba)
    {
        int rowBytes = _width * 4;
        using var output = new MemoryStream(rgba.Length / 8 + 64);

        // CMF 0x78 = deflate with a 32 KB window; FLG 0x01 makes 0x7801 a multiple of 31, as the
        // header check requires, and says "fastest" compression, which is what is used.
        output.WriteByte(0x78);
        output.WriteByte(0x01);

        uint adlerA = 1;
        uint adlerB = 0;

        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] row = new byte[rowBytes + 1];
            for (int y = 0; y < _height; y++)
            {
                row[0] = 0; // filter type none
                rgba.Slice(y * rowBytes, rowBytes).CopyTo(row.AsSpan(1));
                deflate.Write(row, 0, row.Length);
                Adler32(row, ref adlerA, ref adlerB);
            }
        }

        uint adler = (adlerB << 16) | adlerA;
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);

        return output.ToArray();
    }

    /// <summary>
    /// Adds bytes to a running Adler-32 checksum (RFC 1950 section 8.2).
    /// </summary>
    /// <param name="data">
    /// The bytes.
    /// </param>
    /// <param name="a">
    /// The running sum of bytes, modulo 65521.
    /// </param>
    /// <param name="b">
    /// The running sum of sums, modulo 65521.
    /// </param>
    private static void Adler32(byte[] data, ref uint a, ref uint b)
    {
        const uint Mod = 65521;

        // 5552 is the most bytes that can be summed before b could overflow 32 bits (RFC 1950's
        // reference code calls it NMAX), so the modulo is taken once per block, not per byte.
        int i = 0;
        while (i < data.Length)
        {
            int end = Math.Min(data.Length, i + 5552);
            for (; i < end; i++)
            {
                a += data[i];
                b += a;
            }
            a %= Mod;
            b %= Mod;
        }
    }

    /// <summary>
    /// Writes one PNG chunk: length, type, data, CRC-32 of type and data.
    /// </summary>
    /// <param name="type">
    /// The four-letter chunk type.
    /// </param>
    /// <param name="data">
    /// The chunk's data.
    /// </param>
    private void WriteChunk(string type, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        WriteUInt32(head, 0, (uint)data.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];

        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, head.Slice(4, 4));
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> tail = stackalloc byte[4];
        WriteUInt32(tail, 0, crc);

        _file.Write(head);
        _file.Write(data);
        _file.Write(tail);
    }

    /// <summary>
    /// Runs bytes through the PNG CRC-32 (ISO 3309 polynomial, RFC 2083 section 15).
    /// </summary>
    /// <param name="crc">
    /// The running register.
    /// </param>
    /// <param name="data">
    /// The bytes.
    /// </param>
    /// <returns>
    /// The register after the bytes.
    /// </returns>
    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    /// <summary>
    /// Builds the byte-at-a-time CRC table, as in the PNG specification's sample code.
    /// </summary>
    /// <returns>
    /// 256 entries.
    /// </returns>
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    /// <summary>
    /// Writes a 32-bit number most significant byte first, as PNG stores every number.
    /// </summary>
    /// <param name="target">
    /// Where to write.
    /// </param>
    /// <param name="offset">
    /// Byte offset.
    /// </param>
    /// <param name="value">
    /// The number.
    /// </param>
    private static void WriteUInt32(Span<byte> target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
