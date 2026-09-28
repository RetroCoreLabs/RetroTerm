using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// Processes input bytes for TDV terminals with TDV-specific mode handling
/// Orchestrates ISO646 variant selection, SS2/SS3, DLE mode, and 2115 compatibility mode
/// </summary>
public class TDVInputProcessor
{
    private readonly TDVEmulatorBase _emulator;
    private readonly TDVISO646VariantHandler _iso646Handler;
    private readonly TDVCharacterSetManager _characterSetManager;
    private readonly TDV2115CompatibilityHandler _compatibilityHandler;

    public TDVInputProcessor(
        TDVEmulatorBase emulator,
        TDVISO646VariantHandler iso646Handler,
        TDVCharacterSetManager characterSetManager,
        TDV2115CompatibilityHandler compatibilityHandler)
    {
        _emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
        _iso646Handler = iso646Handler ?? throw new ArgumentNullException(nameof(iso646Handler));
        _characterSetManager = characterSetManager ?? throw new ArgumentNullException(nameof(characterSetManager));
        _compatibilityHandler = compatibilityHandler ?? throw new ArgumentNullException(nameof(compatibilityHandler));
    }

    /// <summary>
    /// Process input data byte-by-byte with TDV-specific mode handling
    /// </summary>
    public void ProcessInput(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            char c = (char)data[i];

            // DLE coordinate bytes MUST be consumed before anything else looks at them.
            // They are raw binary coordinates (0x80-0xCF for the biased encoding used by
            // SINTRAN terminal type 53), not characters - letting the ISO646 variant handler
            // or a pending single shift interpret them first would corrupt the position.
            if (_compatibilityHandler.HandleDLE((byte)c))
                continue;

            // DLE introducer. Handled here rather than inside ProcessTDV2115ControlCharacter
            // because that method returns early when 2115 compatibility mode is off, which is
            // exactly the case in TDV-native mode where the host still uses DLE addressing.
            // See TDV2115CompatibilityHandler.BeginDLE for the full explanation.
            if ((byte)c == 0x10)
            {
                _compatibilityHandler.BeginDLE();
                continue;
            }

            // Handle ISO646 variant selection
            if (_iso646Handler.HandleVariantSelection(c))
                continue;

            // NOTE: SS2/SS3 single shifts are deliberately NOT intercepted here.
            //
            // This used to call _characterSetManager.HandleSingleShift and write the
            // ALREADY-MAPPED character via ProcessCharacterWithFont. That double-maps: the
            // TDV2200 bitmap renderer selects a glyph from the RAW character plus the cell's
            // FontNumber, so writing a pre-mapped Unicode codepoint produced a cell with the
            // right FontNumber but no drawable glyph - a blank on screen.
            //
            // The emulator's own parser path already handles ESC N / ESC O correctly,
            // including setting FontNumber. This branch was unreachable dead code until
            // TDVEmulatorBase.ProcessData was routed through ProcessInput, at which point it
            // immediately broke SS2 rendering (Screenshot_SS2_GraphicsI). Leaving single
            // shifts to the parser is both correct and simpler.

            // The Tektronix side gets first refusal, but only in Ground - a coordinate byte and an
            // escape sequence's parameter byte look identical, and the sequence in flight wins.
            //
            // This is where a drawing actually happens: GS puts the terminal in graph mode and the
            // bytes that follow are vectors, not text. Offering them to the parser first would
            // print a screenful of punctuation.
            if (_emulator.TryConsumeAsGraphics((byte)c))
            {
                continue;
            }

            // Handle 2115 compatibility mode control characters.
            //
            // ONLY WHEN THE PARSER IS NOT MID-SEQUENCE. A byte that belongs to a sequence already
            // in flight belongs to the PARSER, and stealing it here means the sequence can never
            // complete.
            //
            // The case that exposed this: on a TDV, a bare ENQ (0x05) lights keyboard lamp 1, and
            // it is active in native mode. On a Tektronix 4014 - which an ND graphic terminal also
            // is - ESC ENQ is the identification request. Same byte, two commands, told apart only
            // by whether an ESC came first. This filter runs BEFORE the parser and so cannot see
            // that; it swallowed the ENQ, the identification reply never happened, and a host
            // probing for graphics concluded there was no graphics terminal.
            //
            // Asking the parser for its state is the smallest honest fix. The architecture review
            // (section C.1) names this exact shape as the reason ground modes belong IN the parser
            // rather than in a filter ahead of it.
            if (_emulator.GetParser().State == Parsing.ParserState.Ground
                && _compatibilityHandler.ProcessTDV2115ControlCharacter(c))
            {
                continue;
            }

            // Process through parser directly (bypasses ProcessInput override to avoid recursion)
            // This ensures parser events are dispatched correctly without HandleTDVSequences interference
            // We slice to single byte to handle TDV-specific modes, but the parser maintains state between calls
            _emulator.GetParser().ProcessBytes(data.Slice(i, 1));
        }
    }

    /// <summary>
    /// Reset all handlers to initial state
    /// </summary>
    public void Reset()
    {
        _iso646Handler.Reset();
        _characterSetManager.Reset();
        _compatibilityHandler.Reset();
    }
}

