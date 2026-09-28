using System;
using System.Globalization;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Turns one line of text into an <see cref="IEthernetBackend"/>, so the whole mapping choice is
/// a single value in the settings file and a single field in the UI.
///
/// The grammar is deliberately the same as RetroCore's, so a spec that works in one product works
/// in the other and can be copied between them:
///
///     (empty) | none | null        discard everything
///     pcap:&lt;interface&gt;             a real host adapter, by index or name substring
///     tcp:&lt;host&gt;:&lt;port&gt;            join a segment by dialling out
///     &lt;host&gt;:&lt;port&gt;                same, shorthand
///     tcp-listen:&lt;port&gt;            BE the segment; peers dial in
///     listen:&lt;port&gt;                same, shorthand
///     udp                          multicast segment on the defaults
///     udp:&lt;port&gt;                   multicast on the default group
///     udp:&lt;group&gt;:&lt;port&gt;           multicast, fully specified
/// </summary>
public static class EthernetBackendFactory
{
    /// <summary>Build the backend named by <paramref name="spec"/>. Never returns null and never
    /// throws on a malformed spec: an unparseable mapping becomes the null backend, because a
    /// typo in a settings file must not stop the gateway from serving terminals and disks.</summary>
    public static IEthernetBackend FromSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return new NullEthernetBackend();
        }

        string s = spec.Trim();

        if (s.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return new NullEthernetBackend();
        }

        if (s.StartsWith("pcap:", StringComparison.OrdinalIgnoreCase))
        {
            string iface = s.Substring("pcap:".Length).Trim();
            return iface.Length == 0 ? new NullEthernetBackend() : new PcapEthernetBackend(iface);
        }

        if (s.Equals("udp", StringComparison.OrdinalIgnoreCase))
        {
            return new UdpEthernetBackend(UdpEthernetBackend.DefaultGroup, RethProtocol.DefaultPort);
        }

        if (s.StartsWith("udp:", StringComparison.OrdinalIgnoreCase))
        {
            string rest = s.Substring("udp:".Length).Trim();
            int colon = rest.LastIndexOf(':');
            if (colon > 0)
            {
                return new UdpEthernetBackend(rest.Substring(0, colon), ParsePort(rest.Substring(colon + 1)));
            }

            // A bare number is a port on the default group; anything else is a group.
            return int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int onlyPort)
                ? new UdpEthernetBackend(UdpEthernetBackend.DefaultGroup, onlyPort)
                : new UdpEthernetBackend(rest, RethProtocol.DefaultPort);
        }

        if (s.StartsWith("tcp-listen:", StringComparison.OrdinalIgnoreCase))
        {
            return new RethHubBackend(ParsePort(s.Substring("tcp-listen:".Length)));
        }

        if (s.StartsWith("listen:", StringComparison.OrdinalIgnoreCase))
        {
            return new RethHubBackend(ParsePort(s.Substring("listen:".Length)));
        }

        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring("tcp:".Length).Trim();
        }

        // What is left is host:port, or a bare host on the default port.
        int sep = s.LastIndexOf(':');
        if (sep > 0)
        {
            return new RethTcpBackend(s.Substring(0, sep), ParsePort(s.Substring(sep + 1)));
        }

        return s.Length == 0
            ? new NullEthernetBackend()
            : new RethTcpBackend(s, RethProtocol.DefaultPort);
    }

    private static int ParsePort(string text)
        => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0
            ? port
            : RethProtocol.DefaultPort;
}
