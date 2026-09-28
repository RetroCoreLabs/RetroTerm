using System;
using System.Threading;
using System.Threading.Tasks;

namespace RetroTerm.Core.Transfer;

public enum TransferDirection { Send, Receive }

public enum TransferState { Idle, Initializing, Transferring, Completing, Completed, Failed, Cancelled }

public readonly struct TransferProgress
{
    public string FileName { get; }
    public long BytesTransferred { get; }
    public long TotalBytes { get; }
    public int FileNumber { get; }
    public int TotalFiles { get; }
    public TransferState State { get; }
    public TransferDirection Direction { get; }
    public string? ErrorMessage { get; }

    public TransferProgress(
        string fileName,
        long bytesTransferred,
        long totalBytes,
        int fileNumber,
        int totalFiles,
        TransferState state,
        TransferDirection direction,
        string? errorMessage = null)
    {
        FileName = fileName;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        FileNumber = fileNumber;
        TotalFiles = totalFiles;
        State = state;
        Direction = direction;
        ErrorMessage = errorMessage;
    }
}

public delegate Task SendBytesAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

public interface IFileTransferHandler
{
    TransferState State { get; }

    event Action<TransferProgress>? ProgressChanged;

    event Action<TransferProgress>? TransferCompleted;

    void ProcessIncomingData(ReadOnlySpan<byte> data);

    Task StartSendAsync(string[] filePaths, SendBytesAsync sendDelegate, CancellationToken ct);

    Task StartReceiveAsync(string saveDirectory, SendBytesAsync sendDelegate, CancellationToken ct);

    void Cancel();
}
