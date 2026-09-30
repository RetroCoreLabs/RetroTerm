using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Selection;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Tests for the extended menu system: public API on TerminalCanvas/TerminalControl,
/// keyboard shortcut routing (Ctrl+Shift combos bypass C0 fallback), and SelectAll behavior.
/// </summary>
[Collection("Avalonia")]
public class MenuAndShortcutTests
{
    private readonly ITestOutputHelper _output;

    public MenuAndShortcutTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Creates a headless Window containing a TerminalCanvas wired to a VT100 emulator.
    /// Returns the window and canvas for testing.
    /// </summary>
    private (Window window, TerminalCanvas canvas) CreateTestWindow()
    {
        var window = new Window { Width = 800, Height = 600 };
        var canvas = new TerminalCanvas();
        window.Content = canvas;
        window.Show();

        var emulator = new VT100Emulator(80, 24);
        canvas.SetEmulator(emulator);
        canvas.Focus();

        return (window, canvas);
    }

    /// <summary>
    /// Creates a headless Window containing a TerminalControl wired to a VT100 emulator.
    /// </summary>
    private (Window window, TerminalControl control) CreateTestWindowWithControl()
    {
        var window = new Window { Width = 800, Height = 600 };
        var control = new TerminalControl();
        window.Content = control;
        window.Show();

        var emulator = new VT100Emulator(80, 24);
        control.SetEmulator(emulator);
        control.Focus();

        return (window, control);
    }

    #region TerminalCanvas Public API Tests

    [AvaloniaFact]
    public void TerminalCanvas_SelectAll_ShouldSelectEntireBuffer()
    {
        var (window, canvas) = CreateTestWindow();

        // Act
        canvas.SelectAll();

        // Assert
        var selectionManager = canvas.GetSelectionManager();
        Assert.NotNull(selectionManager);
        Assert.True(selectionManager.HasSelection);

        var selection = selectionManager.CurrentSelection;
        Assert.NotNull(selection);
        var (start, end) = selection.Normalized;
        Assert.Equal((0, 0), start);
        Assert.Equal((23, 79), end);

        _output.WriteLine("SelectAll correctly selects entire 80x24 buffer");
        window.Close();
    }

    [AvaloniaFact]
    public void TerminalCanvas_SelectAll_ThenGetSelectedText_ShouldReturnBufferContent()
    {
        var (window, canvas) = CreateTestWindow();
        var selectionManager = canvas.GetSelectionManager();
        Assert.NotNull(selectionManager);

        // Write some text to the emulator buffer via ProcessData
        var emulator = new VT100Emulator(80, 24);
        canvas.SetEmulator(emulator);
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes("Hello World"));

        // Re-grab selection manager after SetEmulator replaces it
        selectionManager = canvas.GetSelectionManager();
        Assert.NotNull(selectionManager);

        // Act
        canvas.SelectAll();
        var text = selectionManager.GetSelectedText();

        // Assert
        Assert.Contains("Hello World", text);
        _output.WriteLine($"Selected text contains 'Hello World': OK");
        window.Close();
    }

    [AvaloniaFact]
    public void TerminalCanvas_Copy_WithNoSelection_ShouldNotThrow()
    {
        var (window, canvas) = CreateTestWindow();

        // Act — should not throw even with no selection
        var exception = Record.Exception(() => canvas.Copy());
        Assert.Null(exception);

        _output.WriteLine("Copy with no selection: no exception");
        window.Close();
    }

    [AvaloniaFact]
    public void TerminalCanvas_Paste_WithNoClipboard_ShouldNotThrow()
    {
        var (window, canvas) = CreateTestWindow();

        // Act — should not throw
        var exception = Record.Exception(() => canvas.Paste());
        Assert.Null(exception);

        _output.WriteLine("Paste with empty clipboard: no exception");
        window.Close();
    }

    #endregion

    #region TerminalControl Pass-Through Tests

    [AvaloniaFact]
    public void TerminalControl_SelectAll_ShouldDelegateToCanvas()
    {
        var (window, control) = CreateTestWindowWithControl();

        // Act — should not throw and should propagate to canvas
        var exception = Record.Exception(() => control.SelectAll());
        Assert.Null(exception);

        _output.WriteLine("TerminalControl.SelectAll delegates without error");
        window.Close();
    }

    [AvaloniaFact]
    public void TerminalControl_Copy_ShouldDelegateToCanvas()
    {
        var (window, control) = CreateTestWindowWithControl();

        var exception = Record.Exception(() => control.Copy());
        Assert.Null(exception);

        _output.WriteLine("TerminalControl.Copy delegates without error");
        window.Close();
    }

    [AvaloniaFact]
    public void TerminalControl_Paste_ShouldDelegateToCanvas()
    {
        var (window, control) = CreateTestWindowWithControl();

        var exception = Record.Exception(() => control.Paste());
        Assert.Null(exception);

        _output.WriteLine("TerminalControl.Paste delegates without error");
        window.Close();
    }

    #endregion

    #region Keyboard Shortcut Routing Tests

    [AvaloniaFact]
    public void CtrlShiftA_ShouldNotProduceTerminalInput()
    {
        // Ctrl+Shift+A must NOT be consumed by C0 fallback —
        // it should bubble to MainWindow for Select All.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act — simulate Ctrl+Shift+A
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Assert — InputReceived should NOT have fired
        Assert.Null(receivedInput);
        _output.WriteLine("Ctrl+Shift+A correctly not consumed by terminal (bubbles for Select All)");
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlShiftF_ShouldNotProduceTerminalInput()
    {
        // Ctrl+Shift+F must NOT be consumed by C0 fallback —
        // it should bubble to MainWindow for Find.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act — simulate Ctrl+Shift+F
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Assert — InputReceived should NOT have fired
        Assert.Null(receivedInput);
        _output.WriteLine("Ctrl+Shift+F correctly not consumed by terminal (bubbles for Find)");
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlShiftC_ShouldNotProduceTerminalInput()
    {
        // Ctrl+Shift+C is handled as Copy — must NOT send 0x03 to terminal.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Assert
        Assert.Null(receivedInput);
        _output.WriteLine("Ctrl+Shift+C correctly handled as Copy, not sent to terminal");
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlShiftV_ShouldNotProduceTerminalInput()
    {
        // Ctrl+Shift+V is handled as Paste — must NOT send 0x16 to terminal.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Assert
        Assert.Null(receivedInput);
        _output.WriteLine("Ctrl+Shift+V correctly handled as Paste, not sent to terminal");
        window.Close();
    }

    [AvaloniaFact]
    public void BareCtrlA_ShouldProduceC0ControlCode()
    {
        // Bare Ctrl+A (no Shift) SHOULD send 0x01 (SOH) to terminal.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act — simulate bare Ctrl+A
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);

        // Assert — should have sent 0x01
        Assert.NotNull(receivedInput);
        Assert.Equal("\x01", receivedInput);
        _output.WriteLine("Ctrl+A correctly sends 0x01 (SOH) to terminal");
        window.Close();
    }

    [AvaloniaFact]
    public void BareCtrlC_ShouldProduceC0ControlCode()
    {
        // Bare Ctrl+C (no Shift) SHOULD send 0x03 (ETX) to terminal.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.C, RawInputModifiers.Control);

        // Assert — 0x03 (ETX/interrupt)
        Assert.NotNull(receivedInput);
        Assert.Equal("\x03", receivedInput);
        _output.WriteLine("Ctrl+C correctly sends 0x03 (ETX) to terminal");
        window.Close();
    }

    [AvaloniaFact]
    public void BareCtrlF_ShouldProduceC0ControlCode()
    {
        // Bare Ctrl+F (no Shift) SHOULD send 0x06 (ACK) to terminal.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.F, RawInputModifiers.Control);

        // Assert — 0x06 (ACK)
        Assert.NotNull(receivedInput);
        Assert.Equal("\x06", receivedInput);
        _output.WriteLine("Ctrl+F correctly sends 0x06 (ACK) to terminal");
        window.Close();
    }

    #endregion

    #region SelectionManager.SelectAll Integration Tests

    [Fact]
    public void SelectionManager_SelectAll_WithContent_AllTextIncluded()
    {
        // Verify SelectAll captures all buffer content, not just visible area
        var buffer = new TerminalBuffer(40, 10);

        // Fill first and last rows
        string firstRow = "First row content";
        for (int i = 0; i < firstRow.Length; i++)
            buffer[0, i] = new TerminalCell(firstRow[i]);

        string lastRow = "Last row content";
        for (int i = 0; i < lastRow.Length; i++)
            buffer[9, i] = new TerminalCell(lastRow[i]);

        var manager = new SelectionManager(buffer);

        // Act
        manager.SelectAll();
        var text = manager.GetSelectedText();

        // Assert
        Assert.Contains("First row content", text);
        Assert.Contains("Last row content", text);
        _output.WriteLine("SelectAll captures content from first and last rows");
    }

    [Fact]
    public void SelectionManager_SelectAll_SelectionMode_IsCharacter()
    {
        var buffer = new TerminalBuffer(80, 24);
        var manager = new SelectionManager(buffer);

        manager.SelectAll();

        Assert.NotNull(manager.CurrentSelection);
        Assert.Equal(RetroTerm.Core.Selection.SelectionMode.Character, manager.CurrentSelection.Mode);
        _output.WriteLine("SelectAll uses Character selection mode");
    }

    #endregion

    #region Connect Shortcut Routing Tests

    [AvaloniaFact]
    public void CtrlShiftN_ShouldNotProduceTerminalInput()
    {
        // Ctrl+Shift+N must NOT be consumed by C0 fallback —
        // it should bubble to MainWindow for Connect dialog.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act — simulate Ctrl+Shift+N
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift);

        // Assert — InputReceived should NOT have fired
        Assert.Null(receivedInput);
        _output.WriteLine("Ctrl+Shift+N correctly not consumed by terminal (bubbles for Connect)");
        window.Close();
    }

    [AvaloniaFact]
    public void BareCtrlN_ShouldProduceC0ControlCode()
    {
        // Bare Ctrl+N (no Shift) SHOULD send 0x0E (SO/Shift Out) to terminal.
        var (window, canvas) = CreateTestWindow();
        string? receivedInput = null;
        canvas.InputReceived += input => receivedInput = input;

        // Act
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.N, RawInputModifiers.Control);

        // Assert — 0x0E (SO)
        Assert.NotNull(receivedInput);
        Assert.Equal("\x0E", receivedInput);
        _output.WriteLine("Ctrl+N correctly sends 0x0E (SO) to terminal");
        window.Close();
    }

    #endregion

    #region Disconnect Menu State Tests

    [AvaloniaFact]
    public void DisconnectMenuItem_ShouldBeDisabledByDefault()
    {
        // The Disconnect menu item should start disabled (no connection)
        var window = new RetroTerm.Desktop.MainWindow();
        window.Show();

        var disconnectItem = window.FindControl<MenuItem>("DisconnectMenuItem");
        Assert.NotNull(disconnectItem);
        Assert.False(disconnectItem.IsEnabled);

        _output.WriteLine("Disconnect menu item correctly disabled by default");
        window.Close();
    }

    [AvaloniaFact]
    public void CopyMenuItem_ShouldExist()
    {
        var window = new RetroTerm.Desktop.MainWindow();
        window.Show();

        var copyItem = window.FindControl<MenuItem>("CopyMenuItem");
        Assert.NotNull(copyItem);

        _output.WriteLine("Copy menu item exists in Edit menu");
        window.Close();
    }

    [AvaloniaFact]
    public void VirtualKeyboardMenuItem_ShouldExist()
    {
        var window = new RetroTerm.Desktop.MainWindow();
        window.Show();

        var vkItem = window.FindControl<MenuItem>("VirtualKeyboardMenuItem");
        Assert.NotNull(vkItem);

        _output.WriteLine("Virtual Keyboard menu item exists in View menu");
        window.Close();
    }

    #endregion
}
