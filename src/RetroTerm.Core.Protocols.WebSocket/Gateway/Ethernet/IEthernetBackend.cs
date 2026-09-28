using System;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Where an emulated machine's Ethernet frames go once they leave the gateway.
///
/// The gateway itself is only a bridge: frames arrive from the emulator over the WebSocket
/// as <c>0x31</c> and are handed to <see cref="SendPacket"/>; frames the backend receives are
/// raised on <see cref="OnPacketReceived"/> and go back as <c>0x30</c>. Every mapping the user
/// can choose - a real host NIC, a RETH segment, a multicast group, or nothing at all - is one
/// implementation of this interface, so the gateway has no idea which is in use.
///
/// Threading: <see cref="OnPacketReceived"/> is raised on a backend-owned thread, never the
/// caller's. <see cref="SendPacket"/> may be called from any thread and implementations
/// serialise internally.
/// </summary>
public interface IEthernetBackend : IDisposable
{
    /// <summary>Raised for each frame received from whatever this backend is attached to.
    /// The buffer is only valid for the duration of the call - copy it to keep it.</summary>
    event Action<byte[], int>? OnPacketReceived;

    /// <summary>Begin carrying traffic. Safe to call once; a failed start leaves
    /// <see cref="IsActive"/> false rather than throwing, because a missing npcap or a relay
    /// that is not up yet must not take the whole gateway down with it.</summary>
    void Start();

    /// <summary>Stop carrying traffic and release the socket or capture handle.</summary>
    void Stop();

    /// <summary>Send one Ethernet frame, without FCS.</summary>
    void SendPacket(byte[] data, int offset, int length);

    /// <summary>True while frames can actually move. The gateway reports this to the emulator
    /// as the <c>0x32</c> link status, so a guest sees the link go down when a relay dies.</summary>
    bool IsActive { get; }

    /// <summary>Human-readable mapping description, shown in the gateway UI.</summary>
    string Description { get; }
}
