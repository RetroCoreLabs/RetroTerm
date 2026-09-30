using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;

namespace RetroTerm.Desktop.Views;

/// <summary>
/// Interactive script console (View → Script Console): type one DSL command per line
/// and it runs against the ACTIVE TAB's session — this is where HELP prints for a
/// human, and where you poke at a machine one command at a time when a script stops.
///
/// Console-only verbs (not part of the script language):
///   RUN name   — load a stored script from the library and run it, transcript here
///   SCRIPTS    — list the stored scripts
///   CLS        — clear the console (Ctrl+L does the same)
///
/// Up/Down arrows walk the command history; the history survives closing and
/// reopening the console (kept for the app's lifetime, not persisted to disk).
/// The window title shows which tab the commands will hit.
///
/// All colors come from the live theme resources (GetResourceObservable) so a theme
/// switch restyles the open window — same pattern as ScriptEditorWindow.BindTheme.
/// </summary>
public sealed class ScriptConsoleWindow : Window
{
    private readonly SessionTargetSelector _target;
    private readonly CommandRegistry _registry;
    private readonly ScriptLibrary _library;
    private readonly ScriptParser _parser;
    private readonly ScriptRunner _runner;

    private readonly TextBox _output;
    private readonly TextBox _input;

    // History and variables are STATIC so they survive closing the console window and
    // opening it again mid-session — losing your SET variables because you closed a
    // window is infuriating. App restart clears both (deliberate: no disk state).
    private static readonly List<string> SharedHistory = new();
    private static readonly ScriptContext SharedContext = new();

    private int _historyIndex;
    private bool _busy;

    public ScriptConsoleWindow(Func<TerminalSession?> activeSession,
        Func<IReadOnlyList<TerminalSession>> openSessions,
        CommandRegistry registry, ScriptLibrary library)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        // "Run on" picker: follow the active tab (default) or pin a specific one.
        _target = new SessionTargetSelector(activeSession, openSessions);
        _target.TargetChanged += UpdateTitle;
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _parser = new ScriptParser(registry);
        _runner = new ScriptRunner(registry);
        _historyIndex = SharedHistory.Count;

        Width = 820;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        BindTheme(this, BackgroundProperty, "WindowBackgroundBrush");
        UpdateTitle();

        _output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            FontSize = 12
        };
        BindTheme(_output, BackgroundProperty, "SectionBackgroundBrush");
        BindTheme(_output, ForegroundProperty, "PrimaryTextBrush");
        // No color flip when the output area is clicked/focused.
        TextBoxChrome.Freeze(_output, this, "SectionBackgroundBrush");

        _input = new TextBox
        {
            PlaceholderText = "type a command — HELP lists them, RUN <name> runs a stored script",
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        BindTheme(_input, BackgroundProperty, "SectionBackgroundBrush");
        BindTheme(_input, ForegroundProperty, "PrimaryTextBrush");
        TextBoxChrome.Freeze(_input, this, "SectionBackgroundBrush");
        _input.KeyDown += OnInputKeyDown;

        // Top strip: which connection the commands hit.
        var targetLabel = new TextBlock
        {
            Text = "Run on:",
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        BindTheme(targetLabel, TextBlock.ForegroundProperty, "SecondaryTextBrush");
        var targetStrip = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(targetLabel, Dock.Left);
        targetStrip.Children.Add(targetLabel);
        targetStrip.Children.Add(_target);

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(targetStrip, Dock.Top);
        DockPanel.SetDock(_input, Dock.Bottom);
        panel.Children.Add(targetStrip);
        panel.Children.Add(_input);
        panel.Children.Add(_output);
        Content = panel;

        Append("RetroTerm script console. HELP for commands, RUN <name> for stored scripts, CLS (or Ctrl+L) to clear.");
        Opened += (_, _) => _input.Focus();
        // The active tab can change while the console is open — refresh the title
        // whenever the user comes back to this window.
        Activated += (_, _) => UpdateTitle();
    }

    /// <summary>
    /// Title names the target tab so you know WHERE the commands land.
    /// </summary>
    private void UpdateTitle()
    {
        Title = $"Script Console — {_target.DescribeTarget()}";
    }

    private void Append(string text)
    {
        _output.Text += text + "\n";
        _output.CaretIndex = _output.Text?.Length ?? 0;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+L = clear, the muscle memory every terminal user already has.
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _output.Text = string.Empty;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && !_busy)
        {
            var line = _input.Text?.Trim() ?? string.Empty;
            _input.Text = string.Empty;
            if (line.Length > 0)
            {
                SharedHistory.Add(line);
                _historyIndex = SharedHistory.Count;
                _ = ExecuteLineAsync(line);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up && SharedHistory.Count > 0)
        {
            _historyIndex = Math.Max(0, _historyIndex - 1);
            _input.Text = SharedHistory[_historyIndex];
            _input.CaretIndex = _input.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SharedHistory.Count > 0)
        {
            _historyIndex = Math.Min(SharedHistory.Count, _historyIndex + 1);
            _input.Text = _historyIndex >= SharedHistory.Count ? string.Empty : SharedHistory[_historyIndex];
            _input.CaretIndex = _input.Text?.Length ?? 0;
            e.Handled = true;
        }
    }

    private async Task ExecuteLineAsync(string line)
    {
        UpdateTitle();
        Append("> " + line);
        _busy = true;
        _input.IsEnabled = false;
        try
        {
            // Console-only verbs first.
            if (string.Equals(line, "CLS", StringComparison.OrdinalIgnoreCase))
            {
                _output.Text = string.Empty;
                return;
            }
            if (string.Equals(line, "SCRIPTS", StringComparison.OrdinalIgnoreCase))
            {
                var names = _library.List();
                Append(names.Count == 0
                    ? $"no stored scripts ({_library.Directory})"
                    : string.Join("\n", names));
                return;
            }
            if (string.Equals(line, "VARS", StringComparison.OrdinalIgnoreCase))
            {
                if (SharedContext.Names.Count == 0)
                {
                    Append("no variables set — SET name \"value\"");
                    return;
                }
                var sb = new System.Text.StringBuilder();
                var e = SharedContext.Names.GetEnumerator();
                while (e.MoveNext())
                {
                    sb.Append(e.Current).Append(" = ").Append(SharedContext.Get(e.Current)).Append('\n');
                }
                Append(sb.ToString().TrimEnd('\n'));
                return;
            }
            if (line.StartsWith("RUN ", StringComparison.OrdinalIgnoreCase))
            {
                await RunStoredScriptAsync(line.Substring(4).Trim());
                return;
            }

            var session = _target.ResolveSession();
            if (session == null)
            {
                Append("no active tab");
                return;
            }

            // One DSL line = one step. The parser validates it exactly like a script line.
            var parsed = _parser.Parse(line);
            if (!parsed.IsValid)
            {
                Append(parsed.Errors[0].Message);
                return;
            }
            if (parsed.Steps.Count == 0)
            {
                return; // comment / blank
            }
            var step = parsed.Steps[0];
            if (step.Kind == ScriptStepKind.Set)
            {
                // SET persists in the console context across commands (and window reopens).
                SharedContext.Set(SharedContext.Expand(step.SetName!), SharedContext.Expand(step.SetValue!));
                Append($"{step.SetName} = {SharedContext.Get(step.SetName!)}");
                return;
            }
            if (step.Kind != ScriptStepKind.Command)
            {
                Append("control flow (LABEL/GOTO/IF/...) only works inside a script — use RUN <name>");
                return;
            }

            // Interactive commands see the console's variables too.
            var result = await _registry.ExecuteAsync(step.CommandName, session, step.Args.ExpandWith(SharedContext.Expand));
            if (result.Success && step.IntoVariable != null)
            {
                SharedContext.Set(step.IntoVariable, result.CaptureValue ?? result.Output ?? string.Empty);
            }
            if (result.Error != null) Append(result.Error);
            if (!string.IsNullOrEmpty(result.Output)) Append(result.Output!);
            Append($"[{(result.Success ? "ok" : "FAILED")} {(long)result.Elapsed.TotalMilliseconds} ms]");
        }
        catch (Exception ex)
        {
            Append("error: " + ex.Message);
        }
        finally
        {
            _busy = false;
            _input.IsEnabled = true;
            _input.Focus();
        }
    }

    private async Task RunStoredScriptAsync(string name)
    {
        if (name.Length == 0)
        {
            Append("RUN needs a script name — SCRIPTS lists them");
            return;
        }
        if (!_library.Exists(name))
        {
            Append($"no script named '{name}' — SCRIPTS lists them");
            return;
        }

        var session = _target.ResolveSession();
        if (session == null)
        {
            Append("no active tab");
            return;
        }

        var parsed = _parser.Parse(_library.Load(name));
        if (!parsed.IsValid)
        {
            Append($"script has {parsed.Errors.Count} error(s):");
            for (int i = 0; i < parsed.Errors.Count; i++)
            {
                Append("  " + parsed.Errors[i]);
            }
            return;
        }

        // Each step prints as it completes, like watching the script type. Steps whose
        // purpose IS output (READSCREEN, READNEW, STATUS, HELP) print their output too.
        var progress = new Progress<ScriptStepResult>(stepResult =>
        {
            Append($"  {(stepResult.Result.Success ? "ok  " : "FAIL")} line {stepResult.Step.LineNumber} " +
                   $"[{(long)stepResult.Result.Elapsed.TotalMilliseconds} ms] {stepResult.Step.SourceText}");
            if (stepResult.ShowOutput && !string.IsNullOrEmpty(stepResult.Result.Output))
            {
                Append(stepResult.Result.Output!);
            }
        });

        Append($"running '{name}' ({parsed.Steps.Count} steps)...");
        // The console's variables flow into the script and survive it — a script can
        // SET something the user then inspects with VARS.
        var result = await _runner.RunAsync(parsed, session, progress: progress, context: SharedContext);

        if (result.Success)
        {
            Append($"script OK — {(long)result.Elapsed.TotalMilliseconds} ms total");
        }
        else
        {
            Append($"script FAILED: {result.Error}");
            if (result.Transcript.Count > 0)
            {
                var last = result.Transcript[result.Transcript.Count - 1].Result;
                if (!string.IsNullOrEmpty(last.Output))
                {
                    Append("screen at failure:");
                    Append(last.Output!);
                }
            }
            Append("the script STOPPED at the failing step — the connection is untouched and stays open; try single commands here");
        }
    }

    /// <summary>
    /// Live theme binding — restyles on theme switch (same as ScriptEditorWindow).
    /// </summary>
    private void BindTheme(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        target.Bind(property, this.GetResourceObservable(key));
    }
}
