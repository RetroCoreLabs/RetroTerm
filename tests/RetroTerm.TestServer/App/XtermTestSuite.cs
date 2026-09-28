using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;

namespace RetroTerm.TestServer.App;

/// <summary>
/// The xterm half of the test server - the modern features a program like vim or tmux relies on.
/// </summary>
/// <remarks>
/// These are the sequences that were checked against spec\DEC\xterm-ctlseqs.txt for modes 1004,
/// 1006 and 2004 only. Everything else in this menu was written from the emulator's own code, so
/// this suite is a way to SEE what it does, not a proof that it is right.
///
/// Three of these tests are interactive because nothing else can settle them: mouse reporting,
/// bracketed paste and focus reporting all need a person doing something with a pointer, a
/// clipboard or another window.
/// </remarks>
public partial class TestServerApp
{
    #region xterm menu routing

    /// <summary>
    /// Runs one entry of the xterm tests menu.
    /// </summary>
    private async Task HandleXtermTestsMenuAsync(TelnetSession session, char key)
    {
        switch (key)
        {
            case '1':
                await RunXterm_AlternateScreenAsync(session);
                await WriteMenuAsync(session);
                break;
            case '2':
                await RunXterm_MouseReportingAsync(session);
                await WriteMenuAsync(session);
                break;
            case '3':
                await RunXterm_BracketedPasteAsync(session);
                await WriteMenuAsync(session);
                break;
            case '4':
                await RunXterm_FocusReportingAsync(session);
                await WriteMenuAsync(session);
                break;
            case '5':
                await RunXterm_WindowTitleAsync(session);
                await WriteMenuAsync(session);
                break;
            case '6':
                await RunXterm_ColorDepthAsync(session);
                await WriteMenuAsync(session);
                break;
            case '7':
                await RunXterm_EditingEdgeCasesAsync(session);
                await WriteMenuAsync(session);
                break;
            case '8':
                await RunXterm_CursorStyleAsync(session);
                await WriteMenuAsync(session);
                break;
        }
    }

    /// <summary>
    /// Prints the xterm tests menu.
    /// </summary>
    internal void WriteXtermTestsMenu(StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== xterm / Modern Terminal Tests ===\x1b[0m");
        writer.AppendLine("1. Alternate Screen - 1049, the vim and less behaviour");
        writer.AppendLine("2. Mouse Reporting - 1000/1002/1003 with 1006 encoding (interactive)");
        writer.AppendLine("3. Bracketed Paste - 2004 (interactive, needs a real paste)");
        writer.AppendLine("4. Focus Reporting - 1004 (interactive, click another window)");
        writer.AppendLine("5. Window Title - OSC 0/2 and the title stack");
        writer.AppendLine("6. Colour Depth - 16, 256 and 24-bit ramps");
        writer.AppendLine("7. Editing Edge Cases - REP, ECH, ICH/DCH, IL/DL, SU/SD, last column");
        writer.AppendLine("8. Cursor Style - DECSCUSR shapes, show and hide");
        writer.AppendLine("0/B. Back to Main Menu");
    }

    #endregion

    #region xterm tests

    /// <summary>
    /// The alternate screen buffer, which is what makes quitting vim put the shell back.
    /// </summary>
    private async Task RunXterm_AlternateScreenAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Alternate Screen");

        await session.WriteAsync("This text is on the MAIN screen. Remember how it looks.\r\n");
        for (int i = 1; i <= 8; i++)
        {
            await session.WriteAsync($"  main screen line {i}\r\n");
        }
        await session.WriteAsync("\r\nPress Enter to switch to the alternate screen (CSI ? 1049 h)...");
        await session.WaitForEnterAsync();

        await session.WriteAsync("\x1b[?1049h");
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\x1b[1;35mThis is the ALTERNATE screen.\x1b[0m\r\n\r\n");
        await session.WriteAsync("Two things to check while you are here:\r\n");
        await session.WriteAsync("  1. Scrolling back must NOT show the main screen's text.\r\n");
        await session.WriteAsync("  2. Nothing written here may end up in the scrollback.\r\n\r\n");
        for (int i = 1; i <= 30; i++)
        {
            await session.WriteAsync($"  alternate line {i}\r\n");
        }
        await session.WriteAsync("\r\nPress Enter to switch back (CSI ? 1049 l)...");
        await session.WaitForEnterAsync();

        await session.WriteAsync("\x1b[?1049l");
        await session.WriteAsync("\r\n\x1b[1;37mExpected:\x1b[0m the main screen came back exactly as it was, the\r\n");
        await session.WriteAsync("cursor is where it was, and the scrollback has no alternate lines in it.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Mouse reporting, which is the largest surface in this program that no automated test can
    /// judge.
    /// </summary>
    /// <remarks>
    /// Every event that arrives is decoded and printed. The 1006 (SGR) encoding is used because it
    /// is the only one that survives past column 95, and the older X10 encoding is offered as a
    /// comparison because some hosts still ask for it.
    /// </remarks>
    private async Task RunXterm_MouseReportingAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Mouse Reporting");

        await session.WriteAsync("Keys: \x1b[1;37m1\x1b[0m clicks only (1000)   \x1b[1;37m2\x1b[0m clicks and drag (1002)\r\n");
        await session.WriteAsync("      \x1b[1;37m3\x1b[0m all motion (1003)     \x1b[1;37m0\x1b[0m off\r\n");
        await session.WriteAsync("      \x1b[1;37mS\x1b[0m SGR encoding (1006)   \x1b[1;37mX\x1b[0m old X10 encoding\r\n");
        await session.WriteAsync("      \x1b[1;37mQ\x1b[0m quit this test\r\n\r\n");
        await session.WriteAsync("Starting with click reporting and SGR encoding.\r\n");
        await session.WriteAsync("\x1b[1;37mWhat to judge:\x1b[0m the row and column reported are the cell you clicked,\r\n");
        await session.WriteAsync("the wheel reports buttons 64 and 65, and holding Shift gives you LOCAL\r\n");
        await session.WriteAsync("selection instead of a report - that last one is a choice, not a rule.\r\n\r\n");

        await session.WriteAsync("\x1b[?1006h\x1b[?1000h");

        try
        {
            while (true)
            {
                var input = await session.ReadInputAsync();

                if (input.Type == InputType.Character)
                {
                    char key = char.ToUpperInvariant(input.CommandChar);
                    if (key == 'Q')
                    {
                        break;
                    }
                    if (key == '0')
                    {
                        await session.WriteAsync("\x1b[?1000l\x1b[?1002l\x1b[?1003l");
                        await session.WriteAsync("all mouse reporting off\r\n");
                        continue;
                    }
                    if (key == '1' || key == '2' || key == '3')
                    {
                        // Turn the other two off first, or the terminal is left in the widest of them.
                        await session.WriteAsync("\x1b[?1000l\x1b[?1002l\x1b[?1003l");
                        string mode = key == '1' ? "1000" : (key == '2' ? "1002" : "1003");
                        await session.WriteAsync($"\x1b[?{mode}h");
                        await session.WriteAsync($"mouse mode {mode} on\r\n");
                        continue;
                    }
                    if (key == 'S')
                    {
                        await session.WriteAsync("\x1b[?1006h");
                        await session.WriteAsync("SGR encoding on - reports look like CSI < b ; col ; row M\r\n");
                        continue;
                    }
                    if (key == 'X')
                    {
                        await session.WriteAsync("\x1b[?1006l");
                        await session.WriteAsync("old X10 encoding - reports look like CSI M followed by three bytes\r\n");
                        continue;
                    }
                    continue;
                }

                if (input.Type == InputType.EscapeSequence)
                {
                    await session.WriteAsync($"  {ToVisible(input.Value)}   hex: {ToHex(input.Value)}");
                    await session.WriteAsync($"   {DescribeMouseReport(input.Value)}\r\n");
                }
            }
        }
        finally
        {
            // Whatever happens, do not leave the terminal reporting the mouse to a menu.
            await session.WriteAsync("\x1b[?1000l\x1b[?1002l\x1b[?1003l\x1b[?1006l");
        }

        await session.WriteAsync("\r\nMouse reporting turned off.\r\n");
        await EndTestAsync(session);
    }

    /// <summary>
    /// Turns an SGR mouse report into words, so the screen says what happened rather than showing
    /// three numbers.
    /// </summary>
    /// <remarks>
    /// Only the SGR form is decoded. The X10 form carries its numbers as raw bytes offset by 32,
    /// which is exactly the encoding that breaks past column 95 - seeing it undecoded here is a
    /// fair reminder of why 1006 exists.
    /// </remarks>
    internal static string DescribeMouseReport(string report)
    {
        // SGR form: ESC [ < button ; column ; row M-or-m
        if (report.Length < 6 || report[0] != 0x1B || report[1] != '[' || report[2] != '<')
        {
            return "(not an SGR report)";
        }

        int button = 0;
        int column = 0;
        int row = 0;
        int field = 0;
        for (int i = 3; i < report.Length; i++)
        {
            char c = report[i];
            if (c >= '0' && c <= '9')
            {
                int digit = c - '0';
                if (field == 0) { button = button * 10 + digit; }
                else if (field == 1) { column = column * 10 + digit; }
                else { row = row * 10 + digit; }
                continue;
            }
            if (c == ';')
            {
                field++;
                continue;
            }

            // M is a press, m is a release.
            string action = c == 'M' ? "press" : "release";
            string which;
            if ((button & 64) != 0)
            {
                which = (button & 1) != 0 ? "wheel down" : "wheel up";
            }
            else if ((button & 32) != 0)
            {
                which = "motion";
            }
            else
            {
                int basic = button & 3;
                which = basic == 0 ? "left" : (basic == 1 ? "middle" : (basic == 2 ? "right" : "none"));
            }

            var modifiers = new StringBuilder();
            if ((button & 4) != 0) { modifiers.Append(" +Shift"); }
            if ((button & 8) != 0) { modifiers.Append(" +Alt"); }
            if ((button & 16) != 0) { modifiers.Append(" +Ctrl"); }

            return $"{which} {action} at row {row}, column {column}{modifiers}";
        }

        return "(incomplete report)";
    }

    /// <summary>
    /// Bracketed paste - ten seconds of work that settles whether a pasted command runs by itself.
    /// </summary>
    private async Task RunXterm_BracketedPasteAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Bracketed Paste");

        await session.WriteAsync("Turning bracketed paste ON (CSI ? 2004 h).\r\n\r\n");
        await session.WriteAsync("Now paste something with more than one line in it - copy these two\r\n");
        await session.WriteAsync("lines and paste them back:\r\n\r\n");
        await session.WriteAsync("    echo one\r\n");
        await session.WriteAsync("    echo two\r\n\r\n");
        await session.WriteAsync("\x1b[1;37mExpected:\x1b[0m what arrives is wrapped in ESC [ 200 ~ and ESC [ 201 ~,\r\n");
        await session.WriteAsync("and the newline INSIDE the paste arrives as data, not as Enter.\r\n");
        await session.WriteAsync("Press Q to finish.\r\n\r\n");

        await session.WriteAsync("\x1b[?2004h");

        try
        {
            while (true)
            {
                var input = await session.ReadInputAsync();
                if (input.Type == InputType.Character && char.ToUpperInvariant(input.CommandChar) == 'Q')
                {
                    break;
                }

                await session.WriteAsync($"  {ToVisible(input.Value)}   hex: {ToHex(input.Value)}\r\n");

                if (input.Value.Contains("200~"))
                {
                    await session.WriteAsync("  \x1b[32m^ paste START marker\x1b[0m\r\n");
                }
                if (input.Value.Contains("201~"))
                {
                    await session.WriteAsync("  \x1b[32m^ paste END marker\x1b[0m\r\n");
                }
            }
        }
        finally
        {
            await session.WriteAsync("\x1b[?2004l");
        }

        await session.WriteAsync("\r\nBracketed paste turned off. Paste once more now and the markers\r\n");
        await session.WriteAsync("must be gone - a terminal that still sends them is not honouring the reset.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Focus reporting - the terminal telling the host when the window gains or loses focus.
    /// </summary>
    private async Task RunXterm_FocusReportingAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Focus Reporting");

        await session.WriteAsync("Turning focus reporting ON (CSI ? 1004 h).\r\n\r\n");
        await session.WriteAsync("Click on another window, then click back on this one. Do it twice.\r\n");
        await session.WriteAsync("\x1b[1;37mExpected:\x1b[0m CSI I when focus arrives, CSI O when it leaves.\r\n");
        await session.WriteAsync("Press Q to finish.\r\n\r\n");

        await session.WriteAsync("\x1b[?1004h");

        try
        {
            while (true)
            {
                var input = await session.ReadInputAsync();
                if (input.Type == InputType.Character && char.ToUpperInvariant(input.CommandChar) == 'Q')
                {
                    break;
                }

                if (input.Value == "\x1b[I")
                {
                    await session.WriteAsync("  \x1b[32mfocus IN\x1b[0m\r\n");
                }
                else if (input.Value == "\x1b[O")
                {
                    await session.WriteAsync("  \x1b[33mfocus OUT\x1b[0m\r\n");
                }
                else
                {
                    await session.WriteAsync($"  {ToVisible(input.Value)}   hex: {ToHex(input.Value)}\r\n");
                }
            }
        }
        finally
        {
            await session.WriteAsync("\x1b[?1004l");
        }

        await EndTestAsync(session);
    }

    /// <summary>
    /// The window title, and the title stack that lets a program borrow it and give it back.
    /// </summary>
    private async Task RunXterm_WindowTitleAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Window Title");

        await session.WriteAsync("Setting the title to 'RetroTerm test - one' (OSC 2)...\r\n");
        await session.WriteAsync("\x1b]2;RetroTerm test - one\x07");
        await session.WriteAsync("Press Enter when you have looked at the tab or window title...");
        await session.WaitForEnterAsync();

        await session.WriteAsync("\r\nPushing the title on the stack (CSI 22 ; 0 t) and setting a new one...\r\n");
        await session.WriteAsync("\x1b[22;0t");
        await session.WriteAsync("\x1b]2;RetroTerm test - TWO\x07");
        await session.WriteAsync("Press Enter...");
        await session.WaitForEnterAsync();

        await session.WriteAsync("\r\nPopping the old title back (CSI 23 ; 0 t)...\r\n");
        await session.WriteAsync("\x1b[23;0t");
        await session.WriteAsync("\r\n\x1b[1;37mExpected:\x1b[0m the title is 'RetroTerm test - one' again.\r\n");
        await session.WriteAsync("This is exactly what tmux does when it borrows your title bar.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// The three colour depths side by side, which is the only honest way to see a ramp that has
    /// been collapsed into fewer colours than it claims.
    /// </summary>
    private async Task RunXterm_ColorDepthAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Colour Depth");

        await session.WriteAsync("\x1b[1;37m16 colours - normal then bright:\x1b[0m\r\n");
        for (int c = 0; c < 8; c++)
        {
            await session.WriteAsync($"\x1b[4{c}m  ");
        }
        await session.WriteAsync("\x1b[0m\r\n");
        for (int c = 0; c < 8; c++)
        {
            await session.WriteAsync($"\x1b[10{c}m  ");
        }
        await session.WriteAsync("\x1b[0m\r\n\r\n");

        await session.WriteAsync("\x1b[1;37m256 colours - the 6x6x6 cube:\x1b[0m\r\n");
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 36; col++)
            {
                int index = 16 + row * 36 + col;
                await session.WriteAsync($"\x1b[48;5;{index}m ");
            }
            await session.WriteAsync("\x1b[0m\r\n");
        }
        await session.WriteAsync("\r\n\x1b[1;37m256 colours - the 24-step grey ramp:\x1b[0m\r\n");
        for (int i = 232; i < 256; i++)
        {
            await session.WriteAsync($"\x1b[48;5;{i}m  ");
        }
        await session.WriteAsync("\x1b[0m\r\n\r\n");

        await session.WriteAsync("\x1b[1;37m24-bit colour - a red to blue sweep in 72 steps:\x1b[0m\r\n");
        for (int i = 0; i < 72; i++)
        {
            int red = 255 - (i * 255 / 71);
            int blue = i * 255 / 71;
            await session.WriteAsync($"\x1b[48;2;{red};0;{blue}m ");
        }
        await session.WriteAsync("\x1b[0m\r\n\r\n");

        await session.WriteAsync("\x1b[1;37mWhat to judge:\x1b[0m the 24-bit sweep must be smooth. If it steps in\r\n");
        await session.WriteAsync("visible bands it is being folded into 256 colours. And the first colour\r\n");
        await session.WriteAsync("of the sweep is RED and the last is BLUE - if they are swapped, the\r\n");
        await session.WriteAsync("channels are the wrong way round.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// The editing sequences whose corner cases keep producing defects - all shown against a ruler
    /// so a one-cell error is countable.
    /// </summary>
    /// <remarks>
    /// Every case here is one that the xterm.js screen corpus caught at least once: what a repeat
    /// counts as its last character, what disarms the pending wrap at the right margin, and whether
    /// an insert or delete moves the cursor.
    /// </remarks>
    private async Task RunXterm_EditingEdgeCasesAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Editing Edge Cases");

        await WriteRulerAsync(session);

        await session.WriteAsync("\x1b[4;1HREP: 'x' then CSI 5 b  ->  ");
        await session.WriteAsync("x\x1b[5b");
        await session.WriteAsync("   expected: six x characters");

        await session.WriteAsync("\x1b[5;1HREP after a cursor move: 'abcdefg' CSI 3 D CSI b  ->  ");
        await session.WriteAsync("abcdefg\x1b[3D\x1b[b");
        await session.WriteAsync("   expected: abcdefg unchanged, because the move ended the repeat");

        await session.WriteAsync("\x1b[6;1HECH: 'ABCDEFGH' back 6, CSI 3 X  ->  ");
        await session.WriteAsync("ABCDEFGH\x1b[6D\x1b[3X");
        await session.WriteAsync("   expected: AB then three blanks then FGH");

        await session.WriteAsync("\x1b[7;1HICH and DCH: '12345678' back 4, CSI 3 @ then CSI 2 P  ->  ");
        await session.WriteAsync("12345678\x1b[4D\x1b[3@\x1b[2P");
        await session.WriteAsync("   expected: one column of blank inserted overall");

        await session.WriteAsync("\x1b[9;1HSU and SD inside a region - rows 10 to 14:");
        await session.WriteAsync("\x1b[10;14r");                       // a scrolling region
        for (int row = 10; row <= 14; row++)
        {
            await session.WriteAsync($"\x1b[{row};1Hregion row {row}");
        }
        await session.WriteAsync("\x1b[2S");                            // SU - pan up 2
        await session.WriteAsync("\x1b[1T");                            // SD - pan down 1
        await session.WriteAsync("\x1b[r");                             // region off
        await session.WriteAsync("\x1b[15;1H   expected: the region scrolled by one net, rows outside it untouched");

        await session.WriteAsync("\x1b[17;1HThe last column - a character at column 80 does NOT wrap until the next one:");
        await session.WriteAsync("\x1b[18;80HA");                       // arms the pending wrap
        await session.WriteAsync("\x1b[18;1H<- 'A' is at column 80 and the cursor is still on this row");
        await session.WriteAsync("\x1b[19;80HB");
        await session.WriteAsync("C");                                  // this one wraps
        await session.WriteAsync("\x1b[20;1H   expected: 'C' is at the START of row 20, not beside B");

        await session.WriteAsync("\x1b[22;1H\x1b[1;37mCount against the ruler on row 1.\x1b[0m One cell out is a defect.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Prints a column ruler on the top two rows so a one-cell error can be counted rather than
    /// guessed at.
    /// </summary>
    private static async Task WriteRulerAsync(TelnetSession session)
    {
        var tens = new StringBuilder(80);
        var units = new StringBuilder(80);
        for (int col = 1; col <= 80; col++)
        {
            tens.Append(col % 10 == 0 ? (char)('0' + (col / 10) % 10) : ' ');
            units.Append((char)('0' + col % 10));
        }
        await session.WriteAsync("\x1b[1;1H\x1b[2;37m" + tens.ToString() + "\x1b[0m");
        await session.WriteAsync("\x1b[2;1H\x1b[2;37m" + units.ToString() + "\x1b[0m");
    }

    /// <summary>
    /// The cursor shapes, which are worth seeing because a blink that never stops is a defect the
    /// automated tests cannot feel.
    /// </summary>
    private async Task RunXterm_CursorStyleAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Cursor Style");

        string[] names =
        {
            "0 - the terminal's default",
            "1 - blinking block",
            "2 - steady block",
            "3 - blinking underline",
            "4 - steady underline",
            "5 - blinking bar",
            "6 - steady bar"
        };

        for (int style = 0; style < names.Length; style++)
        {
            await session.WriteAsync($"\x1b[{style} q");
            await session.WriteAsync($"Cursor style {names[style]} - look at the cursor, then press Enter.");
            await session.WaitForEnterAsync();
            await session.WriteAsync("\r\n");
        }

        await session.WriteAsync("\r\nHiding the cursor (CSI ? 25 l). Press Enter to bring it back...");
        await session.WriteAsync("\x1b[?25l");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[?25h");
        await session.WriteAsync("\r\nCursor shown again, and put back to the default shape.\r\n");
        await session.WriteAsync("\x1b[0 q");

        await EndTestAsync(session);
    }

    #endregion
}
