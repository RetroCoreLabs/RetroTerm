namespace RetroTerm.Core.Protocols.TelnetServer.Utilities;

public enum TerminalType
{
    Unknown,
    VT100,
    VT220,

    // THE VT300 FAMILY AND UP, added 25 August 2026. Their absence was not a small gap: a VT340
    // answered the server's own DA query with a perfectly correct identity, the server had no word
    // for it, and it reported "auto-detection failed" - which reads as a fault in the terminal
    // rather than a missing entry in a list. Two people went looking for a bug in the emulator.
    //
    // Names are the ones DEC's Secondary DA table gives, so the mapping in TestServerApp is a
    // transcription rather than a guess.
    VT240,
    VT320,
    VT330,
    VT340,
    VT382,
    VT420,

    TDV1200,
    TDV2215,
    TDV2200,
    XTerm,
    ANSI
}
