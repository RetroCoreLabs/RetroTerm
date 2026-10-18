//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// One short line about an Ethernet frame, for the gateway debug window.
///
/// Decodes only what the bytes say, by the published layouts: Ethernet II (DIX) and IEEE 802.3
/// headers, ARP (RFC 826), IPv4 (RFC 791) with ICMP / TCP / UDP ports. An ND machine running
/// TCP/IP sends DIX frames; one running COSMOS sends 802.3 length frames with an LLC header.
/// Anything else is shown by its EtherType, so unknown traffic is still visible.
/// </summary>
public static class EthernetFrameSummary
{
    /// <summary>Describe the frame at <paramref name="offset"/>, <paramref name="length"/>
    /// bytes long.</summary>
    public static string Describe(byte[] buf, int offset, int length)
    {
        if (buf is null || length < 14 || offset < 0 || offset + length > buf.Length)
        {
            return $"short frame ({length}B)";
        }

        string dst = Mac(buf, offset);
        string src = Mac(buf, offset + 6);
        int type = (buf[offset + 12] << 8) | buf[offset + 13];
        string head = $"{src} > {dst}";

        if (type <= 1500)
        {
            // IEEE 802.3: the field is a length; an 802.2 LLC header follows.
            if (length < 17)
            {
                return $"{head} 802.3 len {type} short";
            }

            return $"{head} 802.3 len {type} LLC dsap {buf[offset + 14]:X2} ssap {buf[offset + 15]:X2} ctl {buf[offset + 16]:X2}";
        }

        int p = offset + 14;
        int n = length - 14;
        switch (type)
        {
            case 0x0806:
                return $"{head} {Arp(buf, p, n)}";
            case 0x0800:
                return $"{head} {Ipv4(buf, p, n)}";
            case 0x86DD:
                return $"{head} IPv6";
            default:
                return $"{head} type {type:X4}";
        }
    }

    private static string Mac(byte[] b, int o)
    {
        if (b[o] == 0xFF && b[o + 1] == 0xFF && b[o + 2] == 0xFF && b[o + 3] == 0xFF && b[o + 4] == 0xFF && b[o + 5] == 0xFF)
        {
            return "bcast";
        }

        return $"{b[o]:x2}:{b[o + 1]:x2}:{b[o + 2]:x2}:{b[o + 3]:x2}:{b[o + 4]:x2}:{b[o + 5]:x2}";
    }

    private static string Ip(byte[] b, int o) => $"{b[o]}.{b[o + 1]}.{b[o + 2]}.{b[o + 3]}";

    private static int Be16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    private static string Arp(byte[] b, int o, int n)
    {
        if (n < 28)
        {
            return "ARP short";
        }

        int op = Be16(b, o + 6);
        return op switch
        {
            1 => $"ARP who-has {Ip(b, o + 24)} tell {Ip(b, o + 14)}",
            2 => $"ARP {Ip(b, o + 14)} is-at {Mac(b, o + 8)}",
            _ => $"ARP op {op}"
        };
    }

    private static string Ipv4(byte[] b, int o, int n)
    {
        if (n < 20 || (b[o] >> 4) != 4)
        {
            return "IPv4 short or not version 4";
        }

        int ihl = (b[o] & 0x0F) * 4;
        if (ihl < 20 || n < ihl)
        {
            return "IPv4 bad header length";
        }

        string src = Ip(b, o + 12);
        string dst = Ip(b, o + 16);
        int p = o + ihl;
        int pn = n - ihl;
        switch (b[o + 9])
        {
            case 1:
                if (pn < 8)
                {
                    return $"ICMP {src} > {dst} short";
                }

                return b[p] switch
                {
                    8 => $"ICMP echo request {src} > {dst} seq {Be16(b, p + 6)}",
                    0 => $"ICMP echo reply {src} > {dst} seq {Be16(b, p + 6)}",
                    _ => $"ICMP type {b[p]} code {b[p + 1]} {src} > {dst}"
                };
            case 6:
                if (pn < 20)
                {
                    return $"TCP {src} > {dst} short";
                }

                return $"TCP {src}:{Be16(b, p)} > {dst}:{Be16(b, p + 2)} [{TcpFlags(b[p + 13])}]{TcpPayload(b, o, ihl, p, pn)}";
            case 17:
                if (pn < 8)
                {
                    return $"UDP {src} > {dst} short";
                }

                return $"UDP {src}:{Be16(b, p)} > {dst}:{Be16(b, p + 2)}";
            default:
                return $"IPv4 proto {b[o + 9]} {src} > {dst}";
        }
    }

    /// <summary>The TCP data in hex, at most 16 bytes, e.g. " data 3: FF FB 01". The length
    /// comes from the IPv4 total length, not the frame: a short frame is padded to 60 bytes and
    /// the padding is not data.</summary>
    private static string TcpPayload(byte[] b, int ip, int ihl, int tcp, int available)
    {
        int doff = (b[tcp + 12] >> 4) * 4;
        int total = Be16(b, ip + 2);
        int len = total - ihl - doff;
        if (doff < 20 || len <= 0) return string.Empty;

        int shown = Math.Min(Math.Min(len, 16), available - doff);
        if (shown <= 0) return $" data {len}";

        var sb = new System.Text.StringBuilder($" data {len}:");
        for (int i = 0; i < shown; i++) sb.Append(' ').Append(b[tcp + doff + i].ToString("X2"));
        if (len > shown) sb.Append(" ...");
        return sb.ToString();
    }

    // tcpdump letters and order: SYN FIN RST PSH, ACK as '.', URG.
    private static string TcpFlags(byte f)
    {
        var s = new System.Text.StringBuilder(6);
        if ((f & 0x02) != 0) s.Append('S');
        if ((f & 0x01) != 0) s.Append('F');
        if ((f & 0x04) != 0) s.Append('R');
        if ((f & 0x08) != 0) s.Append('P');
        if ((f & 0x10) != 0) s.Append('.');
        if ((f & 0x20) != 0) s.Append('U');
        return s.ToString();
    }
}
