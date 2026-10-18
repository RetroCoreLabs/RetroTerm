//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;
using Xunit;

namespace RetroTerm.Tests.Gateway;

/// <summary>
/// The Ethernet capture file, read back byte by byte against the pcapng layout
/// (draft-ietf-opsawg-pcapng): Section Header, Interface Description with LINKTYPE_ETHERNET,
/// then one Enhanced Packet Block per frame with the direction in epb_flags.
/// </summary>
public class PcapngWriterTests
{
    private static int Int(byte[] b, int p) => BitConverter.ToInt32(b, p);

    [Fact]
    public void Writes_Header_Interface_AndOnePacketPerFrame_WithDirection()
    {
        string path = Path.Combine(Path.GetTempPath(), $"retroterm-pcapng-{Guid.NewGuid():N}.pcapng");
        try
        {
            var fromMachine = new byte[60];
            fromMachine[0] = 0xAA;
            var toMachine = new byte[61];   // odd length: the block pads to 64
            toMachine[0] = 0xBB;

            using (var w = new PcapngWriter(path))
            {
                w.WriteFrame(fromMachine, 0, fromMachine.Length, false);
                w.WriteFrame(toMachine, 0, toMachine.Length, true);
                Assert.Equal(2, w.FrameCount);
            }

            byte[] f = File.ReadAllBytes(path);

            // Section Header Block, 28 bytes, little-endian magic.
            Assert.Equal(0x0A0D0D0A, Int(f, 0));
            Assert.Equal(28, Int(f, 4));
            Assert.Equal(0x1A2B3C4D, Int(f, 8));
            Assert.Equal(28, Int(f, 24));

            // Interface Description Block: link type 1 = Ethernet.
            Assert.Equal(1, Int(f, 28));
            Assert.Equal(20, Int(f, 32));
            Assert.Equal(1, BitConverter.ToUInt16(f, 36));

            // First Enhanced Packet Block: 28 + 60 + 12 options + 4 trailer = 104.
            int p = 48;
            Assert.Equal(6, Int(f, p));
            Assert.Equal(104, Int(f, p + 4));
            Assert.Equal(60, Int(f, p + 20));
            Assert.Equal(60, Int(f, p + 24));
            Assert.Equal(0xAA, f[p + 28]);
            Assert.Equal(2, BitConverter.ToUInt16(f, p + 88));   // epb_flags
            Assert.Equal(2, Int(f, p + 92));                     // outbound = from the machine
            Assert.Equal(104, Int(f, p + 100));

            // Second: 61 bytes padded to 64 -> 28 + 64 + 12 + 4 = 108, inbound.
            p += 104;
            Assert.Equal(6, Int(f, p));
            Assert.Equal(108, Int(f, p + 4));
            Assert.Equal(61, Int(f, p + 20));
            Assert.Equal(0xBB, f[p + 28]);
            Assert.Equal(1, Int(f, p + 96));                     // inbound = to the machine
            Assert.Equal(p + 108, f.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
