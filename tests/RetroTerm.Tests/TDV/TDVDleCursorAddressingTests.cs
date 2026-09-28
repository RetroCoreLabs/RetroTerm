using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Regression tests for DLE (0x10) cursor addressing in TDV-NATIVE mode.
/// </summary>
/// <remarks>
/// <para>
/// Bug: DLE decoding existed but was only reachable from
/// TDV2115CompatibilityHandler.ProcessTDV2115ControlCharacter, which returns early when 2115
/// compatibility mode is off. SINTRAN terminal type 53 (TDV-2200/9) drives the terminal in
/// native mode with 2115 mode OFF, so cursor addressing was dropped and the two coordinate
/// bytes were painted on screen as high-bit garbage.
/// </para>
/// <para>
/// The byte sequences below are taken verbatim from the live RX capture in the bug report,
/// together with the ANSI CSI sequence the same program emits for the same screen position
/// when running in terminal type 6/93.
/// </para>
/// </remarks>
public class TDVDleCursorAddressingTests
{
    private static TDV2200Emulator CreateEmulator()
    {
        // 80x25, the geometry SINTRAN type 53 assumes.
        return new TDV2200Emulator(80, 25);
    }

    private static void Send(TDV2200Emulator emulator, params int[] bytes)
    {
        var data = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            data[i] = (byte)bytes[i];

        emulator.ProcessData(new ReadOnlySpan<byte>(data));
    }

    // ---- The four captured positions, cross-checked against their ANSI equivalents ----

    [Theory]
    [InlineData(0x80, 0x80, 0, 0)]   // DLE 80 80 -> row 1, col 1   (ESC[1;1H)
    [InlineData(0x83, 0x82, 3, 2)]   // DLE 83 82 -> row 4, col 3   (ESC[4;3H)
    [InlineData(0x84, 0x82, 4, 2)]   // DLE 84 82 -> row 5, col 3   (ESC[5;3H)
    [InlineData(0x85, 0x82, 5, 2)]   // DLE 85 82 -> row 6, col 3   (ESC[6;3H)
    public void DLE_InNativeMode_PositionsCursor(int rowByte, int colByte, int expectedRow0, int expectedCol0)
    {
        var emulator = CreateEmulator();

        // Park the cursor somewhere else so a no-op cannot produce a false pass.
        Send(emulator, 0x10, 0x8A, 0x8A);

        Send(emulator, 0x10, rowByte, colByte);

        Assert.Equal(expectedRow0, emulator.Cursor.Row);
        Assert.Equal(expectedCol0, emulator.Cursor.Column);
    }

    [Fact]
    public void DLE_MatchesTheEquivalentAnsiCupSequence()
    {
        // The core claim of the bug report: DLE 83 82 must land where ESC[4;3H lands.
        var viaDle = CreateEmulator();
        Send(viaDle, 0x10, 0x83, 0x82);

        var viaAnsi = CreateEmulator();
        var ansi = Encoding.ASCII.GetBytes("\x1B[4;3H");
        viaAnsi.ProcessData(new ReadOnlySpan<byte>(ansi));

        Assert.Equal(viaAnsi.Cursor.Row, viaDle.Cursor.Row);
        Assert.Equal(viaAnsi.Cursor.Column, viaDle.Cursor.Column);
    }

    // ---- The actual user-visible symptom ----

    [Fact]
    public void DLE_CoordinateBytes_AreNotPaintedAsText()
    {
        // Before the fix, 0x83 and 0x82 were rendered as characters, producing the
        // "garbled boxes" on screen.
        var emulator = CreateEmulator();

        Send(emulator, 0x10, 0x83, 0x82);

        // Nothing should have been written anywhere on the first rows.
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 80; col++)
            {
                uint codepoint = emulator.Buffer.GetCell(row, col).Codepoint;
                Assert.True(codepoint == ' ' || codepoint == 0,
                    $"Cell ({row},{col}) holds codepoint 0x{codepoint:X2} - DLE coordinate bytes leaked into the buffer");
            }
        }
    }

    [Fact]
    public void DLE_ThenText_WritesAtTheAddressedPosition()
    {
        // The linker writes its label immediately after positioning.
        var emulator = CreateEmulator();

        Send(emulator, 0x10, 0x83, 0x82);
        var text = Encoding.ASCII.GetBytes("LINKER:INIT");
        emulator.ProcessData(new ReadOnlySpan<byte>(text));

        // Row 4 (1-based) = index 3, starting at column 3 (1-based) = index 2.
        Assert.Equal((uint)'L', emulator.Buffer.GetCell(3, 2).Codepoint);
        Assert.Equal((uint)'I', emulator.Buffer.GetCell(3, 3).Codepoint);
        Assert.Equal((uint)'N', emulator.Buffer.GetCell(3, 4).Codepoint);
    }

    // ---- Split across packets, which the real capture does ----

    [Fact]
    public void DLE_SplitAcrossProcessDataCalls_StillPositionsCursor()
    {
        var emulator = CreateEmulator();

        Send(emulator, 0x10);
        Send(emulator, 0x83);
        Send(emulator, 0x82);

        Assert.Equal(3, emulator.Cursor.Row);
        Assert.Equal(2, emulator.Cursor.Column);
    }

    // ---- The unbiased encoding must keep working (2115-style coordinates) ----

    [Fact]
    public void DLE_UnbiasedCoordinates_StillWork()
    {
        // Masking means raw 0-based coordinates decode identically. This is the encoding
        // asserted by the pre-existing DisplayValidationTests 2115-mode test.
        var emulator = CreateEmulator();

        Send(emulator, 0x10, 0x05, 0x0A);

        Assert.Equal(5, emulator.Cursor.Row);
        Assert.Equal(10, emulator.Cursor.Column);
    }

    // ---- Bounds ----

    [Fact]
    public void DLE_Home_GoesToTopLeft()
    {
        var emulator = CreateEmulator();

        Send(emulator, 0x10, 0x8A, 0x8A);
        Send(emulator, 0x10, 0x80, 0x80);

        Assert.Equal(0, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    [Fact]
    public void DLE_LastAddressableCell_IsReachable()
    {
        var emulator = CreateEmulator();

        // Row 25 -> 0x7F + 25 = 0x98, column 80 -> 0x7F + 80 = 0xCF.
        Send(emulator, 0x10, 0x98, 0xCF);

        Assert.Equal(24, emulator.Cursor.Row);
        Assert.Equal(79, emulator.Cursor.Column);
    }
}
