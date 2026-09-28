using System;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Tests for the Kermit Send-Init auto-detect scanner in TerminalSession.
/// Verifies that valid S-packets trigger the KermitSendInitDetected event
/// and invalid/other packets do not.
/// </summary>
public class KermitAutoDetectTests
{
    // Kermit protocol helpers (mirrors KermitConst which is internal to Kermit project)
    private static byte ToChar(int value) => (byte)(value + 32);
    private static int UnChar(int ch) => ch - 32;
    private static byte Ctl(int ch) => (byte)((ch ^ 64) & 0xFF);

    private static int ComputeType1Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
            sum += data[i];
        return (sum + ((sum & 0xC0) >> 6)) & 0x3F;
    }

    private TerminalSession CreateSession()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator);
        session.KermitAutoDetectEnabled = true;
        return session;
    }

    /// <summary>
    /// Build a realistic Kermit Send-Init packet with default parameters.
    /// Format: SOH LEN SEQ TYPE DATA... CHECK EOL
    /// </summary>
    private static byte[] BuildSendInitPacket()
    {
        // Send-Init parameters (default Kermit values)
        byte[] sData = new byte[]
        {
            ToChar(80),   // MAXL - max packet length
            ToChar(8),    // TIME - timeout
            ToChar(0),    // NPAD - padding count
            Ctl(0),       // PADC - padding char (ctl'd NUL = '@')
            ToChar(13),   // EOL  - end of line
            (byte)'#',    // QCTL - control quote
            (byte)'&',    // QBIN - 8-bit quote
            (byte)'1',    // CHKT - check type
            ToChar(0),    // REPT - repeat prefix (none)
        };

        // Build packet
        int checkLen = 1; // type 1 checksum
        byte lenByte = ToChar(sData.Length + 2 + checkLen); // data + SEQ + TYPE + CHECK
        byte seqByte = ToChar(0);
        byte typeByte = (byte)'S';

        // Assemble bytes to checksum: LEN, SEQ, TYPE, DATA...
        int checksumFieldLen = 1 + 1 + 1 + sData.Length; // LEN + SEQ + TYPE + DATA
        var checksumInput = new byte[checksumFieldLen];
        checksumInput[0] = lenByte;
        checksumInput[1] = seqByte;
        checksumInput[2] = typeByte;
        Array.Copy(sData, 0, checksumInput, 3, sData.Length);

        int chk = ComputeType1Checksum(checksumInput);
        byte checkByte = ToChar(chk);

        // Full packet: SOH + LEN + SEQ + TYPE + DATA + CHECK + EOL
        var packet = new byte[1 + checksumFieldLen + 1 + 1]; // SOH + fields + CHECK + EOL
        packet[0] = 0x01; // SOH
        Array.Copy(checksumInput, 0, packet, 1, checksumFieldLen);
        packet[1 + checksumFieldLen] = checkByte;
        packet[1 + checksumFieldLen + 1] = 0x0D; // EOL (CR)

        return packet;
    }

    /// <summary>
    /// Build a minimal valid S-packet (no data field).
    /// </summary>
    private static byte[] BuildMinimalSPacket()
    {
        byte lenByte = ToChar(3); // SEQ + TYPE + CHECK = 3
        byte seqByte = ToChar(0);
        byte typeByte = (byte)'S';

        var checksumInput = new byte[] { lenByte, seqByte, typeByte };
        int chk = ComputeType1Checksum(checksumInput);
        byte checkByte = ToChar(chk);

        return new byte[] { 0x01, lenByte, seqByte, typeByte, checkByte, 0x0D };
    }

    /// <summary>
    /// Build an ACK packet (type 'Y') - should NOT be detected.
    /// </summary>
    private static byte[] BuildAckPacket()
    {
        byte lenByte = ToChar(3);
        byte seqByte = ToChar(0);
        byte typeByte = (byte)'Y'; // ACK

        var checksumInput = new byte[] { lenByte, seqByte, typeByte };
        int chk = ComputeType1Checksum(checksumInput);
        byte checkByte = ToChar(chk);

        return new byte[] { 0x01, lenByte, seqByte, typeByte, checkByte, 0x0D };
    }

    [Fact]
    public void ValidSendInitPacket_ShouldTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        var packet = BuildSendInitPacket();
        session.ScanForKermitSendInit(packet);

        Assert.True(detected, "Valid Send-Init packet should trigger detection");
    }

    [Fact]
    public void MinimalSPacket_ShouldTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        var packet = BuildMinimalSPacket();
        session.ScanForKermitSendInit(packet);

        Assert.True(detected, "Minimal valid S-packet should trigger detection");
    }

    [Fact]
    public void SPacketWithLeadingGarbage_ShouldTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        var packet = BuildSendInitPacket();
        var data = new byte[15 + packet.Length];
        // Fill garbage before packet
        for (int i = 0; i < 15; i++) data[i] = (byte)(0x41 + i);
        Array.Copy(packet, 0, data, 15, packet.Length);

        session.ScanForKermitSendInit(data);

        Assert.True(detected, "S-packet after leading garbage should still be detected");
    }

    [Fact]
    public void AckPacket_ShouldNotTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        var packet = BuildAckPacket();
        session.ScanForKermitSendInit(packet);

        Assert.False(detected, "ACK packet should NOT trigger detection");
    }

    [Fact]
    public void CorruptedChecksum_ShouldNotTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        var packet = BuildSendInitPacket();
        // Corrupt the checksum (second to last byte, before EOL)
        packet[packet.Length - 2] ^= 0x07;

        session.ScanForKermitSendInit(packet);

        Assert.False(detected, "S-packet with corrupted checksum should NOT trigger detection");
    }

    [Fact]
    public void RandomData_ShouldNotTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64 };
        session.ScanForKermitSendInit(data);

        Assert.False(detected, "Random data should NOT trigger detection");
    }

    [Fact]
    public void EmptyData_ShouldNotTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        session.ScanForKermitSendInit(ReadOnlySpan<byte>.Empty);

        Assert.False(detected, "Empty data should NOT trigger detection");
    }

    [Fact]
    public void LoneSOH_ShouldNotTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        session.ScanForKermitSendInit(new byte[] { 0x01 });

        Assert.False(detected, "Lone SOH byte should NOT trigger detection");
    }

    [Fact]
    public void SOHWithTruncatedPacket_ShouldNotTriggerDetection()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        // SOH + LEN but not enough data for full packet
        session.ScanForKermitSendInit(new byte[] { 0x01, ToChar(20), ToChar(0) });

        Assert.False(detected, "Truncated packet should NOT trigger detection");
    }

    [Fact]
    public void ValidSPacket_VerifyPacketBytes()
    {
        // Verify our test packet builder produces valid Kermit packets
        var packet = BuildSendInitPacket();

        Assert.Equal(0x01, packet[0]); // SOH
        Assert.Equal((byte)'S', packet[3]); // TYPE

        int lenField = UnChar(packet[1]);
        Assert.True(lenField >= 3, "LEN field should be at least 3");

        // Verify total length: SOH(1) + LEN(1) + lenField bytes + EOL(1)
        Assert.Equal(1 + 1 + lenField + 1, packet.Length);
        Assert.Equal(0x0D, packet[packet.Length - 1]); // EOL
    }

    [Fact]
    public void ValidSPacket_MultipleSOHInData_DetectsCorrectOne()
    {
        var session = CreateSession();
        bool detected = false;
        session.KermitSendInitDetected += () => detected = true;

        // Put a fake SOH followed by garbage, then the real packet
        var packet = BuildSendInitPacket();
        var data = new byte[5 + packet.Length];
        data[0] = 0x01; // Fake SOH
        data[1] = 0x00; // Invalid LEN (underflows when unchar'd)
        data[2] = 0x00;
        data[3] = 0x00;
        data[4] = 0x00;
        Array.Copy(packet, 0, data, 5, packet.Length);

        session.ScanForKermitSendInit(data);

        Assert.True(detected, "Should skip invalid SOH and find the valid S-packet");
    }
}
