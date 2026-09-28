using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV
{
    /// <summary>
    /// Tests for TDV2200KeyRegistry — the single source of truth for all TDV2200 key data.
    /// Verifies sequences match the ND246KeyboardMapper output exactly,
    /// color assignments match the UI panel logic, and all lookups work correctly.
    /// </summary>
    public class TDV2200KeyRegistryTests
    {
        // ─── Extended Control Mode: G-row ────────────────────────

        [Fact]
        public void ExtendedMode_GRowKeys_MatchND246Mapper()
        {
            Assert.Equal("\x1B[00_", TDV2200KeyRegistry.GetSequence("G9", true, false));
            Assert.Equal("\x1B[01_", TDV2200KeyRegistry.GetSequence("G9", true, false, shift: true));
            Assert.Equal("\x1B[02_", TDV2200KeyRegistry.GetSequence("G10", true, false));
            Assert.Equal("\x1B[03_", TDV2200KeyRegistry.GetSequence("G10", true, false, shift: true));
            Assert.Equal("\x1B[04_", TDV2200KeyRegistry.GetSequence("G11", true, false));
            Assert.Equal("\x1B[05_", TDV2200KeyRegistry.GetSequence("G11", true, false, shift: true));
            Assert.Equal("\x1B[06_", TDV2200KeyRegistry.GetSequence("G12", true, false));
            Assert.Equal("\x1B[07_", TDV2200KeyRegistry.GetSequence("G12", true, false, shift: true));
            Assert.Equal("\x1B[08_", TDV2200KeyRegistry.GetSequence("G13", true, false));
            Assert.Equal("\x1B[09_", TDV2200KeyRegistry.GetSequence("G13", true, false, shift: true));
            Assert.Equal("\x1B[10_", TDV2200KeyRegistry.GetSequence("G47", true, false));
            Assert.Equal("\x1B[11_", TDV2200KeyRegistry.GetSequence("G47", true, false, shift: true));
            Assert.Equal("\x1B[12_", TDV2200KeyRegistry.GetSequence("G48", true, false));
            Assert.Equal("\x1B[13_", TDV2200KeyRegistry.GetSequence("G48", true, false, shift: true));
            Assert.Equal("\x1B[14_", TDV2200KeyRegistry.GetSequence("G49", true, false));
            Assert.Equal("\x1B[15_", TDV2200KeyRegistry.GetSequence("G49", true, false, shift: true));
            Assert.Equal("\x1B[42_", TDV2200KeyRegistry.GetSequence("G51", true, false));
            Assert.Equal("\x1B[43_", TDV2200KeyRegistry.GetSequence("G51", true, false, shift: true));
            Assert.Equal("\x1B[44_", TDV2200KeyRegistry.GetSequence("G52", true, false));
            Assert.Equal("\x1B[45_", TDV2200KeyRegistry.GetSequence("G52", true, false, shift: true));
            Assert.Equal("\x1B[46_", TDV2200KeyRegistry.GetSequence("G53", true, false));
            Assert.Equal("\x1B[47_", TDV2200KeyRegistry.GetSequence("G53", true, false, shift: true));
            Assert.Equal("\x1B[48_", TDV2200KeyRegistry.GetSequence("G54", true, false));
            Assert.Equal("\x1B[49_", TDV2200KeyRegistry.GetSequence("G54", true, false, shift: true));
        }

        // ─── Extended Control Mode: F-row ────────────────────────

        [Fact]
        public void ExtendedMode_FRowKeys_MatchND246Mapper()
        {
            Assert.Equal("\x1B[16_", TDV2200KeyRegistry.GetSequence("F47", true, false));
            Assert.Equal("\x1B[17_", TDV2200KeyRegistry.GetSequence("F47", true, false, shift: true));
            Assert.Equal("\x1B[18_", TDV2200KeyRegistry.GetSequence("F48", true, false));
            Assert.Equal("\x1B[19_", TDV2200KeyRegistry.GetSequence("F48", true, false, shift: true));
            Assert.Equal("\x1B[20_", TDV2200KeyRegistry.GetSequence("F49", true, false));
            Assert.Equal("\x1B[21_", TDV2200KeyRegistry.GetSequence("F49", true, false, shift: true));
            Assert.Equal("\x1B[50_", TDV2200KeyRegistry.GetSequence("F51", true, false));
            Assert.Equal("\x1B[51_", TDV2200KeyRegistry.GetSequence("F51", true, false, shift: true));
            Assert.Equal("\x1B[52_", TDV2200KeyRegistry.GetSequence("F52", true, false));
            Assert.Equal("\x1B[53_", TDV2200KeyRegistry.GetSequence("F52", true, false, shift: true));
            Assert.Equal("\x1B[54_", TDV2200KeyRegistry.GetSequence("F52", true, false, ctrl: true));
            Assert.Equal("\x1B[55_", TDV2200KeyRegistry.GetSequence("F53", true, false));
            Assert.Equal("\x1B[56_", TDV2200KeyRegistry.GetSequence("F53", true, false, shift: true));
            Assert.Equal("\x1B[57_", TDV2200KeyRegistry.GetSequence("F53", true, false, ctrl: true));
            Assert.Equal("\x1B[58_", TDV2200KeyRegistry.GetSequence("F54", true, false));
            Assert.Equal("\x1B[59_", TDV2200KeyRegistry.GetSequence("F54", true, false, shift: true));
        }

        // ─── Extended Control Mode: E-row ────────────────────────

        [Fact]
        public void ExtendedMode_ERowKeys_MatchND246Mapper()
        {
            Assert.Equal("\x1B[86_", TDV2200KeyRegistry.GetSequence("E13", true, false));
            Assert.Equal("\x1B[87_", TDV2200KeyRegistry.GetSequence("E13", true, false, shift: true));
            Assert.Equal("\x1B[22_", TDV2200KeyRegistry.GetSequence("E47", true, false));
            Assert.Equal("\x1B[23_", TDV2200KeyRegistry.GetSequence("E47", true, false, shift: true));
            Assert.Equal("\x1B[24_", TDV2200KeyRegistry.GetSequence("E48", true, false));
            Assert.Equal("\x1B[25_", TDV2200KeyRegistry.GetSequence("E48", true, false, shift: true));
            Assert.Equal("\x1B[26_", TDV2200KeyRegistry.GetSequence("E49", true, false));
            Assert.Equal("\x1B[27_", TDV2200KeyRegistry.GetSequence("E49", true, false, shift: true));
            Assert.Equal("\x1B[60_", TDV2200KeyRegistry.GetSequence("E51", true, false));
            Assert.Equal("\x1B[61_", TDV2200KeyRegistry.GetSequence("E51", true, false, shift: true));
            Assert.Equal("\x1B[62_", TDV2200KeyRegistry.GetSequence("E52", true, false));
            Assert.Equal("\x1B[63_", TDV2200KeyRegistry.GetSequence("E52", true, false, shift: true));
            Assert.Equal("\x1B[64_", TDV2200KeyRegistry.GetSequence("E53", true, false));
            Assert.Equal("\x1B[65_", TDV2200KeyRegistry.GetSequence("E53", true, false, shift: true));
            Assert.Equal("\x1B[66_", TDV2200KeyRegistry.GetSequence("E54", true, false));
            Assert.Equal("\x1B[67_", TDV2200KeyRegistry.GetSequence("E54", true, false, shift: true));
        }

        // ─── Extended Control Mode: D-row ────────────────────────

        [Fact]
        public void ExtendedMode_DRowKeys_MatchND246Mapper()
        {
            Assert.Equal("\x1B[82_", TDV2200KeyRegistry.GetSequence("D99", true, false));
            Assert.Equal("\x1B[83_", TDV2200KeyRegistry.GetSequence("D99", true, false, shift: true));
            Assert.Equal("\x1B[28_", TDV2200KeyRegistry.GetSequence("D47", true, false));
            Assert.Equal("\x1B[29_", TDV2200KeyRegistry.GetSequence("D47", true, false, shift: true));
            Assert.Equal("\x1B[30_", TDV2200KeyRegistry.GetSequence("D48", true, false));
            Assert.Equal("\x1B[31_", TDV2200KeyRegistry.GetSequence("D48", true, false, shift: true));
            Assert.Equal("\x1B[32_", TDV2200KeyRegistry.GetSequence("D49", true, false));
            Assert.Equal("\x1B[33_", TDV2200KeyRegistry.GetSequence("D49", true, false, shift: true));
        }

        // ─── Extended Control Mode: C-row ────────────────────────

        [Fact]
        public void ExtendedMode_CRowKeys_MatchND246Mapper()
        {
            Assert.Equal("\x1B[84_", TDV2200KeyRegistry.GetSequence("C99", true, false));
            Assert.Equal("\x1B[85_", TDV2200KeyRegistry.GetSequence("C99", true, false, shift: true));
            Assert.Equal("\x1B[34_", TDV2200KeyRegistry.GetSequence("C47", true, false));
            Assert.Equal("\x1B[35_", TDV2200KeyRegistry.GetSequence("C47", true, false, shift: true));
            // C48 UP arrow always sends 0x1C
            Assert.Equal("\x1C", TDV2200KeyRegistry.GetSequence("C48", true, false));
            Assert.Equal("\x1C", TDV2200KeyRegistry.GetSequence("C48", true, false, shift: true));
            Assert.Equal("\x1B[36_", TDV2200KeyRegistry.GetSequence("C49", true, false));
            Assert.Equal("\x1B[37_", TDV2200KeyRegistry.GetSequence("C49", true, false, shift: true));
        }

        // ─── Cursor keys: AlwaysSameCode ─────────────────────────

        [Fact]
        public void ExtendedMode_CursorKeys_SendSingleByteValues()
        {
            // B47 LEFT: BS (0x08)
            Assert.Equal("\x08", TDV2200KeyRegistry.GetSequence("B47", true, false));
            Assert.Equal("\x08", TDV2200KeyRegistry.GetSequence("B47", true, false, shift: true));
            // B48 HOME: GS (0x1D)
            Assert.Equal("\x1D", TDV2200KeyRegistry.GetSequence("B48", true, false));
            Assert.Equal("\x1D", TDV2200KeyRegistry.GetSequence("B48", true, false, shift: true));
            // B49 RIGHT: CAN (0x18)
            Assert.Equal("\x18", TDV2200KeyRegistry.GetSequence("B49", true, false));
            Assert.Equal("\x18", TDV2200KeyRegistry.GetSequence("B49", true, false, shift: true));
            // A48 DOWN: VT (0x0B)
            Assert.Equal("\x0B", TDV2200KeyRegistry.GetSequence("A48", true, false));
            Assert.Equal("\x0B", TDV2200KeyRegistry.GetSequence("A48", true, false, shift: true));
            // C48 UP: FS (0x1C)
            Assert.Equal("\x1C", TDV2200KeyRegistry.GetSequence("C48", true, false));
            Assert.Equal("\x1C", TDV2200KeyRegistry.GetSequence("C48", true, false, shift: true));
        }

        // ─── A-row ───────────────────────────────────────────────

        [Fact]
        public void ExtendedMode_ARowKeys_MatchND246Mapper()
        {
            Assert.Equal("\x1B[38_", TDV2200KeyRegistry.GetSequence("A47", true, false));
            Assert.Equal("\x1B[39_", TDV2200KeyRegistry.GetSequence("A47", true, false, shift: true));
            Assert.Equal("\x1B[40_", TDV2200KeyRegistry.GetSequence("A49", true, false));
            Assert.Equal("\x1B[41_", TDV2200KeyRegistry.GetSequence("A49", true, false, shift: true));
        }

        // ─── Always-special keys ─────────────────────────────────

        [Fact]
        public void AlwaysSpecialKeys_SendSameCodeRegardlessOfMode()
        {
            // ESC always 0x1B
            Assert.Equal("\x1B", TDV2200KeyRegistry.GetSequence("G0", true, false));

            // DEL always 0x7F
            Assert.Equal("\x7F", TDV2200KeyRegistry.GetSequence("E14", true, false));
            Assert.Equal("\x7F", TDV2200KeyRegistry.GetSequence("E14", false, false));

            // LF always 0x0A
            Assert.Equal("\x0A", TDV2200KeyRegistry.GetSequence("D13", true, false));
            Assert.Equal("\x0A", TDV2200KeyRegistry.GetSequence("D13", false, false));

            // CR always 0x0D
            Assert.Equal("\x0D", TDV2200KeyRegistry.GetSequence("C13", true, false));
            Assert.Equal("\x0D", TDV2200KeyRegistry.GetSequence("C13", false, false));

            // B54 KPENTER: CR in both modes (spec 6.8.3 fixed keys) EXCEPT numeric pad function
            // mode, where spec 6.8.4 gives CSI 81 _. Pinned as "always 0x0D" until 27 September 2026.
            Assert.Equal("\x0D", TDV2200KeyRegistry.GetSequence("B54", true, false));
            Assert.Equal("\x0D", TDV2200KeyRegistry.GetSequence("B54", false, false));
            Assert.Equal("\x1B[81_", TDV2200KeyRegistry.GetSequence("B54", true, true));
        }

        // ─── Simple ASCII mode ───────────────────────────────────

        [Fact]
        public void SimpleAsciiMode_FunctionKeys_SendCorrectControlCodes()
        {
            Assert.Equal("\x02", TDV2200KeyRegistry.GetSequence("G10", false, false)); // FELT: STX
            Assert.Equal("\x01", TDV2200KeyRegistry.GetSequence("G11", false, false)); // AVSN: SOH
            Assert.Equal("\x03", TDV2200KeyRegistry.GetSequence("G12", false, false)); // SETN: ETX
            Assert.Equal("\x09", TDV2200KeyRegistry.GetSequence("F47", false, false)); // TAB: HT
            Assert.Equal("\x11", TDV2200KeyRegistry.GetSequence("F48", false, false)); // DC1
            Assert.Equal("\x14", TDV2200KeyRegistry.GetSequence("F49", false, false)); // DC4
            Assert.Equal("\x1E", TDV2200KeyRegistry.GetSequence("F51", false, false)); // F1: RS
            Assert.Equal("\x1F", TDV2200KeyRegistry.GetSequence("F52", false, false)); // F2: US
            Assert.Equal("\x18", TDV2200KeyRegistry.GetSequence("F53", false, false)); // F3: CAN
            Assert.Equal("\x08", TDV2200KeyRegistry.GetSequence("E13", false, false)); // BS
        }

        [Fact]
        public void SimpleAsciiMode_NavigationKeys_SendCorrectControlCodes()
        {
            Assert.Equal("\x1C", TDV2200KeyRegistry.GetSequence("C48", false, false)); // UP: FS
            Assert.Equal("\x08", TDV2200KeyRegistry.GetSequence("B47", false, false)); // LEFT: BS
            Assert.Equal("\x1D", TDV2200KeyRegistry.GetSequence("B48", false, false)); // HOME: GS, same as extended
            Assert.Equal("\x18", TDV2200KeyRegistry.GetSequence("B49", false, false)); // RIGHT: CAN
            Assert.Equal("\x0B", TDV2200KeyRegistry.GetSequence("A48", false, false)); // DOWN: VT
        }

        [Fact]
        public void SimpleAsciiMode_MultiCharacterSequences()
        {
            Assert.Equal("000", TDV2200KeyRegistry.GetSequence("E51", false, false));
            Assert.Equal("00", TDV2200KeyRegistry.GetSequence("E52", false, false));
            Assert.Equal("0", TDV2200KeyRegistry.GetSequence("E53", false, false));
            Assert.Equal("+", TDV2200KeyRegistry.GetSequence("E54", false, false));
        }

        // ─── Numeric pad function mode ───────────────────────────

        [Fact]
        public void NumericPadFunctionMode_SendsCSISequences()
        {
            Assert.Equal("\x1B[75_", TDV2200KeyRegistry.GetSequence("D51", true, true));
            Assert.Equal("\x1B[76_", TDV2200KeyRegistry.GetSequence("D52", true, true));
            Assert.Equal("\x1B[77_", TDV2200KeyRegistry.GetSequence("D53", true, true));
            Assert.Equal("\x1B[80_", TDV2200KeyRegistry.GetSequence("D54", true, true));
            Assert.Equal("\x1B[72_", TDV2200KeyRegistry.GetSequence("C51", true, true));
            Assert.Equal("\x1B[73_", TDV2200KeyRegistry.GetSequence("C52", true, true));
            Assert.Equal("\x1B[74_", TDV2200KeyRegistry.GetSequence("C53", true, true));
            Assert.Equal("\x1B[79_", TDV2200KeyRegistry.GetSequence("C54", true, true));
            Assert.Equal("\x1B[69_", TDV2200KeyRegistry.GetSequence("B51", true, true));
            Assert.Equal("\x1B[70_", TDV2200KeyRegistry.GetSequence("B52", true, true));
            Assert.Equal("\x1B[71_", TDV2200KeyRegistry.GetSequence("B53", true, true));
            // B54 ENTER sends CSI 81 _ in pad function mode like every other pad key (spec 6.8.4);
            // this line pinned CR as a "special case" with no citation until 27 September 2026.
            Assert.Equal("\x1B[81_", TDV2200KeyRegistry.GetSequence("B54", true, true));
            Assert.Equal("\x1B[68_", TDV2200KeyRegistry.GetSequence("A51", true, true));
            Assert.Equal("\x1B[78_", TDV2200KeyRegistry.GetSequence("A53", true, true));
        }

        // ─── PUSH keys ──────────────────────────────────────────

        [Fact]
        public void PushKeys_ReturnNull()
        {
            for (int i = 1; i <= 8; i++)
            {
                Assert.Null(TDV2200KeyRegistry.GetSequence($"G{i}", true, false));
                Assert.Null(TDV2200KeyRegistry.GetSequence($"G{i}", false, false));
            }
        }

        // ─── Name → Grid lookups ─────────────────────────────────

        [Fact]
        public void GetGridForName_ResolvesEnglishNames()
        {
            Assert.Equal("G9", TDV2200KeyRegistry.GetGridForName("MARK")!);
            Assert.Equal("G10", TDV2200KeyRegistry.GetGridForName("FIELD")!);
            Assert.Equal("G11", TDV2200KeyRegistry.GetGridForName("PARA"));
            Assert.Equal("G12", TDV2200KeyRegistry.GetGridForName("SENT"));
            Assert.Equal("G13", TDV2200KeyRegistry.GetGridForName("WORD"));
            Assert.Equal("G51", TDV2200KeyRegistry.GetGridForName("FUNC"));
            Assert.Equal("G52", TDV2200KeyRegistry.GetGridForName("PRINT"));
            Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("HELP")!);
            Assert.Equal("G54", TDV2200KeyRegistry.GetGridForName("EXIT")!);
            Assert.Equal("G48", TDV2200KeyRegistry.GetGridForName("COPY"));
            Assert.Equal("G49", TDV2200KeyRegistry.GetGridForName("MOVE"));
        }

        [Fact]
        public void GetGridForName_ResolvesNorwegianNames()
        {
            Assert.Equal("G9", TDV2200KeyRegistry.GetGridForName("MERK"));
            Assert.Equal("G10", TDV2200KeyRegistry.GetGridForName("FELT"));
            Assert.Equal("G11", TDV2200KeyRegistry.GetGridForName("AVSN"));
            Assert.Equal("G12", TDV2200KeyRegistry.GetGridForName("SETN"));
            Assert.Equal("G13", TDV2200KeyRegistry.GetGridForName("ORD"));
            Assert.Equal("G51", TDV2200KeyRegistry.GetGridForName("FUNK"));
            Assert.Equal("G52", TDV2200KeyRegistry.GetGridForName("SKRIV"));
            Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("HJELP"));
            Assert.Equal("G54", TDV2200KeyRegistry.GetGridForName("SLUTT"));
        }

        [Fact]
        public void GetGridForName_IsCaseInsensitive()
        {
            Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("help"));
            Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("Help"));
            Assert.Equal("G53", TDV2200KeyRegistry.GetGridForName("HELP")!);
        }

        [Fact]
        public void GetGridForName_ResolvesNumericPadNames()
        {
            Assert.Equal("D51", TDV2200KeyRegistry.GetGridForName("KP_7"));
            Assert.Equal("D52", TDV2200KeyRegistry.GetGridForName("NUMPAD8"));
            Assert.Equal("C51", TDV2200KeyRegistry.GetGridForName("KP_4"));
            Assert.Equal("B51", TDV2200KeyRegistry.GetGridForName("KP_1"));
            Assert.Equal("A51", TDV2200KeyRegistry.GetGridForName("KP_0"));
            Assert.Equal("A53", TDV2200KeyRegistry.GetGridForName("KP_PERIOD"));
            Assert.Equal("B54", TDV2200KeyRegistry.GetGridForName("KP_ENTER"));
        }

        [Fact]
        public void GetGridForName_ResolvesPushKeys()
        {
            for (int i = 1; i <= 8; i++)
            {
                Assert.Equal($"G{i}", TDV2200KeyRegistry.GetGridForName($"PUSH{i}"));
                Assert.Equal($"G{i}", TDV2200KeyRegistry.GetGridForName($"P{i}"));
            }
        }

        [Fact]
        public void GetGridForName_ResolvesNavigationKeys()
        {
            Assert.Equal("C48", TDV2200KeyRegistry.GetGridForName("UP"));
            Assert.Equal("C48", TDV2200KeyRegistry.GetGridForName("ARROWUP"));
            Assert.Equal("A48", TDV2200KeyRegistry.GetGridForName("DOWN"));
            Assert.Equal("B47", TDV2200KeyRegistry.GetGridForName("LEFT"));
            Assert.Equal("B49", TDV2200KeyRegistry.GetGridForName("RIGHT"));
            Assert.Equal("B48", TDV2200KeyRegistry.GetGridForName("HOME"));
        }

        [Fact]
        public void GetGridForName_ReturnsNullForUnknown()
        {
            Assert.Null(TDV2200KeyRegistry.GetGridForName("NONEXISTENT"));
            Assert.Null(TDV2200KeyRegistry.GetGridForName(null));
        }

        // ─── VK → Grid lookups ──────────────────────────────────

        [Fact]
        public void GetGridForVK_ResolvesCommonKeys()
        {
            Assert.Equal("G0", TDV2200KeyRegistry.GetGridForVK(27));   // ESC
            Assert.Equal("C48", TDV2200KeyRegistry.GetGridForVK(38));  // Up
            Assert.Equal("A48", TDV2200KeyRegistry.GetGridForVK(40));  // Down
            Assert.Equal("B47", TDV2200KeyRegistry.GetGridForVK(37));  // Left
            Assert.Equal("B49", TDV2200KeyRegistry.GetGridForVK(39));  // Right
            Assert.Equal("B48", TDV2200KeyRegistry.GetGridForVK(36));  // Home
        }

        [Fact]
        public void GetGridForVK_ReturnsNullForUnmapped()
        {
            Assert.Null(TDV2200KeyRegistry.GetGridForVK(0));
            Assert.Null(TDV2200KeyRegistry.GetGridForVK(999));
        }

        // ─── Color assignments ───────────────────────────────────

        [Fact]
        public void Colors_BrownKeys_MatchUILogic()
        {
            // Navigation area: columns 47-49 (minus the ones changed to Orange)
            // G1-G8 PUSH keys are brown, G14 LOKAL is brown
            string[] brownGrids = { "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "G14",
                "D47", "D49", "C47", "C48", "C49",
                "B47", "B48", "B49", "A47", "A48", "A49" };

            for (int i = 0; i < brownGrids.Length; i++)
            {
                var key = TDV2200KeyRegistry.GetKey(brownGrids[i]);
                Assert.NotNull(key);
                Assert.Equal(TDVKeyColor.Brown, key.Color);
            }
        }

        [Fact]
        public void Colors_OrangeKeys_MatchUILogic()
        {
            string[] orangeGrids = { "G0", "G9", "G10", "G11", "G12", "G13",
                "G47", "G48", "G49",
                "G51", "G52", "G53", "G54",
                "F47", "F48", "F49",
                "F51", "F52", "F53", "F54",
                "E47", "E48", "E49",
                "E51", "E52", "E53", "E54",
                "E13", "E14", "D48", "D99", "D13", "C99", "C13" };

            for (int i = 0; i < orangeGrids.Length; i++)
            {
                var key = TDV2200KeyRegistry.GetKey(orangeGrids[i]);
                Assert.NotNull(key);
                Assert.Equal(TDVKeyColor.Orange, key.Color);
            }
        }

        [Fact]
        public void Colors_WhiteKeys_AreDefault()
        {
            string[] whiteGrids = { "E0", "E1", "E2", "D0", "D1", "D2",
                "C0", "C1", "C2", "B99", "B0", "B1", "B11",
                "D51", "D52", "D53", "D54",
                "C51", "C52", "C53", "C54",
                "B51", "B52", "B53", "B54",
                "A51", "A53", "A5" };

            for (int i = 0; i < whiteGrids.Length; i++)
            {
                var key = TDV2200KeyRegistry.GetKey(whiteGrids[i]);
                Assert.NotNull(key);
                Assert.Equal(TDVKeyColor.White, key.Color);
            }
        }

        // ─── Flags ───────────────────────────────────────────────

        [Fact]
        public void Flags_ModifierKeys_HaveIsModifierFlag()
        {
            Assert.True((TDV2200KeyRegistry.GetKey("D0")!.Flags & TDVKeyFlags.IsModifier) != 0);
            Assert.True((TDV2200KeyRegistry.GetKey("B99")!.Flags & TDVKeyFlags.IsModifier) != 0);
            Assert.True((TDV2200KeyRegistry.GetKey("B11")!.Flags & TDVKeyFlags.IsModifier) != 0);
        }

        [Fact]
        public void Flags_ToggleKeys_HaveIsToggleFlag()
        {
            Assert.True((TDV2200KeyRegistry.GetKey("E0")!.Flags & TDVKeyFlags.IsToggle) != 0);
            Assert.True((TDV2200KeyRegistry.GetKey("C0")!.Flags & TDVKeyFlags.IsToggle) != 0);
        }

        [Fact]
        public void Flags_PushKeys_HaveIsProgrammableFlag()
        {
            for (int i = 1; i <= 8; i++)
            {
                Assert.True((TDV2200KeyRegistry.GetKey($"G{i}")!.Flags & TDVKeyFlags.IsProgrammable) != 0);
            }
        }

        [Fact]
        public void Flags_CursorKeys_HaveAlwaysSameCodeFlag()
        {
            Assert.True((TDV2200KeyRegistry.GetKey("C48")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // UP
            Assert.True((TDV2200KeyRegistry.GetKey("A48")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // DOWN
            Assert.True((TDV2200KeyRegistry.GetKey("B47")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // LEFT
            Assert.True((TDV2200KeyRegistry.GetKey("B49")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // RIGHT
            Assert.True((TDV2200KeyRegistry.GetKey("B48")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // HOME
            Assert.True((TDV2200KeyRegistry.GetKey("G0")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0);  // ESC
            Assert.True((TDV2200KeyRegistry.GetKey("E14")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // DEL
            Assert.True((TDV2200KeyRegistry.GetKey("D13")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // LF
            Assert.True((TDV2200KeyRegistry.GetKey("C13")!.Flags & TDVKeyFlags.AlwaysSameCode) != 0); // CR
        }

        [Fact]
        public void Flags_NumericPadKeys_HaveIsNumericPadFlag()
        {
            string[] npGrids = { "D51", "D52", "D53", "D54",
                "C51", "C52", "C53", "C54",
                "B51", "B52", "B53",
                "A51", "A53" };

            for (int i = 0; i < npGrids.Length; i++)
            {
                Assert.True((TDV2200KeyRegistry.GetKey(npGrids[i])!.Flags & TDVKeyFlags.IsNumericPad) != 0);
            }
        }

        // ─── Label lookups ───────────────────────────────────────

        [Fact]
        public void Labels_NorwegianDefault_ReturnsCorrectLabels()
        {
            Assert.True(TDV2200KeyRegistry.TryGetLabel("G9", "no", out var label));
            Assert.Equal("MERK", label.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("G53", "no", out var helpLabel));
            Assert.Equal("HJELP", helpLabel.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("G54", "no", out var exitLabel));
            Assert.Equal("SLUTT", exitLabel.Primary);
        }

        [Fact]
        public void Labels_EnglishVariant_ReturnsCorrectLabels()
        {
            Assert.True(TDV2200KeyRegistry.TryGetLabel("G9", "en", out var label));
            Assert.Equal("MARK", label.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("G53", "en", out var helpLabel));
            Assert.Equal("HELP", helpLabel.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("G54", "en", out var exitLabel));
            Assert.Equal("EXIT", exitLabel.Primary);
        }

        [Fact]
        public void Labels_FinnishVariant_ReturnsCorrectLabels()
        {
            Assert.True(TDV2200KeyRegistry.TryGetLabel("G10", "fi", out var label));
            Assert.Equal("KENTTÄ", label.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("G53", "fi", out var helpLabel));
            Assert.Equal("AUTA", helpLabel.Primary);
        }

        [Fact]
        public void Labels_FrenchVariant_ReturnsCorrectLabels()
        {
            Assert.True(TDV2200KeyRegistry.TryGetLabel("G53", "fr", out var helpLabel));
            Assert.Equal("AIDE", helpLabel.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("G54", "fr", out var exitLabel));
            Assert.Equal("FIN", exitLabel.Primary);
        }

        [Fact]
        public void Labels_NationalCharacterKeys_ReturnCorrectVariants()
        {
            // C10 position: Ø (Norwegian) vs Ö (Swedish) vs ; (US)
            Assert.True(TDV2200KeyRegistry.TryGetLabel("C10", "no", out var noLabel));
            Assert.Equal("\u00D8", noLabel.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("C10", "sv", out var svLabel));
            Assert.Equal("\u00D6", svLabel.Primary);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("C10", "us", out var usLabel));
            Assert.Equal(";", usLabel.Primary);
        }

        [Fact]
        public void Labels_FunctionKeys_HaveAlternativeLabels()
        {
            Assert.True(TDV2200KeyRegistry.TryGetLabel("F52", "no", out var f2Label));
            Assert.Equal("F2", f2Label.Primary);
            Assert.Equal("SI", f2Label.Alternative);

            Assert.True(TDV2200KeyRegistry.TryGetLabel("E51", "no", out var f5Label));
            Assert.Equal("F5", f5Label.Primary);
            Assert.Equal("000", f5Label.Alternative);
        }

        // ─── Default Alt mappings ────────────────────────────────

        [Fact]
        public void DefaultAltMappings_LetterKeys_ResolveToCorrectGridPositions()
        {
            Assert.Equal("G9", TDV2200KeyRegistry.GetDefaultAltTarget(65));   // Alt+A → MERK
            Assert.Equal("G10", TDV2200KeyRegistry.GetDefaultAltTarget(76));  // Alt+L → FELT
            Assert.Equal("G11", TDV2200KeyRegistry.GetDefaultAltTarget(82));  // Alt+R → AVSN
            Assert.Equal("G12", TDV2200KeyRegistry.GetDefaultAltTarget(69));  // Alt+E → SETN
            Assert.Equal("G13", TDV2200KeyRegistry.GetDefaultAltTarget(87));  // Alt+W → ORD
            Assert.Equal("G53", TDV2200KeyRegistry.GetDefaultAltTarget(72));  // Alt+H → HJELP
            Assert.Equal("G51", TDV2200KeyRegistry.GetDefaultAltTarget(85));  // Alt+U → FUNK
            Assert.Equal("G52", TDV2200KeyRegistry.GetDefaultAltTarget(80));  // Alt+P → SKRIV
            Assert.Equal("E47", TDV2200KeyRegistry.GetDefaultAltTarget(88));  // Alt+X → GUILLEMETS
            Assert.Equal("G54", TDV2200KeyRegistry.GetDefaultAltTarget(83));  // Alt+S → SLUTT
            Assert.Equal("G48", TDV2200KeyRegistry.GetDefaultAltTarget(75));  // Alt+K → KOPI
            Assert.Equal("G49", TDV2200KeyRegistry.GetDefaultAltTarget(86));  // Alt+V → FLYTT
            Assert.Equal("E48", TDV2200KeyRegistry.GetDefaultAltTarget(74));  // Alt+J → JUST
        }

        [Fact]
        public void DefaultAltMappings_NumberKeys_ResolveToPushKeys()
        {
            for (int i = 1; i <= 8; i++)
            {
                Assert.Equal($"G{i}", TDV2200KeyRegistry.GetDefaultAltTarget(48 + i));
            }
        }

        [Fact]
        public void DefaultAltMappings_NavigationKeys_ResolveCorrectly()
        {
            Assert.Equal("G47", TDV2200KeyRegistry.GetDefaultAltTarget(46));  // Alt+Delete → STRYK
            Assert.Equal("D47", TDV2200KeyRegistry.GetDefaultAltTarget(33));  // Alt+PageUp → ROLLUP (spec section 6.3)
            Assert.Equal("D49", TDV2200KeyRegistry.GetDefaultAltTarget(34));  // Alt+PageDown → ROLLDN
        }

        // ─── Cross-verification with ND246KeyboardMapper ─────────

        [Fact]
        public void CrossVerify_AllExtendedSequences_MatchND246Mapper()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // Verify every key that has an extended sequence
            string[] keysWithSequences =
            {
                "G9", "G10", "G11", "G12", "G13",
                "G47", "G48", "G49", "G51", "G52", "G53", "G54",
                "F47", "F48", "F49", "F51", "F52", "F53", "F54",
                "E13", "E47", "E48", "E49", "E51", "E52", "E53", "E54",
                "D99", "D47", "D48", "D49",
                "C99", "C47", "C48", "C49",
                "B47", "B48", "B49",
                "A47", "A48", "A49"
            };

            for (int i = 0; i < keysWithSequences.Length; i++)
            {
                var grid = keysWithSequences[i];
                var mapperNormal = mapper.MapGridKey(grid, shift: false);
                var registryNormal = TDV2200KeyRegistry.GetSequence(grid, true, false);
                Assert.Equal(mapperNormal, registryNormal);

                var mapperShift = mapper.MapGridKey(grid, shift: true);
                var registryShift = TDV2200KeyRegistry.GetSequence(grid, true, false, shift: true);
                Assert.Equal(mapperShift, registryShift);
            }
        }

        [Fact]
        public void CrossVerify_CtrlKeys_MatchND246Mapper()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // F52 Ctrl
            Assert.Equal(mapper.MapGridKey("F52", ctrl: true),
                TDV2200KeyRegistry.GetSequence("F52", true, false, ctrl: true));
            // F53 Ctrl
            Assert.Equal(mapper.MapGridKey("F53", ctrl: true),
                TDV2200KeyRegistry.GetSequence("F53", true, false, ctrl: true));
        }

        [Fact]
        public void CrossVerify_NumPadFunctionMode_MatchND246Mapper()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true, numericPadFunctionMode: true);

            string[] numpadKeys = { "D51", "D52", "D53", "D54",
                "C51", "C52", "C53", "C54",
                "B51", "B52", "B53", "B54",
                "A51", "A53" };

            for (int i = 0; i < numpadKeys.Length; i++)
            {
                var grid = numpadKeys[i];
                Assert.Equal(mapper.MapGridKey(grid),
                    TDV2200KeyRegistry.GetSequence(grid, true, true));
            }
        }

        [Fact]
        public void CrossVerify_NameBasedMapping_MatchND246Mapper()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // Verify name → grid → sequence matches mapper.MapKey
            Assert.Equal(mapper.MapKey("MARK"), TDV2200KeyRegistry.GetSequence(
                TDV2200KeyRegistry.GetGridForName("MARK")!, true, false));
            Assert.Equal(mapper.MapKey("FIELD"), TDV2200KeyRegistry.GetSequence(
                TDV2200KeyRegistry.GetGridForName("FIELD")!, true, false));
            Assert.Equal(mapper.MapKey("HELP"), TDV2200KeyRegistry.GetSequence(
                TDV2200KeyRegistry.GetGridForName("HELP")!, true, false));
            Assert.Equal(mapper.MapKey("EXIT"), TDV2200KeyRegistry.GetSequence(
                TDV2200KeyRegistry.GetGridForName("EXIT")!, true, false));
        }

        // ─── TryGetKey ──────────────────────────────────────────

        [Fact]
        public void TryGetKey_ExistingKey_ReturnsTrue()
        {
            Assert.True(TDV2200KeyRegistry.TryGetKey("G9", out var key));
            Assert.NotNull(key);
            Assert.Equal("MERK", key.Name);
        }

        [Fact]
        public void TryGetKey_NonExistentKey_ReturnsFalse()
        {
            Assert.False(TDV2200KeyRegistry.TryGetKey("Z99", out _));
        }

        [Fact]
        public void GetKey_NonExistentKey_ReturnsNull()
        {
            Assert.Null(TDV2200KeyRegistry.GetKey("Z99"));
        }

        // ─── GetSequence edge cases ─────────────────────────────

        [Fact]
        public void GetSequence_NonExistentGrid_ReturnsNull()
        {
            Assert.Null(TDV2200KeyRegistry.GetSequence("Z99", true, false));
        }

        [Fact]
        public void GetSequence_ModifierKey_ReturnsNull()
        {
            // Modifier keys have no sequences
            Assert.Null(TDV2200KeyRegistry.GetSequence("D0", true, false));   // CTRL
            Assert.Null(TDV2200KeyRegistry.GetSequence("B99", true, false));  // LSHIFT
            Assert.Null(TDV2200KeyRegistry.GetSequence("B11", true, false));  // RSHIFT
        }

        [Fact]
        public void GetSequence_ToggleKey_ReturnsNull()
        {
            Assert.Null(TDV2200KeyRegistry.GetSequence("E0", true, false));  // CAPS
            Assert.Null(TDV2200KeyRegistry.GetSequence("C0", true, false));  // LOCK
        }
    }
}
