//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using Xunit;

namespace RetroTerm.Tests.Recording;

/// <summary>
/// Reads an animated PNG back for the tests: checks its chunk structure against the APNG
/// specification and decodes every frame with SkiaSharp's PNG decoder.
/// </summary>
/// <remarks>
/// <para><b>Why the pixels are decoded by Skia, not by code written here</b></para>
/// A reader written next to the writer would share its misunderstandings and agree with it. The
/// chunk walk here only rearranges bytes: each frame's compressed data is put into a plain
/// one-frame PNG (IHDR, IDAT, IEND) and libpng inside SkiaSharp decompresses, checks and decodes
/// it. The whole file is also decoded by Skia as an ordinary PNG, which is how a viewer without
/// APNG support sees it - the first frame.
/// </remarks>
internal sealed class ApngTestReader
{
    /// <summary>
    /// One decoded frame.
    /// </summary>
    /// <param name="DurationMs">
    /// Its duration from fcTL, in milliseconds.
    /// </param>
    /// <param name="Bitmap">
    /// Its pixels, decoded by Skia.
    /// </param>
    internal sealed record Frame(double DurationMs, SKBitmap Bitmap);

    /// <summary>
    /// Picture width from IHDR.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Picture height from IHDR.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// The frame count acTL declares.
    /// </summary>
    public int DeclaredFrames { get; private set; }

    /// <summary>
    /// The play count acTL declares.
    /// </summary>
    public int Plays { get; private set; }

    /// <summary>
    /// The frames found, in order.
    /// </summary>
    public List<Frame> Frames { get; } = new();

    /// <summary>
    /// The file decoded as an ordinary PNG.
    /// </summary>
    public SKBitmap? AsStill { get; private set; }

    /// <summary>
    /// Reads a file, asserting on its structure as it goes.
    /// </summary>
    /// <param name="path">
    /// The file.
    /// </param>
    /// <returns>
    /// What it holds.
    /// </returns>
    public static ApngTestReader Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new ApngTestReader();
        reader.Parse(bytes);
        reader.AsStill = SKBitmap.Decode(bytes);
        Assert.NotNull(reader.AsStill);
        return reader;
    }

    private void Parse(byte[] file)
    {
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < 8; i++) Assert.Equal(signature[i], file[i]);

        byte[]? ihdr = null;
        int pos = 8;
        int expectedSequence = 0;
        double pendingDuration = -1;
        bool sawIend = false;

        while (pos < file.Length)
        {
            int length = (int)ReadUInt32(file, pos);
            string type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
            int data = pos + 8;
            Assert.True(data + length + 4 <= file.Length, "chunk " + type + " runs past the end of the file");

            if (type == "IHDR")
            {
                ihdr = new byte[length];
                Array.Copy(file, data, ihdr, 0, length);
                Width = (int)ReadUInt32(file, data);
                Height = (int)ReadUInt32(file, data + 4);
            }
            else if (type == "acTL")
            {
                Assert.Empty(Frames);
                DeclaredFrames = (int)ReadUInt32(file, data);
                Plays = (int)ReadUInt32(file, data + 4);
            }
            else if (type == "fcTL")
            {
                Assert.Equal(26, length);
                Assert.Equal(expectedSequence++, (int)ReadUInt32(file, data));
                Assert.Equal(Width, (int)ReadUInt32(file, data + 4));
                Assert.Equal(Height, (int)ReadUInt32(file, data + 8));
                Assert.Equal(0u, ReadUInt32(file, data + 12));
                Assert.Equal(0u, ReadUInt32(file, data + 16));
                int num = (file[data + 20] << 8) | file[data + 21];
                int den = (file[data + 22] << 8) | file[data + 23];
                if (den == 0) den = 100; // the specification's rule for a zero denominator
                pendingDuration = num * 1000.0 / den;
            }
            else if (type == "IDAT" || type == "fdAT")
            {
                Assert.True(pendingDuration >= 0, type + " without an fcTL in front of it");
                Assert.True(type == "IDAT" ? Frames.Count == 0 : Frames.Count > 0,
                    "IDAT must carry the first frame and fdAT the others");
                int skip = 0;
                if (type == "fdAT")
                {
                    Assert.Equal(expectedSequence++, (int)ReadUInt32(file, data));
                    skip = 4;
                }

                var compressed = new byte[length - skip];
                Array.Copy(file, data + skip, compressed, 0, compressed.Length);
                var bitmap = SKBitmap.Decode(BuildOneFramePng(ihdr!, compressed));
                Assert.NotNull(bitmap);
                Frames.Add(new Frame(pendingDuration, bitmap));
                pendingDuration = -1;
            }
            else if (type == "IEND")
            {
                sawIend = true;
            }

            pos = data + length + 4;
        }

        Assert.True(sawIend, "no IEND");
        Assert.NotNull(ihdr);
    }

    /// <summary>
    /// A plain PNG holding one frame's compressed data.
    /// </summary>
    private static byte[] BuildOneFramePng(byte[] ihdr, byte[] compressed)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(BigEndian((uint)data.Length));
        output.Write(typeBytes);
        output.Write(data);

        // CRC-32 as in the PNG specification's sample code; libpng checks it on decode.
        uint crc = 0xFFFFFFFFu;
        crc = Crc(crc, typeBytes);
        crc = Crc(crc, data);
        output.Write(BigEndian(crc ^ 0xFFFFFFFFu));
    }

    private static uint Crc(uint crc, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc;
    }

    private static byte[] BigEndian(uint value)
        => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    private static uint ReadUInt32(byte[] b, int i)
        => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    /// <summary>
    /// Saves every frame as its own PNG so a person can look at them.
    /// </summary>
    /// <param name="folder">
    /// Where to put them.
    /// </param>
    /// <param name="prefix">
    /// The start of each file name.
    /// </param>
    public void SaveFramesForLooking(string folder, string prefix)
    {
        Directory.CreateDirectory(folder);
        for (int i = 0; i < Frames.Count; i++)
        {
            using var image = SKImage.FromBitmap(Frames[i].Bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(folder, prefix + "-frame" + i + ".png"), png.ToArray());
        }
    }
}
