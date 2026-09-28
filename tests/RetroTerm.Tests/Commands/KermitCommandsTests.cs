using System;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Protocols.Kermit;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// Validation-level tests for SENDFILE/RECEIVEFILE (registration, help, parameter
/// checks). A full Kermit transfer needs a Kermit peer — that lives in the existing
/// Kermit protocol tests; here we prove the command surface behaves.
/// </summary>
public class KermitCommandsTests : IDisposable
{
    private readonly CommandRegistry _registry;
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;

    public KermitCommandsTests()
    {
        _registry = new CommandRegistry();
        KermitCommands.RegisterAll(_registry);
        _session = new TerminalSession(new VT100Emulator(80, 24), "KermitCmdTest");
        _connection = new InMemoryConnection();
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    [Fact]
    public void Register_BothCommands_WithGeneratedHelp()
    {
        Assert.True(_registry.TryGet("SENDFILE", out _));
        Assert.True(_registry.TryGet("RECEIVEFILE", out _));

        var help = CommandHelpGenerator.GenerateFor(_registry, "SENDFILE");
        Assert.Contains("path", help);
        Assert.Contains("Kermit", help);
    }

    [Fact]
    public async Task SendFile_MissingFile_FailsBeforeTransferring()
    {
        await _session.ConnectAsync(_connection);

        var result = await _registry.ExecuteAsync("SENDFILE", _session,
            new CommandArgs().Set("path", "C:\\does\\not\\exist-" + Guid.NewGuid().ToString("N") + ".bin"));

        Assert.False(result.Success);
        Assert.Contains("file not found", result.Error);
        Assert.Empty(_connection.GetSentData()); // nothing went on the wire
    }

    [Fact]
    public async Task ReceiveFile_MissingDirectory_FailsBeforeTransferring()
    {
        await _session.ConnectAsync(_connection);

        var result = await _registry.ExecuteAsync("RECEIVEFILE", _session,
            new CommandArgs().Set("dir", "C:\\does\\not\\exist-" + Guid.NewGuid().ToString("N")));

        Assert.False(result.Success);
        Assert.Contains("directory not found", result.Error);
        Assert.Empty(_connection.GetSentData());
    }

    [Fact]
    public async Task SendFile_NotConnected_FailsAsReportableResult()
    {
        // Point at a real file so the not-connected check is what fires.
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            var result = await _registry.ExecuteAsync("SENDFILE", _session,
                new CommandArgs().Set("path", tempFile));

            Assert.False(result.Success);
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }
    }
}
