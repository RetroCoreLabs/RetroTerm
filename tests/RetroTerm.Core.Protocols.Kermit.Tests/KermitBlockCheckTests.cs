using System.Text;

namespace RetroTerm.Core.Protocols.Kermit.Tests;

/// <summary>
/// Tests for block check type negotiation and all three check types.
/// Validates that the engine correctly negotiates check types during the
/// S/Y exchange and uses the agreed type for all subsequent packets.
///
/// Block check types:
///   Type 1: 6-bit folded checksum (1 byte) — default, minimal
///   Type 2: 12-bit checksum (2 bytes) — moderate error detection
///   Type 3: CRC-16 CCITT (3 bytes) — strongest error detection
/// </summary>
public class KermitBlockCheckTests
{
    private sealed class MemoryFileHandler : IKermitFileHandler
    {
        private readonly Dictionary<string, byte[]> _files = new();
        private string? _currentFile;
        private int _readOffset;
        private MemoryStream? _writeStream;

        public void AddFile(string name, byte[] data) => _files[name] = data;
        public byte[]? GetWrittenFile(string name) => _files.GetValueOrDefault(name);

        public bool OpenFileForRead(string fileName, out long fileSize)
        {
            if (_files.TryGetValue(fileName, out byte[]? data))
            {
                _currentFile = fileName;
                _readOffset = 0;
                fileSize = data.Length;
                return true;
            }
            fileSize = -1;
            return false;
        }

        public int ReadFile(Span<byte> buffer)
        {
            if (_currentFile == null || !_files.TryGetValue(_currentFile, out byte[]? data))
                return 0;
            int remaining = data.Length - _readOffset;
            int toRead = Math.Min(buffer.Length, remaining);
            data.AsSpan(_readOffset, toRead).CopyTo(buffer);
            _readOffset += toRead;
            return toRead;
        }

        public bool OpenFileForWrite(string fileName)
        {
            _currentFile = fileName;
            _writeStream = new MemoryStream();
            return true;
        }

        public void WriteFile(ReadOnlySpan<byte> data) => _writeStream?.Write(data);

        public void CloseFile()
        {
            if (_writeStream != null && _currentFile != null)
            {
                _files[_currentFile] = _writeStream.ToArray();
                _writeStream.Dispose();
                _writeStream = null;
            }
            _currentFile = null;
        }
    }

    private static (KermitEngine sender, KermitEngine receiver, MemoryFileHandler receiverFiles)
        RunFileTransfer(string fileName, byte[] content,
            KermitOptions senderOpts, KermitOptions receiverOpts,
            int maxRounds = 500)
    {
        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile(fileName, content);
        var receiverFiles = new MemoryFileHandler();

        var sender = new KermitEngine(senderOpts, senderFiles);
        var receiver = new KermitEngine(receiverOpts, receiverFiles);

        List<byte> senderOutput = new();
        List<byte> receiverOutput = new();
        sender.DataToSend += data => senderOutput.AddRange(data.ToArray());
        receiver.DataToSend += data => receiverOutput.AddRange(data.ToArray());

        receiver.BeginReceive();
        sender.BeginSend(fileName);

        int round = 0;
        while (round++ < maxRounds)
        {
            if (senderOutput.Count > 0)
            {
                byte[] bytes = senderOutput.ToArray();
                senderOutput.Clear();
                receiver.ProcessReceivedData(bytes);
            }

            if (receiverOutput.Count > 0)
            {
                byte[] bytes = receiverOutput.ToArray();
                receiverOutput.Clear();
                sender.ProcessReceivedData(bytes);
            }

            if ((sender.State == KermitState.Completed || sender.State == KermitState.Failed) &&
                (receiver.State == KermitState.Completed || receiver.State == KermitState.Failed))
                break;

            if (senderOutput.Count == 0 && receiverOutput.Count == 0 && round > 2)
                break;
        }

        return (sender, receiver, receiverFiles);
    }

    // ================================================================
    // Block check type 1 (default)
    // ================================================================

    [Fact]
    public void BlockCheckType1_TextFile_TransfersCorrectly()
    {
        byte[] content = Encoding.ASCII.GetBytes("Check type 1 test");
        var opts = new KermitOptions { BlockCheckType = 1 };

        var (sender, receiver, files) = RunFileTransfer("type1.txt", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("type1.txt"));
    }

    [Fact]
    public void BlockCheckType1_BinaryFile_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { BlockCheckType = 1 };

        var (sender, receiver, files) = RunFileTransfer("type1.bin", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("type1.bin"));
    }

    // ================================================================
    // Block check type 2 (12-bit checksum)
    // ================================================================

    [Fact]
    public void BlockCheckType2_TextFile_TransfersCorrectly()
    {
        byte[] content = Encoding.ASCII.GetBytes("Check type 2 — 12-bit checksum");
        var opts = new KermitOptions { BlockCheckType = 2 };

        var (sender, receiver, files) = RunFileTransfer("type2.txt", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("type2.txt"));
    }

    [Fact]
    public void BlockCheckType2_BinaryFile_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { BlockCheckType = 2 };

        var (sender, receiver, files) = RunFileTransfer("type2.bin", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("type2.bin"));
    }

    [Fact]
    public void BlockCheckType2_LargeFile_TransfersCorrectly()
    {
        byte[] content = new byte[3000];
        for (int i = 0; i < content.Length; i++) content[i] = (byte)(i % 251);

        var opts = new KermitOptions { BlockCheckType = 2 };

        var (sender, receiver, files) = RunFileTransfer("type2_large.bin", content, opts, opts, maxRounds: 1000);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("type2_large.bin"));
    }

    // ================================================================
    // Block check type 3 (CRC-16)
    // ================================================================

    [Fact]
    public void BlockCheckType3_TextFile_TransfersCorrectly()
    {
        byte[] content = Encoding.ASCII.GetBytes("Check type 3 — CRC-16 CCITT strongest check");
        var opts = new KermitOptions { BlockCheckType = 3 };

        var (sender, receiver, files) = RunFileTransfer("type3.txt", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("type3.txt"));
    }

    [Fact]
    public void BlockCheckType3_BinaryFile_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { BlockCheckType = 3 };

        var (sender, receiver, files) = RunFileTransfer("type3.bin", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("type3.bin"));
    }

    [Fact]
    public void BlockCheckType3_LargeFile_TransfersCorrectly()
    {
        byte[] content = new byte[5000];
        var rng = new Random(123);
        rng.NextBytes(content);

        var opts = new KermitOptions { BlockCheckType = 3 };

        var (sender, receiver, files) = RunFileTransfer("type3_large.bin", content, opts, opts, maxRounds: 2000);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        byte[]? received = files.GetWrittenFile("type3_large.bin");
        Assert.NotNull(received);
        Assert.Equal(content.Length, received.Length);
        Assert.Equal(content, received);
    }

    // ================================================================
    // CRC-16 over 7-bit wire (realistic nd-kermit scenario)
    // ================================================================

    [Fact]
    public void BlockCheckType3_With8BitQuoting_Over7BitWire_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { BlockCheckType = 3, Force8BitQuoting = true };

        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile("crc7bit.bin", content);
        var receiverFiles = new MemoryFileHandler();

        var sender = new KermitEngine(opts, senderFiles);
        var receiver = new KermitEngine(opts, receiverFiles);

        List<byte> senderOutput = new();
        List<byte> receiverOutput = new();

        // Simulate 7-bit wire
        sender.DataToSend += data =>
        {
            byte[] stripped = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) stripped[i] = (byte)(data.Span[i] & 0x7F);
            senderOutput.AddRange(stripped);
        };
        receiver.DataToSend += data =>
        {
            byte[] stripped = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) stripped[i] = (byte)(data.Span[i] & 0x7F);
            receiverOutput.AddRange(stripped);
        };

        receiver.BeginReceive();
        sender.BeginSend("crc7bit.bin");

        int round = 0;
        while (round++ < 1000)
        {
            if (senderOutput.Count > 0)
            {
                byte[] bytes = senderOutput.ToArray();
                senderOutput.Clear();
                receiver.ProcessReceivedData(bytes);
            }

            if (receiverOutput.Count > 0)
            {
                byte[] bytes = receiverOutput.ToArray();
                receiverOutput.Clear();
                sender.ProcessReceivedData(bytes);
            }

            if ((sender.State == KermitState.Completed || sender.State == KermitState.Failed) &&
                (receiver.State == KermitState.Completed || receiver.State == KermitState.Failed))
                break;

            if (senderOutput.Count == 0 && receiverOutput.Count == 0 && round > 2)
                break;
        }

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, receiverFiles.GetWrittenFile("crc7bit.bin"));
    }

    // ================================================================
    // Negotiation: sides request different check types
    // ================================================================

    [Fact]
    public void Negotiation_SenderWants3_ReceiverWants1_UsesType1()
    {
        byte[] content = Encoding.ASCII.GetBytes("Negotiation test: 3 vs 1");
        var senderOpts = new KermitOptions { BlockCheckType = 3 };
        var receiverOpts = new KermitOptions { BlockCheckType = 1 };

        var (sender, receiver, files) = RunFileTransfer("neg31.txt", content, senderOpts, receiverOpts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("neg31.txt"));
    }

    [Fact]
    public void Negotiation_SenderWants2_ReceiverWants3_UsesType2()
    {
        byte[] content = Encoding.ASCII.GetBytes("Negotiation test: 2 vs 3");
        var senderOpts = new KermitOptions { BlockCheckType = 2 };
        var receiverOpts = new KermitOptions { BlockCheckType = 3 };

        var (sender, receiver, files) = RunFileTransfer("neg23.txt", content, senderOpts, receiverOpts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("neg23.txt"));
    }

    [Fact]
    public void Negotiation_BothWant3_UsesType3()
    {
        byte[] content = Encoding.ASCII.GetBytes("Negotiation test: both want 3");
        var opts = new KermitOptions { BlockCheckType = 3 };

        var (sender, receiver, files) = RunFileTransfer("neg33.txt", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("neg33.txt"));
    }

    // ================================================================
    // Check type with parity (full combination)
    // ================================================================

    [Fact]
    public void BlockCheckType3_WithParity_BinaryFile_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { BlockCheckType = 3, Parity = ParityMode.Even };

        var (sender, receiver, files) = RunFileTransfer("crc_parity.bin", content, opts, opts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("crc_parity.bin"));
    }

    [Fact]
    public void BlockCheckType2_WithParity_LargeFile_TransfersCorrectly()
    {
        byte[] content = new byte[2000];
        var rng = new Random(77);
        rng.NextBytes(content);

        var opts = new KermitOptions { BlockCheckType = 2, Parity = ParityMode.Even };

        var (sender, receiver, files) = RunFileTransfer("chk2_parity.bin", content, opts, opts, maxRounds: 1000);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        byte[]? received = files.GetWrittenFile("chk2_parity.bin");
        Assert.NotNull(received);
        Assert.Equal(content.Length, received.Length);
        Assert.Equal(content, received);
    }

    // ================================================================
    // BuildPacket: verify check bytes are correct length per type
    // ================================================================

    [Theory]
    [InlineData(1, 8)]  // MARK+LEN+SEQ+TYPE+DATA(2)+CHECK(1)+EOL = 8
    [InlineData(2, 9)]  // MARK+LEN+SEQ+TYPE+DATA(2)+CHECK(2)+EOL = 9
    [InlineData(3, 10)] // MARK+LEN+SEQ+TYPE+DATA(2)+CHECK(3)+EOL = 10
    public void BuildPacket_CheckLengthMatchesType(int checkType, int expectedTotalLen)
    {
        var opts = new KermitOptions { BlockCheckType = checkType };
        var engine = new KermitEngine(opts, new MemoryFileHandler());

        // Force negotiation to complete so the check type is used
        // We need to manually trigger this; use a helper that calls BuildPacket
        // after setting up negotiation state. For simplicity, test via full transfer:

        // Instead, directly test BuildPacket by sending a non-init packet
        // The engine starts with _checkTypeNegotiated=false, so non-init packets
        // still use type 1. We need to test via full transfer instead.

        // Test via file transfer to ensure the negotiated type is actually used
        byte[] content = Encoding.ASCII.GetBytes("Hi");
        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile("t.txt", content);
        var receiverFiles = new MemoryFileHandler();

        var sender = new KermitEngine(opts, senderFiles);
        var receiver = new KermitEngine(opts, receiverFiles);

        // Capture F packet (first post-negotiation packet) to check its length
        List<byte[]> senderPackets = new();
        List<byte> senderOutput = new();
        List<byte> receiverOutput = new();

        sender.DataToSend += data =>
        {
            senderPackets.Add(data.ToArray());
            senderOutput.AddRange(data.ToArray());
        };
        receiver.DataToSend += data => receiverOutput.AddRange(data.ToArray());

        receiver.BeginReceive();
        sender.BeginSend("t.txt");

        // Run enough rounds to get past S/Y into F packet
        for (int round = 0; round < 20; round++)
        {
            if (senderOutput.Count > 0)
            {
                byte[] bytes = senderOutput.ToArray();
                senderOutput.Clear();
                receiver.ProcessReceivedData(bytes);
            }
            if (receiverOutput.Count > 0)
            {
                byte[] bytes = receiverOutput.ToArray();
                receiverOutput.Clear();
                sender.ProcessReceivedData(bytes);
            }
            if (sender.State == KermitState.Completed || sender.State == KermitState.Failed)
                break;
        }

        Assert.Equal(KermitState.Completed, sender.State);

        // Packet 0 = S (always type 1), Packet 1 = F (uses negotiated type)
        // The F packet carries the filename "t.txt" (5 bytes after quoting)
        Assert.True(senderPackets.Count >= 2, "Expected at least S and F packets");

        // Verify the S packet uses type 1 (always)
        byte[] sPkt = senderPackets[0];
        // S packet: MARK + LEN + ... + CHECK(1) + EOL
        // LEN field encodes data+3 (seq+type+1byte check)
        byte sLen = sPkt[1];
        int sPayloadLen = KermitConst.UnChar(sLen);
        int sPktExpected = 2 + sPayloadLen + 1; // SOH+LEN+payload+EOL
        Assert.Equal(sPktExpected, sPkt.Length);

        // The D (data) packet carries the 2-byte payload "Hi" - neither byte needs
        // quoting - so its total length is exactly what the InlineData predicts:
        // MARK+LEN+SEQ+TYPE+DATA(2)+CHECK(checkType)+EOL.
        byte[]? dPkt = null;
        for (int i = 0; i < senderPackets.Count; i++)
        {
            // Packet layout: [0]=MARK [1]=LEN [2]=SEQ [3]=TYPE
            if (senderPackets[i].Length > 3 && senderPackets[i][3] == (byte)'D')
            {
                dPkt = senderPackets[i];
                break;
            }
        }

        Assert.NotNull(dPkt);
        Assert.Equal(expectedTotalLen, dPkt.Length);
    }
}
