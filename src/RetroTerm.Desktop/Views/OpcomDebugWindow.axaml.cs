using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Session;

namespace RetroTerm.Desktop.Views;

public partial class OpcomDebugWindow : Window
{
    private TerminalSession? _session;
    private OpcomProtocol? _protocol;
    private CancellationTokenSource? _uploadCts;
    private readonly StringBuilder _logBuffer = new();
    private int _logLineCount;
    private const int MaxLogLines = 10000;

    // The log is written from the protocol's thread and read by the UI timer, so the
    // buffer needs a lock. _logDirty says there is something new to show; the timer
    // clears it. See OnProtocolLogEntry for why the display is not updated per entry.
    private readonly object _logLock = new();
    private bool _logDirty;
    private volatile bool _logShowHex;
    private DispatcherTimer? _logRefreshTimer;

    /// <summary>
    /// How often the log display is refreshed while entries are pouring in. Ten times a
    /// second is faster than anybody can read and slow enough that thousands of logged
    /// bytes collapse into one redraw.
    /// </summary>
    private const int LogRefreshMs = 100;
    private DateTime _uploadStartTime;
    private DateTime _lastUploadUiUpdate;

    // Memory view state
    private int _memoryViewAddress;

    // True while the window itself is moving the pass-through checkbox, so the box's own
    // change handler does not treat it as the user clicking.
    private bool _syncingPassThrough;

    // Internal register TextBlock references (populated in code to avoid 14 FindControl calls)
    private readonly TextBlock?[] _iRegLabels = new TextBlock?[InternalRegisterDefs.Count];

    public OpcomDebugWindow()
    {
        InitializeComponent();

        // Populate level selector (00-17 octal = 0-15 decimal)
        var levelSelector = this.FindControl<ComboBox>("LevelSelector")!;
        for (int i = 0; i < 16; i++)
        {
            levelSelector.Items.Add(OctalHelper.ToOctal6((ushort)i).Substring(4)); // Last 2 digits
        }
        levelSelector.SelectedIndex = 0;
        levelSelector.SelectionChanged += (_, _) => UpdateRegisterDisplay();

        // Populate boot device combo
        var bootCombo = this.FindControl<ComboBox>("BootDeviceCombo")!;
        for (int i = 0; i < OpcomAldDecoder.BootPresets.Length; i++)
        {
            var preset = OpcomAldDecoder.BootPresets[i];
            bootCombo.Items.Add($"{preset.OpcomCommand}& - {preset.DisplayName}");
        }
        bootCombo.Items.Add("Custom...");
        bootCombo.SelectedIndex = 0;

        // Populate upload method combo
        var uploadMethodCombo = this.FindControl<ComboBox>("UploadMethodCombo")!;
        uploadMethodCombo.Items.Add("Deposit loop (slow)");
        uploadMethodCombo.Items.Add("NDBoot fast transfer (3 chars/word)");
        uploadMethodCombo.SelectedIndex = 0;

        // Wire pass-through checkbox
        var ptCb = this.FindControl<CheckBox>("PassThroughCheckBox")!;
        ptCb.IsCheckedChanged += (_, _) =>
        {
            if (_syncingPassThrough) return; // the window is updating the box, not the user
            if (_protocol != null)
                _protocol.PassThrough = ptCb.IsChecked == true;
        };

        // Wire TRA/TRR button tabs
        var traBtn = this.FindControl<Button>("TraTabBtn")!;
        var trrBtn = this.FindControl<Button>("TrrTabBtn")!;
        traBtn.Click += (_, _) => SelectInternalRegTab(0);
        trrBtn.Click += (_, _) => SelectInternalRegTab(1);

        // Wire lower area button tabs
        var memBtn = this.FindControl<Button>("MemoryTabBtn")!;
        var uplBtn = this.FindControl<Button>("UploadTabBtn")!;
        var ioBtn = this.FindControl<Button>("IoBootTabBtn")!;
        var logBtn = this.FindControl<Button>("LogTabBtn")!;
        memBtn.Click += (_, _) => SelectLowerTab(0);
        uplBtn.Click += (_, _) => SelectLowerTab(1);
        ioBtn.Click += (_, _) => SelectLowerTab(2);
        logBtn.Click += (_, _) => SelectLowerTab(3);

        // Wire working register TextBoxes: write on Enter or LostFocus
        string[] regBoxNames = { "RegS", "RegD", "RegP", "RegB", "RegL", "RegA", "RegT", "RegX" };
        for (int i = 0; i < regBoxNames.Length; i++)
        {
            int regIndex = i; // Capture for closure
            var tb = this.FindControl<TextBox>(regBoxNames[i]);
            if (tb != null)
            {
                tb.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter)
                        OnWorkingRegisterEnter(regIndex, tb.Text);
                };
                tb.LostFocus += (_, _) => OnWorkingRegisterEnter(regIndex, tb.Text);
            }
        }

        // Wire TRR (write) register TextBoxes: write on LostFocus or Enter
        WireTrrRegisterBoxes();

        // The log formatter runs off the UI thread, so it cannot read the checkbox
        // directly. Mirror it into a field the moment it changes instead.
        var hexCb = this.FindControl<CheckBox>("LogHexCheckBox");
        if (hexCb != null)
        {
            _logShowHex = hexCb.IsChecked == true;
            hexCb.IsCheckedChanged += (_, _) =>
            {
                _logShowHex = hexCb.IsChecked == true;
                // Only new lines change format; the ones already buffered stay as they
                // were written, which is the same as any other console log.
            };
        }

        StartLogRefreshTimer();
    }

    /// <summary>
    /// Feeds one log entry in, as the protocol would. For tests only, so the flood can be
    /// reproduced without a machine.
    /// </summary>
    internal void HandleLogEntryForTest(OpcomLogEntry entry) => OnProtocolLogEntry(entry);

    /// <summary>
    /// Starts the timer that moves buffered log lines onto the screen.
    ///
    /// This is the other half of the freeze fix. The protocol appends to the buffer as
    /// fast as it likes; the window redraws at a fixed, human rate, and only when the log
    /// tab is actually showing. With the tab hidden nothing is rebuilt at all, so a long
    /// upload costs nothing until somebody looks at it.
    /// </summary>
    private void StartLogRefreshTimer()
    {
        _logRefreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(LogRefreshMs),
            DispatcherPriority.Background, (_, _) => RefreshLogDisplay());
        _logRefreshTimer.Start();
    }

    /// <summary>
    /// Copies the buffered log onto the screen if anything has changed and the log is
    /// visible. Internal so a headless test can drive it without waiting for a timer.
    /// </summary>
    internal void RefreshLogDisplay()
    {
        var panel = this.FindControl<DockPanel>("LogPanel");
        if (panel == null || !panel.IsVisible) return; // nobody is looking

        string text;
        lock (_logLock)
        {
            if (!_logDirty) return;
            _logDirty = false;
            text = _logBuffer.ToString();
        }

        var display = this.FindControl<TextBlock>("LogDisplayText");
        if (display != null) display.Text = text;

        var autoScroll = this.FindControl<CheckBox>("LogAutoScrollCheckBox");
        if (autoScroll?.IsChecked == true)
        {
            this.FindControl<ScrollViewer>("LogScrollViewer")?.ScrollToEnd();
        }
    }

    /// <summary>
    /// Turns one logged transfer into a log line. Static and given everything it needs,
    /// so it can run on the protocol's thread without touching a control.
    /// </summary>
    internal static string FormatLogEntry(OpcomLogEntry entry, bool showHex)
    {
        var sb = new StringBuilder(32 + entry.Data.Length * 4);
        sb.Append('[').Append(entry.Timestamp.ToString("HH:mm:ss.fff")).Append("] [")
          .Append(entry.Direction == OpcomLogDirection.Tx ? "TX" : "RX").Append("] ");

        for (int i = 0; i < entry.Data.Length; i++)
        {
            if (showHex)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(entry.Data[i].ToString("X2"));
                continue;
            }

            byte b = (byte)(entry.Data[i] & 0x7F);
            if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            else if (b == 0x0D) sb.Append("<CR>");
            else if (b == 0x0A) sb.Append("<LF>");
            else if (b == 0x1B) sb.Append("<ESC>");
            else sb.Append('<').Append(b.ToString("X2")).Append('>');
        }

        sb.Append('\n');
        return sb.ToString();
    }

    private void WireTrrRegisterBoxes()
    {
        // TRR registers: write goes through the Ixx slot (OPCOM uses Ixx/ to examine,
        // then deposit writes via TRR to the WRITE register at that slot).
        // E.g. I2/ reads OPR (TRA 02), deposit to I2 writes LMP (TRR 02).
        string[] trrBoxNames = { "TrrPANC", "TrrSTS", "TrrLMP", "TrrPCR", "TrrIIE", "TrrPID", "TrrPIE", "TrrCCL", "TrrLCIL", "TrrUCIL" };
        string[] trrSlotCmds = { "I0", "I1", "I2", "I3", "I5", "I6", "I7", "I10", "I11", "I12" };

        for (int i = 0; i < trrBoxNames.Length; i++)
        {
            string slotCmd = trrSlotCmds[i];
            var tb = this.FindControl<TextBox>(trrBoxNames[i]);
            if (tb != null)
            {
                tb.LostFocus += (_, _) => OnTrrRegisterWrite(slotCmd, tb.Text);
                tb.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter)
                        OnTrrRegisterWrite(slotCmd, tb.Text);
                };
            }
        }
    }

    private async void OnTrrRegisterWrite(string slotCmd, string? text)
    {
        if (_protocol == null || string.IsNullOrWhiteSpace(text)) return;
        if (!OctalHelper.TryParseOctal(text.AsSpan(), out ushort value)) return;

        // Write via Ixx slot — OPCOM examines the TRA register, then deposit writes the TRR register
        await _protocol.WriteInternalRegisterAsync(slotCmd, value);
        // No read-back — TRR registers are write-only, reading back would return the TRA register
    }

    private void SelectInternalRegTab(int index)
    {
        var buttons = new[] { this.FindControl<Button>("TraTabBtn"), this.FindControl<Button>("TrrTabBtn") };
        var panels = new[] { this.FindControl<Grid>("TraRegsGrid"), this.FindControl<Grid>("TrrRegsGrid") };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null || panels[i] == null) continue;
            if (i == index)
            {
                buttons[i]!.Classes.Add("active");
                panels[i]!.IsVisible = true;
            }
            else
            {
                buttons[i]!.Classes.Remove("active");
                panels[i]!.IsVisible = false;
            }
        }
    }

    private void SelectLowerTab(int index)
    {
        var buttons = new[] {
            this.FindControl<Button>("MemoryTabBtn"),
            this.FindControl<Button>("UploadTabBtn"),
            this.FindControl<Button>("IoBootTabBtn"),
            this.FindControl<Button>("LogTabBtn")
        };
        string[] panelNames = { "MemoryPanel", "UploadPanel", "IoBootPanel", "LogPanel" };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            var panel = this.FindControl<DockPanel>(panelNames[i]);
            if (i == index)
            {
                buttons[i]!.Classes.Add("active");
                if (panel != null) panel.IsVisible = true;
                // Coming back to the log shows whatever piled up while it was hidden.
                if (panelNames[i] == "LogPanel")
                {
                    lock (_logLock) _logDirty = true;
                    RefreshLogDisplay();
                }
            }
            else
            {
                buttons[i]!.Classes.Remove("active");
                if (panel != null) panel.IsVisible = false;
            }
        }
    }

    private async void OnWorkingRegisterEnter(int regIndex, string? text)
    {
        if (_protocol == null || string.IsNullOrWhiteSpace(text)) return;
        if (!OctalHelper.TryParseOctal(text.AsSpan(), out ushort value)) return;

        var levelCombo = this.FindControl<ComboBox>("LevelSelector")!;
        int level = levelCombo.SelectedIndex;
        string regName = WorkingRegisterNames.ByNumber[regIndex];

        // Write the value, then read it back to confirm
        await _protocol.WriteRegisterAsync(level, regName, value);
        await _protocol.ReadRegisterAsync(level, regName);
    }

    private async void OnInternalRegisterEnter(string regName, string? text)
    {
        if (_protocol == null || string.IsNullOrWhiteSpace(text)) return;
        if (!OctalHelper.TryParseOctal(text.AsSpan(), out ushort value)) return;

        await _protocol.WriteInternalRegisterAsync(regName, value);
        await _protocol.ReadInternalRegisterAsync(regName);
    }

    /// <summary>
    /// Initializes the window with a terminal session.
    /// Creates and attaches the OPCOM protocol handler.
    /// </summary>
    public void Initialize(TerminalSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _protocol = new OpcomProtocol();

        // Wire up events
        _protocol.LogEntry += OnProtocolLogEntry;
        _protocol.StateChanged += OnProtocolStateChanged;
        _protocol.CpuStateChanged += OnCpuStateChanged;
        _protocol.ProgressChanged += OnProtocolProgress;
        _protocol.Registers.RegistersChanged += OnRegistersChanged;
        _protocol.Memory.MemoryChanged += OnMemoryChanged;
        _protocol.Memory.MemoryRangeChanged += OnMemoryRangeChanged;

        // Attach to session
        session.AttachOpcomHandler(_protocol);

        // Push the checkbox's state into the freshly created protocol.
        //
        // Reported by Ronny on 8 September 2026: pass-through is ticked when the window
        // opens but nothing passes through until the box is unticked and ticked again.
        // The cause is that the checkbox and the protocol were two separate truths. The
        // box is wired in the CONSTRUCTOR, where no protocol exists yet, and the protocol
        // is created here, so nothing ever carried one value to the other. They agreed
        // only because both happened to default to on - and stopped agreeing the moment
        // anything switched the protocol off behind the box's back, which an upload and
        // the NDBoot monitor check both do. The box then read "ticked" over a protocol
        // that was off, and toggling it was the only thing that put them back in step.
        SetPassThrough(this.FindControl<CheckBox>("PassThroughCheckBox")?.IsChecked == true);

        // Update status
        UpdateConnectionStatus();

        // Cache internal register label references
        CacheInternalRegisterLabels();
    }

    /// <summary>
    /// Sets pass-through on BOTH the protocol and the checkbox, so the window can never
    /// show one thing while the protocol does another.
    ///
    /// Everything that changes pass-through goes through here, including the temporary
    /// switch-off during an upload and in NDBoot mode. Those used to write straight to
    /// the protocol, which is how the box ended up claiming pass-through was on while it
    /// was off - see the note in Initialize.
    /// </summary>
    private void SetPassThrough(bool on)
    {
        if (_protocol != null) _protocol.PassThrough = on;

        var box = this.FindControl<CheckBox>("PassThroughCheckBox");
        if (box == null || (box.IsChecked == true) == on) return;

        _syncingPassThrough = true;
        try { box.IsChecked = on; }
        finally { _syncingPassThrough = false; }
    }

    /// <summary>
    /// Drives the pass-through setter from a test, standing in for an upload or the
    /// NDBoot check switching it off for their duration.
    /// </summary>
    internal void SetPassThroughForTest(bool on) => SetPassThrough(on);

    private void CacheInternalRegisterLabels()
    {
        // TRA register names matching the XAML control names (Tra + name)
        // All are read-only TextBlocks in the TRA tab
        string[] traNames = { "PANS", "STS", "OPR", "PGS", "PVL", "IIC", "PID", "PIE",
                              "CSR", "ACTL", "ALD", "PES", "PCR", "PEA" };
        for (int i = 0; i < traNames.Length && i < _iRegLabels.Length; i++)
        {
            _iRegLabels[i] = this.FindControl<TextBlock>($"Tra{traNames[i]}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _uploadCts?.Cancel();
        _logRefreshTimer?.Stop();
        _logRefreshTimer = null;

        if (_protocol != null)
        {
            _protocol.LogEntry -= OnProtocolLogEntry;
            _protocol.StateChanged -= OnProtocolStateChanged;
            _protocol.CpuStateChanged -= OnCpuStateChanged;
            _protocol.ProgressChanged -= OnProtocolProgress;
            _protocol.Registers.RegistersChanged -= OnRegistersChanged;
            _protocol.Memory.MemoryChanged -= OnMemoryChanged;
            _protocol.Memory.MemoryRangeChanged -= OnMemoryRangeChanged;
        }

        _session?.DetachOpcomHandler();
    }

    private void UpdateConnectionStatus()
    {
        var label = this.FindControl<TextBlock>("StatusLabel")!;
        if (_session?.IsConnected == true)
        {
            label.Text = "Connected";
            label.Foreground = Avalonia.Media.Brushes.LightGreen;
        }
        else
        {
            label.Text = "Disconnected";
            label.Foreground = Avalonia.Media.Brushes.Gray;
        }
    }

    // ==================== Event Handlers (from protocol, dispatched to UI thread) ====================

    /// <summary>
    /// Takes a logged byte and puts it in the buffer. This runs on whatever thread the
    /// protocol is on and DOES NOT TOUCH THE WINDOW.
    ///
    /// Reported by Ronny, 6 September 2026: opening the OPCOM Log during a BPUN upload
    /// froze the window - Windows marked it Not Responding and it went translucent -
    /// while the transfer itself carried on. The cause was here. Every logged byte used
    /// to post its own item to the UI thread, and each of those rebuilt the WHOLE log
    /// into a fresh string, assigned it to the text block and scrolled to the end. A
    /// deposit-loop upload logs a send and an echo per character, so a few thousand words
    /// is tens of thousands of full re-layouts of a ten-thousand-line block. The
    /// dispatcher queue simply outran the UI thread, and the transfer kept going because
    /// it never needed that thread.
    ///
    /// So the formatting happens here, off the UI thread, and the display is refreshed on
    /// a timer instead - see StartLogRefreshTimer.
    /// </summary>
    private void OnProtocolLogEntry(OpcomLogEntry entry)
    {
        string line = FormatLogEntry(entry, _logShowHex);
        lock (_logLock)
        {
            _logBuffer.Append(line);
            _logLineCount++;
            if (_logLineCount > MaxLogLines)
            {
                // Drop the oldest line rather than letting the buffer grow without limit.
                int cutIndex = -1;
                for (int ci = 0; ci < _logBuffer.Length; ci++)
                {
                    if (_logBuffer[ci] == '\n') { cutIndex = ci; break; }
                }
                if (cutIndex >= 0)
                {
                    _logBuffer.Remove(0, cutIndex + 1);
                    _logLineCount--;
                }
            }
            _logDirty = true;
        }
    }

    private void OnProtocolStateChanged(OpcomProtocolState state)
    {
        // Could update a state indicator if needed
    }

    /// <summary>
    /// Shows what the protocol is doing in the status bar at the bottom of the window.
    ///
    /// The bar exists because a dump takes seconds on a real machine - every character is
    /// sent one at a time and waits for its echo - and the window used to sit there
    /// looking identical whether it was working or hung. It is not a modal dialog on
    /// purpose: nothing here should stop you reading the registers while a dump runs.
    ///
    /// A finished command leaves its outcome on screen rather than blanking, so a failure
    /// is still readable afterwards.
    /// </summary>
    private void OnProtocolProgress(OpcomProgress progress)
    {
        Dispatcher.UIThread.Post(() => ApplyProgress(progress));
    }

    /// <summary>
    /// Puts a progress report on screen. Split out from the event handler so a headless
    /// test can drive it directly instead of waiting on a dispatcher.
    /// </summary>
    internal void ApplyProgress(OpcomProgress progress)
    {
        var message = this.FindControl<TextBlock>("ProgressMessageLabel");
        var count = this.FindControl<TextBlock>("ProgressCountLabel");
        var bar = this.FindControl<ProgressBar>("OperationProgressBar");
        if (message == null || count == null || bar == null) return;

        switch (progress.Kind)
        {
            case OpcomProgressKind.Idle:
                message.Text = "Ready";
                message.Foreground = this.FindResource("SecondaryTextBrush") as Avalonia.Media.IBrush
                                     ?? Avalonia.Media.Brushes.Gray;
                count.Text = "";
                bar.IsVisible = false;
                return;

            case OpcomProgressKind.Failed:
                message.Text = progress.Message;
                message.Foreground = Avalonia.Media.Brushes.OrangeRed;
                count.Text = "";
                bar.IsVisible = false;
                return;

            case OpcomProgressKind.Succeeded:
                message.Text = progress.Message;
                message.Foreground = Avalonia.Media.Brushes.LightGreen;
                count.Text = "";
                bar.IsVisible = false;
                return;
        }

        // Running.
        message.Text = progress.Message;
        message.Foreground = this.FindResource("PrimaryTextBrush") as Avalonia.Media.IBrush
                             ?? Avalonia.Media.Brushes.White;

        if (progress.Total > 0)
        {
            count.Text = progress.Completed + " / " + progress.Total;
            bar.IsIndeterminate = false;
            bar.Value = progress.Percent;
        }
        else
        {
            // No count to show - a single examine is one step - so the bar just moves to
            // say the line is busy rather than pretending to know how far along it is.
            count.Text = "";
            bar.IsIndeterminate = true;
        }
        bar.IsVisible = true;
    }

    private void OnCpuStateChanged(OpcomCpuState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var label = this.FindControl<TextBlock>("CpuStateLabel")!;
            switch (state)
            {
                case OpcomCpuState.Running:
                    label.Text = "RUNNING";
                    label.Foreground = Avalonia.Media.Brushes.LightGreen;
                    break;
                case OpcomCpuState.Stopped:
                    label.Text = "STOPPED";
                    label.Foreground = Avalonia.Media.Brushes.Orange;
                    break;
                default:
                    label.Text = "UNKNOWN";
                    label.Foreground = Avalonia.Media.Brushes.Gray;
                    break;
            }
        });
    }

    private void OnRegistersChanged()
    {
        Dispatcher.UIThread.Post(UpdateRegisterDisplay);
    }

    private void OnMemoryChanged(int address, ushort value)
    {
        Dispatcher.UIThread.Post(UpdateMemoryDisplay);
    }

    private void OnMemoryRangeChanged(int address, int count)
    {
        Dispatcher.UIThread.Post(UpdateMemoryDisplay);
    }

    // ==================== Register Display ====================

    private void UpdateRegisterDisplay()
    {
        if (_protocol == null) return;
        var regs = _protocol.Registers;
        var levelCombo = this.FindControl<ComboBox>("LevelSelector")!;
        int level = levelCombo.SelectedIndex;
        if (level < 0 || level >= OpcomRegisterState.MaxLevels) level = 0;

        var set = regs.Levels[level];
        SetRegText("RegS", set.S);
        SetRegText("RegD", set.D);
        SetRegText("RegP", set.P);
        SetRegText("RegB", set.B);
        SetRegText("RegL", set.L);
        SetRegText("RegA", set.A);
        SetRegText("RegT", set.T);
        SetRegText("RegX", set.X);

        // Internal registers (TRA - read side, all TextBlocks)
        for (int i = 0; i < InternalRegisterDefs.Count; i++)
        {
            string val = OctalHelper.ToOctal6(regs.Internal[i]);
            if (i < _iRegLabels.Length && _iRegLabels[i] != null)
            {
                _iRegLabels[i]!.Text = val;
            }
            // ALD decode (index 10)
            if (i == 10)
            {
                var aldInfo = OpcomAldDecoder.Decode(regs.Internal[i]);
                var aldLabel = this.FindControl<TextBlock>("AldDecodeLabel");
                if (aldLabel != null) aldLabel.Text = "> " + aldInfo.Description;
                var aldInfoLabel = this.FindControl<TextBlock>("AldInfoLabel");
                if (aldInfoLabel != null) aldInfoLabel.Text = $"ALD = {val} ({aldInfo.Description})";
            }
        }
    }

    private void SetRegText(string name, ushort value)
    {
        var tb = this.FindControl<TextBox>(name);
        if (tb != null) tb.Text = OctalHelper.ToOctal6(value);
    }

    // ==================== Memory Display ====================

    private void UpdateMemoryDisplay()
    {
        if (_protocol == null) return;
        var cache = _protocol.Memory;
        var sb = new StringBuilder();

        // Show 16 rows of 8 words each (128 words per page)
        int startAddr = _memoryViewAddress;
        ushort[] values = new ushort[8];
        bool[] valid = new bool[8];
        Span<char> asciiBuf = stackalloc char[16];

        for (int row = 0; row < 16; row++)
        {
            int rowAddr = startAddr + (row * 8);
            cache.GetRange(rowAddr, 8, values, valid);

            sb.Append(OctalHelper.ToOctal6((ushort)rowAddr));
            sb.Append(':');

            for (int col = 0; col < 8; col++)
            {
                sb.Append(' ');
                if (valid[col])
                    sb.Append(OctalHelper.ToOctal6(values[col]));
                else
                    sb.Append("------");
            }

            // ASCII representation
            sb.Append("  |");
            for (int col = 0; col < 8; col++)
            {
                if (valid[col])
                {
                    OctalHelper.WordToAscii(values[col], asciiBuf);
                    sb.Append(asciiBuf[0]);
                    sb.Append(asciiBuf[1]);
                }
                else
                {
                    sb.Append("..");
                }
            }
            sb.Append('|');
            sb.AppendLine();
        }

        var display = this.FindControl<TextBlock>("MemoryDisplayText");
        if (display != null) display.Text = sb.ToString();
    }

    // ==================== Toolbar Button Handlers ====================

    private async void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        if (NdBootActive) ExitNdBootMode("stopped with STOP, back to OPCOM");
        await _protocol.StopCpuAsync();
    }

    private async void OnMclClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        if (NdBootActive) ExitNdBootMode("master clear, back to OPCOM");
        // TODO: Add confirmation dialog
        await _protocol.MasterClearAsync();
    }

    private async void OnStepClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var stepBox = this.FindControl<TextBox>("StepCountBox")!;
        int count = 1;
        if (int.TryParse(stepBox.Text, out int parsed) && parsed > 0)
            count = parsed;
        await _protocol.SingleStepAsync(count);
    }

    /// <summary>
    /// Starts the CPU, after asking. Ronny's instruction, 6 September 2026: on a real
    /// ND there is no way back from this over the wire - once the machine is running he
    /// has to walk over and reset it by hand. His words: "start starts the machine and
    /// you are unable to reset it, i need to physically reset it". So this is the one
    /// button in the window that asks before it acts, and the address it is about to
    /// start from is in the question, because starting from the wrong address is just as
    /// bad as not meaning to start at all.
    /// </summary>
    private async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var addrBox = this.FindControl<TextBox>("StartAddressBox")!;
        int addr = 0;
        if (!string.IsNullOrWhiteSpace(addrBox.Text))
        {
            if (!OctalHelper.TryParseOctal32(addrBox.Text.AsSpan(), out addr))
                return;
        }

        if (!await ConfirmStartAsync(addr)) return;

        await _protocol.StartAsync(addr);
    }

    /// <summary>
    /// Asks whether the machine should really be started, naming the octal address it
    /// would start from. Returns true only if the Start button in the dialog was pressed.
    /// </summary>
    private async Task<bool> ConfirmStartAsync(int address)
    {
        return await ConfirmRunAsync(address, forBreakpoint: false);
    }

    /// <summary>
    /// Asks before anything that sets the CPU running. Both buttons that do reach this:
    /// Start, and Breakpoint, which despite its name executes until it reaches the
    /// address. Returns true only if the dialog was answered with its go-ahead button.
    /// </summary>
    private async Task<bool> ConfirmRunAsync(int address, bool forBreakpoint)
    {
        var dialog = BuildStartConfirmationDialog(address, out Func<bool> wasConfirmed, forBreakpoint);
        await dialog.ShowDialog(this);
        return wasConfirmed();
    }

    /// <summary>
    /// Builds the start-confirmation dialog and hands back a way to read the answer once
    /// it has closed. Separate from showing it so a headless test can check the wording -
    /// above all that the address really appears in the question, since a confirmation
    /// that does not say what it is about is worse than none.
    /// </summary>
    internal static Window BuildStartConfirmationDialog(int address, out Func<bool> wasConfirmed,
        bool forBreakpoint = false)
    {
        bool confirmed = false;
        wasConfirmed = () => confirmed;
        string octal = OctalHelper.ToOctal6((ushort)address);

        var dialog = new Window
        {
            Title = forBreakpoint ? "Run to a breakpoint?" : "Start the machine?",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        // The theme's window background and the shared dialog button classes. Until
        // 27 September 2026 this window took Fluent's stock background and its two buttons were
        // bare, so it was the one dialog in the app that did not look like the others.
        dialog[!Window.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("WindowBackgroundBrush");

        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Name = "StartConfirmQuestion",
            Text = forBreakpoint
                ? "Are you sure you want to run the machine until it reaches address " + octal + "?"
                : "Are you sure you want to start the machine from address " + octal + "?",
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = forBreakpoint
                ? "Setting a breakpoint RUNS the CPU. It executes until it reaches that address,"
                  + " and if the address is never reached it keeps going until a key is pressed."
                : "The CPU begins executing immediately. On real hardware there is no way to stop"
                  + " it again from here - it has to be reset by hand at the machine.",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancel = new Button { Name = "StartConfirmCancelButton", Content = "Cancel", Classes = { "dialog-secondary" } };
        cancel.Click += (_, _) => { confirmed = false; dialog.Close(); };

        // Danger, not primary: this runs a real CPU that cannot be stopped from here.
        var start = new Button
        {
            Name = "StartConfirmStartButton",
            Content = forBreakpoint ? "Run" : "Start",
            Classes = { "dialog-danger" }
        };
        start.Click += (_, _) => { confirmed = true; dialog.Close(); };

        // Cancel is the safe answer, so it is the one that has focus when the dialog opens
        // and the one the Escape key picks.
        cancel.IsDefault = true;
        cancel.IsCancel = true;

        buttons.Children.Add(cancel);
        buttons.Children.Add(start);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog;
    }

    /// <summary>
    /// Sets a breakpoint, after asking - because on a real ND this RUNS THE MACHINE.
    /// Measured on an ND-120/CX on 6 September 2026: "4." was echoed and then, a second and
    /// a half later, a second full stop came back. The CPU had executed its way round to
    /// address 4 and stopped there. The command reference says as much once read
    /// carefully: if the address is never reached, execution continues until a character
    /// is typed. The name suggests it only arms something; it does not.
    /// </summary>
    private async void OnBreakpointClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var addrBox = this.FindControl<TextBox>("StartAddressBox")!;
        if (string.IsNullOrWhiteSpace(addrBox.Text)) return;
        if (!OctalHelper.TryParseOctal32(addrBox.Text.AsSpan(), out int addr))
            return;

        if (!await ConfirmRunAsync(addr, forBreakpoint: true)) return;

        await _protocol.SetBreakpointAsync(addr);
    }

    private async void OnEscClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        await _protocol.EscapeAsync();
    }

    // ==================== Register Button Handlers ====================

    private async void OnRefreshRegistersClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var levelCombo = this.FindControl<ComboBox>("LevelSelector")!;
        int level = levelCombo.SelectedIndex;

        // Read all 8 working registers for the selected level
        string[] regNames = WorkingRegisterNames.ByNumber;
        for (int i = 0; i < regNames.Length; i++)
        {
            await _protocol.ReadRegisterAsync(level, regNames[i]);
        }
    }

    private async void OnDumpAllLevelsClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        await _protocol.DumpRegistersAsync(0, 15); // 0<17RD
    }

    private async void OnRefreshInternalClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        // Read each internal register using Ixx OPCOM notation
        for (int i = 0; i < InternalRegisterDefs.Count; i++)
        {
            await _protocol.ReadInternalRegisterAsync(InternalRegisterDefs.GetReadCommand(i));
        }
    }

    // ==================== Memory Button Handlers ====================

    private async void OnMemoryExamineClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var addrBox = this.FindControl<TextBox>("MemoryAddressBox")!;
        if (string.IsNullOrWhiteSpace(addrBox.Text)) return;
        if (!OctalHelper.TryParseOctal32(addrBox.Text.AsSpan(), out int addr)) return;

        _memoryViewAddress = addr;
        if (NdBootActive)
        {
            // One monitor READ fills the whole 128-word page the view shows.
            await ReadMemoryViaNdBootAsync(addr, Math.Min(128, 0x10000 - addr));
            return;
        }
        await _protocol.ReadMemoryAsync(addr);
    }

    private async void OnMemoryDumpClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var fromBox = this.FindControl<TextBox>("MemoryDumpFromBox")!;
        var toBox = this.FindControl<TextBox>("MemoryDumpToBox")!;
        if (string.IsNullOrWhiteSpace(fromBox.Text) || string.IsNullOrWhiteSpace(toBox.Text)) return;
        if (!OctalHelper.TryParseOctal32(fromBox.Text.AsSpan(), out int fromAddr)) return;
        if (!OctalHelper.TryParseOctal32(toBox.Text.AsSpan(), out int toAddr)) return;

        _memoryViewAddress = fromAddr;
        if (NdBootActive)
        {
            if (toAddr < fromAddr) return;
            await ReadMemoryViaNdBootAsync(fromAddr, Math.Min(toAddr - fromAddr + 1, 0x10000 - fromAddr));
            return;
        }
        await _protocol.DumpMemoryAsync(fromAddr, toAddr);
    }

    private void OnMemoryPrevClick(object? sender, RoutedEventArgs e)
    {
        _memoryViewAddress = Math.Max(0, _memoryViewAddress - 128);
        var addrBox = this.FindControl<TextBox>("MemoryAddressBox")!;
        addrBox.Text = OctalHelper.ToOctal6((ushort)_memoryViewAddress);
        UpdateMemoryDisplay();
    }

    private void OnMemoryNextClick(object? sender, RoutedEventArgs e)
    {
        _memoryViewAddress = Math.Min(0xFFFF, _memoryViewAddress + 128);
        var addrBox = this.FindControl<TextBox>("MemoryAddressBox")!;
        addrBox.Text = OctalHelper.ToOctal6((ushort)_memoryViewAddress);
        UpdateMemoryDisplay();
    }

    // ==================== File Upload Handlers ====================

    private BpunFileData? _bpunData;
    private Aout16FileData? _aoutData;
    private byte[]? _rawFileBytes; // Raw file bytes for BPUN binary transfer

    private async void OnUploadBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select File to Upload",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ND-100 Binaries") { Patterns = new[] { "*.bpun", "*.aout16", "*.aout" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 0) return;
        var file = files[0];
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var pathBox = this.FindControl<TextBox>("UploadFilePathBox")!;
        pathBox.Text = path;

        // Try to parse the file
        byte[] fileBytes = System.IO.File.ReadAllBytes(path);
        _rawFileBytes = fileBytes;
        _bpunData = null;
        _aoutData = null;

        if (BpunFileParser.IsBpunFile(fileBytes))
        {
            _bpunData = BpunFileParser.Parse(fileBytes);
            if (_bpunData != null)
            {
                ShowFileInfo("BPUN",
                    OctalHelper.ToOctal6(_bpunData.LoadAddress),
                    _bpunData.WordCount.ToString(),
                    OctalHelper.ToOctal6(_bpunData.StartAddress),
                    _bpunData.ChecksumValid ? "OK" : "MISMATCH!");
            }
        }
        else if (Aout16FileParser.IsAout16File(fileBytes))
        {
            _aoutData = Aout16FileParser.Parse(fileBytes);
            if (_aoutData != null)
            {
                ShowFileInfo(_aoutData.MagicDescription,
                    OctalHelper.ToOctal6(_aoutData.LoadAddress),
                    _aoutData.TotalWords.ToString(),
                    OctalHelper.ToOctal6(_aoutData.EntryPoint),
                    "N/A");
            }
        }
        else
        {
            ShowFileInfo("Unknown format", "-", "-", "-", "-");
        }

        var uploadBtn = this.FindControl<Button>("UploadButton")!;
        uploadBtn.IsEnabled = _bpunData != null || _aoutData != null;
    }

    private void ShowFileInfo(string format, string address, string wordCount, string entry, string checksum)
    {
        this.FindControl<TextBlock>("UploadFormatLabel")!.Text = format;
        this.FindControl<TextBlock>("UploadAddressLabel")!.Text = address;
        this.FindControl<TextBlock>("UploadWordCountLabel")!.Text = wordCount;
        this.FindControl<TextBlock>("UploadEntryLabel")!.Text = entry;
        this.FindControl<TextBlock>("UploadChecksumLabel")!.Text = checksum;
    }

    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;

        var methodCombo = this.FindControl<ComboBox>("UploadMethodCombo")!;
        bool useBinaryLoader = methodCombo.SelectedIndex == 1;

        if (useBinaryLoader)
        {
            await DoUploadBpunBinary();
        }
        else if (NdBootActive)
        {
            this.FindControl<TextBlock>("UploadProgressLabel")!.Text =
                "NDBoot monitor owns the console: OPCOM deposits will not work. Use NDBoot fast transfer, or press STOP first.";
        }
        else
        {
            await DoUploadDepositLoop();
        }
    }

    private async Task DoUploadDepositLoop()
    {
        if (_protocol == null) return;

        ushort[]? words = null;
        int startAddress = 0;
        int count = 0;

        if (_bpunData != null)
        {
            words = _bpunData.Words;
            startAddress = _bpunData.LoadAddress;
            count = _bpunData.WordCount;
        }
        else if (_aoutData != null)
        {
            words = _aoutData.Words;
            startAddress = _aoutData.LoadAddress;
            count = _aoutData.TotalWords;
        }

        if (words == null || count == 0) return;

        var uploadBtn = this.FindControl<Button>("UploadButton")!;
        var cancelBtn = this.FindControl<Button>("UploadCancelButton")!;
        uploadBtn.IsEnabled = false;
        cancelBtn.IsEnabled = true;

        _uploadCts = new CancellationTokenSource();
        _uploadStartTime = DateTime.UtcNow;
        _lastUploadUiUpdate = DateTime.UtcNow;

        ResetProgressDisplay($"Uploading 0 / {count} words (deposit)...", startAddress);

        var result = await _protocol.UploadWordsAsync(startAddress, words, count,
            (current, total) =>
            {
                var now = DateTime.UtcNow;
                if (current >= total || (now - _lastUploadUiUpdate).TotalMilliseconds >= 100)
                {
                    _lastUploadUiUpdate = now;
                    Dispatcher.UIThread.Post(() => UpdateTransferProgress(current, total, "words", startAddress + current));
                }
            },
            _uploadCts.Token);

        cancelBtn.IsEnabled = false;
        uploadBtn.IsEnabled = true;

        if (result.Success)
            this.FindControl<TextBlock>("UploadProgressLabel")!.Text = $"Upload complete ({count} words)";
        else
            this.FindControl<TextBlock>("UploadProgressLabel")!.Text = $"Upload failed: {result.ErrorMessage}";
    }

    /// <summary>Words at the start of the monitor image compared against memory to
    /// decide whether the monitor is still resident after a payload ran.</summary>
    private const int NdBootFingerprintWords = 10;

    // ==================== NDBoot mode ====================
    //
    // While the NDBoot monitor is running on the ND it owns the console: OPCOM does not
    // see what we type, so every OPCOM control except STOP and MCL is disabled, the
    // Memory tab reads through the monitor's READ command (128 words per frame instead
    // of one OPCOM examine per word), and the upload tab streams through it. STOP and
    // MCL leave the mode first and then do their OPCOM work.

    private NdBootProtocol? _nd;
    private NdBootMonitorInfo? _ndInfo;
    private bool _ndSavedPassThrough = true;
    private bool NdBootActive => _nd != null;

    private static readonly string[] NdBootDisabledControls =
    {
        "StepButton", "StartButton", "BreakpointButton", "EscButton", "StepCountBox", "StartAddressBox",
        "LevelSelector", "WorkingRegsGrid", "TraRegsGrid", "TrrRegsGrid",
        "RefreshRegsButton", "DumpAllLevelsButton", "RefreshInternalButton",
        "IoBootPanel", "PassThroughCheckBox",
    };

    /// <summary>
    /// Hands the console over to the NDBoot monitor and puts the window into that mode.
    /// </summary>
    /// <param name="nd">
    /// The monitor protocol that now owns the line. Its receiver is wired to the raw stream.
    /// </param>
    /// <param name="info">
    /// What the monitor reported about itself when it answered.
    /// </param>
    /// <param name="savedPassThrough">
    /// The pass-through setting to restore when the mode ends. Callers switch it off before
    /// talking to the monitor, so the value they had is remembered here rather than read back
    /// from a checkbox that has already changed.
    /// </param>
    private void EnterNdBootMode(NdBootProtocol nd, NdBootMonitorInfo info, bool savedPassThrough)
    {
        if (_protocol == null) return;
        if (_nd == null)
            _ndSavedPassThrough = savedPassThrough;
        _nd = nd;
        _ndInfo = info;
        SetPassThrough(false);
        _protocol.RawReceiver = nd.OnDataReceived;
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var name in NdBootDisabledControls)
            {
                var c = this.FindControl<Control>(name);
                if (c != null) c.IsEnabled = false;
            }
            var cpu = this.FindControl<TextBlock>("CpuStateLabel")!;
            cpu.Text = "RUNNING (NDBoot)";
            cpu.Foreground = Avalonia.Media.Brushes.LightGreen;
            this.FindControl<TextBlock>("NdBootStatusLabel")!.Text = "NDBoot monitor: running, " + info.Describe();
            this.FindControl<TextBlock>("ProgressMessageLabel")!.Text =
                "NDBoot mode: memory reads and uploads go through the monitor; OPCOM controls disabled until STOP or MCL";
        });
    }

    private void ExitNdBootMode(string reason)
    {
        if (_protocol == null) return;
        bool wasActive = _nd != null;
        _nd = null;
        _ndInfo = null;
        _protocol.RawReceiver = null;
        if (wasActive)
            SetPassThrough(_ndSavedPassThrough);
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var name in NdBootDisabledControls)
            {
                var c = this.FindControl<Control>(name);
                if (c != null) c.IsEnabled = true;
            }
            this.FindControl<CheckBox>("PassThroughCheckBox")!.IsChecked = _protocol.PassThrough;
            if (wasActive)
            {
                this.FindControl<TextBlock>("NdBootStatusLabel")!.Text = "NDBoot monitor: " + reason;
                this.FindControl<TextBlock>("ProgressMessageLabel")!.Text = "NDBoot mode left: " + reason;
                OnCpuStateChanged(_protocol.CpuState);
            }
        });
    }

    /// <summary>Reads count words at address through the monitor into the OPCOM memory
    /// cache (128 words per READ frame), so the Memory tab shows them as usual.</summary>
    private async Task<bool> ReadMemoryViaNdBootAsync(int address, int count)
    {
        var nd = _nd;
        if (_protocol == null || nd == null) return false;
        var status = this.FindControl<TextBlock>("ProgressMessageLabel")!;
        var t0 = DateTime.UtcNow;
        try
        {
            int done = 0;
            while (done < count)
            {
                int n = Math.Min(NdBootProtocol.MaxBlockWords, count - done);
                var words = await nd.ReadAsync(address + done, n);
                _protocol.Memory.SetRange(address + done, words, n);
                done += n;
                status.Text = $"NDBoot READ {done}/{count} words from {OctalHelper.ToOctal6((ushort)address)}";
            }
            status.Text = $"NDBoot READ {count} words from {OctalHelper.ToOctal6((ushort)address)} in {(DateTime.UtcNow - t0).TotalMilliseconds:F0} ms";
            return true;
        }
        catch (Exception ex)
        {
            status.Text = "NDBoot READ failed: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Makes sure the NDBoot monitor is running and answers PING, with as little work as
    /// possible:
    ///  1. PING (1.5 s). Answer: done.
    ///  2. No answer: dump the first NdBootFingerprintWords at NdBootImage.LoadAddress
    ///     through OPCOM. If they equal the embedded image, the monitor is still in
    ///     memory (a payload ran and the CPU was stopped): just 170000! and PING again.
    ///  3. Otherwise deposit the embedded image through the OPCOM deposit loop, then
    ///     170000! and PING.
    /// Caller owns RawReceiver/PassThrough; this method leaves RawReceiver pointing at nd.
    /// </summary>
    private async Task<NdBootMonitorInfo> EnsureMonitorAsync(NdBootProtocol nd, Action<string> status, CancellationToken ct)
    {
        if (_protocol == null) throw new InvalidOperationException("OPCOM not active");

        status("NDBoot: PING...");
        _protocol.RawReceiver = nd.OnDataReceived;
        try
        {
            var live = await nd.PingAsync(ct, timeoutMs: 1500);
            status("NDBoot: monitor is running, " + live.Describe());
            return live;
        }
        catch (TimeoutException)
        {
        }

        _protocol.RawReceiver = null;
        var image = BpunFileParser.Parse(NdBootImage.Bpun)
            ?? throw new InvalidOperationException("embedded NDBoot image does not parse");
        string loadOct = OctalHelper.ToOctal6((ushort)image.LoadAddress);

        status($"NDBoot: no answer. Reading {NdBootFingerprintWords} words at {loadOct} to see whether the monitor is still in memory...");
        bool resident = false;
        var dump = await _protocol.DumpMemoryAsync(image.LoadAddress, image.LoadAddress + NdBootFingerprintWords - 1, ct);
        if (dump.Success && dump.DumpValues != null && dump.DumpAddresses != null)
        {
            int matched = 0;
            for (int i = 0; i < dump.DumpValues.Length; i++)
            {
                int idx = dump.DumpAddresses[i] - image.LoadAddress;
                if (idx >= 0 && idx < NdBootFingerprintWords && image.Words[idx] == dump.DumpValues[i])
                    matched++;
            }
            resident = matched == NdBootFingerprintWords;
        }

        if (resident)
        {
            status($"NDBoot: monitor still in memory at {loadOct}, restarting it with {OctalHelper.ToOctalTrimmed(image.LoadAddress)}!...");
        }
        else
        {
            status($"NDBoot: monitor not in memory. Depositing it at {loadOct} through OPCOM " +
                   $"({image.WordCount} words, about {image.WordCount / 4} s)...");
            _uploadStartTime = DateTime.UtcNow;
            _lastUploadUiUpdate = _uploadStartTime;
            var dep = await _protocol.UploadWordsAsync(image.LoadAddress, image.Words, image.WordCount,
                (cur, tot) =>
                {
                    var now = DateTime.UtcNow;
                    if (cur >= tot || (now - _lastUploadUiUpdate).TotalMilliseconds >= 100)
                    {
                        _lastUploadUiUpdate = now;
                        Dispatcher.UIThread.Post(() => UpdateTransferProgress(cur, tot, "monitor words", image.LoadAddress + cur));
                    }
                }, ct);
            if (!dep.Success)
                throw new InvalidOperationException("depositing the monitor failed: " + dep.ErrorMessage);
            status($"NDBoot: deposited, starting it with {OctalHelper.ToOctalTrimmed(image.LoadAddress)}!...");
        }

        var start = await _protocol.StartAsync(image.LoadAddress, ct);
        if (!start.Success)
            throw new InvalidOperationException("starting the monitor failed: " + start.ErrorMessage);

        _protocol.RawReceiver = nd.OnDataReceived;
        bool banner = await nd.WaitForTextAsync("enabled!", 3000, ct);
        status(banner ? "NDBoot: banner seen, PING..." : "NDBoot: no banner seen, PING anyway...");
        var info = await nd.PingAsync(ct, timeoutMs: 2000);
        status("NDBoot: monitor started, " + info.Describe());
        return info;
    }

    /// <summary>"Check / install NDBoot monitor": EnsureMonitorAsync with no file involved.</summary>
    private async void OnNdBootCheckClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var btn = this.FindControl<Button>("NdBootCheckButton")!;
        var statusLabel = this.FindControl<TextBlock>("NdBootStatusLabel")!;
        var uploadBtn = this.FindControl<Button>("UploadButton")!;
        bool uploadWasEnabled = uploadBtn.IsEnabled;
        btn.IsEnabled = false;
        uploadBtn.IsEnabled = false;

        bool wasPassThrough = _protocol.PassThrough;
        SetPassThrough(false);
        var nd = _nd ?? new NdBootProtocol(_protocol.SendRawAsync);
        _uploadCts = new CancellationTokenSource();
        _lastUploadUiUpdate = DateTime.UtcNow;
        void Status(string text) => Dispatcher.UIThread.Post(() => statusLabel.Text = text);
        bool entered = false;
        try
        {
            var info = await EnsureMonitorAsync(nd, Status, _uploadCts.Token);
            if (info.HostSupports)
            {
                EnterNdBootMode(nd, info, wasPassThrough);
                entered = true;
            }
            else
            {
                Status("NDBoot monitor: " + info.Describe());
            }
        }
        catch (OperationCanceledException)
        {
            Status("NDBoot monitor: check cancelled");
        }
        catch (Exception ex)
        {
            Status("NDBoot monitor: " + ex.Message);
        }
        finally
        {
            if (!entered)
            {
                if (NdBootActive) ExitNdBootMode("monitor stopped answering");
                _protocol.RawReceiver = null;
                SetPassThrough(wasPassThrough);
            }
            btn.IsEnabled = true;
            uploadBtn.IsEnabled = uploadWasEnabled;
        }
    }

    /// <summary>
    /// Fast upload through the NDBoot monitor (NDBoot repo, docs/PROTOCOL.md). The line
    /// stays at 7E1. Steps, each reported in the progress label:
    ///  1. PING. If the monitor answers it is already running on the ND.
    ///  2. Otherwise deposit NdBootImage at 170000 through the OPCOM deposit loop,
    ///     start it with 170000!, wait for its banner, PING again.
    ///  3. Stream the selected file's words in 128-word CRC-checked blocks (go-back-N).
    ///  4. GO to the file's boot address.
    /// </summary>
    private async Task DoUploadBpunBinary()
    {
        if (_protocol == null) return;

        ushort[]? words = null;
        int loadAddress = 0, count = 0, bootAddress = 0;
        if (_bpunData != null)
        {
            words = _bpunData.Words;
            loadAddress = _bpunData.LoadAddress;
            count = _bpunData.WordCount;
            bootAddress = _bpunData.BootAddress;
        }
        else if (_aoutData != null)
        {
            words = _aoutData.Words;
            loadAddress = _aoutData.LoadAddress;
            count = _aoutData.TotalWords;
            bootAddress = _aoutData.EntryPoint;
        }
        if (words == null || count == 0) return;

        var uploadBtn = this.FindControl<Button>("UploadButton")!;
        var cancelBtn = this.FindControl<Button>("UploadCancelButton")!;
        var label = this.FindControl<TextBlock>("UploadProgressLabel")!;
        uploadBtn.IsEnabled = false;
        cancelBtn.IsEnabled = true;

        _uploadCts = new CancellationTokenSource();
        var ct = _uploadCts.Token;
        _uploadStartTime = DateTime.UtcNow;
        _lastUploadUiUpdate = DateTime.UtcNow;
        ResetProgressDisplay("NDBoot: checking whether the monitor is running (PING)...", loadAddress);

        bool wasPassThrough = _protocol.PassThrough;
        SetPassThrough(false);
        var nd = _nd ?? new NdBootProtocol(_protocol.SendRawAsync);

        void Status(string text) => Dispatcher.UIThread.Post(() => label.Text = text);

        try
        {
            var info = await EnsureMonitorAsync(nd, Status, ct);
            if (!info.HostSupports)
                throw new InvalidOperationException(NdBootVersions.SupportMessage(info.Version));
            EnterNdBootMode(nd, info, wasPassThrough);
            if (loadAddress < info.Top && loadAddress + count > info.Base)
                throw new InvalidOperationException(
                    $"file {OctalHelper.ToOctal6((ushort)loadAddress)}..{OctalHelper.ToOctal6((ushort)(loadAddress + count - 1))} " +
                    $"overlaps the monitor at {OctalHelper.ToOctal6((ushort)info.Base)}..{OctalHelper.ToOctal6((ushort)(info.Top - 1))}");

            int blocks = (count + NdBootProtocol.MaxBlockWords - 1) / NdBootProtocol.MaxBlockWords;
            Status($"NDBoot: monitor v{info.Version} at {OctalHelper.ToOctal6((ushort)info.Base)} answered. " +
                   $"Streaming {count} words in {blocks} blocks to {OctalHelper.ToOctal6((ushort)loadAddress)}...");
            _uploadStartTime = DateTime.UtcNow;
            int frames = await nd.SendImageAsync(loadAddress, words, count, 4,
                (cur, tot) =>
                {
                    var now = DateTime.UtcNow;
                    if (cur >= tot || (now - _lastUploadUiUpdate).TotalMilliseconds >= 100)
                    {
                        _lastUploadUiUpdate = now;
                        Dispatcher.UIThread.Post(() => UpdateTransferProgress(cur, tot, "words", loadAddress + cur));
                    }
                }, ct);
            double seconds = (DateTime.UtcNow - _uploadStartTime).TotalSeconds;

            Status($"NDBoot: {count} words in {seconds:F1} s ({count / Math.Max(seconds, 0.001):F0} words/s, " +
                   $"{frames - blocks} resends). Starting at {OctalHelper.ToOctal6((ushort)bootAddress)}...");
            await nd.GoAsync(bootAddress, ct);
            ExitNdBootMode($"payload started at {OctalHelper.ToOctal6((ushort)bootAddress)}; monitor stays in memory until overwritten");
            Status($"NDBoot: transfer complete, {count} words in {seconds:F1} s, program started at {OctalHelper.ToOctal6((ushort)bootAddress)}");
        }
        catch (OperationCanceledException)
        {
            Status("NDBoot: cancelled");
        }
        catch (Exception ex)
        {
            Status("NDBoot: failed: " + ex.Message);
        }
        finally
        {
            if (!NdBootActive)
            {
                _protocol.RawReceiver = null;
                SetPassThrough(wasPassThrough);
            }
            cancelBtn.IsEnabled = false;
            uploadBtn.IsEnabled = true;
        }
    }

    private void ResetProgressDisplay(string label, int startAddress)
    {
        this.FindControl<ProgressBar>("UploadProgressBar")!.Value = 0;
        this.FindControl<TextBlock>("UploadProgressLabel")!.Text = label;
        this.FindControl<TextBlock>("UploadCurrentAddrLabel")!.Text =
            startAddress > 0 ? OctalHelper.ToOctal6((ushort)startAddress) : "-";
        this.FindControl<TextBlock>("UploadTimeLabel")!.Text = "-";
    }

    private void UpdateTransferProgress(int current, int total, string unit, int currentAddress)
    {
        if (total <= 0) return;
        double pct = (double)current / total * 100.0;
        this.FindControl<ProgressBar>("UploadProgressBar")!.Value = pct;
        this.FindControl<TextBlock>("UploadProgressLabel")!.Text = $"{current} / {total} {unit} ({pct:F1}%)";
        this.FindControl<TextBlock>("UploadCurrentAddrLabel")!.Text =
            currentAddress >= 0 ? OctalHelper.ToOctal6((ushort)currentAddress) : "-";

        var elapsed = DateTime.UtcNow - _uploadStartTime;
        if (current > 0 && elapsed.TotalSeconds > 0)
        {
            double rate = current / elapsed.TotalSeconds;
            int remaining = total - current;
            double estSeconds = remaining / rate;
            this.FindControl<TextBlock>("UploadTimeLabel")!.Text = $"{estSeconds:F0}s (~{rate:F0} {unit}/s)";
        }
    }

    private void OnUploadCancelClick(object? sender, RoutedEventArgs e)
    {
        _uploadCts?.Cancel();
    }

    // ==================== I/O & Boot Handlers ====================

    private async void OnIoxReadClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var devBox = this.FindControl<TextBox>("IoxDeviceBox")!;
        if (string.IsNullOrWhiteSpace(devBox.Text)) return;
        if (!OctalHelper.TryParseOctal32(devBox.Text.AsSpan(), out int devAddr)) return;

        var result = await _protocol.IOXReadAsync(devAddr);
        var resultLabel = this.FindControl<TextBlock>("IoxResultLabel")!;
        resultLabel.Text = result.Success ? OctalHelper.ToOctal6(result.Value) : "ERROR";
    }

    private async void OnIoxWriteClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        var devBox = this.FindControl<TextBox>("IoxDeviceBox")!;
        var oprBox = this.FindControl<TextBox>("IoxOprBox")!;
        if (string.IsNullOrWhiteSpace(devBox.Text)) return;
        if (!OctalHelper.TryParseOctal32(devBox.Text.AsSpan(), out int devAddr)) return;
        ushort oprVal = 0;
        if (!string.IsNullOrWhiteSpace(oprBox.Text))
            OctalHelper.TryParseOctal(oprBox.Text.AsSpan(), out oprVal);

        var result = await _protocol.IOXWriteAsync(devAddr, oprVal);
        var resultLabel = this.FindControl<TextBlock>("IoxResultLabel")!;
        resultLabel.Text = result.Success ? "OK" : "ERROR";
    }

    private async void OnBootClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        string? deviceAddr = GetSelectedBootDevice();
        if (deviceAddr == null) return;

        await _protocol.BootLoadAsync(deviceAddr);
    }

    private async void OnMclBootClick(object? sender, RoutedEventArgs e)
    {
        if (_protocol == null) return;
        string? deviceAddr = GetSelectedBootDevice();
        if (deviceAddr == null) return;

        // MCL first, wait for ## completion, then boot
        await _protocol.MasterClearAsync();
        await _protocol.BootLoadAsync(deviceAddr);
    }

    private string? GetSelectedBootDevice()
    {
        var bootCombo = this.FindControl<ComboBox>("BootDeviceCombo")!;
        int idx = bootCombo.SelectedIndex;
        if (idx < 0) return null;

        if (idx < OpcomAldDecoder.BootPresets.Length)
        {
            return OpcomAldDecoder.BootPresets[idx].OpcomCommand;
        }
        // Custom - would need a text box, for now return null
        return null;
    }

    // ==================== Log Handlers ====================

    private void OnLogClearClick(object? sender, RoutedEventArgs e)
    {
        lock (_logLock)
        {
            _logBuffer.Clear();
            _logLineCount = 0;
            _logDirty = false;
        }
        var display = this.FindControl<TextBlock>("LogDisplayText");
        if (display != null) display.Text = "";
    }

    private async void OnLogCopyClick(object? sender, RoutedEventArgs e)
    {
        string text;
        lock (_logLock) text = _logBuffer.ToString();

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
