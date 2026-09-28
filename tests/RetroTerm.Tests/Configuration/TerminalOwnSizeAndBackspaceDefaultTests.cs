using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Profiles;
using RetroTerm.Desktop.ViewModels;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Configuration;

/// <summary>
/// A terminal's screen size and its Backspace default live on its profile, and every path that
/// picks a terminal type reads them from there.
/// </summary>
/// <remarks>
/// <para><b>What went wrong before 27 September 2026</b></para>
/// The only place that knew a TDV has 25 rows was a switch in EmulatorFactory, and one of nine
/// type-picking paths asked it. A TDV opened from File, New Tab As, from Quick Connect, from the
/// MCP terminal_open tool, from CONNSAVE or from the first-run samples was 80 by 24 and lost its
/// bottom row. The dialog did not change the size when the type was changed either, so Ronny's
/// own file held eight TDV2200 connections, seven of them at 24 rows. And the Telnet and SSH
/// connections were never told the emulator's size at all before the first window resize.
/// Backspace was a plain bool defaulting to false, so every TDV sent BS to SINTRAN unless the
/// checkbox was found.
/// </remarks>
public class TerminalOwnSizeAndBackspaceDefaultTests
{
    /// <summary>
    /// Returns the stored record with this name, or null.
    /// </summary>
    /// <remarks>
    /// Index-based on purpose: no LINQ in this repository.
    /// </remarks>
    /// <param name="manager">
    /// The loaded configuration store.
    /// </param>
    /// <param name="name">
    /// The connection name to look for.
    /// </param>
    /// <returns>
    /// The matching record, or null when none has that name.
    /// </returns>
    private static HostConfiguration? Find(ConfigurationManager manager, string name)
    {
        var all = manager.Configurations;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Name == name) return all[i];
        }
        return null;
    }

    // ── The profile is the source ────────────────────────────────

    [Fact]
    public void EveryBuildableTerminalHasAProfileUnderItsOwnName()
    {
        var types = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < types.Length; i++)
        {
            var profile = EmulatorFactory.GetProfile(types[i]);
            // The xterm profiles spell their name in lower case, which is what TERM wants.
            Assert.Equal(types[i], profile.Name, ignoreCase: true);
            Assert.True(profile.Columns > 0, types[i]);
            Assert.True(profile.Rows > 0, types[i]);
        }
    }

    [Fact]
    public void AnUnknownTypeHasNoProfile()
    {
        Assert.Throws<NotSupportedException>(() => EmulatorFactory.GetProfile("TDV9999"));
    }

    [Theory]
    [InlineData("TDV1200", 80, 25, true)]
    [InlineData("TDV2215", 80, 25, true)]
    [InlineData("TDV2200", 80, 25, true)]
    [InlineData("VT100", 80, 24, false)]
    [InlineData("VT220", 80, 24, false)]
    [InlineData("VT340", 80, 24, false)]
    [InlineData("XTERM", 80, 24, false)]
    [InlineData("TEK4014", 74, 35, false)]
    public void TheProfileCarriesTheScreenAndTheBackspaceDefault(string type, int columns, int rows, bool backspaceSendsDel)
    {
        var profile = EmulatorFactory.GetProfile(type);
        Assert.Equal(columns, profile.Columns);
        Assert.Equal(rows, profile.Rows);
        Assert.Equal(backspaceSendsDel, profile.BackspaceSendsDel);

        // And the factory's two answers are the profile's, not a second list.
        Assert.Equal((columns, rows), EmulatorFactory.GetRecommendedSize(type));
        Assert.Equal(backspaceSendsDel, EmulatorFactory.GetDefaultBackspaceSendsDel(type));
    }

    [Fact]
    public void TheEmulatorsOwnProfileIsTheOneTheFactoryHandsOut()
    {
        // Not a copy: the emulator and the factory must agree because they hold the same object.
        Assert.Same(EmulatorFactory.GetProfile("TDV2200"), new TDV2200Emulator(80, 25).Profile);
        Assert.Same(EmulatorFactory.GetProfile("TDV1200"), new TDV1200Emulator(80, 25).Profile);
        Assert.Same(EmulatorFactory.GetProfile("TDV2215"), new TDV2215Emulator(80, 25).Profile);
        Assert.Same(EmulatorFactory.GetProfile("VT100"), new VT100Emulator(80, 24).Profile);
    }

    [Fact]
    public void AProfileRefusesAnEmptyScreen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalProfile(
            "X", Array.Empty<byte>(), Array.Empty<byte>(), TerminalFeatures.None, Array.Empty<int>(),
            columns: 0, rows: 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalProfile(
            "X", Array.Empty<byte>(), Array.Empty<byte>(), TerminalFeatures.None, Array.Empty<int>(),
            columns: 80, rows: 0));
    }

    // ── The saved file: migration of what was written before ─────

    [Fact]
    public void AnOldTdvRecordGetsItsRowsAndLosesTheUnchosenBackspace()
    {
        var old = new HostConfiguration
        {
            Name = "D100",
            EmulatorType = "TDV2200",
            Width = 80,
            Height = 24,
            BackspaceSendsDel = false,
            SettingsVersion = 0
        };

        Assert.True(ConfigurationManager.MigrateSettings(new List<HostConfiguration> { old }));

        Assert.Equal(25, old.Height);
        Assert.Equal(80, old.Width);
        Assert.Null(old.BackspaceSendsDel);
        Assert.Equal(ConfigurationManager.CurrentSettingsVersion, old.SettingsVersion);
    }

    [Fact]
    public void AnOldVtRecordKeepsItsRowsAndOnlyLosesTheUnchosenBackspace()
    {
        var old = new HostConfiguration { Name = "unix", EmulatorType = "VT220", Height = 24, BackspaceSendsDel = false };

        ConfigurationManager.MigrateSettings(new List<HostConfiguration> { old });

        // A VT follows the window, so 24 was not a wrong answer and is left alone.
        Assert.Equal(24, old.Height);
        // Null on a VT still means BS - nothing the user sees changes.
        Assert.Null(old.BackspaceSendsDel);
        Assert.False(EmulatorFactory.GetDefaultBackspaceSendsDel(old.EmulatorType));
    }

    [Fact]
    public void AWideTdvKeepsItsColumns()
    {
        // 132 columns on a TDV is the documented way to ask for SINTRAN's wide mode. Only the
        // row count is corrected.
        var wide = new HostConfiguration { Name = "wide", EmulatorType = "TDV2200", Width = 132, Height = 24 };

        ConfigurationManager.MigrateSettings(new List<HostConfiguration> { wide });

        Assert.Equal(132, wide.Width);
        Assert.Equal(25, wide.Height);
    }

    [Fact]
    public void AChosenDelSurvivesTheMigration()
    {
        var chosen = new HostConfiguration { Name = "x", EmulatorType = "TDV2200", BackspaceSendsDel = true };

        ConfigurationManager.MigrateSettings(new List<HostConfiguration> { chosen });

        Assert.Equal(true, chosen.BackspaceSendsDel);
    }

    [Fact]
    public void ARecordAlreadyAtTheCurrentVersionIsNeverTouched()
    {
        // A false written by the three-state checkbox after the change IS a choice.
        var current = new HostConfiguration
        {
            Name = "x",
            EmulatorType = "TDV2200",
            Height = 24,
            BackspaceSendsDel = false,
            SettingsVersion = ConfigurationManager.CurrentSettingsVersion
        };

        Assert.False(ConfigurationManager.MigrateSettings(new List<HostConfiguration> { current }));

        Assert.Equal(24, current.Height);
        Assert.Equal(false, current.BackspaceSendsDel);
    }

    [Fact]
    public void AnUnknownTerminalTypeIsLeftAsSaved()
    {
        var odd = new HostConfiguration { Name = "x", EmulatorType = "SOMETHING-ELSE", Height = 24, BackspaceSendsDel = false };

        ConfigurationManager.MigrateSettings(new List<HostConfiguration> { odd });

        Assert.Equal(24, odd.Height);
        Assert.Null(odd.BackspaceSendsDel);
        Assert.Equal(ConfigurationManager.CurrentSettingsVersion, odd.SettingsVersion);
    }

    [Fact]
    public async Task LoadingAnOldFileMigratesItAndWritesItBack()
    {
        // The shape the file had on 27 September 2026: camelCase, backspaceSendsDel false on
        // every record, a TDV2200 at 24 rows, and no settingsVersion at all.
        var path = Path.Combine(Path.GetTempPath(), "RetroTermMigrate-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path,
                "[{\"name\":\"D100\",\"host\":\"localhost\",\"port\":9010,\"protocol\":\"Telnet\"," +
                "\"emulatorType\":\"TDV2200\",\"width\":80,\"height\":24,\"backspaceSendsDel\":false}," +
                "{\"name\":\"unix\",\"host\":\"localhost\",\"port\":22,\"protocol\":\"SSH\"," +
                "\"emulatorType\":\"XTERM\",\"width\":80,\"height\":24,\"backspaceSendsDel\":false}]");

            var manager = new ConfigurationManager(path);
            await manager.LoadAsync();

            var d100 = Find(manager, "D100");
            Assert.NotNull(d100);
            Assert.Equal(25, d100!.Height);
            Assert.Null(d100.BackspaceSendsDel);

            var unix = Find(manager, "unix");
            Assert.NotNull(unix);
            Assert.Equal(24, unix!.Height);
            Assert.Null(unix.BackspaceSendsDel);

            // Written back stamped, so the next load does nothing.
            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("\"settingsVersion\": " + ConfigurationManager.CurrentSettingsVersion, written);
            Assert.Contains("\"height\": 25", written);
            Assert.DoesNotContain("\"backspaceSendsDel\": false", written);

            var again = new ConfigurationManager(path);
            await again.LoadAsync();
            Assert.Equal(written, await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── The host is told the emulator's size at connect ──────────

    /// <summary>
    /// An in-memory connection that remembers the size it was given and when.
    /// </summary>
    private sealed class SizeRecordingConnection : InMemoryConnection, IConnection
    {
        public int Columns;
        public int Rows;
        public bool SizeArrivedBeforeConnect;

        public new Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            SizeArrivedBeforeConnect = Columns > 0;
            return base.ConnectAsync(cancellationToken);
        }

        public Task ResizeTerminalAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            Columns = columns;
            Rows = rows;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TheConnectionIsToldTheEmulatorsSizeBeforeItOpens()
    {
        var session = new TerminalSession(new TDV2200Emulator(80, 25), "size");
        var connection = new SizeRecordingConnection();

        await session.ConnectAsync(connection);

        Assert.Equal(80, connection.Columns);
        Assert.Equal(25, connection.Rows);
        // Before, not after: a Telnet DO NAWS can arrive in the first bytes off the wire, and an
        // SSH pty is sized at open.
        Assert.True(connection.SizeArrivedBeforeConnect);
        session.Dispose();
    }

    // ── CONNSAVE sizes a new record by its terminal ───────────────

    [Fact]
    public async Task ConnsaveWithoutASizeUsesTheTerminalsOwn()
    {
        var path = Path.Combine(Path.GetTempPath(), "RetroTermConnsave-" + Guid.NewGuid().ToString("N") + ".json");
        var manager = new ConfigurationManager(path);
        var registry = new CommandRegistry();
        ConnectionCommands.RegisterAll(registry, manager, _ => new InMemoryConnection());
        using var session = new TerminalSession(new VT100Emulator(80, 24), "connsave");
        try
        {
            var save = await registry.ExecuteAsync("CONNSAVE", session,
                new CommandArgs().Set("name", "D100").Set("host", "localhost").Set("port", "9010")
                    .Set("emulator", "TDV2200"));
            Assert.True(save.Success, save.Error);

            var saved = Find(manager, "D100");
            Assert.NotNull(saved);
            Assert.Equal(80, saved!.Width);
            Assert.Equal(25, saved.Height);

            // Changing the terminal changes the size with it...
            var change = await registry.ExecuteAsync("CONNSAVE", session,
                new CommandArgs().Set("name", "D100").Set("emulator", "TEK4014"));
            Assert.True(change.Success, change.Error);
            Assert.Equal((74, 35), (saved.Width, saved.Height));

            // ...unless the same command says otherwise.
            var explicitSize = await registry.ExecuteAsync("CONNSAVE", session,
                new CommandArgs().Set("name", "D100").Set("emulator", "TDV2200").Set("width", "132"));
            Assert.True(explicitSize.Success, explicitSize.Error);
            Assert.Equal((132, 25), (saved.Width, saved.Height));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── The connection dialog ────────────────────────────────────

    [Fact]
    public void PickingATerminalInTheDialogSetsItsSize()
    {
        var profile = new ConnectionProfileViewModel();
        Assert.Equal("80x24", profile.Size);

        profile.EmulatorType = "TDV2200";
        Assert.Equal("80x25", profile.Size);

        profile.EmulatorType = "TEK4014";
        Assert.Equal("74x35", profile.Size);

        profile.EmulatorType = "VT220";
        Assert.Equal("80x24", profile.Size);
    }

    [Fact]
    public void ANewProfileHasNotChosenBackspace()
    {
        var profile = new ConnectionProfileViewModel();
        Assert.Null(profile.BackspaceSendsDel);
        Assert.Null(profile.ToHostConfiguration().BackspaceSendsDel);
        Assert.Null(profile.ToConnectionParameters().BackspaceSendsDel);
    }
}
