using System;

namespace RetroTerm.Core.Terminal.Profiles;

/// <summary>
/// What a terminal can do, as a set of flags.
///
/// These describe what THIS EMULATOR presents to a host, not what the original hardware could do.
/// The two differ on purpose in places — see the notes on the individual profiles.
///
/// Most of the flags are descriptive - they state what is true rather than switching anything on.
/// <see cref="TerminalFeatures.HostResize"/> is the exception and DOES gate behaviour: the base
/// emulator refuses DECCOLM and the xterm text-area commands without it.
///
/// They exist so that the answer to "does this terminal do X" has one home instead of being
/// re-derived from the emulator's class name at each call site, which is how the renderer ended up
/// with TDV type-checks in it.
/// </summary>
[Flags]
public enum TerminalFeatures
{
    None = 0,

    /// <summary>
    /// The 8 ANSI colours and their bright variants (SGR 30-37/40-47/90-97/100-107).
    /// </summary>
    AnsiColour = 1 << 0,

    /// <summary>
    /// The xterm 256-colour palette (SGR 38;5;n / 48;5;n).
    /// </summary>
    Colour256 = 1 << 1,

    /// <summary>
    /// Direct RGB colour (SGR 38;2;r;g;b / 48;2;r;g;b).
    /// </summary>
    TrueColour = 1 << 2,

    /// <summary>
    /// 8-bit C1 controls (0x9B for CSI and friends) as well as the 7-bit ESC forms.
    /// </summary>
    EightBitControls = 1 << 3,

    /// <summary>
    /// DECSTBM top/bottom margins.
    /// </summary>
    ScrollingRegions = 1 << 4,

    /// <summary>
    /// The alternate screen buffer (DECSET 47/1047/1049).
    /// </summary>
    AlternateScreen = 1 << 5,

    /// <summary>
    /// National replacement character sets (ISO 646 variants).
    /// </summary>
    NationalCharacterSets = 1 << 6,

    /// <summary>
    /// Programmable function keys via DCS (DECUDK).
    /// </summary>
    UserDefinedKeys = 1 << 7,

    /// <summary>
    /// Sixel raster graphics (DCS q).
    /// </summary>
    Sixel = 1 << 8,

    /// <summary>
    /// ReGIS vector graphics (DCS p).
    /// </summary>
    ReGIS = 1 << 9,

    /// <summary>
    /// Protected fields / forms handling, as the TDV terminals do.
    /// </summary>
    ProtectedFields = 1 << 10,

    /// <summary>
    /// The host may change the screen size: DECCOLM (80/132 columns) and the xterm text-area
    /// commands <c>CSI 8 t</c> / <c>CSI 18 t</c> / <c>CSI 19 t</c>.
    /// </summary>
    /// <remarks>
    /// A capability rather than something every terminal gets, because most of the terminals here
    /// physically cannot do it. A TDV2200 has one screen, 80 by 25, wired that way; a host telling
    /// it to become 132 columns wide is asking for something the hardware never offered, and
    /// obeying would be an emulator inventing a machine that did not exist.
    /// </remarks>
    HostResize = 1 << 11,

    /// <summary>
    /// Downloadable character shapes via DCS (DECDLD).
    /// </summary>
    SoftCharacterSet = 1 << 12,

    /// <summary>
    /// DECLRMM and DECSLRM: left and right margins.
    /// </summary>
    /// <remarks>
    /// A VT420 capability. It gates behaviour rather than describing it, because with the mode off
    /// <c>CSI s</c> means SCOSC - a terminal that accepted DECSLRM without the capability would
    /// stop being able to save the cursor.
    /// </remarks>
    LeftRightMargins = 1 << 14,

    /// <summary>
    /// The VT420 rectangle operations: DECFRA, DECERA, DECSERA and DECCRA.
    /// </summary>
    /// <remarks>
    /// Gated because they share their final bytes with other sequences and are told apart only by
    /// a '$' intermediate. A terminal that acted on them without having them would be answering
    /// for commands it does not implement.
    /// </remarks>
    RectangleOperations = 1 << 15,

    /// <summary>
    /// Page memory: the terminal holds more lines than it displays, divided into pages.
    /// </summary>
    /// <remarks>
    /// A VT320 and VT420 capability, described in chapter 6 of the VT420 Programmer Reference held
    /// in spec\DEC. Gated because DECSLPP and xterm's window manipulation share the final byte
    /// <c>t</c>: a terminal with page memory reads <c>CSI 24 t</c> as a page length, and one
    /// without reads it as a window command. No terminal is both.
    /// </remarks>
    PageMemory = 1 << 16,

    /// <summary>
    /// DECSCL: the host may tell this terminal to behave as an earlier one.
    /// </summary>
    /// <remarks>
    /// Documented in chapter 4 of the VT420 Programmer Reference held in spec\DEC, which is the
    /// only manual here that states it - so only the VT420 claims it. Gated because a terminal
    /// that accepted DECSCL would be promising to drop to VT100 behaviour on request, and one that
    /// merely ignored the sequence would leave the host believing it had.
    /// </remarks>
    ConformanceLevels = 1 << 17,

    /// <summary>
    /// DECIC and DECDC: inserting and deleting whole COLUMNS of the scrolling region.
    /// </summary>
    /// <remarks>
    /// "Available in: VT400 mode only" says the VT420 Programmer Reference, chapter 8; xterm
    /// implements them too and its ctlseqs.txt marks them "VT420 and up". Both documents are held
    /// in spec\DEC and agree on the syntax, so the flag goes to the VT420 and to xterm.
    /// </remarks>
    ColumnEditing = 1 << 18,

    /// <summary>
    /// DECSASD and DECSSDT: the twenty-fifth line, either the terminal's own indicator or a line
    /// the host writes into.
    /// </summary>
    /// <remarks>
    /// "Available in: VT300 mode only" says the VT330/VT340 Text Programming manual, chapter 11;
    /// xterm's ctlseqs.txt marks both sequences "VT320 and up". So the flag goes to the VT320, the
    /// VT330/VT340 and the VT420, and to nothing earlier.
    /// </remarks>
    StatusLine = 1 << 19,

    /// <summary>
    /// A Sixel image's colour definitions also set the terminal's TEXT colours, by the order they
    /// were defined in.
    /// </summary>
    /// <remarks>
    /// <para><b>The VT340's peculiar ordering scheme</b></para>
    /// The sixth colour an image defines becomes the text foreground and the sixteenth becomes the
    /// text background - by position in the image, not by the register number each was given. It
    /// reads like a bug and it is what the hardware does.
    ///
    /// <para><b>Why only this model claims it</b></para>
    /// The evidence is hackerb9's corpus, which is a capture from a real VT340: <c>cat-vt340.six</c>
    /// defines exactly sixteen colours in an order unrelated to their registers, documents the rule
    /// in its own comment, and the photograph beside it shows the whole screen in the sixteenth
    /// colour defined. No DEC manual held here states it, so it is claimed by the model it was
    /// observed on and by nothing else. xterm and libsixel support Sixel and do NOT do this.
    /// </remarks>
    ImageColoursSetTextColours = 1 << 20,

    /// <summary>
    /// Receiving a Sixel image resets every line back to single width.
    /// </summary>
    /// <remarks>
    /// Measured, not derived. hackerb9's two decdwl captures both state it, and the second exists
    /// only to show it: "The VT340 resets all line attributes to single-width when a sixel image is
    /// received. This leads to a quirk where an image indented by a double-width line suddenly
    /// reverts to single-width indentation."
    /// Claimed by the model it was observed on and by nothing else, for the same reason
    /// <see cref="ImageColoursSetTextColours"/> is: no DEC manual held here states it, and xterm and
    /// libsixel support Sixel without doing it.
    /// The captures also say the VT340 clears the underlying TEXT buffer at the same time. That half
    /// is deliberately not implemented - see the remarks where this is honoured.
    /// </remarks>
    ImageResetsLineAttributes = 1 << 21,

    /// <summary>
    /// When a Sixel image ends, the text cursor is set to the SIXEL cursor position rather than to
    /// the row below the pixels that were drawn.
    /// </summary>
    /// <remarks>
    /// <para><b>What the manual says</b></para>
    /// VT330/VT340 Programmer Reference, with sixel scrolling enabled: "when sixel mode is exited,
    /// the text cursor is set to the current sixel cursor position". The sixel cursor moves only on
    /// a graphics new line, <c>-</c>. Painting pixels does not move it and neither does <c>$</c>.
    ///
    /// <para><b>Why it is a flag and not simply the rule</b></para>
    /// The two readings agree whenever an image ends with a <c>-</c> and one screen row is one pixel
    /// row, which is nearly every image ever sent. They part company on an image that does NOT end
    /// with one, and a great many encoders do not - so applying DEC's rule everywhere would make
    /// ordinary output from ordinary programs draw on top of itself.
    ///
    /// <para><b>The evidence for the model that claims it</b></para>
    /// hackerb9's <c>extremeratio.six</c>, captured from a real VT340. It declares an 80:1 aspect
    /// ratio, contains no graphics new line at all, and paints 480 screen rows from a single band.
    /// Its own closing comment reads "A VT340 leaves the text cursor at the top of the screen".
    /// Counting pixel rows instead drove the cursor 24 rows to the bottom.
    ///
    /// Claimed by the model it was observed on and by nothing else, exactly as
    /// <see cref="ImageColoursSetTextColours"/> and <see cref="ImageResetsLineAttributes"/> are.
    /// xterm and libsixel move the cursor below the image, and the profiles that model them keep
    /// doing so.
    /// </remarks>
    SixelExitCursorFollowsTheSixelCursor = 1 << 22,

    /// <summary>
    /// DECANM: the host may drop this terminal into VT52 mode with <c>CSI ? 2 l</c>.
    /// </summary>
    /// <remarks>
    /// A VT-family capability. The TDV terminals never had it, and the VT52 itself has nothing to
    /// drop into - it is already one.
    /// </remarks>
    Vt52Mode = 1 << 13,
}

/// <summary>
/// How typed text is turned into bytes on the wire for a given terminal.
///
/// This is NOT a display question — it is what the host at the other end reads. Every session used
/// to send UTF-8, which is right for a modern host behind an ANSI/VT session and wrong for an ND
/// host behind a TDV one: a TDV line is 8-bit, so one typed character must be one byte. Sending
/// UTF-8 there puts TWO bytes on the line for anything above 0x7F, and the host reads two garbage
/// characters.
/// </summary>
public enum TransportEncoding
{
    /// <summary>
    /// One character, one byte. Characters above 0xFF cannot be represented and are replaced with
    /// '?' — the same substitution <c>Encoding.ASCII</c> makes, and visible rather than silent.
    /// This is the vintage-hardware case: TDV, and any other 8-bit line.
    /// </summary>
    EightBit,

    /// <summary>
    /// UTF-8. The right answer for a modern host reached through an ANSI/VT session.
    /// </summary>
    Utf8,
}

/// <summary>
/// The identity of a terminal: what it calls itself, what it answers to a Device Attributes
/// request, which DEC private modes it recognises, and what it can do.
///
/// WHY THIS EXISTS. TerminalEmulatorBase secretly *was* the VT100 — the DA reply was a VT100 reply
/// hardcoded in the base, VT100Emulator was twenty lines that overrode ToString, and the factory
/// answered a request for a "VT220" with a VT100. With ten to fifteen terminals planned, the thing
/// that distinguishes one from another needs somewhere to live that is not a subclass override.
/// This is that place: a profile is data, so a new terminal that differs only in identity and
/// capabilities does not need a new class at all.
///
/// A profile is immutable and shared — one instance per terminal type, not one per session.
/// </summary>
public sealed class TerminalProfile
{
    /// <summary>
    /// What the terminal calls itself, e.g. "VT100".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The complete reply to a Primary DA request (CSI c), introducer and final byte included.
    /// </summary>
    public byte[] PrimaryDeviceAttributes { get; }

    /// <summary>
    /// The complete reply to a Secondary DA request (CSI > c).
    /// </summary>
    public byte[] SecondaryDeviceAttributes { get; }

    /// <summary>
    /// What this terminal can do.
    /// </summary>
    public TerminalFeatures Features { get; }

    /// <summary>
    /// Which keyboard layout this terminal types on.
    ///
    /// Separate from <see cref="Name"/> on purpose: several terminals can share one keyboard while
    /// differing in everything else, and a terminal's identity should not have to be its keyboard's
    /// identity. It also replaces keying the mapper off the emulator's CLASS NAME, which meant a
    /// rename silently downgraded a session's keyboard to VT100.
    /// </summary>
    public string KeyboardLayout { get; }

    /// <summary>
    /// The DEC private mode numbers this terminal recognises, whether or not they are currently
    /// set. A host asking about anything outside this set is told the mode is not recognised
    /// rather than being given a misleading "reset".
    /// </summary>
    private readonly int[] _recognisedPrivateModes;

    /// <summary>
    /// How typed text becomes bytes on the wire for this terminal. See <see cref="TransportEncoding"/>.
    /// </summary>
    public TransportEncoding TransportEncoding { get; }

    /// <summary>
    /// The screen this terminal has, in columns.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the size lives here</b></para>
    /// Until 27 September 2026 the only place that knew a TDV2200 has 25 rows was a switch in
    /// EmulatorFactory, and only ONE of the nine places that pick a terminal type asked it. Every
    /// other path (File, New Tab As; Quick Connect; the MCP terminal_open tool; CONNSAVE; the sample
    /// connections written on first run) built a TDV at 80 by 24, so row 25 of PED was simply
    /// missing. The profile is the one object every path already holds, so the number lives on it
    /// and the factory reads it from here. For a terminal that follows the window
    /// (<see cref="TerminalFeatures.HostResize"/>) this is only the size it starts at.
    /// </remarks>
    public int Columns { get; }

    /// <summary>
    /// The screen this terminal has, in rows. See <see cref="Columns"/> for why it is here.
    /// </summary>
    public int Rows { get; }

    /// <summary>
    /// Whether the Backspace key sends DEL (0x7F) on this terminal unless the connection says
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// A connection can still override it either way; this is what "the terminal's default" means
    /// when the connection has not chosen. True for the TDV terminals: a SINTRAN line expects DEL
    /// for rubout, and Ronny asked on 27 September 2026 that every TDV send it without having to be
    /// told per connection. False for the VT family, which is what this program always sent.
    /// </remarks>
    public bool BackspaceSendsDel { get; }

    /// <summary>
    /// Describes one terminal's identity and capabilities.
    /// </summary>
    /// <param name="name">
    /// The terminal's name, e.g. "VT220" or "TDV2200".
    /// </param>
    /// <param name="primaryDeviceAttributes">
    /// The bytes sent in reply to a primary DA query.
    /// </param>
    /// <param name="secondaryDeviceAttributes">
    /// The bytes sent in reply to a secondary DA query.
    /// </param>
    /// <param name="features">
    /// Which optional capabilities this terminal has.
    /// </param>
    /// <param name="recognisedPrivateModes">
    /// The DEC private mode numbers this terminal recognises, whether or not they are set.
    /// </param>
    /// <param name="keyboardLayout">
    /// Which keyboard layout to type on. Defaults to the profile's own name, which is right
    /// whenever a terminal has a keyboard of its own; pass it explicitly when a terminal borrows
    /// another's keyboard.
    /// </param>
    /// <param name="transportEncoding">
    /// How typed text becomes bytes on the wire. TDV terminals are 8-bit ISO 646, not Unicode.
    /// </param>
    /// <param name="columns">
    /// The terminal's screen width in character cells. 80 unless the hardware says otherwise.
    /// </param>
    /// <param name="rows">
    /// The terminal's screen height in rows. 24 for the VT family, 25 for the TDV family.
    /// </param>
    /// <param name="backspaceSendsDel">
    /// Whether Backspace sends DEL rather than BS when the connection has not chosen.
    /// </param>
    public TerminalProfile(
        string name,
        byte[] primaryDeviceAttributes,
        byte[] secondaryDeviceAttributes,
        TerminalFeatures features,
        int[] recognisedPrivateModes,
        string? keyboardLayout = null,
        TransportEncoding transportEncoding = TransportEncoding.Utf8,
        int columns = 80,
        int rows = 24,
        bool backspaceSendsDel = false)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        Columns = columns;
        Rows = rows;
        BackspaceSendsDel = backspaceSendsDel;
        TransportEncoding = transportEncoding;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        PrimaryDeviceAttributes = primaryDeviceAttributes ?? throw new ArgumentNullException(nameof(primaryDeviceAttributes));
        SecondaryDeviceAttributes = secondaryDeviceAttributes ?? throw new ArgumentNullException(nameof(secondaryDeviceAttributes));
        Features = features;
        _recognisedPrivateModes = recognisedPrivateModes ?? throw new ArgumentNullException(nameof(recognisedPrivateModes));
        KeyboardLayout = keyboardLayout ?? Name;
    }

    /// <summary>
    /// Whether this terminal offers the given feature.
    /// </summary>
    public bool Supports(TerminalFeatures feature) => (Features & feature) == feature;

    /// <summary>
    /// Whether this terminal knows what the given DEC private mode is. A linear scan is right
    /// here: the lists are short, this is not a hot path, and an array beats a HashSet for both
    /// allocation and cache behaviour at this size.
    /// </summary>
    public bool RecognisesPrivateMode(int mode)
    {
        for (int i = 0; i < _recognisedPrivateModes.Length; i++)
        {
            if (_recognisedPrivateModes[i] == mode) return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    // The built-in profiles
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The DEC private modes the base emulator implements. Shared by the VT-family profiles so
    /// the list cannot drift away from the switch that actually handles them.
    /// </summary>
    private static readonly int[] BasePrivateModes =
        { 1, 2, 3, 6, 7, 9, 12, 25, 47, 69, 1000, 1002, 1003, 1004, 1006, 1047, 1048, 1049, 2004 };

    /// <summary>
    /// VT100 as this emulator presents it.
    ///
    /// NOTE ON AUTHENTICITY: a real VT100 had no colour at all — SGR colour arrived with later
    /// terminals — but this emulator implements it, and the flag describes the emulator. Nothing
    /// currently gates on the flag, so this is a statement of fact rather than a decision to turn
    /// anything on or off. A strict-hardware profile can be added beside this one if authentic
    /// behaviour is ever wanted; that is a deliberate choice for someone to make, not something to
    /// settle silently here.
    /// </summary>
    public static readonly TerminalProfile VT100 = new(
        "VT100",
        // VT100 with Advanced Video Option
        "\x1b[?1;2c"u8.ToArray(),
        // Terminal id 0 (VT100 family), firmware version 10, no ROM cartridge
        "\x1b[>0;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.Vt52Mode,
        BasePrivateModes);

    /// <summary>
    /// A generic ANSI/ECMA-48 terminal.
    ///
    /// "ANSI" is not one precisely defined physical terminal — it is a family of behaviours — so
    /// this profile claims only what the base emulator genuinely implements, and identifies itself
    /// as a VT100-class device because that is the identity a host can rely on from it.
    /// </summary>
    public static readonly TerminalProfile Ansi = new(
        "ANSI",
        "\x1b[?1;2c"u8.ToArray(),
        "\x1b[>0;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.HostResize,
        BasePrivateModes,
        // An ANSI terminal types on a VT100 keyboard. Stated rather than arrived at: this used to
        // happen only because the mapper factory silently fell back to VT100 for every name it did
        // not recognise, and the ANSI emulator's class name was not one it recognised.
        keyboardLayout: "VT100");

    /// <summary>
    /// The DEC VT220.
    /// </summary>
    /// <remarks>
    /// <para><b>The DA reply lists extensions, which is what makes this honest</b></para>
    /// A primary DA answer is a family number followed by the extensions the terminal has, so a
    /// terminal is not forced to choose between claiming everything and claiming nothing. 62 is the
    /// VT200 family; 1 is 132 columns, 6 is selective erase, 7 is a downloadable character set and
    /// 8 is user-defined keys. All four are really implemented - DECCOLM resizes the screen, DECSCA
    /// with DECSED and DECSEL spares protected text, DECDLD downloads character shapes and the
    /// renderer draws them, and DECUDK loads the function keys and the keyboard sends what it
    /// loaded. What is NOT in the list is as deliberate: no 2 (printer port) and no 9 (national
    /// replacement character sets). A host reads the list and sends only what is on it.
    ///
    /// <para><b>What this replaced, and in what order</b></para>
    /// Asking for a VT220 used to build a VT100 answering as a VT100, with a comment explaining
    /// that a truthful VT100 beat a VT220 that could not live up to the name. That was right at the
    /// time. Each extension was added to this list only once it worked - 8 last, and only after the
    /// keyboard could actually send a defined key, because storing a definition nothing presses
    /// would have made the claim false.
    /// </remarks>
    public static readonly TerminalProfile VT220 = new(
        "VT220",
        // VT200 family: 132 columns, selective erase, soft character set, user-defined keys.
        "\x1b[?62;1;6;7;8c"u8.ToArray(),
        // Terminal id 1 (VT220), firmware version 10, no ROM cartridge.
        "\x1b[>1;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.UserDefinedKeys | TerminalFeatures.SoftCharacterSet
            | TerminalFeatures.Vt52Mode,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// The DEC VT52 - the terminal that came before ANSI.
    /// </summary>
    /// <remarks>
    /// <para><b>It answers ESC / Z, and that is all</b></para>
    /// Device Attributes did not exist yet. A host identifies a VT52 by sending <c>ESC Z</c> and
    /// reading back <c>ESC / Z</c>, so the DA arrays here are empty and the terminal stays silent
    /// when probed with <c>CSI c</c> - the same choice the Tektronix 4014 profile makes, for the
    /// same reason.
    ///
    /// <para><b>It claims nothing</b></para>
    /// No colour, no scrolling regions, no alternate screen, no private modes. A VT52 had none of
    /// them, and an emulator that offered them would be a different machine wearing the name.
    /// </remarks>
    public static readonly TerminalProfile VT52 = new(
        "VT52",
        Array.Empty<byte>(),
        Array.Empty<byte>(),
        TerminalFeatures.None,
        Array.Empty<int>(),
        keyboardLayout: "VT100");

    /// <summary>
    /// The DEC VT102 - a VT100 that can insert and delete.
    /// </summary>
    /// <remarks>
    /// <para><b>What separates it from a VT100</b></para>
    /// Insert and delete line, insert and delete character - IL, DL, ICH and DCH. All four are
    /// implemented in the base emulator, so this profile is the identity rather than new behaviour:
    /// it answers the DA a VT102 answers, and a host that recognises it will use the editing
    /// sequences instead of redrawing whole lines.
    ///
    /// <para><b>The secondary DA is empty, and that is a judgement call</b></para>
    /// I could not confirm from any source here whether a VT102 answered <c>CSI > c</c> at all -
    /// secondary DA is a later addition, but exactly how late is not something this repository can
    /// settle. Silence is the safer of the two wrong answers: a host that gets no reply falls back,
    /// while a host given a made-up identity acts on it.
    /// </remarks>
    public static readonly TerminalProfile VT102 = new(
        "VT102",
        // The VT102's own answer. Not a list of extensions - that convention came later.
        "\x1b[?6c"u8.ToArray(),
        Array.Empty<byte>(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.HostResize | TerminalFeatures.Vt52Mode,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// The DEC VT420 - the VT400 series, with left and right margins.
    /// </summary>
    /// <remarks>
    /// <para><b>What it adds here</b></para>
    /// DECLRMM and DECSLRM: a host can confine text to a column range, and it wraps at the right
    /// margin back to the LEFT margin rather than to the edge of the screen. That is the one thing
    /// the VT400 series brought that this emulator did not already have.
    ///
    /// <para><b>What is confined</b></para>
    /// Printing, wrapping AND editing. Insert and delete character stop at the margins rather than
    /// spilling into the columns beside them, and insert and delete line move only the columns
    /// INSIDE the region - which is the entire reason a program sets margins, since it is drawing a
    /// panel and does not want the rest of the screen following it around.
    ///
    /// <para><b>The rectangle operations</b></para>
    /// The whole family: DECFRA fill, DECERA erase, DECSERA selective erase, DECCRA copy, DECCARA
    /// and DECRARA for attributes, and DECSACE to choose whether those two read their coordinates
    /// as a rectangle or as a stream. Not implemented: page memory, so DECCRA's page parameters are
    /// ignored, and the DA list still names only the extensions that work.
    /// </remarks>
    public static readonly TerminalProfile VT420 = new(
        "VT420",
        // VT400 family: 132 columns, selective erase, soft character set, user-defined keys.
        "\x1b[?64;1;6;7;8c"u8.ToArray(),
        // Terminal id 41 (VT420), firmware version 10, no ROM cartridge.
        "\x1b[>41;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.UserDefinedKeys | TerminalFeatures.SoftCharacterSet
            | TerminalFeatures.Vt52Mode | TerminalFeatures.LeftRightMargins
            | TerminalFeatures.RectangleOperations | TerminalFeatures.PageMemory
            | TerminalFeatures.ConformanceLevels | TerminalFeatures.ColumnEditing
            | TerminalFeatures.StatusLine,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// The DEC VT240 - a VT220 with ReGIS graphics, on a monochrome screen.
    /// </summary>
    /// <remarks>
    /// <para><b>What it claims</b></para>
    /// 62 is the VT200 family, the same as the VT220, plus 3 for ReGIS. The rest are the
    /// extensions already proven on the VT220: 132 columns, selective erase, downloadable character
    /// sets, user-defined keys.
    ///
    /// <para><b>Sixel is deliberately absent</b></para>
    /// Whether a VT240 does Sixel is something I could not confirm from any source in this
    /// repository, and the DA reply is not the place to guess. The VT340 claims Sixel because that
    /// machine certainly had it. If a VT240 manual turns up saying otherwise, adding 4 here is a
    /// one-line change.
    ///
    /// <para><b>The screen was monochrome, and now that means something</b></para>
    /// Pair this profile with a single-phosphor theme and the ReGIS drawing collapses to the
    /// phosphor along with the text, because a one-gun screen could not have done anything else.
    /// The colour themes leave it in colour, which is this emulator being more capable than the
    /// hardware rather than pretending otherwise.
    /// </remarks>
    public static readonly TerminalProfile VT240 = new(
        "VT240",
        // VT200 family: 132 columns, ReGIS, Sixel, selective erase, soft character set,
        // user-defined keys.
        //
        // THE 4 IS DOCUMENTED, not assumed. The VT330/VT340 Text Programming manual lists the
        // alias replies a VT300 sends when told to identify as an earlier terminal, and its VT240
        // line is "CSI ? 62; 1; 2; 3; 4; 6; 7; 8; 9 c" - a real VT240 does Sixel as well as ReGIS.
        // This profile claimed only ReGIS, so a host asking "can you do Sixel?" was told no, and
        // img2sixel would not send an image to a terminal that can draw one.
        //
        // The 2 and the 9 stay out: there is no printer port here and no national replacement
        // character sets, and this list only ever grows once the thing behind it works.
        "\x1b[?62;1;3;4;6;7;8c"u8.ToArray(),
        // Terminal id 2 (VT240), firmware version 10, no ROM cartridge.
        "\x1b[>2;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.UserDefinedKeys | TerminalFeatures.SoftCharacterSet
            | TerminalFeatures.ReGIS | TerminalFeatures.Sixel | TerminalFeatures.Vt52Mode
            // Same DEC rule as the VT340. DERIVED, not measured: the hardware evidence
            // (extremeratio.six) is a VT340 capture, but both terminals implement the same
            // DEC sixel specification and splitting them would need evidence, not a hunch.
            | TerminalFeatures.SixelExitCursorFollowsTheSixelCursor,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// The DEC VT320 - the VT300-series text terminal, with no graphics.
    /// </summary>
    /// <remarks>
    /// <para><b>The identity is documented, not inferred</b></para>
    /// The VT330/VT340 Programmer Reference (EK-VT3XX-TP-001), chapter on Device Attributes, gives
    /// the service class codes as 61 for level 1 (VT100 family) and 63 for level 3 (VT200 or VT300
    /// family), and the extension numbers as 1 for 132 columns, 2 printer port, 3 ReGIS, 4 Sixel,
    /// 6 selective erase, 7 soft character set, 8 user-defined keys, 9 national replacement sets.
    /// So 63 is the family this terminal belongs to, from DEC's own table.
    ///
    /// <para><b>What it claims is what it has</b></para>
    /// 132 columns, selective erase, the downloadable character set and user-defined keys - all
    /// four are implemented and tested here. The printer port (2) and the national replacement
    /// sets (9) are NOT claimed, because they are not built. Neither is ReGIS or Sixel: those are
    /// what separate the VT330 and VT340 from this machine, and a VT320 with graphics would be a
    /// different terminal.
    ///
    /// <para><b>Page memory</b></para>
    /// A VT320 has it, and so does this profile - the movement functions were built from the VT420
    /// manual, which documents the same feature for the whole VT300 and VT400 line.
    /// </remarks>
    public static readonly TerminalProfile VT320 = new(
        "VT320",
        // Service class 63 (VT300 family): 132 columns, selective erase, DRCS, UDKs.
        "\x1b[?63;1;6;7;8c"u8.ToArray(),
        // Terminal id 24 (VT320), firmware version 10, no ROM cartridge.
        "\x1b[>24;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.UserDefinedKeys | TerminalFeatures.SoftCharacterSet
            | TerminalFeatures.Vt52Mode | TerminalFeatures.PageMemory
            | TerminalFeatures.StatusLine,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// The DEC VT340 - the VT300-series terminal with Sixel graphics.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this terminal exists here now</b></para>
    /// Sixel is the VT340's defining feature, so a Sixel implementation belongs to a terminal that
    /// says it is one. Putting it on the VT220 instead would have been a lie about a machine that
    /// never had it.
    ///
    /// <para><b>What it claims</b></para>
    /// 63 is the VT300 family. The extensions are the ones that really work: 1 for 132 columns,
    /// 3 for ReGIS, 4 for Sixel, 6 for selective erase, 7 for downloadable character sets, 8 for
    /// user-defined keys. Absent on purpose: 2 (printer port) and 9 (national replacement character
    /// sets), neither of which exists here.
    ///
    /// ReGIS was added to this list last, and only once a drawing actually reached the screen. The
    /// commands that draw are implemented; the rest are counted rather than guessed at, and that
    /// counter is readable through the emulator.
    /// </remarks>
    public static readonly TerminalProfile VT340 = new(
        "VT340",
        // VT300 family: 132 columns, ReGIS, Sixel, selective erase, soft character set,
        // user-defined keys.
        "\x1b[?63;1;3;4;6;7;8c"u8.ToArray(),
        // Terminal id 19 (VT340), firmware version 10, no ROM cartridge.
        "\x1b[>19;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.UserDefinedKeys | TerminalFeatures.SoftCharacterSet
            | TerminalFeatures.Sixel | TerminalFeatures.ReGIS | TerminalFeatures.Vt52Mode
            | TerminalFeatures.StatusLine | TerminalFeatures.ImageColoursSetTextColours
            | TerminalFeatures.ImageResetsLineAttributes
            | TerminalFeatures.SixelExitCursorFollowsTheSixelCursor,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// xterm, as this emulator presents it.
    /// </summary>
    /// <remarks>
    /// <para><b>What it claims, and what it does not</b></para>
    /// The device attributes are the VT100-with-advanced-video ones, NOT the VT420-class string a
    /// modern xterm answers with. A real xterm's secondary DA begins 41, which tells a host it may
    /// send left and right margins and the rectangle operations - none of which exist here. So this
    /// profile identifies as what it can actually do, and earns the name "xterm" through the
    /// extensions that ARE implemented: 256-colour and true colour, the alternate screen, bracketed
    /// paste, focus reporting, the mouse tracking modes with the SGR encoding, the OSC colour
    /// queries, and host-commanded resize.
    ///
    /// <para><b>Why the name matters more than the DA string</b></para>
    /// It goes out over TERMINAL-TYPE negotiation and becomes TERM on the host, which is what picks
    /// the terminfo entry - so the name has to be one a host knows, in the case it knows it in.
    /// </remarks>
    public static readonly TerminalProfile Xterm = new(
        "xterm",
        "\x1b[?1;2c"u8.ToArray(),
        "\x1b[>0;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.Vt52Mode | TerminalFeatures.LeftRightMargins
            | TerminalFeatures.RectangleOperations | TerminalFeatures.ColumnEditing,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// xterm announcing 256-colour support in its name.
    /// </summary>
    /// <remarks>
    /// Identical to <see cref="Xterm"/> in everything the terminal does - the palette has always
    /// been there. The difference is the name on the wire: a host reading TERM=xterm-256color
    /// picks a terminfo entry that says 256 colours are available, and programs that check it
    /// (vim, tmux, anything using ncurses) then use them instead of falling back to eight.
    /// </remarks>
    public static readonly TerminalProfile Xterm256 = new(
        "xterm-256color",
        "\x1b[?1;2c"u8.ToArray(),
        "\x1b[>0;10;0c"u8.ToArray(),
        TerminalFeatures.AnsiColour | TerminalFeatures.Colour256 | TerminalFeatures.TrueColour
            | TerminalFeatures.ScrollingRegions | TerminalFeatures.AlternateScreen
            | TerminalFeatures.EightBitControls | TerminalFeatures.HostResize
            | TerminalFeatures.Vt52Mode | TerminalFeatures.LeftRightMargins
            | TerminalFeatures.RectangleOperations | TerminalFeatures.ColumnEditing,
        BasePrivateModes,
        keyboardLayout: "VT100");

    /// <summary>
    /// The Tektronix 4014 storage-tube graphics terminal.
    /// </summary>
    /// <remarks>
    /// <para><b>It answers nothing, and that is correct</b></para>
    /// A 4014 is from 1974 and predates ANSI device attributes entirely. It has no CSI, no private
    /// modes, no DA reply - a host identifies it by sending <c>ESC ENQ</c> and reading back the
    /// five-byte status-and-position report. So the DA arrays here are empty and the emulator stays
    /// silent when probed, rather than borrowing a VT100 answer it has no right to.
    ///
    /// <para><b>Eight-bit line</b></para>
    /// Same reason as the TDV terminals: one typed character is one byte on the wire. Nothing on
    /// the far end of a 4014 line has ever read UTF-8.
    /// </remarks>
    public static readonly TerminalProfile Tek4014 = new(
        "TEK4014",
        Array.Empty<byte>(),
        Array.Empty<byte>(),
        TerminalFeatures.None,
        Array.Empty<int>(),
        keyboardLayout: "VT100",
        transportEncoding: TransportEncoding.EightBit,
        // 74 by 35 is the 4014's own text geometry at its default character size.
        columns: Emulators.Tektronix.Tek4014Emulator.DefaultColumns,
        rows: Emulators.Tektronix.Tek4014Emulator.DefaultRows);

    /// <summary>
    /// Builds a profile for one of the TDV/ND terminals.
    ///
    /// The TDV emulators answer DA through their own query/response path (TDVEmulatorBase handles
    /// 'c' itself and never reaches the base), so the DA bytes here are what the profile *says*
    /// the terminal is, used for identification rather than for replying. They are kept in step
    /// with the TDV reply builders by TdvProfileMatchesEmulatorReplies in the test suite.
    /// </summary>
    public static TerminalProfile ForTdv(string name, string primaryDa, TerminalFeatures extra = TerminalFeatures.None)
    {
        return new TerminalProfile(
            name,
            System.Text.Encoding.ASCII.GetBytes(primaryDa),
            System.Text.Encoding.ASCII.GetBytes(primaryDa),
            TerminalFeatures.ProtectedFields | TerminalFeatures.NationalCharacterSets
                | TerminalFeatures.ScrollingRegions | extra,
            BasePrivateModes,
            keyboardLayout: null,
            // A TDV line is 8-bit. One typed character must leave as one byte, or an ND host reads
            // two garbage characters for every national character typed.
            transportEncoding: TransportEncoding.EightBit,
            // 80 by 25 for the whole family. TDV2200/9: "This affects all 25 lines on the screen"
            // (spec\TDV2200\OCR\TDV-2200_9-User-s_Guide-ND_combined.md). TDV2215: "the maximum
            // of 25 lines possible on the screen" (spec\TDV2215\TDV2215.md) and the ND 242 product
            // sheet, "displays 25 lines of 80 characters". TDV1200: no sentence in its manuals
            // states the count outright; the closest is the notepad description, "only one
            // screen picture in size (25 lines)" (spec\TDV1200\ND-12045-2-EN_combined.md), and a
            // screen example in the same guide that ends at LINE 25. Until 27 September 2026 the
            // factory said 24 for the TDV1200 with no citation at all.
            columns: 80,
            rows: 25,
            // A SINTRAN line wants DEL for rubout. Asked for by Ronny, 27 September 2026.
            backspaceSendsDel: true);
    }

    /// <summary>
    /// Turns typed text into the bytes this terminal puts on the wire.
    ///
    /// WHY THIS IS ON THE PROFILE. TerminalSession used to call Encoding.UTF8.GetBytes for every
    /// session regardless of terminal, so a TDV session sent two bytes for anything above 0x7F.
    /// The national characters were already handled upstream by the ISO 646 wire conversion, which
    /// hid the problem for the characters people type most — but everything else still went out
    /// doubled, and the fix belonged with the terminal's identity rather than at the send call.
    /// </summary>
    public byte[] EncodeForTransport(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (text.Length == 0) return Array.Empty<byte>();

        if (TransportEncoding == TransportEncoding.Utf8)
            return System.Text.Encoding.UTF8.GetBytes(text);

        // 8-bit: one char, one byte. Explicit loop rather than Encoding.Latin1, which does not
        // exist on netstandard2.1 — and this is the same one-for-one conversion TDVEmulatorBase
        // already does on its reply path.
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            // Above 0xFF there is no byte to send. '?' is what Encoding.ASCII substitutes, and a
            // visible wrong character beats a silently vanished one. A surrogate pair becomes two
            // '?' for the same reason: no 8-bit line can carry it either way.
            bytes[i] = c <= 0xFF ? (byte)c : (byte)'?';
        }
        return bytes;
    }
}
