using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.Core.Protocols.TelnetServer.Utilities;

namespace RetroTerm.TestServer.App;

public enum MenuLevel
{
    Main,
    StandardTests,
    TDVTests,
    TDV1200Tests,
    TDV2215Tests,
    TDV2200Tests,
    TDVKeyDetection,
    TDVFunctionKeys,
    TDVPushKeys,
    TDVSoftKeys,
    TDVControlKeys,
    TDVExtendedControlKeys,
    TDVNumpadFunctionKeys,

    // The three suites added so a person can reach the DEC, xterm and graphics work through a
    // connection. Until these existed the server could only drive VT100/ANSI and TDV.
    DECTests,
    XtermTests,
    GraphicsTests
}

public partial class TestServerApp : ITelnetApp
{
    private TerminalType _detectedTerminalType = TerminalType.Unknown;
    private MenuLevel _currentMenuLevel = MenuLevel.Main;
    private TDVCapabilityChecker? _capabilityChecker;

    public async Task OnConnectedAsync(TelnetSession session)
    {
        var clientEndpoint = session.RemoteEndPoint;
        System.Console.WriteLine($"[TestServer] ========================================");
        System.Console.WriteLine($"[TestServer] Client connected from: {clientEndpoint}");
        System.Console.WriteLine($"[TestServer] Stream.CanRead: {session.Stream.CanRead}");
        System.Console.WriteLine($"[TestServer] Stream.CanWrite: {session.Stream.CanWrite}");
        System.Console.WriteLine($"[TestServer] ========================================");

        // START EVERY CLIENT AT THE TOP OF THE MENU.
        //
        // Measured 11 September 2026, driving this server twice over a raw socket: the second
        // connection came up inside the Standard Tests menu, on the terminal type the FIRST
        // connection had chosen. TelnetServer holds ONE TestServerApp for the whole process, so
        // these three fields survived the client that set them. That is a nasty one to meet by
        // hand - the menu you are looking at answers to keys nobody pressed in this session.
        //
        // This resets them. It does NOT make the state per-session: two clients connected AT THE
        // SAME TIME still share one menu level, and fixing that properly means moving these fields
        // onto an object hung off the session. Nothing here drives two at once today.
        _currentMenuLevel = MenuLevel.Main;
        _detectedTerminalType = TerminalType.Unknown;
        _capabilityChecker = null;

        try
        {
            await WriteWelcomeAsync(session);
            await DetectTerminalTypeAsync(session);
            _capabilityChecker = new TDVCapabilityChecker(_detectedTerminalType);

            session.StartInputPump();
            try
            {
                await WriteMenuAsync(session);

                while (true)
                {
                    var parsed = await session.ReadInputAsync();
                    System.Console.WriteLine($"[TestServer] Parsed input: {parsed}");

                    // Handle based on input type
                    switch (parsed.Type)
                    {
                        case InputType.EscapeSequence:
                            // Log and ignore escape sequences in menu mode
                            System.Console.WriteLine($"[TestServer] Ignoring {parsed.Name ?? "escape sequence"} in menu");

                            // RECORDED, not just logged, so a test can see what the terminal
                            // answered while this loop was running. See the property's remarks:
                            // the pump has ONE reader, and it is this loop.
                            if (parsed.Value != null) EscapeSequencesSeen.Enqueue(parsed.Value);
                            continue;

                        case InputType.Enter:
                            // Enter refreshes the menu
                            await WriteMenuAsync(session);
                            continue;

                        case InputType.Character:
                            // Process as menu command
                            break;

                        default:
                            // Ignore other input types (backspace, tab, control chars)
                            continue;
                    }

                    // Get the command character
                    var key = char.ToUpperInvariant(parsed.CommandChar);
                    if (key == ' ')
                    {
                        await WriteWelcomeAsync(session);
                        await WriteMenuAsync(session);
                        continue;
                    }

                    // Handle hierarchical menu navigation
                    if (key == 'B' || key == '0')
                    {
                        await NavigateBackAsync(session);
                        continue;
                    }

                    // Route to appropriate menu handler
                    switch (_currentMenuLevel)
                    {
                        case MenuLevel.Main:
                            await HandleMainMenuAsync(session, key);
                            break;
                        case MenuLevel.StandardTests:
                            await HandleStandardTestsMenuAsync(session, key);
                            break;
                        case MenuLevel.TDVTests:
                            await HandleTDVTestsMenuAsync(session, key);
                            break;
                        case MenuLevel.TDV1200Tests:
                            await HandleTDV1200TestsMenuAsync(session, key);
                            break;
                        case MenuLevel.TDV2215Tests:
                            await HandleTDV2215TestsMenuAsync(session, key);
                            break;
                        case MenuLevel.TDV2200Tests:
                            await HandleTDV2200TestsMenuAsync(session, key);
                            break;
                        case MenuLevel.TDVKeyDetection:
                            await HandleTDVKeyDetectionMenuAsync(session, key);
                            break;
                        // The six key-detection submenu levels have no case here on purpose.
                        // They are current only while their own interactive test is reading, and
                        // that test - not this loop - handles the keys. Routing them here is what
                        // made six branches deaf to every escape sequence until 11 September 2026:
                        // see the remarks on HandleTDVKeyDetectionMenuAsync.
                        case MenuLevel.DECTests:
                            await HandleDECTestsMenuAsync(session, key);
                            break;
                        case MenuLevel.XtermTests:
                            await HandleXtermTestsMenuAsync(session, key);
                            break;
                        case MenuLevel.GraphicsTests:
                            await HandleGraphicsTestsMenuAsync(session, key);
                            break;
                    }
                }
            }
            catch (ChannelClosedException)
            {
                System.Console.WriteLine($"[TestServer] Connection closed: {clientEndpoint}");
            }
            finally
            {
                await session.StopInputPumpAsync();
            }
        }
        catch (System.IO.IOException ex)
        {
            System.Console.WriteLine($"[TestServer] ========================================");
            System.Console.WriteLine($"[TestServer] Connection lost: IOException");
            System.Console.WriteLine($"[TestServer] Client: {clientEndpoint}");
            System.Console.WriteLine($"[TestServer] Exception: {ex.GetType().Name}: {ex.Message}");
            System.Console.WriteLine($"[TestServer] Stack trace: {ex.StackTrace}");
            System.Console.WriteLine($"[TestServer] ========================================");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            System.Console.WriteLine($"[TestServer] ========================================");
            System.Console.WriteLine($"[TestServer] Connection lost: SocketException");
            System.Console.WriteLine($"[TestServer] Client: {clientEndpoint}");
            System.Console.WriteLine($"[TestServer] SocketErrorCode: {ex.SocketErrorCode}");
            System.Console.WriteLine($"[TestServer] Exception: {ex.GetType().Name}: {ex.Message}");
            System.Console.WriteLine($"[TestServer] Stack trace: {ex.StackTrace}");
            System.Console.WriteLine($"[TestServer] ========================================");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[TestServer] ========================================");
            System.Console.WriteLine($"[TestServer] Unexpected error in OnConnectedAsync");
            System.Console.WriteLine($"[TestServer] Client: {clientEndpoint}");
            System.Console.WriteLine($"[TestServer] Exception: {ex.GetType().Name}: {ex.Message}");
            System.Console.WriteLine($"[TestServer] Stack trace: {ex.StackTrace}");
            System.Console.WriteLine($"[TestServer] ========================================");
        }
        finally
        {
            System.Console.WriteLine($"[TestServer] ========================================");
            System.Console.WriteLine($"[TestServer] Client session ended");
            System.Console.WriteLine($"[TestServer] Client: {clientEndpoint}");
            System.Console.WriteLine($"[TestServer] ========================================");
        }
    }

    private async Task NavigateBackAsync(TelnetSession session)
    {
        switch (_currentMenuLevel)
        {
            case MenuLevel.StandardTests:
            case MenuLevel.TDVTests:
            case MenuLevel.DECTests:
            case MenuLevel.XtermTests:
            case MenuLevel.GraphicsTests:
                _currentMenuLevel = MenuLevel.Main;
                break;
            case MenuLevel.TDV1200Tests:
            case MenuLevel.TDV2215Tests:
            case MenuLevel.TDV2200Tests:
            case MenuLevel.TDVKeyDetection:
                _currentMenuLevel = MenuLevel.TDVTests;
                break;
            case MenuLevel.TDVFunctionKeys:
            case MenuLevel.TDVPushKeys:
            case MenuLevel.TDVSoftKeys:
            case MenuLevel.TDVControlKeys:
                _currentMenuLevel = MenuLevel.TDVKeyDetection;
                break;
            case MenuLevel.Main:
                // Already at main menu, do nothing
                break;
            default:
                _currentMenuLevel = MenuLevel.Main;
                break;
        }
        await WriteMenuAsync(session);
    }

    /// <summary>
    /// Validates that the terminal type supports the requested test
    /// </summary>
    private bool ValidateTerminalType(TerminalType requiredType)
    {
        return _detectedTerminalType == requiredType;
    }

    /// <summary>
    /// Validates that the terminal is a TDV terminal
    /// </summary>
    private bool ValidateTDVTerminal()
    {
        return _detectedTerminalType == TerminalType.TDV1200 ||
               _detectedTerminalType == TerminalType.TDV2200 ||
               _detectedTerminalType == TerminalType.TDV2215;
    }

    /// <summary>
    /// Validates that the terminal supports a specific feature
    /// </summary>
    private bool ValidateFeature(string feature)
    {
        return _capabilityChecker?.Supports(feature) ?? false;
    }

    private async Task HandleMainMenuAsync(TelnetSession session, char key)
    {
        switch (key)
        {
            case '1':
                _currentMenuLevel = MenuLevel.StandardTests;
                await WriteMenuAsync(session);
                break;
            case '2':
                if (_detectedTerminalType == TerminalType.TDV1200 ||
                    _detectedTerminalType == TerminalType.TDV2215 ||
                    _detectedTerminalType == TerminalType.TDV2200)
                {
                    _currentMenuLevel = MenuLevel.TDVTests;
                    await WriteMenuAsync(session);
                }
                break;
            // The DEC, xterm and graphics suites are offered whatever the terminal says it is.
            // Deliberately: the point of a manual pass is often to see what a terminal does with a
            // sequence it does not claim, and gating them behind detection would hide exactly that.
            case '3':
                _currentMenuLevel = MenuLevel.DECTests;
                await WriteMenuAsync(session);
                break;
            case '4':
                _currentMenuLevel = MenuLevel.XtermTests;
                await WriteMenuAsync(session);
                break;
            case '5':
                _currentMenuLevel = MenuLevel.GraphicsTests;
                await WriteMenuAsync(session);
                break;
            case 'I':
                await RunTerminalInfoAsync(session);
                break;
        }
    }

    private async Task HandleStandardTestsMenuAsync(TelnetSession session, char key)
    {
        switch (key)
        {
            case 'S':
            case 's':
                await RunSmoothScrollAsync(session);
                break;
            case '1':
                await RunBasicColorsAsync(session);
                break;
            case '2':
                await RunCursorMovementAsync(session);
                break;
            case '3':
                await RunCharacterAttributesAsync(session);
                break;
            case '4':
                await RunScrollingAsync(session);
                break;
            case '5':
                await RunLineDrawingAsync(session);
                break;
            case '6':
                await RunScreenClearAsync(session);
                break;
            case '7':
                await Run256ColorsAsync(session);
                break;
            case '8':
                await RunTabStopsAsync(session);
                break;
            case '9':
                await RunCharacterSetsAsync(session);
                break;
            case 'A':
                await RunScrollingRegionAsync(session);
                break;
            case 'B':
                await RunCursorSaveRestoreAsync(session);
                break;
            case 'D':
                await RunKeyDecoderAsync(session);
                break;
        }
    }

    private async Task HandleTDVTestsMenuAsync(TelnetSession session, char key)
    {
        // Validate TDV terminal
        if (!ValidateTDVTerminal())
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV tests require a TDV terminal (TDV1200, TDV2200, or TDV2215).\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        switch (key)
        {
            case '1':
                await RunTDV_QueryResponseTestsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '2':
                await RunTDV_CharacterSetsTestsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '3':
                await RunTDV_DrawingOperationsTestsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '4':
                await RunTDV_FunctionKeysTestsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '5':
                await RunTDV_ModesFeaturesTestsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '6':
                _currentMenuLevel = MenuLevel.TDVKeyDetection;
                await WriteMenuAsync(session);
                break;
            case '7':
                if (ValidateTerminalType(TerminalType.TDV1200))
                {
                    _currentMenuLevel = MenuLevel.TDV1200Tests;
                    await WriteMenuAsync(session);
                }
                else
                {
                    await session.WriteAsync("\r\n\x1b[31mError: TDV1200 tests require TDV1200 terminal.\x1b[0m\r\n");
                    await WriteMenuAsync(session);
                }
                break;
            case '8':
                if (ValidateTerminalType(TerminalType.TDV2215))
                {
                    _currentMenuLevel = MenuLevel.TDV2215Tests;
                    await WriteMenuAsync(session);
                }
                else
                {
                    await session.WriteAsync("\r\n\x1b[31mError: TDV2215 tests require TDV2215 terminal.\x1b[0m\r\n");
                    await WriteMenuAsync(session);
                }
                break;
            case '9':
                if (ValidateTerminalType(TerminalType.TDV2200))
                {
                    _currentMenuLevel = MenuLevel.TDV2200Tests;
                    await WriteMenuAsync(session);
                }
                else
                {
                    await session.WriteAsync("\r\n\x1b[31mError: TDV2200 tests require TDV2200 terminal.\x1b[0m\r\n");
                    await WriteMenuAsync(session);
                }
                break;
            case 'A':
                await RunTDV_ComprehensiveDemoAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
        }
    }

    private async Task HandleTDV1200TestsMenuAsync(TelnetSession session, char key)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV1200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV1200 tests require TDV1200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        switch (key)
        {
            case '1':
                await RunTDV1200_2115CompatibilityAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '2':
                await RunTDV1200_NDGraphicsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '3':
                await RunTDV1200_ProtectedAreasAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '4':
                await RunTDV1200_CharacterSetsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
        }
    }

    private async Task HandleTDV2215TestsMenuAsync(TelnetSession session, char key)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2215))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2215 tests require TDV2215 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        switch (key)
        {
            case '1':
                await RunTDV2215_ExtendedModeAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '2':
                await RunTDV2215_TransparentModeAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '3':
                await RunTDV2215_DCSSequencesAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
        }
    }

    private async Task HandleTDV2200TestsMenuAsync(TelnetSession session, char key)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2200 tests require TDV2200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        switch (key)
        {
            case '1':
                await RunTDV2200_GraphicsExtensionAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '2':
                await RunTDV2200_TektronixModeAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '3':
                await RunTDV2200_ISO646VariantsAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
        }
    }

    /// <summary>
    /// The key-detection menu. Every branch runs its interactive test straight away.
    /// </summary>
    /// <param name="session">
    /// The client.
    /// </param>
    /// <param name="key">
    /// The menu key pressed.
    /// </param>
    /// <remarks>
    /// <para><b>Six of these ten branches did nothing at all, and this is why</b></para>
    /// Measured 11 September 2026 by driving the server over a socket: choosing "9. Extended
    /// Control Mode" printed its list of expected sequences, and then four perfectly valid ones -
    /// CSI 28 _, CSI 32 _, CSI 50 _, CSI 16 _ - produced no output whatsoever.
    ///
    /// The cause was a routing gap. Branches 1, 2, 3, 4, 9 and A set a MENU LEVEL and returned to
    /// the main loop, which routes only <c>InputType.Character</c> to a menu handler - an
    /// <c>InputType.EscapeSequence</c> is logged and dropped. So the screen said "press any TDV
    /// function key" while the one thing that could not get through was a TDV function key. The
    /// interactive test behind each branch was correct all along and simply unreachable, unless
    /// the operator happened to press an ordinary letter first.
    ///
    /// Branches 5 to 8 never had the fault - they call their test directly, and that is what all
    /// ten do now. The menu level is still set first so each branch's list of expected sequences
    /// still prints; it is put back when the test returns.
    /// </remarks>
    private async Task HandleTDVKeyDetectionMenuAsync(TelnetSession session, char key)
    {
        switch (key)
        {
            case '1':
                await RunKeyDetectionBranchAsync(session, MenuLevel.TDVFunctionKeys,
                    RunTDV_FunctionKeysInteractiveTestAsync);
                break;
            case '2':
                await RunKeyDetectionBranchAsync(session, MenuLevel.TDVPushKeys,
                    RunTDV_PushKeysInteractiveTestAsync);
                break;
            case '3':
                await RunKeyDetectionBranchAsync(session, MenuLevel.TDVSoftKeys,
                    RunTDV_SoftKeysInteractiveTestAsync);
                break;
            case '4':
                await RunKeyDetectionBranchAsync(session, MenuLevel.TDVControlKeys,
                    RunTDV_ControlKeysInteractiveTestAsync);
                break;
            case '5':
                await RunTDV_2115ControlCodesTestAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '6':
                await RunTDV_ArrowKeysTestAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '7':
                await RunTDV_ModifierKeysTestAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '8':
                await RunTDV_AllKeysInteractiveTestAsync(session);
                await WriteMenuAsync(session); // Return to menu after test
                break;
            case '9':
                await RunKeyDetectionBranchAsync(session, MenuLevel.TDVExtendedControlKeys,
                    RunTDV_ExtendedControlKeysInteractiveTestAsync);
                break;
            case 'A':
            case 'a':
                await RunKeyDetectionBranchAsync(session, MenuLevel.TDVNumpadFunctionKeys,
                    RunTDV_NumpadFunctionKeysInteractiveTestAsync);
                break;
        }
    }

    /// <summary>
    /// Prints one branch's list of expected sequences, runs its interactive test, then comes back.
    /// </summary>
    /// <param name="session">
    /// The client.
    /// </param>
    /// <param name="listLevel">
    /// The menu level whose writer prints the expected sequences for this branch.
    /// </param>
    /// <param name="interactiveTest">
    /// The test to run. It owns the read loop and returns when the operator presses B.
    /// </param>
    /// <remarks>
    /// The level is set only for as long as the test runs, and the test - not the main loop - is
    /// reading, so the main loop never sees it. That is deliberate: those levels are exactly the
    /// ones where the main loop would drop the escape sequences being tested.
    /// </remarks>
    private async Task RunKeyDetectionBranchAsync(
        TelnetSession session,
        MenuLevel listLevel,
        Func<TelnetSession, Task> interactiveTest)
    {
        _currentMenuLevel = listLevel;
        await WriteMenuAsync(session);

        await interactiveTest(session);

        _currentMenuLevel = MenuLevel.TDVKeyDetection;
        await WriteMenuAsync(session);
    }

    private static async Task RunBasicColorsAsync(TelnetSession session)
    {
        await session.WriteAsync("\r\n\x1b[1;33m=== Basic 8 Colors Test ===\x1b[0m\r\n\r\n");
        for (int i = 30; i <= 37; i++)
            await session.WriteAsync($"\x1b[{i}mColor {i}\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunKeyDecoderAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== VT100 Key Decoder ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("Press VT100 keys (arrows, Home/End, Ins/Del, PgUp/PgDn, F1-F12).\r\n");
        await session.WriteAsync("I will show the escape sequence and a decoded name.\r\n");
        await session.WriteAsync("\x1b[1;32mPress Enter to exit this test.\x1b[0m\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Enter) return;

            var visible = ToVisible(input.Value);
            if (input.Name != null)
                await session.WriteAsync($"Seq: {visible}    -> {input.Name}\r\n");
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"Seq: {visible}    -> ?\r\n");
            else if (input.Type == InputType.Character)
                await session.WriteAsync($"Char: '{input.Value}' (0x{((int)input.Value[0]):X2})\r\n");
        }
    }

    private static string ToVisible(string seq)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < seq.Length; i++)
        {
            var ch = seq[i];
            sb.Append(ch switch
            {
                '\x1b' => "<ESC>",
                '\r' => "<CR>",
                '\n' => "<LF>",
                '\t' => "<TAB>",
                _ when char.IsControl(ch) => $"<0x{((int)ch):X2}>",
                _ => ch.ToString()
            });
        }
        return sb.ToString();
    }

    private static async Task WriteWelcomeAsync(TelnetSession session)
    {
        // Clear and draw header box similar to original server
        const int boxWidth = 53;
        const string border = "\x1b[1;36m"; // bold cyan
        const string reset = "\x1b[0m";

        // Determine box-drawing characters based on terminal type
        var box = GetBoxChars(session.Negotiator.TerminalType);

        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync(border + box.TopLeft + new string(box.Horizontal, boxWidth) + box.TopRight + "\r\n");
        await session.WriteAsync(box.Vertical + new string(' ', boxWidth) + box.Vertical + "\r\n");
        await session.WriteAsync(DrawCentered(boxWidth, $"RetroTerm Test Server v{BuildInfo.Version}", "\x1b[1;33m", border, box.Vertical));
        await session.WriteAsync(box.Vertical + new string(' ', boxWidth) + box.Vertical + "\r\n");
        await session.WriteAsync(DrawCentered(boxWidth, $"Build: {BuildInfo.BuildDateTimeString}", "\x1b[2;37m", border, box.Vertical));
        await session.WriteAsync(box.Vertical + new string(' ', boxWidth) + box.Vertical + "\r\n");
        await session.WriteAsync(DrawCentered(boxWidth, "VT100/ANSI Escape Sequence Tests", "\x1b[0;37m", border, box.Vertical));
        await session.WriteAsync(box.Vertical + new string(' ', boxWidth) + box.Vertical + "\r\n");
        await session.WriteAsync(box.BottomLeft + new string(box.Horizontal, boxWidth) + box.BottomRight + reset + "\r\n\r\n");
    }

    private static BoxChars GetBoxChars(string terminalType)
    {
        // Only use UTF-8 box drawing for explicitly modern terminals
        // Default to ASCII for safety (TDV, VT100, unknown, etc.)
        if (terminalType.Equals("xterm", StringComparison.OrdinalIgnoreCase) ||
            terminalType.Equals("ansi", StringComparison.OrdinalIgnoreCase))
        {
            return new BoxChars
            {
                TopLeft = '╔',
                TopRight = '╗',
                BottomLeft = '╚',
                BottomRight = '╝',
                Horizontal = '═',
                Vertical = '║'
            };
        }

        // Default: Plain ASCII for all other terminals (TDV, VT100, unknown)
        return new BoxChars
        {
            TopLeft = '+',
            TopRight = '+',
            BottomLeft = '+',
            BottomRight = '+',
            Horizontal = '-',
            Vertical = '|'
        };
    }

    private record BoxChars
    {
        public char TopLeft { get; init; }
        public char TopRight { get; init; }
        public char BottomLeft { get; init; }
        public char BottomRight { get; init; }
        public char Horizontal { get; init; }
        public char Vertical { get; init; }
    }

    private async Task DetectTerminalTypeAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[1;33m=== Terminal Type Detection ===\x1b[0m\r\n\r\n");

        // Step 1: Try TERMINAL-TYPE negotiation (already sent by Negotiator)
        await session.WriteAsync("Attempting auto-detection...\r\n");
        await session.WriteAsync("1. Checking TERMINAL-TYPE negotiation...\r\n");

        var detectedType = await TryDetectViaTerminalTypeAsync(session);
        if (detectedType != TerminalType.Unknown)
        {
            _detectedTerminalType = detectedType;
            await session.WriteAsync($"\x1b[32m[OK] Detected via TERMINAL-TYPE: {_detectedTerminalType}\x1b[0m\r\n\r\n");
            return;
        }

        // Step 2: Try Device Attributes (DA) parsing
        await session.WriteAsync("2. Checking Device Attributes (DA)...\r\n");
        detectedType = await TryDetectViaDeviceAttributesAsync(session);
        if (detectedType != TerminalType.Unknown)
        {
            _detectedTerminalType = detectedType;
            await session.WriteAsync($"\x1b[32m[OK] Detected via DA: {_detectedTerminalType}\x1b[0m\r\n\r\n");
            return;
        }

        // Step 3: Fall back to manual selection
        await session.WriteAsync("3. Auto-detection failed, manual selection required...\r\n\r\n");
        await ManualTerminalTypeSelectionAsync(session);
    }

    /// <summary>
    /// Attempts to detect terminal type via TERMINAL-TYPE negotiation
    /// </summary>
    private async Task<TerminalType> TryDetectViaTerminalTypeAsync(TelnetSession session)
    {
        var buffer = new byte[256];
        var timeout = DateTime.Now.AddMilliseconds(1000); // 1 second timeout

        while (DateTime.Now < timeout)
        {
            if (session.Stream.DataAvailable)
            {
                var bytesRead = await session.ReadRawAsync(buffer);
                if (bytesRead > 0)
                {
                    if (session.TryParseTerminalType(buffer.AsSpan(0, bytesRead), out var terminalType))
                    {
                        return ParseTerminalTypeString(terminalType);
                    }
                }
            }
            await Task.Delay(50);
        }

        return TerminalType.Unknown;
    }

    /// <summary>
    /// Attempts to detect terminal type via Device Attributes (DA) queries
    /// </summary>
    private async Task<TerminalType> TryDetectViaDeviceAttributesAsync(TelnetSession session)
    {
        // Send Primary DA query
        await session.WriteAsync("\x1b[c");
        await Task.Delay(200);

        // Send Secondary DA query
        await session.WriteAsync("\x1b[>c");
        await Task.Delay(500);

        // Try to read DA responses
        var buffer = new byte[256];
        var timeout = DateTime.Now.AddMilliseconds(1000);
        var responseBuffer = new StringBuilder();

        while (DateTime.Now < timeout)
        {
            if (session.Stream.DataAvailable)
            {
                var bytesRead = await session.ReadRawAsync(buffer);
                if (bytesRead > 0)
                {
                    // Strip IAC but keep escape sequences
                    var text = TelnetCodec.StripIac(buffer.AsSpan(0, bytesRead));
                    responseBuffer.Append(text);
                }
            }
            await Task.Delay(50);
        }

        var response = responseBuffer.ToString();

        // Parse Secondary DA response: ESC [ > Ps ; Pv ; Pc c
        // Ps = firmware ID: 115 (TDV2115/2215), 120 (TDV1200), 220 (TDV2200)
        if (response.Contains("\x1b[>"))
        {
            // Extract firmware ID from response
            var match = System.Text.RegularExpressions.Regex.Match(response, @"\x1b\[>(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int firmwareId))
            {
                return firmwareId switch
                {
                    115 => TerminalType.TDV2215, // TDV2115/2215
                    120 => TerminalType.TDV1200,
                    220 => TerminalType.TDV2200,

                    // DEC's own Secondary DA identity table, transcribed from the DECSET/DA section
                    // of spec\DEC\xterm-ctlseqs.txt on 25 August 2026 rather than recalled.
                    //
                    // Their absence was not a small gap. A VT340 answered this server's own DA query
                    // with ESC[>19;10;0c - correct, and exactly what TerminalProfile.VT340 defines -
                    // and the server, having no case for 19, reported "auto-detection failed". That
                    // reads as a fault in the terminal, and it sent two people looking for a bug in
                    // the emulator that was never there.
                    0 => TerminalType.VT100,
                    1 => TerminalType.VT220,
                    2 => TerminalType.VT240,   // or VT241
                    18 => TerminalType.VT330,
                    19 => TerminalType.VT340,
                    24 => TerminalType.VT320,
                    32 => TerminalType.VT382,
                    41 => TerminalType.VT420,

                    _ => TerminalType.Unknown
                };
            }
        }

        // Parse Primary DA response: ESC [ ? Ps ; Pc c
        // For TDV terminals, this might contain model info
        if (response.Contains("\x1b[?"))
        {
            // THE FIRST PARAMETER, parsed - not a substring hunt through the whole reply.
            //
            // The old form asked whether the response "contains 220", which is true of a VT340's
            // ESC[?63;1;3;4;6;7;8c only by accident of digits and false for reasons nobody could
            // predict. Matching a number by looking for its digits anywhere in a parameter list is
            // how a classifier gets a right answer for a wrong reason.
            var primary = System.Text.RegularExpressions.Regex.Match(response, @"\x1b\[\?(\d+)");
            if (primary.Success && int.TryParse(primary.Groups[1].Value, out int model))
            {
                switch (model)
                {
                    case 1: return TerminalType.VT100;
                    case 6: return TerminalType.VT100;   // VT102, near enough for this menu
                    case 62: return TerminalType.VT220;  // VT200 family
                    case 63: return TerminalType.VT320;  // VT300 family - see below
                    case 64: return TerminalType.VT420;
                }
            }

            // A VT330 and a VT340 both answer 63 to the PRIMARY query - the family, not the model -
            // so the primary reply alone cannot tell them apart. The secondary reply above can, and
            // is tried first for exactly that reason. Reaching here with 63 means the secondary
            // never arrived, and VT320 is the honest answer to "some VT300-family terminal".

            // TDV terminals, kept as they were.
            if (response.Contains("TDV1200"))
                return TerminalType.TDV1200;
            if (response.Contains("TDV2215") || response.Contains("2215"))
                return TerminalType.TDV2215;
            if (response.Contains("TDV2200"))
                return TerminalType.TDV2200;
        }

        return TerminalType.Unknown;
    }

    /// <summary>
    /// Parses terminal type string to TerminalType enum
    /// </summary>
    private TerminalType ParseTerminalTypeString(string terminalType)
    {
        if (string.IsNullOrEmpty(terminalType))
            return TerminalType.Unknown;

        var upper = terminalType.ToUpperInvariant();

        if (upper.Contains("TDV1200") || upper.Contains("1200"))
            return TerminalType.TDV1200;
        if (upper.Contains("TDV2215") || upper.Contains("2215"))
            return TerminalType.TDV2215;
        if (upper.Contains("TDV2200") || upper.Contains("2200"))
            return TerminalType.TDV2200;
        if (upper.Contains("TDV2115") || upper.Contains("2115"))
            return TerminalType.TDV2215; // TDV2115 maps to TDV2215
        if (upper.Contains("VT100") || upper.Contains("VT220"))
            return TerminalType.VT100;
        if (upper.Contains("XTERM") || upper.Contains("XTERM"))
            return TerminalType.XTerm;

        return TerminalType.Unknown;
    }

    /// <summary>
    /// Manual terminal type selection (fallback)
    /// </summary>
    private async Task ManualTerminalTypeSelectionAsync(TelnetSession session)
    {
        await session.WriteAsync("Please identify your terminal type:\r\n");
        await session.WriteAsync("1. VT100/VT220 (Standard ANSI)\r\n");
        await session.WriteAsync("2. TDV1200 (Tandberg Data Video 1200)\r\n");
        await session.WriteAsync("3. TDV2215 (Tandberg Data Video 2215)\r\n");
        await session.WriteAsync("4. TDV2200 (Tandberg Data Video 2200)\r\n");
        await session.WriteAsync("5. XTerm/Modern Terminal\r\n");
        await session.WriteAsync("6. Unknown/Other\r\n");
        await session.WriteAsync("\r\n\x1b[1;37mSelect terminal type (1-6) and press Enter: \x1b[0m");

        // Pre-pump: use raw reads + InputParser directly
        var buffer = new byte[256];
        string? choiceStr = null;
        var timeout = System.DateTime.Now.AddMilliseconds(30000); // 30s timeout for manual selection
        while (choiceStr == null && System.DateTime.Now < timeout)
        {
            int n = await session.ReadRawAsync(buffer, timeoutMs: 1000);
            if (n == 0) continue;
            var text = TelnetCodec.StripIac(buffer.AsSpan(0, n));
            if (string.IsNullOrEmpty(text)) continue;
            session.Parser.Feed(text);
            while (session.Parser.TryGetNext(out var parsed))
            {
                if (parsed.Type == InputType.Character)
                {
                    choiceStr = parsed.Value;
                    break;
                }
                if (parsed.Type == InputType.Enter)
                    goto done;
            }
        }
    done:

        if (choiceStr != null && choiceStr.Length > 0)
        {
            _detectedTerminalType = choiceStr switch
            {
                "1" => TerminalType.VT100,
                "2" => TerminalType.TDV1200,
                "3" => TerminalType.TDV2215,
                "4" => TerminalType.TDV2200,
                "5" => TerminalType.XTerm,
                _ => TerminalType.Unknown
            };

            await session.WriteAsync($"\r\n\x1b[32mTerminal type set to: {_detectedTerminalType}\x1b[0m\r\n\r\n");
        }
        else
        {
            _detectedTerminalType = TerminalType.Unknown;
            await session.WriteAsync($"\r\n\x1b[33mNo input received, defaulting to: {_detectedTerminalType}\x1b[0m\r\n\r\n");
        }
    }

    private async Task WriteMenuAsync(TelnetSession session)
    {
        var writer = new StringBuilder();

        switch (_currentMenuLevel)
        {
            case MenuLevel.Main:
                await WriteMainMenuAsync(session, writer);
                break;
            case MenuLevel.StandardTests:
                await WriteStandardTestsMenuAsync(session, writer);
                break;
            case MenuLevel.TDVTests:
                await WriteTDVTestsMenuAsync(session, writer);
                break;
            case MenuLevel.TDV1200Tests:
                await WriteTDV1200TestsMenuAsync(session, writer);
                break;
            case MenuLevel.TDV2215Tests:
                await WriteTDV2215TestsMenuAsync(session, writer);
                break;
            case MenuLevel.TDV2200Tests:
                await WriteTDV2200TestsMenuAsync(session, writer);
                break;
            case MenuLevel.TDVKeyDetection:
                await WriteTDVKeyDetectionMenuAsync(session, writer);
                break;
            case MenuLevel.TDVFunctionKeys:
                await WriteTDVFunctionKeysMenuAsync(session, writer);
                break;
            case MenuLevel.TDVPushKeys:
                await WriteTDVPushKeysMenuAsync(session, writer);
                break;
            case MenuLevel.TDVSoftKeys:
                await WriteTDVSoftKeysMenuAsync(session, writer);
                break;
            case MenuLevel.TDVControlKeys:
                await WriteTDVControlKeysMenuAsync(session, writer);
                break;
            case MenuLevel.TDVExtendedControlKeys:
                await WriteTDVExtendedControlKeysMenuAsync(session, writer);
                break;
            case MenuLevel.TDVNumpadFunctionKeys:
                await WriteTDVNumpadFunctionKeysMenuAsync(session, writer);
                break;
            case MenuLevel.DECTests:
                WriteDECTestsMenu(writer);
                break;
            case MenuLevel.XtermTests:
                WriteXtermTestsMenu(writer);
                break;
            case MenuLevel.GraphicsTests:
                WriteGraphicsTestsMenu(writer);
                break;
        }

        writer.Append("\x1b[1;37mSelect option: \x1b[0m");
        await session.WriteAsync(writer.ToString());
    }

    private async Task WriteMainMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== Main Menu ===\x1b[0m");
        writer.AppendLine("1. Standard Tests (VT100/ANSI)");

        // Add TDV-specific tests if TDV terminal detected
        if (_detectedTerminalType == TerminalType.TDV1200 ||
            _detectedTerminalType == TerminalType.TDV2215 ||
            _detectedTerminalType == TerminalType.TDV2200)
        {
            writer.AppendLine("2. TDV Terminal Tests");
        }

        writer.AppendLine("3. DEC Terminal Tests (VT220/320/340/420)");
        writer.AppendLine("4. xterm / Modern Terminal Tests");
        writer.AppendLine("5. Graphics Tests (Sixel, ReGIS, Tektronix)");
        writer.AppendLine("I. Terminal Information");
    }

    private async Task WriteStandardTestsMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== Standard Tests (VT100/ANSI) ===\x1b[0m");
        writer.AppendLine("1. Basic Colors");
        writer.AppendLine("2. Cursor Movement");
        writer.AppendLine("3. Character Attributes");
        writer.AppendLine("4. Scrolling Test");
        writer.AppendLine("5. Line Drawing Characters");
        writer.AppendLine("6. Screen Clear Test");
        writer.AppendLine("7. 256 Color Test");
        writer.AppendLine("8. Tab Stops (HT, HTS, TBC)");
        writer.AppendLine("9. Character Sets (G0-G3, DEC Graphics)");
        writer.AppendLine("A. Scrolling Region (DECSTBM)");
        writer.AppendLine("B. Cursor Save/Restore (DECSC/DECRC)");
        writer.AppendLine("D. VT100 Key Decoder (Enter to exit)");
        writer.AppendLine("S. Smooth scroll (DECSCLM) - the same lines slid, then jumped");
        writer.AppendLine("0/B. Back to Main Menu");
    }

    private async Task WriteTDVTestsMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV Terminal Tests ===\x1b[0m");
        writer.AppendLine("1. Query/Response - DA, CPR, DSR, Terminal ID");
        writer.AppendLine("2. Character Sets - Graphics, Math, Greek, etc.");
        writer.AppendLine("3. Drawing Operations - Rectangle ops (NDSAR, NDFC, etc.)");
        writer.AppendLine("4. Function Keys - Press F1-F12 to see sequences");
        writer.AppendLine("5. Modes & Features - Roll type, line wrap, double width");
        writer.AppendLine("6. Key Detection - Submenu for key types");

        // Only show version-specific menus for the detected terminal type
        if (_detectedTerminalType == TerminalType.TDV1200)
        {
            writer.AppendLine("7. TDV1200 Tests - TDV1200-specific features");
        }
        else if (_detectedTerminalType == TerminalType.TDV2215)
        {
            writer.AppendLine("8. TDV2215 Tests - TDV2215-specific features");
        }
        else if (_detectedTerminalType == TerminalType.TDV2200)
        {
            writer.AppendLine("9. TDV2200 Tests - TDV2200-specific features");
        }

        writer.AppendLine("A. Comprehensive Demo - Full TDV feature demo");
        writer.AppendLine("0/B. Back to Main Menu");
    }

    private async Task WriteTDV1200TestsMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV1200 Tests ===\x1b[0m");
        writer.AppendLine("1. 2115 Compatibility - TDV1200 compatibility mode");
        writer.AppendLine("2. ND Graphics - ND-specific graphics sequences");
        writer.AppendLine("3. Protected Areas - DECSCA, work areas, message lamps");
        writer.AppendLine("4. Character Sets - TDV1200 character set switching");
        writer.AppendLine("0/B. Back to TDV Menu");
    }

    private async Task WriteTDV2215TestsMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV2215 Tests ===\x1b[0m");
        writer.AppendLine("1. Extended Mode - the EC switch, CSI 66 h / 66 l");
        writer.AppendLine("2. Transparent Mode - why the host cannot set it");
        writer.AppendLine("3. DCS Sequences - PUSH-key programming");
        writer.AppendLine("0/B. Back to TDV Menu");
    }

    private async Task WriteTDV2200TestsMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV2200 Tests ===\x1b[0m");
        writer.AppendLine("1. Graphics Extension - Graphics extension board");
        writer.AppendLine("2. Tektronix Mode - Tektronix 4010 compatibility");
        writer.AppendLine("3. ISO 646 Variants - Norwegian, Swedish, etc.");
        writer.AppendLine("0/B. Back to TDV Menu");
    }

    private async Task WriteTDVKeyDetectionMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV Key Detection ===\x1b[0m");
        writer.AppendLine("1. Function Keys - F1-F20 interactive test");
        writer.AppendLine("2. PUSH Keys - Programmable keys test");
        writer.AppendLine("3. Soft Keys - Menu keys test");
        writer.AppendLine("4. Control Keys - TAB, ROLL, etc.");
        writer.AppendLine("5. 2115 C0 Codes - SO, SI control codes");
        writer.AppendLine("6. Arrow Keys - Navigation keys");
        writer.AppendLine("7. Modifiers - Ctrl, Shift, Alt combos");
        writer.AppendLine("8. All Keys - Comprehensive test");
        writer.AppendLine("9. Extended Control Mode - CSI nn _ sequences");
        writer.AppendLine("A. Numpad Function Mode - CSI nn _ sequences (numpad)");
        writer.AppendLine("0/B. Back to TDV Menu");
    }

    private async Task WriteTDVFunctionKeysMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV Function Keys Test ===\x1b[0m");
        writer.AppendLine("Press any function key (F1-F20) to test...");
        writer.AppendLine("Press 'B' to go back to Key Detection menu");
        writer.AppendLine();
        writer.AppendLine("\x1b[33mVT220 mode:\x1b[0m");
        writer.AppendLine("  F1-F4:   ESC O P/Q/R/S (SS3) or ESC[11~-14~");
        writer.AppendLine("  F5-F12:  ESC[15~ to ESC[24~");
        writer.AppendLine("  F13-F20: ESC[25~ to ESC[34~");
        writer.AppendLine();
        writer.AppendLine("\x1b[33mTDV Extended Control Mode ON:\x1b[0m");
        writer.AppendLine("  F1-F4:   CSI 50-59 _ (includes Ctrl+F2, Ctrl+F3)");
        writer.AppendLine("  F5-F8:   CSI 60-67 _");
        writer.AppendLine();
        writer.AppendLine("\x1b[32mPress any function key now...\x1b[0m");
    }

    private async Task WriteTDVPushKeysMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV PUSH Keys Test ===\x1b[0m");
        writer.AppendLine("Press any PUSH key (1-8) to test...");
        writer.AppendLine("Press 'B' to go back to Key Detection menu");
        writer.AppendLine();
        writer.AppendLine("\x1b[33mExpected sequences:\x1b[0m");
        writer.AppendLine("PUSH1-PUSH8: ESC[?1~ to ESC[?8~");
        writer.AppendLine("DCS sequences: ESCPN1ESC\\ (normal)");
        writer.AppendLine("DCS sequences: ESCPS1ESC\\ (shifted)");
        writer.AppendLine();
        writer.AppendLine("\x1b[32mPress any PUSH key now...\x1b[0m");
    }

    private async Task WriteTDVSoftKeysMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV Soft Keys Test ===\x1b[0m");
        writer.AppendLine("Press any soft key (1-8) to test...");
        writer.AppendLine("Press 'B' to go back to Key Detection menu");
        writer.AppendLine();
        writer.AppendLine("\x1b[33mExpected sequences:\x1b[0m");
        writer.AppendLine("SOFT1-SOFT8: ESC[?A to ESC[?H");
        writer.AppendLine();
        writer.AppendLine("\x1b[32mPress any soft key now...\x1b[0m");
    }

    private async Task WriteTDVControlKeysMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV Control Keys Test ===\x1b[0m");
        writer.AppendLine("Press any TDV control key to test...");
        writer.AppendLine("Press 'B' to go back to Key Detection menu");
        writer.AppendLine();
        writer.AppendLine("\x1b[33mExpected sequences:\x1b[0m");
        writer.AppendLine("TAB_RIGHT: HT (0x09)");
        writer.AppendLine("TAB_LEFT: ESC[Z");
        writer.AppendLine("ROLL_UP: FF (0x0C)");
        writer.AppendLine("ROLL_DOWN: ETB (0x17)");
        writer.AppendLine("ERASE_PAGE: EM (0x19)");
        writer.AppendLine("ERASE_LINE: EOT (0x04)");
        writer.AppendLine("DEL_LINE: ESC[M");
        writer.AppendLine("INS_LINE: ESC[L");
        writer.AppendLine("DEL_CHAR: ESC[P");
        writer.AppendLine("INS_CHAR: ESC[@");
        writer.AppendLine();
        writer.AppendLine("\x1b[32mPress any control key now...\x1b[0m");
    }

    private async Task WriteTDVExtendedControlKeysMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV Extended Control Mode Keys Test ===\x1b[0m");
        writer.AppendLine("Tests CSI nn _ sequences (Extended Control Mode ON).");
        writer.AppendLine("Press any TDV function/editing/system key to test...");
        writer.AppendLine("Press 'B' to go back to Key Detection menu");
        writer.AppendLine();
        writer.AppendLine("\x1b[33mExpected CSI nn _ sequences:\x1b[0m");
        writer.AppendLine("  MERK/FELT/AVSN/SETN/ORD: CSI 00-09 _");
        writer.AppendLine("  STRYK/KOPI/FLYTT:        CSI 10-15 _");
        writer.AppendLine("  TAB+/TAB-/F48/F49:       CSI 16-21 _");
        writer.AppendLine("  <<>>/JUST/<>><:           CSI 22-27 _");
        writer.AppendLine("  RollUp/ANGRE/RollDown:   CSI 28-33 _");
        writer.AppendLine("  FieldLeft/FieldRight:    CSI 34-37 _");
        writer.AppendLine("  TABLeft/TABRight:        CSI 38-41 _");
        writer.AppendLine("  FUNK/SKRIV/HJELP/SLUTT:  CSI 42-49 _");
        writer.AppendLine("  F1-F4:                   CSI 50-59 _");
        writer.AppendLine("  F5-F8:                   CSI 60-67 _");
        writer.AppendLine("  EKSP/Squiggle:           CSI 82-85 _");
        writer.AppendLine("  NewParagraph:            CSI 86-87 _");
        writer.AppendLine();
        writer.AppendLine("\x1b[32mPress any key now...\x1b[0m");
    }

    private async Task WriteTDVNumpadFunctionKeysMenuAsync(TelnetSession session, StringBuilder writer)
    {
        writer.AppendLine("\x1b[1;36m=== TDV Numpad Function Mode Keys Test ===\x1b[0m");
        writer.AppendLine("Tests CSI nn _ sequences (all numpad keys use _ terminator).");
        writer.AppendLine("Requires: Extended Control Mode ON + Numpad switch = Function");
        writer.AppendLine("Press 'B' to go back to Key Detection menu");
        writer.AppendLine();
        writer.AppendLine("\x1b[33mExpected CSI nn _ sequences:\x1b[0m");
        writer.AppendLine("  0:     CSI 68 _    7: CSI 75 _");
        writer.AppendLine("  1:     CSI 69 _    8: CSI 76 _");
        writer.AppendLine("  2:     CSI 70 _    9: CSI 77 _");
        writer.AppendLine("  3:     CSI 71 _    .: CSI 78 _");
        writer.AppendLine("  4:     CSI 72 _    -: CSI 79 _");
        writer.AppendLine("  5:     CSI 73 _   SP: CSI 80 _");
        writer.AppendLine("  6:     CSI 74 _  ENT: CSI 81 _");
        writer.AppendLine();
        writer.AppendLine("\x1b[32mPress any numpad key now...\x1b[0m");
    }

    private static string DrawCentered(int innerWidth, string text, string textColor, string borderColor, char vertical = '║')
    {
        int textLength = text.Length;
        int totalPadding = innerWidth - textLength;
        if (totalPadding < 0) totalPadding = 0;
        int leftPadding = totalPadding / 2;
        int rightPadding = totalPadding - leftPadding;
        return borderColor + vertical + new string(' ', leftPadding) + textColor + text + "\x1b[0m" + borderColor + new string(' ', rightPadding) + vertical + "\r\n";
    }

    /// <summary>
    /// Escape sequences the menu loop received and ignored, oldest first.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a record rather than a second reader</b></para>
    /// The input pump has exactly ONE reader. When the menu loop is running, that reader is the
    /// menu loop, and anything else calling <see cref="CaptureResponseWithPollingAsync"/> on the
    /// same session is competing with it for the same messages.
    ///
    /// The TcpFlow tests did exactly that, and the console output shows all three consequences in
    /// order: the loop read the terminal's answer first and threw it away as an escape sequence in
    /// the menu, the capture then started with nothing left to find, and when the capture's own
    /// one-second timeout fired it cancelled the read the MENU LOOP was sitting in, which ended
    /// the session. Every test after that got a closed channel.
    ///
    /// So a test that wants the main loop running observes what the loop SAW, here, instead of
    /// trying to read the pump behind its back. Measured 10 September 2026.
    /// </remarks>
    internal System.Collections.Concurrent.ConcurrentQueue<string> EscapeSequencesSeen { get; }
        = new System.Collections.Concurrent.ConcurrentQueue<string>();

    /// <summary>
    /// Waits for the menu loop to report an escape sequence from the terminal.
    /// </summary>
    /// <param name="maxWaitMs">
    /// How long to wait before giving up.
    /// </param>
    /// <returns>
    /// The sequence, or null if none arrived in time.
    /// </returns>
    internal async Task<string?> WaitForEscapeSequenceAsync(int maxWaitMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            if (EscapeSequencesSeen.TryDequeue(out var seen)) return seen;
            await Task.Delay(10);
        }
        return null;
    }

    /// <summary>
    /// Captures response from terminal after sending a query command.
    /// Reads from the pump channel — the main loop is blocked waiting for
    /// the test method to return, so we are the sole reader.
    /// </summary>
    /// <remarks>
    /// THE SOLE READER PART IS A REQUIREMENT, not a description of a happy accident. Calling this
    /// while the menu loop is running takes messages out from under it and, on timeout, cancels
    /// the read the loop is sitting in. Use <see cref="WaitForEscapeSequenceAsync"/> in that case.
    /// </remarks>
    internal static async Task<string?> CaptureResponseWithPollingAsync(TelnetSession session, int maxWaitMs = 1000)
    {
        System.Console.WriteLine($"[TestServer] Starting response capture (max wait: {maxWaitMs}ms) via pump channel");

        using var cts = new System.Threading.CancellationTokenSource(maxWaitMs);
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var input = await session.ReadInputAsync(cts.Token);
                if (input.Type == InputType.EscapeSequence)
                {
                    System.Console.WriteLine($"[TestServer] Captured escape response: {TDVResponseValidator.ToVisibleString(input.Value)}");
                    return input.Value;
                }
                // Discard non-escape input during capture
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }

        System.Console.WriteLine($"[TestServer] [WARN] No response captured within {maxWaitMs}ms timeout");
        return null;
    }

    private static async Task RunCursorMovementAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== Cursor Movement Test ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("Drawing a box using cursor positioning:\r\n\r\n");
        await session.WriteAsync("\x1b[5;10H┌──────────────────┐");
        for (int i = 1; i <= 5; i++)
        {
            await session.WriteAsync($"\x1b[{5 + i};10H│");
            await session.WriteAsync($"\x1b[{5 + i};29H│");
        }
        await session.WriteAsync("\x1b[11;10H└──────────────────┘");
        await session.WriteAsync("\x1b[7;14H\x1b[1;36mCursor Magic!\x1b[0m");
        await session.WriteAsync("\x1b[8;13H\x1b[33mESC[row;colH\x1b[0m");
        await session.WriteAsync("\x1b[13;1H\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunCharacterAttributesAsync(TelnetSession session)
    {
        await session.WriteAsync("\r\n\x1b[1;33m=== Character Attributes Test ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("Normal text\r\n");
        await Task.Delay(300);
        await session.WriteAsync("\x1b[1mBold text (ESC[1m)\x1b[0m\r\n");
        await Task.Delay(300);
        await session.WriteAsync("\x1b[2mDim text (ESC[2m)\x1b[0m\r\n");
        await Task.Delay(300);
        await session.WriteAsync("\x1b[4mUnderlined text (ESC[4m)\x1b[0m\r\n");
        await Task.Delay(300);
        await session.WriteAsync("\x1b[5mBlinking text (ESC[5m)\x1b[0m\r\n");
        await Task.Delay(300);
        await session.WriteAsync("\x1b[7mReverse video (ESC[7m)\x1b[0m\r\n");
        await Task.Delay(300);
        await session.WriteAsync("\x1b[8mHidden text (ESC[8m)\x1b[0m (should be invisible)\r\n");
        await Task.Delay(300);
        await session.WriteAsync("\r\nCombined:\r\n\x1b[1;4;32mBold+Underline+Green\x1b[0m\r\n\x1b[1;7;33mBold+Reverse+Yellow\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunScrollingAsync(TelnetSession session)
    {
        await session.WriteAsync("\r\n\x1b[1;33m=== Scrolling Test ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("Generating 30 lines to test scrolling...\r\n\r\n");
        for (int i = 1; i <= 30; i++)
        {
            int color = 31 + (i % 7);
            await session.WriteAsync($"\x1b[{color}mLine {i,2}: The quick brown fox jumps over the lazy dog\x1b[0m\r\n");
            await Task.Delay(200); // Add delay to see scrolling effect
        }
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunLineDrawingAsync(TelnetSession session)
    {
        // THE PAUSES BELOW ARE THIS TEST'S OWN, not the terminal being slow. They are here so a
        // person can watch each row appear rather than have the whole box arrive at once.
        //
        // They used to be 200ms a line, which made the test take the better part of ten seconds and
        // read as a performance fault - Ronny's reaction on 25 August 2026 was "5 is slow as fuck,
        // dont know why". Nothing was wrong: the test was simply dawdling. Shortened so the pacing
        // still reads as deliberate without looking like the emulator struggling with box-drawing
        // glyphs, which is exactly what it looked like.
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== Line Drawing Characters ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("Box drawing characters:\r\n\r\n");
        await session.WriteAsync("  ┌─────────┬─────────┐\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  │ Cell 1  │ Cell 2  │\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  ├─────────┼─────────┤\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  │ Cell 3  │ Cell 4  │\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  └─────────┴─────────┘\r\n");
        await Task.Delay(60);
        await session.WriteAsync("\r\nDouble lines:\r\n\r\n");
        await session.WriteAsync("  ╔═════════╦═════════╗\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  ║ Header  ║  Value  ║\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  ╠═════════╬═════════╣\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  ║ Item 1  ║   100   ║\r\n");
        await Task.Delay(40);
        await session.WriteAsync("  ╚═════════╩═════════╝\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunScreenClearAsync(TelnetSession session)
    {
        await session.WriteAsync("\r\n\x1b[1;33m=== Screen Clear Test ===\x1b[0m\r\n\r\n");
        for (int i = 0; i < 10; i++)
        {
            await session.WriteAsync($"Line {i + 1}: Sample content for clear test\r\n");
            await Task.Delay(200);
        }
        await session.WriteAsync("\r\nWait 1 second, then clearing screen...\r\n");
        await Task.Delay(1000);
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\x1b[1;32mScreen cleared! (ESC[2J + ESC[H)\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task Run256ColorsAsync(TelnetSession session)
    {
        await session.WriteAsync("\r\n\x1b[1;33m=== 256 Color Test ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("System colors (0-15):\r\n");
        for (int i = 0; i < 16; i++)
        {
            await session.WriteAsync($"\x1b[48;5;{i}m  ");
            if (i == 7 || i == 15)
            {
                await session.WriteAsync("\x1b[0m\r\n");
                await Task.Delay(200);
            }
        }
        await session.WriteAsync("\r\nColor cube (16-231):\r\n");
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    int color = 16 + (r * 36) + (g * 6) + b;
                    await session.WriteAsync($"\x1b[48;5;{color}m ");
                }
                await session.WriteAsync("\x1b[0m ");
            }
            await session.WriteAsync("\r\n");
        }
        await session.WriteAsync("\r\nGrayscale (232-255):\r\n");
        for (int i = 232; i < 256; i++)
            await session.WriteAsync($"\x1b[48;5;{i}m  ");
        await session.WriteAsync("\x1b[0m\r\n\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunTabStopsAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== Tab Stops Test ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("Default tab stops (every 8 columns):\r\n");
        await session.WriteAsync("A\tB\tC\tD\tE\tF\r\n");
        await session.WriteAsync("12345678\t12345678\t12345678\t12345678\r\n\r\n");
        await session.WriteAsync("Custom tab stops at columns 5, 15, 25, 35:\r\n");
        await session.WriteAsync("\x1b[5GHTS\x1b[15GHTS\x1b[25GHTS\x1b[35GHTS\r\n");
        await session.WriteAsync("\x1b[1GA\tB\tC\tD\r\n\r\n");
        await session.WriteAsync("After clearing all tabs (TBC = ESC[3g):\r\n");
        await session.WriteAsync("\x1b[3g");
        await session.WriteAsync("A\tB\tC (no tabs, should stay on same line)\r\n\r\n");
        await session.WriteAsync("Tab stop commands:\r\n  HT (\\t): next tab\r\n  HTS (ESC H): set tab\r\n  TBC (ESC[g): clear current\r\n  TBC (ESC[3g): clear all\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunCharacterSetsAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== Character Sets Test (G0-G3) ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("G0: US ASCII (default):\r\n  ABCDEFGHIJKLMNOPQRSTUVWXYZ\r\n  abcdefghijklmnopqrstuvwxyz\r\n  0123456789 !@#$%^&*()\r\n\r\n");
        await session.WriteAsync("G0: DEC Special Graphics (ESC(0):\r\n");
        await session.WriteAsync("\x1b(0");
        await session.WriteAsync("lqqqqqqqqqwqqqqqqqqqk\r\nx         x         x\r\nx         x         x\r\ntqqqqqqqqqnqqqqqqqqqu\r\nx         x         x\r\nx         x         x\r\nmqqqqqqqqqvqqqqqqqqqj\r\n");
        await session.WriteAsync("\x1b(B");
        await session.WriteAsync("\r\nDEC Special Graphics mapping:\r\n  j=┘ k=┐ l=┌ m=└ n=┼ q=─ t=├ u=┤ v=┴ w=┬ x=│\r\n");
        await session.WriteAsync("\r\nCommands:\r\n  ESC(B G0=US ASCII\r\n  ESC(0 G0=DEC Special\r\n  ESC)B G1=US\r\n  ESC)0 G1=DEC\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunInsertDeleteAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== Insert/Delete Lines Test ===\x1b[0m\r\n\r\n");
        for (int i = 1; i <= 5; i++) await session.WriteAsync($"Line {i}\r\n");
        await session.WriteAsync("\x1b[5;1H\x1b[3;1H\x1b[2L\x1b[31mInserted Line A\x1b[0m");
        await session.WriteAsync("\x1b[4;1H\x1b[32mInserted Line B\x1b[0m");
        await session.WriteAsync("\x1b[4;1H\x1b[1M");
        await session.WriteAsync("\x1b[10;1H\r\nCommands:\r\n  IL (ESC[nL)\r\n  DL (ESC[nM)\r\n  ICH (ESC[n@)\r\n  DCH (ESC[nP)\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunScrollingRegionAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\x1b[1;33m=== Scrolling Region Test (DECSTBM) ===\x1b[0m");
        await session.WriteAsync("\x1b[2;1H\x1b[32m╔════════════════════════════════════════╗\x1b[0m");
        await session.WriteAsync("\x1b[3;1H\x1b[32m║\x1b[0m Scrolling region: lines 7-17           \x1b[32m║\x1b[0m");
        await session.WriteAsync("\x1b[4;1H\x1b[32m╚════════════════════════════════════════╝\x1b[0m");
        await session.WriteAsync("\x1b[6;1H\x1b[36m─── Scrolling Region Start (line 7) ───\x1b[0m");
        await session.WriteAsync("\x1b[7;17r\x1b[7;1H");
        for (int i = 1; i <= 15; i++)
        {
            await session.WriteAsync($"Scrolling line {i}\r\n");
            await Task.Delay(300); // Add delay to see scrolling happen
        }
        await session.WriteAsync("\x1b[r\x1b[18;1H\x1b[36m─── Scrolling Region End (line 17) ───\x1b[0m");
        await session.WriteAsync("\x1b[20;1H\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private static async Task RunCursorSaveRestoreAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== Cursor Save/Restore Test ===\x1b[0m\r\n\r\n");
        await session.WriteAsync("1. Writing at position 5,5:\r\n");
        await session.WriteAsync("\x1b[5;5HOriginal Position");
        await Task.Delay(500);
        await session.WriteAsync("\x1b" + "7");
        await session.WriteAsync("\x1b[10;10H\x1b[31mMoved to 10,10\x1b[0m");
        await Task.Delay(800);
        await session.WriteAsync("\x1b" + "8");
        await session.WriteAsync(" <- Restored!\r\n\r\n");
        await session.WriteAsync("Cursor save/restore commands:\r\n  DECSC (ESC 7)\r\n  DECRC (ESC 8)\r\n");
        await session.WriteAsync("\x1b[15;1H\x1b[32mPress Enter to continue...\x1b[0m");
        await session.DrainInputAsync();
        await session.WaitForEnterAsync();
    }

    private async Task RunTDVTestsAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== TDV Terminal Tests ===\x1b[0m\r\n\r\n");
        await session.WriteAsync($"Testing {_detectedTerminalType} terminal...\r\n\r\n");

        // Test ND-specific sequences based on terminal type
        switch (_detectedTerminalType)
        {
            case TerminalType.TDV1200:
                await RunTDV1200TestsAsync(session);
                break;
            case TerminalType.TDV2215:
                await RunTDV2215TestsAsync(session);
                break;
            case TerminalType.TDV2200:
                await RunTDV2200TestsAsync(session);
                break;
            default:
                await session.WriteAsync("TDV tests only available for TDV terminals.\r\n");
                break;
        }

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV1200TestsAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[1;36m=== TDV1200 Specific Tests ===\x1b[0m\r\n\r\n");

        // Test 2115 compatibility mode
        await session.WriteAsync("Testing 2115 compatibility mode:\r\n");
        await session.WriteAsync("Entering 2115 mode: \x1b[66h\r\n");
        await Task.Delay(500);
        await session.WriteAsync("In 2115 mode - testing function keys...\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Exiting 2115 mode: \x1bQ\r\n");
        await Task.Delay(500);

        // Test ND-specific graphics sequences
        await session.WriteAsync("\r\nTesting ND Graphics Sequences:\r\n");
        await session.WriteAsync("NDDWA (Define Work Area): \x1b[10;10;100;50~\r\n");
        await Task.Delay(300);
        // Corners first, then the attribute or character (ND-1200 sections 5.50, 5.36, 5.46, 5.43).
        // NDRAR is 7C '|' and NDFC is 7D '}'; this demo had them the wrong way round.
        await session.WriteAsync("NDSAR (Set Attribute Rectangle): \x1b[10;10;100;50;7z\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDAAR (Add Attribute Rectangle): \x1b[10;10;100;50;2{\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDRAR (Remove Attribute Rectangle): \x1b[10;10;100;50;7|\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDFC (Fill Character Rectangle): \x1b[10;10;100;50;65}\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDSREC (Save Rectangle): \x1b[10;10;100;50u\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDRREC (Restore Rectangle): \x1b[200;200v\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDVIDEO (Graphics Video ON): \x1b[?1<7F\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDVIDEO (Graphics Video OFF): \x1b[?0<7F\r\n");
        await Task.Delay(300);

        // Test double-width/height lines
        await session.WriteAsync("\r\nTesting Double-Width/Height Lines:\r\n");
        await session.WriteAsync("Double-height top: \x1b#3\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Double-height bottom: \x1b#4\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Single-width line: \x1b#5\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Double-width line: \x1b#6\r\n");
        await Task.Delay(300);

        // Protected areas. There is no SPA or EPA sequence to send: the ND-1200 control list,
        // chapter 5, names 69 functions and has neither, nor DECSCA. What this program implements
        // is DECSCA, CSI Ps " q. The two lines that used to be here ended their CSI at the 'S' of
        // "SPA", so a terminal saw SU - scroll up - and then printed "PA".
        await session.WriteAsync("\r\nProtected areas (DECSCA; the ND-1200 list has no SPA/EPA):\r\n");
        await session.WriteBytesAsync(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'"', (byte)'q' });
        await session.WriteAsync("marked protected");
        await session.WriteBytesAsync(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'"', (byte)'q' });
        await session.WriteAsync(" / not protected\r\n");
        await Task.Delay(300);
        await session.WriteAsync("NDDWA (Define Work Area): \x1b[1;1;20;20~\r\n");
        await Task.Delay(300);

        // Test message LEDs
        // The message lamps, and these ARE real sequences - ND-1200 sections 5.37, 5.38 and 5.52.
        // Three operations on four lamps: 0 all, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE. This block
        // used to print the letters "NDCLED" and "NDSLED" on the screen and send nothing.
        await session.WriteAsync("\r\nTesting Message LEDs (4 lamps: EXPAND APPEND BUSY MESSAGE):\r\n");

        var ndsled = TDVSequenceBuilder.BuildNDSLED(4);
        await session.WriteBytesAsync(ndsled);
        await session.WriteAsync($"NDSLED lamp 4, MESSAGE: {TDVSequenceBuilder.ToVisibleString(ndsled)}\r\n");
        await Task.Delay(300);

        var ndbled = TDVSequenceBuilder.BuildNDBLED(3);
        await session.WriteBytesAsync(ndbled);
        await session.WriteAsync($"NDBLED lamp 3, BUSY:    {TDVSequenceBuilder.ToVisibleString(ndbled)}\r\n");
        await Task.Delay(300);

        var ndcled = TDVSequenceBuilder.BuildNDCLED(0);
        await session.WriteBytesAsync(ndcled);
        await session.WriteAsync($"NDCLED all lamps:       {TDVSequenceBuilder.ToVisibleString(ndcled)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("\r\nRetroTerm acts on these as of 11 September 2026 - watch the\r\n");
        await session.WriteAsync("virtual keyboard's four lamps. MESSAGE lights, BUSY blinks,\r\n");
        await session.WriteAsync("then all four clear.\r\n");

        // Test character sets
        await session.WriteAsync("\r\nTesting TDV Character Sets:\r\n");
        await session.WriteAsync("Graphics I: \x1b(1Graphics I characters\r\n");
        await session.WriteAsync("Graphics II: \x1b(2Graphics II characters\r\n");
        await session.WriteAsync("Math: \x1b(3Math characters\r\n");
        await session.WriteAsync("Greek: \x1b(4Greek characters\r\n");
        await session.WriteAsync("Norwegian: \x1b(5Norwegian characters\r\n");
        await session.WriteAsync("Swedish: \x1b(6Swedish characters\r\n");
        await session.WriteAsync("Danish: \x1b(7Danish characters\r\n");
        await session.WriteAsync("Finnish: \x1b(8Finnish characters\r\n");
        await session.WriteAsync("German: \x1b(9German characters\r\n");
        await session.WriteAsync("US ASCII: \x1b(0US ASCII characters\r\n");
    }

    private async Task RunTDV2215TestsAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[1;36m=== TDV2215 Specific Tests ===\x1b[0m\r\n\r\n");

        // Extended mode, corrected 11 September 2026.
        //
        // This block used to SEND ESC [ ? 1 h and call it extended mode. ESC [ ? 1 h is DECCKM -
        // application cursor keys - so the demo silently changed what the arrow keys transmit and
        // never changed it back. Extended operation is the EC switch, mode 66, no private marker.
        await session.WriteAsync("Testing extended mode (EC switch, mode 66):\r\n");
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildExtendedModeEnable());
        await session.WriteAsync("  Sent ESC [ 66 h - Extended Control on\r\n");
        await Task.Delay(500);
        await session.WriteAsync("In extended mode - the full 2215 control set is accepted...\r\n");
        await Task.Delay(500);

        // Transparent mode: nothing is sent, because no host sequence exists for it.
        //
        // The old line here was a string with a missing escape, so it PRINTED the characters
        // 1b[?45h on the screen rather than sending anything, and the ESC [ ? 45 l below it
        // turned OFF xterm reverse-wraparound on anything that understood it. Transparent
        // operation is the Send-Receive Mode soft-switch (2215 section 4.3.1), keyboard only.
        await session.WriteAsync("\r\nTransparent mode is a keyboard soft-switch (SRM, 4.3.1) -\r\n");
        await session.WriteAsync("the host cannot set it, so nothing is sent for it here.\r\n");

        // Test double-width/height lines
        await session.WriteAsync("\r\nTesting Double-Width/Height Lines:\r\n");
        await session.WriteAsync("Double-height top: \x1b#3\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Double-height bottom: \x1b#4\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Single-width line: \x1b#5\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Double-width line: \x1b#6\r\n");
        await Task.Delay(300);

        // Test DCS sequences
        await session.WriteAsync("\r\nTesting DCS sequences:\r\n");
        await session.WriteAsync("PUSH-key programming: \x1bP1;0|PUSH1\x1b\\\r\n");
        await Task.Delay(300);
        await session.WriteAsync("PROGRAM-key loading: \x1bP1;0|PROGRAM1\x1b\\\r\n");
        await Task.Delay(300);
    }

    private async Task RunTDV2200TestsAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[1;36m=== TDV2200 Specific Tests ===\x1b[0m\r\n\r\n");

        // Test graphics extension board
        await session.WriteAsync("Testing graphics extension board:\r\n");
        await session.WriteAsync("Graphics mode: \x1b[?1h\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Graphics operations...\r\n");
        await Task.Delay(500);

        // Test Tektronix 4010 compatibility
        await session.WriteAsync("\r\nTesting Tektronix 4010 compatibility:\r\n");
        await session.WriteAsync("Entering Tektronix mode: \x1b[?38h\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Tektronix graphics commands...\r\n");
        await Task.Delay(500);
        await session.WriteAsync("\x1b[?38l\r\n"); // Exit Tektronix mode

        // Test double-width/height lines
        await session.WriteAsync("\r\nTesting Double-Width/Height Lines:\r\n");
        await session.WriteAsync("Double-height top: \x1b#3\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Double-height bottom: \x1b#4\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Single-width line: \x1b#5\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Double-width line: \x1b#6\r\n");
        await Task.Delay(300);

        // Test ISO 646 variants
        await session.WriteAsync("\r\nTesting ISO 646 variants:\r\n");
        await session.WriteAsync("Norwegian: \x1b[?1;0h\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Swedish: \x1b[?1;1h\r\n");
        await Task.Delay(300);
        await session.WriteAsync("Danish: \x1b[?1;2h\r\n");
        await Task.Delay(300);
    }

    private async Task RunTerminalInfoAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H\r\n\x1b[1;33m=== Terminal Information ===\x1b[0m\r\n\r\n");
        await session.WriteAsync($"Detected Terminal Type: \x1b[1;32m{_detectedTerminalType}\x1b[0m\r\n\r\n");

        await session.WriteAsync("Terminal Capabilities:\r\n");
        switch (_detectedTerminalType)
        {
            case TerminalType.TDV1200:
                await session.WriteAsync("* ISO 646/2022/6429 compliant\r\n");
                await session.WriteAsync("* 2115 compatibility mode\r\n");
                await session.WriteAsync("* ND-specific sequences\r\n");
                await session.WriteAsync("* 10 TDV character sets\r\n");
                await session.WriteAsync("* Protected areas (DECSCA - the ND-1200 list has no SPA/EPA)\r\n");
                await session.WriteAsync("* Work areas (NDDWA)\r\n");
                await session.WriteAsync("* Message LEDs\r\n");
                break;
            case TerminalType.TDV2215:
                await session.WriteAsync("* Extended mode support\r\n");
                await session.WriteAsync("* Extended Control switch (CSI 66)\r\n");
                await session.WriteAsync("* Transparent mode (keyboard soft-switch only)\r\n");
                await session.WriteAsync("* DCS sequences\r\n");
                await session.WriteAsync("* Function key programming\r\n");
                await session.WriteAsync("* All TDV1200 features\r\n");
                break;
            case TerminalType.TDV2200:
                await session.WriteAsync("* Graphics extension board\r\n");
                await session.WriteAsync("* Tektronix 4010 compatibility\r\n");
                await session.WriteAsync("* ISO 646 variants\r\n");
                await session.WriteAsync("* Character mapping\r\n");
                await session.WriteAsync("* Graphics operations\r\n");
                break;
            default:
                await session.WriteAsync("* Standard VT100/ANSI sequences\r\n");
                await session.WriteAsync("* Basic color support\r\n");
                await session.WriteAsync("* Cursor movement\r\n");
                await session.WriteAsync("* Character attributes\r\n");
                break;
        }

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    // TDV1200 Test Methods
    private async Task RunTDV1200_2115CompatibilityAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV1200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV1200 2115 compatibility test requires TDV1200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV1200: 2115 Compatibility Mode ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing 2115 compatibility mode:\r\n");
        // Mode 66 is EC, the Extended Control switch, and its RESET is the 2115 side - TDV 2215
        // section 3.1 and ND-1200 section 8.1. The label used to say "ESC [ ? 40 h", which is the
        // printer code format with a marker no TDV mode table has.
        await session.WriteAsync("Entering 2115 mode: ESC [ 66 l   (EC off)\r\n");
        var enable2115 = TDVSequenceBuilder.Build2115CompatibilityEnable();
        await session.WriteBytesAsync(enable2115);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(enable2115)}\r\n");
        await Task.Delay(500);
        await session.WriteAsync("In 2115 mode - testing function keys...\r\n");
        await Task.Delay(500);
        // ESC [ 66 h only works from OUTSIDE 2115 mode - in it the terminal throws away the ESC of
        // every control sequence, and the way out is ESC Q. The demo sends the CSI form because it
        // never really entered 2115 operation, only asked a live terminal to.
        await session.WriteAsync("Exiting 2115 mode: ESC [ 66 h   (EC on; from inside, ESC Q)\r\n");
        var disable2115 = TDVSequenceBuilder.Build2115CompatibilityDisable();
        await session.WriteBytesAsync(disable2115);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(disable2115)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV1200_NDGraphicsAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV1200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV1200 ND Graphics test requires TDV1200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate ND Graphics capability
        if (!ValidateFeature("NDGraphics"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: ND Graphics test requires ND Graphics capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV1200: ND Graphics Sequences ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing ND Graphics Sequences:\r\n");

        await session.WriteAsync("NDDWA (Define Work Area): ");
        var nddwa = TDVSequenceBuilder.BuildNDDWA(10, 10, 100, 50);
        await session.WriteBytesAsync(nddwa);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(nddwa)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("NDSAR (Set Attribute Rectangle): ");
        var ndsar = TDVSequenceBuilder.BuildNDSAR(10, 10, 100, 50, 7);
        await session.WriteBytesAsync(ndsar);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(ndsar)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("NDAAR (Add Attribute Rectangle): ");
        var ndaar = TDVSequenceBuilder.BuildNDAAR(10, 10, 100, 50, 2);
        await session.WriteBytesAsync(ndaar);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(ndaar)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("NDRAR (Remove Attribute Rectangle): ");
        var ndrar = TDVSequenceBuilder.BuildNDRAR(10, 10, 100, 50, 7);
        await session.WriteBytesAsync(ndrar);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(ndrar)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("NDFC (Fill Character Rectangle): ");
        var ndfc = TDVSequenceBuilder.BuildNDFC(10, 10, 100, 50, 65); // 'A' = 65
        await session.WriteBytesAsync(ndfc);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(ndfc)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("NDSREC (Save Rectangle): ");
        var ndsrec = TDVSequenceBuilder.BuildNDSREC(10, 10, 100, 50);
        await session.WriteBytesAsync(ndsrec);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(ndsrec)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("NDRREC (Restore Rectangle): ");
        var ndrrec = TDVSequenceBuilder.BuildNDRREC(200, 200);
        await session.WriteBytesAsync(ndrrec);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(ndrrec)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV1200_ProtectedAreasAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV1200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV1200 protected areas test requires TDV1200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate Protected Areas capability
        if (!ValidateFeature("ProtectedAreas"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: Protected areas test requires Protected Areas capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV1200: Protected Areas & Work Areas ===\x1b[0m\r\n\r\n");

        // WHAT THIS CASE USED TO DO, and why it is different now.
        //
        // It wrote the literal characters "SPA" and "EPA" onto the screen, after a CSI that ended
        // at the 'S' - so a terminal saw ESC [ 1;1;10;10 S, which is SU, scroll up - and then
        // printed "PA". Nothing about protected areas happened at all.
        //
        // And there is no such sequence to send. The ND Display Terminal 1200 control list, chapter
        // 5, names 69 functions and neither SPA nor EPA is among them; nor is DECSCA. What this
        // PROGRAM implements is DECSCA, CSI Ps " q, which marks what is typed next as protected.
        // So that is what gets sent, and the text says plainly that a real TDV1200's answer to it
        // is unknown.
        await session.WriteAsync("Protected areas: this program implements DECSCA, CSI Ps \" q.\r\n");
        await session.WriteAsync("The ND-1200 control list has no SPA, no EPA and no DECSCA, so\r\n");
        await session.WriteAsync("what a real TDV1200 does with this is a probe, not a demo.\r\n\r\n");

        await session.WriteAsync("Protected on  (ESC [ 1 \" q): ");
        await session.WriteBytesAsync(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'"', (byte)'q' });
        await session.WriteAsync("THIS TEXT IS MARKED PROTECTED");
        await session.WriteBytesAsync(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'"', (byte)'q' });
        await session.WriteAsync("\r\nProtected off (ESC [ 0 \" q): this text is not.\r\n");
        await Task.Delay(300);

        await session.WriteAsync("\r\nNDDWA (Define Work Area): ");
        var nddwa = TDVSequenceBuilder.BuildNDDWA(1, 1, 20, 20);
        await session.WriteBytesAsync(nddwa);
        await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(nddwa)}\r\n");
        await Task.Delay(300);

        // Real sequences - ND-1200 sections 5.37, 5.38 and 5.52. Three operations on four lamps:
        // 0 all, 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE. This block used to print the letters
        // "NDCLED" and "NDSLED" on the screen and send nothing at all.
        await session.WriteAsync("\r\nTesting Message LEDs (4 lamps: EXPAND APPEND BUSY MESSAGE):\r\n");

        var ndsled1200 = TDVSequenceBuilder.BuildNDSLED(4);
        await session.WriteBytesAsync(ndsled1200);
        await session.WriteAsync($"NDSLED lamp 4, MESSAGE: {TDVSequenceBuilder.ToVisibleString(ndsled1200)}\r\n");
        await Task.Delay(300);

        var ndbled1200 = TDVSequenceBuilder.BuildNDBLED(3);
        await session.WriteBytesAsync(ndbled1200);
        await session.WriteAsync($"NDBLED lamp 3, BUSY:    {TDVSequenceBuilder.ToVisibleString(ndbled1200)}\r\n");
        await Task.Delay(300);

        var ndcled1200 = TDVSequenceBuilder.BuildNDCLED(0);
        await session.WriteBytesAsync(ndcled1200);
        await session.WriteAsync($"NDCLED all lamps:       {TDVSequenceBuilder.ToVisibleString(ndcled1200)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("\r\nRetroTerm acts on these as of 11 September 2026 - watch the\r\n");
        await session.WriteAsync("virtual keyboard's four lamps. MESSAGE lights, BUSY blinks,\r\n");
        await session.WriteAsync("then all four clear.\r\n");

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV1200_CharacterSetsAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV1200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV1200 character sets test requires TDV1200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV1200: Character Sets ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing TDV Character Sets:\r\n");

        var characterSets = new[] { ("Graphics I", 1), ("Graphics II", 2), ("Math", 3), ("Greek", 4),
            ("Norwegian", 5), ("Swedish", 6), ("Danish", 7), ("Finnish", 8), ("German", 9), ("US ASCII", 0) };

        for (int i = 0; i < characterSets.Length; i++)
        {
            var (name, setNumber) = characterSets[i];
            await session.WriteAsync($"{name}: ");
            var sequence = TDVSequenceBuilder.BuildCharacterSet(setNumber);
            await session.WriteBytesAsync(sequence);
            await session.WriteAsync($"{name} characters\r\n");
        }

        // Reset to US ASCII
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCharacterSet(0));

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    // TDV2215 Test Methods
    private async Task RunTDV2215_ExtendedModeAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2215))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2215 extended mode test requires TDV2215 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate Extended Mode capability
        if (!ValidateFeature("ExtendedMode"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: Extended mode test requires Extended Mode capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV2215: Extended Mode ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing extended mode:\r\n");
        // "ESC ? 1 h" was never a sequence. Extended operation is the EC switch, mode 66, and the
        // ND private 1 it borrowed is Beginning of Line Wrap.
        await session.WriteAsync("Entering extended mode: ESC [ 66 h   (EC, section 8.7.1)\r\n");
        var enableExtended = TDVSequenceBuilder.BuildExtendedModeEnable();
        await session.WriteBytesAsync(enableExtended);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(enableExtended)}\r\n");
        await Task.Delay(500);
        await session.WriteAsync("In extended mode - the full 2215 control set is accepted...\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Exiting extended mode: ESC [ 66 l   (EC off = 2115 compatible)\r\n");
        var disableExtended = TDVSequenceBuilder.BuildExtendedModeDisable();
        await session.WriteBytesAsync(disableExtended);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(disableExtended)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV2215_TransparentModeAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2215))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2215 transparent mode test requires TDV2215 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate Transparent Mode capability
        if (!ValidateFeature("TransparentMode"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: Transparent mode test requires Transparent Mode capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV2215: Transparent Mode ===\x1b[0m\r\n\r\n");

        // NOTHING IS SENT HERE, and that is the whole finding.
        //
        // This case used to send ESC ? 2 h and ESC ? 2 l and call them transparent mode. Neither is
        // a sequence any TDV parses, and transparent operation is not host-settable at all:
        //
        //   TDV 2215 section 4.3.1 - the Send-Receive Mode soft-switch, SIMULTANEOUS or TRANSPARENT,
        //   set from the keyboard. "The only exit possible from this mode is obtained by depressing
        //   the MODE key twice, which gives access to the Soft-switch menu." Section 8.7.1 lists
        //   every mode the host may set and SRM is not among them.
        //
        //   TDV 2200/9 S User's Guide, the switch table - "Send Receive Mode: Simultaneous /
        //   Transparent". Same switch, same place.
        //
        //   ND Display Terminal 1200, chapter 3 - "Transparent mode: Disabled / Enabled", a set-up
        //   menu option.
        //
        // So the honest demo is to say so and to say how to drive it by hand.
        await session.WriteAsync("Transparent mode CANNOT be entered by the host.\r\n\r\n");
        await session.WriteAsync("  2215: Send-Receive Mode soft-switch (4.3.1), MODE key twice.\r\n");
        await session.WriteAsync("  2200: Send Receive Mode switch, Simultaneous / Transparent.\r\n");
        await session.WriteAsync("  1200: set-up menu, Transparent mode Disabled / Enabled.\r\n\r\n");
        await session.WriteAsync("Section 8.7.1 lists every host-settable mode and SRM is not one.\r\n");
        await session.WriteAsync("This case sends no sequence - the two it used to send were invented.\r\n");

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV2215_DCSSequencesAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2215))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2215 DCS sequences test requires TDV2215 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate DCS Sequences capability
        if (!ValidateFeature("DCSSequences"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: DCS sequences test requires DCS Sequences capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV2215: DCS Sequences ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing DCS sequences:\r\n");
        await session.WriteAsync("PUSH-key programming (PUSH key 1, hex data '44617461' = 'Data'): ");
        var pushKeySequence = TDVSequenceBuilder.BuildPUSHKeyProgram(1, "44617461");
        await session.WriteBytesAsync(pushKeySequence);
        await session.WriteAsync($"\r\n  Sequence: {TDVSequenceBuilder.ToVisibleString(pushKeySequence)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("PROGRAM-key loading (PROGRAM key 1): ");
        var programKeySequence = TDVSequenceBuilder.BuildPROGRAMKeyLoad(1);
        await session.WriteBytesAsync(programKeySequence);
        await session.WriteAsync($"\r\n  Sequence: {TDVSequenceBuilder.ToVisibleString(programKeySequence)}\r\n");
        await Task.Delay(300);

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    // TDV2200 Test Methods
    private async Task RunTDV2200_GraphicsExtensionAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2200 graphics extension test requires TDV2200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate Graphics Extension capability
        if (!ValidateFeature("GraphicsExtension"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: Graphics extension test requires Graphics Extension capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV2200: Graphics Extension ===\x1b[0m\r\n\r\n");

        // ESC [ 1 > HAS NO SOURCE, and the emulator says so itself.
        //
        // TDV2200Emulator.HandleModeControl counts this as an unrecognised sequence rather than
        // acting on it, with a comment written on 25 August 2026 recording the search: the ND
        // graphic terminal analysis, the TDV1200 graphics library reference and the comprehensive
        // TDV reference name no such sequence. It is this program's own invention. The case is kept
        // because sending it at a real TDV2200 and watching what happens is how the invention gets
        // settled - but the screen must not call it a feature.
        await session.WriteAsync("Testing graphics extension board:\r\n");
        await session.WriteAsync("WARNING: ESC [ 1 > has NO source in any manual held here. It is\r\n");
        await session.WriteAsync("this program's own invention, counted as unrecognised rather than\r\n");
        await session.WriteAsync("obeyed. Sending it is a probe. The ND graphics sequences that ARE\r\n");
        await session.WriteAsync("documented are the ESC-quote family - see the graphics tests.\r\n\r\n");
        await session.WriteAsync("Graphics mode: ESC [ 1 >\r\n");
        var enableGraphics = TDVSequenceBuilder.BuildGraphicsExtensionEnable();
        await session.WriteBytesAsync(enableGraphics);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(enableGraphics)}\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Graphics operations...\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Disable graphics: ESC [ 0 >\r\n");
        var disableGraphics = TDVSequenceBuilder.BuildGraphicsExtensionDisable();
        await session.WriteBytesAsync(disableGraphics);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(disableGraphics)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV2200_TektronixModeAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2200 Tektronix mode test requires TDV2200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate Tektronix Mode capability
        if (!ValidateFeature("TektronixMode"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: Tektronix mode test requires Tektronix Mode capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV2200: Tektronix Mode ===\x1b[0m\r\n\r\n");

        // DECTEK, CSI ? 38 h, is DEC's own way in and is what this program implements. On a real
        // ND graphics terminal the documented entry is GS, hex 1D - "GS (0x1D): Enter Graph Mode,
        // standard Tek 4014" in the ND graphic terminal analysis under spec\Tektronix - and the ND
        // private drawing controls are the ESC-quote family. Both facts belong on the screen so a
        // person reading the result knows which terminal answered what.
        await session.WriteAsync("Testing Tektronix 4010 compatibility:\r\n");
        await session.WriteAsync("A real ND graphics board enters graph mode with GS (hex 1D).\r\n");
        await session.WriteAsync("This sends DECTEK, which is what RetroTerm implements.\r\n\r\n");
        await session.WriteAsync("Entering Tektronix mode: ESC [ ? 38 h\r\n");
        var enableTektronix = TDVSequenceBuilder.BuildTektronixModeEnable();
        await session.WriteBytesAsync(enableTektronix);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(enableTektronix)}\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Tektronix graphics commands...\r\n");
        await Task.Delay(500);
        await session.WriteAsync("Exiting Tektronix mode: ESC [ ? 38 l\r\n");
        var disableTektronix = TDVSequenceBuilder.BuildTektronixModeDisable();
        await session.WriteBytesAsync(disableTektronix);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(disableTektronix)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    private async Task RunTDV2200_ISO646VariantsAsync(TelnetSession session)
    {
        // Validate terminal type
        if (!ValidateTerminalType(TerminalType.TDV2200))
        {
            await session.WriteAsync("\r\n\x1b[31mError: TDV2200 ISO646 variants test requires TDV2200 terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate ISO646 Variants capability
        if (!ValidateFeature("ISO646Variants"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: ISO646 variants test requires ISO646 Variants capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV2200: ISO 646 Variants ===\x1b[0m\r\n\r\n");

        // FOUR national versions exist, not six.
        //
        // TDV 2215 sections 9.1.1 to 9.1.4 print the character tables for International,
        // Norwegian, Swedish and German, and appendix A's ordering list has the same four. There is
        // no Danish and no Finnish TDV, and "US" is the International version. This case used to
        // offer six, so two of its six sequences named versions that do not exist and a third used
        // the wrong letter for one that does.
        //
        // AND THE SEQUENCE ITSELF IS UNSOURCED. No manual held here gives a host sequence for
        // selecting the national version - it is a hardware version, ordered as such. ESC % X is
        // what this program implements, and worse, ISO 2022 uses ESC % for designating another
        // coding system, where ESC % G means "switch to UTF-8". Kept and sent because a real
        // terminal's answer is the only thing that can settle it, with the doubt on the screen.
        await session.WriteAsync("Testing ISO 646 variants:\r\n");
        await session.WriteAsync("NOTE: no manual here gives a host sequence for the national\r\n");
        await session.WriteAsync("version - it is a factory version (2215 appendix A). ESC % X is\r\n");
        await session.WriteAsync("this program's own, and ISO 2022 reads ESC % G as 'use UTF-8'.\r\n\r\n");

        var variants = new[] { ('I', "International"), ('N', "Norwegian"), ('S', "Swedish"),
            ('G', "German") };

        for (int i = 0; i < variants.Length; i++)
        {
            var (variant, name) = variants[i];
            await session.WriteAsync($"{name}: ");
            var sequence = TDVSequenceBuilder.BuildISO646Variant(variant);
            await session.WriteBytesAsync(sequence);
            await session.WriteAsync($" {TDVSequenceBuilder.ToVisibleString(sequence)}\r\n");
            await Task.Delay(300);
        }

        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    #region Comprehensive TDV Tests

    /// <summary>
    /// Runs comprehensive TDV query/response tests
    /// </summary>
    private async Task RunTDV_QueryResponseTestsAsync(TelnetSession session)
    {
        // Validate TDV terminal
        if (!ValidateTDVTerminal())
        {
            await session.WriteAsync("\r\n\x1b[31mError: Query/Response tests require a TDV terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Reset terminal state completely
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\x1b[0m"); // Reset attributes
        await Task.Delay(100);

        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Query/Response Tests ===\x1b[0m\r\n\r\n");

        // Test Primary DA
        await session.WriteAsync("Testing Device Attributes (DA) queries...\r\n");
        await session.WriteAsync("Sending Primary DA: ESC [ c\r\n");
        System.Console.WriteLine("[TestServer] Sending Primary DA query using TDVSequenceBuilder");
        var daQuery = TDVSequenceBuilder.BuildDAQuery();
        System.Console.WriteLine($"[TestServer] DA query bytes: {BitConverter.ToString(daQuery)}");
        await session.WriteBytesAsync(daQuery);
        await session.FlushAsync(); // Ensure query is sent immediately
        System.Console.WriteLine($"[TestServer] Query sent ({daQuery.Length} bytes), waiting for response...");
        System.Console.WriteLine($"[TestServer] Query hex: {BitConverter.ToString(daQuery)}");
        await Task.Delay(100); // Delay to allow TCP to deliver query and give emulator time to generate response
        var response1 = await CaptureResponseWithPollingAsync(session, maxWaitMs: 1000);
        var daResponse = TDVResponseValidator.ParseDAResponse(response1);
        System.Console.WriteLine($"[TestServer] Primary DA response: {(response1 != null ? TDVResponseValidator.ToVisibleString(response1) : "null")}");
        await session.WriteAsync($"Response: {TDVResponseValidator.ToVisibleString(response1 ?? "No response received")}\r\n");
        if (daResponse.IsValid)
        {
            await session.WriteAsync($"\x1b[32m[OK] Valid DA response detected\x1b[0m\r\n");
            if (daResponse.IsSecondary && daResponse.FirmwareId.HasValue)
            {
                await session.WriteAsync($"  Firmware ID: {daResponse.FirmwareId}\r\n");
            }
        }
        else
        {
            await session.WriteAsync($"\x1b[33m[WARN] Invalid or missing DA response\x1b[0m\r\n");
        }

        // Test Secondary DA
        await session.WriteAsync("\r\nSending Secondary DA: ESC [ > c\r\n");
        System.Console.WriteLine("[TestServer] Sending Secondary DA query using TDVSequenceBuilder");
        var secondaryDAQuery = TDVSequenceBuilder.BuildSecondaryDAQuery();
        System.Console.WriteLine($"[TestServer] Secondary DA query bytes: {BitConverter.ToString(secondaryDAQuery)}");
        await session.WriteBytesAsync(secondaryDAQuery);
        await session.FlushAsync(); // Ensure query is sent immediately
        System.Console.WriteLine("[TestServer] Secondary DA query sent, waiting for response...");
        await Task.Delay(50); // Small delay to allow TCP to deliver query and response to be generated
        var response2 = await CaptureResponseWithPollingAsync(session, maxWaitMs: 1000);
        var secondaryDAResponse = TDVResponseValidator.ParseDAResponse(response2);
        System.Console.WriteLine($"[TestServer] Secondary DA response: {(response2 != null ? TDVResponseValidator.ToVisibleString(response2) : "null")}");
        await session.WriteAsync($"Response: {TDVResponseValidator.ToVisibleString(response2 ?? "No response received")}\r\n");
        if (secondaryDAResponse.IsValid && secondaryDAResponse.IsSecondary)
        {
            await session.WriteAsync($"\x1b[32m[OK] Valid Secondary DA response detected\x1b[0m\r\n");
            if (secondaryDAResponse.FirmwareId.HasValue)
            {
                await session.WriteAsync($"  Firmware ID: {secondaryDAResponse.FirmwareId}\r\n");
                var expectedType = _detectedTerminalType;
                if (TDVResponseValidator.ValidateDATerminalType(secondaryDAResponse, expectedType))
                {
                    await session.WriteAsync($"  \x1b[32m[OK] Terminal type matches detected type ({expectedType})\x1b[0m\r\n");
                }
                else
                {
                    await session.WriteAsync($"  \x1b[33m[WARN] Terminal type mismatch (expected {expectedType})\x1b[0m\r\n");
                }
            }
        }
        else
        {
            await session.WriteAsync($"\x1b[33m[WARN] Invalid or missing Secondary DA response\x1b[0m\r\n");
        }

        // Test CPR
        await session.WriteAsync("\r\nTesting Cursor Position Report (CPR)...\r\n");
        await session.WriteAsync("Sending CPR: ESC [ 6 n\r\n");
        System.Console.WriteLine("[TestServer] Sending CPR query using TDVSequenceBuilder");
        var cprQuery = TDVSequenceBuilder.BuildCPRQuery();
        System.Console.WriteLine($"[TestServer] CPR query bytes: {BitConverter.ToString(cprQuery)}");
        await session.WriteBytesAsync(cprQuery);
        await session.FlushAsync(); // Ensure query is sent immediately
        System.Console.WriteLine("[TestServer] CPR query sent, waiting for response...");
        await Task.Delay(50); // Small delay to allow TCP to deliver query and response to be generated
        var response3 = await CaptureResponseWithPollingAsync(session, maxWaitMs: 1000);
        var cprResponse = TDVResponseValidator.ParseCPRResponse(response3);
        System.Console.WriteLine($"[TestServer] CPR response: {(response3 != null ? TDVResponseValidator.ToVisibleString(response3) : "null")}");
        await session.WriteAsync($"Response: {TDVResponseValidator.ToVisibleString(response3 ?? "No response received")}\r\n");
        if (cprResponse.IsValid)
        {
            await session.WriteAsync($"\x1b[32m[OK] Valid CPR response detected\x1b[0m\r\n");
            if (cprResponse.Row.HasValue && cprResponse.Column.HasValue)
            {
                await session.WriteAsync($"  Cursor Position: Row {cprResponse.Row}, Column {cprResponse.Column}\r\n");
            }
        }
        else
        {
            await session.WriteAsync($"\x1b[33m[WARN] Invalid or missing CPR response\x1b[0m\r\n");
        }

        // Test DSR
        await session.WriteAsync("\r\nTesting Device Status Report (DSR)...\r\n");
        await session.WriteAsync("Sending DSR: ESC [ 5 n\r\n");
        System.Console.WriteLine("[TestServer] Sending DSR query using TDVSequenceBuilder");
        var dsrQuery = TDVSequenceBuilder.BuildDSRQuery();
        System.Console.WriteLine($"[TestServer] DSR query bytes: {BitConverter.ToString(dsrQuery)}");
        await session.WriteBytesAsync(dsrQuery);
        await session.FlushAsync(); // Ensure query is sent immediately
        System.Console.WriteLine("[TestServer] DSR query sent, waiting for response...");
        await Task.Delay(50); // Small delay to allow TCP to deliver query and response to be generated
        var response4 = await CaptureResponseWithPollingAsync(session, maxWaitMs: 1000);
        var dsrResponse = TDVResponseValidator.ParseDSRResponse(response4);
        System.Console.WriteLine($"[TestServer] DSR response: {(response4 != null ? TDVResponseValidator.ToVisibleString(response4) : "null")}");
        await session.WriteAsync($"Response: {TDVResponseValidator.ToVisibleString(response4 ?? "No response received")}\r\n");
        if (dsrResponse.IsValid)
        {
            await session.WriteAsync($"\x1b[32m[OK] Valid DSR response detected\x1b[0m\r\n");
            if (dsrResponse.IsOK)
            {
                await session.WriteAsync($"  Status: OK\r\n");
            }
            else if (dsrResponse.IsFailure)
            {
                await session.WriteAsync($"  Status: Failure\r\n");
            }
        }
        else
        {
            await session.WriteAsync($"\x1b[33m[WARN] Invalid or missing DSR response\x1b[0m\r\n");
        }

        // Test Terminal ID
        await session.WriteAsync("\r\nTesting Terminal Identification...\r\n");
        await session.WriteAsync("Sending Terminal ID: ESC Z\r\n");
        System.Console.WriteLine("[TestServer] Sending Terminal ID query using TDVSequenceBuilder");
        var terminalIDQuery = TDVSequenceBuilder.BuildTerminalIDQuery();
        System.Console.WriteLine($"[TestServer] Terminal ID query bytes: {BitConverter.ToString(terminalIDQuery)}");
        await session.WriteBytesAsync(terminalIDQuery);
        await session.FlushAsync(); // Ensure query is sent immediately
        System.Console.WriteLine("[TestServer] Terminal ID query sent, waiting for response...");
        await Task.Delay(50); // Small delay to allow TCP to deliver query and response to be generated
        var response5 = await CaptureResponseWithPollingAsync(session, maxWaitMs: 1000);
        var terminalIDResponse = TDVResponseValidator.ParseTerminalIDResponse(response5);
        System.Console.WriteLine($"[TestServer] Terminal ID response: {(response5 != null ? TDVResponseValidator.ToVisibleString(response5) : "null")}");
        await session.WriteAsync($"Response: {TDVResponseValidator.ToVisibleString(response5 ?? "No response received")}\r\n");
        if (terminalIDResponse.IsValid)
        {
            await session.WriteAsync($"\x1b[32m[OK] Valid Terminal ID response detected\x1b[0m\r\n");
            if (!string.IsNullOrEmpty(terminalIDResponse.TerminalType))
            {
                await session.WriteAsync($"  Terminal Type: {terminalIDResponse.TerminalType}\r\n");
            }
            if (TDVResponseValidator.ValidateTerminalIDType(terminalIDResponse, _detectedTerminalType))
            {
                await session.WriteAsync($"  \x1b[32m[OK] Terminal type matches detected type ({_detectedTerminalType})\x1b[0m\r\n");
            }
        }
        else
        {
            await session.WriteAsync($"\x1b[33m[WARN] Invalid or missing Terminal ID response\x1b[0m\r\n");
        }

        // THE MODE QUERY IS NDRQ, and it is sent for real now.
        //
        // Two DECRQM queries used to live here, one for "2115 mode 66" and one for "smooth scroll
        // 67". No TDV manual has DECRQM at all: TDV 2215 Functional Specifications section 8.7
        // lists every CSI sequence the terminal accepts and not one carries a dollar intermediate,
        // and section 8.3.2 lists everything it ever sends, which is CPR alone. Mode 67 is not
        // smooth scroll either - it is HAN, the XON/XOFF handshake (8.7.1).
        //
        // Sending them at a REAL Tandberg would have got silence at best. In 2115 operation it is
        // worse: section 3.1 says the ESC is discarded and the rest of the sequence is DISPLAYED,
        // so "[?66$p" would have been typed onto the operator screen.
        //
        // What a TDV really answers is NDRQ - ND Display Terminal 1200 section 5.48. CSI Ps x asks,
        // CSI Ps ; n1 ; ... x replies. This case sent nothing between 11 September 2026 and the
        // afternoon of the same day, when NDRQ was implemented; it asks for all four report types
        // the terminal can fill.
        //
        // See docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md.
        await session.WriteAsync("\r\nTesting NDRQ, Request and Report Terminal Parameters...\r\n");
        await session.WriteAsync("ND-1200 section 5.48. This is the report a TDV really has -\r\n");
        await session.WriteAsync("DECRQM appears in no TDV manual and is never sent here.\r\n");

        var ndrqTypes = new[]
        {
            (1, "emulator level"),
            (2, "the three mode bitmasks"),
            (5, "error conditions"),
            (6, "auxiliary devices fitted")
        };

        for (int i = 0; i < ndrqTypes.Length; i++)
        {
            var (reportType, what) = ndrqTypes[i];

            await session.WriteAsync($"\r\nNDRQ type {reportType} ({what}): ESC [ {reportType} x\r\n");
            await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { reportType }, 'x'));
            await session.FlushAsync();
            await Task.Delay(50);

            var ndrqReply = await CaptureResponseWithPollingAsync(session, maxWaitMs: 1000);
            await session.WriteAsync(
                $"Response: {TDVResponseValidator.ToVisibleString(ndrqReply ?? "No response received")}\r\n");

            // A terminal that cannot fill a report answers CSI 0 x, which is the manual's own
            // "requested report type not available" and is a valid answer, not a failure.
            if (ndrqReply != null && ndrqReply.EndsWith("x"))
            {
                await session.WriteAsync("\x1b[32m[OK] NDRQ reply received\x1b[0m\r\n");
            }
            else
            {
                await session.WriteAsync("\x1b[33m[WARN] No NDRQ reply - a real 2115-mode terminal\x1b[0m\r\n");
                await session.WriteAsync("\x1b[33m       would have DISPLAYED the query instead\x1b[0m\r\n");
            }
        }

        await session.WriteAsync("\r\n\x1b[32mQuery/Response tests completed!\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    /// <summary>
    /// Runs comprehensive TDV character sets tests
    /// </summary>
    private async Task RunTDV_CharacterSetsTestsAsync(TelnetSession session)
    {
        // Validate TDV terminal
        if (!ValidateTDVTerminal())
        {
            await session.WriteAsync("\r\n\x1b[31mError: Character sets tests require a TDV terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Reset terminal state completely
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\x1b[0m"); // Reset attributes
        await Task.Delay(100);

        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Character Sets Tests ===\x1b[0m\r\n\r\n");

        var characterSets = new[]
        {
            ("Graphics I", 1),
            ("Graphics II", 2),
            ("Math", 3),
            ("Greek", 4),
            ("Norwegian", 5),
            ("Swedish", 6),
            ("Danish", 7),
            ("Finnish", 8),
            ("German", 9),
            ("US ASCII", 0)
        };

        for (int i = 0; i < characterSets.Length; i++)
        {
            var (name, setNumber) = characterSets[i];
            await session.WriteAsync($"\r\nTesting {name} character set:\r\n");
            var sequence = TDVSequenceBuilder.BuildCharacterSet(setNumber);
            await session.WriteAsync($"Sequence: {TDVSequenceBuilder.ToVisibleString(sequence)}\r\n");

            // Switch to the character set using raw bytes
            await session.WriteBytesAsync(sequence);
            await Task.Delay(100);

            // Test by sending ASCII bytes 0x60-0x7F that should be mapped by each character set
            // TDV character sets only map characters in the range 0x60-0x7F (`, a-z, {|}~, DEL)
            await session.WriteAsync($"Mapped chars: ");

            // Send ASCII bytes 0x60-0x6F (16 characters) that get mapped by the character set
            // These are: ` a b c d e f g h i j k l m n o
            var testBytes = new byte[16];
            for (int j = 0; j < 16; j++)
            {
                testBytes[j] = (byte)(0x60 + j);
            }
            await session.WriteBytesAsync(testBytes);
            await session.WriteAsync("\r\n");

            // Reset to US ASCII to prevent corruption
            await session.WriteBytesAsync(TDVSequenceBuilder.BuildCharacterSet(0));
            await Task.Delay(200);
        }

        await session.WriteAsync("\r\n\x1b[32mCharacter sets tests completed!\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    /// <summary>
    /// Runs comprehensive TDV drawing operations tests
    /// </summary>
    private async Task RunTDV_DrawingOperationsTestsAsync(TelnetSession session)
    {
        // Validate TDV terminal
        if (!ValidateTDVTerminal())
        {
            await session.WriteAsync("\r\n\x1b[31mError: Drawing operations tests require a TDV terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        // Validate ND Graphics capability
        if (!ValidateFeature("NDGraphics"))
        {
            await session.WriteAsync("\r\n\x1b[31mError: Drawing operations require ND Graphics capability.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Drawing Operations Tests ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing Rectangle Operations...\r\n\r\n");

        // Draw a test rectangle with NDSAR
        await session.WriteAsync("Drawing test rectangle with NDSAR...\r\n");
        var ndsarSequence = TDVSequenceBuilder.BuildNDSAR(10, 5, 20, 10, 7); // Set inverse in rectangle
        await session.WriteBytesAsync(ndsarSequence);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(ndsarSequence)}\r\n");
        await Task.Delay(500);

        // Fill rectangle with character
        await session.WriteAsync("\r\nFilling rectangle with character...\r\n");
        var ndfcSequence = TDVSequenceBuilder.BuildNDFC(10, 5, 20, 10, 65); // Fill with 'A' (ASCII 65)
        await session.WriteBytesAsync(ndfcSequence);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(ndfcSequence)}\r\n");
        await Task.Delay(500);

        // Define work area
        await session.WriteAsync("\r\nTesting work area definition...\r\n");
        var nddwaSequence = TDVSequenceBuilder.BuildNDDWA(5, 10, 25, 15); // Define work area
        await session.WriteBytesAsync(nddwaSequence);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(nddwaSequence)}\r\n");
        await Task.Delay(500);

        // Save rectangle
        await session.WriteAsync("\r\nTesting rectangle save...\r\n");
        var ndsrecSequence = TDVSequenceBuilder.BuildNDSREC(10, 5, 20, 10); // Save rectangle
        await session.WriteBytesAsync(ndsrecSequence);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(ndsrecSequence)}\r\n");
        await Task.Delay(500);

        // Restore rectangle
        await session.WriteAsync("\r\nTesting rectangle restore...\r\n");
        var ndrrecSequence = TDVSequenceBuilder.BuildNDRREC(30, 15); // Restore rectangle
        await session.WriteBytesAsync(ndrrecSequence);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(ndrrecSequence)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\n\x1b[32mDrawing operations tests completed!\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    /// <summary>
    /// Runs comprehensive TDV function keys tests
    /// </summary>
    private async Task RunTDV_FunctionKeysTestsAsync(TelnetSession session)
    {
        // Validate TDV terminal
        if (!ValidateTDVTerminal())
        {
            await session.WriteAsync("\r\n\x1b[31mError: Function keys tests require a TDV terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Function Keys Tests ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press function keys, arrow keys, and special keys.\r\n");
        await session.WriteAsync("I will detect and display the escape sequences received.\r\n");
        await session.WriteAsync("\x1b[1;32mPress Enter to exit this test.\x1b[0m\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Enter) return;

            var visible = ToVisible(input.Value);
            if (input.Name != null)
                await session.WriteAsync($"\x1b[1;36mDetected: {input.Name}\x1b[0m - Sequence: \x1b[33m{visible}\x1b[0m\r\n");
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown sequence: {visible}\x1b[0m\r\n");
            else if (input.Type == InputType.Character)
                await session.WriteAsync($"Char: '{input.Value}'\r\n");
        }
    }

    /// <summary>
    /// Runs comprehensive TDV modes and features tests
    /// </summary>
    private async Task RunTDV_ModesFeaturesTestsAsync(TelnetSession session)
    {
        // Validate TDV terminal
        if (!ValidateTDVTerminal())
        {
            await session.WriteAsync("\r\n\x1b[31mError: Modes & features tests require a TDV terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Modes & Features Tests ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing smooth scroll mode...\r\n");
        await session.WriteAsync("Enabling smooth scroll: ESC [ 60 h, the RT Roll Type switch\r\n");
        var enableSmoothScroll = TDVSequenceBuilder.BuildSmoothScrollEnable();
        await session.WriteBytesAsync(enableSmoothScroll);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(enableSmoothScroll)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\nDisabling smooth scroll: ESC [ 60 l\r\n");
        var disableSmoothScroll = TDVSequenceBuilder.BuildSmoothScrollDisable();
        await session.WriteBytesAsync(disableSmoothScroll);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(disableSmoothScroll)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\nTesting the line wrap modes...\r\n");
        // These two replace what this section used to call "blink" and "enhanced blink".
        // NDBLWM and NDELWM are the Beginning and End of Line WRAP modes - ND Display Terminal
        // 1200 sections 4.10 and 4.11 - and the 2215 numbers them 31 and 36 (section 8.7.1).
        // The numbers this used to send, 68 and 69, are the cursor type and the printer mode.
        await session.WriteAsync("Beginning of line wrap on: ESC [ 31 h\r\n");
        var enableBolWrap = TDVSequenceBuilder.BuildBeginningOfLineWrapEnable();
        await session.WriteBytesAsync(enableBolWrap);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(enableBolWrap)}\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\nEnd of line wrap on: ESC [ 36 h\r\n");
        var enableEolWrap = TDVSequenceBuilder.BuildEndOfLineWrapEnable();
        await session.WriteBytesAsync(enableEolWrap);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(enableEolWrap)}\r\n");
        await Task.Delay(500);

        // PUT THEM BACK. A demonstration that leaves a mode switched on hands the next case, and
        // the operator, a terminal that is no longer in the state they think it is. Beginning of
        // line wrap especially: with it on, a backspace at column 1 climbs to the line above.
        //
        // End of line wrap goes back ON, not off - it is the power-up state here and a terminal
        // left with it off stops dead at the right margin.
        await session.WriteAsync("\r\nPutting them back: ESC [ 31 l, then ESC [ 36 h\r\n");
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildBeginningOfLineWrapDisable());
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildEndOfLineWrapEnable());
        await Task.Delay(500);

        await session.WriteAsync("\r\nTesting double-width/height lines...\r\n");
        await session.WriteAsync("Double-height top: ESC # 3\r\n");
        var doubleHeightTop = TDVSequenceBuilder.BuildDoubleWidthHeight(3);
        await session.WriteBytesAsync(doubleHeightTop);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(doubleHeightTop)}\r\n");
        await session.WriteAsync("Double-height top line\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\nDouble-height bottom: ESC # 4\r\n");
        var doubleHeightBottom = TDVSequenceBuilder.BuildDoubleWidthHeight(4);
        await session.WriteBytesAsync(doubleHeightBottom);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(doubleHeightBottom)}\r\n");
        await session.WriteAsync("Double-height bottom line\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\nDouble-width: ESC # 6\r\n");
        var doubleWidth = TDVSequenceBuilder.BuildDoubleWidthHeight(6);
        await session.WriteBytesAsync(doubleWidth);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(doubleWidth)}\r\n");
        await session.WriteAsync("Double-width line\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\nSingle-width: ESC # 5\r\n");
        var singleWidth = TDVSequenceBuilder.BuildDoubleWidthHeight(5);
        await session.WriteBytesAsync(singleWidth);
        await session.WriteAsync($"  Sequence: {TDVSequenceBuilder.ToVisibleString(singleWidth)}\r\n");
        await session.WriteAsync("Single-width line\r\n");
        await Task.Delay(500);

        await session.WriteAsync("\r\n\x1b[32mModes & features tests completed!\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    /// <summary>
    /// Runs comprehensive TDV demo
    /// </summary>
    private async Task RunTDV_ComprehensiveDemoAsync(TelnetSession session)
    {
        // Validate TDV terminal
        if (!ValidateTDVTerminal())
        {
            await session.WriteAsync("\r\n\x1b[31mError: Comprehensive demo requires a TDV terminal.\x1b[0m\r\n");
            await WriteMenuAsync(session);
            return;
        }

        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new[] { 2 }, 'J')); // Clear screen
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCSI(null, new int[0], 'H')); // Cursor home
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Comprehensive Demo ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("This demo showcases all TDV features...\r\n\r\n");

        // Character sets demo
        await session.WriteAsync("\x1b[1;36mCharacter Sets Demo:\x1b[0m\r\n");
        var graphicsI = TDVSequenceBuilder.BuildCharacterSet(1);
        await session.WriteBytesAsync(graphicsI);
        await session.WriteAsync("Graphics I: ████████████\x1b[0m\r\n");
        var greek = TDVSequenceBuilder.BuildCharacterSet(4);
        await session.WriteBytesAsync(greek);
        await session.WriteAsync("Greek: ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠ\x1b[0m\r\n");
        await session.WriteBytesAsync(TDVSequenceBuilder.BuildCharacterSet(0)); // Reset to US ASCII
        await Task.Delay(1000);

        // Drawing demo
        if (ValidateFeature("NDGraphics"))
        {
            await session.WriteAsync("\r\n\x1b[1;36mDrawing Operations Demo:\x1b[0m\r\n");
            var ndsarDemo = TDVSequenceBuilder.BuildNDSAR(10, 10, 30, 15, 7); // Set inverse in rectangle
            await session.WriteBytesAsync(ndsarDemo);
            var ndfcDemo = TDVSequenceBuilder.BuildNDFC(10, 10, 30, 15, 65); // Fill with 'A'
            await session.WriteBytesAsync(ndfcDemo);
            await session.WriteAsync("  Rectangle operations executed\r\n");
            await Task.Delay(1000);
        }

        // Function keys demo
        await session.WriteAsync("\r\n\x1b[1;36mFunction Keys Demo:\x1b[0m\r\n");
        await session.WriteAsync("F1-F12, Arrow keys, Special keys all supported\r\n");
        await Task.Delay(1000);

        // Modes demo
        await session.WriteAsync("\r\n\x1b[1;36mModes Demo:\x1b[0m\r\n");
        var enableSmoothScrollDemo = TDVSequenceBuilder.BuildSmoothScrollEnable();
        await session.WriteBytesAsync(enableSmoothScrollDemo);
        await session.WriteAsync("Smooth scroll enabled\r\n");
        // The old BuildBlinkEnable sent CSI ? 68 h and was named for NDBLWM. NDBLWM is the
        // Beginning of Line WRAP mode (ND-1200 section 4.10) and 68 is the cursor type switch
        // (2215 section 8.7.1) - neither is a blink mode. The blinking below is SGR 5, which is
        // what actually makes text blink and always was.
        await session.WriteAsync("\x1b[5mBlinking text\x1b[0m\r\n");
        await Task.Delay(1000);

        await session.WriteAsync("\r\n\x1b[32mComprehensive demo completed!\x1b[0m\r\n");
        await session.WriteAsync("\r\n\x1b[32mPress Enter to continue...\x1b[0m");
        await session.WaitForEnterAsync();
    }

    #endregion

    #region TDV Key Detection Tests

    /// <summary>
    /// Interactive test for TDV function keys (F1-F20)
    /// </summary>
    private async Task RunTDV_FunctionKeysInteractiveTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Function Keys Interactive Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press any function key (F1-F20), Ctrl+letter, or special key...\r\n");
        await session.WriteAsync("Detects VT220 (ESC[nn~), TDV (CSI nn _), and Ctrl+A..Z\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            // Show hex dump
            var sb = new StringBuilder();
            sb.Append("Hex: ");
            for (int i = 0; i < input.Value.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append($"{((int)input.Value[i]):X2}");
            }
            await session.WriteAsync($"{sb}\r\n");

            if (input.Name != null)
            {
                // Classify the format
                string format = "";
                if (input.Type == InputType.Control)
                    format = " (Control code)";
                else if (input.Value.Length >= 3 && input.Value[0] == 0x1B && input.Value[1] == '[' &&
                    input.Value[input.Value.Length - 1] == '_')
                    format = " (TDV CSI nn _)";
                else if (input.Value.Length >= 3 && input.Value[0] == 0x1B && input.Value[1] == 'O')
                    format = " (SS3)";
                else if (input.Value.Length >= 3 && input.Value[0] == 0x1B && input.Value[1] == '[')
                    format = " (CSI nn ~)";

                await session.WriteAsync($"\x1b[32m[OK] {input.Name}{format}\x1b[0m\r\n");
            }
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown sequence: {visible}\x1b[0m\r\n");
            else if (input.Type == InputType.Control)
                await session.WriteAsync($"\x1b[33m? Unknown control: {visible}\x1b[0m\r\n");

            await session.WriteAsync("Press another key or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Interactive test for TDV PUSH keys
    /// </summary>
    private async Task RunTDV_PushKeysInteractiveTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV PUSH Keys Interactive Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press any PUSH key (1-8) to see the sequence sent...\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            if (input.Name != null && input.Name.Contains("PUSH"))
                await session.WriteAsync($"\x1b[32m[OK] {input.Name} detected!\x1b[0m\r\n");
            else if (input.Name != null)
                await session.WriteAsync($"\x1b[32m[OK] {input.Name} detected!\x1b[0m\r\n");
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown sequence\x1b[0m\r\n");

            await session.WriteAsync("Press another PUSH key or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Interactive test for TDV soft keys
    /// </summary>
    private async Task RunTDV_SoftKeysInteractiveTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Soft Keys Interactive Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press any soft key (1-8) to see the sequence sent...\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            if (input.Name != null)
                await session.WriteAsync($"\x1b[32m[OK] {input.Name} detected!\x1b[0m\r\n");
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown sequence\x1b[0m\r\n");

            await session.WriteAsync("Press another soft key or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Interactive test for TDV control keys
    /// </summary>
    private async Task RunTDV_ControlKeysInteractiveTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Control Keys Interactive Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press any TDV control key to see the sequence sent...\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            if (input.Name != null)
                await session.WriteAsync($"\x1b[32m[OK] {input.Name} detected!\x1b[0m\r\n");
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown escape sequence\x1b[0m\r\n");
            else if (input.Type == InputType.Control)
                await session.WriteAsync($"\x1b[33m? Unknown control char: 0x{((int)input.Value[0]):X2}\x1b[0m\r\n");

            await session.WriteAsync("Press another control key or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Test for TDV 2115 C0 control codes
    /// </summary>
    private async Task RunTDV_2115ControlCodesTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV 2115 C0 Control Codes Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Testing TDV 2115 C0 control codes...\r\n\r\n");

        var controlCodes = new[]
        {
            ("VIDEO_OFF", "\x02", "STX"),
            ("VIDEO_ON", "\x03", "ETX"),
            ("ERASE_LINE", "\x04", "EOT"),
            ("LIGHT1_ON", "\x05", "ENQ"),
            ("LIGHT2_ON", "\x06", "ACK"),
            ("BELL", "\x07", "BEL"),
            ("CURSOR_LEFT", "\x08", "BS"),
            ("TAB", "\x09", "HT"),
            ("LINE_FEED", "\x0A", "LF"),
            ("CURSOR_DOWN", "\x0B", "VT"),
            ("ROLL_UP", "\x0C", "FF"),
            ("CURSOR_RETURN", "\x0D", "CR"),
            ("UNDERLINE", "\x0E", "SO"),
            ("NORMAL", "\x0F", "SI"),
            ("CURSOR_LOAD", "\x10", "DLE"),
            ("LIGHT3_ON", "\x15", "NAK"),
            ("LIGHTS_OFF", "\x16", "SYN"),
            ("ROLL_DOWN", "\x17", "ETB"),
            ("CURSOR_RIGHT", "\x18", "CAN"),
            ("ERASE_PAGE", "\x19", "EM"),
            ("CURSOR_UP", "\x1C", "FS"),
            ("CURSOR_HOME", "\x1D", "GS")
        };

        for (int i = 0; i < controlCodes.Length; i++)
        {
            var (name, code, description) = controlCodes[i];
            await session.WriteAsync($"Testing {name} ({description}): ");
            await session.WriteAsync(code);
            await session.WriteAsync(" [OK]\r\n");
            await Task.Delay(100);
        }

        await session.WriteAsync("\r\n\x1b[32mAll TDV 2115 C0 control codes tested!\x1b[0m\r\n");
        await session.WriteAsync("\x1b[15;1H\x1b[32mPress Enter to continue...\x1b[0m");
        await session.DrainInputAsync();
        await session.WaitForEnterAsync();
    }

    /// <summary>
    /// Interactive test for arrow keys and navigation
    /// </summary>
    private async Task RunTDV_ArrowKeysTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Arrow Keys Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press arrow keys and navigation keys to see sequences...\r\n");
        await session.WriteAsync("Expected keys: UP, DOWN, LEFT, RIGHT, HOME, END, PAGE_UP, PAGE_DOWN, INSERT, DELETE\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            if (input.Name != null)
                await session.WriteAsync($"\x1b[32m[OK] {input.Name} detected!\x1b[0m\r\n");
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown sequence\x1b[0m\r\n");

            await session.WriteAsync("Press another arrow/navigation key or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Test for modifier key combinations
    /// </summary>
    private async Task RunTDV_ModifierKeysTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Modifier Keys Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press modifier key combinations to test...\r\n");
        await session.WriteAsync("Expected combinations:\r\n");
        await session.WriteAsync("  Shift+Arrow: ESC[1;2A/B/C/D\r\n");
        await session.WriteAsync("  Ctrl+Arrow:  ESC[1;5A/B/C/D\r\n");
        await session.WriteAsync("  Alt+Arrow:   ESC[1;3A/B/C/D\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            if (input.Name != null)
                await session.WriteAsync($"\x1b[32m[OK] {input.Name} detected!\x1b[0m\r\n");
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown sequence: {visible}\x1b[0m\r\n");

            await session.WriteAsync("Press another modifier combination or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Interactive test for TDV Extended Control Mode keys (CSI nn _ sequences)
    /// </summary>
    private async Task RunTDV_ExtendedControlKeysInteractiveTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Extended Control Mode Keys Interactive Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press any TDV function/editing/system key to see the CSI nn _ sequence...\r\n");
        await session.WriteAsync("Terminal must have Extended Control Mode ON.\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            // Show hex dump of the raw bytes
            var sb = new StringBuilder();
            sb.Append("Hex: ");
            for (int i = 0; i < input.Value.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append($"{((int)input.Value[i]):X2}");
            }
            await session.WriteAsync($"{sb}\r\n");

            if (input.Name != null)
            {
                // Check if it's a CSI nn _ sequence (ends with '_')
                if (input.Value.Length >= 2 && input.Value[0] == 0x1B && input.Value[1] == '[' &&
                    input.Value.Length > 0 && input.Value[input.Value.Length - 1] == '_')
                    await session.WriteAsync($"\x1b[32m[OK] CSI nn _ key: {input.Name}\x1b[0m\r\n");
                else
                    await session.WriteAsync($"\x1b[36m[--] Non-CSI-_ key: {input.Name}\x1b[0m\r\n");
            }
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown escape sequence\x1b[0m\r\n");
            else if (input.Type == InputType.Control)
                await session.WriteAsync($"\x1b[33m? Control char: 0x{((int)input.Value[0]):X2}\x1b[0m\r\n");

            await session.WriteAsync("Press another key or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Interactive test for TDV Numpad Function Mode keys (CSI nn _ sequences)
    /// </summary>
    private async Task RunTDV_NumpadFunctionKeysInteractiveTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV Numpad Function Mode Keys Interactive Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press numpad keys to see the CSI sequences...\r\n");
        await session.WriteAsync("Requires: Extended Control Mode ON + Numpad switch = Function\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            // Show hex dump
            var sb = new StringBuilder();
            sb.Append("Hex: ");
            for (int i = 0; i < input.Value.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append($"{((int)input.Value[i]):X2}");
            }
            await session.WriteAsync($"{sb}\r\n");

            if (input.Name != null)
            {
                if (input.Name.Contains("Numpad_"))
                    await session.WriteAsync($"\x1b[32m[OK] Numpad function key: {input.Name}\x1b[0m\r\n");
                else
                    await session.WriteAsync($"\x1b[36m[--] Non-numpad key: {input.Name}\x1b[0m\r\n");
            }
            else if (input.Type == InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[33m? Unknown escape sequence\x1b[0m\r\n");
            else if (input.Type == InputType.Character)
                await session.WriteAsync($"\x1b[36m[--] ASCII char '{input.Value}' (numpad NOT in function mode?)\x1b[0m\r\n");

            await session.WriteAsync("Press another numpad key or 'B' to go back...\r\n");
        }
    }

    /// <summary>
    /// Comprehensive interactive test for all TDV keys.
    /// Detects: CSI nn _ (Extended Control Mode), VT220 F-keys, DCS PUSH keys,
    /// C0 control codes, arrows, modifiers, numpad, and all typewriter keys.
    /// </summary>
    private async Task RunTDV_AllKeysInteractiveTestAsync(TelnetSession session)
    {
        await session.WriteAsync("\x1b[2J\x1b[H");
        await session.WriteAsync("\r\n\x1b[1;33m=== TDV All Keys Interactive Test ===\x1b[0m\r\n\r\n");

        await session.WriteAsync("Press ANY key to see what sequence is sent...\r\n");
        await session.WriteAsync("This will detect all types of TDV keys:\r\n");
        await session.WriteAsync("- Editing keys: MERK/FELT/AVSN/SETN/ORD      (CSI 00-09 _)\r\n");
        await session.WriteAsync("- Action keys:  STRYK/KOPI/FLYTT              (CSI 10-15 _)\r\n");
        await session.WriteAsync("- F-row keys:   TAB+/TAB-, F48, F49           (CSI 16-21 _)\r\n");
        await session.WriteAsync("- Middle block:  <<>>/JUST/<>><               (CSI 22-27 _)\r\n");
        await session.WriteAsync("- Navigation:   Roll/ANGRE/Field/TAB          (CSI 28-41 _)\r\n");
        await session.WriteAsync("- System keys:  FUNK/SKRIV/HJELP/SLUTT        (CSI 42-49 _)\r\n");
        await session.WriteAsync("- Function:     F1-F4 (w/ Ctrl)               (CSI 50-59 _)\r\n");
        await session.WriteAsync("- Function:     F5-F8                          (CSI 60-67 _)\r\n");
        await session.WriteAsync("- Numpad:       0-9, ., -, SP, ENTER           (CSI 68-81 _)\r\n");
        await session.WriteAsync("- Misc:         EKSP/Squiggle/NewParagraph    (CSI 82-87 _)\r\n");
        await session.WriteAsync("- PUSH keys:    P1-P8 (DCS format)\r\n");
        await session.WriteAsync("- Arrows:       Up/Down/Left/Right + modifiers\r\n");
        await session.WriteAsync("- Fixed keys:   BS(08)/GS(1D)/CAN(18)/FS(1C)/VT(0B)\r\n");
        await session.WriteAsync("- VT220 F-keys: F1-F20 (ESC[nn~ / ESC O x)\r\n\r\n");
        await session.WriteAsync("Press 'B' to go back to menu\r\n\r\n");

        while (true)
        {
            var input = await session.ReadInputAsync();
            if (input.Type == InputType.Character && (input.CommandChar == 'B' || input.CommandChar == 'b'))
                break;

            var visible = ToVisible(input.Value);
            await session.WriteAsync($"\r\nReceived: {visible}\r\n");

            // Show hex dump of raw bytes
            var sb = new StringBuilder();
            sb.Append("Hex: ");
            for (int i = 0; i < input.Value.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append($"{((int)input.Value[i]):X2}");
            }
            await session.WriteAsync($"{sb}\r\n");

            // Classify and categorize
            string category;
            string keyName;

            if (input.Type == InputType.EscapeSequence)
            {
                if (input.Value.Length >= 3 && input.Value[0] == 0x1B && input.Value[1] == '[' &&
                    input.Value[input.Value.Length - 1] == '_')
                {
                    category = "TDV CSI nn _";
                }
                else if (input.Value.Length >= 3 && input.Value[0] == 0x1B && input.Value[1] == 'P')
                {
                    category = "DCS (PUSH)";
                }
                else if (input.Value.Length >= 3 && input.Value[0] == 0x1B && input.Value[1] == 'O')
                {
                    category = "SS3";
                }
                else if (input.Value.Length >= 3 && input.Value[0] == 0x1B && input.Value[1] == '[')
                {
                    category = "CSI";
                }
                else
                {
                    category = "ESC";
                }

                keyName = input.Name ?? "Unknown";
            }
            else if (input.Type == InputType.Control)
            {
                category = "C0 Control";
                keyName = input.Name ?? $"0x{((int)input.Value[0]):X2}";
            }
            else if (input.Type == InputType.Tab)
            {
                category = "C0 Control";
                keyName = input.Name ?? "Tab";
            }
            else if (input.Type == InputType.Backspace)
            {
                category = "C0 Control";
                keyName = "Backspace";
            }
            else if (input.Type == InputType.Enter)
            {
                category = "C0 Control";
                keyName = "Enter";
            }
            else
            {
                category = "ASCII";
                keyName = $"'{input.Value}'";
            }

            if (input.Name != null || input.Type != InputType.EscapeSequence)
                await session.WriteAsync($"\x1b[32m[OK] [{category}] {keyName}\x1b[0m\r\n");
            else
                await session.WriteAsync($"\x1b[33m[??] [{category}] {keyName} — {visible}\x1b[0m\r\n");

            await session.WriteAsync("Press another key or 'B' to go back...\r\n");
        }
    }

    #endregion
}


