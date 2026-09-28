using System;
using System.Diagnostics;

namespace RetroTerm.Core.Configuration
{
    /// <summary>
    /// TDV Terminal Configuration - manages all TDV-specific switches and modes
    /// </summary>
    public class TDVConfiguration
    {
        public TDVConfiguration()
        {
            SetDefaults();
        }

        #region Character Set Switching

        /// <summary>
        /// SS2 - Single shift 2 ESC N(0x4E)
        /// SS2 indicates that the next character is to be taken as being from the optional second character set.
        /// </summary>
        public bool SS2 { get; internal set; }

        /// <summary>
        /// SS3 - Single shift 3 ESC O(0x4F)
        /// SS3 indicates that the next character is to be taken as being from the optional third character set.
        /// </summary>
        public bool SS3 { get; internal set; }

        #endregion

        #region Extended Control

        /// <summary>
        /// EC - Extended Control switch
        /// PUT is ignored when the EC switch is ON.
        /// </summary>
        public bool EC { get; internal set; }

        #endregion

        #region Privacy and Device Control

        /// <summary>
        /// PM - Privacy message ESC *(0x5E)
        /// This is an opening sequence for a privacy message string, ST terminates the string.
        /// The string will be displayed on the last line of the screen.
        /// </summary>
        public bool PM { get; internal set; }

        public string PrivacyMessage { get; internal set; } = string.Empty;

        /// <summary>
        /// DCS - Device Control String
        /// </summary>
        public bool DCS { get; internal set; }

        public string DCS_string { get; internal set; } = string.Empty;

        #endregion

        #region Graphics and Display

        /// <summary>
        /// GRM - Graphics Mode switch
        /// </summary>
        public bool GRM { get; internal set; }

        /// <summary>
        /// UR - Update Region switch
        /// </summary>
        public bool UR { get; internal set; }

        #endregion

        #region Direct Line Entry

        /// <summary>
        /// DLE - Direct Line Entry cursor positioning
        /// DLE is a lead-in for direct cursor addressing. The next two characters give the binary value of the desired cursor position.
        /// First Line number; then column number. (Line number 0-24 and column number 0-79).
        /// Parameters are accepted as long as the five rightmost bits are valid.
        /// </summary>
        public bool DLE { get; internal set; }

        public byte DLE_Count { get; internal set; }

        #endregion

        #region Escape Sequence Processing

        /// <summary>
        /// Three-character escape sequence intermediate character
        /// </summary>
        public char ThreeCharacterEsc { get; internal set; }

        #endregion

        #region Cursor and Display Settings

        /// <summary>
        /// CT - Cursor Type Switch
        /// It has two settings: LINE and BLOCK
        /// * In the LINE state, an underline blinking cursor will be used.
        /// * In the BLOCK state, a steady block cursor will be used.
        /// The default setting is LINE.
        /// </summary>
        public enum CursorType
        {
            LINE,
            BLOCK
        }

        public CursorType CT { get; set; } = CursorType.LINE;

        /// <summary>
        /// MB - Margin Bell Switch
        /// The margin bell switch controls whether or not the bell should sound
        /// when the 72nd character position of a single-width line is entered, respectively the 32nd character position of a double-width line.
        /// The switch has two settings: ON and OFF
        /// The margin bell is enabled in the ON state.
        /// The default setting is ON.
        /// </summary>
        public bool MB { get; set; } = true;

        /// <summary>
        /// Application cursor mode (DECCKM)
        /// </summary>
        public bool IsApplicationCursorMode { get; internal set; }

        #endregion

        #region TDV 2115 Compatibility Mode

        /// <summary>
        /// TDV 2115 compatibility mode (CSI ? 40 h/l)
        /// When enabled, only C0 control codes are accepted, ESC sequences ignored (except ESC Q)
        /// </summary>
        public bool Is2115CompatibilityMode { get; internal set; }

        #endregion

        #region Methods

        private void SetDefaults()
        {
            // Set all defaults
            SS2 = false;
            SS3 = false;
            EC = false;
            PM = false;
            DCS = false;
            GRM = false;
            UR = false;
            DLE = false;
            DLE_Count = 0;
            ThreeCharacterEsc = '\0';
            CT = CursorType.LINE;
            MB = true;
            IsApplicationCursorMode = false;
            Is2115CompatibilityMode = false;
        }

        /// <summary>
        /// Process received push keys via DCS sequences
        /// The PUSH-keys can be loaded from the host computer by Device control strings (DCS) in the following format:
        /// DCS P XX AA BB CC DD EE ST
        /// where
        /// DCS ESC P(0x50)
        /// P Letter P for PUSH(0x50)
        /// XX two decimal digits giving numbers in the range 01 to 16 to specify the PUSH-key to be loaded.
        /// AA BB CC DD EE for each character to be loaded, the hex value is represented by two ASCII digits
        /// ST string terminator ESC \(0x5C)
        /// </summary>
        internal void ProcessDCS()
        {
            DCS = false;
            // TODO: Implement PUSH key processing
            DCS_string = string.Empty;
        }

        internal void ProcessPM()
        {
            PM = false;
            // TODO: Implement privacy message processing
        }

        /// <summary>
        /// ANSI mode of setting configuration
        /// This mode are part of the broader ANSI X3.64 standard
        /// </summary>
        internal void SetConfiguration(int mode, string intermediateChars, bool mode_enable)
        {
            switch (mode)
            {
                default:
                    Debug.WriteLine($"SetConfiguration: Unknown mode '{mode}', Enable={mode_enable}");
                    break;
            }
        }

        /// <summary>
        /// DECSET - DEC private mode setting
        /// </summary>
        internal void DecSet(int mode, bool mode_enable)
        {
            switch (mode)
            {
                case 40: // TDV 2115 compatibility mode
                    Is2115CompatibilityMode = mode_enable;
                    break;
                case 1: // Application cursor mode
                    IsApplicationCursorMode = mode_enable;
                    break;
                default:
                    Debug.WriteLine($"DecSet: Unknown mode '{mode}', Enable={mode_enable}");
                    break;
            }
        }

        /// <summary>
        /// Reset all configuration to defaults
        /// </summary>
        public void Reset()
        {
            SetDefaults();
        }

        #endregion
    }
}
