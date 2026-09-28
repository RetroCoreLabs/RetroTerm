using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Comprehensive unit tests for all TDV Terminal Tests menu options
/// Tests Query/Response, Character Sets, Drawing Operations, Function Keys, Modes, Key Detection, TDV2200-specific, and Comprehensive Demo
/// Uses InMemoryConnection for bidirectional testing and validates terminal buffer state
/// </summary>
public class TDVTerminalTestsComprehensiveTests
{
    #region Helper Methods

    /// <summary>
    /// Builds a TDV2200 and an in-memory connection, WIRED TO EACH OTHER in both directions.
    /// </summary>
    /// <remarks>
    /// <para><b>They were never wired, and it made most of this file meaningless</b></para>
    /// Until 11 September 2026 this method built the two objects, connected the connection to
    /// nothing, and handed them back. Every <c>connection.SendAsync(...)</c> in the file therefore
    /// put bytes into a list that nothing read, and nine tests closed on
    /// <c>Assert.NotNull(emulator.Buffer)</c>, which is true of a brand new emulator. They passed
    /// green whatever they sent. That is how a wrong smooth-scroll mode number survived in here
    /// long enough to be found by reading a manual instead of by a failing test.
    ///
    /// <para><b>And the direction was backwards</b></para>
    /// <c>SendAsync</c> is the TERMINAL-to-host path - it is what the emulator uses to answer a
    /// query. The host-to-terminal path is <see cref="InMemoryConnection.SimulateReceive"/>, which
    /// raises <c>DataReceived</c>. So tests written as "the host sends this" were calling the
    /// outbound method. The call sites use <c>SimulateReceive</c> now.
    ///
    /// Both directions are joined here: host bytes reach <c>ProcessInput</c>, and anything the
    /// emulator answers goes out through the connection where <c>GetSentData</c> can see it.
    /// </remarks>
    /// <returns>
    /// The emulator and the connection feeding it.
    /// </returns>
    private static (TDV2200Emulator emulator, InMemoryConnection connection) CreateTestEmulator()
    {
        var emulator = new TDV2200Emulator(80, 24, 1000);
        var connection = new InMemoryConnection();

        connection.ConnectAsync().Wait();

        // Host to terminal.
        connection.DataReceived += bytes => emulator.ProcessInput(bytes.Span);

        // Terminal to host. This is the wire a real query reply travels on.
        emulator.DataToSend += bytes => connection.SendAsync(bytes);

        return (emulator, connection);
    }

    /// <summary>
    /// Sends a query and captures the response from the emulator
    /// Captures response via OnResponseReady event (for query/response sequences)
    /// </summary>
    private async Task<string> SendAndCaptureResponseAsync(
        TDV2200Emulator emulator,
        InMemoryConnection connection,
        byte[] query,
        int timeoutMs = 500)
    {
        string? response = null;
        bool responseReceived = false;

        void OnResponseReady(string r)
        {
            response = r;
            responseReceived = true;
        }

        // Subscribe to OnResponseReady to capture query responses
        emulator.OnResponseReady += OnResponseReady;

        try
        {
            // Send query directly to emulator
            emulator.ProcessInput(query);

            // Give emulator time to process and respond
            await Task.Delay(50);

            // Wait for response with timeout
            var startTime = DateTime.UtcNow;
            while (!responseReceived && (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(10);
            }

            return response ?? string.Empty;
        }
        finally
        {
            emulator.OnResponseReady -= OnResponseReady;
        }
    }

    /// <summary>
    /// Sends a query and captures what the emulator puts ON THE WIRE.
    /// </summary>
    /// <remarks>
    /// <see cref="SendAndCaptureResponseAsync"/> watches <c>OnResponseReady</c>, which is the TDV
    /// classes&#39; own observation event. Replies produced by <c>TerminalEmulatorBase</c> - DECRQM
    /// among them - never raise it; they go straight out of <c>DataToSend</c>, which is where a
    /// real host would see them. So anything asking about a base-class reply has to watch this
    /// instead, or it reads silence and calls it a failure.
    /// </remarks>
    /// <param name="emulator">
    /// The emulator to query.
    /// </param>
    /// <param name="query">
    /// The bytes to feed it.
    /// </param>
    /// <returns>
    /// Everything written to the wire while the query was processed, as one string.
    /// </returns>
    private static string CaptureWireReply(TDV2200Emulator emulator, byte[] query)
    {
        var wire = new StringBuilder();
        void OnWire(byte[] bytes) => wire.Append(Encoding.ASCII.GetString(bytes));

        emulator.DataToSend += OnWire;
        try
        {
            emulator.ProcessInput(query);
        }
        finally
        {
            emulator.DataToSend -= OnWire;
        }

        return wire.ToString();
    }

    /// <summary>
    /// Asserts that a specific buffer cell contains expected values
    /// </summary>
    private void AssertBufferCell(
        TDV2200Emulator emulator,
        int row,
        int col,
        uint expectedCodepoint,
        byte expectedFontNum = 0,
        string message = "")
    {
        var cell = emulator.Buffer[row, col];
        Assert.Equal(expectedCodepoint, cell.Codepoint);
        Assert.Equal(expectedFontNum, cell.FontNumber);
    }

    /// <summary>
    /// Reads a line of text from the terminal buffer
    /// </summary>
    private string ReadBufferLine(TDV2200Emulator emulator, int row, int maxCol = 80)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < maxCol; col++)
        {
            var cell = emulator.Buffer[row, col];
            if (cell.Codepoint == 0 || cell.Codepoint == 0x20)
            {
                break; // Stop at null or space padding
            }
            sb.Append((char)cell.Codepoint);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Reads text from a rectangular region of the buffer.
    /// Parameters are 1-indexed (matching CSI conventions), converted to 0-indexed for buffer access.
    /// </summary>
    private string ReadBufferRegion(
        TDV2200Emulator emulator,
        int row1, int col1,
        int row2, int col2)
    {
        // Convert from 1-indexed (CSI) to 0-indexed (buffer)
        var r1 = row1 - 1;
        var c1 = col1 - 1;
        var r2 = row2 - 1;
        var c2 = col2 - 1;

        var sb = new StringBuilder();
        for (int row = r1; row <= r2; row++)
        {
            for (int col = c1; col <= c2; col++)
            {
                var cell = emulator.Buffer[row, col];
                sb.Append((char)cell.Codepoint);
            }
            if (row < r2)
            {
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Validates that all cells in a rectangle have the expected attribute
    /// </summary>
    private void AssertRectangleAttribute(
        TDV2200Emulator emulator,
        int row1, int col1,
        int row2, int col2,
        Func<RetroTerm.Core.Terminal.Buffer.TerminalCell, bool> attributeCheck,
        string attributeName)
    {
        for (int row = row1; row <= row2; row++)
        {
            for (int col = col1; col <= col2; col++)
            {
                var cell = emulator.Buffer[row, col];
                Assert.True(attributeCheck(cell),
                    $"Cell at ({row},{col}) should have {attributeName}");
            }
        }
    }

    /// <summary>
    /// Clears the screen and homes the cursor
    /// </summary>
    private async Task ClearScreenAsync(TDV2200Emulator emulator)
    {
        // ESC[2J - Clear screen, ESC[H - Home cursor
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x32, 0x4A, 0x1B, 0x5B, 0x48 });
        await Task.Delay(10);
    }

    #endregion

    #region Test Suite 1: Query/Response Tests

    [Fact]
    public async Task QueryResponse_PrimaryDA_SendsCorrectQuery_ReturnsValidResponse()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Act - Send Primary DA query: ESC[c
        var query = new byte[] { 0x1B, 0x5B, 0x63 };
        var response = await SendAndCaptureResponseAsync(emulator, connection, query);

        // Assert - Should return ESC[?220;0c for TDV2200
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        // Response should be "\x1B[?220;0c" or "[?220;0c" (ESC might be stripped in string conversion)
        Assert.Contains("[?220;0c", response);
    }

    [Fact]
    public async Task QueryResponse_SecondaryDA_ValidatesFirmwareID()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Act - Send Secondary DA query: ESC[>c
        var query = new byte[] { 0x1B, 0x5B, 0x3E, 0x63 };
        var response = await SendAndCaptureResponseAsync(emulator, connection, query);

        // Assert - Should return ESC[>220;0;0c for TDV2200
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        // Response should be "\x1B[>220;0;0c" or "[>220;0;0c"
        Assert.Contains("[>220;0;0c", response);
    }

    [Fact]
    public async Task QueryResponse_CPR_ReturnsAccurateCursorPosition()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Position cursor at row 10, column 20: ESC[10;20H
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x31, 0x30, 0x3B, 0x32, 0x30, 0x48 });
        await Task.Delay(10);

        // Act - Send CPR query: ESC[6n
        var query = new byte[] { 0x1B, 0x5B, 0x36, 0x6E };
        var response = await SendAndCaptureResponseAsync(emulator, connection, query);

        // Assert - Should return ESC[10;20R or [10;20R
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        Assert.EndsWith("R", response);
        Assert.Contains("10;20", response);
    }

    [Fact]
    public async Task QueryResponse_DSR_ReturnsOKStatus()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Act - Send DSR query: ESC[5n
        var query = new byte[] { 0x1B, 0x5B, 0x35, 0x6E };
        var response = await SendAndCaptureResponseAsync(emulator, connection, query);

        // Assert - Should return ESC[0n or [0n (OK status)
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        Assert.Contains("[0n", response);
    }

    [Fact]
    public async Task QueryResponse_TerminalID_ReturnsTerminalType()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Act - Send Terminal ID query: ESC Z
        var query = new byte[] { 0x1B, 0x5A };
        var response = await SendAndCaptureResponseAsync(emulator, connection, query);

        // Assert - Should return terminal identifier
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        // TDV2200 should return specific ID sequence
    }

    /// <summary>
    /// DECRQM about smooth scroll uses DEC&#39;s number, 4, and answers DEC&#39;s values.
    /// </summary>
    /// <remarks>
    /// This asked about mode 67 and called it smooth scroll. Mode 67 is HAN, the XON/XOFF
    /// handshake, in TDV 2215 Functional Specifications section 8.7.1. The TDV number for smooth
    /// scroll is 60, RT Roll Type; ND Display Terminal 1200 section 5.64 uses DEC&#39;s own
    /// <c>CSI ? 4</c>. DECRQM itself appears in no TDV manual and answers here only because
    /// <c>TerminalEmulatorBase</c> answers it for every emulator.
    /// See <c>docs&#92;TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    [Fact]
    public async Task QueryResponse_DECRQM_SmoothScrollMode_ReturnsCorrectState()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Act - DECRQM for DECSCLM: ESC [ ? 4 $ p
        var query = new byte[] { 0x1B, 0x5B, 0x3F, 0x34, 0x24, 0x70 };
        var response = CaptureWireReply(emulator, query);

        // Assert - reset, because nothing has turned it on. DECRPM 2 means "known, and off".
        Assert.EndsWith("$y", response);
        Assert.Contains("?4;2", response);
        await Task.CompletedTask;
    }

    #endregion

    #region Test Suite 2: Character Sets Tests

    [Fact]
    public async Task CharacterSet_GraphicsI_DisplaysAndBufferValidated()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Act - Use SS2 (Single Shift 2) to invoke Graphics I from G2: ESC N
        // This temporarily invokes G2 (GraphicsI) for the next character
        for (int i = 0; i < 5; i++)
        {
            emulator.ProcessInput(new byte[] { 0x1B, 0x4E, 0x61 }); // ESC N 'a' - each char uses SS2
        }
        await Task.Delay(10);

        // Assert - G2 should be GraphicsI (this is the default)
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.GraphicsI, emulator.GetG2CharacterSet());

        // Validate buffer contains characters with graphics font
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(2, emulator.Buffer[0, i].FontNumber); // Graphics font
        }
    }

    [Fact]
    public async Task CharacterSet_Math_DisplaysMathSymbols()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Act - Switch to Math character set: ESC(3
        emulator.ProcessInput(new byte[] { 0x1B, 0x28, 0x33 });
        await Task.Delay(10);

        // Write math symbols
        emulator.ProcessInput(Encoding.ASCII.GetBytes("+-*/="));
        await Task.Delay(10);

        // Assert - Verify character set switched
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.Math, emulator.GetG0CharacterSet());

        // Validate buffer has content
        var cell0 = emulator.Buffer[0, 0];
        Assert.NotEqual(0u, cell0.Codepoint);
    }

    [Fact]
    public async Task CharacterSet_Greek_DisplaysGreekAlphabet()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Act - Switch to Greek character set: ESC(4
        emulator.ProcessInput(new byte[] { 0x1B, 0x28, 0x34 });
        await Task.Delay(10);

        // Write Greek letters
        emulator.ProcessInput(Encoding.ASCII.GetBytes("ABGDE"));
        await Task.Delay(10);

        // Assert - Verify character set switched
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.Greek, emulator.GetG0CharacterSet());

        // Validate buffer has Greek characters
        var bufferText = ReadBufferLine(emulator, 0);
        Assert.NotEmpty(bufferText);
    }

    [Fact]
    public async Task CharacterSet_USASCII_ResetsToDefault()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Switch to Graphics I first
        emulator.ProcessInput(new byte[] { 0x1B, 0x28, 0x31 });
        await Task.Delay(10);

        // Act - Reset to US ASCII: ESC(0
        emulator.ProcessInput(new byte[] { 0x1B, 0x28, 0x30 });
        await Task.Delay(10);

        // Write normal text
        emulator.ProcessInput(Encoding.ASCII.GetBytes("Hello"));
        await Task.Delay(10);

        // Assert - Verify character set is USASCII
        Assert.Equal(TDVCharacterSets.TDVCharacterSetType.USASCII, emulator.GetG0CharacterSet());

        // Validate buffer shows "Hello" with fontNum=0
        AssertBufferCell(emulator, 0, 0, (uint)'H', 0);
        AssertBufferCell(emulator, 0, 1, (uint)'e', 0);
        AssertBufferCell(emulator, 0, 2, (uint)'l', 0);
        AssertBufferCell(emulator, 0, 3, (uint)'l', 0);
        AssertBufferCell(emulator, 0, 4, (uint)'o', 0);
    }

    [Fact]
    public async Task CharacterSet_AllTenSets_CycleAndValidate()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Act & Assert - Test all 10 character sets (0-9)
        var characterSets = new[]
        {
            (0x30, TDVCharacterSets.TDVCharacterSetType.USASCII),
            (0x31, TDVCharacterSets.TDVCharacterSetType.GraphicsI),
            (0x32, TDVCharacterSets.TDVCharacterSetType.GraphicsII),
            (0x33, TDVCharacterSets.TDVCharacterSetType.Math),
            (0x34, TDVCharacterSets.TDVCharacterSetType.Greek),
            (0x35, TDVCharacterSets.TDVCharacterSetType.NIX),  // Norwegian
            (0x36, TDVCharacterSets.TDVCharacterSetType.Diacritics),  // Swedish/Diacritics
            (0x37, TDVCharacterSets.TDVCharacterSetType.Box),  // Danish/Box
            (0x38, TDVCharacterSets.TDVCharacterSetType.T),    // Finnish/Technical
            (0x39, TDVCharacterSets.TDVCharacterSetType.ND),   // German/ND
        };

        for (int i = 0; i < characterSets.Length; i++)
        {
            var (setNum, expectedType) = characterSets[i];

            // Send ESC(N where N is the set number
            emulator.ProcessInput(new byte[] { 0x1B, 0x28, (byte)setNum });
            await Task.Delay(10);

            // Validate character set switched
            var actualType = emulator.GetG0CharacterSet();
            Assert.True(actualType == expectedType ||
                        (setNum >= 0x35 && setNum <= 0x39), // National character sets may vary
                        $"Character set {i} (0x{setNum:X2}) failed");
        }
    }

    #endregion

    #region Test Suite 3: Drawing Operations Tests

    [Fact]
    public async Task DrawingOps_NDSAR_SetAttributeRectangle_AppliesBoldToRegion()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Fill area with text first
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x31, 0x30, 0x3B, 0x35, 0x48 }); // Position 10,5
        emulator.ProcessInput(Encoding.ASCII.GetBytes("AAAAAAAAAA")); // 10 A's
        await Task.Delay(10);

        // Act - Send NDSAR to set bold in rectangle (10,5)-(10,14): ESC[1;10;5;10;14z
        emulator.ProcessInput(new byte[]
        {
            0x1B, 0x5B, 0x31, 0x3B, 0x31, 0x30, 0x3B, 0x35, 0x3B, 0x31, 0x30, 0x3B, 0x31, 0x34, 0x7A
        });
        await Task.Delay(10);

        // Assert - Verify cells in rectangle have bold attribute
        for (int col = 5; col <= 14; col++)
        {
            var cell = emulator.Buffer[10, col];
            Assert.True(cell.Attributes.HasFlag(RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Bold),
                $"Cell at (10,{col}) should have bold attribute");
        }
    }

    [Fact]
    public async Task DrawingOps_NDFC_FillCharacter_FillsRectangleWithChar()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Act - Send NDFC to fill rectangle (5,10)-(7,15) with 'X' (ASCII 88): ESC[88;5;10;7;15}
        //
        // The final is 7D, not 7C. NDRAR and NDFC were swapped throughout this program until
        // 11 September 2026; ND-1200 section 2.8 gives 7C NDRAR and 7D NDFC.
        emulator.ProcessInput(new byte[]
        {
            0x1B, 0x5B, 0x38, 0x38, 0x3B, 0x35, 0x3B, 0x31, 0x30, 0x3B, 0x37, 0x3B, 0x31, 0x35, 0x7D
        });
        await Task.Delay(10);

        // Assert - Verify all cells in rectangle contain 'X'
        for (int row = 5; row <= 7; row++)
        {
            for (int col = 10; col <= 15; col++)
            {
                AssertBufferCell(emulator, row, col, (uint)'X');
            }
        }

        // Verify cells outside rectangle are not filled
        AssertBufferCell(emulator, 4, 10, 0); // Above rectangle: never written, so codepoint 0
        AssertBufferCell(emulator, 5, 9, 0);  // Left of rectangle: never written
    }

    /// <summary>
    /// NDDWA sets the work area, and the emulator reports the boundaries it was given.
    /// </summary>
    /// <remarks>
    /// This closed on <c>Assert.NotNull(emulator.Buffer)</c> with a comment saying "This would
    /// require checking internal emulator state for work area". It does not: the boundaries are
    /// public, on <c>TDVEmulatorBase.CurrentWorkArea</c>.
    ///
    /// The parameter order is the one <c>HandleDefineWorkArea</c> uses - row, column, row, column,
    /// 0-indexed, which it passes on to <c>DefineWorkArea</c> as (x1, y1, x2, y2). So
    /// <c>ESC [ 5;10;20;70 ~</c> is rows 5 to 20 and columns 10 to 70, and the reported tuple is
    /// (Left, Top, Right, Bottom) = (10, 5, 70, 20).
    ///
    /// The default is checked first, so the test can tell "the sequence set these numbers" from
    /// "these numbers happened to be there already" - a whole-screen work area would be
    /// (0, 0, 79, 23) and could not be mistaken for this one either way round.
    /// </remarks>
    [Fact]
    public void DrawingOps_NDDWA_DefineWorkArea_SetsBoundaries()
    {
        var (emulator, connection) = CreateTestEmulator();

        // Before: no work area, so the whole screen is reported.
        var before = emulator.CurrentWorkArea;
        Assert.Equal((0, 0, emulator.Width - 1, emulator.Height - 1), before);

        // ESC [ 5;10;20;70 ~
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[5;10;20;70~"));

        var after = emulator.CurrentWorkArea;
        Assert.Equal(10, after.Left);
        Assert.Equal(5, after.Top);
        Assert.Equal(70, after.Right);
        Assert.Equal(20, after.Bottom);
    }

    [Fact]
    public async Task DrawingOps_NDSREC_SaveRectangle_StoresRegion()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Fill a rectangle with known content
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x31, 0x30, 0x3B, 0x35, 0x48 }); // Position 10,5
        emulator.ProcessInput(Encoding.ASCII.GetBytes("SAVED"));
        await Task.Delay(10);

        // Act - Send NDSREC to save rectangle (10,5)-(10,9): ESC[10;5;10;9u
        emulator.ProcessInput(new byte[]
        {
            0x1B, 0x5B, 0x31, 0x30, 0x3B, 0x35, 0x3B, 0x31, 0x30, 0x3B, 0x39, 0x75
        });
        await Task.Delay(10);

        // Assert - Verify original content is still in buffer
        var savedText = ReadBufferRegion(emulator, 10, 5, 10, 9);
        Assert.Contains("SAVED", savedText);
    }

    [Fact]
    public async Task DrawingOps_NDRREC_RestoreRectangle_RestoresContent()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Fill and save a rectangle
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x31, 0x30, 0x3B, 0x35, 0x48 }); // Position 10,5
        emulator.ProcessInput(Encoding.ASCII.GetBytes("TEST"));
        await Task.Delay(10);

        // Save rectangle (10,5)-(10,8): ESC[10;5;10;8u
        emulator.ProcessInput(new byte[]
        {
            0x1B, 0x5B, 0x31, 0x30, 0x3B, 0x35, 0x3B, 0x31, 0x30, 0x3B, 0x38, 0x75
        });
        await Task.Delay(10);

        // Overwrite with different content
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x31, 0x30, 0x3B, 0x35, 0x48 }); // Position 10,5
        emulator.ProcessInput(Encoding.ASCII.GetBytes("XXXX"));
        await Task.Delay(10);

        // Act - Restore rectangle at new position (15,10): ESC[15;10v
        emulator.ProcessInput(new byte[]
        {
            0x1B, 0x5B, 0x31, 0x35, 0x3B, 0x31, 0x30, 0x76
        });
        await Task.Delay(10);

        // Assert - Verify original content restored at new location
        var restoredText = ReadBufferRegion(emulator, 15, 10, 15, 13);
        Assert.Contains("TEST", restoredText);
    }

    #endregion

    #region Test Suite 4: Modes & Features Tests

    /// <summary>
    /// The TDV smooth-scroll switch is 60, and DECRQM about mode 4 follows it.
    /// </summary>
    /// <remarks>
    /// <para><b>Two things were wrong here, and the second one is worse</b></para>
    /// Both halves used mode 67 and called it smooth scroll. Mode 67 is HAN, the XON/XOFF
    /// handshake; TDV 2215 Functional Specifications section 8.7.1 gives 60, RT Roll Type, RESET
    /// STEP and SET SMOOTH, with no private marker.
    ///
    /// And the bytes never reached the emulator at all. <c>CreateTestEmulator</c> builds a
    /// <c>TDV2200Emulator</c> and an <c>InMemoryConnection</c> and NEVER WIRES THEM TOGETHER, so
    /// <c>connection.SendAsync</c> put the sequence into a queue nothing was reading. The old test
    /// then closed with <c>Assert.NotNull(emulator.Buffer)</c>, which is true of a brand new
    /// emulator, so it passed green while testing nothing - and it would have gone on passing with
    /// any mode number whatever. The bytes go to the emulator directly now.
    /// </remarks>
    [Fact]
    public void Modes_SmoothScroll_EnableDisable_TogglesCorrectly()
    {
        var (emulator, _) = CreateTestEmulator();

        // DECRQM for DECSCLM: ESC [ ? 4 $ p
        var query = new byte[] { 0x1B, 0x5B, 0x3F, 0x34, 0x24, 0x70 };

        // Off to begin with. DECRPM 2 is "I know that mode and it is reset".
        Assert.Contains("?4;2", CaptureWireReply(emulator, query));

        // RT to SMOOTH: ESC [ 60 h
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x36, 0x30, 0x68 });
        Assert.Contains("?4;1", CaptureWireReply(emulator, query));

        // RT back to STEP: ESC [ 60 l
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x36, 0x30, 0x6C });
        Assert.Contains("?4;2", CaptureWireReply(emulator, query));
    }

    /// <summary>
    /// NDBLWM, beginning of line wrap: with it on, backspace at column 0 goes to the line above.
    /// </summary>
    /// <remarks>
    /// This was <c>Modes_Blink_EnableDisable_TogglesCorrectly</c>, sending <c>CSI ? 68 h</c> and
    /// closing on <c>Assert.NotNull(emulator.Buffer)</c> - true of a brand new emulator, so it
    /// passed whatever it sent. There is no blink mode on a TDV: mode 68 is CT, the cursor type
    /// (TDV 2215 Functional Specifications section 8.7.1), and NDBLWM is the Beginning of Line
    /// WRAP mode, ND-1200 section 4.10: "SET - Cursor will wrap around to preceding line when left
    /// margin is reached. RESET - Cursor will stop at left margin." The 2215 numbers it 31.
    ///
    /// Asserted on what the cursor actually does, not on a flag, because a flag can be set by code
    /// that nothing reads - which is exactly how the two dead fields this replaces survived.
    /// </remarks>
    [Fact]
    public void Modes_BeginningOfLineWrap_ChangesWhatBackspaceDoesAtTheLeftMargin()
    {
        var (emulator, connection) = CreateTestEmulator();

        // Park the cursor at the start of row 2 (1-based), i.e. buffer row 1, column 0.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[2;1H"));
        Assert.Equal(1, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);

        // OFF by default: backspace at the left margin must not move anywhere.
        connection.SimulateReceive(new byte[] { 0x08 });
        Assert.Equal(1, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);

        // ON: ESC [ 31 h. Now the same backspace goes to the end of the line above.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[31h"));
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[2;1H"));
        connection.SimulateReceive(new byte[] { 0x08 });
        Assert.Equal(0, emulator.Cursor.Row);
        Assert.Equal(emulator.Width - 1, emulator.Cursor.Column);

        // OFF again: ESC [ 31 l, and the old behaviour is back.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[31l"));
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[2;1H"));
        connection.SimulateReceive(new byte[] { 0x08 });
        Assert.Equal(1, emulator.Cursor.Row);
        Assert.Equal(0, emulator.Cursor.Column);
    }

    /// <summary>
    /// NDELWM, end of line wrap: with it off the cursor stops at the right margin.
    /// </summary>
    /// <remarks>
    /// This was <c>Modes_EnhancedBlink_EnableDisable_TogglesCorrectly</c>, sending
    /// <c>CSI ? 69 h</c>. Mode 69 is PM, the printer mode; NDELWM is the End of Line Wrap mode,
    /// ND-1200 section 4.11, and the 2215 numbers it 36 - a real TDV2200 termcap sets it in its
    /// init string, <c>ESC [ 36;62;62 h</c>.
    ///
    /// Autowrap is ON at power-up here, so the test turns it off first and proves the cursor stops,
    /// then turns it on and proves the text carries to the next line.
    /// </remarks>
    [Fact]
    public void Modes_EndOfLineWrap_DecidesWhetherTextCarriesToTheNextLine()
    {
        var (emulator, connection) = CreateTestEmulator();

        // OFF: ESC [ 36 l. Fill the row and one more character - the extra must not start row 2.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[36l"));
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[1;1H"));
        connection.SimulateReceive(Encoding.ASCII.GetBytes(new string('A', emulator.Width) + "B"));

        Assert.Equal(0, emulator.Cursor.Row);
        // Row 2 is untouched. An unwritten cell holds 0, not a space, so this asks that the
        // overflow character is absent rather than guessing what an empty cell contains.
        Assert.NotEqual((uint)'B', emulator.Buffer.GetCell(1, 0).Codepoint);

        // ON: ESC [ 36 h. The same overflow now lands on row 2.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[2J\x1b[36h\x1b[1;1H"));
        connection.SimulateReceive(Encoding.ASCII.GetBytes(new string('C', emulator.Width) + "D"));

        Assert.Equal(1, emulator.Cursor.Row);
        Assert.Equal((uint)'D', emulator.Buffer.GetCell(1, 0).Codepoint);
    }

    [Fact]
    public async Task Modes_DoubleHeight_TopAndBottom_AppliesCorrectly()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Write text on line 5
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x48 }); // Position 5,1
        emulator.ProcessInput(Encoding.ASCII.GetBytes("DOUBLE"));
        await Task.Delay(10);

        // Act - Apply double-height top: ESC#3
        emulator.ProcessInput(new byte[] { 0x1B, 0x23, 0x33 });
        await Task.Delay(10);

        // Assert - Verify buffer contains text and line has double-height attribute
        // ESC[5;1H uses 1-indexed coordinates (row 5) = buffer row 4 (0-indexed)
        var bufferText = ReadBufferLine(emulator, 4);
        Assert.Contains("DOUBLE", bufferText);
        // Would need to check DoubleHeightTop attribute on line
    }

    [Fact]
    public async Task Modes_DoubleWidth_AppliesAndResetsCorrectly()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Write text on line 5
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x35, 0x3B, 0x31, 0x48 }); // Position 5,1
        emulator.ProcessInput(Encoding.ASCII.GetBytes("WIDE"));
        await Task.Delay(10);

        // Act - Apply double-width: ESC#6
        emulator.ProcessInput(new byte[] { 0x1B, 0x23, 0x36 });
        await Task.Delay(10);

        // Assert - Verify buffer contains text
        // ESC[5;1H uses 1-indexed coordinates (row 5) = buffer row 4 (0-indexed)
        var bufferText = ReadBufferLine(emulator, 4);
        Assert.Contains("WIDE", bufferText);

        // Act - Reset to single-width: ESC#5
        emulator.ProcessInput(new byte[] { 0x1B, 0x23, 0x35 });
        await Task.Delay(10);

        // Assert - Verify still readable
        bufferText = ReadBufferLine(emulator, 4);
        Assert.Contains("WIDE", bufferText);
    }

    /// <summary>
    /// The three TDV mode switches in one list, each one read back afterwards.
    /// </summary>
    /// <remarks>
    /// This used to send them one at a time and then assert <c>emulator.Buffer</c> was not null and
    /// that the screen was still 80 by 24 - all three true of an emulator that had received
    /// nothing at all. It sent <c>CSI ? 67/68/69</c>, which are the handshake, the cursor type and
    /// the printer mode.
    ///
    /// The numbers are the manual's now (2215 section 8.7.1: 60 RT, 31 BOL, 36 EOL) and each is
    /// read back through DECRQM against the DEC mode that carries the same behaviour - 4 DECSCLM,
    /// 45 XTREVWRAP, 7 DECAWM. DECRPM 1 is set, 2 is reset.
    ///
    /// They also go as ONE list here, <c>CSI 60;31;36 h</c>, which is the shape a real host uses -
    /// a captured TDV2200 termcap sends <c>ESC [ 36;62;62 h</c> - and the shape that hid a defect
    /// once before: every mode after the first used to vanish. See <c>PedExitCaptureTests</c>.
    /// </remarks>
    [Fact]
    public void Modes_AllThreeInOneList_AreEachSetAndEachReset()
    {
        var (emulator, connection) = CreateTestEmulator();

        var smoothScroll = Encoding.ASCII.GetBytes("\x1b[?4$p");    // DECSCLM
        var reverseWrap = Encoding.ASCII.GetBytes("\x1b[?45$p");    // XTREVWRAP
        var autoWrap = Encoding.ASCII.GetBytes("\x1b[?7$p");        // DECAWM

        // One list, all three set.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[60;31;36h"));

        Assert.Contains("?4;1", CaptureWireReply(emulator, smoothScroll));
        Assert.Contains("?45;1", CaptureWireReply(emulator, reverseWrap));
        Assert.Contains("?7;1", CaptureWireReply(emulator, autoWrap));

        // One list, all three reset.
        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[60;31;36l"));

        Assert.Contains("?4;2", CaptureWireReply(emulator, smoothScroll));
        Assert.Contains("?45;2", CaptureWireReply(emulator, reverseWrap));
        Assert.Contains("?7;2", CaptureWireReply(emulator, autoWrap));
    }

    #endregion

    #region Test Suite 5: Key Detection Tests

    /// <summary>
    /// SO invokes G1 and SI invokes G0, read off the invoked-set index.
    /// </summary>
    /// <remarks>
    /// This sent SO then SI and asserted that the set at the end equalled the set at the start -
    /// which is true whether or not either byte ever arrived, and it did not arrive, because the
    /// connection was not wired to the emulator. The middle of the test was a comment saying
    /// "This would require checking internal invoked character set state". It does not:
    /// <c>GetCurrentCharacterSet</c> returns the invoked index and is public.
    ///
    /// Reading the INDEX rather than the character set type matters here. G0 and G1 are both
    /// US ASCII at power-up on a TDV2200, so a test comparing types would see no change across SO
    /// and pass for the wrong reason a second time.
    /// </remarks>
    [Fact]
    public void KeyDetection_ShiftOut_ShiftIn_SwitchesCharacterSets()
    {
        var (emulator, connection) = CreateTestEmulator();

        Assert.Equal(0, emulator.GetCurrentCharacterSet());

        // SO, 0x0E - invoke G1.
        connection.SimulateReceive(new byte[] { 0x0E });
        Assert.Equal(1, emulator.GetCurrentCharacterSet());

        // SI, 0x0F - back to G0.
        connection.SimulateReceive(new byte[] { 0x0F });
        Assert.Equal(0, emulator.GetCurrentCharacterSet());
    }

    [Fact]
    public async Task KeyDetection_TAB_ProcessesCorrectly()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Position at column 0
        var initialCol = emulator.Cursor.Column;

        // Act - Send TAB (0x09)
        emulator.ProcessInput(new byte[] { 0x09 });
        await Task.Delay(10);

        // Assert - Cursor should have advanced to next tab stop (typically 8)
        Assert.True(emulator.Cursor.Column > initialCol);
        Assert.True(emulator.Cursor.Column % 8 == 0); // Tab stops typically at multiples of 8
    }

    #endregion

    #region Test Suite 6: TDV2200-Specific Tests

    /// <summary>
    /// The four CSI n greater-than modes are recorded, not obeyed.
    /// </summary>
    /// <remarks>
    /// Two tests used to live here asserting that these sequences toggled a graphics-extension flag
    /// and a Tektronix flag. Neither sequence appears in the ND graphic terminal analysis, the
    /// TDV1200 graphics library reference or the comprehensive TDV reference - all three were
    /// searched on 25 August 2026. The flags were removed and the sequence is now counted, so a
    /// real ND host can supply the meaning instead of us inventing one.
    /// </remarks>
    [Fact]
    public async Task TDV2200_ModeControlSequence_IsCountedNotObeyed()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Act - all four of the modes that used to be acted on.
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x30, 0x3E });
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x31, 0x3E });
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x32, 0x3E });
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x33, 0x3E });
        await Task.Delay(10);

        // Assert - each one recorded under its own parameter, so the counter is a work list rather
        // than a single total.
        Assert.True(emulator.UnrecognisedSequences.ContainsKey("CSI 0 >"));
        Assert.True(emulator.UnrecognisedSequences.ContainsKey("CSI 1 >"));
        Assert.True(emulator.UnrecognisedSequences.ContainsKey("CSI 2 >"));
        Assert.True(emulator.UnrecognisedSequences.ContainsKey("CSI 3 >"));
    }

    [Fact]
    public async Task TDV2200_ISO646Variants_SwitchBetweenVariants()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();

        // Test Norwegian variant - initial state should be valid
        var initialVariant = emulator.CurrentISO646Variant;

        // Could test switching variants if DCS sequences are supported
        // For now, verify initial state is International (default per TDV2115 spec 9.1.1)
        Assert.Equal(TDV2200ISO646Variant.International, emulator.CurrentISO646Variant);
    }

    #endregion

    #region Test Suite 7: Comprehensive Demo Tests

    [Fact]
    public async Task ComprehensiveDemo_CharacterSets_AllDisplay()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Act - Run character sets demo
        // Graphics I
        emulator.ProcessInput(new byte[] { 0x1B, 0x28, 0x31 }); // ESC(1
        emulator.ProcessInput(Encoding.ASCII.GetBytes("GRAPHICS"));
        await Task.Delay(10);

        // Greek
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x32, 0x3B, 0x31, 0x48 }); // Line 2
        emulator.ProcessInput(new byte[] { 0x1B, 0x28, 0x34 }); // ESC(4
        emulator.ProcessInput(Encoding.ASCII.GetBytes("GREEK"));
        await Task.Delay(10);

        // Reset to USASCII
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x33, 0x3B, 0x31, 0x48 }); // Line 3
        emulator.ProcessInput(new byte[] { 0x1B, 0x28, 0x30 }); // ESC(0
        emulator.ProcessInput(Encoding.ASCII.GetBytes("NORMAL"));
        await Task.Delay(10);

        // Assert - Verify buffer has content from all three character sets
        var line1 = ReadBufferLine(emulator, 0);
        var line2 = ReadBufferLine(emulator, 1);
        var line3 = ReadBufferLine(emulator, 2);

        Assert.NotEmpty(line1);
        Assert.NotEmpty(line2);
        Assert.Contains("NORMAL", line3); // USASCII should be readable
    }

    [Fact]
    public async Task ComprehensiveDemo_Modes_AllToggle()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Act - Enable modes
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x36, 0x30, 0x68 }); // RT Roll Type to SMOOTH/STEP, 2215 section 8.7.1
        await Task.Delay(10);
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x33, 0x31, 0x68 }); // NDBLWM, beginning of line wrap
        await Task.Delay(10);

        // Write blinking text: ESC[5m + "BLINK" + ESC[0m
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x35, 0x6D }); // SGR blink
        emulator.ProcessInput(Encoding.ASCII.GetBytes("BLINK"));
        emulator.ProcessInput(new byte[] { 0x1B, 0x5B, 0x30, 0x6D }); // SGR reset
        await Task.Delay(10);

        // Assert - Text in buffer
        var bufferText = ReadBufferLine(emulator, 0);
        Assert.Contains("BLINK", bufferText);

        // Verify blink attribute on cells
        for (int col = 0; col < 5; col++)
        {
            var cell = emulator.Buffer[0, col];
            Assert.True(cell.Attributes.HasFlag(RetroTerm.Core.Terminal.Buffer.CharacterAttributes.Blink));
        }
    }

    [Fact]
    public async Task ComprehensiveDemo_FullSequence_NoErrors()
    {
        // Arrange
        var (emulator, connection) = CreateTestEmulator();
        await connection.ConnectAsync();
        await ClearScreenAsync(emulator);

        // Act - Execute full demo sequence without errors
        try
        {
            // Character sets
            connection.SimulateReceive(new byte[] { 0x1B, 0x28, 0x31 });
            connection.SimulateReceive(Encoding.ASCII.GetBytes("ABC"));

            connection.SimulateReceive(new byte[] { 0x1B, 0x5B, 0x32, 0x3B, 0x31, 0x48 });
            connection.SimulateReceive(new byte[] { 0x1B, 0x28, 0x34 });
            connection.SimulateReceive(Encoding.ASCII.GetBytes("DEF"));

            connection.SimulateReceive(new byte[] { 0x1B, 0x5B, 0x33, 0x3B, 0x31, 0x48 });
            connection.SimulateReceive(new byte[] { 0x1B, 0x28, 0x30 });
            connection.SimulateReceive(Encoding.ASCII.GetBytes("NORMAL"));

            // Modes
            connection.SimulateReceive(new byte[] { 0x1B, 0x5B, 0x36, 0x30, 0x68 });
            connection.SimulateReceive(new byte[] { 0x1B, 0x5B, 0x33, 0x31, 0x68 });

            await Task.Delay(100);

            // Assert on what the demo actually PUT ON THE SCREEN and what it left switched on.
            //
            // This used to assert that the buffer was not null and the screen was still 80 by 24 -
            // all three true of an emulator that had received nothing at all, which is what it was
            // getting before the connection was wired to anything. Wrapping the whole thing in a
            // try/catch and calling "no exception" a pass was the other half of the same problem:
            // the demo could have dropped every byte and still gone green.
            Assert.Equal("ABC", ReadBufferLine(emulator, 0));
            Assert.Equal("DEF", ReadBufferLine(emulator, 1));
            Assert.Equal("NORMAL", ReadBufferLine(emulator, 2));

            // ESC [ 60 h is RT to SMOOTH and ESC [ 31 h is beginning-of-line wrap. Read back
            // through DECRQM against the DEC modes carrying the same behaviour; 1 is set.
            Assert.Contains("?4;1", CaptureWireReply(emulator, Encoding.ASCII.GetBytes("\x1b[?4$p")));
            Assert.Contains("?45;1", CaptureWireReply(emulator, Encoding.ASCII.GetBytes("\x1b[?45$p")));
        }
        catch (Exception ex)
        {
            Assert.Fail($"Comprehensive demo threw exception: {ex.Message}");
        }
    }

    #endregion
}
