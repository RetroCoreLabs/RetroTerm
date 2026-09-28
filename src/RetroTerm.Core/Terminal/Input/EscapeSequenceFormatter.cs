using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// Bidirectional conversion between human-readable escape format and byte arrays.
    /// Display format uses: \r (CR), \n (LF), \t (TAB), \e (ESC), \xNN (hex), \\ (backslash).
    /// Stored format uses each char as a raw byte value.
    /// </summary>
    public static class EscapeSequenceFormatter
    {
        /// <summary>
        /// Parse human-readable escape format into bytes.
        /// Supports: \r \n \t \e \xNN \\ and literal characters.
        /// </summary>
        public static byte[] Parse(string input)
        {
            if (string.IsNullOrEmpty(input))
                return Array.Empty<byte>();

            var result = new List<byte>();
            var i = 0;

            while (i < input.Length)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    var next = input[i + 1];

                    switch (next)
                    {
                        case 'r':
                            result.Add(0x0D);
                            i += 2;
                            break;

                        case 'n':
                            result.Add(0x0A);
                            i += 2;
                            break;

                        case 't':
                            result.Add(0x09);
                            i += 2;
                            break;

                        case 'e':
                            result.Add(0x1B);
                            i += 2;
                            break;

                        case 'x':
                            if (i + 3 < input.Length)
                            {
                                var hexString = input.Substring(i + 2, 2);
                                if (byte.TryParse(hexString, NumberStyles.HexNumber, null, out var hexByte))
                                {
                                    result.Add(hexByte);
                                    i += 4;
                                }
                                else
                                {
                                    throw new FormatException($"Invalid hex sequence: \\x{hexString}");
                                }
                            }
                            else
                            {
                                throw new FormatException("Incomplete hex sequence \\xNN");
                            }
                            break;

                        case '\\':
                            result.Add((byte)'\\');
                            i += 2;
                            break;

                        default:
                            // Unknown escape, treat as literal backslash
                            result.Add((byte)'\\');
                            i++;
                            break;
                    }
                }
                else
                {
                    result.Add((byte)input[i]);
                    i++;
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Convert a stored byte string (each char = a byte value) to human-readable escape format.
        /// </summary>
        public static string Format(string storedByteString)
        {
            if (string.IsNullOrEmpty(storedByteString))
                return string.Empty;

            var sb = new StringBuilder(storedByteString.Length * 2);

            for (int i = 0; i < storedByteString.Length; i++)
            {
                byte b = (byte)storedByteString[i];

                switch (b)
                {
                    case 0x0D:
                        sb.Append("\\r");
                        break;
                    case 0x0A:
                        sb.Append("\\n");
                        break;
                    case 0x09:
                        sb.Append("\\t");
                        break;
                    case 0x1B:
                        sb.Append("\\e");
                        break;
                    case (byte)'\\':
                        sb.Append("\\\\");
                        break;
                    default:
                        if (b >= 0x20 && b <= 0x7E)
                        {
                            // Printable ASCII (backslash already handled above)
                            sb.Append((char)b);
                        }
                        else
                        {
                            sb.Append("\\x");
                            sb.Append(b.ToString("X2"));
                        }
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parse escape format to a stored byte string (each char = a byte value).
        /// Convenience method combining Parse + conversion to string.
        /// </summary>
        public static string ParseToString(string input)
        {
            var bytes = Parse(input);
            var sb = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append((char)bytes[i]);
            }
            return sb.ToString();
        }
    }
}
