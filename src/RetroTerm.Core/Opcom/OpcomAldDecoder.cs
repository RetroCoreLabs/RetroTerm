using System;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Decodes the ALD (Automatic Load Descriptor) register I12 value
/// into human-readable boot device information.
/// </summary>
public static class OpcomAldDecoder
{
    /// <summary>
    /// Known boot device entries with their device addresses and descriptions.
    /// </summary>
    private static readonly (int DeviceAddress, string DeviceName)[] KnownDevices =
    {
        (0,    "No device (STOP)"),
        (256,  "Paper Tape Reader (400)"),    // 400 octal = 256 decimal
        (320,  "Winchester Disk (500)"),       // 500 octal = 320 decimal
        (864,  "SMD/ECC Disk (1540)"),         // 1540 octal = 864 decimal
        (880,  "Floppy Disc 1 (1560)"),        // 1560 octal = 880 decimal
        (888,  "Floppy Disc 2 (1570)"),        // 1570 octal = 888 decimal
        (896,  "HDLC Remote (1600)"),          // 1600 octal = 896 decimal
    };

    /// <summary>
    /// Decoded ALD information.
    /// </summary>
    public readonly struct AldInfo
    {
        /// <summary>
        /// Raw I12 register value.
        /// </summary>
        public int RawValue { get; }

        /// <summary>
        /// Bit 17: load on power-up even with standby power.
        /// </summary>
        public bool LoadOnPowerUp { get; }

        /// <summary>
        /// Bit 16: true = bootstrap (mass storage), false = BPUN (binary).
        /// </summary>
        public bool IsBootstrap { get; }

        /// <summary>
        /// Bits 15-0: hardware device address.
        /// </summary>
        public int DeviceAddress { get; }

        /// <summary>
        /// Human-readable device name.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// Whether this ALD setting performs a load (vs STOP).
        /// </summary>
        public bool PerformsLoad { get; }

        /// <summary>
        /// Whether the CPU auto-starts after load.
        /// </summary>
        public bool AutoRun { get; }

        /// <summary>
        /// Full human-readable description.
        /// </summary>
        public string Description { get; }

        public AldInfo(int rawValue, bool loadOnPowerUp, bool isBootstrap, int deviceAddress,
            string deviceName, bool performsLoad, bool autoRun, string description)
        {
            RawValue = rawValue;
            LoadOnPowerUp = loadOnPowerUp;
            IsBootstrap = isBootstrap;
            DeviceAddress = deviceAddress;
            DeviceName = deviceName;
            PerformsLoad = performsLoad;
            AutoRun = autoRun;
            Description = description;
        }
    }

    /// <summary>
    /// Decodes an I12 (ALD) register value into structured information.
    /// The value is an 18-bit field: bit17=load-on-powerup, bit16=bootstrap, bits15-0=device address.
    /// </summary>
    public static AldInfo Decode(int i12Value)
    {
        // The I12 value is a 16-bit register:
        // Bit 15 (0x8000): load on power-up (always load, even with standby power)
        // Bit 14: reserved
        // Bit 13 (0x2000): 1=bootstrap/mass-storage, 0=BPUN/binary
        // Bits 12-0: device address
        //
        // Verified from ALD table: 021540 oct (9056 dec) = bit13 set + device 1540 oct
        //                          101560 oct (33648 dec) = bit15 set + device 1560 oct
        bool loadOnPowerUp = (i12Value & 0x8000) != 0;
        bool isBootstrap = (i12Value & 0x2000) != 0;
        int deviceAddress = i12Value & 0x1FFF; // 13 bits

        bool performsLoad = deviceAddress != 0;
        // ALD positions 8-15 = load+run, positions 2-7 = load only
        // In the I12 encoding: positions 8-15 have bit17=0, positions 2-7 have bit17=1
        // Actually: autoRun = !loadOnPowerUp when performsLoad
        bool autoRun = performsLoad && !loadOnPowerUp;

        string deviceName = LookupDeviceName(deviceAddress);
        string format = isBootstrap ? "Bootstrap" : "BPUN";
        string action = !performsLoad ? "STOP (no load)" :
                        autoRun ? "load and run" : "load only";

        string description;
        if (!performsLoad)
        {
            description = "STOP (no load)";
        }
        else
        {
            description = $"{format} from {deviceName}, {action}";
        }

        return new AldInfo(i12Value, loadOnPowerUp, isBootstrap, deviceAddress,
            deviceName, performsLoad, autoRun, description);
    }

    /// <summary>
    /// Looks up a human-readable device name for a given device address.
    /// </summary>
    private static string LookupDeviceName(int deviceAddress)
    {
        for (int i = 0; i < KnownDevices.Length; i++)
        {
            if (KnownDevices[i].DeviceAddress == deviceAddress)
                return KnownDevices[i].DeviceName;
        }
        return $"Unknown device ({OctalHelper.ToOctalTrimmed(deviceAddress)})";
    }

    /// <summary>
    /// Load format names indexed by mode: 0=BPUN, 1=Bootstrap, 2=Binary, 3=Mass storage.
    /// </summary>
    private static readonly string[] LoadFormatNames = { "BPUN", "Bootstrap", "Binary", "Mass storage" };

    /// <summary>
    /// Decodes a full OPCOM boot command value (typed before the ampersand or the dollar sign) into structured information.
    /// Unlike Decode() which interprets ALD register semantics, this uses OPCOM boot command encoding:
    /// - Bits 0-12: Device IOX address
    /// - Bit 13 (0x2000): Bootstrap flag (load+run, MASS loader starts at address 0)
    /// - Bit 15 (0x8000): Binary/mass-storage flag
    /// - Combined mode: 00=BPUN, 01=Bootstrap, 10=Binary, 11=Mass storage
    /// - Only Bootstrap mode auto-runs (starts at address 20 octal)
    /// </summary>
    public static BootCommandInfo DecodeBootCommand(int commandValue)
    {
        int deviceAddress = commandValue & 0x1FFF;
        int loadMode = ((commandValue & 0x8000) != 0 ? 2 : 0) | ((commandValue & 0x2000) != 0 ? 1 : 0);
        bool performsLoad = deviceAddress != 0;
        bool autoRun = performsLoad && loadMode == 1; // Only Bootstrap auto-runs

        string deviceName = LookupDeviceName(deviceAddress);
        string formatName = LoadFormatNames[loadMode];
        string action = !performsLoad ? "STOP (no load)" :
                        autoRun ? "load and run" : "load only";

        string description;
        if (!performsLoad)
        {
            description = "STOP (no load)";
        }
        else
        {
            description = $"{formatName} from {deviceName}, {action}";
        }

        return new BootCommandInfo(commandValue, deviceAddress, loadMode, formatName,
            deviceName, performsLoad, autoRun, description);
    }

    /// <summary>
    /// Decoded OPCOM boot command information.
    /// </summary>
    public readonly struct BootCommandInfo
    {
        public int RawValue { get; }
        public int DeviceAddress { get; }
        public int LoadMode { get; }
        public string LoadFormatName { get; }
        public string DeviceName { get; }
        public bool PerformsLoad { get; }
        public bool AutoRun { get; }
        public string Description { get; }

        public BootCommandInfo(int rawValue, int deviceAddress, int loadMode, string loadFormatName,
            string deviceName, bool performsLoad, bool autoRun, string description)
        {
            RawValue = rawValue;
            DeviceAddress = deviceAddress;
            LoadMode = loadMode;
            LoadFormatName = loadFormatName;
            DeviceName = deviceName;
            PerformsLoad = performsLoad;
            AutoRun = autoRun;
            Description = description;
        }
    }

    /// <summary>
    /// Known boot command presets for the Boot UI dropdown.
    /// OpcomCommand is the full octal value to type before the ampersand at the OPCOM prompt.
    /// </summary>
    public static readonly (string OpcomCommand, string DisplayName)[] BootPresets =
    {
        ("400",    "Paper Tape - BPUN, load only"),
        ("1560",   "Floppy/SCSI - BPUN, load only"),
        ("1570",   "Floppy 2 - BPUN, load only"),
        ("1600",   "HDLC - BPUN, load only"),
        ("20500",  "Winchester - Bootstrap, load+run"),
        ("21540",  "SMD Disk - Bootstrap, load+run"),
        ("21560",  "SCSI/Floppy - Bootstrap, load+run"),
        ("100400", "Paper Tape - Binary, load only"),
        ("101560", "SCSI/Floppy - Binary, load only"),
        ("101600", "HDLC - Binary, load only"),
        ("120500", "Winchester - Mass storage, load only"),
        ("121540", "SMD Disk - Mass storage, load only"),
        ("121560", "SCSI/Floppy - Mass storage, load only"),
    };
}
