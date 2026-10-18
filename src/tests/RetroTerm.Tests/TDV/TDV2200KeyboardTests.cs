using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.TDV
{
    /// <summary>
    /// Tests for TDV keyboard mappers in TDV2115 compatibility mode and normal mode.
    /// In TDV2115 mode, arrow keys send C0 control codes instead of ESC sequences.
    /// </summary>
    [Collection("TDVKeyBinding")]
    public class TDV2200KeyboardTests
    {
        public TDV2200KeyboardTests()
        {
            TDVKeyBindingConfiguration.ResetForTesting();
        }

        [Fact]
        public void TDV2200_RightArrow_In2115Mode_Sends0x18()
        {
            // Arrange
            var mapper = new TDV2200KeyboardMapper();

            // Act - In TDV2115 mode, arrow keys send C0 codes
            var result = mapper.MapKey(39, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("\x18", result); // 0x18 = CAN
        }

        [Fact]
        public void TDV2200_UpArrow_In2115Mode_Sends0x1C()
        {
            // Arrange
            var mapper = new TDV2200KeyboardMapper();

            // Act
            var result = mapper.MapKey(38, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("\x1c", result); // 0x1C = FS
        }

        [Fact]
        public void TDV2200_DownArrow_In2115Mode_Sends0x0B()
        {
            // Arrange
            var mapper = new TDV2200KeyboardMapper();

            // Act
            var result = mapper.MapKey(40, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("\x0b", result); // 0x0B = VT
        }

        [Fact]
        public void TDV2200_LeftArrow_In2115Mode_Sends0x08()
        {
            // Arrange
            var mapper = new TDV2200KeyboardMapper();

            // Act
            var result = mapper.MapKey(37, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("\x08", result); // 0x08 = BS
        }

        [Fact]
        public void TDV2200_Home_In2115Mode_Sends0x1D()
        {
            // Corrected twice on 31 August 2026, ending up back where it started. First a live
            // test against a real ND-100 (terminal_localkey pressing HOME, TDV2115 mode
            // confirmed on via a DECRQM round trip) showed the app always sent GS - so this was
            // "fixed" to expect DLE, matching TDV2200KeyRegistry's SimpleAscii at the time. That
            // second fix was itself wrong: keyboard-spec.md section 6.8.3, citing the TDV-2200/9
            // User's Guide (ND-30.003.04 EN), explains the DLE reading was an OCR error corrected
            // by a fresh OCR of section 9.1, which marks HOME "is always" GS in both modes - the
            // registry's SimpleAscii is corrected to match. HOME was never the exception.
            //
            // Arrange
            var mapper = new TDV2200KeyboardMapper();

            // Act
            var result = mapper.MapKey(36, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("\x1d", result); // 0x1D = GS
        }

        [Theory]
        [InlineData(38, "\x1c")]  // Up → FS
        [InlineData(40, "\x0b")]  // Down → VT
        [InlineData(39, "\x18")]  // Right → CAN
        [InlineData(37, "\x08")]  // Left → BS
        [InlineData(36, "\x1d")]  // Home → GS, same in both modes
        public void TDV2200_ArrowKeys_In2115Mode_SendC0Codes(int keyCode, string expected)
        {
            // Arrange
            var mapper = new TDV2200KeyboardMapper();

            // Act - In TDV2115 mode, arrow keys send C0 codes
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TDV2215_RightArrow_In2115Mode_Sends0x18()
        {
            // Arrange
            var mapper = new TDV2215KeyboardMapper();

            // Act
            var result = mapper.MapKey(39, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("\x18", result); // 0x18 = CAN
        }

        [Fact]
        public void TDV1200_RightArrow_In2115Mode_Sends0x18()
        {
            // Arrange
            var mapper = new TDV1200KeyboardMapper();

            // Act
            var result = mapper.MapKey(39, KeyModifiers.None, TerminalModes.TDV2115Mode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("\x18", result); // 0x18 = CAN
        }

        #region Normal Mode Tests (TDV C0 codes from registry)

        [Theory]
        [InlineData(38, "\x1c")]   // Up = FS
        [InlineData(40, "\x0b")]   // Down = VT
        [InlineData(39, "\x18")]   // Right = CAN
        [InlineData(37, "\x08")]   // Left = BS
        [InlineData(36, "\x1d")]   // Home = GS
        public void TDV2200_ArrowKeys_InNormalMode_SendC0Codes(int keyCode, string expected)
        {
            // Arrange
            var mapper = new TDV2200KeyboardMapper();

            // Act - Arrow keys send TDV C0 codes via registry (AlwaysSameCode)
            var result = mapper.MapKey(keyCode, KeyModifiers.None, TerminalModes.None);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion
    }
}
