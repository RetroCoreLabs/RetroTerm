using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands;

/// <summary>
/// Holds every registered <see cref="ISessionCommand"/> and dispatches calls by name.
/// The script DSL, the MCP tool layer and UI actions all resolve through here —
/// registering a command in one line makes it available everywhere at once.
///
/// Registration VALIDATES metadata: a command with an empty summary, a missing
/// example or an undocumented parameter fails to register. A new command brings its
/// own docs or does not exist — that is what keeps the generated help trustworthy.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ISessionCommand> _byName =
        new Dictionary<string, ISessionCommand>(StringComparer.OrdinalIgnoreCase);

    // Registration order preserved for help listings (no LINQ ordering later).
    private readonly List<ISessionCommand> _ordered = new List<ISessionCommand>();

    /// <summary>
    /// All registered commands in registration order.
    /// </summary>
    public IReadOnlyList<ISessionCommand> Commands => _ordered;

    /// <summary>
    /// Registers a command after validating its metadata. Throws ArgumentException on
    /// missing docs or a duplicate name — loudly, at startup, not quietly at call time.
    /// </summary>
    public void Register(ISessionCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Command has an empty Name", nameof(command));
        if (command.Name.IndexOf(' ') >= 0)
            throw new ArgumentException($"Command name '{command.Name}' must not contain spaces", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Summary))
            throw new ArgumentException($"Command '{command.Name}' has an empty Summary — help is generated, docs are mandatory", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Example))
            throw new ArgumentException($"Command '{command.Name}' has an empty Example — help is generated, docs are mandatory", nameof(command));

        var parameters = command.Parameters;
        if (parameters == null)
            throw new ArgumentException($"Command '{command.Name}' has null Parameters (use an empty list)", nameof(command));
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (string.IsNullOrWhiteSpace(p.Name))
                throw new ArgumentException($"Command '{command.Name}' parameter {i} has an empty name", nameof(command));
            if (string.IsNullOrWhiteSpace(p.Description))
                throw new ArgumentException($"Command '{command.Name}' parameter '{p.Name}' has no description — help is generated, docs are mandatory", nameof(command));
        }

        if (_byName.ContainsKey(command.Name))
            throw new ArgumentException($"Command '{command.Name}' is already registered", nameof(command));

        _byName.Add(command.Name, command);
        _ordered.Add(command);
    }

    /// <summary>
    /// Looks a command up by name (case-insensitive).
    /// </summary>
    public bool TryGet(string name, out ISessionCommand command)
    {
        return _byName.TryGetValue(name, out command!);
    }

    /// <summary>
    /// Dispatches a call by name. Unknown names and missing required parameters come
    /// back as failed results (with the known-command list / the missing name spelled
    /// out), not exceptions — script and MCP callers want reportable errors.
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(string name, TerminalSession session,
        CommandArgs args, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        args ??= CommandArgs.Empty;

        if (!_byName.TryGetValue(name, out var command))
        {
            return CommandResult.Fail($"Unknown command '{name}'. Known commands: {GetCommandNameList()}");
        }

        // Validate up front so the error names the parameter instead of the command
        // failing halfway through — missing required values AND values of the wrong type.
        //
        // The type check lives here, not at a call site, because dispatch is where every
        // surface converges. The script parser also checks at parse time (so the editor
        // can underline the line before anything runs); the MCP provider had no check at
        // all, so an Int parameter given "300/ontimeout=wake" or "soon" reached GetInt,
        // silently became the default, and the caller was never told.
        if (!CommandParameterValidation.TryValidate(command, args, out var validationError))
        {
            return CommandResult.Fail(validationError!);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await command.ExecuteAsync(session, args, cancellationToken).ConfigureAwait(false);
            if (result.Elapsed == TimeSpan.Zero)
            {
                result.Elapsed = stopwatch.Elapsed;
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw; // deliberate cancellation belongs to the caller
        }
        catch (Exception ex)
        {
            var failed = CommandResult.Fail($"Command '{command.Name}' threw: {ex.Message}");
            failed.Elapsed = stopwatch.Elapsed;
            return failed;
        }
    }

    private string GetCommandNameList()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(_ordered[i].Name);
        }
        return sb.ToString();
    }
}
