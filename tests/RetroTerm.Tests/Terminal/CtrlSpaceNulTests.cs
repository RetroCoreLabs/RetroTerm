using System;
using System.Threading.Tasks;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Ctrl+Space must transmit NUL (0x00) in ALL terminal emulations, and the send
/// pipeline (TerminalSession → connection) must carry the 0x00 byte unmangled.
///
/// The mapping lives in Core (BaseKeyboardMapper / TDV2200KeyboardMapper) so every
/// consumer gets it — the desktop canvas fallback is only a safety net. On the real
/// TDV2200 the spacebar's CTRL variant sends NUL (see TDV2200KeyVisualRegistry A5).
///
/// NOTE on literals: C#'s \x string escape is variable-length — "A\x00B" parses as
/// 'A' + char 0x00B (vertical tab), NOT 'A', NUL, 'B'. All NULs in this file are
/// written as '\0' in single-char context or built explicitly to avoid that trap.
///
/// In the TDV collection because TDV2200KeyboardMapper reads the
/// TDVKeyBindingConfiguration singleton.
/// </summary>
[Collection("TDVKeyBinding")]
public class CtrlSpaceNulTests : IDisposable
{
    private const int VK_SPACE = 32;

    public CtrlSpaceNulTests()
    {
        TDVKeyBindingConfiguration.ResetForTesting();
    }

    public void Dispose()
    {
        TDVKeyBindingConfiguration.ResetForTesting();
    }

    // ─────────────────────────────────────────────────────────────
    // Mapper level: every emulation maps Ctrl+Space → NUL
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VT100")]
    [InlineData("VT220")]
    [InlineData("TDV1200")]
    [InlineData("TDV2215")]
    [InlineData("TDV2200")]
    [InlineData("SomeUnknownEmulator")] // factory fallback path
    public void CtrlSpace_MapsToNul_InAllEmulations(string terminalType)
    {
        var mapper = KeyboardMapperFactory.CreateMapper(terminalType);

        var result = mapper.MapKey(VK_SPACE, KeyModifiers.Ctrl, TerminalModes.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Length);
        Assert.Equal('\0', result[0]);
    }

    [Fact]
    public void CtrlSpace_MapsToNul_InTDV2115Mode()
    {
        // Extended Control Mode OFF (TDV-2115 compatibility) must not change this
        var mapper = new TDV2200KeyboardMapper();

        var result = mapper.MapKey(VK_SPACE, KeyModifiers.Ctrl, TerminalModes.TDV2115Mode);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Length);
        Assert.Equal('\0', result[0]);
    }

    [Fact]
    public void CtrlSpace_MapsToNul_InApplicationCursorKeysMode()
    {
        // DECCKM only rewrites arrow sequences — NUL must pass through untouched
        var mapper = new VT100KeyboardMapper();

        var result = mapper.MapKey(VK_SPACE, KeyModifiers.Ctrl, TerminalModes.ApplicationCursorKeys);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Length);
        Assert.Equal('\0', result[0]);
    }

    [Fact]
    public void CtrlShiftSpace_IsNotNul()
    {
        // Only the exact Ctrl+Space chord sends NUL; extra modifiers do not
        var mapper = new VT100KeyboardMapper();

        var result = mapper.MapKey(VK_SPACE, KeyModifiers.Ctrl | KeyModifiers.Shift, TerminalModes.None);

        Assert.Null(result);
    }

    [Fact]
    public void PlainSpace_IsNotMapped()
    {
        // Bare Space arrives via TextInput as ' ', never through the mapper
        var mapper = new VT100KeyboardMapper();

        var result = mapper.MapKey(VK_SPACE, KeyModifiers.None, TerminalModes.None);

        Assert.Null(result);
    }

    // ─────────────────────────────────────────────────────────────
    // Send pipeline: a 0x00 in the input string reaches the wire
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendInputAsync_TransmitsBareNulByte()
    {
        var emulator = new VT100Emulator(80, 24);
        using var session = new TerminalSession(emulator, "NulTest");
        var connection = new InMemoryConnection();
        await session.ConnectAsync(connection);

        await session.SendInputAsync("\0");

        var sent = connection.GetSentData();
        Assert.Single(sent);
        Assert.Single(sent[0]);
        Assert.Equal(0x00, sent[0][0]);
    }

    [Fact]
    public async Task SendInputAsync_TransmitsEmbeddedNulByte()
    {
        // NUL in the middle of a string must not truncate it (C-string trap)
        var emulator = new VT100Emulator(80, 24);
        using var session = new TerminalSession(emulator, "NulTest");
        var connection = new InMemoryConnection();
        await session.ConnectAsync(connection);

        // Built from chars, not a "\x00" literal — see the class doc note
        await session.SendInputAsync(new string(new[] { 'A', '\0', 'B' }));

        var sent = connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal(new byte[] { 0x41, 0x00, 0x42 }, sent[0]);
    }

    [Fact]
    public async Task SendBytesAsync_TransmitsNulByte()
    {
        // The raw byte path (scripts, MCP terminal_sendraw) must carry 0x00 too
        var emulator = new VT100Emulator(80, 24);
        using var session = new TerminalSession(emulator, "NulTest");
        var connection = new InMemoryConnection();
        await session.ConnectAsync(connection);

        await session.SendBytesAsync(new byte[] { 0x00 });

        var sent = connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal(new byte[] { 0x00 }, sent[0]);
    }

    // ─────────────────────────────────────────────────────────────
    // Escape notation: scripts / MCP terminal_send can express 0x00
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\\x00")] // hex escape (the script dialect's \xHH is FIXED two digits)
    [InlineData("\\0")]   // octal escape
    public void SendEscapeNotation_DecodesToNul(string raw)
    {
        var ok = ScriptStringEscapes.TryDecode(raw, out var decoded, out var error);

        Assert.True(ok, error);
        Assert.Equal(1, decoded.Length);
        Assert.Equal('\0', decoded[0]);
    }
}
