namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// Turns the user's choice - a mode plus one parameter - into a backend spec string.
///
/// A static function rather than a method on the settings object, because the settings live in
/// the application's preferences singleton, which reads and writes a file on disk. Keeping the
/// rule here means it can be exercised directly, without a settings file and without mutating
/// the running application's own configuration.
/// </summary>
public static class EthernetMappingSpec
{
    /// <summary>The default segment port: 3094, the ND Ethernet II PCB number.</summary>
    public const int DefaultPort = 3094;

    /// <summary>The default multicast group, administratively scoped.</summary>
    public const string DefaultGroup = "239.3.9.4";

    /// <summary>
    /// Build the spec. <paramref name="enabled"/> wins over the mode: switching Ethernet off
    /// gives the guest a dead wire whatever mapping is still selected, so the user's choice is
    /// remembered rather than cleared.
    /// </summary>
    public static string Build(bool enabled, EthernetMappingMode mode, string? target)
    {
        if (!enabled)
        {
            return "none";
        }

        string t = (target ?? string.Empty).Trim();

        return mode switch
        {
            EthernetMappingMode.HostAdapter => t.Length == 0 ? "none" : "pcap:" + t,
            EthernetMappingMode.JoinSegment => "tcp:" + (t.Length == 0 ? "127.0.0.1:" + DefaultPort : t),
            EthernetMappingMode.HostSegment => "listen:" + (t.Length == 0 ? DefaultPort.ToString() : t),
            EthernetMappingMode.Multicast => t.Length == 0 ? "udp" : "udp:" + t,
            _ => "none"
        };
    }
}
