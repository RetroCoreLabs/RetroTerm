//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

using RetroTerm.Core.Terminal.Input.Keyboards;

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// The two IBM on-screen keyboards (Ronny, 22 November 2026): the original IBM 3278 Typewriter
    /// keyboard, and the 3270 PC style 122-key KB 3270 Plus "Native" layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row and legend is from docs\spec\IBM3270\KEYBOARD.md: section 2.3 for the 3278 (Figure
    /// 3-2 of GA27-2890-4, the 87-key Typewriter keyboard) and section 3 for the KB 3270 Plus (the
    /// seasip keytops drawing and Ronny's photo). Where that document says NOT VERIFIED, the comment
    /// here says so too.
    /// </para>
    /// <para>
    /// Widths are in key units. The 3278 figure gives no dimensions, so widths are chosen to make
    /// each row of the main block the same length (15 units), the way the drawing shows it.
    /// </para>
    /// </remarks>
    public static class Ibm3270Keyboards
    {
        /// <summary>
        /// The IBM 3278 87-key Typewriter keyboard.
        /// </summary>
        public static KeyboardDescription Ibm3278 { get; } = Build3278();

        /// <summary>
        /// The KB 3270 Plus 122-key keyboard, Native layout.
        /// </summary>
        public static KeyboardDescription Kb3270Plus { get; } = BuildKb3270Plus();

        /// <summary>
        /// Both, the 3278 first.
        /// </summary>
        public static IReadOnlyList<KeyboardDescription> All { get; } = new[] { Ibm3278, Kb3270Plus };

        private static KeyCap Attn(double x, double y, double w, string legend, string press)
        {
            return new KeyCap(x, y, w, legend) { Role = KeyRole.Attention, Press = press };
        }

        private static KeyCap Edit(double x, double y, double w, string legend, string press)
        {
            return new KeyCap(x, y, w, legend) { Role = KeyRole.Edit, Press = press };
        }

        private static KeyCap Blank(double x, double y, double w, string legend)
        {
            return new KeyCap(x, y, w, legend) { Role = KeyRole.Blank };
        }

        private static KeyCap Mod(double x, double y, double w, string legend, KeyLatch latch)
        {
            return new KeyCap(x, y, w, legend) { Role = KeyRole.Modifier, Latch = latch };
        }

        // A letter: the cap shows the capital, unshifted types lower case, Shift types the capital.
        private static KeyCap Letter(double x, double y, char letter)
        {
            return new KeyCap(x, y, 1, letter.ToString())
            {
                Types = char.ToLowerInvariant(letter).ToString(),
                ShiftTypes = letter.ToString(),
            };
        }

        // A two-character key: the shifted character on top, the plain one below.
        private static KeyCap Pair(double x, double y, string plain, string shifted)
        {
            // A key whose shifted character is the same (the number pad, the 3278 comma and period)
            // carries one legend, not the same glyph twice.
            return new KeyCap(x, y, 1, plain)
            {
                TopLegend = shifted == plain ? null : shifted,
                Types = plain,
                ShiftTypes = shifted,
            };
        }

        private static void Letters(List<KeyCap> keys, double x, double y, string letters)
        {
            for (int i = 0; i < letters.Length; i++)
            {
                keys.Add(Letter(x + i, y, letters[i]));
            }
        }

        private static KeyboardDescription Build3278()
        {
            var k = new List<KeyCap>(90);

            // Left block, 2 wide, rows 0-3 (KEYBOARD.md 2.3). Front legend = the ALT function.
            k.Add(new KeyCap(0, 0, 1, "ATTN") { Role = KeyRole.Attention, Press = "ATTN", FrontLegend = "SYS REQ", AltPress = "SYSREQ" });
            k.Add(new KeyCap(1, 0, 1, "CURSR SEL") { Role = KeyRole.Edit, Press = "CURSORSELECT", FrontLegend = "CLEAR", AltPress = "CLEAR" });
            k.Add(Blank(0, 1, 1, string.Empty));
            k.Add(new KeyCap(1, 1, 1, string.Empty) { Role = KeyRole.Blank, FrontLegend = "ERASE INPUT", AltPress = "ERASEINPUT" });

            // CURSR BLINK / ALT CURSR, and the Print / IDENT and Click / TEST pair: real keys with no
            // 3270 data-stream meaning in RetroTerm (cursor style, printer, keyboard click).
            k.Add(new KeyCap(0, 2, 1, "CURSR BLINK") { Role = KeyRole.Blank, FrontLegend = "ALT CURSR" });
            k.Add(Edit(1, 2, 1, "ERASE EOF", "ERASEEOF"));
            k.Add(new KeyCap(0, 3, 1, "PRINT") { Role = KeyRole.Blank, FrontLegend = "IDENT" });
            k.Add(new KeyCap(1, 3, 1, "CLICK") { Role = KeyRole.Blank, FrontLegend = "TEST" });

            const double M = 2.5; // main block left edge

            // Row 0: the digit row. ALT + 1 ... = gives PF1-PF12 (GA27-2890-4 page 3-9).
            k.Add(Pair(M, 0, "`", "~"));
            string digits = "1234567890-=";
            string shifted = "|@#$%¬&*()_+";
            for (int i = 0; i < 12; i++)
            {
                KeyCap key = Pair(M + 1 + i, 0, digits[i].ToString(), shifted[i].ToString());
                key.FrontLegend = "PF" + (i + 1);
                key.AltPress = "PF" + (i + 1);
                k.Add(key);
            }

            k.Add(Edit(M + 13, 0, 2, "←", "BACKSPACE"));

            // Row 1. The right-hand key: KEYBOARD.md calls it New Line with the Home square front
            // legend; NOT VERIFIED there which key carries Home, so this follows the document.
            k.Add(new KeyCap(M, 1, 1.5, "TAB") { Role = KeyRole.Edit, Press = "TAB", ShiftPress = "BACKTAB", TopLegend = "⇤" });
            Letters(k, M + 1.5, 1, "QWERTYUIOP");
            k.Add(Pair(M + 11.5, 1, "¢", "!"));
            k.Add(Pair(M + 12.5, 1, "\\", "¦"));
            k.Add(new KeyCap(M + 13.5, 1, 1.5, "⤶") { Role = KeyRole.Edit, Press = "NEWLINE", FrontLegend = "HOME", AltPress = "HOME" });

            // Row 2. Lock is shift until pressed again.
            k.Add(Mod(M, 2, 1.75, "LOCK", KeyLatch.Lock));
            Letters(k, M + 1.75, 2, "ASDFGHJKL");
            k.Add(Pair(M + 10.75, 2, ";", ":"));
            k.Add(Pair(M + 11.75, 2, "'", "\""));
            k.Add(Pair(M + 12.75, 2, "{", "}"));
            k.Add(new KeyCap(M + 13.75, 2, 1.25, "NEW LINE") { Role = KeyRole.Edit, Press = "NEWLINE" });

            // Row 3.
            k.Add(Mod(M, 3, 1.25, "SHIFT", KeyLatch.Shift));
            k.Add(Pair(M + 1.25, 3, "<", ">"));
            Letters(k, M + 2.25, 3, "ZXCVBNM");
            k.Add(Pair(M + 9.25, 3, ",", ","));
            k.Add(Pair(M + 10.25, 3, ".", "."));
            k.Add(Pair(M + 11.25, 3, "/", "?"));
            k.Add(Mod(M + 12.25, 3, 2.75, "SHIFT", KeyLatch.Shift));

            // Row 4: RESET / DEV CNCL, space bar, ALT, ENTER.
            k.Add(new KeyCap(M, 4, 1.5, "RESET") { Role = KeyRole.Edit, Press = "RESET", FrontLegend = "DEV CNCL" });
            k.Add(new KeyCap(M + 1.5, 4, 9, string.Empty) { Types = " " });
            k.Add(Mod(M + 10.5, 4, 1.5, "ALT", KeyLatch.Alt));
            k.Add(Attn(M + 12, 4, 3, "ENTER", "ENTER"));

            // Middle block, 2 wide.
            const double C = M + 15.5;
            k.Add(new KeyCap(C, 0, 1, "DUP") { Role = KeyRole.Edit, Press = "DUP", FrontLegend = "PA1", AltPress = "PA1" });
            k.Add(new KeyCap(C + 1, 0, 1, "FIELD MARK") { Role = KeyRole.Edit, Press = "FIELDMARK", FrontLegend = "PA2", AltPress = "PA2" });
            k.Add(Edit(C, 1, 1, "INS", "INSERT"));
            k.Add(Edit(C + 1, 1, 1, "DEL", "DELETE"));
            k.Add(Edit(C, 2, 1, "↑", "UP"));
            k.Add(Edit(C + 1, 2, 1, "↓", "DOWN"));

            // Double-speed left/right are the ALT functions here; RetroTerm has no double-speed move.
            k.Add(new KeyCap(C, 3, 1, "←") { Role = KeyRole.Edit, Press = "LEFT", FrontLegend = "⇇" });
            k.Add(new KeyCap(C + 1, 3, 1, "→") { Role = KeyRole.Edit, Press = "RIGHT", FrontLegend = "⇉" });

            // Right block, PF13-PF24, 3 wide (the 87-key keyboard only).
            const double P = C + 2.5;
            for (int i = 0; i < 12; i++)
            {
                k.Add(Attn(P + (i % 3), i / 3, 1, "PF" + (13 + i), "PF" + (13 + i)));
            }

            return new KeyboardDescription("IBM3278", "IBM 3278 (87 keys)", "IBM", k);
        }

        private static KeyboardDescription BuildKb3270Plus()
        {
            var k = new List<KeyCap>(125);
            const double M = 2.5;     // main block left edge
            const double R = 2.0;     // first row below the two PF rows

            // PF13-PF24 above PF1-PF12, over the main block (KEYBOARD.md 3.1).
            for (int i = 0; i < 12; i++)
            {
                double x = M + i * 1.25;
                k.Add(Attn(x, 0, 1, "PF" + (13 + i), "PF" + (13 + i)));
                k.Add(Attn(x, 1, 1, "PF" + (1 + i), "PF" + (1 + i)));
            }

            // Left block, 2 wide, 5 rows (3.2). The blue second legends are shown as front legends
            // and are NOT wired: KEYBOARD.md 3.2 says it is NOT VERIFIED that a blue legend is reached
            // with Alt, and the photos do not show them at all.
            k.Add(new KeyCap(0, R, 1, "Help") { Role = KeyRole.Blank, FrontLegend = "SysRq" });
            k.Add(new KeyCap(1, R, 1, "Clear") { Role = KeyRole.Attention, Press = "CLEAR", FrontLegend = "Test" });
            k.Add(new KeyCap(0, R + 1, 1, "WS Ctrl") { Role = KeyRole.Blank, FrontLegend = "ExSel" });
            k.Add(new KeyCap(1, R + 1, 1, "Finish") { Role = KeyRole.Blank, FrontLegend = "Attn" });
            k.Add(new KeyCap(0, R + 2, 1, "ChgSc") { Role = KeyRole.Blank, TopLegend = "Jump", FrontLegend = "CrSel" });
            k.Add(new KeyCap(1, R + 2, 1, "Erase EOF") { Role = KeyRole.Edit, Press = "ERASEEOF", FrontLegend = "ErInp" });
            k.Add(new KeyCap(0, R + 3, 1, "Print") { Role = KeyRole.Blank, TopLegend = "*", FrontLegend = "Ident" });
            k.Add(new KeyCap(1, R + 3, 1, "Copy") { Role = KeyRole.Blank, TopLegend = "Auto", FrontLegend = "Pause" });
            k.Add(Blank(0, R + 4, 1, "□→□"));
            k.Add(Blank(1, R + 4, 1, "Ctrl"));

            // Main block (3.3).
            k.Add(Pair(M, R, "`", "~"));
            string digits = "1234567890-=";
            string shifted = "!@#$%¬&*()_+";
            for (int i = 0; i < 12; i++)
            {
                k.Add(Pair(M + 1 + i, R, digits[i].ToString(), shifted[i].ToString()));
            }

            k.Add(Edit(M + 13, R, 2, "←", "BACKSPACE"));

            // Tab is 1.75 wide so this row ends where the home row ends, and the big ↵ key can span
            // both rows beside them (the overlap test caught a 1.5-wide Tab putting ↵ over the { key).
            k.Add(new KeyCap(M, R + 1, 1.75, "⇥") { Role = KeyRole.Edit, Press = "TAB", ShiftPress = "BACKTAB", TopLegend = "⇤" });
            Letters(k, M + 1.75, R + 1, "QWERTYUIOP");
            k.Add(Pair(M + 11.75, R + 1, "¢", "!"));
            k.Add(Pair(M + 12.75, R + 1, "\\", "¦"));

            // The big ↵ key spans rows 2-3: the 3278's New Line position. NOT VERIFIED (3.3) that the
            // KB 3270 Plus means it as New Line; the .ods only names its PC meaning, Return.
            k.Add(new KeyCap(M + 13.75, R + 1, 1.25, "↵") { Role = KeyRole.Edit, Press = "NEWLINE", Height = 2 });

            k.Add(Mod(M, R + 2, 1.75, "Caps", KeyLatch.Lock));
            Letters(k, M + 1.75, R + 2, "ASDFGHJKL");
            k.Add(Pair(M + 10.75, R + 2, ";", ":"));
            k.Add(Pair(M + 11.75, R + 2, "'", "\""));
            k.Add(Pair(M + 12.75, R + 2, "{", "}"));

            k.Add(Mod(M, R + 3, 1.25, "Shift", KeyLatch.Shift));
            k.Add(Pair(M + 1.25, R + 3, "<", ">"));
            Letters(k, M + 2.25, R + 3, "ZXCVBNM");
            k.Add(Pair(M + 9.25, R + 3, ",", "<"));
            k.Add(Pair(M + 10.25, R + 3, ".", ">"));
            k.Add(Pair(M + 11.25, R + 3, "/", "?"));
            k.Add(Mod(M + 12.25, R + 3, 2.75, "Shift", KeyLatch.Shift));

            k.Add(new KeyCap(M, R + 4, 1.5, "Reset") { Role = KeyRole.Edit, Press = "RESET", FrontLegend = "Quit" });
            k.Add(Mod(M + 1.5, R + 4, 1.25, "Alt", KeyLatch.Alt));
            k.Add(new KeyCap(M + 2.75, R + 4, 7.5, string.Empty) { Types = " " });
            k.Add(Mod(M + 10.25, R + 4, 1.25, "Alt", KeyLatch.Alt));
            k.Add(Attn(M + 11.5, R + 4, 3.5, "Enter", "ENTER"));

            // Middle block (3.4).
            const double C = M + 15.5;
            k.Add(new KeyCap(C, R, 1, "Dup") { Role = KeyRole.Edit, Press = "DUP", FrontLegend = "PA1", AltPress = "PA1" });
            k.Add(new KeyCap(C + 1, R, 1, "Field Mark") { Role = KeyRole.Edit, Press = "FIELDMARK", FrontLegend = "PA2", AltPress = "PA2" });
            k.Add(Attn(C + 2, R, 1, "PA3", "PA3"));
            k.Add(Edit(C, R + 1, 1, "|←", "BACKTAB"));
            k.Add(Edit(C + 1, R + 1, 1, "Ins", "INSERT"));
            k.Add(Edit(C + 2, R + 1, 1, "Del", "DELETE"));
            k.Add(Edit(C + 1, R + 2, 1, "↑", "UP"));
            k.Add(Edit(C, R + 3, 1, "←", "LEFT"));

            // The centre key: the .ods calls it Rule / Home; blank in photo 1 (3.4). Home is what the
            // drawing names, so it presses HOME.
            k.Add(Edit(C + 1, R + 3, 1, "↖", "HOME"));
            k.Add(Edit(C + 2, R + 3, 1, "→", "RIGHT"));
            k.Add(Edit(C + 1, R + 4, 1, "↓", "DOWN"));

            // Number pad (3.5). Digits type; the pad's Num Lock cursor functions are not modelled.
            const double N = C + 3.5;
            k.Add(Blank(N, R, 1, "Esc"));
            k.Add(Blank(N + 1, R, 1, "Num Lock"));
            k.Add(Blank(N + 2, R, 1, "ScrLk"));
            k.Add(Edit(N + 3, R, 1, "Sys Req", "SYSREQ"));
            k.Add(Pair(N, R + 1, "7", "7"));
            k.Add(Pair(N + 1, R + 1, "8", "8"));
            k.Add(Pair(N + 2, R + 1, "9", "9"));
            k.Add(Edit(N + 3, R + 1, 1, "⇥", "TAB"));
            k.Add(Pair(N, R + 2, "4", "4"));
            k.Add(Pair(N + 1, R + 2, "5", "5"));
            k.Add(Pair(N + 2, R + 2, "6", "6"));
            k.Add(Pair(N + 3, R + 2, "-", "-"));
            k.Add(Pair(N, R + 3, "1", "1"));
            k.Add(Pair(N + 1, R + 3, "2", "2"));
            k.Add(Pair(N + 2, R + 3, "3", "3"));
            k.Add(new KeyCap(N + 3, R + 3, 1, "Enter") { Role = KeyRole.Attention, Press = "ENTER", Height = 2 });
            k.Add(new KeyCap(N, R + 4, 2, "0") { Types = "0" });
            k.Add(Pair(N + 2, R + 4, ".", "."));

            return new KeyboardDescription("KB3270PLUS", "3270 PC (KB 3270 Plus, 122 keys)", "IBM", k);
        }
    }
}
