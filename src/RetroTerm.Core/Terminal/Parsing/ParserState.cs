namespace RetroTerm.Core.Terminal.Parsing;

/// <summary>
/// Represents the current state of the escape sequence parser.
///
/// NOT ALL STATES ARE REACHABLE YET. <see cref="CsiIgnore"/>, <see cref="DcsIgnore"/>,
/// <see cref="ApcString"/>, <see cref="PmString"/> and <see cref="SosString"/> are
/// declared but never entered by <see cref="EscapeSequenceParser"/> today: ESC _ / ESC ^
/// / ESC X currently dispatch as ordinary escape sequences and their payload is then
/// printed as text. They are kept because they name real ECMA-48 states the parser needs
/// when string-sequence handling is completed — do not assume a value here means the
/// parser handles that construct.
/// </summary>
public enum ParserState : byte
{
    /// <summary>
    /// Normal text processing (ground state)
    /// </summary>
    Ground,

    /// <summary>
    /// ESC received, waiting for next byte
    /// </summary>
    Escape,

    /// <summary>
    /// ESC [ received (CSI - Control Sequence Introducer)
    /// </summary>
    CsiEntry,

    /// <summary>
    /// CSI intermediate bytes (space to /)
    /// </summary>
    CsiIntermediate,

    /// <summary>
    /// CSI parameter bytes (0-9, ;)
    /// </summary>
    CsiParam,

    /// <summary>
    /// A CSI sequence that has already gone wrong: swallow the rest of it.
    /// </summary>
    /// <remarks>
    /// Entered when a byte turns up somewhere it cannot legally be - a digit after an intermediate,
    /// for instance. Everything up to and including the final byte is thrown away, and NOTHING is
    /// dispatched. Without this state the parser dropped straight back to Ground and the remains of
    /// the sequence were printed as text: "ESC [ * 2 ; CAN D" put a stray semicolon on the screen,
    /// which is what the t0014-CAN fixture caught.
    /// </remarks>
    CsiIgnore,

    /// <summary>
    /// ESC ] received (OSC - Operating System Command)
    /// </summary>
    OscString,

    /// <summary>
    /// ESC P received (DCS - Device Control String)
    /// </summary>
    DcsEntry,

    /// <summary>
    /// DCS parameter bytes
    /// </summary>
    DcsParam,

    /// <summary>
    /// DCS intermediate bytes
    /// </summary>
    DcsIntermediate,

    /// <summary>
    /// DCS pass-through mode
    /// </summary>
    DcsPassthrough,

    /// <summary>
    /// DCS ignore state
    /// </summary>
    DcsIgnore,

    /// <summary>
    /// ESC intermediate byte received (space to /)
    /// </summary>
    EscapeIntermediate,

    /// <summary>
    /// UTF-8 continuation bytes expected
    /// </summary>
    Utf8Sequence,

    /// <summary>
    /// APC - Application Program Command (ESC _)
    /// </summary>
    ApcString,

    /// <summary>
    /// PM - Privacy Message (ESC ^)
    /// </summary>
    PmString,

    /// <summary>
    /// SOS - Start of String (ESC X)
    /// </summary>
    SosString,

    /// <summary>
    /// ESC " received, collecting the parameters of a Norsk Data graphics sequence.
    ///
    /// Only reachable when <see cref="EscapeSequenceParser.NorskDataGraphicsSequences"/> is on.
    /// This is a GROUND MODE, not a fix: <c>ESC " 5 d</c> is perfectly good ECMA-48 the way the
    /// parser reads it by default - 0x22 is an intermediate byte and the first digit is a legal
    /// final - so the default reading is right for every terminal that is not an ND, and only a
    /// profile that knows it is talking to one may change it.
    /// </summary>
    NorskDataParam
}

