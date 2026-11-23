//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Desktop.Printing;
using RetroTerm.Desktop.Views;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The 3287 printer's desktop side: the PDF and printer outputs, the Printer tab of Manage
/// Connections, and the printer tab's rendered status screen.
/// </summary>
[Collection("Avalonia")]
public sealed class Ibm3287PrinterTests : IDisposable
{
    // A folder of its own in %TEMP%, removed afterwards; never the user's Documents.
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RetroTerm.Tests.Printer3287Pdf." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static PrintJob TwoPageJob()
    {
        var printer = new Printer3287();
        printer.ProcessRecord(Printer3287Tests.Record(Command3270.EraseWrite, 0x08,
            new string('W', 132), (byte)0x15, "SECOND LINE", (byte)0x15, (byte)0x0C, "PAGE TWO", (byte)0x19));
        PrintJob? job = printer.TakeJob();
        Assert.NotNull(job);
        Assert.Equal(2, job.PageCount);
        return job;
    }

    // "/Type /Page" not followed by "s": one per sheet.
    private static int PdfPageCount(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf("/Type /Page", at, StringComparison.Ordinal)) >= 0)
        {
            at += "/Type /Page".Length;
            if (at >= text.Length || text[at] != 's')
            {
                count++;
            }
        }

        return count;
    }

    [AvaloniaFact]
    public void APdfJobIsOneFileWithOneSheetPerPage()
    {
        var output = new PdfPrintJobOutput(_folder);

        string first = output.Write(TwoPageJob(), 1, new DateTime(2026, 11, 23, 16, 40, 1));
        string second = output.Write(TwoPageJob(), 2, new DateTime(2026, 11, 23, 16, 40, 1));

        Assert.Equal(Path.Combine(Path.GetFullPath(_folder), "print-20261123-164001-001.pdf"), first);
        Assert.NotEqual(first, second);
        byte[] pdf = File.ReadAllBytes(first);
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal(2, PdfPageCount(pdf));
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
    }

    [AvaloniaFact]
    public void PrinterOutputHandsThePdfToThePrintCommandAndDeletesItWhenAsked()
    {
        var command = new RecordingPrintCommand();
        var output = new SystemPrinterJobOutput(_folder, command, deletePdf: true);

        string result = output.Write(TwoPageJob(), 1, new DateTime(2026, 11, 23, 16, 40, 1));
        Assert.Single(command.Printed);
        Assert.StartsWith(command.Printed[0], result, StringComparison.Ordinal);
        Assert.True(File.Exists(command.Printed[0]));

        command.Finish(true);
        Assert.True(output.LastPrint.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(command.Printed[0]));
    }

    [AvaloniaFact]
    public void PrinterOutputKeepsThePdfWhenNotAskedToDelete()
    {
        var command = new RecordingPrintCommand();
        var output = new SystemPrinterJobOutput(_folder, command, deletePdf: false);

        output.Write(TwoPageJob(), 1, DateTime.Now);
        command.Finish(true);
        Assert.True(output.LastPrint.Wait(TimeSpan.FromSeconds(10)));

        Assert.True(File.Exists(command.Printed[0]));
    }

    [AvaloniaFact]
    public void TheFactoryFollowsTheConnectionsOutputSetting()
    {
        var parameters = new ConnectionFactory.ConnectionParameters { PrinterFolder = _folder };
        Assert.IsType<TextPrintJobOutput>(PrinterJobOutputFactory.Create(parameters));

        parameters.PrinterOutput = ConnectionFactory.PrinterOutputs.Pdf;
        Assert.IsType<PdfPrintJobOutput>(PrinterJobOutputFactory.Create(parameters));

        parameters.PrinterOutput = ConnectionFactory.PrinterOutputs.Printer;
        Assert.IsType<SystemPrinterJobOutput>(PrinterJobOutputFactory.Create(parameters, new RecordingPrintCommand()));
    }

    [AvaloniaFact]
    public void ThePrinterTabSavesWithTheConnection()
    {
        WithWindow(window =>
        {
            window.LoadProfileForTesting(new HostConfiguration
            {
                Name = "tk5 printer",
                Protocol = "TN3270",
                Host = "localhost",
                Port = 3270,
                EmulatorType = ConnectionFactory.Ibm3287,
                PrinterOutput = ConnectionFactory.PrinterOutputs.Printer,
                PrinterFolder = @"D:\Print",
                PrinterDeletePdf = true,
                PrinterQuietSeconds = 7,
                PrinterLinesPerPage = 56,
                PrinterTractorPaper = true,
                PrinterTractorColour = ConnectionFactory.TractorPaperColours.Green,
            });
            window.ShowPrinterTabForTesting();

            ComboBox output = Find<ComboBox>(window, "PrinterOutputCombo");
            TextBox folder = Find<TextBox>(window, "PrinterFolderBox");
            CheckBox delete = Find<CheckBox>(window, "PrinterDeletePdfCheck");
            TextBox quiet = Find<TextBox>(window, "PrinterQuietBox");
            Assert.Equal(ManageConnectionsWindow.PrinterOutputPrinterText, output.SelectedItem);
            Assert.Equal(@"D:\Print", folder.Text);
            Assert.True(delete.IsChecked);
            Assert.True(delete.IsEnabled);
            Assert.Equal("7", quiet.Text);
            TextBox linesPerPage = Find<TextBox>(window, "PrinterLinesPerPageBox");
            CheckBox tractor = Find<CheckBox>(window, "PrinterTractorCheck");
            ComboBox colour = Find<ComboBox>(window, "PrinterTractorColourCombo");
            Assert.Equal("56", linesPerPage.Text);
            Assert.True(tractor.IsChecked);
            Assert.Equal(ManageConnectionsWindow.PrinterTractorGreenText, colour.SelectedItem);
            Assert.True(colour.IsEnabled);

            // The saved connection is still a printer, not turned into a 3278.
            Assert.Equal(ConnectionFactory.Ibm3287, window.ProfileForTesting.ToHostConfiguration().EmulatorType);

            output.SelectedIndex = 1;
            folder.Text = @"E:\Jobs";
            quiet.Text = "0";
            linesPerPage.Text = "60";
            colour.SelectedIndex = 0;
            HostConfiguration saved = window.ProfileForTesting.ToHostConfiguration();
            Assert.Equal(ConnectionFactory.PrinterOutputs.Pdf, saved.PrinterOutput);
            Assert.Equal(@"E:\Jobs", saved.PrinterFolder);
            Assert.Equal(0, saved.PrinterQuietSeconds);
            Assert.False(delete.IsEnabled);

            ConnectionFactory.ConnectionParameters parameters = saved.ToConnectionParameters();
            Assert.Equal(ConnectionFactory.PrinterOutputs.Pdf, parameters.PrinterOutput);
            Assert.Equal(@"E:\Jobs", parameters.PrinterFolder);
            Assert.True(parameters.PrinterDeletePdf);
            Assert.Equal(0, parameters.PrinterQuietSeconds);
            Assert.Equal(60, saved.PrinterLinesPerPage);
            Assert.Equal(60, parameters.PrinterLinesPerPage);
            Assert.True(parameters.PrinterTractorPaper);
            Assert.Null(saved.PrinterTractorColour);

            // Not a number of lines: the stored value stays.
            linesPerPage.Text = "0";
            Assert.Equal(60, window.ProfileForTesting.ToHostConfiguration().PrinterLinesPerPage);
            colour.SelectedIndex = 1;
            Assert.Equal(ConnectionFactory.TractorPaperColours.Green, window.ProfileForTesting.ToHostConfiguration().PrinterTractorColour);

            output.SelectedIndex = 0;
            folder.Text = "";
            quiet.Text = "";
            linesPerPage.Text = "";
            tractor.IsChecked = false;
            Assert.False(colour.IsEnabled);
            saved = window.ProfileForTesting.ToHostConfiguration();
            Assert.Null(saved.PrinterLinesPerPage);
            Assert.Null(saved.PrinterTractorPaper);
            Assert.Null(saved.PrinterOutput);
            Assert.Null(saved.PrinterFolder);
            Assert.Null(saved.PrinterQuietSeconds);

            using var shot = RenderedScreenshot.CaptureWindow(window, "manage-connections-printer-tab");
        });
    }

    [AvaloniaFact]
    public void ThePrinterTabShowsItsStateAndItsJobs()
    {
        DateTime now = new DateTime(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc);
        var printer = new Ibm3287PrinterEmulator(() => now);
        printer.Configure(new TextPrintJobOutput(_folder), null);
        printer.ConnectionStatusChanged(ConnectionStatus.Connected);
        printer.ProcessData(Printer3287Tests.Record(Command3270.EraseWrite, 0x08, "HELLO", (byte)0x19));
        printer.EndOfJobReceived();
        printer.ProcessData(Printer3287Tests.Record(Command3270.EraseWrite, 0x08, "AGAIN", (byte)0x19));

        using var shot = RenderedScreenshot.Capture(printer, "ibm3287-printer-status");

        // The title, the state line and the job line carry ink; the empty row between does not.
        Assert.True(shot.CellHasInk(0, 0));
        Assert.True(shot.CellHasInk(3, 0));
        Assert.True(shot.CellHasInk(10, 3));
        Assert.False(shot.CellHasInk(1, 0));
    }

    private static void WithWindow(Action<ManageConnectionsWindow> test)
    {
        // A configuration file of its own in %TEMP%, never the user's real one.
        string path = Path.Combine(Path.GetTempPath(), "RetroTermPrinterTab-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var window = new ManageConnectionsWindow(new ConfigurationManager(path));
            window.Show();
            test(window);
            window.Close();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static T Find<T>(ManageConnectionsWindow window, string name)
        where T : Control
    {
        var all = new List<ILogical>(window.GetLogicalDescendants());
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is T control && control.Name == name)
            {
                return control;
            }
        }

        throw new Xunit.Sdk.XunitException("no " + typeof(T).Name + " named " + name);
    }

    // Records what it was asked to print; never touches a real printer.
    private sealed class RecordingPrintCommand : ISystemPrintCommand
    {
        private readonly TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Printed { get; } = new List<string>();

        public Task<bool> PrintAsync(string path)
        {
            Printed.Add(path);
            return _done.Task;
        }

        public void Finish(bool fileNoLongerNeeded) => _done.TrySetResult(fileNoLongerNeeded);
    }
}
