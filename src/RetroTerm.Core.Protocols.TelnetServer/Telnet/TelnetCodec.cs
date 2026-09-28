using System.Text;

namespace RetroTerm.Core.Protocols.TelnetServer.Telnet;

public static class TelnetCodec
{
    public const byte IAC = 255;
    public const byte DO = 253;
    public const byte DONT = 254;
    public const byte WILL = 251;
    public const byte WONT = 252;
    public const byte SB = 250;
    public const byte SE = 240;

    public const byte OPT_ECHO = 1;
    public const byte OPT_SUPPRESS_GA = 3;
    public const byte OPT_TTYPE = 24;
    public const byte OPT_NAWS = 31;

    public static byte[] Command(byte verb, byte option) => new[] { IAC, verb, option };

    public static byte[] Subneg(byte option, params byte[] data)
    {
        var buf = new byte[3 + data.Length + 2];
        buf[0] = IAC; buf[1] = SB; buf[2] = option;
        Buffer.BlockCopy(data, 0, buf, 3, data.Length);
        buf[^2] = IAC; buf[^1] = SE;
        return buf;
    }

    public static string StripIac(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];
            if (b == IAC)
            {
                if (i + 1 >= data.Length) break;
                byte cmd = data[++i];
                if (cmd == IAC) { sb.Append((char)0xFF); i++; continue; }
                if (cmd == DO || cmd == DONT || cmd == WILL || cmd == WONT)
                { i += 2; continue; }
                if (cmd == SB)
                {
                    i++;
                    while (i < data.Length)
                    {
                        if (data[i] == IAC && i + 1 < data.Length && data[i + 1] == SE)
                        { i += 2; break; }
                        i++;
                    }
                    continue;
                }
                i++;
                continue;
            }
            sb.Append((char)b);
            i++;
        }
        return sb.ToString();
    }
}
