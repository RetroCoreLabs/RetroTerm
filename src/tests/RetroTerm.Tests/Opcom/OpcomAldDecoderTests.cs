using RetroTerm.Core.Opcom;
using Xunit;

namespace RetroTerm.Tests.Opcom;

public class OpcomAldDecoderTests
{
    // ==================== ALD Register Decode (existing) ====================

    [Fact]
    public void Decode_Zero_IsStop()
    {
        var info = OpcomAldDecoder.Decode(0);
        Assert.False(info.PerformsLoad);
        Assert.Equal("STOP (no load)", info.Description);
        Assert.Equal(0, info.DeviceAddress);
    }

    [Fact]
    public void Decode_021540_IsSmdBootstrapLoadAndRun()
    {
        int value = 0;
        OctalHelper.TryParseOctal32("021540".AsSpan(), out value);

        var info = OpcomAldDecoder.Decode(value);
        Assert.True(info.PerformsLoad);
        Assert.True(info.IsBootstrap);
        Assert.False(info.LoadOnPowerUp);
        Assert.True(info.AutoRun);
        Assert.Contains("SMD", info.DeviceName);
        Assert.Contains("Bootstrap", info.Description);
    }

    [Fact]
    public void Decode_001560_IsFloppyBpunLoadAndRun()
    {
        int value = 0;
        OctalHelper.TryParseOctal32("001560".AsSpan(), out value);

        var info = OpcomAldDecoder.Decode(value);
        Assert.True(info.PerformsLoad);
        Assert.False(info.IsBootstrap);
        Assert.True(info.AutoRun);
        Assert.Contains("Floppy", info.DeviceName);
        Assert.Contains("BPUN", info.Description);
    }

    [Fact]
    public void Decode_101560_IsFloppyLoadOnly()
    {
        int value = 0;
        OctalHelper.TryParseOctal32("101560".AsSpan(), out value);

        var info = OpcomAldDecoder.Decode(value);
        Assert.True(info.PerformsLoad);
        Assert.True(info.LoadOnPowerUp);
        Assert.False(info.AutoRun);
        Assert.Contains("Floppy", info.DeviceName);
        Assert.Contains("load only", info.Description);
    }

    [Fact]
    public void Decode_100000_IsStopWithLoadFlag()
    {
        int value = 0;
        OctalHelper.TryParseOctal32("100000".AsSpan(), out value);

        var info = OpcomAldDecoder.Decode(value);
        Assert.False(info.PerformsLoad);
        Assert.Equal("STOP (no load)", info.Description);
    }

    [Fact]
    public void Decode_020500_IsWinchesterBootstrapRun()
    {
        int value = 0;
        OctalHelper.TryParseOctal32("020500".AsSpan(), out value);

        var info = OpcomAldDecoder.Decode(value);
        Assert.True(info.PerformsLoad);
        Assert.True(info.IsBootstrap);
        Assert.True(info.AutoRun);
        Assert.Contains("Winchester", info.DeviceName);
    }

    // ==================== Boot Presets ====================

    [Fact]
    public void BootPresets_HasAll13Entries()
    {
        Assert.Equal(13, OpcomAldDecoder.BootPresets.Length);
        Assert.Equal("400", OpcomAldDecoder.BootPresets[0].OpcomCommand);
        Assert.Equal("20500", OpcomAldDecoder.BootPresets[4].OpcomCommand);
        Assert.Equal("121560", OpcomAldDecoder.BootPresets[12].OpcomCommand);
    }

    // ==================== DecodeBootCommand ====================

    [Fact]
    public void DecodeBootCommand_Zero_IsStop()
    {
        var info = OpcomAldDecoder.DecodeBootCommand(0);
        Assert.False(info.PerformsLoad);
        Assert.Equal("STOP (no load)", info.Description);
    }

    [Fact]
    public void DecodeBootCommand_Bpun_1560_LoadOnly()
    {
        // 1560 octal = BPUN, load only
        OctalHelper.TryParseOctal32("1560".AsSpan(), out int value);
        var info = OpcomAldDecoder.DecodeBootCommand(value);

        Assert.True(info.PerformsLoad);
        Assert.False(info.AutoRun);
        Assert.Equal(0, info.LoadMode);
        Assert.Equal("BPUN", info.LoadFormatName);
        Assert.Contains("Floppy", info.DeviceName);
    }

    [Fact]
    public void DecodeBootCommand_Bootstrap_20500_LoadAndRun()
    {
        // 20500 octal = Bootstrap from Winchester, load+run
        OctalHelper.TryParseOctal32("20500".AsSpan(), out int value);
        var info = OpcomAldDecoder.DecodeBootCommand(value);

        Assert.True(info.PerformsLoad);
        Assert.True(info.AutoRun);
        Assert.Equal(1, info.LoadMode);
        Assert.Equal("Bootstrap", info.LoadFormatName);
        Assert.Contains("Winchester", info.DeviceName);
    }

    [Fact]
    public void DecodeBootCommand_Binary_100400_LoadOnly()
    {
        // 100400 octal = Binary from Paper Tape, load only
        OctalHelper.TryParseOctal32("100400".AsSpan(), out int value);
        var info = OpcomAldDecoder.DecodeBootCommand(value);

        Assert.True(info.PerformsLoad);
        Assert.False(info.AutoRun);
        Assert.Equal(2, info.LoadMode);
        Assert.Equal("Binary", info.LoadFormatName);
        Assert.Contains("Paper Tape", info.DeviceName);
    }

    [Fact]
    public void DecodeBootCommand_MassStorage_121540_LoadOnly()
    {
        // 121540 octal = Mass storage from SMD Disk, load only
        OctalHelper.TryParseOctal32("121540".AsSpan(), out int value);
        var info = OpcomAldDecoder.DecodeBootCommand(value);

        Assert.True(info.PerformsLoad);
        Assert.False(info.AutoRun);
        Assert.Equal(3, info.LoadMode);
        Assert.Equal("Mass storage", info.LoadFormatName);
        Assert.Contains("SMD", info.DeviceName);
    }

    [Fact]
    public void DecodeBootCommand_Bootstrap_21560_ScsiLoadAndRun()
    {
        // 21560 octal = Bootstrap from SCSI/Floppy, load+run
        OctalHelper.TryParseOctal32("21560".AsSpan(), out int value);
        var info = OpcomAldDecoder.DecodeBootCommand(value);

        Assert.True(info.PerformsLoad);
        Assert.True(info.AutoRun);
        Assert.Equal(1, info.LoadMode);
        Assert.Equal("Bootstrap", info.LoadFormatName);
        Assert.Contains("Floppy", info.DeviceName);
        Assert.Contains("load and run", info.Description);
    }
}
