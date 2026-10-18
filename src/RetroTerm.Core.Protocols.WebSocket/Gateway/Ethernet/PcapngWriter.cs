//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Writes Ethernet frames to a pcapng file that Wireshark and tcpdump open directly.
///
/// pcapng rather than classic pcap because each frame can say which way it went: the gateway
/// sees frames FROM the emulated machine (0x31) and frames TO it (0x30), and in a capture of a
/// conversation that is the first thing one wants to know. The layout follows the pcapng
/// specification (draft-ietf-opsawg-pcapng): one Section Header Block, one Interface Description
/// Block with LINKTYPE_ETHERNET (1), then one Enhanced Packet Block per frame carrying an
/// epb_flags option (code 2) whose low two bits are the direction: 01 inbound, 10 outbound.
/// Here "inbound" means into the emulated machine. Little-endian throughout, which the Section
/// Header's byte-order magic tells the reader.
///
/// Thread-safe: frames arrive from the WebSocket receive loop and from the Ethernet backend's
/// thread, so every write takes one lock. Each block is flushed, so a capture is readable while
/// it is still running and nothing is lost if the program stops.
/// </summary>
public sealed class PcapngWriter : IDisposable
{
    private const int LinkTypeEthernet = 1;
    private const int SnapLength = 65535;

    private readonly object _lock = new();
    private FileStream? _stream;

    /// <summary>The file being written.</summary>
    public string Path { get; }

    /// <summary>Frames written so far.</summary>
    public long FrameCount { get; private set; }

    /// <summary>Create <paramref name="path"/> (replacing any file of that name) and write the
    /// file header. Throws the usual IO exceptions when the file cannot be created.</summary>
    public PcapngWriter(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteSectionHeader();
        WriteInterfaceDescription();
        _stream.Flush();
    }

    /// <summary>Record one frame. <paramref name="toMachine"/> is true for a frame delivered to
    /// the emulated machine, false for one the machine sent.</summary>
    public void WriteFrame(byte[] buffer, int offset, int length, bool toMachine)
    {
        if (buffer is null || length <= 0 || offset < 0 || offset + length > buffer.Length)
        {
            return;
        }

        lock (_lock)
        {
            if (_stream == null)
            {
                return;
            }

            int padded = (length + 3) & ~3;
            int optionsLength = 4 + 4 + 4;           // epb_flags (header + value) + opt_endofopt
            int blockLength = 28 + padded + optionsLength + 4;

            // Microseconds since 1970 (the default if_tsresol of 6).
            long micros = (DateTimeOffset.UtcNow.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;

            var block = new byte[blockLength];
            int p = 0;
            p = PutInt(block, p, 6);                 // Enhanced Packet Block
            p = PutInt(block, p, blockLength);
            p = PutInt(block, p, 0);                 // interface 0
            p = PutInt(block, p, (int)(micros >> 32));
            p = PutInt(block, p, (int)micros);
            p = PutInt(block, p, length);            // captured length
            p = PutInt(block, p, length);            // original length
            Buffer.BlockCopy(buffer, offset, block, p, length);
            p += padded;
            p = PutShort(block, p, 2);               // epb_flags
            p = PutShort(block, p, 4);
            p = PutInt(block, p, toMachine ? 1 : 2); // 01 inbound, 10 outbound
            p = PutInt(block, p, 0);                 // opt_endofopt
            PutInt(block, p, blockLength);

            _stream.Write(block, 0, block.Length);
            _stream.Flush();
            FrameCount++;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    private void WriteSectionHeader()
    {
        var b = new byte[28];
        int p = PutInt(b, 0, 0x0A0D0D0A);           // Section Header Block
        p = PutInt(b, p, 28);
        p = PutInt(b, p, 0x1A2B3C4D);               // byte-order magic
        p = PutShort(b, p, 1);                      // major version
        p = PutShort(b, p, 0);                      // minor version
        p = PutInt(b, p, -1);                       // section length unknown (64-bit -1)
        p = PutInt(b, p, -1);
        PutInt(b, p, 28);
        _stream!.Write(b, 0, b.Length);
    }

    private void WriteInterfaceDescription()
    {
        var b = new byte[20];
        int p = PutInt(b, 0, 1);                    // Interface Description Block
        p = PutInt(b, p, 20);
        p = PutShort(b, p, LinkTypeEthernet);
        p = PutShort(b, p, 0);                      // reserved
        p = PutInt(b, p, SnapLength);
        PutInt(b, p, 20);
        _stream!.Write(b, 0, b.Length);
    }

    private static int PutInt(byte[] b, int p, int v)
    {
        b[p] = (byte)v;
        b[p + 1] = (byte)(v >> 8);
        b[p + 2] = (byte)(v >> 16);
        b[p + 3] = (byte)(v >> 24);
        return p + 4;
    }

    private static int PutShort(byte[] b, int p, int v)
    {
        b[p] = (byte)v;
        b[p + 1] = (byte)(v >> 8);
        return p + 2;
    }
}
