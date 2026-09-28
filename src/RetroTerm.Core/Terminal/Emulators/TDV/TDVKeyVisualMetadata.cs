namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// How a key's primary content should be rendered.
    /// The renderer dispatches on this — no hardcoded grid-position checks needed.
    /// </summary>
    public enum KeyRenderMode : byte
    {
        /// <summary>
        /// Normal single text label (most keys)
        /// </summary>
        Text = 0,
        /// <summary>
        /// SVG symbol from asset (arrows, return, etc.)
        /// </summary>
        Symbol = 1,
        /// <summary>
        /// Two stacked text lines centered in circle (E47, E49, F48, F49)
        /// </summary>
        TwoLineText = 2,
        /// <summary>
        /// Three stacked text lines centered in circle (F47: -/TAB/+)
        /// </summary>
        ThreeLineText = 3,
        /// <summary>
        /// Individual characters stacked vertically (B54: E-N-T-E-R)
        /// </summary>
        VerticalLetterStack = 4,
        /// <summary>
        /// Font-rendered text char + SVG overlay on top (E14: "a" with diagonal slash)
        /// </summary>
        TextWithOverlay = 5
    }

    /// <summary>
    /// Semantic category of a key. Replaces the Desktop-layer KeyType enum.
    /// </summary>
    public enum KeyCategory : byte
    {
        Alphanumeric = 0,
        Modifier = 1,
        Toggle = 2,
        Function = 3,
        PushKey = 4,
        Navigation = 5,
        NumericPad = 6,
        ApplicationControl = 7,
        System = 8,
        Local = 9
    }

    /// <summary>
    /// Complete visual metadata for a single TDV2200 keyboard key.
    /// Contains all information needed to render and interact with a key
    /// on any platform — no UI-framework types, pure doubles and strings.
    /// </summary>
    public sealed class TDVKeyVisualMetadata
    {
        // --- Identity ---

        /// <summary>
        /// Grid position identifier (e.g. "G0", "E14", "B54")
        /// </summary>
        public string GridPosition { get; }

        /// <summary>
        /// Row letter: 'A' (bottom) through 'G' (top)
        /// </summary>
        public char Row { get; }

        /// <summary>
        /// Column number within the row
        /// </summary>
        public int Column { get; }

        // --- Geometry (layout units, not pixels — portable) ---

        /// <summary>
        /// X coordinate of key's top-left corner
        /// </summary>
        public double X { get; }

        /// <summary>
        /// Y coordinate of key's top-left corner
        /// </summary>
        public double Y { get; }

        /// <summary>
        /// Key width in layout units
        /// </summary>
        public double Width { get; }

        /// <summary>
        /// Key height in layout units
        /// </summary>
        public double Height { get; }

        // --- Visual classification ---

        /// <summary>
        /// Physical key color (White, Orange, Brown)
        /// </summary>
        public TDVKeyColor Color { get; }

        /// <summary>
        /// Semantic category of the key
        /// </summary>
        public KeyCategory Category { get; }

        /// <summary>
        /// How the primary content should be rendered
        /// </summary>
        public KeyRenderMode RenderMode { get; }

        // --- Rendering hints (replace ALL hardcoded grid-position checks) ---

        /// <summary>
        /// Whether the key has a hardware LED indicator (only E0 CAPS, C0 LOCK)
        /// </summary>
        public bool HasLED { get; }

        /// <summary>
        /// SVG asset name for Symbol/TextWithOverlay modes, null for text-only keys
        /// </summary>
        public string? SymbolId { get; }

        /// <summary>
        /// Symbol size as fraction of inner shape (default 0.40, C13=0.55, D47/D49=0.50)
        /// </summary>
        public double SymbolSizeFactor { get; }

        /// <summary>
        /// True = filled symbol, false = stroke-only outline
        /// </summary>
        public bool SymbolFilled { get; }

        /// <summary>
        /// Text lines for TwoLineText/ThreeLineText modes, null otherwise
        /// </summary>
        public string[]? MultiLineTexts { get; }

        /// <summary>
        /// Overlay text char for TextWithOverlay mode (e.g. "a" for E14 DEL key), null otherwise
        /// </summary>
        public string? OverlayText { get; }

        /// <summary>
        /// Whether this key can have user key bindings
        /// </summary>
        public bool IsBindable { get; }

        /// <summary>
        /// PC Virtual Key Code mapping (0 = no direct mapping)
        /// </summary>
        public int VirtualKeyCode { get; }

        /// <summary>
        /// Whether the second line of TwoLineText should be underlined (F49)
        /// </summary>
        public bool SecondLineUnderlined { get; }

        /// <summary>
        /// Whether multi-line text lines should use bold weight (E47, E49 chevrons)
        /// </summary>
        public bool MultiLineBold { get; }

        /// <summary>
        /// Indicator text shown in top-left corner (e.g. "000", "SI", "CLEAR"), null when the key has none
        /// </summary>
        public string? Indicator { get; }

        /// <summary>
        /// Sequence sent when Ctrl is held while pressing this key, null = no Ctrl variant
        /// </summary>
        public string? CtrlSequence { get; }

        public TDVKeyVisualMetadata(
            string gridPosition, char row, int column,
            double x, double y, double width, double height,
            TDVKeyColor color, KeyCategory category, KeyRenderMode renderMode,
            bool hasLED, string? symbolId, double symbolSizeFactor, bool symbolFilled,
            string[]? multiLineTexts, string? overlayText,
            bool isBindable, int virtualKeyCode,
            bool secondLineUnderlined = false, bool multiLineBold = false,
            string? indicator = null, string? ctrlSequence = null)
        {
            GridPosition = gridPosition;
            Row = row;
            Column = column;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Color = color;
            Category = category;
            RenderMode = renderMode;
            HasLED = hasLED;
            SymbolId = symbolId;
            SymbolSizeFactor = symbolSizeFactor;
            SymbolFilled = symbolFilled;
            MultiLineTexts = multiLineTexts;
            OverlayText = overlayText;
            IsBindable = isBindable;
            VirtualKeyCode = virtualKeyCode;
            SecondLineUnderlined = secondLineUnderlined;
            MultiLineBold = multiLineBold;
            Indicator = indicator;
            CtrlSequence = ctrlSequence;
        }
    }
}
