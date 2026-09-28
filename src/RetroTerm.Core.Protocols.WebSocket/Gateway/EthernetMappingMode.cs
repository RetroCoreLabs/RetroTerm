namespace RetroTerm.Core.Protocols.WebSocket.Gateway;

/// <summary>
/// How the gateway maps an emulated machine's Ethernet onto the host.
///
/// These are genuinely different network topologies, not presets of one another, which is why
/// the choice is explicit rather than inferred from whatever happens to be reachable.
/// </summary>
public enum EthernetMappingMode
{
    /// <summary>Frames are discarded. The guest sees a dead wire.</summary>
    None = 0,

    /// <summary>Bridge onto a REAL host adapter, so the guest is on the physical LAN with its
    /// own MAC and address. Needs npcap on Windows, and root or CAP_NET_RAW elsewhere.</summary>
    HostAdapter = 1,

    /// <summary>Dial out and join a segment someone else is hosting - another RetroTerm, a
    /// RetroCore instance, an nd100x gateway, or a relay. Works through NAT.</summary>
    JoinSegment = 2,

    /// <summary>Be the segment: listen, and let peers dial in. A hub - every frame reaches every
    /// other member, so three machines work as readily as two.</summary>
    HostSegment = 3,

    /// <summary>A multicast group on the local network. No relay to start and nothing to
    /// configure, but LAN-local only.</summary>
    Multicast = 4
}
