//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// Where a finished 3287 print job goes: one file per job (Ronny, 23 November 2026).
    /// </summary>
    public interface IPrintJobOutput
    {
        /// <summary>
        /// Gets a short description for the printer's status screen, such as "text files in C:\Print".
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Writes one job.
        /// </summary>
        /// <param name="job">
        /// The job.
        /// </param>
        /// <param name="number">
        /// The job's number in this session, from 1.
        /// </param>
        /// <param name="localTime">
        /// When the job ended, local time; it goes into the file name.
        /// </param>
        /// <returns>
        /// What happened, for the status screen: normally the full path of the file written.
        /// </returns>
        string Write(PrintJob job, int number, DateTime localTime);
    }

    /// <summary>
    /// The file names of print jobs: "print-20261123-164001-001.txt" in the chosen folder.
    /// </summary>
    public static class PrintJobFiles
    {
        /// <summary>
        /// The folder used when a connection names none: Documents\RetroTerm\Printer.
        /// </summary>
        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetroTerm", "Printer");

        /// <summary>
        /// A new file path for a job, in a folder that exists afterwards. A name already taken
        /// (two sessions, same second, same number) gets "-2", "-3" and so on, so no job ever
        /// overwrites another.
        /// </summary>
        /// <param name="folder">
        /// The output folder, or null or empty for <see cref="DefaultFolder"/>.
        /// </param>
        /// <param name="localTime">
        /// When the job ended.
        /// </param>
        /// <param name="number">
        /// The job number in this session.
        /// </param>
        /// <param name="extension">
        /// The file extension with its dot, for example ".pdf".
        /// </param>
        /// <returns>
        /// The full path; no file exists there yet.
        /// </returns>
        public static string NewPath(string? folder, DateTime localTime, int number, string extension)
        {
            string directory = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
            Directory.CreateDirectory(directory);
            string stem = "print-" + localTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-" + number.ToString("000", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, stem + extension);
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(directory, stem + "-" + n.ToString(CultureInfo.InvariantCulture) + extension);
            }

            return Path.GetFullPath(path);
        }
    }

    /// <summary>
    /// Writes each job as a UTF-8 text file: the lines, and a Form Feed character between pages.
    /// </summary>
    public sealed class TextPrintJobOutput : IPrintJobOutput
    {
        private readonly string? _folder;

        /// <summary>
        /// Creates the output.
        /// </summary>
        /// <param name="folder">
        /// The output folder, or null for <see cref="PrintJobFiles.DefaultFolder"/>.
        /// </param>
        public TextPrintJobOutput(string? folder)
        {
            _folder = folder;
        }

        /// <inheritdoc/>
        public string Description => "text files in " + (string.IsNullOrWhiteSpace(_folder) ? PrintJobFiles.DefaultFolder : _folder);

        /// <inheritdoc/>
        public string Write(PrintJob job, int number, DateTime localTime)
        {
            ArgumentNullException.ThrowIfNull(job);
            string path = PrintJobFiles.NewPath(_folder, localTime, number, ".txt");
            File.WriteAllText(path, job.ToText(Environment.NewLine), new UTF8Encoding(false));
            return path;
        }
    }
}
