namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Abstraction for file I/O operations used by the Kermit protocol engine.
/// The caller implements this interface to provide file access without
/// coupling the engine to any specific filesystem or storage mechanism.
/// </summary>
public interface IKermitFileHandler
{
    /// <summary>
    /// Opens a file for reading (sending).
    /// </summary>
    /// <param name="fileName">
    /// Name of the file to open.
    /// </param>
    /// <param name="fileSize">
    /// Size of the file in bytes, or -1 if unknown.
    /// </param>
    /// <returns>
    /// True if the file was opened successfully.
    /// </returns>
    bool OpenFileForRead(string fileName, out long fileSize);

    /// <summary>
    /// Reads data from the currently open file.
    /// </summary>
    /// <param name="buffer">
    /// Buffer to read into.
    /// </param>
    /// <returns>
    /// Number of bytes read, or 0 at end of file.
    /// </returns>
    int ReadFile(Span<byte> buffer);

    /// <summary>
    /// Opens (or creates) a file for writing (receiving).
    /// </summary>
    /// <param name="fileName">
    /// Name of the file to create/open.
    /// </param>
    /// <returns>
    /// True if the file was opened successfully.
    /// </returns>
    bool OpenFileForWrite(string fileName);

    /// <summary>
    /// Writes data to the currently open file.
    /// </summary>
    /// <param name="data">
    /// Data to write.
    /// </param>
    void WriteFile(ReadOnlySpan<byte> data);

    /// <summary>
    /// Closes the currently open file (if any).
    /// Called after a file transfer completes or is aborted.
    /// </summary>
    void CloseFile();
}
