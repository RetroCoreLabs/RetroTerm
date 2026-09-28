using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;

namespace RetroTerm.TestServer.App;

/// <summary>
/// The DEC half of the test server - VT220, VT320, VT340 and VT420 features driven from a host.
/// </summary>
/// <remarks>
/// Why this exists: until it did, the server's menus were VT100/ANSI and TDV only, so every DEC
/// feature written into the emulators - page memory, the rectangle family, margins, soft fonts,
/// selective erase - could only be exercised by a unit test written by whoever wrote the code.
/// A person could not reach any of it through a connection.
///
/// Each test prints what the manual says should happen BEFORE it does it, so the screen can be
/// judged without holding the manual open. Where a reply comes back from the terminal it is shown
/// as text and as hex, because a report that looks right and has the wrong byte count is a real
/// defect and the eye will not catch it.
///
/// Reference documents behind these tests:
///  - spec\DEC\VT420-Programmer-Reference-EK-VT420-RM-002.pdf for margins, rectangles, page memory.
///  - Ronny's OCR of EK-VT3XX-TP-001 (VT330/VT340 text programming) for soft fonts and status line.
///  - spec\DEC\xterm-ctlseqs.txt where DEC and xterm overlap.
/// </remarks>
public partial class TestServerApp
{
    #region DEC menu routing

    /// <summary>
    /// Runs one entry of the DEC Terminal Tests menu.
    /// </summary>
    private async Task HandleDECTestsMenuAsync(TelnetSession session, char key)
    {
        switch (key)
        {
            case '1':
                await RunDEC_ReportsAsync(session);
                await WriteMenuAsync(session);
                break;
            case '2':
                await RunDEC_PageMemoryAsync(session);
                await WriteMenuAsync(session);
                break;
            case '3':
                await RunDEC_RectangleOpsAsync(session);
                await WriteMenuAsync(session);
                break;
            case '4':
                await RunDEC_MarginsAsync(session);
                await WriteMenuAsync(session);
                break;
            case '5':
                await RunDEC_SoftFontAndUdkAsync(session);
                await WriteMenuAsync(session);
                break;
            case '6':
                await RunDEC_StatusLineAsync(session);
                await WriteMenuAsync(session);
                break;
            case '7':
                await RunDEC_NationalSetsAsync(session);
                await WriteMenuAsync(session);
                break;
            case '8':
                await RunDEC_ConformanceLevelAsync(session);
                await WriteMenuAsync(session);
                break;
            case '9':
                await RunDEC_Vt52ModeAsync(session);
                await WriteMenuAsync(session);
                break;
            case 'A':
                await RunDEC_SelectiveEraseAsync(session);
                await WriteMenuAsync(session);
                break;
        }
    }

    /// <summary>
    /// Prints the DEC Terminal Tests menu.
    /// </summary>
    internal void WriteDECTestsMenu(StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== DEC Terminal Tests (VT220/320/340/420) ===\x1b[0m");
        writer.AppendLine("1. Reports and Identity - DA1/DA2/DA3, DSR, CPR, DECRQSS, DECRQM");
        writer.AppendLine("2. Page Memory - DECSLPP, NP, PP, PPA, PPR, PPB");
        writer.AppendLine("3. Rectangle Operations - DECFRA, DECCRA, DECERA, DECSERA, DECRQCRA");
        writer.AppendLine("4. Margins and Column Editing - DECLRMM, DECSLRM, DECIC/DECDC, SL/SR");
        writer.AppendLine("5. Soft Font and User Keys - DECDLD (96 and 94 char), DECUDK");
        writer.AppendLine("6. Status Line - DECSSDT, DECSASD");
        writer.AppendLine("7. National Character Sets - NRCS, incl. Norwegian/Danish");
        writer.AppendLine("8. Conformance Levels - DECSCL 61..65, re-queried with DA");
        writer.AppendLine("9. VT52 Mode - enter, move, identify, and come back");
        writer.AppendLine("A. Selective Erase - DECSCA, DECSEL, DECSED");
        writer.AppendLine("0/B. Back to Main Menu");
    }

    #endregion

    #region DEC helpers

    /// <summary>
    /// Sends one query, waits for the reply, and prints it as text and as hex beside what was
    /// expected.
    /// </summary>
    /// <remarks>
    /// The expected text is printed whether or not the reply matches. A person reading the screen
    /// can then judge it; this server deliberately does not decide pass or fail for reports, because
    /// what is correct depends on which terminal is connected.
    /// </remarks>
    private static async Task AskAndShowAsync(TelnetSession session, string label, string query, string expected)
    {
        await session.WriteAsync($"\x1b[1;37m{label}\x1b[0m\r\n");
        await session.WriteAsync($"  sent    : {ToVisible(query)}\r\n");

        await session.WriteAsync(query);
        var reply = await CaptureResponseWithPollingAsync(session, 1200);

        if (reply == null || reply.Length == 0)
        {
            await session.WriteAsync("  reply   : \x1b[33m(nothing came back)\x1b[0m\r\n");
        }
        else
        {
            await session.WriteAsync($"  reply   : {ToVisible(reply)}\r\n");
            await session.WriteAsync($"  hex     : {ToHex(reply)}\r\n");
        }

        await session.WriteAsync($"  expected: {expected}\r\n\r\n");
    }

    /// <summary>
    /// Formats a reply as space-separated hex, so a reply with the right shape and the wrong length
    /// is visible.
    /// </summary>
    internal static string ToHex(string value)
    {
        var sb = new StringBuilder(value.Length * 3);
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }
            sb.Append(((int)value[i]).ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Clears the screen and prints a test heading.
    /// </summary>
    private static async Task BeginTestAsync(TelnetSession session, string title)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync($"\x1b[1;33m=== {title} ===\x1b[0m\r\n\r\n");
    }

    /// <summary>
    /// Prints the "press Enter" footer and waits, so a drawn screen can be looked at before the
    /// menu paints over it.
    /// </summary>
    private static async Task EndTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\r\n\x1b[32mPress Enter to return to the menu...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    #endregion

    #region DEC tests

    /// <summary>
    /// Every report a DEC terminal answers, with the expected shape beside each reply.
    /// </summary>
    private async Task RunDEC_ReportsAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Reports and Identity");

        await AskAndShowAsync(session, "DA1 - primary device attributes", "\x1b[c",
            "CSI ? 62 ; ... c for a VT220 and up. The first number is the class: 62=VT200, 63=VT300, 64=VT400");
        await AskAndShowAsync(session, "DA2 - secondary device attributes", "\x1b[>c",
            "CSI > Pp ; Pv ; Pc c - model, firmware version, keyboard");
        await AskAndShowAsync(session, "DA3 - tertiary device attributes", "\x1b[=c",
            "DCS ! | <unit id> ST on a VT400. Older terminals answer nothing at all");
        await AskAndShowAsync(session, "DSR - are you ready", "\x1b[5n",
            "CSI 0 n meaning ready, with no malfunction");

        // Put the cursor somewhere known first, so a wrong CPR is obvious rather than plausible.
        await session.WriteAsync("\x1b[10;20H");
        await AskAndShowAsync(session, "CPR - where is the cursor (moved to row 10, column 20)", "\x1b[6n",
            "CSI 10 ; 20 R");
        await AskAndShowAsync(session, "DECXCPR - where is the cursor, including the page", "\x1b[?6n",
            "CSI ? 10 ; 20 ; 1 R - the third number is the page");

        await session.WriteAsync("\x1b[1;24r");   // a scrolling region, so DECRQSS has something to report
        await AskAndShowAsync(session, "DECRQSS - what is the scrolling region", "\x1bP$qr\x1b\\",
            "DCS 1 $ r 1 ; 24 r ST - the leading 1 means the request was valid");
        await session.WriteAsync("\x1b[1;31m");
        await AskAndShowAsync(session, "DECRQSS - what are the current attributes", "\x1bP$qm\x1b\\",
            "DCS 1 $ r 0 ; 1 ; 31 m ST");
        await session.WriteAsync("\x1b[0m");
        await AskAndShowAsync(session, "DECRQSS - what is the conformance level", "\x1bP$q\"p\x1b\\",
            "DCS 1 $ r 6x ; 1 \" p ST");
        await AskAndShowAsync(session, "DECRQSS - an invalid request", "\x1bP$qZZ\x1b\\",
            "DCS 0 $ r ST - a LEADING ZERO, which is how a terminal refuses");

        await AskAndShowAsync(session, "DECRQM - is autowrap on", "\x1b[?7$p",
            "CSI ? 7 ; 1 $ y when set, ; 2 when reset, ; 0 when the mode is not recognised");
        await AskAndShowAsync(session, "DECRQM - is origin mode on", "\x1b[?6$p",
            "CSI ? 6 ; 2 $ y - reset, unless something turned it on");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Page memory - the VT320 and VT420 feature that keeps several screens of text and pages
    /// between them.
    /// </summary>
    private async Task RunDEC_PageMemoryAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Page Memory");

        await session.WriteAsync("The terminal keeps more than one page of text. Text written to page 1\r\n");
        await session.WriteAsync("must still be there after paging away and back.\r\n\r\n");
        await session.WriteAsync("Press Enter to write three pages...");
        await session.WaitForEnterAsync();

        // DECSLPP: CSI Ps t with Ps of 24 or more sets the page length in lines. 24 lines per page
        // on a 72-line memory is three pages.
        await session.WriteAsync("\x1b[24t");

        await session.WriteAsync("\x1b[2J\x1b[H\x1b[1;32mTHIS IS PAGE 1\x1b[0m\r\nIf you can read this after paging back, page memory holds.");
        await session.WriteAsync("\x1b[U");        // NP - next page
        await session.WriteAsync("\x1b[2J\x1b[H\x1b[1;33mTHIS IS PAGE 2\x1b[0m\r\nPaged to with NP (CSI U).");
        await session.WriteAsync("\x1b[U");
        await session.WriteAsync("\x1b[2J\x1b[H\x1b[1;35mTHIS IS PAGE 3\x1b[0m\r\nPaged to with NP again.");

        await session.WriteAsync("\r\n\r\nPress Enter to go back one page with PP (CSI V)...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[V");
        await session.WriteAsync("\r\n\r\nExpected: PAGE 2. Press Enter for PPA (CSI 1 SP P) - go to page 1...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[1 P");
        await session.WriteAsync("\r\n\r\nExpected: PAGE 1, with its text intact. Press Enter for PPR (forward 2)...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[2 Q");      // PPR - page position relative, forward
        await session.WriteAsync("\r\n\r\nExpected: PAGE 3. Press Enter for PPB (back 2)...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[2 R");      // PPB - page position back
        await session.WriteAsync("\r\n\r\nExpected: PAGE 1 again.\r\n");

        await session.WriteAsync("\r\n\x1b[1;37mWhat to judge:\x1b[0m each page keeps its own text and its own cursor,\r\n");
        await session.WriteAsync("and paging does not scroll or clear anything.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// The VT420 rectangle family, including the overlapping copy that smears when the copy is
    /// done in the wrong direction.
    /// </summary>
    private async Task RunDEC_RectangleOpsAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Rectangle Operations");

        await session.WriteAsync("A grid is drawn first so every rectangle edge can be counted.\r\n\r\n");

        // A background of dots, 20 rows of 60, to make rectangle edges countable.
        for (int row = 3; row <= 22; row++)
        {
            await session.WriteAsync($"\x1b[{row};1H");
            var line = new StringBuilder(60);
            for (int col = 0; col < 60; col++)
            {
                line.Append('.');
            }
            await session.WriteAsync(line.ToString());
        }

        await session.WriteAsync("\x1b[23;1HPress Enter for DECFRA - fill rows 5..9, columns 5..20 with '#'...");
        await session.WaitForEnterAsync();
        // DECFRA: CSI Pch ; Pt ; Pl ; Pb ; Pr $ x - the character comes FIRST, as its code.
        await session.WriteAsync("\x1b[35;5;5;9;20$x");

        await session.WriteAsync("\x1b[23;1H\x1b[KPress Enter for DECCRA - copy that block to rows 12..16, columns 30..45...");
        await session.WaitForEnterAsync();
        // DECCRA: CSI Pts;Pls;Pbs;Prs;Pps ; Ptd;Pld;Ppd $ v
        await session.WriteAsync("\x1b[5;5;9;20;1;12;30;1$v");

        await session.WriteAsync("\x1b[23;1H\x1b[KPress Enter for an OVERLAPPING copy - the one that smears if done wrongly...");
        await session.WaitForEnterAsync();
        // Source and destination overlap by three rows. A copy that walks the wrong way repeats the
        // first row all the way down instead of moving the block.
        await session.WriteAsync("\x1b[5;5;9;20;1;7;5;1$v");

        await session.WriteAsync("\x1b[23;1H\x1b[KExpected: a MOVED block, not a smear. Enter for DECERA - erase rows 12..16, cols 30..45...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[12;30;16;45$z");

        await session.WriteAsync("\x1b[23;1H\x1b[KPress Enter for DECSERA - selective erase, which spares protected cells...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[18;5H\x1b[1\"qPROTECTED\x1b[0\"q  unprotected");
        await session.WriteAsync("\x1b[18;1;18;60$" + "{");
        await session.WriteAsync("\x1b[23;1H\x1b[KExpected on row 18: PROTECTED survives, the rest is gone.\r\n");

        // DECRQCRA is NOT implemented in this emulator as of 2026-08-17 - verified by reading
        // TerminalEmulatorBase.cs, which has no case for the '*' intermediate with a 'y' final.
        // The query is sent anyway, and the expected line says so, because a manual pass that
        // quietly skips a missing feature is how a gap stays invisible.
        await session.WriteAsync("\x1b[24;1H");
        await AskAndShowAsync(session, "DECRQCRA - checksum of rows 5..9, columns 5..20", "\x1b[1;1;5;5;9;20*y",
            "DCS 1 ! ~ xxxx ST per ctlseqs. NOT IMPLEMENTED HERE YET - nothing coming back is the known state");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Left and right margins, column insert and delete, and the one place origin mode and the left
    /// margin are known to disagree.
    /// </summary>
    /// <remarks>
    /// The last section here is P4.4 of the finish plan made visible: with a left margin set and
    /// origin mode on, the manual says CUP homes to the LEFT MARGIN, and libvterm's 15state_mode
    /// script says we home to column 0 instead. This test prints where the cursor actually lands,
    /// so the disagreement can be seen rather than read about.
    /// </remarks>
    private async Task RunDEC_MarginsAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Margins and Column Editing");

        await session.WriteAsync("Enabling left/right margin mode (DECLRMM, CSI ? 69 h) and setting\r\n");
        await session.WriteAsync("margins at columns 20 and 60 (DECSLRM, CSI 20 ; 60 s).\r\n\r\n");
        await session.WriteAsync("\x1b[?69h\x1b[20;60s");

        await session.WriteAsync("\x1b[5;20H");
        for (int i = 0; i < 3; i++)
        {
            await session.WriteAsync("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
        }
        await session.WriteAsync("\x1b[10;1H\x1b[1;37mExpected:\x1b[0m the text wrapped inside columns 20..60 and never\r\n");
        await session.WriteAsync("crossed either margin. Press Enter for DECIC - insert 5 columns...");
        await session.WaitForEnterAsync();

        await session.WriteAsync("\x1b[5;30H\x1b[5'}");     // DECIC
        await session.WriteAsync("\x1b[10;1H\x1b[KPress Enter for DECDC - delete 3 columns at the same spot...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[5;30H\x1b[3'~");     // DECDC

        await session.WriteAsync("\x1b[10;1H\x1b[KPress Enter for SL and SR - shift the whole margin area left then right...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[4 @");               // SL
        await session.WriteAsync("\x1b[4 A");               // SR

        // The known disagreement, printed rather than described.
        await session.WriteAsync("\x1b[12;1H\x1b[1;37mOrigin mode against the left margin:\x1b[0m\r\n");
        await session.WriteAsync("\x1b[?6h");               // DECOM - origin mode on
        await session.WriteAsync("\x1b[H");                 // CUP with no parameters - "home"
        await AskAndShowAsync(session, "Where did CUP home to, with a left margin of 20 and origin mode on", "\x1b[6n",
            "The VT420 manual puts home at the LEFT MARGIN, so column 20. Column 1 is the known gap");
        await session.WriteAsync("\x1b[?6l");

        // Put the terminal back, or every later test inherits these margins.
        await session.WriteAsync("\x1b[?69l\x1b[r");
        await session.WriteAsync("\r\nMargins have been cleared again.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Downloading a character set and programming a function key.
    /// </summary>
    /// <remarks>
    /// The 94-character part is deliberate. DECDLD's Pcss parameter says whether the set starts at
    /// 0x20 (96 characters) or 0x21 (94), and that parameter is read and not acted on - so if the
    /// second block below prints its glyphs one position out, that is the defect, and it is one
    /// this test was built to show.
    /// </remarks>
    private async Task RunDEC_SoftFontAndUdkAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Soft Font (DECDLD) and User Keys (DECUDK)");

        await session.WriteAsync("Downloading a 96-character set with three glyphs: a full block, a\r\n");
        await session.WriteAsync("checkerboard and an arrow.\r\n\r\n");

        // DECDLD: DCS Pfn ; Pcn ; Pe ; Pcmw ; Pw ; Pt ; Pcmh ; Pcss { Dscs <glyphs> ST
        // Pcn=1 starts the set at the second position; Pcss=0 is the 96-character form.
        // Each glyph is sixel columns separated by '/' for the second half of the cell.
        await session.WriteAsync("\x1bP1;1;2;8;0;2;12;0{ @");
        await session.WriteAsync("~~~~~~~~/~~~~~~~~;");        // a solid block
        await session.WriteAsync("PPPPPPPP/@@@@@@@@;");        // a coarse checkerboard
        await session.WriteAsync("BFNvNFB@/@@@@@@@@");         // an arrow shape
        await session.WriteAsync("\x1b\\");

        await session.WriteAsync("Designating it as G0 and printing the three glyphs:\r\n");
        await session.WriteAsync("\x1b( @");
        await session.WriteAsync("  ABC  \r\n");
        await session.WriteAsync("\x1b(B");                    // back to ASCII, or everything after is soft
        await session.WriteAsync("\x1b[1;37mExpected:\x1b[0m a block, a checkerboard and an arrow where A, B and C were.\r\n\r\n");

        await session.WriteAsync("Now the SAME glyphs as a 94-character set (Pcss=1), which starts one\r\n");
        await session.WriteAsync("position later:\r\n");
        await session.WriteAsync("\x1bP1;1;2;8;0;2;12;1{ A");
        await session.WriteAsync("~~~~~~~~/~~~~~~~~;");
        await session.WriteAsync("PPPPPPPP/@@@@@@@@;");
        await session.WriteAsync("BFNvNFB@/@@@@@@@@");
        await session.WriteAsync("\x1b\\");
        await session.WriteAsync("\x1b( A");
        await session.WriteAsync("  ABC  \r\n");
        await session.WriteAsync("\x1b(B");
        await session.WriteAsync("\x1b[1;37mExpected:\x1b[0m the same three glyphs in the same places.\r\n");
        await session.WriteAsync("\x1b[33mIf they are one position out, that is the known DECDLD size gap.\x1b[0m\r\n\r\n");

        // DECUDK: DCS Pc ; Pl | Ky1 / St1 ; Ky2 / St2 ST, where the string is hex.
        // Key 11 is F1 on a VT220 keyboard. 48454C4C4F is "HELLO".
        await session.WriteAsync("Programming F1 to send HELLO, and F2 to send WORLD:\r\n");
        await session.WriteAsync("\x1bP1;1|11/48454C4C4F;12/574F524C44\x1b\\");
        await session.WriteAsync("Press F1 and F2 now. Press Enter when done.\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Enter)
            {
                break;
            }
            await session.WriteAsync($"  received: {ToVisible(input.Value)}   hex: {ToHex(input.Value)}\r\n");
        }

        await session.WriteAsync("\x1b[1;37mExpected:\x1b[0m F1 sends HELLO and F2 sends WORLD, as plain text.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// The VT320 and VT340 status line, which is a 25th row the host can write to.
    /// </summary>
    /// <remarks>
    /// NOT IMPLEMENTED as of 2026-08-17 - verified by searching the whole Terminal folder for
    /// DECSASD, DECSSDT and any status-line handling, which turned up nothing. This test is kept
    /// because it is the thing that will prove the feature when it arrives, and because a menu that
    /// silently omits a missing feature is how the gap stays out of sight.
    /// </remarks>
    private async Task RunDEC_StatusLineAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Status Line");

        await session.WriteAsync("\x1b[33mKnown state: the status line is NOT implemented in this emulator yet\r\n");
        await session.WriteAsync("(checked 2026-08-17). Expect nothing to happen. The sequences are sent\r\n");
        await session.WriteAsync("anyway so this test proves the feature the day it lands.\x1b[0m\r\n\r\n");
        await session.WriteAsync("DECSSDT chooses what the status line shows:\r\n");
        await session.WriteAsync("  0 = none, 1 = the terminal's own indicator, 2 = host writable.\r\n\r\n");

        await session.WriteAsync("Press Enter to turn on the terminal's indicator line (CSI 1 $ ~)...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[1$~");

        await session.WriteAsync("\r\nPress Enter to switch it to host writable (CSI 2 $ ~) and write to it...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[2$~");
        await session.WriteAsync("\x1b[1$}");                  // DECSASD - send following output to the status line
        await session.WriteAsync("\x1b[2K STATUS LINE: written by the host ");
        await session.WriteAsync("\x1b[0$}");                  // back to the main display

        await session.WriteAsync("\r\n\x1b[1;37mExpected:\x1b[0m the bottom line carries the status text, the main screen\r\n");
        await session.WriteAsync("is untouched, and the main screen still scrolls in 24 rows.\r\n");
        await session.WriteAsync("\r\nPress Enter to turn the status line off again...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[0$~");

        await EndTestAsync(session);
    }

    /// <summary>
    /// The national replacement character sets, which change a handful of ASCII positions.
    /// </summary>
    /// <remarks>
    /// The Norwegian/Danish set is the one that matters on the machines this program is used with,
    /// so it is first and its expected glyphs are spelled out.
    /// </remarks>
    private async Task RunDEC_NationalSetsAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "National Character Sets (NRCS)");

        await session.WriteAsync("Each row designates one national set as G0 and prints the positions\r\n");
        await session.WriteAsync("that the set replaces. Only those positions should change.\r\n\r\n");
        await session.WriteAsync("Positions printed:  # @ [ \\ ] ^ ` { | } ~\r\n\r\n");

        // Name, and the designator's final character(s) after ESC ( .
        string[] names = { "ASCII (the baseline)", "United Kingdom", "Norwegian/Danish", "Swedish", "German", "French", "Finnish", "Italian", "Spanish", "Swiss" };
        string[] designators = { "B", "A", "E", "H", "K", "R", "C", "Y", "Z", "=" };

        for (int i = 0; i < names.Length; i++)
        {
            await session.WriteAsync($"\x1b[1;37m{names[i],-22}\x1b[0m ");
            await session.WriteAsync("\x1b(" + designators[i]);
            await session.WriteAsync("# @ [ \\ ] ^ ` { | } ~");
            await session.WriteAsync("\x1b(B\r\n");
        }

        await session.WriteAsync("\r\n\x1b[1;37mExpected for Norwegian/Danish:\x1b[0m [ \\ ] become AE, O-slash, AA\r\n");
        await session.WriteAsync("and { | } become the same three in lower case.\r\n");
        await session.WriteAsync("\x1b[1;37mExpected for German:\x1b[0m @ becomes the section sign and ~ becomes sharp s.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// DECSCL, which tells the terminal to behave as an older one - and the DA reply that proves it
    /// took effect.
    /// </summary>
    private async Task RunDEC_ConformanceLevelAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Conformance Levels (DECSCL)");

        int[] levels = { 61, 62, 63, 64, 65 };
        string[] meaning = { "VT100", "VT200", "VT300", "VT400", "VT500" };

        for (int i = 0; i < levels.Length; i++)
        {
            await session.WriteAsync($"\x1b[1;37mSetting level {levels[i]} ({meaning[i]})\x1b[0m\r\n");
            await session.WriteAsync($"\x1b[{levels[i]}\"p");
            await AskAndShowAsync(session, "  DA1 after the change", "\x1b[c",
                $"a reply whose first number matches {levels[i]}, or the highest level this terminal has");
        }

        // Leave the terminal at its own highest level rather than at VT500, which it may not be.
        await session.WriteAsync("\x1b[64\"p");
        await session.WriteAsync("Conformance level put back to 64 (VT400).\r\n");
        await session.WriteAsync("\r\n\x1b[1;37mWhat to judge:\x1b[0m setting a LOWER level must actually take features away.\r\n");
        await session.WriteAsync("A terminal that answers VT400 after being told 61 is not switching.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// VT52 mode - entered from ANSI mode, driven with VT52's own short sequences, and left again.
    /// </summary>
    private async Task RunDEC_Vt52ModeAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "VT52 Mode");

        await session.WriteAsync("Entering VT52 mode with CSI ? 2 l. From here the sequences are\r\n");
        await session.WriteAsync("VT52's own - two bytes, no CSI, no parameters.\r\n\r\n");
        await session.WriteAsync("Press Enter to go...");
        await session.WaitForEnterAsync();

        await session.WriteAsync("\x1b[?2l");                  // DECANM reset - into VT52

        await session.WriteAsync("\x1bH");                     // cursor home
        await session.WriteAsync("\x1bJ");                     // erase to end of screen
        await session.WriteAsync("VT52 MODE");
        await session.WriteAsync("\x1bY(0");                   // direct address: row 9, column 17
        await session.WriteAsync("Placed with ESC Y - row 9, column 17");
        await session.WriteAsync("\x1bY&$");                   // row 7, column 5
        await session.WriteAsync("Row 7, column 5");
        await session.WriteAsync("\x1bY*$");                   // row 11, column 5
        await session.WriteAsync("\x1bFaaaaa\x1bG");           // graphics mode on, five characters, off
        await session.WriteAsync("  <- five graphic characters between ESC F and ESC G");

        var identity = await AskVt52IdentityAsync(session);
        await session.WriteAsync("\x1bY,$");                   // row 13, column 5
        await session.WriteAsync($"ESC Z identity: {ToVisible(identity)}");

        await session.WriteAsync("\x1bY.$");
        await session.WriteAsync("Press Enter to return to ANSI mode with ESC <");

        // ReadInputAsync is still parsing, and Enter is Enter in either mode.
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b<");                     // back to ANSI

        await session.WriteAsync("\r\n\x1b[1;37mExpected:\x1b[0m every line landed where ESC Y said, the graphic\r\n");
        await session.WriteAsync("characters were not letters, the identity was ESC / Z, and this text\r\n");
        await session.WriteAsync("is in colour again - which only works if ANSI mode came back.\r\n");

        await EndTestAsync(session);
    }

    /// <summary>
    /// Asks a VT52 who it is and returns whatever came back.
    /// </summary>
    private static async Task<string> AskVt52IdentityAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1bZ");
        var reply = await CaptureResponseWithPollingAsync(session, 1000);
        return reply ?? "(nothing came back)";
    }

    /// <summary>
    /// DECSCA, DECSEL and DECSED - the erase that spares what the host marked as protected.
    /// </summary>
    private async Task RunDEC_SelectiveEraseAsync(TelnetSession session)
    {
        await BeginTestAsync(session, "Selective Erase");

        await session.WriteAsync("Text marked with DECSCA (CSI 1 \" q) survives a selective erase.\r\n");
        await session.WriteAsync("Text marked unprotected (CSI 0 \" q) does not.\r\n\r\n");

        for (int row = 5; row <= 12; row++)
        {
            await session.WriteAsync($"\x1b[{row};1H");
            await session.WriteAsync("\x1b[1\"q");             // protected
            await session.WriteAsync($"row {row}: KEEP-THIS  ");
            await session.WriteAsync("\x1b[0\"q");             // unprotected
            await session.WriteAsync("erase-this erase-this erase-this");
        }

        await session.WriteAsync("\x1b[15;1HPress Enter for DECSEL on row 8 (CSI ? 2 K - the whole line)...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[8;1H\x1b[?2K");

        await session.WriteAsync("\x1b[15;1H\x1b[KPress Enter for DECSED over the whole screen (CSI ? 2 J)...");
        await session.WaitForEnterAsync();
        await session.WriteAsync("\x1b[?2J");

        await session.WriteAsync("\x1b[15;1H\x1b[1;37mExpected:\x1b[0m every KEEP-THIS is still on screen and every\r\n");
        await session.WriteAsync("erase-this is gone. An ordinary CSI 2 J would have taken both.\r\n");

        // Unprotect again, or later tests inherit it.
        await session.WriteAsync("\x1b[0\"q");

        await EndTestAsync(session);
    }

    #endregion
}
