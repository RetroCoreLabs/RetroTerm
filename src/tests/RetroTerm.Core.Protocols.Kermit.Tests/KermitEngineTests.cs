using System.Text;

namespace RetroTerm.Core.Protocols.Kermit.Tests;

/// <summary>
/// Integration tests for the Kermit protocol engine.
/// Tests the full send/receive flow by wiring two engines together
/// (one sender, one receiver) with data flowing between them in memory.
/// </summary>
public class KermitEngineTests
{
    /// <summary>
    /// Simple in-memory file handler for testing.
    /// </summary>
    private sealed class MemoryFileHandler : IKermitFileHandler
    {
        private readonly Dictionary<string, byte[]> _files = new();
        private string? _currentFile;
        private int _readOffset;
        private MemoryStream? _writeStream;

        public void AddFile(string name, byte[] data)
        {
            _files[name] = data;
        }

        public byte[]? GetWrittenFile(string name)
        {
            return _files.GetValueOrDefault(name);
        }

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

        public void WriteFile(ReadOnlySpan<byte> data)
        {
            _writeStream?.Write(data);
        }

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
    /// Wires two engines together and exchanges packets until both are done.
    /// Events MUST be wired before calling BeginSend/BeginReceive, so this
    /// method takes output lists that should be passed to the event handlers
    /// before initiating the transfer.
    /// </summary>
    private static void RunExchangeLoop(
        KermitEngine sender, KermitEngine receiver,
        List<byte> senderOutput, List<byte> receiverOutput,
        int maxRounds = 200)
    {
        int round = 0;
        while (round < maxRounds)
        {
            round++;

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
            {
                break;
            }

            if (senderOutput.Count == 0 && receiverOutput.Count == 0)
            {
                if (round > 2) break;
            }
        }
    }

    /// <summary>
    /// Helper: creates engines, wires events, starts transfer, runs to completion.
    /// </summary>
    private static (KermitEngine sender, KermitEngine receiver, MemoryFileHandler receiverFiles)
        RunFileTransfer(string fileName, byte[] fileContent, KermitOptions? senderOpts = null, KermitOptions? receiverOpts = null, int maxRounds = 200)
    {
        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile(fileName, fileContent);

        var receiverFiles = new MemoryFileHandler();

        var sender = new KermitEngine(senderOpts ?? new KermitOptions(), senderFiles);
        var receiver = new KermitEngine(receiverOpts ?? new KermitOptions(), receiverFiles);

        // Wire events BEFORE initiating transfer
        List<byte> senderOutput = new();
        List<byte> receiverOutput = new();
        sender.DataToSend += data => senderOutput.AddRange(data.ToArray());
        receiver.DataToSend += data => receiverOutput.AddRange(data.ToArray());

        // Start the transfer
        receiver.BeginReceive();
        sender.BeginSend(fileName);

        // Run the exchange
        RunExchangeLoop(sender, receiver, senderOutput, receiverOutput, maxRounds);

        return (sender, receiver, receiverFiles);
    }

    /// <summary>
    /// Helper for server mode: sender sends to a server instead of a receiver.
    /// </summary>
    private static (KermitEngine sender, KermitEngine server, MemoryFileHandler serverFiles)
        RunServerTransfer(string fileName, byte[] fileContent, int maxRounds = 200)
    {
        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile(fileName, fileContent);

        var serverFiles = new MemoryFileHandler();

        var sender = new KermitEngine(new KermitOptions(), senderFiles);
        var server = new KermitEngine(new KermitOptions(), serverFiles);

        List<byte> senderOutput = new();
        List<byte> serverOutput = new();
        sender.DataToSend += data => senderOutput.AddRange(data.ToArray());
        server.DataToSend += data => serverOutput.AddRange(data.ToArray());

        server.BeginServe();
        sender.BeginSend(fileName);

        RunExchangeLoop(sender, server, senderOutput, serverOutput, maxRounds);

        return (sender, server, serverFiles);
    }

    [Fact]
    public void SendReceive_SmallTextFile_TransfersCorrectly()
    {
        byte[] fileContent = Encoding.ASCII.GetBytes("Hello, Kermit!");

        var (sender, receiver, receiverFiles) = RunFileTransfer("test.txt", fileContent);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);

        byte[]? received = receiverFiles.GetWrittenFile("test.txt");
        Assert.NotNull(received);
        Assert.Equal(fileContent, received);
    }

    [Fact]
    public void SendReceive_BinaryData_TransfersCorrectly()
    {
        byte[] fileContent = new byte[256];
        for (int i = 0; i < 256; i++)
            fileContent[i] = (byte)i;

        var (sender, receiver, receiverFiles) = RunFileTransfer("binary.bin", fileContent);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);

        byte[]? received = receiverFiles.GetWrittenFile("binary.bin");
        Assert.NotNull(received);
        Assert.Equal(fileContent, received);
    }

    [Fact]
    public void SendReceive_WithEvenParity_TransfersCorrectly()
    {
        byte[] fileContent = Encoding.ASCII.GetBytes("Parity test data!");

        var senderOpts = new KermitOptions { Parity = ParityMode.Even };
        var receiverOpts = new KermitOptions { Parity = ParityMode.Even };

        var (sender, receiver, receiverFiles) = RunFileTransfer(
            "parity.txt", fileContent, senderOpts, receiverOpts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);

        byte[]? received = receiverFiles.GetWrittenFile("parity.txt");
        Assert.NotNull(received);
        Assert.Equal(fileContent, received);
    }

    [Fact]
    public void SendReceive_BinaryDataWithParity_TransfersCorrectly()
    {
        byte[] fileContent = new byte[256];
        for (int i = 0; i < 256; i++)
            fileContent[i] = (byte)i;

        var senderOpts = new KermitOptions { Parity = ParityMode.Even };
        var receiverOpts = new KermitOptions { Parity = ParityMode.Even };

        var (sender, receiver, receiverFiles) = RunFileTransfer(
            "binary.bin", fileContent, senderOpts, receiverOpts);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);

        byte[]? received = receiverFiles.GetWrittenFile("binary.bin");
        Assert.NotNull(received);
        Assert.Equal(fileContent, received);
    }

    [Fact]
    public void SendReceive_EmptyFile_TransfersCorrectly()
    {
        byte[] fileContent = Array.Empty<byte>();

        var (sender, receiver, receiverFiles) = RunFileTransfer("empty.txt", fileContent);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);

        byte[]? received = receiverFiles.GetWrittenFile("empty.txt");
        Assert.NotNull(received);
        Assert.Equal(fileContent, received);
    }

    [Fact]
    public void SendReceive_LargerFile_TransfersCorrectly()
    {
        byte[] fileContent = new byte[5000];
        for (int i = 0; i < fileContent.Length; i++)
            fileContent[i] = (byte)(i % 251);

        var (sender, receiver, receiverFiles) = RunFileTransfer(
            "large.bin", fileContent, maxRounds: 1000);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, receiver.State);

        byte[]? received = receiverFiles.GetWrittenFile("large.bin");
        Assert.NotNull(received);
        Assert.Equal(fileContent.Length, received.Length);
        Assert.Equal(fileContent, received);
    }

    [Fact]
    public void ServerMode_AcceptsFileTransfer()
    {
        byte[] fileContent = Encoding.ASCII.GetBytes("Server test!");

        var (sender, server, serverFiles) = RunServerTransfer("server.txt", fileContent);

        Assert.Equal(KermitState.Completed, sender.State);
        Assert.Equal(KermitState.Completed, server.State);

        byte[]? received = serverFiles.GetWrittenFile("server.txt");
        Assert.NotNull(received);
        Assert.Equal(fileContent, received);
    }

    [Fact]
    public void Cancel_SendsErrorPacket()
    {
        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile("test.txt", [1, 2, 3]);

        var sender = new KermitEngine(new KermitOptions(), senderFiles);
        bool errorSent = false;
        sender.DataToSend += data =>
        {
            if (data.Length > 3 && data.Span[3] == PacketType.Error)
                errorSent = true;
        };

        sender.BeginSend("test.txt");
        sender.Cancel();

        Assert.Equal(KermitState.Failed, sender.State);
        Assert.True(errorSent);
    }

    [Fact]
    public void FileEvents_FiredCorrectly()
    {
        byte[] fileContent = Encoding.ASCII.GetBytes("Events test");

        var senderFiles = new MemoryFileHandler();
        senderFiles.AddFile("events.txt", fileContent);

        var receiverFiles = new MemoryFileHandler();

        var sender = new KermitEngine(new KermitOptions(), senderFiles);
        var receiver = new KermitEngine(new KermitOptions(), receiverFiles);

        string? senderStartFile = null;
        string? senderDoneFile = null;
        string? receiverStartFile = null;
        string? receiverDoneFile = null;
        bool senderTransferDone = false;
        bool receiverTransferDone = false;

        // Wire ALL events before initiating transfer
        List<byte> senderOutput = new();
        List<byte> receiverOutput = new();
        sender.DataToSend += data => senderOutput.AddRange(data.ToArray());
        receiver.DataToSend += data => receiverOutput.AddRange(data.ToArray());

        sender.FileStarting += f => senderStartFile = f;
        sender.FileCompleted += f => senderDoneFile = f;
        sender.TransferCompleted += () => senderTransferDone = true;
        receiver.FileStarting += f => receiverStartFile = f;
        receiver.FileCompleted += f => receiverDoneFile = f;
        receiver.TransferCompleted += () => receiverTransferDone = true;

        receiver.BeginReceive();
        sender.BeginSend("events.txt");

        RunExchangeLoop(sender, receiver, senderOutput, receiverOutput);

        Assert.Equal("events.txt", senderStartFile);
        Assert.Equal("events.txt", senderDoneFile);
        Assert.True(senderTransferDone);
        Assert.Equal("events.txt", receiverStartFile);
        Assert.Equal("events.txt", receiverDoneFile);
        Assert.True(receiverTransferDone);
    }

    [Fact]
    public void BuildPacket_ProducesValidPacket()
    {
        var engine = new KermitEngine(new KermitOptions(), new MemoryFileHandler());
        byte[] buf = new byte[128];
        byte[] data = Encoding.ASCII.GetBytes("Hi");

        int len = engine.BuildPacket(buf, 0, PacketType.Data, data);

        // MARK + LEN + SEQ + TYPE + DATA(2) + CHECK + EOL = 8
        Assert.Equal(8, len);
        Assert.Equal(KermitConst.SOH, buf[0]);
        Assert.Equal(KermitConst.ToChar(2 + 3), buf[1]); // LEN = data(2) + 3
        Assert.Equal(KermitConst.ToChar(0), buf[2]);      // SEQ = 0
        Assert.Equal(PacketType.Data, buf[3]);             // TYPE = 'D'
        Assert.Equal((byte)'H', buf[4]);
        Assert.Equal((byte)'i', buf[5]);
        Assert.Equal(KermitConst.DefaultEol, buf[7]);

        // Verify checksum
        int chk = KermitChecksum.ComputeType1(buf.AsSpan(1, 5)); // LEN through DATA
        Assert.Equal(KermitConst.ToChar(chk), buf[6]);
    }
}
