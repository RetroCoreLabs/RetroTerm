using System;
using System.Collections.Generic;
using System.Threading;
using SharpPcap;

namespace RetroTerm.Core.Protocols.WebSocket.Gateway.Ethernet;

/// <summary>
/// Bridges the guest onto a REAL host network adapter with SharpPcap, so the emulated machine
/// appears on the physical LAN with its own MAC and its own address.
///
/// Requires npcap on Windows (https://npcap.com/) or root/CAP_NET_RAW on Linux. Neither is
/// checked here: a failed open leaves <see cref="IsActive"/> false and the gateway carries on,
/// because a missing capture driver must not stop terminals and disks from working.
///
/// SELF-ECHO IS THE TRAP. A promiscuous capture sees the frames this very backend transmits, so
/// without filtering the guest receives everything it sends and reads it as a duplicate-address
/// fault. RetroCore solves this inside the emulated card, which knows its own MAC; the gateway
/// has no card, so it LEARNS the guest's MAC from the source address of the frames the guest
/// transmits (<see cref="NoteLocalMac"/>) and drops inbound frames carrying it.
/// </summary>
public sealed class PcapEthernetBackend : IEthernetBackend
{
    private readonly string _interfaceName;
    private readonly byte[] _localMac = new byte[6];
    private readonly object _macLock = new();

    private ILiveDevice? _device;
    private Thread? _captureThread;
    private volatile bool _stopRequested;
    private bool _macKnown;

    /// <summary>Bridge onto the adapter named or indexed by <paramref name="interfaceName"/>.</summary>
    public PcapEthernetBackend(string interfaceName)
    {
        _interfaceName = interfaceName ?? string.Empty;
        Description = $"host adapter {_interfaceName}";
    }

    /// <inheritdoc />
    public event Action<byte[], int>? OnPacketReceived;

    /// <inheritdoc />
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public string Description { get; private set; }

    /// <summary>
    /// One host adapter, as offered in the settings UI.
    /// </summary>
    /// <param name="Name">The adapter's system name. On Windows this is a GUID, which is why
    /// it is never what the user is shown - and why it is exactly what gets STORED: a
    /// description can change when a driver is updated, the name does not.</param>
    /// <param name="Description">The human-readable name, e.g. "Intel(R) Ethernet I219-V".</param>
    public readonly record struct AdapterInfo(string Name, string Description);

    /// <summary>List the adapters available to capture on. Returns an EMPTY list rather than
    /// throwing when no capture driver is installed, so the settings window can say "install
    /// npcap" instead of failing to open.</summary>
    public static IReadOnlyList<AdapterInfo> ListInterfaces()
    {
        try
        {
            var devices = CaptureDeviceList.Instance;
            var adapters = new List<AdapterInfo>(devices.Count);
            for (int i = 0; i < devices.Count; i++)
            {
                string description = string.IsNullOrWhiteSpace(devices[i].Description)
                    ? devices[i].Name
                    : devices[i].Description;

                adapters.Add(new AdapterInfo(devices[i].Name, description));
            }

            return adapters;
        }
        catch (Exception)
        {
            return Array.Empty<AdapterInfo>();
        }
    }

    /// <summary>Remember the source MAC of a frame the guest transmitted, so the matching inbound
    /// echo can be dropped. Cheap enough to call per frame; only the first address is kept.</summary>
    public void NoteLocalMac(byte[] frame, int offset, int length)
    {
        if (frame is null || length < 12)
        {
            return;
        }

        lock (_macLock)
        {
            if (_macKnown)
            {
                return;
            }

            Buffer.BlockCopy(frame, offset + 6, _localMac, 0, 6);
            _macKnown = true;
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        if (_device is not null)
        {
            return;
        }

        try
        {
            var devices = CaptureDeviceList.Instance;
            ILiveDevice? chosen = null;

            // An index first, then a substring of the name or description - the same two forms
            // the settings UI offers, because a Windows adapter name is a GUID nobody types.
            if (int.TryParse(_interfaceName, out int index) && index >= 0 && index < devices.Count)
            {
                chosen = devices[index] as ILiveDevice;
            }
            else if (_interfaceName.Length > 0)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].Name.Contains(_interfaceName, StringComparison.OrdinalIgnoreCase) ||
                        devices[i].Description.Contains(_interfaceName, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = devices[i] as ILiveDevice;
                        break;
                    }
                }
            }

            if (chosen is null)
            {
                IsActive = false;
                Description = $"host adapter '{_interfaceName}' not found";
                return;
            }

            chosen.Open(DeviceModes.Promiscuous, 100);
            _device = chosen;
            Description = $"host adapter {chosen.Description}";
        }
        catch (Exception ex)
        {
            IsActive = false;
            Description = $"host adapter unavailable ({ex.GetType().Name})";
            return;
        }

        _stopRequested = false;
        IsActive = true;
        _captureThread = new Thread(CaptureLoop)
        {
            Name = "PcapCapture",
            IsBackground = true
        };
        _captureThread.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        _stopRequested = true;
        _captureThread?.Join(2000);
        _captureThread = null;

        if (_device is not null)
        {
            try { _device.Close(); } catch (Exception) { }
            _device = null;
        }

        IsActive = false;
    }

    /// <inheritdoc />
    public void SendPacket(byte[] data, int offset, int length)
    {
        var device = _device;
        if (device is null || !IsActive || length <= 0)
        {
            return;
        }

        NoteLocalMac(data, offset, length);

        try
        {
            device.SendPacket(new ReadOnlySpan<byte>(data, offset, length));
        }
        catch (Exception)
        {
            // A adapter pulled mid-run: report the link down rather than throwing into the
            // emulator's frame path.
            IsActive = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void CaptureLoop()
    {
        while (!_stopRequested)
        {
            var device = _device;
            if (device is null)
            {
                return;
            }

            GetPacketStatus status;
            PacketCapture capture;
            try
            {
                status = device.GetNextPacket(out capture);
            }
            catch (Exception)
            {
                return;
            }

            if (status != GetPacketStatus.PacketRead)
            {
                continue;   // a timeout, which is normal - the 100ms read timeout above
            }

            byte[] data = capture.GetPacket().Data;
            if (data.Length < 14 || IsOurOwnTransmission(data))
            {
                continue;
            }

            OnPacketReceived?.Invoke(data, data.Length);
        }
    }

    /// <summary>True when the frame's SOURCE is the guest's own MAC - see the class summary.</summary>
    private bool IsOurOwnTransmission(byte[] data)
    {
        lock (_macLock)
        {
            if (!_macKnown)
            {
                return false;
            }

            for (int i = 0; i < 6; i++)
            {
                if (data[6 + i] != _localMac[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
