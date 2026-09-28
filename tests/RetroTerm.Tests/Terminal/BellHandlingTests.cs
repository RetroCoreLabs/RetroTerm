using System;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Verifies that BEL (0x07) reaches the <see cref="TerminalEmulatorBase.Bell"/> event
/// for every emulator family. The Desktop layer subscribes to this event to make the
/// host machine actually beep, so a swallowed BEL means a silent terminal.
/// </summary>
public class BellHandlingTests
{
    [Fact]
    public void VT100_BEL_RaisesBellEvent()
    {
        var emulator = new VT100Emulator();
        int bells = 0;
        emulator.Bell += () => bells++;

        emulator.ProcessData(new byte[] { 0x07 });

        Assert.Equal(1, bells);
    }

    [Fact]
    public void TDV2200_BEL_RaisesBellEvent()
    {
        var emulator = new TDV2200Emulator();
        int bells = 0;
        emulator.Bell += () => bells++;

        emulator.ProcessData(new byte[] { 0x07 });

        Assert.Equal(1, bells);
    }

    [Fact]
    public void VT100_BEL_DoesNotConsumeSurroundingText()
    {
        var emulator = new VT100Emulator();
        int bells = 0;
        emulator.Bell += () => bells++;

        // "A" BEL "B" - the bell must not eat the printable characters around it.
        emulator.ProcessData(new byte[] { (byte)'A', 0x07, (byte)'B' });

        Assert.Equal(1, bells);
        var buffer = emulator.GetBuffer();
        Assert.Equal((uint)'A', buffer.GetCell(0, 0).Codepoint);
        Assert.Equal((uint)'B', buffer.GetCell(0, 1).Codepoint);
    }

    [Fact]
    public void MultipleBELs_RaiseOneEventEach()
    {
        var emulator = new VT100Emulator();
        int bells = 0;
        emulator.Bell += () => bells++;

        emulator.ProcessData(new byte[] { 0x07, 0x07, 0x07 });

        Assert.Equal(3, bells);
    }
}
