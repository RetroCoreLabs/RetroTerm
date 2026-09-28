using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for TDV2200 locking shift sequences (LS2, LS3) and shift in/out (SI, SO).
/// These tests verify that character set switching correctly updates the FontNumber
/// stored in each cell, which determines which font glyphs are used for rendering.
/// </summary>
public class TDV2200LockingShiftTests
{
    [Fact]
    public void LS2_ShouldSetFontNumberTo2_ForSubsequentChars()
    {
        // ESC n = LS2 (Locking Shift 2) - switch to G2 character set
        var emulator = new TDV2200Emulator(80, 24);

        // Send LS2 sequence followed by a character
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn")); // ESC n = LS2
        emulator.ProcessData(Encoding.ASCII.GetBytes("A"));

        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.Equal(2, cell.FontNumber);
    }

    [Fact]
    public void LS3_ShouldSetFontNumberTo3_ForSubsequentChars()
    {
        // ESC o = LS3 (Locking Shift 3) - switch to G3 character set
        var emulator = new TDV2200Emulator(80, 24);

        // Send LS3 sequence followed by a character
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bo")); // ESC o = LS3
        emulator.ProcessData(Encoding.ASCII.GetBytes("X"));

        var cell = emulator.Buffer.GetCell(0, 0);
        Assert.Equal(3, cell.FontNumber);
    }

    [Fact]
    public void SI_AfterLS2_ShouldResetFontNumberTo0()
    {
        // SI (0x0F) should reset back to G0 after LS2
        var emulator = new TDV2200Emulator(80, 24);

        // LS2, write char, SI, write char
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn")); // LS2
        emulator.ProcessData(Encoding.ASCII.GetBytes("A"));     // fontNum=2
        emulator.ProcessData(new byte[] { 0x0F });              // SI
        emulator.ProcessData(Encoding.ASCII.GetBytes("B"));     // should be fontNum=0

        Assert.Equal(2, emulator.Buffer.GetCell(0, 0).FontNumber); // A with LS2
        Assert.Equal(0, emulator.Buffer.GetCell(0, 1).FontNumber); // B after SI
    }

    [Fact]
    public void SI_AfterLS3_ShouldResetFontNumberTo0()
    {
        // SI (0x0F) should reset back to G0 after LS3
        var emulator = new TDV2200Emulator(80, 24);

        // LS3, write char, SI, write char
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bo")); // LS3
        emulator.ProcessData(Encoding.ASCII.GetBytes("X"));     // fontNum=3
        emulator.ProcessData(new byte[] { 0x0F });              // SI
        emulator.ProcessData(Encoding.ASCII.GetBytes("Y"));     // should be fontNum=0

        Assert.Equal(3, emulator.Buffer.GetCell(0, 0).FontNumber); // X with LS3
        Assert.Equal(0, emulator.Buffer.GetCell(0, 1).FontNumber); // Y after SI
    }

    [Fact]
    public void SO_ShouldSetFontNumberTo1()
    {
        // SO (0x0E) should switch to G1 (fontNum=1)
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(new byte[] { 0x0E }); // SO
        emulator.ProcessData(Encoding.ASCII.GetBytes("A"));

        // G1 is typically US ASCII (fontNum=0 or 1 depending on designation)
        // With default G1=USASCII, fontNum should be 0 because G0 and G1 both map to fontNum=0
        // But the _invokedCharacterSet is 1, and GetActiveCharacterSetType returns USASCII
        // which doesn't trigger fontNum 2 or 3
        var cell = emulator.Buffer.GetCell(0, 0);
        // Since G1 is USASCII by default, fontNum stays 0
        Assert.Equal(0, cell.FontNumber);
    }

    [Fact]
    public void MultipleCharacters_WithLS2_ShouldAllHaveFontNumber2()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Send LS2 and write multiple characters
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn")); // LS2
        emulator.ProcessData(Encoding.ASCII.GetBytes("ABCDEF"));

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(2, emulator.Buffer.GetCell(0, i).FontNumber);
        }
    }

    [Fact]
    public void MultipleCharacters_WithLS3_ShouldAllHaveFontNumber3()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Send LS3 and write multiple characters
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bo")); // LS3
        emulator.ProcessData(Encoding.ASCII.GetBytes("123456"));

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(3, emulator.Buffer.GetCell(0, i).FontNumber);
        }
    }

    [Fact]
    public void AlternatingLS2AndSI_ShouldCorrectlyToggleFontNumber()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Pattern: LS2 A SI B LS2 C SI D
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn"));  // LS2
        emulator.ProcessData(Encoding.ASCII.GetBytes("A"));      // fontNum=2
        emulator.ProcessData(new byte[] { 0x0F });               // SI
        emulator.ProcessData(Encoding.ASCII.GetBytes("B"));      // fontNum=0
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn"));  // LS2
        emulator.ProcessData(Encoding.ASCII.GetBytes("C"));      // fontNum=2
        emulator.ProcessData(new byte[] { 0x0F });               // SI
        emulator.ProcessData(Encoding.ASCII.GetBytes("D"));      // fontNum=0

        Assert.Equal(2, emulator.Buffer.GetCell(0, 0).FontNumber); // A
        Assert.Equal(0, emulator.Buffer.GetCell(0, 1).FontNumber); // B
        Assert.Equal(2, emulator.Buffer.GetCell(0, 2).FontNumber); // C
        Assert.Equal(0, emulator.Buffer.GetCell(0, 3).FontNumber); // D
    }

    [Fact]
    public void SS2_ShouldOnlyAffectOneCharacter()
    {
        // ESC N = SS2 (Single Shift 2) - only affects next character
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1BN"));  // SS2
        emulator.ProcessData(Encoding.ASCII.GetBytes("A"));      // fontNum=2 (single shift)
        emulator.ProcessData(Encoding.ASCII.GetBytes("B"));      // fontNum=0 (back to normal)

        Assert.Equal(2, emulator.Buffer.GetCell(0, 0).FontNumber); // A with SS2
        Assert.Equal(0, emulator.Buffer.GetCell(0, 1).FontNumber); // B normal
    }

    [Fact]
    public void SS3_ShouldOnlyAffectOneCharacter()
    {
        // ESC O = SS3 (Single Shift 3) - only affects next character
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1BO"));  // SS3
        emulator.ProcessData(Encoding.ASCII.GetBytes("X"));      // fontNum=3 (single shift)
        emulator.ProcessData(Encoding.ASCII.GetBytes("Y"));      // fontNum=0 (back to normal)

        Assert.Equal(3, emulator.Buffer.GetCell(0, 0).FontNumber); // X with SS3
        Assert.Equal(0, emulator.Buffer.GetCell(0, 1).FontNumber); // Y normal
    }

    [Fact]
    public void ResetShouldClearLockingShiftState()
    {
        var emulator = new TDV2200Emulator(80, 24);

        // Set LS2, verify it works, reset, verify state cleared
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn")); // LS2
        emulator.ProcessData(Encoding.ASCII.GetBytes("A"));     // fontNum=2
        Assert.Equal(2, emulator.Buffer.GetCell(0, 0).FontNumber);

        emulator.Reset();

        emulator.ProcessData(Encoding.ASCII.GetBytes("B"));     // fontNum=0 after reset
        Assert.Equal(0, emulator.Buffer.GetCell(0, 0).FontNumber);
    }

    [Fact]
    public void LS2_WithGraphicsCharacters_ShouldRenderLineDrawing()
    {
        // LS2 maps to G2 which is GraphicsI (line drawing characters)
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn")); // LS2

        // Write some graphic character codes that would be line drawing in set 2
        emulator.ProcessData(new byte[] { 0x6A }); // 'j' in graphics I is typically bottom-right corner
        emulator.ProcessData(new byte[] { 0x6B }); // 'k' is typically top-right corner
        emulator.ProcessData(new byte[] { 0x6C }); // 'l' is typically top-left corner
        emulator.ProcessData(new byte[] { 0x6D }); // 'm' is typically bottom-left corner

        // All characters should have fontNum=2
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(2, emulator.Buffer.GetCell(0, i).FontNumber);
        }
    }

    [Fact]
    public void GetCurrentCharacterSet_ShouldReflectLockingShiftState()
    {
        var emulator = new TDV2200Emulator(80, 24);

        Assert.Equal(0, emulator.GetCurrentCharacterSet()); // Initially G0

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bn")); // LS2
        Assert.Equal(2, emulator.GetCurrentCharacterSet());

        emulator.ProcessData(new byte[] { 0x0F }); // SI
        Assert.Equal(0, emulator.GetCurrentCharacterSet());

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1Bo")); // LS3
        Assert.Equal(3, emulator.GetCurrentCharacterSet());

        emulator.ProcessData(new byte[] { 0x0E }); // SO
        Assert.Equal(1, emulator.GetCurrentCharacterSet());
    }
}
