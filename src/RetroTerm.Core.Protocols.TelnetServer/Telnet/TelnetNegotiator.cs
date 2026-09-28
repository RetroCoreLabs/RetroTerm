using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RetroTerm.Core.Protocols.TelnetServer.Telnet;

public class TelnetNegotiator
{
    private readonly NetworkStream _stream;
    public string TerminalType { get; private set; } = "unknown";
    public int Columns { get; private set; } = 80;
    public int Rows { get; private set; } = 24;

    public TelnetNegotiator(NetworkStream stream)
    {
        _stream = stream;
    }

    public async Task SendInitialAsync()
    {
        await _stream.WriteAsync(TelnetCodec.Command(TelnetCodec.WILL, TelnetCodec.OPT_ECHO));
        await _stream.WriteAsync(TelnetCodec.Command(TelnetCodec.WILL, TelnetCodec.OPT_SUPPRESS_GA));
        await _stream.WriteAsync(TelnetCodec.Command(TelnetCodec.DO, TelnetCodec.OPT_NAWS));
        await _stream.WriteAsync(TelnetCodec.Command(TelnetCodec.DO, TelnetCodec.OPT_TTYPE));
        await _stream.WriteAsync(TelnetCodec.Subneg(TelnetCodec.OPT_TTYPE, 1)); // SEND
        await _stream.FlushAsync();
    }

    /// <summary>
    /// Parses TERMINAL-TYPE subnegotiation response
    /// Format: IAC SB TERMINAL_TYPE IS "terminal-type" IAC SE
    /// </summary>
    public bool ParseTerminalTypeSubnegotiation(ReadOnlySpan<byte> data, int startIndex, out string terminalType, out int consumed)
    {
        terminalType = "unknown";
        consumed = 0;

        if (startIndex + 4 >= data.Length)
            return false;

        // Check for IAC SB TERMINAL_TYPE
        if (data[startIndex] != TelnetCodec.IAC ||
            data[startIndex + 1] != TelnetCodec.SB ||
            data[startIndex + 2] != TelnetCodec.OPT_TTYPE)
            return false;

        // Check for IS (0)
        if (data[startIndex + 3] != 0)
            return false;

        // Find IAC SE
        int endIndex = startIndex + 4;
        while (endIndex + 1 < data.Length)
        {
            if (data[endIndex] == TelnetCodec.IAC && data[endIndex + 1] == TelnetCodec.SE)
            {
                // Extract terminal type string
                int length = endIndex - (startIndex + 4);
                if (length > 0)
                {
                    terminalType = Encoding.ASCII.GetString(data.Slice(startIndex + 4, length));
                    TerminalType = terminalType;
                    consumed = endIndex + 2 - startIndex;
                    return true;
                }
                break;
            }
            endIndex++;
        }

        return false;
    }
}
