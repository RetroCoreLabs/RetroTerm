//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using RetroTerm.Core.Commands;
using RetroTerm.Desktop.Views;

using Xunit;

namespace RetroTerm.Tests.Avalonia
{
    /// <summary>
    /// The IND$FILE dialog of a 3270 tab (Transfer, Send or Receive): the fields become the
    /// arguments of the registered HOSTSEND or HOSTRECEIVE command, and nothing else.
    /// </summary>
    [Collection("Avalonia")]
    public class HostFileTransferDialogTests
    {
        private readonly ITestOutputHelper _output;

        public HostFileTransferDialogTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void TheFieldsBecomeTheCommandsArguments()
        {
            var dialog = new HostFileTransferDialog(true);
            dialog.Fill("  C:\\temp\\hello.txt ", " RTTFT.TEXT ", true, " RECFM(F) LRECL(80) ");

            CommandArgs? args = dialog.ArgsFromFields();

            Assert.NotNull(args);
            Assert.Equal("HOSTSEND", dialog.CommandName);
            Assert.Equal("C:\\temp\\hello.txt", args!.GetString("path"));
            Assert.Equal("RTTFT.TEXT", args.GetString("dataset"));
            Assert.Equal("binary", args.GetString("mode"));
            Assert.Equal("RECFM(F) LRECL(80)", args.GetString("options"));
        }

        [AvaloniaFact]
        public void ReceiveIsHostReceiveInAscii()
        {
            var dialog = new HostFileTransferDialog(false);
            dialog.Fill("C:\\temp\\back.txt", "RTTFT.TEXT", false, string.Empty);

            CommandArgs? args = dialog.ArgsFromFields();

            Assert.Equal("HOSTRECEIVE", dialog.CommandName);
            Assert.Equal("ascii", args!.GetString("mode"));
        }

        [AvaloniaFact]
        public void AnEmptyDataSetIsRefusedOnScreen()
        {
            var dialog = new HostFileTransferDialog(true);
            dialog.Fill("C:\\temp\\hello.txt", "  ", false, string.Empty);
            dialog.Show();

            Assert.Null(dialog.ArgsFromFields());

            TextBlock? problem = Find<TextBlock>(dialog, "ProblemText");
            Assert.NotNull(problem);
            Assert.True(problem!.IsVisible);
            Assert.Equal("Give the host data set.", problem.Text);
        }

        [AvaloniaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void EveryArgumentIsAParameterOfTheRegisteredCommand(bool upload)
        {
            // If a parameter is renamed on the command, the dialog must not go on sending the old
            // name to a command that ignores it.
            var dialog = new HostFileTransferDialog(upload);
            dialog.Fill("C:\\temp\\x.txt", "X", false, string.Empty);
            CommandArgs args = dialog.ArgsFromFields()!;

            var registry = new CommandRegistry();
            RetroTerm.Core.Commands.Builtin.BuiltinCommands.RegisterAll(registry);
            Assert.True(registry.TryGet(dialog.CommandName, out ISessionCommand command));
            string[] names = { "path", "dataset", "mode", "options" };
            for (int i = 0; i < names.Length; i++)
            {
                bool declared = false;
                for (int p = 0; p < command.Parameters.Count; p++)
                {
                    declared |= command.Parameters[p].Name == names[i];
                }

                Assert.True(declared, names[i] + " is not a parameter of " + dialog.CommandName);
                Assert.NotNull(args.GetString(names[i]));
            }
        }

        [AvaloniaFact]
        public void TheDialogDraws()
        {
            var dialog = new HostFileTransferDialog(true);
            dialog.Fill("C:\\temp\\hello.txt", "RTTFT.TEXT", false, string.Empty);
            dialog.Show();

            var shot = RenderedScreenshot.CaptureWindow(dialog, "host-file-transfer-dialog");
            Assert.NotNull(shot);
            using (shot)
            {
                _output.WriteLine("saved to " + shot!.SavedPath);
                Assert.True(shot.HasAnyPixelDifferentFrom(shot.PixelAt(0, 0)), "the dialog is one flat colour");
            }

            dialog.Close();
        }

        private static T? Find<T>(Window window, string name) where T : Control
        {
            var all = new System.Collections.Generic.List<global::Avalonia.Visual>(window.GetVisualDescendants());
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is T control && control.Name == name)
                {
                    return control;
                }
            }

            return null;
        }
    }
}
