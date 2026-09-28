using System.Text;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// THE one backslash-escape table for the whole product: <c>\" \\ \r \n \t \e (ESC)
/// \xHH (hex byte) \NNN (octal, 1-3 digits, max \377)</c>. Same dialect as
/// EscapeSequenceFormatter (programmable keys), so a carriage return is written
/// <c>\r</c> everywhere.
///
/// Two callers share it so the notation can NEVER drift between surfaces:
///  - the script parser, decoding a quoted string literal while it scans for the
///    closing quote (<see cref="TryDecodeEscape"/>, one escape at a time);
///  - non-script surfaces (MCP tool arguments) where the value arrives WITHOUT
///    surrounding quotes and the whole thing must be decoded (<see cref="TryDecode"/>).
///
/// This is exactly why the MCP <c>terminal_send text=SYSTEM\r</c> bug happened: the
/// JSON value skipped the parser, so <c>\r</c> reached the wire as two literal
/// characters instead of a carriage return. Flagged parameters now run through
/// <see cref="TryDecode"/> at the MCP boundary.
/// </summary>
public static class ScriptStringEscapes
{
    /// <summary>
    /// Decodes ONE backslash escape. <paramref name="pos"/> must point at the
    /// backslash in <paramref name="s"/>. On success the decoded character(s) are
    /// appended to <paramref name="sb"/>, <paramref name="consumed"/> is set to how
    /// many source characters the escape used (always >= 2), and it returns true.
    /// On a malformed escape it returns false with <paramref name="error"/> set and
    /// leaves <paramref name="sb"/> untouched past what earlier calls wrote.
    /// </summary>
    public static bool TryDecodeEscape(string s, int pos, StringBuilder sb, out int consumed, out string? error)
    {
        error = null;
        consumed = 0;

        // A backslash with nothing after it is always a mistake — the caller decides
        // how to phrase it per surface, but we still guard here.
        if (pos + 1 >= s.Length)
        {
            error = "Dangling backslash at end of string — use \\\\ for a literal backslash";
            return false;
        }

        char next = s[pos + 1];
        switch (next)
        {
            case '"': sb.Append('"'); consumed = 2; return true;
            case '\\': sb.Append('\\'); consumed = 2; return true;
            case 'r': sb.Append('\r'); consumed = 2; return true;
            case 'n': sb.Append('\n'); consumed = 2; return true;
            case 't': sb.Append('\t'); consumed = 2; return true;
            case 'e': sb.Append('\x1B'); consumed = 2; return true;
            case 'x':
            {
                // \xHH — exactly two hex digits, like \x1B for ESC.
                int high = pos + 2 < s.Length ? HexDigit(s[pos + 2]) : -1;
                int low = pos + 3 < s.Length ? HexDigit(s[pos + 3]) : -1;
                if (high < 0 || low < 0)
                {
                    error = "\\x needs two hex digits, e.g. \\x1B for ESC";
                    return false;
                }
                sb.Append((char)(high * 16 + low));
                consumed = 4;
                return true;
            }
            default:
                if (next >= '0' && next <= '7')
                {
                    // \NNN — octal, one to three digits, max \377 (255).
                    int j = pos + 1;
                    int octal = 0;
                    int digits = 0;
                    while (j < s.Length && digits < 3 && s[j] >= '0' && s[j] <= '7')
                    {
                        octal = octal * 8 + (s[j] - '0');
                        j++;
                        digits++;
                    }
                    if (octal > 255)
                    {
                        error = $"Octal escape \\{s.Substring(pos + 1, digits)} is over \\377 (255)";
                        return false;
                    }
                    sb.Append((char)octal);
                    consumed = 1 + digits; // the backslash plus the octal digits
                    return true;
                }
                error = $"Unknown escape \\{next} — valid: \\\" \\\\ \\r \\n \\t \\e \\xHH \\NNN (octal)";
                return false;
        }
    }

    /// <summary>
    /// Decodes an ENTIRE unquoted string: every <c>\x</c> escape is applied and
    /// everything else is copied verbatim. This is what surfaces WITHOUT the script's
    /// quoting layer (MCP tool arguments) use, so <c>\r</c> there means the same byte
    /// it does inside a script's quoted string. A dangling or malformed escape fails
    /// with <paramref name="error"/> set; on failure <paramref name="decoded"/> is the
    /// original input unchanged.
    /// </summary>
    public static bool TryDecode(string raw, out string decoded, out string? error)
    {
        error = null;
        decoded = raw;
        if (raw.Length == 0 || raw.IndexOf('\\') < 0)
        {
            // Nothing to do — no backslash means no escapes (the common case, and it
            // keeps a plain value byte-for-byte identical).
            return true;
        }

        var sb = new StringBuilder(raw.Length);
        int pos = 0;
        while (pos < raw.Length)
        {
            char c = raw[pos];
            if (c == '\\')
            {
                if (!TryDecodeEscape(raw, pos, sb, out int consumed, out error))
                {
                    return false;
                }
                pos += consumed;
                continue;
            }
            sb.Append(c);
            pos++;
        }
        decoded = sb.ToString();
        return true;
    }

    /// <summary>
    /// Hex digit value, or -1 when the character is not a hex digit.
    /// </summary>
    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }
}
