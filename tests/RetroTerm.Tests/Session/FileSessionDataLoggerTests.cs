using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Spy implementation of ISessionDataLogger that records all calls for assertion.
/// Thread-safe via lock.
/// </summary>
public sealed class SpySessionDataLogger : ISessionDataLogger
{
    private readonly object _lock = new object();
    private readonly List<LogEntry> _entries = new List<LogEntry>();
    private bool _isLogging;

    public bool IsLogging
    {
        get { lock (_lock) { return _isLogging; } }
    }

    public int EntryCount
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    public void Start(string filePath, SessionLogFormat format)
    {
        lock (_lock) { _isLogging = true; }
    }

    public void Stop()
    {
        lock (_lock) { _isLogging = false; }
    }

    public void LogIncoming(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(LogDirection.Incoming, data.ToArray()));
        }
    }

    public void LogOutgoing(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(LogDirection.Outgoing, data.ToArray()));
        }
    }

    public LogEntry GetEntry(int index)
    {
        lock (_lock) { return _entries[index]; }
    }

    public LogEntry[] GetAllEntries()
    {
        lock (_lock)
        {
            var result = new LogEntry[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
                result[i] = _entries[i];
            return result;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    public enum LogDirection { Incoming, Outgoing }

    public sealed class LogEntry
    {
        public LogDirection Direction { get; }
        public byte[] Data { get; }

        public LogEntry(LogDirection direction, byte[] data)
        {
            Direction = direction;
            Data = data;
        }
    }
}

public class FileSessionDataLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public FileSessionDataLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RetroTermTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private string GetTempFile(string extension)
    {
        return Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}{extension}");
    }

    // --- IsLogging property ---

    [Fact]
    public void IsLogging_ReturnsFalse_WhenNotStarted()
    {
        using var logger = new FileSessionDataLogger();
        Assert.False(logger.IsLogging);
    }

    [Fact]
    public void IsLogging_ReturnsTrue_AfterStart()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);

        Assert.True(logger.IsLogging);
    }

    [Fact]
    public void IsLogging_ReturnsFalse_AfterStop()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.Stop();

        Assert.False(logger.IsLogging);
    }

    // --- Start/Stop lifecycle ---

    [Fact]
    public void Start_ThrowsOnNullPath()
    {
        using var logger = new FileSessionDataLogger();
        Assert.Throws<ArgumentNullException>(() => logger.Start(null!, SessionLogFormat.HexDump));
    }

    [Fact]
    public void Start_ThrowsOnEmptyPath()
    {
        using var logger = new FileSessionDataLogger();
        Assert.Throws<ArgumentNullException>(() => logger.Start("", SessionLogFormat.HexDump));
    }

    [Fact]
    public void Start_ThrowsAfterDispose()
    {
        var logger = new FileSessionDataLogger();
        logger.Dispose();
        Assert.Throws<ObjectDisposedException>(() => logger.Start(GetTempFile(".log"), SessionLogFormat.HexDump));
    }

    [Fact]
    public void Start_CreatesFile()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.Stop();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DoubleStart_StopsPreviousAndStartsNew()
    {
        var path1 = GetTempFile(".log");
        var path2 = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path1, SessionLogFormat.HexDump);
        logger.LogIncoming(new byte[] { 0x41 });
        logger.Start(path2, SessionLogFormat.HexDump);
        logger.LogIncoming(new byte[] { 0x42 });
        logger.Stop();

        // Both files should exist
        Assert.True(File.Exists(path1));
        Assert.True(File.Exists(path2));

        // First file should have data (A)
        var content1 = File.ReadAllText(path1);
        Assert.Contains("41", content1);

        // Second file should have data (B)
        var content2 = File.ReadAllText(path2);
        Assert.Contains("42", content2);
    }

    [Fact]
    public void Stop_FlushesData()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.LogIncoming(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }); // "Hello"
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("48 65 6C 6C 6F", content);
    }

    [Fact]
    public void Dispose_StopsLogging()
    {
        var path = GetTempFile(".log");
        var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.LogIncoming(new byte[] { 0x41 });
        logger.Dispose();

        Assert.False(logger.IsLogging);
        // File should be readable after dispose
        var content = File.ReadAllText(path);
        Assert.Contains("41", content);
    }

    // --- HexDump format ---

    [Fact]
    public void HexDump_WritesHeader()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("Session Log Started:", content);
        Assert.Contains("Format: HexDump", content);
    }

    [Fact]
    public void HexDump_LogsIncomingWithCorrectDirection()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.LogIncoming(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }); // "Hello"
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("<<<", content);
        Assert.Contains("48 65 6C 6C 6F", content);
        Assert.Contains("|Hello|", content);
    }

    [Fact]
    public void HexDump_LogsOutgoingWithCorrectDirection()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.LogOutgoing(new byte[] { 0x57, 0x6F, 0x72, 0x6C, 0x64 }); // "World"
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains(">>>", content);
        Assert.Contains("57 6F 72 6C 64", content);
        Assert.Contains("|World|", content);
    }

    [Fact]
    public void HexDump_ShowsDotsForControlCharacters()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.LogIncoming(new byte[] { 0x1B, 0x5B, 0x48 }); // ESC[H
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("1B 5B 48", content);
        Assert.Contains("|.[H|", content);
    }

    [Fact]
    public void HexDump_WrapsLongDataAcrossMultipleLines()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        // 20 bytes - should wrap to 2 lines (16 + 4)
        var data = new byte[20];
        for (int i = 0; i < 20; i++)
            data[i] = (byte)(0x41 + i); // A, B, C, ...

        logger.Start(path, SessionLogFormat.HexDump);
        logger.LogIncoming(data);
        logger.Stop();

        var lines = File.ReadAllLines(path);
        // Header (3 lines) + 2 data lines = 5
        int dataLines = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("<<<"))
                dataLines++;
        }
        Assert.Equal(2, dataLines);
    }

    [Fact]
    public void HexDump_IncludesTimestamp()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.LogIncoming(new byte[] { 0x41 });
        logger.Stop();

        var content = File.ReadAllText(path);
        // Should contain timestamp in [HH:mm:ss.fff] format
        Assert.Matches(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\]", content);
    }

    // --- RawBinary format ---

    [Fact]
    public void RawBinary_WritesCorrectIncomingFrame()
    {
        var path = GetTempFile(".bin");
        using var logger = new FileSessionDataLogger();

        var testData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"

        logger.Start(path, SessionLogFormat.RawBinary);
        logger.LogIncoming(testData);
        logger.Stop();

        var fileBytes = File.ReadAllBytes(path);

        // Frame: [direction=0x00][length=5 LE][data=5 bytes]
        Assert.True(fileBytes.Length >= 10); // 1 + 4 + 5
        Assert.Equal(0x00, fileBytes[0]); // Incoming direction
        Assert.Equal(5, BitConverter.ToInt32(fileBytes, 1)); // Length
        Assert.Equal(0x48, fileBytes[5]); // 'H'
        Assert.Equal(0x65, fileBytes[6]); // 'e'
        Assert.Equal(0x6C, fileBytes[7]); // 'l'
        Assert.Equal(0x6C, fileBytes[8]); // 'l'
        Assert.Equal(0x6F, fileBytes[9]); // 'o'
    }

    [Fact]
    public void RawBinary_WritesCorrectOutgoingFrame()
    {
        var path = GetTempFile(".bin");
        using var logger = new FileSessionDataLogger();

        var testData = new byte[] { 0x41, 0x42, 0x43 }; // "ABC"

        logger.Start(path, SessionLogFormat.RawBinary);
        logger.LogOutgoing(testData);
        logger.Stop();

        var fileBytes = File.ReadAllBytes(path);

        Assert.True(fileBytes.Length >= 8); // 1 + 4 + 3
        Assert.Equal(0x01, fileBytes[0]); // Outgoing direction
        Assert.Equal(3, BitConverter.ToInt32(fileBytes, 1)); // Length
        Assert.Equal(0x41, fileBytes[5]); // 'A'
        Assert.Equal(0x42, fileBytes[6]); // 'B'
        Assert.Equal(0x43, fileBytes[7]); // 'C'
    }

    [Fact]
    public void RawBinary_MultipleFramesReadBackCorrectly()
    {
        var path = GetTempFile(".bin");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.RawBinary);
        logger.LogIncoming(new byte[] { 0x01, 0x02 });
        logger.LogOutgoing(new byte[] { 0x03, 0x04, 0x05 });
        logger.LogIncoming(new byte[] { 0x06 });
        logger.Stop();

        var fileBytes = File.ReadAllBytes(path);

        // Frame 1: direction(1) + length(4) + data(2) = 7
        // Frame 2: direction(1) + length(4) + data(3) = 8
        // Frame 3: direction(1) + length(4) + data(1) = 6
        // Total = 21
        Assert.Equal(21, fileBytes.Length);

        // Frame 1: incoming, 2 bytes
        int offset = 0;
        Assert.Equal(0x00, fileBytes[offset]); // incoming
        Assert.Equal(2, BitConverter.ToInt32(fileBytes, offset + 1));
        Assert.Equal(0x01, fileBytes[offset + 5]);
        Assert.Equal(0x02, fileBytes[offset + 6]);

        // Frame 2: outgoing, 3 bytes
        offset = 7;
        Assert.Equal(0x01, fileBytes[offset]); // outgoing
        Assert.Equal(3, BitConverter.ToInt32(fileBytes, offset + 1));
        Assert.Equal(0x03, fileBytes[offset + 5]);
        Assert.Equal(0x04, fileBytes[offset + 6]);
        Assert.Equal(0x05, fileBytes[offset + 7]);

        // Frame 3: incoming, 1 byte
        offset = 15;
        Assert.Equal(0x00, fileBytes[offset]); // incoming
        Assert.Equal(1, BitConverter.ToInt32(fileBytes, offset + 1));
        Assert.Equal(0x06, fileBytes[offset + 5]);
    }

    [Fact]
    public void RawBinary_NoTextHeader()
    {
        var path = GetTempFile(".bin");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.RawBinary);
        logger.LogIncoming(new byte[] { 0x41 });
        logger.Stop();

        var fileBytes = File.ReadAllBytes(path);

        // Should start directly with direction byte, no text header
        Assert.Equal(0x00, fileBytes[0]); // Direction byte, not text
        Assert.Equal(6, fileBytes.Length); // 1 + 4 + 1
    }

    // --- DecodedText format ---

    [Fact]
    public void DecodedText_WritesHeader()
    {
        var path = GetTempFile(".txt");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.DecodedText);
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("Session Log Started:", content);
        Assert.Contains("Format: DecodedText", content);
    }

    [Fact]
    public void DecodedText_DecodesAsciiCorrectly()
    {
        var path = GetTempFile(".txt");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.DecodedText);
        logger.LogIncoming(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }); // "Hello"
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("<<< Hello", content);
    }

    [Fact]
    public void DecodedText_ShowsControlCodeMnemonics()
    {
        var path = GetTempFile(".txt");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.DecodedText);
        logger.LogIncoming(new byte[] { 0x1B, 0x0D, 0x0A }); // ESC, CR, LF
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("<ESC>", content);
        Assert.Contains("<CR>", content);
        Assert.Contains("<LF>", content);
    }

    [Fact]
    public void DecodedText_ShowsHexForUnnamedControlCodes()
    {
        var path = GetTempFile(".txt");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.DecodedText);
        logger.LogIncoming(new byte[] { 0x80, 0xFF }); // High bytes
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("<0x80>", content);
        Assert.Contains("<0xFF>", content);
    }

    [Fact]
    public void DecodedText_ShowsDirectionMarkers()
    {
        var path = GetTempFile(".txt");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.DecodedText);
        logger.LogIncoming(new byte[] { 0x41 }); // A
        logger.LogOutgoing(new byte[] { 0x42 }); // B
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("<<< A", content);
        Assert.Contains(">>> B", content);
    }

    [Fact]
    public void DecodedText_ShowsAllNamedControlCodes()
    {
        var path = GetTempFile(".txt");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.DecodedText);
        logger.LogIncoming(new byte[]
        {
            0x00, // NUL
            0x07, // BEL
            0x08, // BS
            0x09, // TAB
            0x7F  // DEL
        });
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains("<NUL>", content);
        Assert.Contains("<BEL>", content);
        Assert.Contains("<BS>", content);
        Assert.Contains("<TAB>", content);
        Assert.Contains("<DEL>", content);
    }

    // --- Empty data handling ---

    [Fact]
    public void LogIncoming_IgnoresEmptyData()
    {
        var path = GetTempFile(".bin");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.RawBinary);
        logger.LogIncoming(ReadOnlySpan<byte>.Empty);
        logger.Stop();

        var fileBytes = File.ReadAllBytes(path);
        Assert.Empty(fileBytes); // No frames written
    }

    [Fact]
    public void LogOutgoing_IgnoresEmptyData()
    {
        var path = GetTempFile(".bin");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.RawBinary);
        logger.LogOutgoing(ReadOnlySpan<byte>.Empty);
        logger.Stop();

        var fileBytes = File.ReadAllBytes(path);
        Assert.Empty(fileBytes);
    }

    // --- Not logging / no-op when not started ---

    [Fact]
    public void LogIncoming_DoesNothingWhenNotStarted()
    {
        using var logger = new FileSessionDataLogger();
        // Should not throw
        logger.LogIncoming(new byte[] { 0x41 });
    }

    [Fact]
    public void LogOutgoing_DoesNothingWhenNotStarted()
    {
        using var logger = new FileSessionDataLogger();
        // Should not throw
        logger.LogOutgoing(new byte[] { 0x41 });
    }

    [Fact]
    public void LogIncoming_DoesNothingAfterStop()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();

        logger.Start(path, SessionLogFormat.HexDump);
        logger.Stop();

        // Should not throw
        logger.LogIncoming(new byte[] { 0x41 });
    }

    // --- Thread safety ---

    [Fact]
    public void ConcurrentLogging_DoesNotThrow()
    {
        var path = GetTempFile(".log");
        using var logger = new FileSessionDataLogger();
        logger.Start(path, SessionLogFormat.HexDump);

        var barrier = new ManualResetEventSlim(false);
        var threads = new Thread[8];
        Exception? caughtException = null;

        for (int t = 0; t < threads.Length; t++)
        {
            int threadIndex = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    barrier.Wait();
                    for (int i = 0; i < 100; i++)
                    {
                        if (threadIndex % 2 == 0)
                            logger.LogIncoming(new byte[] { (byte)(i & 0xFF), (byte)(threadIndex & 0xFF) });
                        else
                            logger.LogOutgoing(new byte[] { (byte)(i & 0xFF), (byte)(threadIndex & 0xFF) });
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref caughtException, ex, null);
                }
            });
            threads[t].Start();
        }

        // Release all threads simultaneously
        barrier.Set();

        for (int t = 0; t < threads.Length; t++)
            threads[t].Join(TimeSpan.FromSeconds(10));

        logger.Stop();

        Assert.Null(caughtException);
        Assert.True(File.Exists(path));

        // File should have content
        var info = new FileInfo(path);
        Assert.True(info.Length > 0);
    }

    [Fact]
    public void ConcurrentLogging_RawBinary_ProducesValidFrames()
    {
        var path = GetTempFile(".bin");
        using var logger = new FileSessionDataLogger();
        logger.Start(path, SessionLogFormat.RawBinary);

        var barrier = new ManualResetEventSlim(false);
        var threads = new Thread[4];

        for (int t = 0; t < threads.Length; t++)
        {
            int threadIndex = t;
            threads[t] = new Thread(() =>
            {
                barrier.Wait();
                for (int i = 0; i < 50; i++)
                {
                    var data = new byte[] { (byte)(threadIndex & 0xFF), (byte)(i & 0xFF) };
                    if (threadIndex % 2 == 0)
                        logger.LogIncoming(data);
                    else
                        logger.LogOutgoing(data);
                }
            });
            threads[t].Start();
        }

        barrier.Set();

        for (int t = 0; t < threads.Length; t++)
            threads[t].Join(TimeSpan.FromSeconds(10));

        logger.Stop();

        // Read back and verify all frames are valid
        var fileBytes = File.ReadAllBytes(path);
        int offset = 0;
        int frameCount = 0;

        while (offset < fileBytes.Length)
        {
            Assert.True(offset + 5 <= fileBytes.Length, $"Incomplete frame header at offset {offset}");

            byte direction = fileBytes[offset];
            Assert.True(direction == 0x00 || direction == 0x01, $"Invalid direction {direction} at offset {offset}");

            int length = BitConverter.ToInt32(fileBytes, offset + 1);
            Assert.True(length > 0, $"Invalid length {length} at offset {offset}");
            Assert.True(offset + 5 + length <= fileBytes.Length, $"Incomplete frame data at offset {offset}, length {length}");

            offset += 5 + length;
            frameCount++;
        }

        // 4 threads * 50 iterations = 200 frames
        Assert.Equal(200, frameCount);
    }

    // --- Integration with TerminalSession using SpyLogger ---

    [Fact]
    public void TerminalSession_DataLoggerProperty_GetSet()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        Assert.Null(session.DataLogger);

        using var logger = new SpySessionDataLogger();
        session.DataLogger = logger;
        Assert.Same(logger, session.DataLogger);

        session.DataLogger = null;
        Assert.Null(session.DataLogger);
    }

    [Fact]
    public async Task TerminalSession_LogsIncomingData_ViaSpy()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        using var spy = new SpySessionDataLogger();
        session.DataLogger = spy;

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        // Simulate incoming data arriving at the client
        client.EnqueueReceivedData(Encoding.UTF8.GetBytes("Hello"));

        // Wait for the receive loop to deliver data
        for (int i = 0; i < 200 && spy.EntryCount == 0; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(spy.EntryCount > 0, "Spy logger should have recorded at least one incoming entry");
        var entry = spy.GetEntry(0);
        Assert.Equal(SpySessionDataLogger.LogDirection.Incoming, entry.Direction);
        Assert.Equal(Encoding.UTF8.GetBytes("Hello"), entry.Data);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_LogsOutgoingData_ViaSpy()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        using var spy = new SpySessionDataLogger();
        session.DataLogger = spy;

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        await session.SendInputAsync("Hi", TestContext.Current.CancellationToken);

        Assert.Equal(1, spy.EntryCount);
        var entry = spy.GetEntry(0);
        Assert.Equal(SpySessionDataLogger.LogDirection.Outgoing, entry.Direction);
        Assert.Equal(Encoding.UTF8.GetBytes("Hi"), entry.Data);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_LogsBothDirections_InOrder()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        using var spy = new SpySessionDataLogger();
        session.DataLogger = spy;

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        // Outgoing first
        await session.SendInputAsync("A", TestContext.Current.CancellationToken);

        // Then incoming
        client.EnqueueReceivedData(new byte[] { 0x42 }); // 'B'
        for (int i = 0; i < 200 && spy.EntryCount < 2; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(spy.EntryCount >= 2, $"Expected at least 2 entries, got {spy.EntryCount}");

        var entries = spy.GetAllEntries();
        Assert.Equal(SpySessionDataLogger.LogDirection.Outgoing, entries[0].Direction);
        Assert.Equal(new byte[] { 0x41 }, entries[0].Data); // 'A'
        Assert.Equal(SpySessionDataLogger.LogDirection.Incoming, entries[1].Direction);
        Assert.Equal(new byte[] { 0x42 }, entries[1].Data); // 'B'

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_DoesNotLog_WhenLoggerIsNull()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        // DataLogger is null by default

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        // Should not throw with null logger
        await session.SendInputAsync("Test", TestContext.Current.CancellationToken);
        client.EnqueueReceivedData(new byte[] { 0x41 });
        await Task.Delay(50, TestContext.Current.CancellationToken);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_StopsLogging_WhenLoggerSetToNull()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        using var spy = new SpySessionDataLogger();
        session.DataLogger = spy;

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        await session.SendInputAsync("A", TestContext.Current.CancellationToken);
        Assert.Equal(1, spy.EntryCount);

        // Detach logger
        session.DataLogger = null;

        await session.SendInputAsync("B", TestContext.Current.CancellationToken);
        // Should still be 1 — "B" should NOT be logged
        Assert.Equal(1, spy.EntryCount);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_LoggerAttachedAfterConnect_StillLogs()
    {
        // This is the real-world scenario: connect first, then start logging
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        // Attach logger AFTER connection is established
        using var spy = new SpySessionDataLogger();
        session.DataLogger = spy;

        await session.SendInputAsync("Late", TestContext.Current.CancellationToken);

        Assert.Equal(1, spy.EntryCount);
        var entry = spy.GetEntry(0);
        Assert.Equal(SpySessionDataLogger.LogDirection.Outgoing, entry.Direction);
        Assert.Equal(Encoding.UTF8.GetBytes("Late"), entry.Data);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_LoggerAttachedAfterConnect_LogsIncoming()
    {
        // Critical test: logger set on UI thread, data arrives on receive thread
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        // Attach logger AFTER connection (and its receive loop) is running
        using var spy = new SpySessionDataLogger();
        session.DataLogger = spy;

        // Now push data — the receive loop thread must see the new logger
        client.EnqueueReceivedData(Encoding.UTF8.GetBytes("FromHost"));

        for (int i = 0; i < 200 && spy.EntryCount == 0; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(spy.EntryCount > 0,
            "Logger attached after connect must see incoming data from the receive thread");
        var entry = spy.GetEntry(0);
        Assert.Equal(SpySessionDataLogger.LogDirection.Incoming, entry.Direction);
        Assert.Equal(Encoding.UTF8.GetBytes("FromHost"), entry.Data);

        await session.DisconnectAsync();
    }

    [Fact]
    public async Task TerminalSession_LoggerSeesMultipleIncomingChunks()
    {
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        using var spy = new SpySessionDataLogger();
        session.DataLogger = spy;

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        // Send 5 separate chunks
        for (int i = 0; i < 5; i++)
            client.EnqueueReceivedData(new byte[] { (byte)(0x30 + i) }); // '0'..'4'

        for (int i = 0; i < 200 && spy.EntryCount < 5; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal(5, spy.EntryCount);
        var entries = spy.GetAllEntries();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(SpySessionDataLogger.LogDirection.Incoming, entries[i].Direction);
            Assert.Equal(new byte[] { (byte)(0x30 + i) }, entries[i].Data);
        }

        await session.DisconnectAsync();
    }

    // --- Integration: FileSessionDataLogger end-to-end with TerminalSession ---

    [Fact]
    public async Task TerminalSession_FileLogger_WritesIncomingToFile()
    {
        var path = GetTempFile(".log");
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        using var logger = new FileSessionDataLogger();
        logger.Start(path, SessionLogFormat.HexDump);
        session.DataLogger = logger;

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        client.EnqueueReceivedData(Encoding.UTF8.GetBytes("Hello"));

        for (int i = 0; i < 200; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            // Check if data has been written
            logger.Stop();
            var content = File.ReadAllText(path);
            if (content.Contains("<<<"))
            {
                Assert.Contains("48 65 6C 6C 6F", content);
                await session.DisconnectAsync();
                return; // Test passed
            }
            // Restart for next iteration
            logger.Start(path, SessionLogFormat.HexDump);
        }

        // If we get here, data was never logged
        logger.Stop();
        var finalContent = File.ReadAllText(path);
        Assert.Fail($"Incoming data was never logged to file. File content: {finalContent}");
    }

    [Fact]
    public async Task TerminalSession_FileLogger_WritesOutgoingToFile()
    {
        var path = GetTempFile(".log");
        var emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");

        using var logger = new FileSessionDataLogger();
        logger.Start(path, SessionLogFormat.HexDump);
        session.DataLogger = logger;

        var (client, _) = InMemoryBidirectionalConnection.CreatePair();
        await session.ConnectAsync(client, TestContext.Current.CancellationToken);

        await session.SendInputAsync("Hi", TestContext.Current.CancellationToken);

        session.DataLogger = null;
        logger.Stop();

        var content = File.ReadAllText(path);
        Assert.Contains(">>>", content);
        Assert.Contains("48 69", content); // "Hi" in hex

        await session.DisconnectAsync();
    }
}
