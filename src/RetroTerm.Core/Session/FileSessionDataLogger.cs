using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace RetroTerm.Core.Session;

/// <summary>
/// Logs raw session data to a file with timestamps and direction markers.
/// Thread-safe via lock. Zero-allocation for RawBinary mode using ArrayPool.
/// </summary>
public sealed class FileSessionDataLogger : ISessionDataLogger
{
    private readonly object _lock = new object();
    private FileStream? _stream;
    private StreamWriter? _textWriter; // Used for HexDump and DecodedText formats
    private SessionLogFormat _format;
    private bool _disposed;

    private const byte DirectionIncoming = 0x00;
    private const byte DirectionOutgoing = 0x01;
    private const int HexDumpBytesPerLine = 16;

    // Preallocated header buffer for RawBinary framing (1 direction + 4 length = 5 bytes)
    private const int RawBinaryHeaderSize = 5;

    public bool IsLogging
    {
        get
        {
            lock (_lock)
            {
                return _stream != null;
            }
        }
    }

    public void Start(string filePath, SessionLogFormat format)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));

        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileSessionDataLogger));

            // Stop any existing logging session
            StopInternal();

            _format = format;
            _stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096);

            if (format != SessionLogFormat.RawBinary)
            {
                _textWriter = new StreamWriter(_stream, new UTF8Encoding(false), 4096, leaveOpen: true);
                _textWriter.AutoFlush = false;
                WriteHeader();
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    public void LogIncoming(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        lock (_lock)
        {
            if (_stream == null) return;

            switch (_format)
            {
                case SessionLogFormat.RawBinary:
                    WriteRawBinaryFrame(DirectionIncoming, data);
                    break;
                case SessionLogFormat.HexDump:
                    WriteHexDump("<<<", data);
                    break;
                case SessionLogFormat.DecodedText:
                    WriteDecodedText("<<<", data);
                    break;
            }
        }
    }

    public void LogOutgoing(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        lock (_lock)
        {
            if (_stream == null) return;

            switch (_format)
            {
                case SessionLogFormat.RawBinary:
                    WriteRawBinaryFrame(DirectionOutgoing, data);
                    break;
                case SessionLogFormat.HexDump:
                    WriteHexDump(">>>", data);
                    break;
                case SessionLogFormat.DecodedText:
                    WriteDecodedText(">>>", data);
                    break;
            }
        }
    }

    /// <summary>
    /// Writes a RawBinary frame: [direction:1][length:4 LE][data:N]
    /// Uses ArrayPool for the header to avoid allocation.
    /// </summary>
    private void WriteRawBinaryFrame(byte direction, ReadOnlySpan<byte> data)
    {
        var header = ArrayPool<byte>.Shared.Rent(RawBinaryHeaderSize);
        try
        {
            header[0] = direction;
            // Write length as little-endian 4 bytes
            header[1] = (byte)(data.Length & 0xFF);
            header[2] = (byte)((data.Length >> 8) & 0xFF);
            header[3] = (byte)((data.Length >> 16) & 0xFF);
            header[4] = (byte)((data.Length >> 24) & 0xFF);

            _stream!.Write(header, 0, RawBinaryHeaderSize);

            // Write data - use ToArray() since FileStream.Write doesn't accept Span on netstandard2.1
            var dataArray = ArrayPool<byte>.Shared.Rent(data.Length);
            try
            {
                data.CopyTo(dataArray);
                _stream.Write(dataArray, 0, data.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(dataArray);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    /// <summary>
    /// Writes hex dump format: [HH:mm:ss.fff] DIR XX XX XX ... |ASCII|
    /// </summary>
    private void WriteHexDump(string direction, ReadOnlySpan<byte> data)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        int offset = 0;

        while (offset < data.Length)
        {
            int count = Math.Min(HexDumpBytesPerLine, data.Length - offset);
            var sb = new StringBuilder(80);

            sb.Append('[');
            sb.Append(timestamp);
            sb.Append("] ");
            sb.Append(direction);
            sb.Append(' ');

            // Hex bytes
            for (int i = 0; i < HexDumpBytesPerLine; i++)
            {
                if (i < count)
                {
                    byte b = data[offset + i];
                    sb.Append(HexDigit(b >> 4));
                    sb.Append(HexDigit(b & 0x0F));
                }
                else
                {
                    sb.Append("  ");
                }
                sb.Append(' ');
            }

            sb.Append(" |");

            // ASCII representation
            for (int i = 0; i < count; i++)
            {
                byte b = data[offset + i];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }

            sb.Append('|');
            _textWriter!.WriteLine(sb.ToString());

            offset += count;
        }

        _textWriter!.Flush();
    }

    /// <summary>
    /// Writes decoded text format with control code mnemonics
    /// </summary>
    private void WriteDecodedText(string direction, ReadOnlySpan<byte> data)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var sb = new StringBuilder(data.Length * 2);

        sb.Append('[');
        sb.Append(timestamp);
        sb.Append("] ");
        sb.Append(direction);
        sb.Append(' ');

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            switch (b)
            {
                case 0x00: sb.Append("<NUL>"); break;
                case 0x01: sb.Append("<SOH>"); break;
                case 0x02: sb.Append("<STX>"); break;
                case 0x03: sb.Append("<ETX>"); break;
                case 0x04: sb.Append("<EOT>"); break;
                case 0x05: sb.Append("<ENQ>"); break;
                case 0x06: sb.Append("<ACK>"); break;
                case 0x07: sb.Append("<BEL>"); break;
                case 0x08: sb.Append("<BS>"); break;
                case 0x09: sb.Append("<TAB>"); break;
                case 0x0A: sb.Append("<LF>"); break;
                case 0x0B: sb.Append("<VT>"); break;
                case 0x0C: sb.Append("<FF>"); break;
                case 0x0D: sb.Append("<CR>"); break;
                case 0x1B: sb.Append("<ESC>"); break;
                case 0x7F: sb.Append("<DEL>"); break;
                default:
                    if (b >= 0x20 && b < 0x7F)
                    {
                        sb.Append((char)b);
                    }
                    else
                    {
                        sb.Append("<0x");
                        sb.Append(HexDigit(b >> 4));
                        sb.Append(HexDigit(b & 0x0F));
                        sb.Append('>');
                    }
                    break;
            }
        }

        _textWriter!.WriteLine(sb.ToString());
        _textWriter.Flush();
    }

    private void WriteHeader()
    {
        var now = DateTime.Now;
        _textWriter!.WriteLine($"Session Log Started: {now:yyyy-MM-dd HH:mm:ss}");
        _textWriter.WriteLine($"Format: {_format}");
        _textWriter.WriteLine(new string('-', 72));
        _textWriter.Flush();
    }

    private static char HexDigit(int nibble)
    {
        return (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
    }

    private void StopInternal()
    {
        if (_textWriter != null)
        {
            _textWriter.Flush();
            _textWriter.Dispose();
            _textWriter = null;
        }

        if (_stream != null)
        {
            _stream.Flush();
            _stream.Dispose();
            _stream = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            StopInternal();
        }
    }
}
