//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Globalization;

using RetroTerm.Core.Logging;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// One print job as the printer's status screen lists it.
    /// </summary>
    /// <param name="Number">
    /// The job number in this session, from 1.
    /// </param>
    /// <param name="LocalTime">
    /// When the job ended.
    /// </param>
    /// <param name="Pages">
    /// How many pages it had.
    /// </param>
    /// <param name="Result">
    /// Where it went (normally the file's full path), or why it could not be written.
    /// </param>
    /// <param name="Failed">
    /// True when the job could not be written.
    /// </param>
    /// <param name="EndedBy">
    /// What ended the job, in words.
    /// </param>
    public sealed record PrintedJob(int Number, DateTime LocalTime, int Pages, string Result, bool Failed, string EndedBy);

    /// <summary>
    /// An IBM 3287 printer session: the tab of a TN3270 connection that answers the host as
    /// IBM-3287-1. It has no screen for the host; what the tab shows is the printer's own state
    /// (connected, waiting, the jobs printed and their files), drawn into the ordinary cell buffer
    /// so the normal renderer shows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One file per print job. What ends a job.</b>
    /// Three things, each read from x3270's pr3287 as a specification. First, telnet Abort Output
    /// from the host in plain TN3270 (pr3287 telnet.c, case AO), which the session passes on
    /// through <see cref="IPrintJobEndAware"/>. Second, the connection closing (pr3287 flushes the
    /// job when the host goes away, pr3287.c "Flush any pending data" before the socket is
    /// closed). Third, a quiet time with no data from the host (pr3287 -eojtimeout, pr3287.c
    /// line 65), 5 seconds unless the connection says otherwise; 0 turns it off.
    /// </para>
    /// <para>
    /// EM is NOT a job end: GA23-0059-4 p. 8-2 calls it "End of Message", which ends one printout,
    /// and pr3287 treats it the same way (it flushes the printout, ctlr.c -emflush, and carries on
    /// with the same job).
    /// What Hercules and TK5 actually send at the end of a job has not been measured; see
    /// docs\IBM3270-INTRODUCTION.md, "Printing".
    /// </para>
    /// <para>
    /// <b>TN3270E</b> (RFC 2355): the same printer takes SCS-DATA (Printer3287.ProcessScs) and
    /// 3270-DATA, says how each record went for the RESPONSES function
    /// (<see cref="IHostRecordOutcomeTerminal"/>), and ends a job on PRINT-EOJ, which the
    /// connection passes on as the same end-of-job signal as Abort Output. One printer, two
    /// transports. Built 24 November 2026 from the RFC; no real TN3270E host has sent it a job.
    /// </para>
    /// <para>
    /// <b>Not ready (Intervention Required).</b> A real 3287 is not ready "when it is out of paper,
    /// its cover is open, or it is mechanically disabled" (GA23-0061-2 p. 2-50, Error Conditions). This printer has
    /// no paper or cover; the one thing that can honestly stop it is a job that cannot be
    /// written: the output folder is not writable, the disk is full, the print command fails.
    /// So a failed write makes the printer not ready. The job is kept, not lost, and written
    /// again every <see cref="NotReadyRetrySeconds"/> seconds; jobs that end meanwhile wait behind
    /// it in order. While it is not ready and the host will hear about it (TN3270E with the
    /// RESPONSES function, and a record whose RESPONSE-FLAG is ERROR-RESPONSE or ALWAYS-RESPONSE),
    /// each such record is refused without printing it and answered Intervention Required: "A
    /// command attemped to start a printer but found it not ready. The printout is suppressed."
    /// (GA23-0061-2 p. 4-26). When every waiting job is written the printer is ready, and the
    /// connection sends REQUEST ERR-COND-CLEARED (RFC 2355 section 10.4.1) if the host was told.
    /// </para>
    /// <para>
    /// When the host cannot be told, records are still taken and wait as jobs: refusing them would
    /// lose them with nobody knowing. That is the case without RESPONSES, and also for a record
    /// sent with NO-RESPONSE: "The receiver must therefore not send any response to this
    /// data-message" (RFC 2355 8.1.3), and 10.4.1 says the receiver "must attempt to process the
    /// data". The session hands the printer each record's flag before the record
    /// (<see cref="HostRecordResponseRequested"/>). Keeping such a record as a waiting job is our
    /// reading of those two sentences; the RFC does not say what a not-ready printer does with
    /// it, and no real TN3270E host has been measured sending one.
    /// </para>
    /// <para>
    /// <b>Component Disconnected</b> (RFC 2355 10.4.1 status X'03', "Printer is powered off or
    /// not connected") is never reported by this printer. GA23-0061-2 p. 5-66 gives it for a
    /// device the 3274 "cannot be contacted by a device poll", from power off or a cable; here
    /// the TN3270E client and the printer are one program, so a printer that can answer the host
    /// has been contacted, and every failure it has is a job it took and holds (Intervention
    /// Required). The connection still sends the status correctly if an outcome says so
    /// (Core.Protocols.HostRecordOutcome.ComponentDisconnected).
    /// </para>
    /// </remarks>
    public sealed class Ibm3287PrinterEmulator : TerminalEmulatorBase, IConnectionStatusAware, IPrintJobEndAware, ITn3270EAware, IHostRecordOutcomeTerminal
    {
        /// <summary>
        /// The terminal type a 3287 announces: the only printer name Hercules accepts ("The only
        /// supported 3270 printer type is IBM-3287-1", hyperion console.c, negotiate_ttype).
        /// </summary>
        public const string TerminalType = "IBM-3287-1";

        /// <summary>
        /// The quiet time when the connection does not set one: 5 seconds (Ronny, 23 November 2026).
        /// </summary>
        public const int DefaultQuietSeconds = 5;

        /// <summary>
        /// How often a printer that is not ready tries its waiting job again: 10 seconds. Our
        /// choice, not from any manual: often enough that a fixed folder is noticed soon, seldom
        /// enough that a failing print command is not started every second.
        /// </summary>
        public const int NotReadyRetrySeconds = 10;

        private const int ScreenColumns = 80;
        private const int ScreenRows = 24;

        // The rows above the job list: title, blank, connection, state, output, job end, blank,
        // problem, blank, "Jobs printed" heading.
        private const int JobListRow = 10;

        private static readonly TerminalColor Ink = TerminalColor.FromRgb(0xE0, 0xE0, 0xE0);
        private static readonly TerminalColor Paper = TerminalColor.FromRgb(0x10, 0x10, 0x10);

        private readonly TerminalProfile _profile;
        private readonly Printer3287 _printer = new Printer3287();
        private readonly Func<DateTime> _utcNow;
        private readonly List<PrintedJob> _jobs = new List<PrintedJob>();

        private IPrintJobOutput _output = new TextPrintJobOutput(null);
        private int _quietSeconds = DefaultQuietSeconds;
        private ConnectionStatus _connection = ConnectionStatus.Disconnected;
        private DateTime _lastDataUtc;
        private string? _lastProblem;
        private int _jobNumber;

        // True while the connection runs TN3270E, and the printer LU the server gave.
        private bool _inTn3270E;
        private string? _luName;

        // True while TN3270E runs with RESPONSES: only then can a refusal reach the host.
        private bool _responses;

        // Not ready: the jobs that could not be written yet, oldest first, each with its index in
        // _jobs (where it is listed as FAILED until it is written). Empty means ready.
        private readonly List<HeldJob> _held = new List<HeldJob>();
        private DateTime _nextRetryUtc;
        private string? _notReadyReason;

        // True when the record being applied was refused because the printer is not ready.
        private bool _refusedNotReady;

        // What the host asked to hear about the record about to be applied (its RESPONSE-FLAG),
        // from the session just before the record; back to NoResponse after each record.
        private HostRecordResponseRequest _request = HostRecordResponseRequest.NoResponse;

        /// <summary>
        /// Creates a printer on the system clock.
        /// </summary>
        public Ibm3287PrinterEmulator()
            : this(() => DateTime.UtcNow)
        {
        }

        /// <summary>
        /// Creates a printer on a given clock, so a test can move time instead of waiting for it.
        /// </summary>
        /// <param name="utcNow">
        /// Answers the current time in UTC.
        /// </param>
        public Ibm3287PrinterEmulator(Func<DateTime> utcNow)
            : base(ScreenColumns, ScreenRows, maxScrollback: 0)
        {
            ArgumentNullException.ThrowIfNull(utcNow);
            _utcNow = utcNow;
            _profile = new TerminalProfile(
                name: TerminalType,
                primaryDeviceAttributes: Array.Empty<byte>(),
                secondaryDeviceAttributes: Array.Empty<byte>(),
                features: TerminalFeatures.None,
                recognisedPrivateModes: Array.Empty<int>(),
                keyboardLayout: "IBM3270",
                transportEncoding: TransportEncoding.EightBit,
                columns: ScreenColumns,
                rows: ScreenRows);
            _printer.ProtocolError += OnProtocolError;
            Cursor.Visible = false;
            Paint();
        }

        /// <summary>
        /// The status screen: 80 by 24.
        /// </summary>
        public override TerminalProfile Profile => _profile;

        /// <summary>
        /// Gets the jobs printed in this session, oldest first.
        /// </summary>
        public IReadOnlyList<PrintedJob> Jobs => _jobs;

        /// <summary>
        /// Gets the printer model, for tests and tools.
        /// </summary>
        public Printer3287 Printer => _printer;

        /// <summary>
        /// Gets the quiet time in seconds that ends a job; 0 when the timer is off.
        /// </summary>
        public int QuietSeconds => _quietSeconds;

        /// <summary>
        /// Gets the output the jobs go to.
        /// </summary>
        public IPrintJobOutput Output => _output;

        /// <summary>
        /// Gets the last thing that went wrong (a rejected record, a file that could not be
        /// written), or null.
        /// </summary>
        public string? LastProblem => _lastProblem;

        /// <summary>
        /// Sets where jobs go and the quiet time. Call on the session thread.
        /// </summary>
        /// <param name="output">
        /// Where each finished job is written.
        /// </param>
        /// <param name="quietSeconds">
        /// Seconds without data that end a job; null for <see cref="DefaultQuietSeconds"/>, 0 for never.
        /// </param>
        public void Configure(IPrintJobOutput output, int? quietSeconds)
        {
            ArgumentNullException.ThrowIfNull(output);
            _output = output;
            _quietSeconds = quietSeconds is int seconds && seconds >= 0 ? seconds : DefaultQuietSeconds;
            Repaint();
        }

        /// <summary>
        /// Applies ONE complete 3270 record from the host (the connection hands over whole records).
        /// </summary>
        /// <param name="data">
        /// The record, command byte first.
        /// </param>
        public override void ProcessData(ReadOnlySpan<byte> data)
        {
            _lastDataUtc = _utcNow();
            if (RefuseWhileNotReady(data.Length))
            {
                return;
            }

            _printer.ProcessRecord(data);
            Repaint();
        }

        /// <summary>
        /// Gets how the last record went (3270 or SCS), for a TN3270E response: Intervention
        /// Required when it was refused because the printer is not ready.
        /// </summary>
        public HostRecordOutcome LastRecordOutcome => _refusedNotReady ? HostRecordOutcome.InterventionRequired : _printer.LastOutcome;

        /// <summary>
        /// What the host asked to hear about the next record (RFC 2355 8.1.3). A not-ready printer
        /// refuses a record only when the host will hear about it (see the class remarks).
        /// </summary>
        /// <param name="request">
        /// The record's response flag.
        /// </param>
        public void HostRecordResponseRequested(HostRecordResponseRequest request)
        {
            _request = request;
        }

        /// <summary>
        /// Gets a value indicating whether the printer is ready: no job is waiting to be written.
        /// </summary>
        public bool IsReady => _held.Count == 0;

        /// <summary>
        /// Gets why the printer is not ready (the last write failure), or null when it is ready.
        /// </summary>
        public string? NotReadyReason => _notReadyReason;

        // Called first for every record: refuses it, unprinted, when the printer is not ready and
        // the host will be told (see the class remarks). True when refused. The record's flag is
        // used once: the next record brings its own.
        private bool RefuseWhileNotReady(int length)
        {
            HostRecordResponseRequest request = _request;
            _request = HostRecordResponseRequest.NoResponse;
            _refusedNotReady = false;
            if (_held.Count == 0 || !_inTn3270E || !_responses || request == HostRecordResponseRequest.NoResponse)
            {
                return false;
            }

            _refusedNotReady = true;
            OnProtocolError("a record of " + length.ToString(CultureInfo.InvariantCulture)
                + " bytes was refused, not printed: the printer is not ready (answered Intervention Required)");
            Repaint();
            return true;
        }

        /// <summary>
        /// Gets a value indicating whether the connection runs TN3270E.
        /// </summary>
        public bool InTn3270E => _inTn3270E;

        /// <summary>
        /// A printer has no keys. Raised with <see cref="Tn3270EKey.ErrorConditionCleared"/> when
        /// the printer is ready again, so the connection can tell the host (RFC 2355 10.4.1).
        /// </summary>
        public event Tn3270EKeyHandler? Tn3270EKeyPressed;

        /// <summary>
        /// The connection's TN3270E state changed; the status screen shows it and the printer LU.
        /// </summary>
        /// <param name="on">
        /// True when TN3270E is in force.
        /// </param>
        /// <param name="luName">
        /// The printer's LU name the server gave, or null.
        /// </param>
        /// <param name="bindImage">
        /// Not used by a printer.
        /// </param>
        /// <param name="sysReq">
        /// Not used by a printer.
        /// </param>
        /// <param name="responses">
        /// True when the RESPONSES function was agreed: a refused record reaches the host.
        /// </param>
        public void Tn3270EChanged(bool on, string? luName, bool bindImage, bool sysReq, bool responses)
        {
            _inTn3270E = on;
            _luName = on ? luName : null;
            _responses = on && responses;
            Repaint();
        }

        /// <summary>
        /// A TN3270E message that is not 3270 data: SCS-DATA is printed; anything else is not
        /// printer data and is reported.
        /// </summary>
        /// <param name="kind">
        /// What it is.
        /// </param>
        /// <param name="data">
        /// Its data part.
        /// </param>
        public void Tn3270EHostMessageReceived(Tn3270EHostMessage kind, ReadOnlySpan<byte> data)
        {
            if (kind == Tn3270EHostMessage.ScsData)
            {
                _lastDataUtc = _utcNow();
                if (RefuseWhileNotReady(data.Length))
                {
                    return;
                }

                _printer.ProcessScs(data);
                Repaint();
                return;
            }

            // The printer asks for neither BIND-IMAGE nor SYSREQ, so the connection refuses those
            // messages before they get here; NVT text is basic TN3270E, which "refers to terminal
            // sessions only" (RFC 2355 section 9).
            OnProtocolError("TN3270E " + kind + " message on a printer session was not printed (" + data.Length.ToString(CultureInfo.InvariantCulture) + " bytes).");
            Repaint();
        }

        /// <summary>
        /// Shows the connection state; a closed connection ends the job in progress.
        /// </summary>
        /// <param name="status">
        /// The new state.
        /// </param>
        public void ConnectionStatusChanged(ConnectionStatus status)
        {
            _connection = status;
            if (status == ConnectionStatus.Disconnected || status == ConnectionStatus.Error)
            {
                EndJob("the connection closed");
            }

            Repaint();
        }

        /// <summary>
        /// The host sent the end-of-job signal (telnet Abort Output).
        /// </summary>
        public void EndOfJobReceived()
        {
            EndJob(_inTn3270E ? "the host (TN3270E PRINT-EOJ)" : "the host (telnet Abort Output)");
            Repaint();
        }

        /// <summary>
        /// Ends the job in progress when the host has been quiet for the quiet time. The app calls
        /// this about once a second on the session thread; a test calls it with the time it wants.
        /// </summary>
        /// <param name="utcNow">
        /// The time now, UTC.
        /// </param>
        /// <returns>
        /// True when a job was ended.
        /// </returns>
        public bool CheckQuietTime(DateTime utcNow)
        {
            if (_quietSeconds <= 0 || !_printer.HasOutput)
            {
                return false;
            }

            if (utcNow - _lastDataUtc < TimeSpan.FromSeconds(_quietSeconds))
            {
                return false;
            }

            EndJob(_quietSeconds.ToString(CultureInfo.InvariantCulture) + " seconds with no data");
            Repaint();
            return true;
        }

        private void EndJob(string endedBy)
        {
            PrintJob? job = _printer.TakeJob();
            if (job == null)
            {
                return;
            }

            _jobNumber++;
            DateTime local = _utcNow().ToLocalTime();
            _jobs.Add(new PrintedJob(_jobNumber, local, job.PageCount, "waiting", false, endedBy));
            _held.Add(new HeldJob(job, _jobs.Count - 1));

            // A job that ends while others wait goes behind them, so the files keep their order.
            if (_held.Count == 1)
            {
                WriteHeldJobs();
            }
            else
            {
                _jobs[_jobs.Count - 1] = _jobs[_jobs.Count - 1] with { Result = "waiting: the printer is not ready", Failed = true };
            }
        }

        /// <summary>
        /// A printer that is not ready tries its waiting jobs again, at most every
        /// <see cref="NotReadyRetrySeconds"/> seconds. The app calls this about once a second on
        /// the session thread, with <see cref="CheckQuietTime"/>; a test calls it with the time it
        /// wants.
        /// </summary>
        /// <param name="utcNow">
        /// The time now, UTC.
        /// </param>
        /// <returns>
        /// True when the printer became ready.
        /// </returns>
        public bool RetryWhenNotReady(DateTime utcNow)
        {
            if (_held.Count == 0 || utcNow < _nextRetryUtc)
            {
                return false;
            }

            bool ready = WriteHeldJobs();
            Repaint();
            return ready;
        }

        // Writes the waiting jobs in order and stops at the first that fails; that failure makes
        // (or keeps) the printer not ready. True when the printer is ready afterwards.
        private bool WriteHeldJobs()
        {
            bool wasNotReady = _notReadyReason != null;
            while (_held.Count > 0)
            {
                HeldJob held = _held[0];
                PrintedJob listed = _jobs[held.ListIndex];
                try
                {
                    string result = _output.Write(held.Job, listed.Number, listed.LocalTime);
                    _jobs[held.ListIndex] = listed with { Result = result, Failed = false };
                    _held.RemoveAt(0);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                    || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
                {
                    // Not swallowed: the job is listed as failed with the reason, the reason is
                    // the screen's "last problem", it goes to the log, and the printer is not
                    // ready until the job is written.
                    _jobs[held.ListIndex] = listed with { Result = ex.Message, Failed = true };
                    _notReadyReason = "job " + listed.Number.ToString(CultureInfo.InvariantCulture) + " was not written: " + ex.Message;
                    _nextRetryUtc = _utcNow() + TimeSpan.FromSeconds(NotReadyRetrySeconds);
                    OnProtocolError(_notReadyReason + " (printer not ready; tried again every "
                        + NotReadyRetrySeconds.ToString(CultureInfo.InvariantCulture) + " s)");
                    return false;
                }
            }

            _notReadyReason = null;
            if (wasNotReady)
            {
                _lastProblem = null;
                ApplicationLogger.Log(LogCategory.Network, LogLevel.Info, "Ibm3287Printer", "printer ready again: every waiting job is written");
                if (_inTn3270E && _responses)
                {
                    // The connection sends REQUEST ERR-COND-CLEARED if it told the host
                    // Intervention Required (RFC 2355 10.4.1).
                    Tn3270EKeyPressed?.Invoke(Tn3270EKey.ErrorConditionCleared);
                }
            }

            return true;
        }

        // A job not written yet, and where it is listed in _jobs.
        private readonly record struct HeldJob(PrintJob Job, int ListIndex);

        private void OnProtocolError(string message)
        {
            _lastProblem = message;
            ApplicationLogger.Log(LogCategory.Network, LogLevel.Error, "Ibm3287Printer", message);
        }

        private void Repaint()
        {
            BeginBatch();
            try
            {
                Paint();
                OnInvalidated();
            }
            finally
            {
                EndBatch();
            }
        }

        private void Paint()
        {
            for (int row = 0; row < ScreenRows; row++)
            {
                PutLine(row, string.Empty);
            }

            PutLine(0, "IBM 3287 printer   terminal type " + TerminalType + (_inTn3270E
                ? ", TN3270E" + (_luName != null ? ", LU " + _luName : string.Empty)
                : ", plain TN3270"));
            PutLine(2, "Connection: " + ConnectionText(_connection));
            PutLine(3, "State:      " + StateText());
            PutLine(4, "Output:     " + KeepEnd(_output.Description, ScreenColumns - 12));
            PutLine(5, "Job end:    " + (_inTn3270E ? "PRINT-EOJ" : "telnet Abort Output") + ", the connection closing" + (_quietSeconds > 0
                ? ", or " + _quietSeconds.ToString(CultureInfo.InvariantCulture) + " s with no data"
                : " (no quiet time)"));
            PutLine(7, "Problem:    " + (_lastProblem ?? "none"));
            PutLine(9, "Jobs printed: " + _jobs.Count.ToString(CultureInfo.InvariantCulture));

            // The newest jobs that fit, oldest of them at the top.
            int room = ScreenRows - JobListRow;
            int first = _jobs.Count > room ? _jobs.Count - room : 0;
            for (int i = first; i < _jobs.Count; i++)
            {
                PrintedJob job = _jobs[i];
                string head = job.Number.ToString(CultureInfo.InvariantCulture).PadLeft(4) + "  "
                    + job.LocalTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  "
                    + job.Pages.ToString(CultureInfo.InvariantCulture).PadLeft(3) + (job.Pages == 1 ? " page   " : " pages  ")
                    + (job.Failed ? "FAILED: " : string.Empty);
                PutLine(JobListRow + i - first, head + KeepEnd(job.Result, ScreenColumns - head.Length));
            }
        }

        private string StateText()
        {
            if (_held.Count > 0)
            {
                // Before the connection: a job waits to be written whether or not the host is there.
                return "NOT READY, " + _held.Count.ToString(CultureInfo.InvariantCulture) + (_held.Count == 1 ? " job" : " jobs")
                    + " waiting; tried again every " + NotReadyRetrySeconds.ToString(CultureInfo.InvariantCulture) + " s";
            }

            if (_connection != ConnectionStatus.Connected)
            {
                return "not printing";
            }

            if (_printer.HasOutput)
            {
                return "printing a job (" + _printer.PendingBytes.ToString(CultureInfo.InvariantCulture) + " bytes so far)";
            }

            if (_printer.PendingBytes > 0)
            {
                return "data received (" + _printer.PendingBytes.ToString(CultureInfo.InvariantCulture) + " bytes), no Start Print yet";
            }

            return "waiting for print data";
        }

        private static string ConnectionText(ConnectionStatus status) => status switch
        {
            ConnectionStatus.Connected => "connected",
            ConnectionStatus.Connecting => "connecting",
            ConnectionStatus.Error => "failed",
            _ => "not connected",
        };

        // A long path keeps its end (the file name), with "..." in front.
        private static string KeepEnd(string text, int width)
        {
            if (width <= 3 || text.Length <= width)
            {
                return text;
            }

            return "..." + text.Substring(text.Length - (width - 3));
        }

        private void PutLine(int row, string text)
        {
            for (int column = 0; column < ScreenColumns; column++)
            {
                char c = column < text.Length ? text[column] : ' ';
                Buffer.SetCell(row, column, new TerminalCell(c, CharacterAttributes.None, Ink, Paper));
            }
        }
    }
}
