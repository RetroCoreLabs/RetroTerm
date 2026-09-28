using System;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// The "not mapped anywhere" choice: frames from the guest are discarded and none ever arrive.
///
/// This is a real option rather than a placeholder. A guest with an Ethernet card configured and
/// no mapping behaves exactly like a machine plugged into a dead switch port - which is what the
/// user asked for when they chose it, and is different from the card not existing at all.
/// <see cref="IsActive"/> is true because the mapping IS working; there is simply nothing on the
/// other side of it.
/// </summary>
public sealed class NullEthernetBackend : IEthernetBackend
{
    /// <inheritdoc />
    public event Action<byte[], int>? OnPacketReceived
    {
        add { }      // nothing is ever raised, so the handler is not stored
        remove { }
    }

    /// <inheritdoc />
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public string Description => "not mapped (frames discarded)";

    /// <inheritdoc />
    public void Start() => IsActive = true;

    /// <inheritdoc />
    public void Stop() => IsActive = false;

    /// <inheritdoc />
    public void SendPacket(byte[] data, int offset, int length)
    {
        // Deliberately empty - see the class summary.
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
