using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Desktop.Models;
using RetroTerm.Desktop.Rendering;
using RetroTerm.Desktop.ViewModels;

namespace RetroTerm.Desktop.Controls
{
    public partial class VirtualKeyboardPanel : UserControl
    {
        private readonly VirtualKeyboardViewModel _viewModel;
        private readonly Dictionary<string, VisualKey> _visualKeys = new Dictionary<string, VisualKey>();
        private Canvas? _canvas;
        private ComboBox? _layoutComboBox;
        private CheckBox? _showKeyNoCheckBox;
        private Button? _resetBindingsButton;
        private bool _showKeyNo = false;

        // LED indicator ellipses (TDV2200 hardware LEDs)
        private Ellipse? _ledL1, _ledL2, _ledL3, _ledL4;
        private Ellipse? _ledLine, _ledCar, _ledWait, _ledError, _ledOn;

        // Key binding popup
        private Popup? _bindingPopup;
        private Border? _capturePopupBorder;
        private string? _currentEditingTDVKey;
        private bool _capturingKey = false;
        private TextBlock? _captureStatusText;
        private CheckBox? _captureShiftedCheckBox;
        private TDVKeyVisualMetadata? _captureMetadata;
        private Button? _captureAddButton;

        // Mirrored physical key tracking
        private readonly HashSet<string> _mirroredGridPositions = new HashSet<string>();
        private Avalonia.Threading.DispatcherTimer? _mirrorReleaseTimer;

        // Event to send key input to terminal
        public event EventHandler<string>? InputReceived;

        /// <summary>
        /// Raised when the user changes the keyboard layout. Carries the new ISO 646 language code
        /// (e.g. "no", "sv", "de") so listeners can sync the terminal's national variant.
        /// </summary>
        public event EventHandler<string>? LayoutChanged;

        /// <summary>
        /// Current ISO 646 language code from the selected keyboard layout
        /// </summary>
        public string? CurrentLanguageCode => _viewModel?.CurrentLanguageCode;

        /// <summary>
        /// Programmatically sets the keyboard layout dropdown to match the given language name.
        /// Supported values: Norwegian, Danish, Swedish, German, English, Finnish, etc.
        /// </summary>
        public void SetLayout(string languageName)
        {
            if (_layoutComboBox == null || string.IsNullOrEmpty(languageName)) return;

            for (int i = 0; i < _layoutComboBox.ItemCount; i++)
            {
                if (_layoutComboBox.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), languageName, StringComparison.OrdinalIgnoreCase))
                {
                    _layoutComboBox.SelectedIndex = i;
                    return;
                }

                // Also match by Content text (e.g. "US ASCII" matches language "English")
                if (_layoutComboBox.Items[i] is ComboBoxItem contentItem &&
                    string.Equals(contentItem.Content?.ToString(), languageName, StringComparison.OrdinalIgnoreCase))
                {
                    _layoutComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>
        /// Gets the current layout name (Tag value of selected combo item).
        /// </summary>
        public string? CurrentLayoutName
        {
            get
            {
                if (_layoutComboBox?.SelectedItem is ComboBoxItem item)
                    return item.Tag?.ToString();
                return null;
            }
        }

        public VirtualKeyboardPanel()
        {
            InitializeComponent();

            _viewModel = new VirtualKeyboardViewModel();
            DataContext = _viewModel;

            // Make focusable so TextInput events fire for physical keyboard input
            Focusable = true;

            // Subscribe to ViewModel events
            _viewModel.KeyPressed += OnKeyPressed;
            _viewModel.KeyReleased += OnKeyReleased;
            _viewModel.StickyModifiersReleased += OnStickyModifiersReleased;

            // Will be called after InitializeComponent
            this.Loaded += OnLoaded;
        }

        private void InitializeComponent()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            // Get references to named controls
            _canvas = this.FindControl<Canvas>("KeyboardCanvas");
            _layoutComboBox = this.FindControl<ComboBox>("LayoutComboBox");
            _showKeyNoCheckBox = this.FindControl<CheckBox>("ShowKeyNoCheckBox");
            _resetBindingsButton = this.FindControl<Button>("ResetBindingsButton");

            // Get LED indicators (TDV2200 hardware LEDs)
            _ledL1 = this.FindControl<Ellipse>("LedL1");
            _ledL2 = this.FindControl<Ellipse>("LedL2");
            _ledL3 = this.FindControl<Ellipse>("LedL3");
            _ledL4 = this.FindControl<Ellipse>("LedL4");
            _ledLine = this.FindControl<Ellipse>("LedLine");
            _ledCar = this.FindControl<Ellipse>("LedCar");
            _ledWait = this.FindControl<Ellipse>("LedWait");
            _ledError = this.FindControl<Ellipse>("LedError");
            _ledOn = this.FindControl<Ellipse>("LedOn");

            // Set up layout selection
            if (_layoutComboBox != null)
            {
                _layoutComboBox.SelectedIndex = 0; // Default to Norwegian
                _layoutComboBox.SelectionChanged += OnLayoutChanged;
            }

            // Set up Key No checkbox
            if (_showKeyNoCheckBox != null)
            {
                _showKeyNoCheckBox.IsCheckedChanged += OnShowKeyNoChanged;
            }

            // Set up reset button
            if (_resetBindingsButton != null)
            {
                _resetBindingsButton.Click += OnResetBindingsClick;
            }

            // Subscribe to configuration changes
            TDVKeyBindingConfiguration.Instance.ConfigurationChanged += OnBindingConfigChanged;
            TDVPushKeyConfiguration.Instance.ConfigurationChanged += OnPushKeyConfigChanged;
        }

        private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Render the keyboard
            RenderKeyboard();

            // Update LED states
            UpdateLEDIndicators();

            // Focus the panel so physical keyboard TextInput events fire
            Focus();
        }

        /// <summary>
        /// Render all keys on the canvas
        /// </summary>
        private void RenderKeyboard()
        {
            if (_canvas == null || _viewModel == null) return;

            _canvas.Children.Clear();
            _visualKeys.Clear();

            var layout = _viewModel.Layout;
            if (layout == null) return;

            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();

            // Render each key
            for (int i = 0; i < allKeys.Length; i++)
            {
                var meta = allKeys[i];
                Dictionary<string, KeyLabel>? labels = null;
                layout.KeyLabels?.TryGetValue(meta.GridPosition, out labels);

                var visualKey = CreateVisualKey(meta, labels);
                _visualKeys[meta.GridPosition] = visualKey;

                // Add to canvas: Background → Border → InnerShape → TextPanel
                // Toggle LED keys (CAPS, LOCK) have their inner circle already parented by the text panel,
                // so only add standalone inner circles (those without a parent yet).
                _canvas.Children.Add(visualKey.Background);
                _canvas.Children.Add(visualKey.Border);
                if (visualKey.InnerCircle != null && visualKey.InnerCircle.Parent == null)
                    _canvas.Children.Add(visualKey.InnerCircle);
                _canvas.Children.Add(visualKey.TextPanel);
            }
        }

        /// <summary>
        /// Create visual representation of a key
        /// </summary>
        private VisualKey CreateVisualKey(TDVKeyVisualMetadata meta, Dictionary<string, KeyLabel>? labels)
        {
            var visualKey = new VisualKey { Metadata = meta, Labels = labels };

            // Key background (filled rectangle)
            var background = new Rectangle
            {
                Width = meta.Width,
                Height = meta.Height,
                Fill = GetKeyBackgroundBrush(meta),
                RadiusX = 4,
                RadiusY = 4
            };
            Canvas.SetLeft(background, meta.X);
            Canvas.SetTop(background, meta.Y);
            visualKey.Background = background;

            // Key border (outline) — hit test disabled so clicks pass through to background
            var border = new Rectangle
            {
                Width = meta.Width,
                Height = meta.Height,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                RadiusX = 4,
                RadiusY = 4,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(border, meta.X);
            Canvas.SetTop(border, meta.Y);
            visualKey.Border = border;

            // Inner circle/pill shape for all keys
            bool isToggleLEDKey = meta.HasLED;

            // For non-toggle-LED keys, create an inner circle/pill shape
            double innerShapeX = 0, innerShapeY = 0, innerShapeW = 0, innerShapeH = 0;
            if (!isToggleLEDKey)
            {
                var innerShape = CreateInnerShape(meta, out innerShapeX, out innerShapeY,
                    out innerShapeW, out innerShapeH);
                Canvas.SetLeft(innerShape, innerShapeX);
                Canvas.SetTop(innerShape, innerShapeY);
                visualKey.InnerCircle = innerShape;
            }

            // Text panel for labels — special rendering for toggle LED keys (CAPS, LOCK)
            Panel textPanel;
            if (isToggleLEDKey)
            {
                textPanel = CreateToggleLEDKeyTextPanel(meta, labels, visualKey);
            }
            else
            {
                textPanel = CreateKeyTextPanel(meta, labels, innerShapeX - meta.X,
                    innerShapeY - meta.Y, innerShapeW, innerShapeH);
            }
            Canvas.SetLeft(textPanel, meta.X);
            Canvas.SetTop(textPanel, meta.Y);
            visualKey.TextPanel = textPanel;

            // Set tooltip for PUSH keys showing stored string preview
            if (meta.Category == KeyCategory.PushKey)
            {
                var tooltipText = GetPushKeyTooltip(meta.GridPosition);
                if (!string.IsNullOrEmpty(tooltipText))
                {
                    ToolTip.SetTip(background, tooltipText);
                }
            }

            // Set up pointer events — capture metadata via closure
            background.PointerPressed += (s, e) => OnKeyPointerPressed(meta, e);
            background.PointerReleased += (s, e) => OnKeyPointerReleased(meta, e);
            background.PointerEntered += (s, e) => OnKeyPointerEntered(meta, e);
            background.PointerExited += (s, e) => OnKeyPointerExited(meta, e);

            return visualKey;
        }

        /// <summary>
        /// Create text panel with labels for a key.
        /// Inner shape offset/size used to center primary text/symbol within the inner circle/pill.
        /// Rendering is driven by TDVKeyVisualMetadata — no hardcoded grid-position checks.
        /// </summary>
        private Panel CreateKeyTextPanel(TDVKeyVisualMetadata meta, Dictionary<string, KeyLabel>? labels,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH)
        {
            var panel = new Canvas
            {
                Width = meta.Width,
                Height = meta.Height,
                IsHitTestVisible = false
            };

            // Get label for current language
            KeyLabel? label = null;
            var langCode = _viewModel.CurrentLanguageCode;
            if (labels != null)
            {
                if (!labels.TryGetValue(langCode, out label))
                {
                    // Fallback to Norwegian
                    labels.TryGetValue("no", out label);
                }
            }

            if (label == null) return panel;

            var isBrown = meta.Color == TDVKeyColor.Brown;
            var textColor = isBrown ? Brushes.White : Brushes.Black;

            // Determine render mode from metadata
            var renderMode = meta.RenderMode;

            // When "Show Key No" is active, always show grid position as text
            if (_showKeyNo)
            {
                RenderGridPositionOverlay(panel, meta.GridPosition, innerOffsetX, innerOffsetY, innerW, innerH);
            }
            else
            {
                // Dispatch rendering based on metadata render mode
                switch (renderMode)
                {
                    case KeyRenderMode.Symbol:
                        RenderSymbolKey(panel, meta, innerOffsetX, innerOffsetY, innerW, innerH);
                        break;
                    case KeyRenderMode.TextWithOverlay:
                        RenderTextWithOverlayKey(panel, meta, textColor, innerOffsetX, innerOffsetY, innerW, innerH,
                            GetPrimaryFontSizeForMeta(meta, labels));
                        break;
                    case KeyRenderMode.TwoLineText:
                        RenderTwoLineTextKey(panel, meta, textColor, innerOffsetX, innerOffsetY, innerW, innerH);
                        break;
                    case KeyRenderMode.ThreeLineText:
                        RenderThreeLineTextKey(panel, meta, textColor, innerOffsetX, innerOffsetY, innerW, innerH);
                        break;
                    case KeyRenderMode.VerticalLetterStack:
                        RenderVerticalLetterStackKey(panel, label, textColor, innerOffsetX, innerOffsetY, innerW, innerH,
                            GetPrimaryFontSizeForMeta(meta, labels));
                        break;
                    case KeyRenderMode.Text:
                    default:
                        RenderStandardTextKey(panel, meta, label, textColor, innerOffsetX, innerOffsetY, innerW, innerH);
                        break;
                }
            }

            // Alternative label (bottom of key body, smaller)
            if (!string.IsNullOrEmpty(label.Alternative))
            {
                var altText = new TextBlock
                {
                    Text = label.Alternative,
                    FontSize = 8,
                    FontWeight = FontWeight.Normal,
                    Foreground = isBrown ? new SolidColorBrush(Color.FromRgb(200, 200, 180)) : Brushes.DarkSlateGray,
                    TextAlignment = TextAlignment.Center,
                    Width = meta.Width
                };
                Canvas.SetLeft(altText, 0);
                Canvas.SetBottom(altText, 2);
                panel.Children.Add(altText);
            }

            // Indicator (top left corner) — always show key bindings for bindable keys
            string? indicatorToShow = null;
            IBrush indicatorBrush = Brushes.Gray;
            bool isBindingIndicator = false;

            if (meta.IsBindable)
            {
                var bindingLabel = TDVKeyBindingConfiguration.Instance.GetBindingLabelForGrid(meta.GridPosition);
                if (!string.IsNullOrEmpty(bindingLabel))
                {
                    indicatorToShow = bindingLabel;
                    indicatorBrush = new SolidColorBrush(Color.FromRgb(0, 100, 200)); // Blue for bindings
                    isBindingIndicator = true;
                }
            }

            // Fall back to normal indicator if no binding indicator
            if (!isBindingIndicator && !string.IsNullOrEmpty(label.Indicator))
            {
                indicatorToShow = label.Indicator;
            }

            if (!string.IsNullOrEmpty(indicatorToShow))
            {
                var indicatorText = new TextBlock
                {
                    Text = indicatorToShow,
                    FontSize = isBindingIndicator ? 9 : 7,
                    FontWeight = isBindingIndicator ? FontWeight.Bold : FontWeight.Normal,
                    Foreground = indicatorBrush,
                    TextAlignment = TextAlignment.Left
                };
                Canvas.SetLeft(indicatorText, 2);
                Canvas.SetTop(indicatorText, 2);
                panel.Children.Add(indicatorText);
            }

            // PUSH key programmed indicator (green * in top-left)
            if (meta.Category == KeyCategory.PushKey)
            {
                var pushConfig = TDVPushKeyConfiguration.Instance;
                bool unshifted = pushConfig.GetKeyString(meta.GridPosition, false) != null;
                bool shifted = pushConfig.GetKeyString(meta.GridPosition, true) != null;

                if (unshifted || shifted)
                {
                    var pushIndicator = new TextBlock
                    {
                        Text = (unshifted && shifted) ? "**" : "*",
                        FontSize = 9,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 0)),
                        TextAlignment = TextAlignment.Left
                    };
                    Canvas.SetLeft(pushIndicator, 2);
                    Canvas.SetTop(pushIndicator, 2);
                    panel.Children.Add(pushIndicator);
                }
            }

            return panel;
        }

        // --- Metadata-driven rendering methods ---

        /// <summary>
        /// Render grid position text (debug overlay when "Show Key No" is active).
        /// </summary>
        private static void RenderGridPositionOverlay(Canvas panel, string gridPosition,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH)
        {
            var gridText = new TextBlock
            {
                Text = gridPosition,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 139)),
                TextAlignment = TextAlignment.Center,
                Width = innerW
            };
            Canvas.SetLeft(gridText, innerOffsetX);
            Canvas.SetTop(gridText, innerOffsetY + innerH / 2 - 7);
            panel.Children.Add(gridText);
        }

        /// <summary>
        /// Render an SVG symbol key. Size factor and fill/stroke come from metadata.
        /// </summary>
        private static void RenderSymbolKey(Canvas panel, TDVKeyVisualMetadata meta,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH)
        {
            if (meta == null || meta.SymbolId == null) return;
            if (!KeySymbolPaths.TryGetSymbol(meta.GridPosition, out var symbolGeometry)) return;

            double symbolW, symbolH;
            double factor = meta.SymbolSizeFactor;

            // For tall/wide keys, apply factor to both dimensions independently
            if (meta.Height > meta.Width * 1.2 || meta.Width > meta.Height * 1.2)
            {
                symbolW = innerW * factor;
                symbolH = innerH * factor;
            }
            else
            {
                double sz = Math.Min(innerW, innerH) * factor;
                symbolW = sz;
                symbolH = sz;
            }

            var symbolPath = new Avalonia.Controls.Shapes.Path
            {
                Data = symbolGeometry,
                Stretch = Stretch.Uniform,
                Width = symbolW,
                Height = symbolH,
                Stroke = Brushes.Black,
                StrokeThickness = 1.5,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Fill = meta.SymbolFilled ? Brushes.Black : null,
                IsHitTestVisible = false
            };

            // Use geometry bounds for precise centering
            var geoBounds = symbolGeometry.Bounds;
            if (geoBounds.Width > 0 && geoBounds.Height > 0)
            {
                double scale = Math.Min(symbolW / geoBounds.Width, symbolH / geoBounds.Height);
                double renderedW = geoBounds.Width * scale;
                double renderedH = geoBounds.Height * scale;
                Canvas.SetLeft(symbolPath, innerOffsetX + (innerW - renderedW) / 2);
                Canvas.SetTop(symbolPath, innerOffsetY + (innerH - renderedH) / 2);
            }
            else
            {
                Canvas.SetLeft(symbolPath, innerOffsetX + (innerW - symbolW) / 2);
                Canvas.SetTop(symbolPath, innerOffsetY + (innerH - symbolH) / 2);
            }
            panel.Children.Add(symbolPath);
        }

        /// <summary>
        /// Render text with overlay (e.g. E14 DEL: lowercase "a" with diagonal slash).
        /// OverlayText and SymbolId come from metadata.
        /// </summary>
        private static void RenderTextWithOverlayKey(Canvas panel, TDVKeyVisualMetadata meta, IBrush textColor,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH, double baseFontSize)
        {
            if (meta == null) return;

            string overlayChar = meta.OverlayText ?? "a";
            double aFontSize = baseFontSize * 1.95;

            var aText = new TextBlock
            {
                Text = overlayChar,
                FontSize = aFontSize,
                FontWeight = FontWeight.Normal,
                Foreground = textColor,
                TextAlignment = TextAlignment.Center,
                Width = innerW
            };
            double aVerticalNudge = aFontSize * 0.15;
            Canvas.SetLeft(aText, innerOffsetX);
            Canvas.SetTop(aText, innerOffsetY + innerH / 2 - aFontSize / 2 - aVerticalNudge);
            panel.Children.Add(aText);

            // Diagonal slash overlay
            double aCenterX = innerOffsetX + innerW / 2;
            double aCenterY = innerOffsetY + innerH / 2 - aVerticalNudge + 2;
            double aHalfW = aFontSize * 0.25;
            double aHalfH = aFontSize * 0.315;
            var slashLine = new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(aCenterX - aHalfW + 3, aCenterY + aHalfH + 3),
                EndPoint = new Point(aCenterX + aHalfW - 3, aCenterY - aHalfH + 3),
                Stroke = textColor,
                StrokeThickness = 1.7,
                StrokeLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            panel.Children.Add(slashLine);
        }

        /// <summary>
        /// Render two-line text key. Lines come from metadata.MultiLineTexts.
        /// </summary>
        private static void RenderTwoLineTextKey(Canvas panel, TDVKeyVisualMetadata meta, IBrush textColor,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH)
        {
            if (meta == null || meta.MultiLineTexts == null || meta.MultiLineTexts.Length < 2) return;

            double fontSize = 14;
            double centerY = innerOffsetY + innerH / 2;
            double gap = 1;
            var fontWeight = meta.MultiLineBold ? FontWeight.Bold : FontWeight.Normal;

            var topText = new TextBlock
            {
                Text = meta.MultiLineTexts[0],
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = textColor,
                TextAlignment = TextAlignment.Center,
                Width = innerW,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(topText, innerOffsetX);
            Canvas.SetTop(topText, centerY - fontSize - gap / 2);
            panel.Children.Add(topText);

            var botText = new TextBlock
            {
                Text = meta.MultiLineTexts[1],
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = textColor,
                TextAlignment = TextAlignment.Center,
                Width = innerW,
                IsHitTestVisible = false
            };
            if (meta.SecondLineUnderlined)
            {
                botText.TextDecorations = TextDecorations.Underline;
            }
            Canvas.SetLeft(botText, innerOffsetX);
            Canvas.SetTop(botText, centerY + gap / 2);
            panel.Children.Add(botText);
        }

        /// <summary>
        /// Render three-line text key. Lines come from metadata.MultiLineTexts.
        /// Middle line is larger, top/bottom are smaller.
        /// </summary>
        private static void RenderThreeLineTextKey(Canvas panel, TDVKeyVisualMetadata meta, IBrush textColor,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH)
        {
            if (meta == null || meta.MultiLineTexts == null || meta.MultiLineTexts.Length < 3) return;

            double fontSize = 14;
            double smallFontSize = 12;
            double centerY = innerOffsetY + innerH / 2;
            double gap = 1;

            // Middle line (largest)
            var midText = new TextBlock
            {
                Text = meta.MultiLineTexts[1],
                FontSize = fontSize,
                Foreground = textColor,
                TextAlignment = TextAlignment.Center,
                Width = innerW,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(midText, innerOffsetX);
            Canvas.SetTop(midText, centerY - fontSize / 2);
            panel.Children.Add(midText);

            // Top line (smaller)
            var topText = new TextBlock
            {
                Text = meta.MultiLineTexts[0],
                FontSize = smallFontSize,
                Foreground = textColor,
                TextAlignment = TextAlignment.Center,
                Width = innerW,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(topText, innerOffsetX);
            Canvas.SetTop(topText, centerY - fontSize / 2 - smallFontSize - gap);
            panel.Children.Add(topText);

            // Bottom line (smaller)
            var botText = new TextBlock
            {
                Text = meta.MultiLineTexts[2],
                FontSize = smallFontSize,
                Foreground = textColor,
                TextAlignment = TextAlignment.Center,
                Width = innerW,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(botText, innerOffsetX);
            Canvas.SetTop(botText, centerY + fontSize / 2 + gap);
            panel.Children.Add(botText);
        }

        /// <summary>
        /// Render vertical letter stack (e.g. B54 ENTER: E-N-T-E-R stacked vertically).
        /// </summary>
        private static void RenderVerticalLetterStackKey(Canvas panel, KeyLabel label, IBrush textColor,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH, double baseFontSize)
        {
            if (label == null || string.IsNullOrEmpty(label.Primary) || label.Primary.Length <= 1) return;

            var sb = new System.Text.StringBuilder(label.Primary.Length * 2);
            for (int ci = 0; ci < label.Primary.Length; ci++)
            {
                if (ci > 0) sb.Append('\n');
                sb.Append(label.Primary[ci]);
            }
            double stackFontSize = baseFontSize * 0.75;
            var primaryText = new TextBlock
            {
                Text = sb.ToString(),
                FontSize = stackFontSize,
                FontWeight = FontWeight.Normal,
                Foreground = textColor,
                TextAlignment = TextAlignment.Center,
                LineHeight = stackFontSize * 1.15,
                Width = innerW
            };
            double totalTextH = label.Primary.Length * stackFontSize * 1.15;
            Canvas.SetLeft(primaryText, innerOffsetX);
            Canvas.SetTop(primaryText, innerOffsetY + (innerH - totalTextH) / 2);
            panel.Children.Add(primaryText);
        }

        /// <summary>
        /// Render standard text key (single label, or shifted/primary pair).
        /// </summary>
        private static void RenderStandardTextKey(Canvas panel, TDVKeyVisualMetadata meta, KeyLabel label, IBrush textColor,
            double innerOffsetX, double innerOffsetY, double innerW, double innerH)
        {
            if (string.IsNullOrEmpty(label.Primary)) return;

            double fontSize = GetPrimaryFontSizeStatic(meta, label);

            bool hasDifferentShifted = !string.IsNullOrEmpty(label.Shifted)
                && !string.Equals(label.Primary, label.Shifted, StringComparison.OrdinalIgnoreCase);

            if (hasDifferentShifted)
            {
                // Two-symbol key: shifted above, primary below, both inside circle
                var shiftedText = new TextBlock
                {
                    Text = label.Shifted,
                    FontSize = fontSize * 0.85,
                    FontWeight = FontWeight.Normal,
                    Foreground = textColor,
                    TextAlignment = TextAlignment.Center,
                    Width = innerW
                };
                Canvas.SetLeft(shiftedText, innerOffsetX);
                Canvas.SetTop(shiftedText, innerOffsetY + innerH * 0.15);
                panel.Children.Add(shiftedText);

                var primaryText = new TextBlock
                {
                    Text = label.Primary,
                    FontSize = fontSize * 0.85,
                    FontWeight = FontWeight.Normal,
                    Foreground = textColor,
                    TextAlignment = TextAlignment.Center,
                    Width = innerW
                };
                Canvas.SetLeft(primaryText, innerOffsetX);
                Canvas.SetTop(primaryText, innerOffsetY + innerH * 0.52);
                panel.Children.Add(primaryText);
            }
            else
            {
                // Single-symbol key: centered in circle
                var primaryText = new TextBlock
                {
                    Text = label.Primary,
                    FontSize = fontSize,
                    FontWeight = FontWeight.Normal,
                    Foreground = textColor,
                    TextAlignment = TextAlignment.Center,
                    Width = innerW
                };
                Canvas.SetLeft(primaryText, innerOffsetX);
                Canvas.SetTop(primaryText, innerOffsetY + innerH / 2 - fontSize / 2);
                panel.Children.Add(primaryText);
            }
        }

        /// <summary>
        /// Get font size for standard text keys (static version for use from rendering methods).
        /// </summary>
        private static double GetPrimaryFontSizeStatic(TDVKeyVisualMetadata meta, KeyLabel label)
        {
            if (label == null || string.IsNullOrEmpty(label.Primary))
                return 14;

            var textLength = label.Primary.Length;

            if (meta.Category == KeyCategory.Alphanumeric)
                return 16;

            if (textLength > 8) return 10;
            if (textLength > 5) return 12;
            return 14;
        }

        // Pre-built brushes for toggle LED keys
        private static readonly IBrush ToggleLEDOnBrush = new SolidColorBrush(Color.FromRgb(0, 220, 0));
        private static readonly IBrush ToggleLEDOffBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));

        // Inner circle/pill brush sets per key color family
        private readonly struct InnerCircleBrushSet
        {
            public readonly IBrush Normal;
            public readonly IBrush Pressed;
            public readonly IBrush Border;

            public InnerCircleBrushSet(IBrush normal, IBrush pressed, IBrush border)
            {
                Normal = normal;
                Pressed = pressed;
                Border = border;
            }
        }

        private static readonly InnerCircleBrushSet WhiteCircleBrushes = new InnerCircleBrushSet(
            new SolidColorBrush(Color.FromRgb(245, 245, 240)),
            new SolidColorBrush(Color.FromRgb(200, 200, 195)),
            new SolidColorBrush(Color.FromRgb(195, 195, 190)));

        private static readonly InnerCircleBrushSet OrangeCircleBrushes = new InnerCircleBrushSet(
            new SolidColorBrush(Color.FromRgb(230, 195, 120)),
            new SolidColorBrush(Color.FromRgb(190, 155, 80)),
            new SolidColorBrush(Color.FromRgb(180, 140, 60)));

        private static readonly InnerCircleBrushSet BrownCircleBrushes = new InnerCircleBrushSet(
            new SolidColorBrush(Color.FromRgb(170, 130, 85)),
            new SolidColorBrush(Color.FromRgb(130, 90, 50)),
            new SolidColorBrush(Color.FromRgb(120, 80, 45)));

        /// <summary>
        /// Get the inner circle brush set for a key based on its metadata color.
        /// </summary>
        private static InnerCircleBrushSet GetInnerCircleBrushSet(TDVKeyVisualMetadata meta)
        {
            if (meta != null)
            {
                switch (meta.Color)
                {
                    case TDVKeyColor.Brown: return BrownCircleBrushes;
                    case TDVKeyColor.Orange: return OrangeCircleBrushes;
                }
            }
            return WhiteCircleBrushes;
        }

        /// <summary>
        /// Create the inner circle/pill shape for a key based on its dimensions.
        /// Circle for square keys, horizontal pill for wide keys, vertical pill for tall keys.
        /// </summary>
        private static Shape CreateInnerShape(TDVKeyVisualMetadata meta, out double shapeX, out double shapeY,
            out double shapeW, out double shapeH)
        {
            double keyW = meta.Width;
            double keyH = meta.Height;
            double ratio = keyW / keyH;

            var brushSet = GetInnerCircleBrushSet(meta);

            Shape shape;
            if (ratio > 1.2)
            {
                // Wide key → horizontal pill (Rectangle with full corner radius)
                shapeW = keyW * 0.90;
                shapeH = keyH * 0.75;
                double cornerRadius = shapeH / 2;
                shape = new Rectangle
                {
                    Width = shapeW,
                    Height = shapeH,
                    RadiusX = cornerRadius,
                    RadiusY = cornerRadius,
                    Fill = brushSet.Normal,
                    Stroke = brushSet.Border,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
            }
            else if (ratio < 0.8)
            {
                // Tall key → vertical pill
                shapeW = keyW * 0.75;
                shapeH = keyH * 0.90;
                double cornerRadius = shapeW / 2;
                shape = new Rectangle
                {
                    Width = shapeW,
                    Height = shapeH,
                    RadiusX = cornerRadius,
                    RadiusY = cornerRadius,
                    Fill = brushSet.Normal,
                    Stroke = brushSet.Border,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
            }
            else
            {
                // Square-ish key → circle
                double diameter = Math.Min(keyW, keyH) * 0.85;
                shapeW = diameter;
                shapeH = diameter;
                shape = new Ellipse
                {
                    Width = diameter,
                    Height = diameter,
                    Fill = brushSet.Normal,
                    Stroke = brushSet.Border,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
            }

            // Center within key bounds
            shapeX = meta.X + (keyW - shapeW) / 2;
            shapeY = meta.Y + (keyH - shapeH) / 2;

            return shape;
        }

        /// <summary>
        /// Create special text panel for toggle LED keys (CAPS E0, LOCK C0).
        /// Renders: outer frame (handled by Background), inner circular key top,
        /// LED indicator dot to the left of the circle, text centered in circle.
        /// </summary>
        private Panel CreateToggleLEDKeyTextPanel(TDVKeyVisualMetadata meta, Dictionary<string, KeyLabel>? labels, VisualKey visualKey)
        {
            var panel = new Canvas
            {
                Width = meta.Width,
                Height = meta.Height,
                IsHitTestVisible = false
            };

            double keyW = meta.Width;
            double keyH = meta.Height;

            // Inner circle dimensions — ~63% of key height
            double circleDiameter = keyH * 0.63;
            // LED dimensions — ~13% of key height
            double ledDiameter = keyH * 0.13;

            // Layout: LED on left, circle centered-right (proportional spacing)
            double ledX = keyW * 0.08;
            double ledY = (keyH - ledDiameter) / 2;
            double gapAfterLed = keyW * 0.08;
            double circleX = ledX + ledDiameter + gapAfterLed;
            double circleY = (keyH - circleDiameter) / 2;

            // Inner circular key top
            var innerCircle = new Ellipse
            {
                Width = circleDiameter,
                Height = circleDiameter,
                Fill = WhiteCircleBrushes.Normal,
                Stroke = WhiteCircleBrushes.Border,
                StrokeThickness = 1
            };
            Canvas.SetLeft(innerCircle, circleX);
            Canvas.SetTop(innerCircle, circleY);
            panel.Children.Add(innerCircle);
            visualKey.InnerCircle = innerCircle;

            // LED indicator dot
            bool isActive = _viewModel.IsModifierActive(meta.GridPosition);
            var ledDot = new Ellipse
            {
                Width = ledDiameter,
                Height = ledDiameter,
                Fill = isActive ? ToggleLEDOnBrush : ToggleLEDOffBrush,
                Stroke = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                StrokeThickness = 0.5
            };
            Canvas.SetLeft(ledDot, ledX);
            Canvas.SetTop(ledDot, ledY);
            panel.Children.Add(ledDot);
            visualKey.ToggleLED = ledDot;

            // Text label centered on the inner circle
            KeyLabel? label = null;
            var langCode = _viewModel.CurrentLanguageCode;
            if (labels != null)
            {
                if (!labels.TryGetValue(langCode, out label))
                    labels.TryGetValue("no", out label);
            }

            string labelText = label?.Primary ?? meta.GridPosition;

            // When "Show Key No" is active, show grid position instead
            if (_showKeyNo)
                labelText = meta.GridPosition;

            var textBlock = new TextBlock
            {
                Text = labelText,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                Width = circleDiameter,
                Height = circleDiameter,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Canvas.SetLeft(textBlock, circleX);
            Canvas.SetTop(textBlock, circleY + circleDiameter / 2 - 7);
            panel.Children.Add(textBlock);

            return panel;
        }

        /// <summary>
        /// Get appropriate font size for primary label based on key category and text length
        /// </summary>
        private double GetPrimaryFontSizeForMeta(TDVKeyVisualMetadata meta, Dictionary<string, KeyLabel>? labels)
        {
            KeyLabel? label = null;
            var langCode = _viewModel.CurrentLanguageCode;
            if (labels != null)
            {
                if (!labels.TryGetValue(langCode, out label))
                    labels.TryGetValue("no", out label);
            }

            if (label == null || string.IsNullOrEmpty(label.Primary))
                return 14;

            var textLength = label.Primary.Length;

            // Adjust based on key category and text length
            if (meta.Category == KeyCategory.Alphanumeric)
                return 16; // Standard size for alphanumeric

            if (textLength > 8)
                return 10; // Small for long labels
            else if (textLength > 5)
                return 12; // Medium for medium labels
            else
                return 14; // Normal for short labels
        }

        // Pre-built brushes for key colors (avoid per-key allocations)
        private static readonly IBrush WhiteBrush = new SolidColorBrush(Color.FromRgb(235, 235, 225));
        private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.FromRgb(210, 160, 70));
        private static readonly IBrush BrownBrush = new SolidColorBrush(Color.FromRgb(140, 95, 55));

        // Cached keyboard mapper for VT220-compatible sequences (avoids per-press allocation)
        private static readonly RetroTerm.Core.Terminal.Input.TDV2200KeyboardMapper _cachedMapper =
            new RetroTerm.Core.Terminal.Input.TDV2200KeyboardMapper();

        /// <summary>
        /// Get background brush for a key from its visual metadata color.
        /// </summary>
        private static IBrush GetKeyBackgroundBrush(TDVKeyVisualMetadata meta)
        {
            if (meta == null) return WhiteBrush;

            switch (meta.Color)
            {
                case TDVKeyColor.Brown: return BrownBrush;
                case TDVKeyColor.Orange: return OrangeBrush;
                default: return WhiteBrush;
            }
        }

        /// <summary>
        /// Get tooltip text for a PUSH key showing stored string preview.
        /// Returns null if no strings are programmed.
        /// </summary>
        private static string? GetPushKeyTooltip(string gridPosition)
        {
            var config = TDVPushKeyConfiguration.Instance;
            var unshifted = config.GetKeyString(gridPosition, false);
            var shifted = config.GetKeyString(gridPosition, true);

            if (unshifted == null && shifted == null)
                return null;

            var sb = new System.Text.StringBuilder();

            if (unshifted != null)
            {
                var formatted = EscapeSequenceFormatter.Format(unshifted);
                if (formatted.Length > 20)
                    formatted = formatted.Substring(0, 20) + "...";
                sb.Append("Normal: ");
                sb.Append(formatted);
            }

            if (shifted != null)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                var formatted = EscapeSequenceFormatter.Format(shifted);
                if (formatted.Length > 20)
                    formatted = formatted.Substring(0, 20) + "...";
                sb.Append("Shifted: ");
                sb.Append(formatted);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Check if a grid position is a brown key (for text color decisions)
        /// </summary>
        private static bool IsBrownKey(string gp)
        {
            if (TDV2200KeyVisualRegistry.TryGetKey(gp, out var meta))
                return meta.Color == TDVKeyColor.Brown;
            return false;
        }

        /// <summary>
        /// Update key visual state (pressed, hover, etc.)
        /// </summary>
        private void UpdateKeyVisual(string gridPosition, KeyVisualState state)
        {
            if (!_visualKeys.TryGetValue(gridPosition, out var visualKey))
                return;

            var meta = visualKey.Metadata;

            // All keys with inner circle/pill: darken the inner shape, not the outer frame
            if (visualKey.InnerCircle != null)
            {
                var brushSet = GetInnerCircleBrushSet(meta);
                switch (state)
                {
                    case KeyVisualState.Normal:
                        visualKey.InnerCircle.Fill = brushSet.Normal;
                        visualKey.Border.Stroke = Brushes.Gray;
                        visualKey.Border.StrokeThickness = 1;
                        break;
                    case KeyVisualState.Hover:
                        visualKey.Border.Stroke = Brushes.Blue;
                        visualKey.Border.StrokeThickness = 2;
                        break;
                    case KeyVisualState.Pressed:
                        visualKey.InnerCircle.Fill = brushSet.Pressed;
                        visualKey.Border.Stroke = Brushes.DarkBlue;
                        visualKey.Border.StrokeThickness = 2;
                        break;
                }
                return;
            }

            // Fallback for keys without inner circle (should not happen, but safety)
            var background = visualKey.Background as Rectangle;
            if (background == null) return;

            switch (state)
            {
                case KeyVisualState.Normal:
                    background.Fill = GetKeyBackgroundBrush(meta);
                    visualKey.Border.Stroke = Brushes.Gray;
                    visualKey.Border.StrokeThickness = 1;
                    break;

                case KeyVisualState.Hover:
                    background.Fill = GetKeyBackgroundBrush(meta);
                    visualKey.Border.Stroke = Brushes.Blue;
                    visualKey.Border.StrokeThickness = 2;
                    break;

                case KeyVisualState.Pressed:
                    var originalBrush = GetKeyBackgroundBrush(meta);
                    if (originalBrush is SolidColorBrush solidBrush)
                    {
                        var color = solidBrush.Color;
                        var darkerColor = Color.FromRgb(
                            (byte)(color.R * 0.7),
                            (byte)(color.G * 0.7),
                            (byte)(color.B * 0.7)
                        );
                        background.Fill = new SolidColorBrush(darkerColor);
                    }
                    visualKey.Border.Stroke = Brushes.DarkBlue;
                    visualKey.Border.StrokeThickness = 2;
                    break;
            }
        }

        /// <summary>
        /// Event raised when a PUSH key is right-clicked (for programming dialog).
        /// Payload is the grid number (1-8).
        /// </summary>
        public event EventHandler<int>? PushKeyRightClicked;

        /// <summary>
        /// Handle key pointer pressed
        /// </summary>
        private void OnKeyPointerPressed(TDVKeyVisualMetadata meta, PointerPressedEventArgs e)
        {
            var pointerProps = e.GetCurrentPoint(null).Properties;

            // Right-click on PUSH key → raise event for programming dialog
            if (meta.Category == KeyCategory.PushKey && pointerProps.IsRightButtonPressed)
            {
                // Extract grid number from "G1"-"G8"
                var gp = meta.GridPosition;
                if (gp.Length >= 2 && gp[0] == 'G'
                    && int.TryParse(gp.Substring(1), out int gridNum)
                    && gridNum >= 1 && gridNum <= 8)
                {
                    PushKeyRightClicked?.Invoke(this, gridNum);
                    e.Handled = true;
                    return;
                }
            }

            // Right-click on any bindable key → open binding popup
            if (pointerProps.IsRightButtonPressed
                && meta.IsBindable
                && meta.Category != KeyCategory.Modifier
                && meta.Category != KeyCategory.Toggle)
            {
                ShowBindingPopup(meta);
                e.Handled = true;
                return;
            }

            // Set physical modifiers from the pointer event BEFORE handling the key
            _viewModel.PhysicalModifiers = ConvertPointerModifiers(e.KeyModifiers);

            try
            {
                _viewModel.HandleKeyDown(meta);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VirtualKeyboard HandleKeyDown error for {meta.GridPosition}: {ex}");
            }

            // Clear physical modifiers after processing (they are per-click, not persistent)
            _viewModel.PhysicalModifiers = RetroTerm.Core.Terminal.Input.KeyModifiers.None;

            // For sticky modifiers: ViewModel toggles state — reflect that in the visual
            if (meta.Category == KeyCategory.Modifier)
            {
                var isSticky = _viewModel.IsStickyModifier(meta.GridPosition);
                UpdateKeyVisual(meta.GridPosition, isSticky ? KeyVisualState.Pressed : KeyVisualState.Normal);
            }
            else if (meta.Category == KeyCategory.Toggle)
            {
                // Toggle keys (CAPS, LOCK): update LED and visual based on toggle state
                bool active = _viewModel.IsModifierActive(meta.GridPosition);
                UpdateToggleKeyLED(meta.GridPosition, active);
                UpdateKeyVisual(meta.GridPosition, active ? KeyVisualState.Pressed : KeyVisualState.Normal);
            }
            else
            {
                UpdateKeyVisual(meta.GridPosition, KeyVisualState.Pressed);
            }

            // Re-focus panel so physical keyboard TextInput continues to work
            Focus();
            e.Handled = true;
        }

        /// <summary>
        /// Handle key pointer released
        /// </summary>
        private void OnKeyPointerReleased(TDVKeyVisualMetadata meta, PointerReleasedEventArgs e)
        {
            // Sticky modifiers stay visually pressed — don't reset on mouse-up
            if (meta.Category == KeyCategory.Modifier && _viewModel.IsStickyModifier(meta.GridPosition))
            {
                e.Handled = true;
                return;
            }

            // Toggle keys (CAPS, LOCK) manage their own visual state — don't reset on mouse-up
            if (meta.Category == KeyCategory.Toggle)
            {
                e.Handled = true;
                return;
            }

            _viewModel.HandleKeyUp(meta);
            UpdateKeyVisual(meta.GridPosition, KeyVisualState.Normal);
            e.Handled = true;
        }

        /// <summary>
        /// Handle key pointer entered (hover)
        /// </summary>
        private void OnKeyPointerEntered(TDVKeyVisualMetadata meta, PointerEventArgs e)
        {
            if (!_viewModel.IsKeyPressed(meta.GridPosition))
            {
                UpdateKeyVisual(meta.GridPosition, KeyVisualState.Hover);
            }
        }

        /// <summary>
        /// Handle key pointer exited
        /// </summary>
        private void OnKeyPointerExited(TDVKeyVisualMetadata meta, PointerEventArgs e)
        {
            if (!_viewModel.IsKeyPressed(meta.GridPosition))
            {
                UpdateKeyVisual(meta.GridPosition, KeyVisualState.Normal);
            }
        }

        /// <summary>
        /// Handle key pressed event from ViewModel
        /// </summary>
        private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
        {
            var meta = e.Key;
            var keyCode = meta.VirtualKeyCode;
            var modifiers = e.Modifiers;
            string? sequence = null;

            bool shift = modifiers.HasFlag(RetroTerm.Core.Terminal.Input.KeyModifiers.Shift);
            bool ctrl = modifiers.HasFlag(RetroTerm.Core.Terminal.Input.KeyModifiers.Ctrl);

            // LOCK (C0): disables grey area keys (orange/brown) UNLESS they have AlwaysSameCode.
            // Keys with AlwaysSameCode (arrows, DEL, ESC, LF, CR, HOME, KPENTER) are unaffected.
            // Normal character keys, NumericPad, and PushKeys are never affected by LOCK.
            if (_viewModel.IsLockActive
                && meta.Category != KeyCategory.Alphanumeric
                && meta.Category != KeyCategory.NumericPad
                && meta.Category != KeyCategory.PushKey)
            {
                if (TDV2200KeyRegistry.TryGetKey(meta.GridPosition, out var regEntry))
                {
                    if ((regEntry.Flags & TDVKeyFlags.AlwaysSameCode) != 0)
                    {
                        // LOCK ignored — key works normally (arrows, DEL, ESC, LF, CR, etc.)
                    }
                    else if (regEntry.Color == TDVKeyColor.Orange || regEntry.Color == TDVKeyColor.Brown)
                    {
                        return; // Grey area key DISABLED — no output
                    }
                }
            }

            // Handle PUSH keys (P1-P16): send stored programmed string
            if (meta.Category == KeyCategory.PushKey)
            {
                var pushString = RetroTerm.Core.Terminal.Input.TDVPushKeyConfiguration.Instance
                    .GetKeyString(meta.GridPosition, shift);
                if (!string.IsNullOrEmpty(pushString))
                {
                    InputReceived?.Invoke(this, pushString);
                }
                return;
            }

            // Ctrl + key with metadata-defined CtrlSequence → send it directly
            if (ctrl && meta.CtrlSequence != null)
            {
                sequence = meta.CtrlSequence;
            }

            // For TDV-specific keys (ApplicationControl/Function/Local — HJELP, SLUTT, FUNK, etc.),
            // use the TDV2200KeyRegistry FIRST — these have native CSI nn _ sequences
            // that must not be overridden by the VK-based VT220 mapper.
            if (string.IsNullOrEmpty(sequence) && (meta.Category == KeyCategory.ApplicationControl
                || meta.Category == KeyCategory.Function
                || meta.Category == KeyCategory.Local))
            {
                sequence = TDV2200KeyRegistry.GetSequence(
                    meta.GridPosition, true, false, shift, ctrl);
            }

            // For navigation/system keys with a VK code, use the cached VT220-compatible mapper.
            // This sends ESC sequences for arrows/navigation (ESC[D, ESC[A, etc.)
            // instead of ND-246 native C0 codes (0x08, 0x1C, etc.) that hosts
            // would misinterpret.
            //
            // Ctrl on an ALPHANUMERIC key is deliberately left to the label-based resolution
            // further down. On an on-screen keyboard the label is what the user actually clicked,
            // and it is what changes with the national layout while the VK code on the layout
            // entry stays put — so the label decides which control code Ctrl+<letter> produces.
            // The mapper's own Ctrl+letter rule serves the physical-keyboard paths, where there
            // is no label to consult.
            bool ctrlOnLetterKey = ctrl && meta.Category == KeyCategory.Alphanumeric;

            if (string.IsNullOrEmpty(sequence) && keyCode > 0 && !ctrlOnLetterKey)
            {
                sequence = _cachedMapper.MapKey(keyCode, modifiers,
                    RetroTerm.Core.Terminal.Input.TerminalModes.None);
            }

            // Fallback: for any remaining keys with a grid position, try the registry.
            if (string.IsNullOrEmpty(sequence))
            {
                sequence = TDV2200KeyRegistry.GetSequence(
                    meta.GridPosition, true, false, shift, ctrl);
            }

            // Handle Space (non-Ctrl — Ctrl+Space already handled by CtrlSequence above)
            if (string.IsNullOrEmpty(sequence) && keyCode == 32)
            {
                sequence = " ";
            }

            // Handle numeric pad keys — send digit characters (no NumLock detection on virtual keyboard)
            if (string.IsNullOrEmpty(sequence) && meta.Category == KeyCategory.NumericPad)
            {
                if (keyCode >= 96 && keyCode <= 105) // VK_NUMPAD0-9 → '0'-'9'
                    sequence = ((char)('0' + keyCode - 96)).ToString();
                else if (keyCode == 110) // VK_DECIMAL → '.'
                    sequence = ".";
                else if (keyCode == 109) // VK_SUBTRACT → '-'
                    sequence = "-";
            }

            // Handle normal character keys (letters, numbers, symbols)
            if (string.IsNullOrEmpty(sequence) && meta.Category == KeyCategory.Alphanumeric)
            {
                var langCode = _viewModel.CurrentLanguageCode;
                Dictionary<string, KeyLabel>? labels = null;
                _viewModel.Layout?.KeyLabels?.TryGetValue(meta.GridPosition, out labels);

                KeyLabel? label = null;
                if (labels != null)
                {
                    if (!labels.TryGetValue(langCode, out label))
                        labels.TryGetValue("no", out label);
                }

                if (label != null)
                {
                    var charLabel = shift && !string.IsNullOrEmpty(label.Shifted)
                        ? label.Shifted : label.Primary;
                    if (!string.IsNullOrEmpty(charLabel) && charLabel.Length == 1)
                    {
                        char ch = charLabel[0];

                        // Ctrl+letter → ASCII control code (Ctrl+A=0x01, Ctrl+C=0x03, ..., Ctrl+Z=0x1A)
                        if (ctrl && char.IsLetter(ch))
                        {
                            char upper = char.ToUpper(ch);
                            if (upper >= 'A' && upper <= 'Z')
                            {
                                sequence = ((char)(upper - '@')).ToString();
                            }
                        }
                        else
                        {
                            // Apply CAPS logic BEFORE ISO 646 lookup so case is consistent:
                            // PC convention: unshifted=lowercase, shifted=uppercase
                            // TDV labels: Primary=UPPERCASE, Shifted=lowercase
                            // CAPS logic normalizes to PC convention for ALL characters,
                            // including national variants (Ø↔ø, Æ↔æ, Å↔å)
                            if (char.IsLetter(ch))
                            {
                                bool wantUpper = _viewModel.IsCapsLockActive != shift;
                                ch = wantUpper ? char.ToUpper(ch) : char.ToLower(ch);
                            }

                            // ISO 646 variant characters (ø, æ, å, Ø, Æ, Å, etc.): convert to wire
                            // byte. The TDV renders bytes through the active NRC, so we send the
                            // ASCII position byte (e.g. ø → '|', Æ → '[', Å → ']'). Conversion is
                            // in Core, shared with the two typed-input paths.
                            sequence = RetroTerm.Core.Terminal.Emulators.TDV.TDVCharacterSets
                                .ConvertToWireBytes(ch.ToString(), langCode);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(sequence))
            {
                // Send to terminal
                InputReceived?.Invoke(this, sequence);
            }
        }

        /// <summary>
        /// Handle sticky modifiers auto-released after a key combo is sent
        /// </summary>
        private void OnStickyModifiersReleased(object? sender, StickyModifiersReleasedEventArgs e)
        {
            for (int i = 0; i < e.ReleasedGridPositions.Length; i++)
            {
                var gp = e.ReleasedGridPositions[i];
                UpdateKeyVisual(gp, KeyVisualState.Normal);
            }
        }

        /// <summary>
        /// Handle key released event from ViewModel
        /// </summary>
        private void OnKeyReleased(object? sender, KeyReleasedEventArgs e)
        {
            // Key released - currently no action needed
        }

        /// <summary>
        /// Handle layout selection changed
        /// </summary>
        private void OnLayoutChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_layoutComboBox?.SelectedItem is ComboBoxItem item)
            {
                var layoutName = item.Tag?.ToString();
                if (Enum.TryParse<NationalKeyboardLayout>(layoutName, out var layout))
                {
                    _viewModel.SelectedLayout = layout;
                    RenderKeyboard(); // Re-render with new labels
                    LayoutChanged?.Invoke(this, _viewModel.CurrentLanguageCode);
                }
            }
        }

        /// <summary>
        /// Handle Show Key No checkbox changed
        /// </summary>
        private void OnShowKeyNoChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _showKeyNo = _showKeyNoCheckBox?.IsChecked ?? false;
            RenderKeyboard();
        }

        /// <summary>
        /// Handle Reset Bindings button click
        /// </summary>
        private void OnResetBindingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            TDVKeyBindingConfiguration.Instance.SetDefaults();
            TDVKeyBindingConfiguration.Instance.Save();
            RenderKeyboard(); // Re-render to update indicators
        }

        /// <summary>
        /// Handle key binding configuration changes
        /// </summary>
        private void OnBindingConfigChanged(object? sender, EventArgs e)
        {
            RenderKeyboard(); // Re-render to update indicators
        }

        /// <summary>
        /// Handle PUSH key configuration changes — re-render to update indicators and tooltips
        /// </summary>
        private void OnPushKeyConfigChanged(object? sender, EventArgs e)
        {
            RenderKeyboard();
        }

        /// <summary>
        /// Show popup to manage key bindings for a TDV key.
        /// Uses key capture mode: press a key combination to bind it.
        /// Anchored to the right edge of the clicked key.
        /// </summary>
        private void ShowBindingPopup(TDVKeyVisualMetadata meta)
        {
            var gridPos = meta.GridPosition;
            if (string.IsNullOrEmpty(gridPos))
                return;

            _currentEditingTDVKey = gridPos;
            _capturingKey = false;

            // Close any existing popup
            if (_bindingPopup != null)
            {
                _bindingPopup.IsOpen = false;
            }

            // Create popup content
            // Themed since 27 September 2026: this popup was a WHITE panel with bare Fluent buttons
            // inside the dark app, with its status text in DarkBlue and Red that a dark panel
            // cannot show. Every colour now comes from the theme, through the helpers below.
            var stackPanel = new StackPanel
            {
                Margin = new Thickness(5),
                MinWidth = 220
            };

            // Header with key name (e.g. "HJELP (G53)")
            var keyName = TDV2200KeyRegistry.GetEnglishName(gridPos);
            var headerText = !string.IsNullOrEmpty(keyName)
                ? $"{keyName} ({gridPos})"
                : gridPos;

            var header = new TextBlock
            {
                Text = headerText,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 5)
            };
            SetThemeForeground(header, "PrimaryTextBrush");
            stackPanel.Children.Add(header);

            // Current bindings list
            var sources = TDVKeyBindingConfiguration.Instance.GetSourcesForGrid(gridPos);
            if (sources.Count > 0)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    var src = sources[i];
                    var bindingRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };

                    var bindingLabel = new TextBlock
                    {
                        Text = TDVKeyBindingConfiguration.GetBindingLabel(src),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        FontSize = 12
                    };
                    SetThemeForeground(bindingLabel, "PrimaryTextBrush");
                    bindingRow.Children.Add(bindingLabel);

                    var removeBtn = new Button
                    {
                        Content = "X",
                        Classes = { "Secondary" },
                        FontSize = 9,
                        Padding = new Thickness(3, 1),
                        MinWidth = 20,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Tag = src
                    };
                    removeBtn.Click += (s, ev) =>
                    {
                        if (s is Button btn && btn.Tag is KeyBindingSource removeSrc)
                        {
                            TDVKeyBindingConfiguration.Instance.RemoveBinding(removeSrc);
                            TDVKeyBindingConfiguration.Instance.Save();
                            // Close and reopen to refresh
                            if (_bindingPopup != null)
                                _bindingPopup.IsOpen = false;
                            ShowBindingPopup(meta);
                        }
                    };
                    bindingRow.Children.Add(removeBtn);

                    stackPanel.Children.Add(bindingRow);
                }
            }
            else
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "(no bindings)",
                    [!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SecondaryTextBrush"),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 3)
                });
            }

            // Shifted checkbox
            var shiftedCheckBox = new CheckBox
            {
                Content = "Send shifted variant",
                IsChecked = false,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0)
            };
            stackPanel.Children.Add(shiftedCheckBox);

            // Status/capture text
            var statusText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                [!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentBrush"),
                Margin = new Thickness(0, 5, 0, 5),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 220
            };
            stackPanel.Children.Add(statusText);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 5
            };

            var addButton = new Button { Content = "Add Binding", Classes = { "dialog-primary" }, MinWidth = 80, Padding = new Thickness(10, 5) };
            var clearButton = new Button
            {
                Content = "Clear All",
                Classes = { "dialog-danger" },
                MinWidth = 60,
                Padding = new Thickness(10, 5),
                IsEnabled = sources.Count > 0
            };
            var closeButton = new Button { Content = "Close", Classes = { "dialog-secondary" }, MinWidth = 50, Padding = new Thickness(10, 5) };

            addButton.Click += (s, ev) =>
            {
                _capturingKey = true;
                _captureStatusText = statusText;
                _captureShiftedCheckBox = shiftedCheckBox;
                _captureMetadata = meta;
                _captureAddButton = addButton;
                statusText.Text = "Press a key combination...\n(Escape to cancel)";
                SetThemeForeground(statusText, "AccentBrush");
                addButton.IsEnabled = false;
                // Visual hint: blue border during capture
                if (_capturePopupBorder != null)
                    SetThemeBorder(_capturePopupBorder, "AccentBrush");
            };

            clearButton.Click += (s, ev) =>
            {
                TDVKeyBindingConfiguration.Instance.RemoveBindingsForGrid(gridPos);
                TDVKeyBindingConfiguration.Instance.Save();
                // Close and reopen to refresh
                if (_bindingPopup != null)
                    _bindingPopup.IsOpen = false;
                ShowBindingPopup(meta);
            };

            closeButton.Click += (s, ev) =>
            {
                _capturingKey = false;
                if (_bindingPopup != null)
                    _bindingPopup.IsOpen = false;
            };

            buttonPanel.Children.Add(addButton);
            buttonPanel.Children.Add(clearButton);
            buttonPanel.Children.Add(closeButton);
            stackPanel.Children.Add(buttonPanel);

            // Create popup border
            var border = new Border
            {
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Child = stackPanel,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 5, OffsetX = 2, OffsetY = 2, Color = Color.FromArgb(64, 0, 0, 0) })
            };
            _capturePopupBorder = border;
            border[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("PanelBackgroundBrush");
            SetThemeBorder(border, "BorderBrush");

            _bindingPopup = new Popup
            {
                Child = border,
                // Placement replaces the obsolete PlacementMode property (CS0618).
                Placement = PlacementMode.AnchorAndGravity,
                IsLightDismissEnabled = false // Don't dismiss on Alt press
            };

            // Add popup to the canvas, positioned at the right edge of the key
            if (_canvas != null)
            {
                // Remove any existing popup from canvas first
                for (int i = _canvas.Children.Count - 1; i >= 0; i--)
                {
                    if (_canvas.Children[i] is Popup)
                    {
                        _canvas.Children.RemoveAt(i);
                    }
                }

                _canvas.Children.Add(_bindingPopup);
                // Position popup to the right of the key, slightly below
                double popupX = meta.X + meta.Width + 5;
                double popupY = meta.Y;
                Canvas.SetLeft(_bindingPopup, popupX);
                Canvas.SetTop(_bindingPopup, popupY);
                _bindingPopup.PlacementTarget = _canvas;
                _bindingPopup.IsOpen = true;
            }
        }

        /// <summary>
        /// Update LED indicator visuals (TDV2200 hardware LEDs)
        /// </summary>
        private void UpdateLEDIndicators()
        {
            // L1-L4 LEDs (all red when lit)
            UpdateLED(_ledL1, _viewModel.GetLEDState("L1"), "#FF0000");    // Red - Expand
            UpdateLED(_ledL2, _viewModel.GetLEDState("L2"), "#FF0000");    // Red - Append
            UpdateLED(_ledL3, _viewModel.GetLEDState("L3"), "#FF0000");    // Red - Busy
            UpdateLED(_ledL4, _viewModel.GetLEDState("L4"), "#FF0000");    // Red - Message

            // Status LEDs (all red except ON)
            UpdateLED(_ledLine, _viewModel.GetLEDState("LINE"), "#FF0000");  // Red - Terminal online
            UpdateLED(_ledCar, _viewModel.GetLEDState("CAR"), "#FF0000");    // Red - Carrier signal
            UpdateLED(_ledWait, _viewModel.GetLEDState("WAIT"), "#FF0000");  // Red - Wait to get online
            UpdateLED(_ledError, _viewModel.GetLEDState("ERROR"), "#FF0000"); // Red - Error condition
            UpdateLED(_ledOn, _viewModel.GetLEDState("ON"), "#00FF00");      // Green - Power on (always on)
        }

        /// <summary>
        /// Update a single LED visual
        /// </summary>
        private void UpdateLED(Ellipse? led, bool state, string activeColor)
        {
            if (led == null) return;

            led.Fill = state
                ? new SolidColorBrush(Color.Parse(activeColor))
                : new SolidColorBrush(Color.FromRgb(64, 64, 64)); // Dark gray when off
        }

        /// <summary>
        /// Public method to update LED state from external source
        /// </summary>
        public void SetLEDState(string ledName, bool state)
        {
            _viewModel.SetLEDState(ledName, state);
            UpdateLEDIndicators();
        }

        /// <summary>
        /// Mirror physical keyboard input (highlight when user types)
        /// </summary>
        public void MirrorPhysicalKey(int virtualKeyCode, bool pressed)
        {
            if (pressed)
            {
                _viewModel.MirrorPhysicalKeyPress(virtualKeyCode);
            }
            else
            {
                _viewModel.MirrorPhysicalKeyRelease(virtualKeyCode);
            }
        }

        /// <summary>
        /// Whether the panel is currently in key capture mode (for binding assignment)
        /// </summary>
        public bool IsCapturingKey => _capturingKey;

        /// <summary>
        /// Whether the key-binding popup is on screen.
        /// </summary>
        public bool IsBindingPopupOpen => _bindingPopup != null && _bindingPopup.IsOpen;

        /// <summary>
        /// Closes the key-binding popup, cancelling any key capture in progress.
        /// </summary>
        /// <remarks>
        /// Ronny, 27 September 2026: "the right click got stuck in a popup". The popup is not
        /// light-dismissed (an Alt press would otherwise close it mid-capture), Escape only
        /// cancelled a capture, and the Close button was invisible on the old white panel - so
        /// there was no way out. Escape now closes it through this, from VirtualKeyboardWindow.
        /// </remarks>
        /// <returns>
        /// True when a popup was open and is now closed; false when there was none.
        /// </returns>
        public bool CloseBindingPopup()
        {
            if (_bindingPopup == null || !_bindingPopup.IsOpen) return false;
            CancelCaptureMode();
            _bindingPopup.IsOpen = false;
            return true;
        }

        /// <summary>
        /// Puts the open popup into key-capture mode, as the Add Binding button does. Test seam.
        /// </summary>
        internal void BeginCaptureForTesting()
        {
            if (!IsBindingPopupOpen) return;
            _capturingKey = true;
        }

        /// <summary>
        /// Opens the key-binding popup for a grid position, the way a right-click on that key
        /// does. Test seam.
        /// </summary>
        /// <param name="gridPosition">
        /// The key's grid position, for example G53.
        /// </param>
        /// <returns>
        /// True when the key exists and the popup opened.
        /// </returns>
        internal bool OpenBindingPopupForTesting(string gridPosition)
        {
            var keys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].GridPosition == gridPosition)
                {
                    ShowBindingPopup(keys[i]);
                    return IsBindingPopupOpen;
                }
            }
            return false;
        }

        /// <summary>
        /// Handle a captured key event during binding assignment mode.
        /// Called from VirtualKeyboardWindow.OnKeyDown when IsCapturingKey is true.
        /// Smart modifier accumulation: bare modifier presses show real-time feedback
        /// ("Holding: Alt + ..."), binding commits only when a non-modifier key arrives.
        /// Escape cancels capture mode.
        /// </summary>
        public void HandleCapturedKey(KeyEventArgs ev)
        {
            if (!_capturingKey || _currentEditingTDVKey == null)
                return;

            // Escape cancels capture mode
            if (ev.Key == Key.Escape)
            {
                CancelCaptureMode();
                ev.Handled = true;
                return;
            }

            // Convert Avalonia key to VK code
            var avaloniaKeyInt = (int)ev.Key;
            var vkCode = TDVKeyBindingConfiguration.AvaloniaKeyToVK(avaloniaKeyInt);

            // If this is a bare modifier key, show accumulation feedback and wait for real key
            if (KeyBindingSource.IsModifierOnlyKey(vkCode))
            {
                // Show real-time "Holding: Alt + ..." feedback
                if (_captureStatusText != null)
                {
                    var holdingText = BuildModifierHoldingText(ev.KeyModifiers);
                    _captureStatusText.Text = holdingText;
                    SetThemeForeground(_captureStatusText, "AccentBrush");
                }
                ev.Handled = true;
                return; // Wait for the actual key
            }

            // Non-modifier key arrived — commit the binding
            var modifiers = RetroTerm.Core.Terminal.Input.KeyModifiers.None;
            if (ev.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
                modifiers |= RetroTerm.Core.Terminal.Input.KeyModifiers.Shift;
            if (ev.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
                modifiers |= RetroTerm.Core.Terminal.Input.KeyModifiers.Ctrl;
            if (ev.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
                modifiers |= RetroTerm.Core.Terminal.Input.KeyModifiers.Alt;

            var source = new KeyBindingSource(vkCode, modifiers);
            var friendlyLabel = TDVKeyBindingConfiguration.GetBindingLabel(source);

            // Validate
            if (!source.IsValid())
            {
                if (_captureStatusText != null)
                {
                    _captureStatusText.Text = $"Invalid: {friendlyLabel}\nBare text keys need a modifier (Alt/Ctrl)";
                    SetThemeForeground(_captureStatusText, "ErrorBrush");
                }
                ev.Handled = true;
                return;
            }

            var shifted = _captureShiftedCheckBox?.IsChecked ?? false;
            var target = new KeyBindingTarget(_currentEditingTDVKey, shifted);
            var result = TDVKeyBindingConfiguration.Instance.SetBinding(source, target);

            if (result)
            {
                TDVKeyBindingConfiguration.Instance.Save();
                _capturingKey = false;
                // Show success briefly, then close and reopen to refresh
                if (_captureStatusText != null)
                {
                    _captureStatusText.Text = $"Bound: {friendlyLabel}";
                    SetThemeForeground(_captureStatusText, "SuccessBrush");
                }
                // Reset border color
                if (_capturePopupBorder != null)
                    SetThemeBorder(_capturePopupBorder, "BorderBrush");
                // Close and reopen to refresh
                if (_bindingPopup != null)
                    _bindingPopup.IsOpen = false;
                if (_captureMetadata != null)
                    ShowBindingPopup(_captureMetadata);
            }
            else
            {
                if (_captureStatusText != null)
                {
                    _captureStatusText.Text = $"Failed: {friendlyLabel}";
                    SetThemeForeground(_captureStatusText, "ErrorBrush");
                }
            }

            ev.Handled = true;
        }

        /// <summary>
        /// Handle key release during capture mode.
        /// If all modifiers are released without a non-modifier key being pressed,
        /// reset the status text back to "Press a key combination...".
        /// </summary>
        public void HandleCapturedKeyUp(KeyEventArgs ev)
        {
            if (!_capturingKey || _captureStatusText == null)
                return;

            // Check if any modifiers are still held
            if (ev.KeyModifiers == Avalonia.Input.KeyModifiers.None)
            {
                // All modifiers released — reset to waiting state
                _captureStatusText.Text = "Press a key combination...\n(Escape to cancel)";
                SetThemeForeground(_captureStatusText, "AccentBrush");
            }
            else
            {
                // Some modifiers still held — update display
                var holdingText = BuildModifierHoldingText(ev.KeyModifiers);
                _captureStatusText.Text = holdingText;
                SetThemeForeground(_captureStatusText, "AccentBrush");
            }
        }

        /// <summary>
        /// Binds a text block's colour to a theme brush, so the popup follows the theme and
        /// stays readable on its dark panel.
        /// </summary>
        /// <param name="text">
        /// The text block to colour.
        /// </param>
        /// <param name="brushKey">
        /// The theme brush resource, for example AccentBrush or ErrorBrush.
        /// </param>
        private static void SetThemeForeground(TextBlock text, string brushKey)
        {
            text[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey);
        }

        /// <summary>
        /// Binds a border's line colour to a theme brush.
        /// </summary>
        /// <param name="border">
        /// The border to colour.
        /// </param>
        /// <param name="brushKey">
        /// The theme brush resource, for example BorderBrush or AccentBrush.
        /// </param>
        private static void SetThemeBorder(Border border, string brushKey)
        {
            border[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey);
        }

        /// <summary>
        /// Cancel the current key capture mode, resetting the popup to its default state.
        /// </summary>
        private void CancelCaptureMode()
        {
            _capturingKey = false;
            if (_captureStatusText != null)
            {
                _captureStatusText.Text = "";
                SetThemeForeground(_captureStatusText, "AccentBrush");
            }
            if (_capturePopupBorder != null)
                SetThemeBorder(_capturePopupBorder, "BorderBrush");
            if (_captureAddButton != null)
                _captureAddButton.IsEnabled = true;
        }

        /// <summary>
        /// Build display text showing which modifier keys are being held during capture.
        /// e.g. "Holding: Alt + ..." or "Holding: Ctrl + Shift + ..."
        /// </summary>
        private static string BuildModifierHoldingText(Avalonia.Input.KeyModifiers modifiers)
        {
            var sb = new System.Text.StringBuilder("Holding: ");
            bool any = false;

            if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            {
                sb.Append("Ctrl");
                any = true;
            }
            if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
            {
                if (any) sb.Append(" + ");
                sb.Append("Alt");
                any = true;
            }
            if (modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
            {
                if (any) sb.Append(" + ");
                sb.Append("Shift");
                any = true;
            }

            if (any)
                sb.Append(" + ...");
            else
                sb.Append("...");

            return sb.ToString();
        }

        /// <summary>
        /// Mirror a physical key event on the virtual keyboard with visual feedback.
        /// Looks up both TDV key bindings and direct VK code matches from the registry.
        /// </summary>
        public void MirrorKeyEvent(int vkCode, RetroTerm.Core.Terminal.Input.KeyModifiers modifiers, bool pressed)
        {
            if (pressed)
            {
                // Look up via TDV key binding configuration
                if (TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, modifiers, out var target))
                {
                    var gp = target.GridPosition;
                    if (!string.IsNullOrEmpty(gp) && _visualKeys.ContainsKey(gp))
                    {
                        _mirroredGridPositions.Add(gp);
                        UpdateKeyVisual(gp, KeyVisualState.Pressed);
                    }
                }

                // Also look up direct VK code match from visual registry (arrows, Enter, space, etc.)
                var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
                for (int i = 0; i < allKeys.Length; i++)
                {
                    if (allKeys[i].VirtualKeyCode == vkCode)
                    {
                        var gp = allKeys[i].GridPosition;
                        // If the physical key matches a sticky virtual modifier, release it
                        if (allKeys[i].Category == KeyCategory.Modifier && _viewModel.IsStickyModifier(gp))
                        {
                            _viewModel.ReleaseStickyModifier(gp);
                            UpdateKeyVisual(gp, KeyVisualState.Normal);
                        }
                        else
                        {
                            _mirroredGridPositions.Add(gp);
                            UpdateKeyVisual(gp, KeyVisualState.Pressed);
                        }
                        break;
                    }
                }

                // Start/restart auto-release safety timer (catches missed KeyUp, e.g. Alt+Tab)
                EnsureMirrorReleaseTimer();
                _mirrorReleaseTimer!.Stop();
                _mirrorReleaseTimer.Start();
            }
            else
            {
                // Stop the auto-release timer — we got a proper KeyUp
                _mirrorReleaseTimer?.Stop();

                // Release: reset visual for binding match
                if (TDVKeyBindingConfiguration.Instance.TryGetTarget(vkCode, modifiers, out var target))
                {
                    var gp = target.GridPosition;
                    if (!string.IsNullOrEmpty(gp))
                    {
                        _mirroredGridPositions.Remove(gp);
                        if (!_viewModel.IsKeyPressed(gp))
                            UpdateKeyVisual(gp, KeyVisualState.Normal);
                    }
                }

                // Release: reset visual for direct VK match from registry
                var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
                for (int i = 0; i < allKeys.Length; i++)
                {
                    if (allKeys[i].VirtualKeyCode == vkCode)
                    {
                        var gp = allKeys[i].GridPosition;
                        _mirroredGridPositions.Remove(gp);
                        if (!_viewModel.IsKeyPressed(gp))
                            UpdateKeyVisual(gp, KeyVisualState.Normal);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Ensure the auto-release timer exists. Handles edge cases where KeyUp is missed
        /// (e.g., Alt+Tab switches away from the window).
        /// </summary>
        private void EnsureMirrorReleaseTimer()
        {
            if (_mirrorReleaseTimer != null) return;

            _mirrorReleaseTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2000)
            };
            _mirrorReleaseTimer.Tick += (s, e) =>
            {
                _mirrorReleaseTimer.Stop();
                // Reset all mirrored keys to normal
                // Copy to array to avoid modifying during iteration
                var positions = new string[_mirroredGridPositions.Count];
                _mirroredGridPositions.CopyTo(positions);
                _mirroredGridPositions.Clear();
                for (int i = 0; i < positions.Length; i++)
                {
                    if (!_viewModel.IsKeyPressed(positions[i]))
                        UpdateKeyVisual(positions[i], KeyVisualState.Normal);
                }
            };
        }

        /// <summary>
        /// Convert Avalonia pointer KeyModifiers to Core KeyModifiers
        /// </summary>
        private static RetroTerm.Core.Terminal.Input.KeyModifiers ConvertPointerModifiers(
            Avalonia.Input.KeyModifiers mods)
        {
            var result = RetroTerm.Core.Terminal.Input.KeyModifiers.None;
            if (mods.HasFlag(Avalonia.Input.KeyModifiers.Shift))
                result |= RetroTerm.Core.Terminal.Input.KeyModifiers.Shift;
            if (mods.HasFlag(Avalonia.Input.KeyModifiers.Control))
                result |= RetroTerm.Core.Terminal.Input.KeyModifiers.Ctrl;
            if (mods.HasFlag(Avalonia.Input.KeyModifiers.Alt))
                result |= RetroTerm.Core.Terminal.Input.KeyModifiers.Alt;
            return result;
        }

        /// <summary>
        /// Update the LED indicator on a toggle key (CAPS, LOCK).
        /// Uses the direct ToggleLED Ellipse reference for LED-style keys,
        /// falls back to searching for "•" TextBlock for legacy rendering.
        /// </summary>
        private void UpdateToggleKeyLED(string gridPosition, bool active)
        {
            if (!_visualKeys.TryGetValue(gridPosition, out var visualKey))
                return;

            // Use direct LED Ellipse reference if available (toggle LED keys)
            if (visualKey.ToggleLED != null)
            {
                visualKey.ToggleLED.Fill = active ? ToggleLEDOnBrush : ToggleLEDOffBrush;
                return;
            }

            // Fallback: find "•" TextBlock indicator
            var panel = visualKey.TextPanel;
            if (panel == null) return;

            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is TextBlock tb
                    && Canvas.GetLeft(tb) == 2 && Canvas.GetTop(tb) == 2
                    && (tb.Text == "•" || tb.Text == "\u2022"))
                {
                    tb.Foreground = active
                        ? new SolidColorBrush(Color.FromRgb(0, 255, 0))
                        : Brushes.Gray;
                    break;
                }
            }
        }

        /// <summary>
        /// Visual representation of a key
        /// </summary>
        private class VisualKey
        {
            // Metadata and Labels are set in the object initializer, so `required`
            // enforces that at the call site.
            public required TDVKeyVisualMetadata Metadata { get; set; }

            /// <summary>
            /// Per-language labels, null for keys that have none.
            /// </summary>
            public required Dictionary<string, KeyLabel>? Labels { get; set; }

            // These three are built and assigned by CreateVisualKey a few lines after
            // construction, not in the initializer - hence null! rather than `required`.
            // They are never observed null outside that method.
            public Shape Background { get; set; } = null!;
            public Shape Border { get; set; } = null!;
            public Panel TextPanel { get; set; } = null!;
            // For toggle LED keys (CAPS E0, LOCK C0) — direct references for fast updates
            public Ellipse? ToggleLED { get; set; }
            public Shape? InnerCircle { get; set; }
        }

        /// <summary>
        /// Visual state of a key
        /// </summary>
        private enum KeyVisualState
        {
            Normal,
            Hover,
            Pressed
        }
    }
}
