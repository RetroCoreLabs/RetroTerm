using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV
{
    /// <summary>
    /// Comprehensive keyboard tests for all TDV terminal models
    /// Tests both C0 control codes and ESC sequences with modifiers
    /// </summary>
    [Collection("TDVKeyBinding")]
    public class TDVKeyboardComprehensiveTests
    {
        public TDVKeyboardComprehensiveTests()
        {
            // Ensure tests use default key binding configuration regardless of user config file
            TDVKeyBindingConfiguration.ResetForTesting();
        }

        #region TDV1200 Tests

        [Theory]
        [InlineData(38, KeyModifiers.None, "\x1c")]  // Up → FS (0x1C)
        [InlineData(40, KeyModifiers.None, "\x0b")]  // Down → VT (0x0B)
        [InlineData(39, KeyModifiers.None, "\x18")]  // Right → CAN (0x18)
        [InlineData(37, KeyModifiers.None, "\x08")]  // Left → BS (0x08)
        [InlineData(36, KeyModifiers.None, "\x1d")]  // Home in TDV2115 mode → GS (0x1D), same in both modes
        public void TDV1200_ArrowKeys_In2115Mode_SendC0Codes(int keyCode, KeyModifiers modifiers, string expected)
        {
            var mapper = new TDV1200KeyboardMapper();
            // C0 control codes are sent when TDV2115Mode is active
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.TDV2115Mode);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(38, KeyModifiers.Shift, "\x1c")]  // Shift+Up → same C0 (AlwaysSameCode)
        [InlineData(38, KeyModifiers.Ctrl, "\x1c")]   // Ctrl+Up → same C0 (AlwaysSameCode)
        [InlineData(39, KeyModifiers.Shift, "\x18")]   // Shift+Right → same C0 (AlwaysSameCode)
        [InlineData(39, KeyModifiers.Ctrl, "\x18")]    // Ctrl+Right → same C0 (AlwaysSameCode)
        public void TDV1200_ArrowKeys_WithModifiers_SendC0Codes(int keyCode, KeyModifiers modifiers, string expected)
        {
            var mapper = new TDV1200KeyboardMapper();
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(38, KeyModifiers.Alt)]   // Alt+Up → no binding, step 3 skips Alt → null
        [InlineData(39, KeyModifiers.Alt)]   // Alt+Right → no binding, step 3 skips Alt → null
        public void TDV1200_ArrowKeys_WithAlt_ReturnNull(int keyCode, KeyModifiers modifiers)
        {
            var mapper = new TDV1200KeyboardMapper();
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.None);
            Assert.Null(result);
        }

        [Theory]
        [InlineData(72, "\x1b[46_")]    // Alt+H → HJELP (G53)
        [InlineData(68, "\x1b[20_")]    // Alt+D → REPLACE (F49)
        [InlineData(85, "\x1b[42_")]    // Alt+U → FUNK (G51)
        [InlineData(80, "\x1b[44_")]    // Alt+P → SKRIV (G52)
        [InlineData(75, "\x1b[12_")]    // Alt+K → KOPI (G48)
        [InlineData(86, "\x1b[14_")]    // Alt+V → FLYTT (G49)
        public void TDV1200_AltLetter_SendsSpecialKeys(int keyCode, string expected)
        {
            var mapper = new TDV1200KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.Alt, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(112, "\x1b[50_")]   // F1 (VK 112) → F51 TDV F1
        [InlineData(113, "\x1b[52_")]   // F2 (VK 113) → F52
        [InlineData(114, "\x1b[55_")]   // F3 (VK 114) → F53
        [InlineData(115, "\x1b[58_")]   // F4 (VK 115) → F54
        [InlineData(116, "\x1b[60_")]   // F5 (VK 116) → E51
        [InlineData(117, "\x1b[62_")]   // F6 (VK 117) → E52
        [InlineData(118, "\x1b[64_")]   // F7 (VK 118) → E53
        [InlineData(119, "\x1b[66_")]   // F8 (VK 119) → E54
        public void TDV1200_FunctionKeys_SendTDVNativeSequences(int keyCode, string expected)
        {
            var mapper = new TDV1200KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(120)]   // F9 (VK 120) → no TDV equivalent
        [InlineData(121)]   // F10 (VK 121) → no TDV equivalent
        [InlineData(122)]   // F11 (VK 122) → no TDV equivalent
        [InlineData(123)]   // F12 (VK 123) → no TDV equivalent
        public void TDV1200_FunctionKeys_NoTDVEquivalent_ReturnNull(int keyCode)
        {
            var mapper = new TDV1200KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);
            Assert.Null(result);
        }

        #endregion

        #region TDV2215 Tests

        [Theory]
        [InlineData(38, KeyModifiers.None, "\x1c")]  // Up → FS (0x1C)
        [InlineData(40, KeyModifiers.None, "\x0b")]  // Down → VT (0x0B)
        [InlineData(39, KeyModifiers.None, "\x18")]  // Right → CAN (0x18)
        [InlineData(37, KeyModifiers.None, "\x08")]  // Left → BS (0x08)
        [InlineData(36, KeyModifiers.None, "\x1d")]  // Home in TDV2115 mode → GS (0x1D), same in both modes
        public void TDV2215_ArrowKeys_In2115Mode_SendC0Codes(int keyCode, KeyModifiers modifiers, string expected)
        {
            var mapper = new TDV2215KeyboardMapper();
            // C0 control codes are sent when TDV2115Mode is active
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.TDV2115Mode);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(38, KeyModifiers.Shift, "\x1c")]  // Shift+Up → same C0 (AlwaysSameCode)
        [InlineData(38, KeyModifiers.Ctrl, "\x1c")]   // Ctrl+Up → same C0 (AlwaysSameCode)
        public void TDV2215_ArrowKeys_WithModifiers_SendC0Codes(int keyCode, KeyModifiers modifiers, string expected)
        {
            var mapper = new TDV2215KeyboardMapper();
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TDV2215_ArrowKeys_WithAlt_ReturnNull()
        {
            var mapper = new TDV2215KeyboardMapper();
            var result = mapper.MapKey(38, KeyModifiers.Alt, TerminalModes.None);
            Assert.Null(result);
        }

        [Theory]
        [InlineData(72, "\x1b[46_")]    // Alt+H → HJELP (G53)
        [InlineData(68, "\x1b[20_")]    // Alt+D → REPLACE (F49)
        [InlineData(85, "\x1b[42_")]    // Alt+U → FUNK (G51)
        public void TDV2215_AltLetter_SendsSpecialKeys(int keyCode, string expected)
        {
            var mapper = new TDV2215KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.Alt, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(112, "\x1b[50_")]   // F1 (VK 112) → F51 TDV F1
        [InlineData(113, "\x1b[52_")]   // F2 (VK 113) → F52
        [InlineData(114, "\x1b[55_")]   // F3 (VK 114) → F53
        [InlineData(115, "\x1b[58_")]   // F4 (VK 115) → F54
        [InlineData(116, "\x1b[60_")]   // F5 (VK 116) → E51
        [InlineData(117, "\x1b[62_")]   // F6 (VK 117) → E52
        [InlineData(118, "\x1b[64_")]   // F7 (VK 118) → E53
        [InlineData(119, "\x1b[66_")]   // F8 (VK 119) → E54
        public void TDV2215_FunctionKeys_SendTDVNativeSequences(int keyCode, string expected)
        {
            var mapper = new TDV2215KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(120)]   // F9 (VK 120) → no TDV equivalent
        [InlineData(121)]   // F10 (VK 121) → no TDV equivalent
        [InlineData(122)]   // F11 (VK 122) → no TDV equivalent
        [InlineData(123)]   // F12 (VK 123) → no TDV equivalent
        [InlineData(124)]   // F13 (VK 124) → no TDV equivalent
        [InlineData(131)]   // F20 (VK 131) → no TDV equivalent
        public void TDV2215_FunctionKeys_NoTDVEquivalent_ReturnNull(int keyCode)
        {
            var mapper = new TDV2215KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);
            Assert.Null(result);
        }

        #endregion

        #region TDV2200 Tests

        [Theory]
        [InlineData(38, KeyModifiers.None, "\x1c")]  // Up → FS (0x1C)
        [InlineData(40, KeyModifiers.None, "\x0b")]  // Down → VT (0x0B)
        [InlineData(39, KeyModifiers.None, "\x18")]  // Right → CAN (0x18)
        [InlineData(37, KeyModifiers.None, "\x08")]  // Left → BS (0x08)
        [InlineData(36, KeyModifiers.None, "\x1d")]  // Home in TDV2115 mode → GS (0x1D), same in both modes
        public void TDV2200_ArrowKeys_In2115Mode_SendC0Codes(int keyCode, KeyModifiers modifiers, string expected)
        {
            var mapper = new TDV2200KeyboardMapper();
            // C0 control codes are sent when TDV2115Mode is active
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.TDV2115Mode);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(38, KeyModifiers.Shift, "\x1c")]  // Shift+Up → same C0 (AlwaysSameCode)
        [InlineData(38, KeyModifiers.Ctrl, "\x1c")]   // Ctrl+Up → same C0 (AlwaysSameCode)
        [InlineData(40, KeyModifiers.Shift, "\x0b")]   // Shift+Down → same C0 (AlwaysSameCode)
        [InlineData(39, KeyModifiers.Shift, "\x18")]   // Shift+Right → same C0 (AlwaysSameCode)
        [InlineData(37, KeyModifiers.Shift, "\x08")]   // Shift+Left → same C0 (AlwaysSameCode)
        public void TDV2200_ArrowKeys_WithModifiers_SendC0Codes(int keyCode, KeyModifiers modifiers, string expected)
        {
            var mapper = new TDV2200KeyboardMapper();
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TDV2200_ArrowKeys_WithAlt_ReturnNull()
        {
            var mapper = new TDV2200KeyboardMapper();
            var result = mapper.MapKey(38, KeyModifiers.Alt, TerminalModes.None);
            Assert.Null(result);
        }

        [Theory]
        [InlineData(72, "\x1b[46_")]    // Alt+H → HJELP (G53)
        [InlineData(68, "\x1b[20_")]    // Alt+D → REPLACE (F49)
        [InlineData(85, "\x1b[42_")]    // Alt+U → FUNK (G51)
        [InlineData(80, "\x1b[44_")]    // Alt+P → SKRIV (G52)
        [InlineData(88, "\x1b[22_")]    // Alt+X → GUILLEMETS (E47)
        [InlineData(8, "\x1b[30_")]     // Alt+Backspace → ANGRE (D48)
        [InlineData(77, "\x1b[84_")]    // Alt+M → MODE (C99)
        [InlineData(70, "\x1b[18_")]    // Alt+F → SEARCH (F48)
        [InlineData(83, "\x1b[48_")]    // Alt+S → SLUTT (G54)
        [InlineData(75, "\x1b[12_")]    // Alt+K → KOPI (G48)
        [InlineData(86, "\x1b[14_")]    // Alt+V → FLYTT (G49)
        [InlineData(74, "\x1b[24_")]    // Alt+J → JUST (E48)
        [InlineData(65, "\x1b[00_")]    // Alt+A → MERK (G9)
        [InlineData(76, "\x1b[02_")]    // Alt+L → FELT (G10)
        [InlineData(82, "\x1b[04_")]    // Alt+R → AVSN (G11)
        [InlineData(69, "\x1b[06_")]    // Alt+E → SETN (G12)
        [InlineData(87, "\x1b[08_")]    // Alt+W → ORD (G13)
        [InlineData(73, "\x1b[26_")]    // Alt+I → SINGLEGUILLEMETS (E49)
        public void TDV2200_AltLetter_SendsSpecialKeys(int keyCode, string expected)
        {
            var mapper = new TDV2200KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.Alt, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(49, 1)]  // Alt+1 → PUSH1
        [InlineData(50, 2)]  // Alt+2 → PUSH2
        [InlineData(56, 8)]  // Alt+8 → PUSH8
        public void TDV2200_AltNumber_SendsPushKeys(int keyCode, int pushNum)
        {
            TDVPushKeyConfiguration.ResetForTesting();
            var testString = $"P{pushNum}";
            TDVPushKeyConfiguration.Instance.ProgramKey(pushNum, testString);

            var mapper = new TDV2200KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.Alt, TerminalModes.None);
            Assert.Equal(testString, result);
        }

        [Theory]
        [InlineData(112, "\x1b[50_")]   // F1 (VK 112) → F51 TDV F1
        [InlineData(113, "\x1b[52_")]   // F2 (VK 113) → F52
        [InlineData(114, "\x1b[55_")]   // F3 (VK 114) → F53
        [InlineData(115, "\x1b[58_")]   // F4 (VK 115) → F54
        [InlineData(116, "\x1b[60_")]   // F5 (VK 116) → E51
        [InlineData(117, "\x1b[62_")]   // F6 (VK 117) → E52
        [InlineData(118, "\x1b[64_")]   // F7 (VK 118) → E53
        [InlineData(119, "\x1b[66_")]   // F8 (VK 119) → E54
        public void TDV2200_FunctionKeys_SendTDVNativeSequences(int keyCode, string expected)
        {
            var mapper = new TDV2200KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(120)]   // F9 (VK 120) → no TDV equivalent
        [InlineData(121)]   // F10 (VK 121) → no TDV equivalent
        [InlineData(122)]   // F11 (VK 122) → no TDV equivalent
        [InlineData(123)]   // F12 (VK 123) → no TDV equivalent
        [InlineData(124)]   // F13 (VK 124) → no TDV equivalent
        [InlineData(131)]   // F20 (VK 131) → no TDV equivalent
        public void TDV2200_FunctionKeys_NoTDVEquivalent_ReturnNull(int keyCode)
        {
            var mapper = new TDV2200KeyboardMapper();
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);
            Assert.Null(result);
        }

        [Theory]
        [InlineData(46, KeyModifiers.Alt, "\x1b[10_")]   // Alt+Delete → STRYK (G47)
        [InlineData(33, KeyModifiers.Alt, "\x1B[28_")]   // Alt+PageUp → ROLLUP (D47), spec section 6.3
        [InlineData(34, KeyModifiers.Alt, "\x1B[32_")]   // Alt+PageDown → ROLLDN (D49)
        public void TDV2200_AltNavigation_SendsSpecialKeys(int keyCode, KeyModifiers modifiers, string expected)
        {
            var mapper = new TDV2200KeyboardMapper();
            var result = mapper.MapKey(keyCode, modifiers, TerminalModes.None);
            Assert.Equal(expected, result);
        }

        #endregion

        #region KeyboardMapperFactory Tests

        [Fact]
        public void KeyboardMapperFactory_TDV2200Emulator_CreatesCorrectMapper()
        {
            // Test that "TDV2200Emulator" (with suffix) creates TDV2200KeyboardMapper
            var mapper = KeyboardMapperFactory.CreateMapper("TDV2200Emulator");
            Assert.IsType<TDV2200KeyboardMapper>(mapper);
        }

        [Fact]
        public void KeyboardMapperFactory_TDV2200_CreatesCorrectMapper()
        {
            // Test that "TDV2200" (without suffix) also works
            var mapper = KeyboardMapperFactory.CreateMapper("TDV2200");
            Assert.IsType<TDV2200KeyboardMapper>(mapper);
        }

        [Fact]
        public void KeyboardMapperFactory_TDV2215Emulator_CreatesCorrectMapper()
        {
            var mapper = KeyboardMapperFactory.CreateMapper("TDV2215Emulator");
            Assert.IsType<TDV2215KeyboardMapper>(mapper);
        }

        [Fact]
        public void KeyboardMapperFactory_TDV1200Emulator_CreatesCorrectMapper()
        {
            var mapper = KeyboardMapperFactory.CreateMapper("TDV1200Emulator");
            Assert.IsType<TDV1200KeyboardMapper>(mapper);
        }

        #endregion

        #region C0 Control Code Tests (All TDV Models)

        [Theory]
        [InlineData(typeof(TDV1200KeyboardMapper))]
        [InlineData(typeof(TDV2215KeyboardMapper))]
        [InlineData(typeof(TDV2200KeyboardMapper))]
        public void AllTDV_ArrowKeys_In2115Mode_SendC0ControlCodes(Type mapperType)
        {
            var mapper = (IKeyboardMapper)Activator.CreateInstance(mapperType)!;

            // In TDV2115 mode, arrow keys send C0 control codes
            var up = mapper.MapKey(38, KeyModifiers.None, TerminalModes.TDV2115Mode);
            var down = mapper.MapKey(40, KeyModifiers.None, TerminalModes.TDV2115Mode);
            var right = mapper.MapKey(39, KeyModifiers.None, TerminalModes.TDV2115Mode);
            var left = mapper.MapKey(37, KeyModifiers.None, TerminalModes.TDV2115Mode);
            var home = mapper.MapKey(36, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert C0 codes (single byte control codes)
            Assert.Equal("\x1c", up);      // FS
            Assert.Equal("\x0b", down);    // VT
            Assert.Equal("\x18", right);   // CAN
            Assert.Equal("\x08", left);    // BS
            Assert.Equal("\x1d", home);    // GS - same in both modes
        }

        [Theory]
        [InlineData(typeof(TDV1200KeyboardMapper))]
        [InlineData(typeof(TDV2215KeyboardMapper))]
        [InlineData(typeof(TDV2200KeyboardMapper))]
        public void AllTDV_ArrowKeys_InNormalMode_SendC0Codes(Type mapperType)
        {
            var mapper = (IKeyboardMapper)Activator.CreateInstance(mapperType)!;

            // TDV arrow keys send C0 codes from registry (AlwaysSameCode)
            var up = mapper.MapKey(38, KeyModifiers.None, TerminalModes.None);
            var down = mapper.MapKey(40, KeyModifiers.None, TerminalModes.None);
            var right = mapper.MapKey(39, KeyModifiers.None, TerminalModes.None);
            var left = mapper.MapKey(37, KeyModifiers.None, TerminalModes.None);
            var home = mapper.MapKey(36, KeyModifiers.None, TerminalModes.None);

            // Assert C0 codes (TDV native)
            Assert.Equal("\x1c", up);    // FS
            Assert.Equal("\x0b", down);  // VT
            Assert.Equal("\x18", right); // CAN
            Assert.Equal("\x08", left);  // BS
            Assert.Equal("\x1d", home);  // GS
        }

        [Theory]
        [InlineData(typeof(TDV1200KeyboardMapper))]
        [InlineData(typeof(TDV2215KeyboardMapper))]
        [InlineData(typeof(TDV2200KeyboardMapper))]
        public void AllTDV_ArrowKeys_WithModifiers_SendC0Codes(Type mapperType)
        {
            var mapper = (IKeyboardMapper)Activator.CreateInstance(mapperType)!;

            // TDV AlwaysSameCode: modifiers don't change arrow key C0 codes
            var shiftUp = mapper.MapKey(38, KeyModifiers.Shift, TerminalModes.None);
            var ctrlUp = mapper.MapKey(38, KeyModifiers.Ctrl, TerminalModes.None);

            Assert.Equal("\x1c", shiftUp);  // AlwaysSameCode: same C0
            Assert.Equal("\x1c", ctrlUp);   // AlwaysSameCode: same C0
        }

        #endregion
    }
}
