using System.Text;

namespace RetroTerm.Core.Protocols.Kermit.Tests;

/// <summary>
/// Tests for Kermit protocol communication over 7-bit channels.
/// Validates that 8th-bit quoting correctly enables binary file transfers
/// when the transport path strips or corrupts the high bit.
///
/// These tests simulate the real-world scenario of communicating with
/// nd-kermit (Kermit-ND) on an ND-100 over a 7-bit even parity serial link.
///
/// Three configurations tested:
/// 1. Both sides use Parity=Even (engine manages parity bits)
/// 2. Both sides use Force8BitQuoting (external transport manages parity)
/// 3. Simulated 7-bit wire: a transport layer that strips bit 7 from all bytes
/// </summary>
public class Kermit7BitChannelTests
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

    /// <summary>
    /// Strips bit 7 from every byte, simulating a 7-bit serial link.
    /// </summary>
    private static byte[] StripHighBits(ReadOnlyMemory<byte> data)
    {
        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data.Span[i] & 0x7F);
        return result;
    }

    /// <summary>
    /// Runs exchange loop between two engines. Events must already be wired
    /// and BeginSend/BeginReceive already called before this.
    /// </summary>
    private static void RunExchangeLoop(
        KermitEngine a, KermitEngine b,
        List<byte> aOutput, List<byte> bOutput,
        int maxRounds = 500)
    {
        int round = 0;
        while (round++ < maxRounds)
        {
            if (aOutput.Count > 0)
            {
                byte[] bytes = aOutput.ToArray();
                aOutput.Clear();
                b.ProcessReceivedData(bytes);
            }

            if (bOutput.Count > 0)
            {
                byte[] bytes = bOutput.ToArray();
                bOutput.Clear();
                a.ProcessReceivedData(bytes);
            }

            if ((a.State == KermitState.Completed || a.State == KermitState.Failed) &&
                (b.State == KermitState.Completed || b.State == KermitState.Failed))
                break;

            if (aOutput.Count == 0 && bOutput.Count == 0 && round > 2)
                break;
        }
    }

    /// <summary>
    /// Sets up a complete transfer: creates engines, wires events (with optional
    /// 7-bit simulation), starts sender and receiver, runs to completion.
    /// </summary>
    private static (KermitEngine sender, KermitEngine receiver, MemoryFileHandler receiverFiles, List<byte>? senderWireBytes)
        SetupAndRun(
            string fileName, byte[] content,
            KermitOptions senderOpts, KermitOptions receiverOpts,
            bool simulate7BitWire, bool captureWireBytes = false,
            bool useServerMode = false, int maxRounds = 500)
    {
        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile(fileName, content);
        var receiverFiles = new MemoryFileHandler();

        var sender = new KermitEngine(senderOpts, senderFiles);
        var receiver = new KermitEngine(receiverOpts, receiverFiles);

        List<byte> senderOutput = new();
        List<byte> receiverOutput = new();
        List<byte>? wireCapture = captureWireBytes ? new() : null;

        if (simulate7BitWire)
        {
            sender.DataToSend += data =>
            {
                wireCapture?.AddRange(data.ToArray());
                senderOutput.AddRange(StripHighBits(data));
            };
            receiver.DataToSend += data => receiverOutput.AddRange(StripHighBits(data));
        }
        else
        {
            sender.DataToSend += data =>
            {
                wireCapture?.AddRange(data.ToArray());
                senderOutput.AddRange(data.ToArray());
            };
            receiver.DataToSend += data => receiverOutput.AddRange(data.ToArray());
        }

        // Start transfer AFTER events are wired
        if (useServerMode)
            receiver.BeginServe();
        else
            receiver.BeginReceive();
        sender.BeginSend(fileName);

        RunExchangeLoop(sender, receiver, senderOutput, receiverOutput, maxRounds);

        return (sender, receiver, receiverFiles, wireCapture);
    }

    // ================================================================
    // Scenario 1: Both sides use Parity=Even
    // Engine adds even parity on send, strips on receive.
    // ================================================================

    [Fact]
    public void ParityEven_AsciiText_TransfersCorrectly()
    {
        byte[] content = Encoding.ASCII.GetBytes("Hello from ND-100!");
        var opts = new KermitOptions { Parity = ParityMode.Even };

        var (sender, receiver, files, _) = SetupAndRun(
            "hello.txt", content, opts, opts, simulate7BitWire: false);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("hello.txt"));
    }

    [Fact]
    public void ParityEven_BinaryData_AllByteValues_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { Parity = ParityMode.Even };

        var (sender, receiver, files, _) = SetupAndRun(
            "all256.bin", content, opts, opts, simulate7BitWire: false);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("all256.bin"));
    }

    // ================================================================
    // Scenario 2: Force8BitQuoting only (no parity manipulation)
    // Matches nd-kermit's SET USE-8-BIT-QUOTE command.
    // ================================================================

    [Fact]
    public void Force8BitQuoting_AsciiText_TransfersCorrectly()
    {
        byte[] content = Encoding.ASCII.GetBytes("SINTRAN III file transfer test");
        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "sintran.txt", content, opts, opts, simulate7BitWire: false);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("sintran.txt"));
    }

    [Fact]
    public void Force8BitQuoting_BinaryData_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "binary.bin", content, opts, opts, simulate7BitWire: false);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("binary.bin"));
    }

    // ================================================================
    // Scenario 3: Simulated 7-bit wire (strips bit 7 on all bytes)
    // Most realistic test of nd-kermit over a real 7-bit serial line.
    // ================================================================

    [Fact]
    public void Simulated7BitWire_AsciiText_TransfersCorrectly()
    {
        byte[] content = Encoding.ASCII.GetBytes("Hello ND-100 via 7-bit link!");
        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "test7bit.txt", content, opts, opts, simulate7BitWire: true);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("test7bit.txt"));
    }

    [Fact]
    public void Simulated7BitWire_BinaryData_With8BitQuoting_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "binary7.bin", content, opts, opts, simulate7BitWire: true);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("binary7.bin"));
    }

    [Fact]
    public void Simulated7BitWire_LargerBinaryFile_TransfersCorrectly()
    {
        byte[] content = new byte[2048];
        for (int i = 0; i < content.Length; i++) content[i] = (byte)(i % 256);

        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "large7.bin", content, opts, opts, simulate7BitWire: true, maxRounds: 1000);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        byte[]? received = files.GetWrittenFile("large7.bin");
        Assert.NotNull(received);
        Assert.Equal(content.Length, received.Length);
        Assert.Equal(content, received);
    }

    [Fact]
    public void Simulated7BitWire_WorstCaseBinary_AllHighBytes_TransfersCorrectly()
    {
        // Every byte has high bit set — worst case for quoting expansion
        byte[] content = new byte[128];
        for (int i = 0; i < 128; i++) content[i] = (byte)(i | 0x80);

        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "allhigh.bin", content, opts, opts, simulate7BitWire: true, maxRounds: 1000);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("allhigh.bin"));
    }

    // ================================================================
    // Scenario 4: Asymmetric — sender enables 8-bit quoting, receiver doesn't.
    // ASCII data should still work (no high bytes to quote).
    // ================================================================

    [Fact]
    public void AsymmetricNegotiation_SenderWants8Bit_ReceiverDoesNot_AsciiSurvives()
    {
        byte[] content = Encoding.ASCII.GetBytes("Plain ASCII works either way");
        var senderOpts = new KermitOptions { Force8BitQuoting = true };
        var receiverOpts = new KermitOptions(); // no 8-bit quoting

        var (sender, receiver, files, _) = SetupAndRun(
            "ascii.txt", content, senderOpts, receiverOpts, simulate7BitWire: false);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("ascii.txt"));
    }

    // ================================================================
    // Scenario 5: Server mode over 7-bit channel
    // ================================================================

    [Fact]
    public void ServerMode_7BitWire_BinaryTransfer_TransfersCorrectly()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, server, files, _) = SetupAndRun(
            "ndfile.dat", content, opts, opts, simulate7BitWire: true, useServerMode: true);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, server.State);
        Assert.Equal(content, files.GetWrittenFile("ndfile.dat"));
    }

    // ================================================================
    // Scenario 6: Verify wire bytes are 7-bit clean when quoting is on
    // ================================================================

    [Fact]
    public void Force8BitQuoting_AllPacketBytesAre7BitClean()
    {
        byte[] content = new byte[256];
        for (int i = 0; i < 256; i++) content[i] = (byte)i;

        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, _, wireBytes) = SetupAndRun(
            "verify.bin", content, opts, opts,
            simulate7BitWire: false, captureWireBytes: true);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.NotNull(wireBytes);

        for (int i = 0; i < wireBytes.Count; i++)
        {
            Assert.True((wireBytes[i] & 0x80) == 0,
                $"Sender byte at offset {i} has high bit set: 0x{wireBytes[i]:X2}. " +
                $"All wire bytes must be 7-bit clean when 8th-bit quoting is active.");
        }
    }

    // ================================================================
    // Scenario 7: nd-kermit-like patterns
    // ================================================================

    [Fact]
    public void NdKermitPattern_SintranFilename_TransfersCorrectly()
    {
        byte[] content = Encoding.ASCII.GetBytes(
            "This file was sent from (PACK-1:USER)TESTFILE:BPUN;1 on SINTRAN III");
        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "TESTFILE.BPUN", content, opts, opts, simulate7BitWire: true);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        Assert.Equal(content, files.GetWrittenFile("TESTFILE.BPUN"));
    }

    [Fact]
    public void NdKermitPattern_BinaryProgram_TransfersCorrectly()
    {
        var rng = new Random(42);
        byte[] content = new byte[1500];
        rng.NextBytes(content);

        var opts = new KermitOptions { Force8BitQuoting = true };

        var (sender, receiver, files, _) = SetupAndRun(
            "PROGRAM.BPUN", content, opts, opts,
            simulate7BitWire: true, maxRounds: 2000);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);
        byte[]? received = files.GetWrittenFile("PROGRAM.BPUN");
        Assert.NotNull(received);
        Assert.Equal(content.Length, received.Length);
        Assert.Equal(content, received);
    }
}
