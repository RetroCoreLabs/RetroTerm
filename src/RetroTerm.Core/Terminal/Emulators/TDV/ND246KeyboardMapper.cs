namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// TDV2200 ND 246 keyboard mapping based on official keyboard specification.
    /// Thin wrapper around TDV2200KeyRegistry that holds mode state (extended/simple, numpad function).
    /// All key data and sequence resolution is delegated to the registry.
    /// </summary>
    public class ND246KeyboardMapper
    {
        private bool _extendedControlMode;
        private bool _numericPadFunctionMode;

        public ND246KeyboardMapper(bool extendedControlMode = true, bool numericPadFunctionMode = false)
        {
            _extendedControlMode = extendedControlMode;
            _numericPadFunctionMode = numericPadFunctionMode;
        }

        /// <summary>
        /// Enable/disable Extended Control Mode (CSI sequences vs simple ASCII)
        /// </summary>
        public bool ExtendedControlMode
        {
            get => _extendedControlMode;
            set => _extendedControlMode = value;
        }

        /// <summary>
        /// Enable/disable Numeric Pad Function Mode (CSI vs numeric characters)
        /// </summary>
        public bool NumericPadFunctionMode
        {
            get => _numericPadFunctionMode;
            set => _numericPadFunctionMode = value;
        }

        /// <summary>
        /// Map a grid position key press to its output sequence
        /// </summary>
        public string? MapGridKey(string gridPosition, bool shift = false, bool ctrl = false)
        {
            return TDV2200KeyRegistry.GetSequence(gridPosition, _extendedControlMode, _numericPadFunctionMode, shift, ctrl);
        }

        /// <summary>
        /// Map standard keyboard key names to grid positions
        /// </summary>
        public string? GetGridPositionForKey(string keyName)
        {
            return TDV2200KeyRegistry.GetGridForName(keyName);
        }

        /// <summary>
        /// Map a key press to output sequence by name
        /// </summary>
        public string? MapKey(string keyName, bool shift = false, bool ctrl = false)
        {
            var gridPos = TDV2200KeyRegistry.GetGridForName(keyName);
            if (gridPos != null)
            {
                return TDV2200KeyRegistry.GetSequence(gridPos, _extendedControlMode, _numericPadFunctionMode, shift, ctrl);
            }

            return null;
        }
    }
}
