using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// 2115 compatibility mode is THE SAME on every TDV model. These tests pin that down:
/// each case runs the identical byte stream against TDV1200, TDV2215 and TDV2200 and
/// asserts the same outcome.
///
/// Before this was unified, TDV1200 alone routed CSI sequences through a stub class
/// (TDV2115CompatibilityMode) that returned "handled" for A/B/C/D/H/J/K while doing
/// nothing, so entering 2115 mode silently disabled all cursor movement and erase on
/// that model only. It also used mode number 66 while the others used 40.
/// </summary>
public class TDV2115ModeParityTests
{
    /// <summary>
    /// Builds one emulator of each TDV model, all the same size.
    /// </summary>
    private static TDVEmulatorBase[] AllModels()
    {
        return new TDVEmulatorBase[]
        {
            new TDV1200Emulator(80, 24),
            new TDV2215Emulator(80, 24),
            new TDV2200Emulator(80, 24)
        };
    }

    /// <summary>
    /// Reads the model's 2115 flag through whichever concrete type it is.
    /// </summary>
    private static bool Is2115(TDVEmulatorBase emulator)
    {
        return emulator switch
        {
            TDV1200Emulator e => e.Is2115CompatibilityMode,
            TDV2215Emulator e => e.Is2115CompatibilityMode,
            TDV2200Emulator e => e.Is2115CompatibilityMode,
            _ => throw new ArgumentException($"Unknown TDV model {emulator.GetType().Name}")
        };
    }

    private static void Send(TDVEmulatorBase emulator, string sequence)
    {
        emulator.ProcessInput(Encoding.ASCII.GetBytes(sequence));
    }

    // ─────────────────────────────────────────────────────────────
    // Entering and leaving the mode
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// There is exactly ONE spelling, and it is the reverse of what this file used to assert.
    /// </summary>
    /// <remarks>
    /// This theory carried four rows until 11 September 2026 - <c>CSI ? 40 h</c>, <c>CSI 40 h</c>,
    /// <c>CSI ? 66 h</c> and <c>CSI 66 h</c> - all entering 2115 mode. Every one of them was wrong.
    ///
    /// Mode 40 is PCF, the printer code format (TDV 2215 Functional Specifications section 8.7.1),
    /// and does not exist on the ND Display Terminal 1200 at all. The private marker does not
    /// belong: these are ANSI modes. And the polarity is inverted - RESET of mode 66 is the 2115
    /// side, because 66 is the Extended Control switch and section 3.1 says "When this switch is
    /// set to OFF, the terminal works like a TDV 2115 from the host computer's point of view."
    /// ND-1200 section 8.1 spells the same thing out: enter with <c>CSI 66 l</c>, leave with
    /// <c>ESC Q</c>.
    ///
    /// A real TDV2200 termcap proves it without needing either manual: its init string is
    /// <c>ESC [ 62;36;66 l</c> followed IMMEDIATELY by <c>ESC Q</c>, and ESC Q is only ever the way
    /// out of 2115 mode.
    ///
    /// See <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    /// <param name="enable">
    /// The sequence that enters 2115 mode.
    /// </param>
    /// <param name="disable">
    /// The sequence that leaves it.
    /// </param>
    [Theory]
    [InlineData("\x1b[66l", "\x1b[66h")]
    public void AllModels_EnterAndExit2115Mode_WithEverySpelling(string enable, string disable)
    {
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;

            Assert.False(Is2115(emulator), $"{name} should start in native mode");

            Send(emulator, enable);
            Assert.True(Is2115(emulator), $"{name} did not enter 2115 mode via {Visible(enable)}");

            Send(emulator, disable);
            Assert.False(Is2115(emulator), $"{name} did not leave 2115 mode via {Visible(disable)}");
        }
    }

    [Fact]
    public void AllModels_EscQ_LeavesCompatibilityMode()
    {
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;

            Send(emulator, "\x1b[66l");
            Assert.True(Is2115(emulator), $"{name} did not enter 2115 mode");

            Send(emulator, "\x1bQ");
            Assert.False(Is2115(emulator), $"{name} did not leave 2115 mode on ESC Q");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The actual regression: 2115 mode must NOT disable normal
    // cursor movement or erase on any model.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AllModels_CursorAddressingStillWorksIn2115Mode()
    {
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;

            Send(emulator, "\x1b[66l");   // enter 2115 mode
            Send(emulator, "\x1b[7;13H");  // CUP to row 7, column 13 (1-based)

            var cursor = emulator.GetCursor();
            Assert.Equal(6, cursor.Row);      // 0-based
            Assert.Equal(12, cursor.Column);
            Assert.True(true, name);
        }
    }

    [Theory]
    [InlineData("\x1b[3B", 3, 0)]   // CUD - cursor down 3
    [InlineData("\x1b[4C", 0, 4)]   // CUF - cursor forward 4
    public void AllModels_RelativeCursorMovementStillWorksIn2115Mode(string sequence, int expectedRow, int expectedCol)
    {
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;

            Send(emulator, "\x1b[66l");
            Send(emulator, "\x1b[1;1H"); // home
            Send(emulator, sequence);

            var cursor = emulator.GetCursor();
            Assert.Equal(expectedRow, cursor.Row);
            Assert.Equal(expectedCol, cursor.Column);
            Assert.True(true, name);
        }
    }

    [Fact]
    public void AllModels_EraseDisplayStillWorksIn2115Mode()
    {
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;

            Send(emulator, "\x1b[1;1H");
            Send(emulator, "HELLO");
            Assert.Equal('H', (char)emulator.GetBuffer()[0, 0].Codepoint);

            Send(emulator, "\x1b[66l");  // enter 2115 mode
            Send(emulator, "\x1b[2J");    // ED - erase whole display

            var cell = emulator.GetBuffer()[0, 0];
            Assert.True(cell.Codepoint == ' ' || cell.Codepoint == 0,
                $"{name}: ED did nothing in 2115 mode (cell still 0x{cell.Codepoint:X2})");
        }
    }

    [Fact]
    public void AllModels_EraseLineStillWorksIn2115Mode()
    {
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;

            Send(emulator, "\x1b[1;1H");
            Send(emulator, "HELLO");
            Send(emulator, "\x1b[66l");
            Send(emulator, "\x1b[1;1H");
            Send(emulator, "\x1b[2K");   // EL - erase whole line

            var cell = emulator.GetBuffer()[0, 0];
            Assert.True(cell.Codepoint == ' ' || cell.Codepoint == 0,
                $"{name}: EL did nothing in 2115 mode (cell still 0x{cell.Codepoint:X2})");
        }
    }

    [Fact]
    public void AllModels_CarriageReturnAndLineFeedStillWorkIn2115Mode()
    {
        // The old TDV1200 stub also swallowed BEL/BS/HT/LF/CR.
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;

            Send(emulator, "\x1b[66l");
            Send(emulator, "\x1b[1;5H"); // row 0, col 4
            Send(emulator, "\r");

            Assert.Equal(0, emulator.GetCursor().Column);

            Send(emulator, "\n");
            Assert.Equal(1, emulator.GetCursor().Row);
            Assert.True(true, name);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // A TDV has no mode query, so it must not invent a reply to one
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Neither <c>CSI ? 40 $ y</c> nor <c>CSI ? 66 $ y</c> draws a TDV reply out of any model.
    /// </summary>
    /// <remarks>
    /// Two tests used to sit here asserting that both numbers reported the 2115 state, one for the
    /// mode set and one for it reset. They were pinning an invented sequence.
    ///
    /// No TDV manual has a mode query. TDV 2215 Functional Specifications section 8.7 lists every
    /// CSI sequence the terminal accepts and there is no <c>$</c> intermediate in it; section 8.3.2
    /// lists everything it ever sends and that is CPR alone. The TDV 2200/9 S delta list (User's
    /// Guide section 11.2) adds no query either. Mode 40 is the printer code format and had nothing
    /// to do with 2115 compatibility in the first place.
    ///
    /// The shape those tests sent is itself telling: <c>$ y</c> is the REPLY final. A real DECRQM
    /// request ends <c>$ p</c>. They were sending a response at the terminal and expecting a
    /// response back.
    ///
    /// See <c>docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    /// <param name="modeNumber">
    /// The mode number to ask about.
    /// </param>
    [Theory]
    [InlineData(40)]
    [InlineData(66)]
    public void AllModels_AnswerNoTdvModeQuery(int modeNumber)
    {
        var models = AllModels();
        for (int i = 0; i < models.Length; i++)
        {
            var emulator = models[i];
            var name = emulator.GetType().Name;
            var responses = new List<string>();
            emulator.OnResponseReady += r => responses.Add(r);

            // In 2115 mode, and then in native mode: neither state may produce a reply.
            Send(emulator, "\x1b[66l");
            responses.Clear();
            Send(emulator, $"\x1b[?{modeNumber}$y");
            Assert.True(responses.Count == 0,
                $"{name} answered a TDV mode query in 2115 mode with {responses.Count} reply/replies");

            Send(emulator, "\x1b[66h");
            responses.Clear();
            Send(emulator, $"\x1b[?{modeNumber}$y");
            Assert.True(responses.Count == 0,
                $"{name} answered a TDV mode query in native mode with {responses.Count} reply/replies");
        }
    }

    /// <summary>
    /// Renders control characters readably for assertion messages.
    /// </summary>
    private static string Visible(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\x1b') sb.Append("ESC");
            else if (c < 0x20) sb.Append($"<{(int)c:X2}>");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
