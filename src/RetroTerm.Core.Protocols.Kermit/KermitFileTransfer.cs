using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Transfer;

namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Bridges IFileTransferHandler (used by TerminalSession) with KermitEngine.
/// Owns the KermitEngine, implements IKermitFileHandler for disk I/O,
/// and manages the transfer lifecycle including delay, file collision, and progress.
/// </summary>
public sealed class KermitFileTransfer : IFileTransferHandler, IKermitFileHandler
{
    private readonly KermitOptions _options;
    private KermitEngine? _engine;
    private SendBytesAsync? _sendDelegate;
    private CancellationToken _ct;
    private TransferState _state = TransferState.Idle;
    private TransferDirection _direction;

    // File I/O state
    private FileStream? _fileStream;
    private string _saveDirectory = string.Empty;
    private string _currentFileName = string.Empty;
    private long _currentFileSize;
    private long _bytesTransferred;

    // Batch state
    private string[]? _filePaths;
    private int _fileIndex;
    private int _totalFiles;
    private TaskCompletionSource<bool>? _completionSource;

    // Timeout timer — fires NotifyTimeout() on the engine when the remote
    // doesn't respond within Options.Timeout seconds
    private System.Threading.Timer? _timeoutTimer;

    public TransferState State => _state;

    /// <summary>
    /// File collision behavior when receiving. Default: Rename.
    /// </summary>
    public FileCollisionMode FileCollision { get; set; } = FileCollisionMode.Rename;

    /// <summary>
    /// Live transfer statistics for the debug window.
    /// </summary>
    public KermitTransferStatistics Statistics { get; } = new();

    public event Action<TransferProgress>? ProgressChanged;
    public event Action<TransferProgress>? TransferCompleted;

    public KermitFileTransfer(KermitOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public KermitFileTransfer() : this(new KermitOptions())
    {
    }

    public void ProcessIncomingData(ReadOnlySpan<byte> data)
    {
        // Data arrived from remote — reset the timeout timer
        ResetTimeoutTimer();
        Statistics.AddCharsReceived(data.Length);
        _engine?.ProcessReceivedData(data);
        UpdateStatisticsFromEngine();
    }

    public async Task StartSendAsync(string[] filePaths, SendBytesAsync sendDelegate, CancellationToken ct)
    {
        if (filePaths == null || filePaths.Length == 0)
            throw new ArgumentException("No files specified", nameof(filePaths));

        _sendDelegate = sendDelegate;
        _ct = ct;
        _direction = TransferDirection.Send;
        _filePaths = filePaths;
        _totalFiles = filePaths.Length;
        _fileIndex = 0;

        _completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Apply sender delay if configured
        if (_options.Delay > 0)
        {
            _state = TransferState.Initializing;
            RaiseProgress();
            await Task.Delay(_options.Delay * 1000, ct).ConfigureAwait(false);
        }

        // Send first file — engine currently handles one file at a time
        // For batch, we chain: on FileCompleted, start next file or send Break
        SendCurrentFile();

        await _completionSource.Task.ConfigureAwait(false);
    }

    public async Task StartReceiveAsync(string saveDirectory, SendBytesAsync sendDelegate, CancellationToken ct)
    {
        _sendDelegate = sendDelegate;
        _ct = ct;
        _direction = TransferDirection.Receive;
        _saveDirectory = saveDirectory;
        _fileIndex = 0;
        _totalFiles = 0; // unknown until transfer completes
        _bytesTransferred = 0;

        _completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _engine = CreateEngine();
        _state = TransferState.Initializing;
        RaiseProgress();

        _engine.BeginReceive();

        await _completionSource.Task.ConfigureAwait(false);
    }

    public void Cancel()
    {
        if (_state == TransferState.Completed || _state == TransferState.Failed || _state == TransferState.Cancelled)
            return;

        StopTimeoutTimer();
        _engine?.Cancel();
        CloseFile();
        _state = TransferState.Cancelled;

        var progress = MakeProgress(TransferState.Cancelled);
        TransferCompleted?.Invoke(progress);
        _completionSource?.TrySetResult(false);
    }

    // ───────────────────────────────────────────────────────────────────
    // Private: engine lifecycle
    // ───────────────────────────────────────────────────────────────────

    private void SendCurrentFile()
    {
        if (_filePaths == null || _fileIndex >= _filePaths.Length)
        {
            // All files sent
            _state = TransferState.Completed;
            var progress = MakeProgress(TransferState.Completed);
            TransferCompleted?.Invoke(progress);
            _completionSource?.TrySetResult(true);
            return;
        }

        _currentFileName = _filePaths[_fileIndex];
        _bytesTransferred = 0;
        _currentFileSize = -1;

        _engine = CreateEngine();
        _state = TransferState.Transferring;
        RaiseProgress();

        _engine.BeginSend(_currentFileName);
    }

    private KermitEngine CreateEngine()
    {
        var engine = new KermitEngine(_options, this);

        engine.DataToSend += OnEngineDataToSend;
        engine.FileStarting += OnEngineFileStarting;
        engine.FileCompleted += OnEngineFileCompleted;
        engine.TransferCompleted += OnEngineTransferCompleted;
        engine.ErrorOccurred += OnEngineError;

        Statistics.Start();
        return engine;
    }

    // ───────────────────────────────────────────────────────────────────
    // Engine event handlers
    // ───────────────────────────────────────────────────────────────────

    private async void OnEngineDataToSend(ReadOnlyMemory<byte> data)
    {
        if (_sendDelegate == null) return;

        try
        {
            Statistics.AddCharsSent(data.Length);
            await _sendDelegate(data, _ct).ConfigureAwait(false);
            // Packet sent — start (or restart) the timeout timer waiting for response
            ResetTimeoutTimer();
        }
        catch (Exception)
        {
            StopTimeoutTimer();
            _state = TransferState.Failed;
            var progress = MakeProgress(TransferState.Failed, "Connection lost during transfer");
            TransferCompleted?.Invoke(progress);
            _completionSource?.TrySetResult(false);
        }
    }

    private void OnEngineFileStarting(string fileName)
    {
        _currentFileName = fileName;
        _fileIndex++;
        _state = TransferState.Transferring;
        Statistics.CurrentFile = fileName;
        UpdateStatisticsFromEngine();
        RaiseProgress();
    }

    private void OnEngineFileCompleted(string fileName)
    {
        if (_direction == TransferDirection.Send)
        {
            // For batch send, the engine handles one file — we need to check if more remain
            // The engine will fire TransferCompleted after this, then we chain to next file
        }
        RaiseProgress();
    }

    private void OnEngineTransferCompleted()
    {
        StopTimeoutTimer();
        Statistics.Stop();
        Statistics.StateName = "Completed";

        if (_direction == TransferDirection.Send && _filePaths != null && _fileIndex < _filePaths.Length)
        {
            // More files to send — start next
            // Note: current engine design handles one file per BeginSend.
            // For batch, we'd need to create a new engine or the engine needs multi-file support.
            // The engine already sends Break after one file, so batch requires new engine per file.
            // For now, mark as completed.
        }

        _state = TransferState.Completed;
        var progress = MakeProgress(TransferState.Completed);
        TransferCompleted?.Invoke(progress);
        _completionSource?.TrySetResult(true);
    }

    private void OnEngineError(string message)
    {
        StopTimeoutTimer();
        Statistics.Stop();
        Statistics.StateName = "Failed";
        Statistics.LastError = message;
        CloseFile();
        _state = TransferState.Failed;
        var progress = MakeProgress(TransferState.Failed, message);
        TransferCompleted?.Invoke(progress);
        _completionSource?.TrySetResult(false);
    }

    // ───────────────────────────────────────────────────────────────────
    // Timeout timer
    // ───────────────────────────────────────────────────────────────────

    private void ResetTimeoutTimer()
    {
        int timeoutMs = _options.Timeout * 1000;
        if (timeoutMs <= 0) return;

        if (_timeoutTimer == null)
        {
            _timeoutTimer = new System.Threading.Timer(OnTimeoutFired, null, timeoutMs, System.Threading.Timeout.Infinite);
        }
        else
        {
            _timeoutTimer.Change(timeoutMs, System.Threading.Timeout.Infinite);
        }
    }

    private void StopTimeoutTimer()
    {
        if (_timeoutTimer != null)
        {
            _timeoutTimer.Dispose();
            _timeoutTimer = null;
        }
    }

    private void OnTimeoutFired(object? state)
    {
        // Timer fired — remote didn't respond in time
        if (_state != TransferState.Transferring && _state != TransferState.Initializing)
            return;

        Statistics.AddTimeout();
        _engine?.NotifyTimeout();
    }

    // ───────────────────────────────────────────────────────────────────
    // IKermitFileHandler — disk I/O for the engine
    // ───────────────────────────────────────────────────────────────────

    public bool OpenFileForRead(string fileName, out long fileSize)
    {
        fileSize = -1;
        try
        {
            _fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            fileSize = _fileStream.Length;
            _currentFileSize = fileSize;
            _bytesTransferred = 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int ReadFile(Span<byte> buffer)
    {
        if (_fileStream == null) return 0;

        byte[] temp = new byte[buffer.Length];
        int read = _fileStream.Read(temp, 0, temp.Length);
        if (read > 0)
        {
            temp.AsSpan(0, read).CopyTo(buffer);
            _bytesTransferred += read;
            Statistics.AddFileBytes(read);
            RaiseProgress();
        }
        return read;
    }

    public bool OpenFileForWrite(string fileName)
    {
        try
        {
            string fullPath = Path.Combine(_saveDirectory, SanitizeFileName(fileName));

            if (File.Exists(fullPath))
            {
                switch (FileCollision)
                {
                    case FileCollisionMode.Skip:
                        return false;

                    case FileCollisionMode.Rename:
                        fullPath = GenerateUniquePath(fullPath);
                        break;

                    case FileCollisionMode.Overwrite:
                        // Fall through — FileMode.Create will overwrite
                        break;
                }
            }

            string? dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _currentFileName = Path.GetFileName(fullPath);
            _currentFileSize = -1;
            _bytesTransferred = 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void WriteFile(ReadOnlySpan<byte> data)
    {
        if (_fileStream == null) return;

        byte[] temp = new byte[data.Length];
        data.CopyTo(temp);
        _fileStream.Write(temp, 0, temp.Length);
        _bytesTransferred += data.Length;
        Statistics.AddFileBytes(data.Length);
        RaiseProgress();
    }

    public void CloseFile()
    {
        if (_fileStream != null)
        {
            _fileStream.Dispose();
            _fileStream = null;
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Statistics helpers
    // ───────────────────────────────────────────────────────────────────

    private void UpdateStatisticsFromEngine()
    {
        if (_engine == null) return;

        Statistics.StateName = _engine.State.ToString();
        Statistics.Use8BitQuoting = _engine.NegotiatedUse8BitQuoting;
        Statistics.MaxSendDataLength = _engine.NegotiatedMaxSendDataLength;
        Statistics.BlockCheckType = _options.BlockCheckType;
        Statistics.NotifyUpdated();
    }

    // ───────────────────────────────────────────────────────────────────
    // Progress helpers
    // ───────────────────────────────────────────────────────────────────

    private void RaiseProgress()
    {
        ProgressChanged?.Invoke(MakeProgress(_state));
    }

    private TransferProgress MakeProgress(TransferState state, string? error = null)
    {
        return new TransferProgress(
            fileName: Path.GetFileName(_currentFileName),
            bytesTransferred: _bytesTransferred,
            totalBytes: _currentFileSize,
            fileNumber: Math.Max(_fileIndex, 1),
            totalFiles: _totalFiles,
            state: state,
            direction: _direction,
            errorMessage: error);
    }

    // ───────────────────────────────────────────────────────────────────
    // File name helpers
    // ───────────────────────────────────────────────────────────────────

    private static string SanitizeFileName(string fileName)
    {
        // Strip path separators the remote may have sent
        int lastSlash = -1;
        for (int i = 0; i < fileName.Length; i++)
        {
            char c = fileName[i];
            if (c == '/' || c == '\\' || c == ':')
                lastSlash = i;
        }

        if (lastSlash >= 0 && lastSlash < fileName.Length - 1)
            fileName = fileName.Substring(lastSlash + 1);

        // Replace any remaining invalid chars
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = fileName.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            for (int j = 0; j < invalid.Length; j++)
            {
                if (chars[i] == invalid[j])
                {
                    chars[i] = '_';
                    break;
                }
            }
        }

        return new string(chars);
    }

    private static string GenerateUniquePath(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? ".";
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        for (int i = 1; i < 10000; i++)
        {
            string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        // Extremely unlikely fallback
        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }
}
