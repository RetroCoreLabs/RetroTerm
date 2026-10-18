using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV
{
    public class TDV2200KeyVisualRegistryTests
    {
        [Fact]
        public void Registry_HasExpectedKeyCount()
        {
            // The ND-246 keyboard has 121 keys in the visual registry
            // (includes all alphanumeric, function, navigation, numpad keys)
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            Assert.True(allKeys.Length >= 110, $"Expected at least 110 keys, got {allKeys.Length}");
            Assert.True(allKeys.Length <= 130, $"Expected at most 130 keys, got {allKeys.Length}");
        }

        [Fact]
        public void TryGetKey_KnownKey_ReturnsTrue()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("G0", out var meta));
            Assert.NotNull(meta);
            Assert.Equal("G0", meta.GridPosition);
        }

        [Fact]
        public void TryGetKey_UnknownKey_ReturnsFalse()
        {
            Assert.False(TDV2200KeyVisualRegistry.TryGetKey("Z99", out _));
        }

        [Fact]
        public void AllKeys_HaveValidGridPosition()
        {
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                Assert.False(string.IsNullOrEmpty(allKeys[i].GridPosition),
                    $"Key at index {i} has null/empty GridPosition");
            }
        }

        [Fact]
        public void AllKeys_HavePositiveGeometry()
        {
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                var k = allKeys[i];
                Assert.True(k.Width > 0, $"{k.GridPosition} has non-positive width: {k.Width}");
                Assert.True(k.Height > 0, $"{k.GridPosition} has non-positive height: {k.Height}");
                Assert.True(k.X >= 0, $"{k.GridPosition} has negative X: {k.X}");
                Assert.True(k.Y >= 0, $"{k.GridPosition} has negative Y: {k.Y}");
            }
        }

        [Fact]
        public void AllKeys_FitWithinTotalBounds()
        {
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                var k = allKeys[i];
                Assert.True(k.X + k.Width <= TDV2200KeyVisualRegistry.TotalWidth + 1,
                    $"{k.GridPosition} extends beyond total width: X={k.X} + W={k.Width} > {TDV2200KeyVisualRegistry.TotalWidth}");
                Assert.True(k.Y + k.Height <= TDV2200KeyVisualRegistry.TotalHeight + 1,
                    $"{k.GridPosition} extends beyond total height: Y={k.Y} + H={k.Height} > {TDV2200KeyVisualRegistry.TotalHeight}");
            }
        }

        [Fact]
        public void AllKeys_RowCharMatchesGridPosition()
        {
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                var k = allKeys[i];
                Assert.Equal(k.GridPosition[0], k.Row);
            }
        }

        // --- Specific key checks ---

        [Fact]
        public void E0_IsCapsToggleWithLED()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("E0", out var meta));
            Assert.Equal(KeyCategory.Toggle, meta.Category);
            Assert.True(meta.HasLED);
            Assert.Equal(KeyRenderMode.Text, meta.RenderMode);
            Assert.False(meta.IsBindable);
        }

        [Fact]
        public void C0_IsLockToggleWithLED()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("C0", out var meta));
            Assert.Equal(KeyCategory.Toggle, meta.Category);
            Assert.True(meta.HasLED);
            Assert.Equal(KeyRenderMode.Text, meta.RenderMode);
            Assert.False(meta.IsBindable);
        }

        [Fact]
        public void OnlyE0AndC0HaveLEDs()
        {
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            int ledCount = 0;
            for (int i = 0; i < allKeys.Length; i++)
            {
                if (allKeys[i].HasLED)
                {
                    ledCount++;
                    Assert.True(allKeys[i].GridPosition == "E0" || allKeys[i].GridPosition == "C0",
                        $"Unexpected LED on key {allKeys[i].GridPosition}");
                }
            }
            Assert.Equal(2, ledCount);
        }

        [Fact]
        public void E14_IsTextWithOverlay()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("E14", out var meta));
            Assert.Equal(KeyRenderMode.TextWithOverlay, meta.RenderMode);
            Assert.Equal("a", meta.OverlayText);
            Assert.Equal(TDVKeyColor.Orange, meta.Color);
            Assert.True(meta.IsBindable);
        }

        [Fact]
        public void B54_IsVerticalLetterStack()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("B54", out var meta));
            Assert.Equal(KeyRenderMode.VerticalLetterStack, meta.RenderMode);
            Assert.True(meta.Height > TDV2200KeyVisualRegistry.StandardKeyHeight * 1.5,
                "B54 should be a tall key");
        }

        [Fact]
        public void C13_IsSymbolWithLargerSizeFactor()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("C13", out var meta));
            Assert.Equal(KeyRenderMode.Symbol, meta.RenderMode);
            Assert.Equal(0.55, meta.SymbolSizeFactor, 2);
            Assert.True(meta.SymbolFilled);
            Assert.True(meta.Height > TDV2200KeyVisualRegistry.StandardKeyHeight * 1.5,
                "C13 should be a tall key");
        }

        [Fact]
        public void D47_D49_AreStrokeOnlySymbols()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("D47", out var d47));
            Assert.Equal(KeyRenderMode.Symbol, d47.RenderMode);
            Assert.False(d47.SymbolFilled);
            Assert.Equal(0.50, d47.SymbolSizeFactor, 2);

            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("D49", out var d49));
            Assert.Equal(KeyRenderMode.Symbol, d49.RenderMode);
            Assert.False(d49.SymbolFilled);
            Assert.Equal(0.50, d49.SymbolSizeFactor, 2);
        }

        [Fact]
        public void E13_IsStrokeOnlySymbol()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("E13", out var meta));
            Assert.Equal(KeyRenderMode.Symbol, meta.RenderMode);
            Assert.False(meta.SymbolFilled);
        }

        [Fact]
        public void C47_C49_AreStrokeOnlySymbols()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("C47", out var c47));
            Assert.Equal(KeyRenderMode.Symbol, c47.RenderMode);
            Assert.False(c47.SymbolFilled);

            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("C49", out var c49));
            Assert.Equal(KeyRenderMode.Symbol, c49.RenderMode);
            Assert.False(c49.SymbolFilled);
        }

        [Fact]
        public void F47_IsThreeLineText()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("F47", out var meta));
            Assert.Equal(KeyRenderMode.ThreeLineText, meta.RenderMode);
            Assert.NotNull(meta.MultiLineTexts);
            Assert.Equal(3, meta.MultiLineTexts.Length);
            Assert.Equal("-", meta.MultiLineTexts[0]);
            Assert.Equal("TAB", meta.MultiLineTexts[1]);
            Assert.Equal("+", meta.MultiLineTexts[2]);
        }

        [Fact]
        public void F48_IsTwoLineText()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("F48", out var meta));
            Assert.Equal(KeyRenderMode.TwoLineText, meta.RenderMode);
            Assert.NotNull(meta.MultiLineTexts);
            Assert.Equal(2, meta.MultiLineTexts.Length);
            Assert.Equal("...)", meta.MultiLineTexts[0]);
            Assert.Equal("(...", meta.MultiLineTexts[1]);
        }

        [Fact]
        public void F49_IsTwoLineTextWithUnderline()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("F49", out var meta));
            Assert.Equal(KeyRenderMode.TwoLineText, meta.RenderMode);
            Assert.True(meta.SecondLineUnderlined);
            Assert.NotNull(meta.MultiLineTexts);
            Assert.Equal("aaa", meta.MultiLineTexts[0]);
            Assert.Equal("aaa", meta.MultiLineTexts[1]);
        }

        [Fact]
        public void E47_E49_AreTwoLineBoldText()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("E47", out var e47));
            Assert.Equal(KeyRenderMode.TwoLineText, e47.RenderMode);
            Assert.True(e47.MultiLineBold);
            Assert.Equal(">>", e47.MultiLineTexts![0]);
            Assert.Equal("<<", e47.MultiLineTexts![1]);

            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("E49", out var e49));
            Assert.Equal(KeyRenderMode.TwoLineText, e49.RenderMode);
            Assert.True(e49.MultiLineBold);
            Assert.Equal("><", e49.MultiLineTexts![0]);
            Assert.Equal("<>", e49.MultiLineTexts![1]);
        }

        [Fact]
        public void G14_IsNotBindable()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("G14", out var meta));
            Assert.False(meta.IsBindable);
            Assert.Equal(KeyCategory.Local, meta.Category);
        }

        [Fact]
        public void ModifierKeys_AreNotBindable()
        {
            // B99 (Left Shift), B11 (Right Shift), D0 (CTRL)
            string[] modifiers = { "B99", "B11", "D0" };
            for (int i = 0; i < modifiers.Length; i++)
            {
                Assert.True(TDV2200KeyVisualRegistry.TryGetKey(modifiers[i], out var meta),
                    $"Modifier {modifiers[i]} not found");
                Assert.False(meta.IsBindable, $"Modifier {modifiers[i]} should not be bindable");
                Assert.Equal(KeyCategory.Modifier, meta.Category);
            }
        }

        [Fact]
        public void ToggleKeys_AreNotBindable()
        {
            string[] toggles = { "E0", "C0" };
            for (int i = 0; i < toggles.Length; i++)
            {
                Assert.True(TDV2200KeyVisualRegistry.TryGetKey(toggles[i], out var meta),
                    $"Toggle {toggles[i]} not found");
                Assert.False(meta.IsBindable, $"Toggle {toggles[i]} should not be bindable");
                Assert.Equal(KeyCategory.Toggle, meta.Category);
            }
        }

        [Fact]
        public void PushKeys_AreBindable()
        {
            for (int i = 1; i <= 8; i++)
            {
                Assert.True(TDV2200KeyVisualRegistry.TryGetKey($"G{i}", out var meta),
                    $"Push key G{i} not found");
                Assert.True(meta.IsBindable, $"Push key G{i} should be bindable");
                Assert.Equal(KeyCategory.PushKey, meta.Category);
            }
        }

        [Fact]
        public void SymbolKeys_HaveSymbolRenderMode()
        {
            // All keys with SVG symbols
            string[] symbolKeys = { "A47", "A48", "A49", "B47", "B48", "B49",
                                    "C47", "C48", "C49", "C13", "D13", "D47", "D49", "E13" };
            for (int i = 0; i < symbolKeys.Length; i++)
            {
                Assert.True(TDV2200KeyVisualRegistry.TryGetKey(symbolKeys[i], out var meta),
                    $"Symbol key {symbolKeys[i]} not found");
                Assert.Equal(KeyRenderMode.Symbol, meta.RenderMode);
                Assert.NotNull(meta.SymbolId);
            }
        }

        [Fact]
        public void KeyColors_MatchExistingRegistry()
        {
            // Verify a sampling of key colors match the existing TDV2200KeyRegistry
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                var meta = allKeys[i];
                if (TDV2200KeyRegistry.TryGetKey(meta.GridPosition, out var regKey))
                {
                    Assert.Equal(regKey.Color, meta.Color);
                }
            }
        }

        [Fact]
        public void FunctionKeys_HaveIndicators()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("E51", out var e51));
            Assert.Equal("000", e51.Indicator);

            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("E52", out var e52));
            Assert.Equal("00", e52.Indicator);

            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("F52", out var f52));
            Assert.Equal("SI", f52.Indicator);

            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("F54", out var f54));
            Assert.Equal("CLEAR", f54.Indicator);
        }

        [Fact]
        public void StandardKeyWidth_Is60()
        {
            Assert.Equal(60.0, TDV2200KeyVisualRegistry.StandardKeyWidth);
        }

        [Fact]
        public void StandardKeyHeight_Is60()
        {
            Assert.Equal(60.0, TDV2200KeyVisualRegistry.StandardKeyHeight);
        }

        [Fact]
        public void KeySpacing_Is5()
        {
            Assert.Equal(5.0, TDV2200KeyVisualRegistry.KeySpacing);
        }

        [Fact]
        public void ExportToJson_ProducesNonEmptyOutput()
        {
            var json = TDV2200KeyVisualRegistry.ExportToJson();
            Assert.False(string.IsNullOrEmpty(json));
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        [Fact]
        public void A51_IsDoubleWide()
        {
            Assert.True(TDV2200KeyVisualRegistry.TryGetKey("A51", out var meta));
            double expected = TDV2200KeyVisualRegistry.StandardKeyWidth * 2 + TDV2200KeyVisualRegistry.KeySpacing;
            Assert.Equal(expected, meta.Width, 1);
        }

        [Fact]
        public void AllRows_ArePresent()
        {
            // Check that all 7 rows (A through G) have at least one key
            char[] rows = { 'A', 'B', 'C', 'D', 'E', 'F', 'G' };
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();

            for (int r = 0; r < rows.Length; r++)
            {
                bool found = false;
                for (int k = 0; k < allKeys.Length; k++)
                {
                    if (allKeys[k].Row == rows[r])
                    {
                        found = true;
                        break;
                    }
                }
                Assert.True(found, $"No keys found for row {rows[r]}");
            }
        }

        [Fact]
        public void NumericPadKeys_HaveCorrectCategory()
        {
            string[] numpadKeys = { "A51", "A53", "B51", "B52", "B53", "C51", "C52", "C53", "C54",
                                    "D51", "D52", "D53", "D54" };
            for (int i = 0; i < numpadKeys.Length; i++)
            {
                Assert.True(TDV2200KeyVisualRegistry.TryGetKey(numpadKeys[i], out var meta),
                    $"Numpad key {numpadKeys[i]} not found");
                Assert.Equal(KeyCategory.NumericPad, meta.Category);
            }
        }

        [Fact]
        public void VirtualKeyCodes_MatchExistingRegistry()
        {
            // Verify VK codes for keys that exist in both registries
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                var meta = allKeys[i];
                if (TDV2200KeyRegistry.TryGetKey(meta.GridPosition, out var regKey))
                {
                    Assert.True(regKey.VirtualKeyCode == meta.VirtualKeyCode,
                        $"VK mismatch for {meta.GridPosition}: existing={regKey.VirtualKeyCode}, visual={meta.VirtualKeyCode}");
                }
            }
        }

        [Fact]
        public void BrownKeys_AreAllInNavigationOrApplicationOrFunction()
        {
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                if (allKeys[i].Color == TDVKeyColor.Brown)
                {
                    bool validCategory = allKeys[i].Category == KeyCategory.Navigation
                        || allKeys[i].Category == KeyCategory.ApplicationControl
                        || allKeys[i].Category == KeyCategory.Function
                        || allKeys[i].Category == KeyCategory.PushKey
                        || allKeys[i].Category == KeyCategory.System
                        || allKeys[i].Category == KeyCategory.Local;
                    Assert.True(validCategory,
                        $"Brown key {allKeys[i].GridPosition} has unexpected category {allKeys[i].Category}");
                }
            }
        }
    }
}
