using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV
{
    /// <summary>
    /// Tests for ND 246 keyboard mapper based on official keyboard specification
    /// Verifies key-to-host sequences match TDV-2200/9 User's Guide Section 9
    /// CSI format: ESC [ nn _ (hex: 1B 5B nn 5F)
    /// </summary>
    public class ND246KeyboardMapperTests
    {
        [Fact]
        public void ExtendedControlMode_GRowKeys_SendCorrectCSISequences()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // G9 - MERK/MARK
            Assert.Equal("\x1B[00_", mapper.MapGridKey("G9", shift: false));
            Assert.Equal("\x1B[01_", mapper.MapGridKey("G9", shift: true));

            // G10 - FELT/FIELD
            Assert.Equal("\x1B[02_", mapper.MapGridKey("G10", shift: false));
            Assert.Equal("\x1B[03_", mapper.MapGridKey("G10", shift: true));

            // G11 - AVSN/PARA
            Assert.Equal("\x1B[04_", mapper.MapGridKey("G11", shift: false));
            Assert.Equal("\x1B[05_", mapper.MapGridKey("G11", shift: true));

            // G12 - SETN/SENT
            Assert.Equal("\x1B[06_", mapper.MapGridKey("G12", shift: false));
            Assert.Equal("\x1B[07_", mapper.MapGridKey("G12", shift: true));

            // G13 - ORD/WORD
            Assert.Equal("\x1B[08_", mapper.MapGridKey("G13", shift: false));
            Assert.Equal("\x1B[09_", mapper.MapGridKey("G13", shift: true));

            // G47 - STRYK/DELETE
            Assert.Equal("\x1B[10_", mapper.MapGridKey("G47", shift: false));
            Assert.Equal("\x1B[11_", mapper.MapGridKey("G47", shift: true));

            // G48 - KOPI/COPY
            Assert.Equal("\x1B[12_", mapper.MapGridKey("G48", shift: false));
            Assert.Equal("\x1B[13_", mapper.MapGridKey("G48", shift: true));

            // G49 - FLYTT/MOVE
            Assert.Equal("\x1B[14_", mapper.MapGridKey("G49", shift: false));
            Assert.Equal("\x1B[15_", mapper.MapGridKey("G49", shift: true));

            // G51 - FUNK/FUNC
            Assert.Equal("\x1B[42_", mapper.MapGridKey("G51", shift: false));
            Assert.Equal("\x1B[43_", mapper.MapGridKey("G51", shift: true));

            // G52 - SKRIV/PRINT
            Assert.Equal("\x1B[44_", mapper.MapGridKey("G52", shift: false));
            Assert.Equal("\x1B[45_", mapper.MapGridKey("G52", shift: true));

            // G53 - HJELP/HELP
            Assert.Equal("\x1B[46_", mapper.MapGridKey("G53", shift: false));
            Assert.Equal("\x1B[47_", mapper.MapGridKey("G53", shift: true));

            // G54 - SLUTT/EXIT
            Assert.Equal("\x1B[48_", mapper.MapGridKey("G54", shift: false));
            Assert.Equal("\x1B[49_", mapper.MapGridKey("G54", shift: true));
        }

        [Fact]
        public void ExtendedControlMode_FRowKeys_SendCorrectCSISequences()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // F47 - TAB
            Assert.Equal("\x1B[16_", mapper.MapGridKey("F47", shift: false));
            Assert.Equal("\x1B[17_", mapper.MapGridKey("F47", shift: true));

            // F48
            Assert.Equal("\x1B[18_", mapper.MapGridKey("F48", shift: false));
            Assert.Equal("\x1B[19_", mapper.MapGridKey("F48", shift: true));

            // F49
            Assert.Equal("\x1B[20_", mapper.MapGridKey("F49", shift: false));
            Assert.Equal("\x1B[21_", mapper.MapGridKey("F49", shift: true));

            // F51 - F1
            Assert.Equal("\x1B[50_", mapper.MapGridKey("F51", shift: false));
            Assert.Equal("\x1B[51_", mapper.MapGridKey("F51", shift: true));

            // F52 - F2/SI (supports CTRL)
            Assert.Equal("\x1B[52_", mapper.MapGridKey("F52", shift: false));
            Assert.Equal("\x1B[53_", mapper.MapGridKey("F52", shift: true));
            Assert.Equal("\x1B[54_", mapper.MapGridKey("F52", ctrl: true));

            // F53 - F3/SO (supports CTRL)
            Assert.Equal("\x1B[55_", mapper.MapGridKey("F53", shift: false));
            Assert.Equal("\x1B[56_", mapper.MapGridKey("F53", shift: true));
            Assert.Equal("\x1B[57_", mapper.MapGridKey("F53", ctrl: true));

            // F54 - F4/CLEAR
            Assert.Equal("\x1B[58_", mapper.MapGridKey("F54", shift: false));
            Assert.Equal("\x1B[59_", mapper.MapGridKey("F54", shift: true));
        }

        [Fact]
        public void ExtendedControlMode_ERowKeys_SendCorrectCSISequences()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // E13 - Backspace
            Assert.Equal("\x1B[86_", mapper.MapGridKey("E13", shift: false));
            Assert.Equal("\x1B[87_", mapper.MapGridKey("E13", shift: true));

            // E47 - >>
            Assert.Equal("\x1B[22_", mapper.MapGridKey("E47", shift: false));
            Assert.Equal("\x1B[23_", mapper.MapGridKey("E47", shift: true));

            // E48 - JUST
            Assert.Equal("\x1B[24_", mapper.MapGridKey("E48", shift: false));
            Assert.Equal("\x1B[25_", mapper.MapGridKey("E48", shift: true));

            // E49 - ><
            Assert.Equal("\x1B[26_", mapper.MapGridKey("E49", shift: false));
            Assert.Equal("\x1B[27_", mapper.MapGridKey("E49", shift: true));

            // E51-E54 - F5-F8 / numpad top row
            Assert.Equal("\x1B[60_", mapper.MapGridKey("E51", shift: false));
            Assert.Equal("\x1B[61_", mapper.MapGridKey("E51", shift: true));
            Assert.Equal("\x1B[62_", mapper.MapGridKey("E52", shift: false));
            Assert.Equal("\x1B[63_", mapper.MapGridKey("E52", shift: true));
            Assert.Equal("\x1B[64_", mapper.MapGridKey("E53", shift: false));
            Assert.Equal("\x1B[65_", mapper.MapGridKey("E53", shift: true));
            Assert.Equal("\x1B[66_", mapper.MapGridKey("E54", shift: false));
            Assert.Equal("\x1B[67_", mapper.MapGridKey("E54", shift: true));
        }

        [Fact]
        public void ExtendedControlMode_DRowKeys_SendCorrectCSISequences()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // D99 - INNS/EKSP
            Assert.Equal("\x1B[82_", mapper.MapGridKey("D99", shift: false));
            Assert.Equal("\x1B[83_", mapper.MapGridKey("D99", shift: true));

            // D47 - PG DN
            Assert.Equal("\x1B[28_", mapper.MapGridKey("D47", shift: false));
            Assert.Equal("\x1B[29_", mapper.MapGridKey("D47", shift: true));

            // D48 - ANGRE
            Assert.Equal("\x1B[30_", mapper.MapGridKey("D48", shift: false));
            Assert.Equal("\x1B[31_", mapper.MapGridKey("D48", shift: true));

            // D49 - PG UP
            Assert.Equal("\x1B[32_", mapper.MapGridKey("D49", shift: false));
            Assert.Equal("\x1B[33_", mapper.MapGridKey("D49", shift: true));
        }

        [Fact]
        public void ExtendedControlMode_CRowKeys_SendCorrectCSISequences()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // C99 - MODE
            Assert.Equal("\x1B[84_", mapper.MapGridKey("C99", shift: false));
            Assert.Equal("\x1B[85_", mapper.MapGridKey("C99", shift: true));

            // C47 - ERASE PAGE
            Assert.Equal("\x1B[34_", mapper.MapGridKey("C47", shift: false));
            Assert.Equal("\x1B[35_", mapper.MapGridKey("C47", shift: true));

            // C48 - UP arrow (always 0x1C)
            Assert.Equal("\x1C", mapper.MapGridKey("C48", shift: false));
            Assert.Equal("\x1C", mapper.MapGridKey("C48", shift: true));

            // C49 - INSERT
            Assert.Equal("\x1B[36_", mapper.MapGridKey("C49", shift: false));
            Assert.Equal("\x1B[37_", mapper.MapGridKey("C49", shift: true));
        }

        [Fact]
        public void ExtendedControlMode_CursorKeys_SendCorrectSingleByteValues()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // B47 - LEFT arrow: BS (0x08) always
            Assert.Equal("\x08", mapper.MapGridKey("B47", shift: false));
            Assert.Equal("\x08", mapper.MapGridKey("B47", shift: true));

            // B48 - HOME: GS (0x1D) always
            Assert.Equal("\x1D", mapper.MapGridKey("B48", shift: false));
            Assert.Equal("\x1D", mapper.MapGridKey("B48", shift: true));

            // B49 - RIGHT arrow: CAN (0x18) always
            Assert.Equal("\x18", mapper.MapGridKey("B49", shift: false));
            Assert.Equal("\x18", mapper.MapGridKey("B49", shift: true));

            // A48 - DOWN arrow: VT (0x0B) always
            Assert.Equal("\x0B", mapper.MapGridKey("A48", shift: false));
            Assert.Equal("\x0B", mapper.MapGridKey("A48", shift: true));

            // C48 - UP arrow: FS (0x1C) always
            Assert.Equal("\x1C", mapper.MapGridKey("C48", shift: false));
            Assert.Equal("\x1C", mapper.MapGridKey("C48", shift: true));
        }

        [Fact]
        public void ExtendedControlMode_ARowKeys_SendCorrectSequences()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // A47 - 0
            Assert.Equal("\x1B[38_", mapper.MapGridKey("A47", shift: false));
            Assert.Equal("\x1B[39_", mapper.MapGridKey("A47", shift: true));

            // A49 - TAB right
            Assert.Equal("\x1B[40_", mapper.MapGridKey("A49", shift: false));
            Assert.Equal("\x1B[41_", mapper.MapGridKey("A49", shift: true));
        }

        [Fact]
        public void SimpleAsciiMode_NavigationKeys_SendCorrectControlCodes()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: false);

            // C48 - UP arrow (FS)
            Assert.Equal("\x1C", mapper.MapGridKey("C48"));

            // B47 - LEFT arrow (BS)
            Assert.Equal("\x08", mapper.MapGridKey("B47"));

            // B48 - HOME (GS, same as extended mode - see keyboard-spec.md §6.8.3)
            Assert.Equal("\x1D", mapper.MapGridKey("B48"));

            // B49 - RIGHT arrow (CAN)
            Assert.Equal("\x18", mapper.MapGridKey("B49"));

            // A48 - DOWN arrow (VT)
            Assert.Equal("\x0B", mapper.MapGridKey("A48"));
        }

        [Fact]
        public void AlwaysSpecialKeys_SendSameCodeRegardlessOfMode()
        {
            var extMapper = new ND246KeyboardMapper(extendedControlMode: true);
            var simpleMapper = new ND246KeyboardMapper(extendedControlMode: false);

            // G0 - ESC always 1B
            Assert.Equal("\x1B", extMapper.MapGridKey("G0"));

            // E14 - DEL always 7F
            Assert.Equal("\x7F", extMapper.MapGridKey("E14"));
            Assert.Equal("\x7F", simpleMapper.MapGridKey("E14"));

            // D13 - LF always 0A
            Assert.Equal("\x0A", extMapper.MapGridKey("D13"));
            Assert.Equal("\x0A", simpleMapper.MapGridKey("D13"));

            // C13 - CR always 0D
            Assert.Equal("\x0D", extMapper.MapGridKey("C13"));
            Assert.Equal("\x0D", simpleMapper.MapGridKey("C13"));
        }

        [Fact]
        public void NumericPadFunctionMode_SendsCSISequencesWithUnderscoreTerminator()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true, numericPadFunctionMode: true);

            // D51-D53 use _ terminator (not ?)
            Assert.Equal("\x1B[75_", mapper.MapGridKey("D51"));
            Assert.Equal("\x1B[76_", mapper.MapGridKey("D52"));
            Assert.Equal("\x1B[77_", mapper.MapGridKey("D53"));
            Assert.Equal("\x1B[80_", mapper.MapGridKey("D54"));

            // C-row numpad
            Assert.Equal("\x1B[72_", mapper.MapGridKey("C51"));
            Assert.Equal("\x1B[73_", mapper.MapGridKey("C52"));
            Assert.Equal("\x1B[74_", mapper.MapGridKey("C53"));
            Assert.Equal("\x1B[79_", mapper.MapGridKey("C54"));

            // B-row numpad
            Assert.Equal("\x1B[69_", mapper.MapGridKey("B51"));
            Assert.Equal("\x1B[70_", mapper.MapGridKey("B52"));
            Assert.Equal("\x1B[71_", mapper.MapGridKey("B53"));
            // B54 ENTER in pad function mode sends CSI 81 _ like the other pad keys (spec 6.8.4).
            // Pinned as "always CR, special case" with no citation until 27 September 2026.
            Assert.Equal("\x1B[81_", mapper.MapGridKey("B54"));
            // A-row numpad
            Assert.Equal("\x1B[68_", mapper.MapGridKey("A51"));
            Assert.Equal("\x1B[78_", mapper.MapGridKey("A53"));
        }

        [Fact]
        public void MapKey_ByName_ResolvesGridPositionAndMaps()
        {
            var mapper = new ND246KeyboardMapper(extendedControlMode: true);

            // Verify that name-based mapping resolves through grid positions
            Assert.Equal("\x1B[00_", mapper.MapKey("MARK"));
            Assert.Equal("\x1B[02_", mapper.MapKey("FIELD"));
            Assert.Equal("\x1B[04_", mapper.MapKey("PARA"));
            Assert.Equal("\x1B[06_", mapper.MapKey("SENT"));
            Assert.Equal("\x1B[08_", mapper.MapKey("WORD"));
            Assert.Equal("\x1B[42_", mapper.MapKey("FUNC"));
            Assert.Equal("\x1B[44_", mapper.MapKey("PRINT"));
            Assert.Equal("\x1B[46_", mapper.MapKey("HELP"));
            Assert.Equal("\x1B[48_", mapper.MapKey("EXIT"));
        }
    }
}
