using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Session;
using RetroTerm.Core.Transfer;

namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Kermit file transfer as registry commands — SENDFILE / RECEIVEFILE become script
/// verbs and MCP tools like everything else. Options come from a provider so the
/// desktop host can plug in the user's Kermit preferences; the defaults match
/// KermitOptions defaults.
/// </summary>
public static class KermitCommands
{
    /// <summary>
    /// Registers SENDFILE and RECEIVEFILE.
    /// </summary>
    public static void RegisterAll(CommandRegistry registry,
        Func<KermitOptions>? optionsProvider = null,
        Func<FileCollisionMode>? collisionProvider = null)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new SendFileCommand(optionsProvider, collisionProvider));
        registry.Register(new ReceiveFileCommand(optionsProvider, collisionProvider));
    }

    /// <summary>
    /// Runs one Kermit transfer to completion and renders the outcome. Shared by
    /// both commands — the direction and paths are the only difference.
    /// </summary>
    internal static async Task<CommandResult> TransferAsync(TerminalSession session,
        KermitOptions options, FileCollisionMode collision,
        TransferDirection direction, string[] paths, CancellationToken cancellationToken)
    {
        var handler = new KermitFileTransfer(options) { FileCollision = collision };

        // The last completion report carries the final state and error text.
        TransferProgress final = default;
        handler.TransferCompleted += p => final = p;

        // StartFileTransferAsync returns when the whole transfer is over (the handler
        // awaits its own completion). The remote side must already be in server /
        // receive mode — sequence that with SEND/WAITFOR before this step.
        await session.StartFileTransferAsync(handler, direction, paths, cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder(128);
        sb.Append(direction == TransferDirection.Send ? "sent " : "received ")
          .Append(final.FileNumber).Append(" file(s), ")
          .Append(final.BytesTransferred).Append(" bytes, state ").Append(final.State);

        if (final.State == TransferState.Completed)
        {
            return CommandResult.Ok(sb.ToString());
        }
        return CommandResult.Fail(final.ErrorMessage ?? $"transfer ended in state {final.State}", sb.ToString());
    }
}

/// <summary>
/// SENDFILE — send local file(s) to the host via Kermit.
/// </summary>
public sealed class SendFileCommand : ISessionCommand
{
    private readonly Func<KermitOptions> _options;
    private readonly Func<FileCollisionMode> _collision;

    public SendFileCommand(Func<KermitOptions>? optionsProvider = null,
        Func<FileCollisionMode>? collisionProvider = null)
    {
        _options = optionsProvider ?? (static () => new KermitOptions());
        _collision = collisionProvider ?? (static () => FileCollisionMode.Rename);
    }

    public string Name => "SENDFILE";
    public string Summary => "Send local file(s) to the host with Kermit (start the remote Kermit receive first)";
    public string Example => "SENDFILE \"C:\\\\files\\\\data.bin\""; // \\ in the DSL — \ starts an escape
    public bool ProducesOutput => true; // the transfer summary belongs in the transcript

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("path", CommandParameterType.String, required: true, defaultValue: null,
            "Full path of the file to send; several files separated by ';'")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var spec = args.GetString("path")!;
        var paths = spec.Split(';');
        for (int i = 0; i < paths.Length; i++)
        {
            paths[i] = paths[i].Trim();
            if (paths[i].Length == 0 || !File.Exists(paths[i]))
            {
                return Task.FromResult(CommandResult.Fail($"file not found: {paths[i]}"));
            }
        }

        return KermitCommands.TransferAsync(session, _options(), _collision(),
            TransferDirection.Send, paths, cancellationToken);
    }
}

/// <summary>
/// RECEIVEFILE — receive file(s) from the host via Kermit into a directory.
/// </summary>
public sealed class ReceiveFileCommand : ISessionCommand
{
    private readonly Func<KermitOptions> _options;
    private readonly Func<FileCollisionMode> _collision;

    public ReceiveFileCommand(Func<KermitOptions>? optionsProvider = null,
        Func<FileCollisionMode>? collisionProvider = null)
    {
        _options = optionsProvider ?? (static () => new KermitOptions());
        _collision = collisionProvider ?? (static () => FileCollisionMode.Rename);
    }

    public string Name => "RECEIVEFILE";
    public string Summary => "Receive file(s) from the host with Kermit into a directory (start the remote Kermit send first)";
    public string Example => "RECEIVEFILE \"C:\\\\incoming\""; // \\ in the DSL — \ starts an escape
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("dir", CommandParameterType.String, required: true, defaultValue: null,
            "Existing local directory to save received files into")
    };

    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        var dir = args.GetString("dir")!;
        if (!Directory.Exists(dir))
        {
            return Task.FromResult(CommandResult.Fail($"directory not found: {dir}"));
        }

        return KermitCommands.TransferAsync(session, _options(), _collision(),
            TransferDirection.Receive, new[] { dir }, cancellationToken);
    }
}
