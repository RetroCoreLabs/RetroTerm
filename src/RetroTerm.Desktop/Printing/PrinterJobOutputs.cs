//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;
using RetroTerm.Ibm3270;

namespace RetroTerm.Desktop.Printing;

/// <summary>
/// Writes each 3287 print job as a PDF: one file per job, one sheet per page, monospaced at 10
/// characters an inch and 6 lines an inch, through the same <see cref="PdfPrintSink"/> the VT
/// printer port uses.
/// </summary>
/// <remarks>
/// The sheet is US Letter and grows to fit a wider line, so a 132-column job is not cut
/// (PdfPrintSink, "The sheet grows rather than cropping"). The page breaks are the host's Form
/// Feeds and nothing else; a job without a Form Feed is one tall sheet.
/// </remarks>
public sealed class PdfPrintJobOutput : IPrintJobOutput
{
    private readonly string? _folder;

    /// <summary>
    /// Creates the output.
    /// </summary>
    /// <param name="folder">
    /// The output folder, or null for <see cref="PrintJobFiles.DefaultFolder"/>.
    /// </param>
    public PdfPrintJobOutput(string? folder)
    {
        _folder = folder;
    }

    /// <inheritdoc/>
    public string Description => "PDF files in " + (string.IsNullOrWhiteSpace(_folder) ? PrintJobFiles.DefaultFolder : _folder);

    /// <inheritdoc/>
    public string Write(PrintJob job, int number, DateTime localTime)
    {
        ArgumentNullException.ThrowIfNull(job);
        string path = PrintJobFiles.NewPath(_folder, localTime, number, ".pdf");
        WritePdf(job, path);
        return path;
    }

    /// <summary>
    /// Writes a job as a PDF at a given path.
    /// </summary>
    /// <param name="job">
    /// The job.
    /// </param>
    /// <param name="path">
    /// Where the PDF goes; an existing file is replaced.
    /// </param>
    internal static void WritePdf(PrintJob job, string path)
    {
        using var sink = new PdfPrintSink(path);
        for (int page = 0; page < job.PageCount; page++)
        {
            if (page > 0)
            {
                sink.FormFeed();
            }

            var lines = job.Page(page);

            // An empty page is a sheet the host ejected with nothing on it. PdfPrintSink skips a
            // page with no bytes, so it gets one empty line to keep the sheet, as the text file does.
            if (lines.Count == 0)
            {
                sink.Write("\n"u8);
                continue;
            }

            for (int line = 0; line < lines.Count; line++)
            {
                WriteLatin1(sink, lines[line]);
                sink.Write("\n"u8);
            }
        }
    }

    // PdfPrintSink reads its text as one byte per character (Latin-1). The 3270 code pages RetroTerm
    // has map into Latin-1; anything outside it would be a different character, so it is a '?'.
    private static void WriteLatin1(PdfPrintSink sink, string text)
    {
        Span<byte> one = stackalloc byte[1];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            one[0] = c <= 'ÿ' ? (byte)c : (byte)'?';
            sink.Write(one);
        }
    }
}

/// <summary>
/// Hands a file to the system's print command.
/// </summary>
public interface ISystemPrintCommand
{
    /// <summary>
    /// Starts printing a file.
    /// </summary>
    /// <param name="path">
    /// The file, a PDF.
    /// </param>
    /// <returns>
    /// A task that completes with true when the print command finished and the file may be
    /// deleted, or false when it cannot be known when the file is no longer needed.
    /// </returns>
    Task<bool> PrintAsync(string path);
}

/// <summary>
/// The print command of the system this runs on: the shell's "print" verb for the file on Windows
/// (the default printer, through whatever program is registered to print PDFs), and lp on Linux
/// and macOS (the default destination).
/// </summary>
/// <remarks>
/// NOT tested by printing: the unit tests use their own command so nothing ever reaches a real
/// printer. On Windows the print verb needs a program that registers "print" for .pdf; without one
/// Process.Start throws and the job is listed as failed with that message.
/// </remarks>
public sealed class SystemPrintCommand : ISystemPrintCommand
{
    /// <inheritdoc/>
    public async Task<bool> PrintAsync(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Verb = "print",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using Process? process = Process.Start(info);

            // The registered program may hand the file to one that is already running and give
            // back no process; then there is no way to know when it has read the file.
            if (process == null)
            {
                return false;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return true;
        }

        var lp = new ProcessStartInfo("lp") { UseShellExecute = false };
        lp.ArgumentList.Add("--");
        lp.ArgumentList.Add(path);
        using Process started = Process.Start(lp) ?? throw new InvalidOperationException("lp could not be started.");
        await started.WaitForExitAsync().ConfigureAwait(false);
        if (started.ExitCode != 0)
        {
            throw new InvalidOperationException("lp ended with exit code " + started.ExitCode + ".");
        }

        // lp has passed the file to the print spooler by the time it exits.
        return true;
    }
}

/// <summary>
/// Makes the PDF of each job and hands it to the system's print command; with "Delete the PDF
/// after printing" ticked, the PDF is deleted once the print command has finished with it.
/// </summary>
public sealed class SystemPrinterJobOutput : IPrintJobOutput
{
    private readonly string? _folder;
    private readonly ISystemPrintCommand _command;
    private readonly bool _deletePdf;

    /// <summary>
    /// Creates the output.
    /// </summary>
    /// <param name="folder">
    /// The folder for the PDFs, or null for <see cref="PrintJobFiles.DefaultFolder"/>.
    /// </param>
    /// <param name="command">
    /// The print command.
    /// </param>
    /// <param name="deletePdf">
    /// True to delete each PDF after printing.
    /// </param>
    public SystemPrinterJobOutput(string? folder, ISystemPrintCommand command, bool deletePdf)
    {
        _folder = folder;
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _deletePdf = deletePdf;
    }

    /// <summary>
    /// Gets the task of the last job's print command and deletion, for the tests.
    /// </summary>
    internal Task LastPrint { get; private set; } = Task.CompletedTask;

    /// <inheritdoc/>
    public string Description => "the system printer, PDF kept in " + (string.IsNullOrWhiteSpace(_folder) ? PrintJobFiles.DefaultFolder : _folder)
        + (_deletePdf ? " until printed" : string.Empty);

    /// <inheritdoc/>
    public string Write(PrintJob job, int number, DateTime localTime)
    {
        ArgumentNullException.ThrowIfNull(job);
        string path = PrintJobFiles.NewPath(_folder, localTime, number, ".pdf");
        PdfPrintJobOutput.WritePdf(job, path);

        // Started here so a missing print program fails this job, where the status screen lists it.
        Task<bool> printing = _command.PrintAsync(path);
        LastPrint = FinishAsync(printing, path);
        return path + (_deletePdf ? " (sent to the printer; deleted when printed)" : " (sent to the printer)");
    }

    private async Task FinishAsync(Task<bool> printing, string path)
    {
        try
        {
            bool done = await printing.ConfigureAwait(false);
            if (!_deletePdf)
            {
                return;
            }

            if (!done)
            {
                ApplicationLogger.Log(LogCategory.General, LogLevel.Warn, "Ibm3287Printer",
                    "The print program gave no way to tell when it had read " + path + ", so the PDF was kept.");
                return;
            }

            File.Delete(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            // Printing has already been reported as started; this is what happened after. Logged,
            // never dropped.
            ApplicationLogger.Log(LogCategory.General, LogLevel.Error, "Ibm3287Printer", "Printing " + path + " failed: " + ex.Message);
        }
    }
}

/// <summary>
/// Picks the output a 3287 printer connection asked for.
/// </summary>
public static class PrinterJobOutputFactory
{
    /// <summary>
    /// The output for a connection's printer settings.
    /// </summary>
    /// <param name="parameters">
    /// The connection.
    /// </param>
    /// <param name="command">
    /// The print command for printer output; null for the system's own.
    /// </param>
    /// <returns>
    /// The output.
    /// </returns>
    public static IPrintJobOutput Create(ConnectionFactory.ConnectionParameters parameters, ISystemPrintCommand? command = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        string? folder = string.IsNullOrWhiteSpace(parameters.PrinterFolder) ? null : parameters.PrinterFolder;
        return ConnectionFactory.PrinterOutputs.Normalise(parameters.PrinterOutput) switch
        {
            ConnectionFactory.PrinterOutputs.Pdf => new PdfPrintJobOutput(folder),
            ConnectionFactory.PrinterOutputs.Printer => new SystemPrinterJobOutput(folder, command ?? new SystemPrintCommand(), parameters.PrinterDeletePdf == true),
            _ => new TextPrintJobOutput(folder),
        };
    }
}
