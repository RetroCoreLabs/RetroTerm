using System;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// The RETH wire format, shared by every TCP-based Ethernet mapping.
///
/// TCP is a byte stream, not a message stream, so a frame cannot simply be written: the reader
/// would have no idea where one ends and the next begins. RETH therefore opens with a one-time
/// 5-byte handshake and then length-prefixes every frame.
///
///     handshake   'R' 'E' 'T' 'H' &lt;version&gt;      both sides write, then read
///     frame       [u16 big-endian length][frame bytes, no FCS]
///
/// BOTH SIDES WRITE THE HANDSHAKE BEFORE READING. Two peers that connect at the same instant
/// would otherwise each sit waiting for the other's greeting.
///
/// A length outside 1..<see cref="MaxFrameBytes"/> means the stream is out of step, and a pure
/// length-prefixed stream cannot be resynchronised - there is no marker to hunt for. The only
/// honest response is to drop the connection; carrying on forwards rubbish as Ethernet frames.
///
/// This format is spoken by RetroCore's TCP backend and relay, and by the nd100x gateway's
/// ethernet segment, so all three interoperate directly.
/// </summary>
public static class RethProtocol
{
    /// <summary>Handshake magic: 'R','E','T','H'.</summary>
    public static readonly byte[] Magic = { 0x52, 0x45, 0x54, 0x48 };

    /// <summary>Protocol version, bumped on any wire-format change.</summary>
    public const byte ProtocolVersion = 1;

    /// <summary>Handshake length: the four magic bytes plus the version.</summary>
    public const int HandshakeLength = 5;

    /// <summary>Largest frame accepted. Ethernet tops out at 1518 bytes including FCS;
    /// the headroom guards against a garbage length without rejecting anything real.</summary>
    public const int MaxFrameBytes = 2048;

    /// <summary>Default TCP port: 3094, the ND Ethernet II PCB number.</summary>
    public const int DefaultPort = 3094;

    /// <summary>Build the 5-byte greeting this endpoint sends.</summary>
    public static byte[] BuildHandshake()
    {
        var hello = new byte[HandshakeLength];
        Buffer.BlockCopy(Magic, 0, hello, 0, Magic.Length);
        hello[4] = ProtocolVersion;
        return hello;
    }

    /// <summary>True when <paramref name="buffer"/> opens with the RETH magic. The version byte
    /// is reported rather than enforced: an unknown version is logged and allowed, so a newer
    /// peer and an older one still interoperate instead of silently refusing each other.</summary>
    public static bool IsHandshake(byte[] buffer, int length, out byte peerVersion)
    {
        peerVersion = 0;
        if (buffer is null || length < HandshakeLength)
        {
            return false;
        }

        for (int i = 0; i < Magic.Length; i++)
        {
            if (buffer[i] != Magic[i])
            {
                return false;
            }
        }

        peerVersion = buffer[4];
        return true;
    }

    /// <summary>True when a length read off the wire could be a real frame.</summary>
    public static bool IsPlausibleLength(int length) => length > 0 && length <= MaxFrameBytes;

    /// <summary>Write the two-byte big-endian length prefix into <paramref name="destination"/>.</summary>
    public static void WriteLengthPrefix(byte[] destination, int offset, int length)
    {
        destination[offset] = (byte)((length >> 8) & 0xFF);
        destination[offset + 1] = (byte)(length & 0xFF);
    }

    /// <summary>Read a two-byte big-endian length prefix.</summary>
    public static int ReadLengthPrefix(byte[] source, int offset)
        => ((source[offset] & 0xFF) << 8) | (source[offset + 1] & 0xFF);
}
