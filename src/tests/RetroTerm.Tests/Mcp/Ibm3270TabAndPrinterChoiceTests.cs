//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Scripting;
using RetroTerm.Core.Session;
using RetroTerm.Ibm3270;
using RetroTerm.Mcp;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Mcp;

/// <summary>
/// PLAN.md, "Every 3270 feature reachable over MCP": the rest of the per-connection 3270
/// settings - what a double click does, the card reader SUBMIT sends to, and the 3287 printer's
/// Printer tab - from terminal_open, CONNECT and CONNSAVE, declared once in
/// Ibm3270ConnectionSettings. CommandSurfaceEquivalenceTests covers the CONNECT and CONNSAVE
/// arguments on both surfaces; ConnSave3270WindowTests covers what the window shows.
/// </summary>
public sealed class Ibm3270TabAndPrinterChoiceTests : IDisposable
{
    private readonly string _tempScripts;
    private readonly string _tempConfig;

    public Ibm3270TabAndPrinterChoiceTests()
    {
        _tempScripts = Path.Combine(Path.GetTempPath(), "RetroTerm3270Tab-" + Guid.NewGuid().ToString("N"));
        _tempConfig = Path.Combine(Path.GetTempPath(), "RetroTerm3270Tab-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempScripts))
        {
            Directory.Delete(_tempScripts, recursive: true);
        }
        if (File.Exists(_tempConfig))
        {
            File.Delete(_tempConfig);
        }
    }

    private RetroTermToolProvider Provider(InProcessSessionHost host)
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        return new RetroTermToolProvider(host, registry, new ScriptLibrary(_tempScripts), new ConfigurationManager(_tempConfig));
    }

    private static string Text(CallToolResult result)
    {
        Assert.NotEmpty(result.Content);
        return Assert.IsType<TextContentBlock>(result.Content[0]).Text;
    }

    private static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] pairs)
    {
        var args = new Dictionary<string, JsonElement>();
        for (int i = 0; i < pairs.Length; i++)
        {
            args[pairs[i].Name] = JsonSerializer.SerializeToElement(pairs[i].Value);
        }
        return args;
    }

    private static CommandParameter Declared(IReadOnlyList<CommandParameter> declared, string name)
    {
        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i].Name == name)
            {
                return declared[i];
            }
        }
        throw new InvalidOperationException("no " + name + " argument");
    }

    private static Guid SessionId(CallToolResult result)
    {
        string text = Text(result);
        const string prefix = "sessionId: ";
        int start = text.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        int end = text.IndexOf('\n', start);
        return Guid.Parse(text.AsSpan(start, end - start));
    }

    private (CommandRegistry Registry, ConfigurationManager Manager, List<ConnectionFactory.ConnectionParameters> Opened) Commands()
    {
        var manager = new ConfigurationManager(_tempConfig);
        var registry = new CommandRegistry();
        var opened = new List<ConnectionFactory.ConnectionParameters>();
        ConnectionCommands.RegisterAll(registry, manager, p =>
        {
            opened.Add(p);
            return new InMemoryConnection();
        });
        return (registry, manager, opened);
    }

    [Fact]
    public void TheWordsAreTheWindowsOwnTables()
    {
        var open = RetroTermToolProvider.DeclaredArguments("terminal_open")!;
        Assert.Equal(Ibm3270ConnectionSettings.DoubleClickWords, Declared(open, "doubleclick").AllowedValues);
        Assert.Equal(ConnectionFactory.PrinterOutputs.Choices, Declared(open, "printer_output").AllowedValues);
        Assert.Equal(ConnectionFactory.TractorPaperColours.Choices, Declared(open, "printer_band_colour").AllowedValues);
        Assert.Equal(CommandParameterType.Int, Declared(open, "reader_port").Type);
        Assert.Equal(CommandParameterType.Bool, Declared(open, "printer_tractor").Type);
    }

    [Fact]
    public void ConnectTakesTheDoubleClickAndTheCardReaderButNotThePrinterSettings()
    {
        var (registry, _, _) = Commands();
        Assert.True(registry.TryGet("CONNECT", out var command));
        var connect = command.Parameters;
        Declared(connect, "doubleclick");
        Declared(connect, "reader_port");
        Declared(connect, "reader_host");
        for (int i = 0; i < Ibm3270ConnectionSettings.PrinterArgumentNames.Length; i++)
        {
            for (int j = 0; j < connect.Count; j++)
            {
                Assert.NotEqual(Ibm3270ConnectionSettings.PrinterArgumentNames[i], connect[j].Name);
            }
        }
    }

    [Fact]
    public async Task ConnSaveStoresEveryPrinterSettingAsTheWindowDoesAndConnShowPrintsThem()
    {
        var (registry, manager, _) = Commands();
        using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");

        var created = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
            .Set("name", "prt").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
            .Set("emulator", "IBM-3287-1")
            .Set("printer_output", "printer").Set("printer_folder", " jobs\\out ").Set("printer_delete_pdf", "yes")
            .Set("printer_quiet_seconds", "7").Set("printer_lines_per_page", "56")
            .Set("printer_tractor", "true").Set("printer_band_colour", "green")
            .Set("reader_port", "3505"), TestContext.Current.CancellationToken);
        Assert.True(created.Success, created.Error);

        var saved = ConnectionCommands.FindByName(manager, "prt")!;
        Assert.Equal(ConnectionFactory.Ibm3287, saved.EmulatorType);
        Assert.Equal("Printer", saved.PrinterOutput);
        Assert.Equal("jobs\\out", saved.PrinterFolder);
        Assert.True(saved.PrinterDeletePdf);
        Assert.Equal(7, saved.PrinterQuietSeconds);
        Assert.Equal(56, saved.PrinterLinesPerPage);
        Assert.True(saved.PrinterTractorPaper);
        Assert.Equal("Green", saved.PrinterTractorColour);
        Assert.Equal(3505, saved.CardReaderPort);

        var shown = await registry.ExecuteAsync("CONNSHOW", session, new CommandArgs().Set("name", "prt"), TestContext.Current.CancellationToken);
        Assert.Contains("printer output: Printer\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("printer folder: jobs\\out\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("delete the PDF after printing: yes\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("seconds with no data that end a job: 7\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("PDF lines per page: 56\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("tractor paper: yes\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("band colour: Green\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("card reader: localhost:3505\n", shown.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("double click", shown.Output, StringComparison.Ordinal);

        // The defaults go back to null, as the window stores its first entry, an unticked box
        // and an empty text box; 0 is the empty lines-per-page box and the empty port box.
        var reset = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
            .Set("name", "prt").Set("printer_output", "Text").Set("printer_folder", "").Set("printer_delete_pdf", "false")
            .Set("printer_lines_per_page", "0").Set("printer_tractor", "no").Set("printer_band_colour", "Blue")
            .Set("reader_port", "0"), TestContext.Current.CancellationToken);
        Assert.True(reset.Success, reset.Error);
        Assert.Null(saved.PrinterOutput);
        Assert.Null(saved.PrinterFolder);
        Assert.Null(saved.PrinterDeletePdf);
        Assert.Null(saved.PrinterLinesPerPage);
        Assert.Null(saved.PrinterTractorPaper);
        Assert.Null(saved.PrinterTractorColour);
        Assert.Null(saved.CardReaderPort);

        // An update changes only what it is given.
        Assert.Equal(7, saved.PrinterQuietSeconds);

        shown = await registry.ExecuteAsync("CONNSHOW", session, new CommandArgs().Set("name", "prt"), TestContext.Current.CancellationToken);
        Assert.Contains("printer output: Text\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("PDF lines per page: none (a new sheet only on a form feed)\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("card reader: none\n", shown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnSaveStoresTheDoubleClickAndTheCardReaderOnADisplay()
    {
        var (registry, manager, _) = Commands();
        using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");

        var created = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
            .Set("name", "tk5").Set("protocol", "tn3270").Set("host", "localhost").Set("port", "3270")
            .Set("doubleclick", "select").Set("reader_port", "3505").Set("reader_host", " reader.lab "),
            TestContext.Current.CancellationToken);
        Assert.True(created.Success, created.Error);

        var saved = ConnectionCommands.FindByName(manager, "tk5")!;
        Assert.False(saved.DoubleClickPressesEnter);
        Assert.Equal(3505, saved.CardReaderPort);
        Assert.Equal("reader.lab", saved.CardReaderHost);

        var shown = await registry.ExecuteAsync("CONNSHOW", session, new CommandArgs().Set("name", "tk5"), TestContext.Current.CancellationToken);
        Assert.Contains("double click: select (select the word)\n", shown.Output, StringComparison.Ordinal);
        Assert.Contains("card reader: reader.lab:3505\n", shown.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("printer output", shown.Output, StringComparison.Ordinal);

        // enter is the window's first entry, stored as null.
        var back = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
            .Set("name", "tk5").Set("doubleclick", "enter").Set("reader_host", ""), TestContext.Current.CancellationToken);
        Assert.True(back.Success, back.Error);
        Assert.Null(saved.DoubleClickPressesEnter);
        Assert.Null(saved.CardReaderHost);
        Assert.Equal(3505, saved.CardReaderPort);
    }

    [Theory]
    [InlineData("telnet", "IBM-3278-2", "doubleclick", "select", "'doubleclick' only applies with protocol=tn3270")]
    [InlineData("tn3270", "IBM-3287-1", "doubleclick", "select", "'doubleclick' is for a 3278 display, not the IBM-3287-1 printer")]
    [InlineData("ssh", "VT100", "reader_port", "3505", "'reader_port' only applies with protocol=telnet or tn3270")]
    [InlineData("tn3270", "IBM-3278-2", "reader_port", "65536", "'reader_port' must be 1 to 65535, or 0 for no card reader, got 65536")]
    [InlineData("tn3270", "IBM-3278-2", "reader_port", "-1", "'reader_port' must be 1 to 65535, or 0 for no card reader, got -1")]
    [InlineData("telnet", "VT100", "printer_output", "PDF", "'printer_output' only applies with protocol=tn3270")]
    [InlineData("tn3270", "IBM-3278-2", "printer_tractor", "true", "'printer_tractor' is for the IBM-3287-1 printer, and this connection's terminal is IBM-3278-2 - give emulator=IBM-3287-1")]
    [InlineData("tn3270", "IBM-3287-1", "printer_quiet_seconds", "-1", "'printer_quiet_seconds' must be 0 (never) or more seconds, got -1")]
    [InlineData("tn3270", "IBM-3287-1", "printer_lines_per_page", "-5", "'printer_lines_per_page' must be 1 or more lines, or 0 for a new sheet only on a form feed, got -5")]
    public async Task AValueThatDoesNotFitIsRefusedByNameAndNothingIsSaved(string protocol, string emulator, string argument, string value, string expected)
    {
        var (registry, manager, _) = Commands();
        using var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "test");

        var result = await registry.ExecuteAsync("CONNSAVE", session, new CommandArgs()
            .Set("name", "x").Set("protocol", protocol).Set("host", "localhost").Set("emulator", emulator).Set(argument, value),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(expected, result.Error);
        Assert.Null(ConnectionCommands.FindByName(manager, "x"));
    }

    [Fact]
    public async Task TerminalOpenPutsTheDoubleClickAndTheCardReaderOnTheSession()
    {
        using var sessions = new InProcessSessionHost(_ => new InMemoryConnection());
        var result = await Provider(sessions).CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "tk5.lab"), ("doubleclick", "select"), ("reader_port", 3505)),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var session = sessions.GetSession(SessionId(result))!;
        Assert.False(Assert.IsType<Ibm3270Emulator>(session.Emulator).DoubleClickPressesEnter);
        Assert.Equal(3505, session.CardReaderPort);
        Assert.Equal("tk5.lab", session.CardReaderHost);
    }

    [Fact]
    public async Task TerminalOpenHandsThePrinterSettingsToAPrinterConnection()
    {
        var opened = new List<ConnectionFactory.ConnectionParameters>();
        using var sessions = new InProcessSessionHost(p =>
        {
            opened.Add(p);
            return new InMemoryConnection();
        });
        var result = await Provider(sessions).CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "tk5.lab"), ("emulator", "IBM-3287-1"),
                ("printer_output", "PDF"), ("printer_lines_per_page", 56), ("printer_tractor", true), ("printer_band_colour", "Green")),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var parameters = Assert.Single(opened);
        Assert.Equal(ConnectionFactory.Ibm3287, parameters.EmulatorType);
        Assert.Equal("PDF", parameters.PrinterOutput);
        Assert.Equal(56, parameters.PrinterLinesPerPage);
        Assert.True(parameters.PrinterTractorPaper);
        Assert.Equal("Green", parameters.PrinterTractorColour);
    }

    [Fact]
    public async Task TerminalOpenRefusesAPrinterSettingOnADisplayAndNothingOpens()
    {
        var opened = new List<ConnectionFactory.ConnectionParameters>();
        using var sessions = new InProcessSessionHost(p =>
        {
            opened.Add(p);
            return new InMemoryConnection();
        });
        var result = await Provider(sessions).CallToolAsync("terminal_open",
            Args(("protocol", "tn3270"), ("host", "tk5.lab"), ("printer_output", "PDF")),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError ?? false);
        Assert.Equal("terminal_open: 'printer_output' is for the IBM-3287-1 printer, and this connection's terminal is IBM-3278-2 - give emulator=IBM-3287-1", Text(result));
        Assert.Empty(opened);
    }

    [Fact]
    public async Task ConnectPutsTheDoubleClickAndTheCardReaderOnThisTab()
    {
        var (registry, _, opened) = Commands();
        var emulator = new Ibm3270Emulator(2);
        using var session = new TerminalSession(emulator, "test");

        var connected = await registry.ExecuteAsync("CONNECT", session, new CommandArgs()
            .Set("protocol", "tn3270").Set("host", "tk5.lab").Set("doubleclick", "select").Set("reader_port", "3505"),
            TestContext.Current.CancellationToken);

        Assert.True(connected.Success, connected.Error);
        Assert.Single(opened);
        Assert.False(emulator.DoubleClickPressesEnter);
        Assert.Equal(3505, session.CardReaderPort);
        Assert.Equal("tk5.lab", session.CardReaderHost);
    }

    [Fact]
    public async Task ConnectByStoredNameAppliesTheSavedDoubleClickAndCardReader()
    {
        var (registry, _, opened) = Commands();
        using var saver = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.VT100Emulator(80, 24), "saver");
        var saved = await registry.ExecuteAsync("CONNSAVE", saver, new CommandArgs()
            .Set("name", "tk5").Set("protocol", "tn3270").Set("host", "tk5.lab").Set("port", "3270")
            .Set("doubleclick", "select").Set("reader_port", "3505"), TestContext.Current.CancellationToken);
        Assert.True(saved.Success, saved.Error);

        var emulator = new Ibm3270Emulator(2);
        using var session = new TerminalSession(emulator, "test");
        var connected = await registry.ExecuteAsync("CONNECT", session, new CommandArgs().Set("name", "tk5"),
            TestContext.Current.CancellationToken);

        Assert.True(connected.Success, connected.Error);
        Assert.Single(opened);
        Assert.False(emulator.DoubleClickPressesEnter);
        Assert.Equal(3505, session.CardReaderPort);
        Assert.Equal("tk5.lab", session.CardReaderHost);
    }

    [Fact]
    public async Task ConnectRefusesAPrinterSettingByName()
    {
        var (registry, _, opened) = Commands();
        using var session = new TerminalSession(new Ibm3270Emulator(2), "test");

        var refused = await registry.ExecuteAsync("CONNECT", session, new CommandArgs()
            .Set("protocol", "tn3270").Set("host", "tk5.lab").Set("printer_output", "PDF"),
            TestContext.Current.CancellationToken);

        Assert.False(refused.Success);
        Assert.Contains("printer_output", refused.Error, StringComparison.Ordinal);
        Assert.Empty(opened);
    }
}
