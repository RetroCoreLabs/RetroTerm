//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Globalization;
using System.Threading.Tasks;

using RetroTerm.Core.Commands;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Core.Configuration
{
    /// <summary>
    /// The two 3270 choices a script or an MCP call can make for a connection - which IBM 3278
    /// model, and which EBCDIC code page - declared once for CONNECT, CONNSAVE and terminal_open.
    /// </summary>
    /// <remarks>
    /// The allowed words come from the tables the Manage Connections window uses:
    /// ConnectionFactory.Ibm3278Models (the size box's models) and
    /// ConnectionFactory.Ibm3270CodePageNumbers (RetroIO's CodePage.All, the code page box).
    /// The screen size of a 3278 is its model (RFC 1576 section 4), so the model is also the
    /// screen-size choice; width and height do not apply to one. A screen LARGER than the model's
    /// is the screen_size argument (<see cref="CheckScreenSize"/>, 24 November 2026), held to the
    /// emulator's own limits; the host is then told IBM-DYNAMIC (RFC 2355 section 7.1).
    /// </remarks>
    public static class Ibm3270ConnectionSettings
    {
        /// <summary>
        /// The name of the model argument.
        /// </summary>
        public const string ModelArgument = "model";

        /// <summary>
        /// The name of the code page argument.
        /// </summary>
        public const string CodePageArgument = "codepage";

        /// <summary>
        /// The model argument, with the four 3278 models as its words.
        /// </summary>
        /// <returns>
        /// A new parameter description.
        /// </returns>
        public static CommandParameter ModelParameter()
        {
            return new CommandParameter(ModelArgument, CommandParameterType.String, required: false, defaultValue: null,
                "TN3270 only: the IBM 3278 model, which also fixes the screen - IBM-3278-2 (80x24), "
                + "IBM-3278-3 (80x32), IBM-3278-4 (80x43) or IBM-3278-5 (132x27); the default is IBM-3278-2",
                allowedValues: ConnectionFactory.Ibm3278Models);
        }

        /// <summary>
        /// The code page argument, with every base code page RetroIO knows as its words.
        /// </summary>
        /// <returns>
        /// A new parameter description.
        /// </returns>
        public static CommandParameter CodePageParameter()
        {
            return new CommandParameter(CodePageArgument, CommandParameterType.String, required: false, defaultValue: null,
                "TN3270 only: the EBCDIC code page by its IBM number (277 for Denmark and Norway); "
                + "the default is 37 (USA, Canada). One of: " + string.Join(", ", ConnectionFactory.Ibm3270CodePageNumbers),
                allowedValues: ConnectionFactory.Ibm3270CodePageNumbers);
        }

        /// <summary>
        /// The name of the screen size argument.
        /// </summary>
        public const string ScreenSizeArgument = "screen_size";

        /// <summary>
        /// The screen size argument: a 3278 screen larger than its model's, as columns x rows.
        /// </summary>
        /// <returns>
        /// A new parameter description.
        /// </returns>
        public static CommandParameter ScreenSizeParameter()
        {
            return new CommandParameter(ScreenSizeArgument, CommandParameterType.String, required: false, defaultValue: null,
                "TN3270 display only: a screen larger than the model's, as columns x rows, such as 100x50; "
                + "the host is then told IBM-DYNAMIC and reads the size from the Query Reply. Each side at least "
                + "the model's, at most " + MaximumScreenCells.ToString(CultureInfo.InvariantCulture)
                + " cells; empty or the model's own size means the model's own screen (the default)");
        }

        // The emulator's cell limit, Ibm3270Emulator.MaximumCells (14-bit buffer addresses), for
        // the help text only; the check itself is the emulator's (ConnectionFactory.CheckIbm3270ScreenSize).
        private const int MaximumScreenCells = 0x3FFF;

        /// <summary>
        /// Checks a screen size against the protocol, the terminal and the emulator's own limits
        /// (ConnectionFactory.CheckIbm3270ScreenSize, which asks Ibm3270Emulator.CheckScreenSize),
        /// and works out what to store. The one rule for terminal_open, CONNECT and CONNSAVE.
        /// </summary>
        /// <param name="protocol">
        /// The protocol the connection uses.
        /// </param>
        /// <param name="terminal">
        /// The terminal the connection uses (a 3278 model, or IBM-3287-1), or null for the
        /// protocol's default, the model 2.
        /// </param>
        /// <param name="text">
        /// The screen_size argument, or null when it was not given.
        /// </param>
        /// <param name="screenSize">
        /// What to store: such as "100x50", or null for the model's own screen (the argument was
        /// empty, or the model's own size).
        /// </param>
        /// <returns>
        /// Null when it fits, else what is wrong, naming the argument.
        /// </returns>
        public static string? CheckScreenSize(ConnectionFactory.ProtocolType protocol, string? terminal, string? text, out string? screenSize)
        {
            screenSize = null;
            if (text == null)
            {
                return null;
            }

            if (protocol != ConnectionFactory.ProtocolType.TN3270)
            {
                return "'" + ScreenSizeArgument + "' only applies with protocol=tn3270";
            }

            if (string.Equals(terminal, ConnectionFactory.Ibm3287, StringComparison.OrdinalIgnoreCase))
            {
                return "'" + ScreenSizeArgument + "' is for a 3278 display, not the " + ConnectionFactory.Ibm3287 + " printer";
            }

            if (text.Trim().Length == 0)
            {
                return null;
            }

            string model = ConnectionFactory.EmulatorTypeFor(ConnectionFactory.ProtocolType.TN3270, terminal);
            int modelNumber = model[model.Length - 1] - '0';
            string? problem = ConnectionFactory.CheckIbm3270ScreenSize(modelNumber, text, out int columns, out int rows);
            if (problem != null)
            {
                return "'" + ScreenSizeArgument + "' " + text.Trim() + " for " + model + ": " + problem;
            }

            string size = TerminalSizeChoices.Format(columns, rows);
            screenSize = size == ModelScreen(model) ? null : size;
            return null;
        }

        /// <summary>
        /// A tab's 3278 written the way a connection stores it: the model's name and, when the
        /// screen is larger than the model's, that screen. CONNECT keeps the tab's terminal, so
        /// this is what it tells the host and what a model or screen_size it is given must match.
        /// </summary>
        /// <param name="terminal">
        /// The tab's terminal.
        /// </param>
        /// <param name="model">
        /// Such as "IBM-3278-2".
        /// </param>
        /// <param name="screenSize">
        /// Such as "100x50", or null for the model's own screen.
        /// </param>
        public static void Describe(IModelScreenTerminal terminal, out string model, out string? screenSize)
        {
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));
            model = "IBM-3278-" + terminal.Model.ToString(CultureInfo.InvariantCulture);
            screenSize = terminal.IsOversize ? TerminalSizeChoices.Format(terminal.AlternateScreenColumns, terminal.AlternateScreenRows) : null;
        }

        /// <summary>
        /// A screen as a person reads it: "100x50" or "80x24 (the model's own)".
        /// </summary>
        /// <param name="model">
        /// The model, such as "IBM-3278-2".
        /// </param>
        /// <param name="screenSize">
        /// The larger screen, or null for the model's own.
        /// </param>
        /// <returns>
        /// The text.
        /// </returns>
        public static string ScreenText(string model, string? screenSize)
        {
            return screenSize ?? ModelScreen(model) + " (the model's own)";
        }

        // A 3278 model's own screen, columns x rows, from the model table.
        private static string ModelScreen(string model)
        {
            int index = Array.IndexOf(ConnectionFactory.Ibm3278Models, model);
            return index < 0 ? string.Empty : ConnectionFactory.Ibm3278ModelSizes[index];
        }

        /// <summary>
        /// Checks a model and a code page against the protocol and the chosen terminal, and
        /// works out what they mean.
        /// </summary>
        /// <param name="protocol">
        /// The protocol the connection will use.
        /// </param>
        /// <param name="emulator">
        /// The terminal named with the emulator argument, or null when none was given.
        /// </param>
        /// <param name="model">
        /// The model argument, or null. Already one of the allowed words.
        /// </param>
        /// <param name="codePage">
        /// The code page argument, or null. Already one of the allowed words.
        /// </param>
        /// <param name="terminal">
        /// The terminal to use: the model in its own spelling when one was given, else the emulator.
        /// </param>
        /// <param name="codePageNumber">
        /// The code page as a number, or null when none was given.
        /// </param>
        /// <returns>
        /// Null when they fit together, else what is wrong, naming the argument.
        /// </returns>
        public static string? Check(ConnectionFactory.ProtocolType protocol, string? emulator, string? model, string? codePage,
            out string? terminal, out int? codePageNumber)
        {
            terminal = emulator;
            codePageNumber = null;
            if (model == null && codePage == null)
            {
                return null;
            }

            if (protocol != ConnectionFactory.ProtocolType.TN3270)
            {
                return "'" + (model != null ? ModelArgument : CodePageArgument) + "' only applies with protocol=tn3270";
            }

            if (model != null)
            {
                // The model names the terminal; an emulator argument that names another one is a
                // contradiction to refuse, not a choice to make for the caller.
                string spelled = ConnectionFactory.EmulatorTypeFor(ConnectionFactory.ProtocolType.TN3270, model);
                if (emulator != null && !string.Equals(emulator, spelled, StringComparison.OrdinalIgnoreCase))
                {
                    return "'" + ModelArgument + "' is " + spelled + " but 'emulator' is " + emulator + " - give one of them";
                }

                terminal = spelled;
            }

            if (codePage != null)
            {
                if (string.Equals(terminal, ConnectionFactory.Ibm3287, StringComparison.OrdinalIgnoreCase))
                {
                    return "'" + CodePageArgument + "' is for a 3278 display, not the " + ConnectionFactory.Ibm3287 + " printer";
                }

                codePageNumber = int.Parse(codePage, NumberStyles.None, CultureInfo.InvariantCulture);
            }

            return null;
        }

        /// <summary>
        /// Puts a connection's code page on a session's terminal, on the session's thread
        /// (SessionPump.cs, the thread rule), through the same ICodePageTerminal.SetCodePage the
        /// CODEPAGE command uses.
        /// </summary>
        /// <param name="session">
        /// The session.
        /// </param>
        /// <param name="codePage">
        /// The code page number, or null to leave the terminal on its own default (37).
        /// </param>
        /// <returns>
        /// Null when it was set or there was nothing to set, else the terminal's reason for refusing.
        /// </returns>
        public static Task<string?> ApplyCodePageAsync(TerminalSession session, int? codePage)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (codePage is not int number)
            {
                return Task.FromResult<string?>(null);
            }

            if (session.Emulator is not ICodePageTerminal terminal)
            {
                return Task.FromResult<string?>("this terminal (" + session.Emulator.GetType().Name + ") has no EBCDIC code page");
            }

            return session.RunOnSessionThreadAsync(() => terminal.SetCodePage(number));
        }

        // ------------------------------------------------------------------------------------
        // The rest of the per-connection 3270 settings (PLAN.md, "Every 3270 feature reachable
        // over MCP"): what a double click does, the card reader SUBMIT sends to, and the 3287
        // printer's Printer tab. Declared here once, for CONNECT, CONNSAVE and terminal_open, with
        // the words and ranges the Manage Connections window takes.
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// The name of the double-click argument.
        /// </summary>
        public const string DoubleClickArgument = "doubleclick";

        /// <summary>
        /// The double-click word for moving the cursor there and pressing Enter (the default).
        /// </summary>
        public const string DoubleClickEnter = "enter";

        /// <summary>
        /// The double-click word for selecting the word under the pointer.
        /// </summary>
        public const string DoubleClickSelect = "select";

        /// <summary>
        /// The double-click words, in the order the window's double-click box lists its two
        /// entries (ManageConnectionsWindow.DoubleClickPressesEnterText, then DoubleClickSelectsWordText).
        /// </summary>
        public static readonly string[] DoubleClickWords = { DoubleClickEnter, DoubleClickSelect };

        /// <summary>
        /// The name of the card reader port argument.
        /// </summary>
        public const string ReaderPortArgument = "reader_port";

        /// <summary>
        /// The name of the card reader host argument.
        /// </summary>
        public const string ReaderHostArgument = "reader_host";

        /// <summary>
        /// The name of the printer output argument.
        /// </summary>
        public const string PrinterOutputArgument = "printer_output";

        /// <summary>
        /// The name of the printer folder argument.
        /// </summary>
        public const string PrinterFolderArgument = "printer_folder";

        /// <summary>
        /// The name of the argument that deletes the PDF after printing.
        /// </summary>
        public const string PrinterDeletePdfArgument = "printer_delete_pdf";

        /// <summary>
        /// The name of the argument for the seconds with no data that end a print job.
        /// </summary>
        public const string PrinterQuietSecondsArgument = "printer_quiet_seconds";

        /// <summary>
        /// The name of the PDF lines per page argument.
        /// </summary>
        public const string PrinterLinesPerPageArgument = "printer_lines_per_page";

        /// <summary>
        /// The name of the tractor paper argument.
        /// </summary>
        public const string PrinterTractorArgument = "printer_tractor";

        /// <summary>
        /// The name of the tractor paper band colour argument.
        /// </summary>
        public const string PrinterBandColourArgument = "printer_band_colour";

        /// <summary>
        /// The name of the argument naming the display LU whose partner printer TN3270E asks for.
        /// </summary>
        public const string PrinterAssociateArgument = "printer_associate";

        // RFC 2355 7.1.1: "The length of device-names and resource-names should not exceed 8
        // bytes", NVT ASCII.
        private const int LuNameLength = 8;

        // The highest TCP port; the window's card reader port box takes 1 to this.
        private const int HighestPort = 65535;

        /// <summary>
        /// The settings that work on a tab that is already open (CONNECT may give them): the
        /// double click and the card reader.
        /// </summary>
        /// <returns>
        /// New parameter descriptions.
        /// </returns>
        public static CommandParameter[] TabParameters()
        {
            return new[]
            {
                new CommandParameter(DoubleClickArgument, CommandParameterType.String, required: false, defaultValue: null,
                    "TN3270 display only: what a double click does - enter (move the cursor there and press Enter, "
                    + "the default) or select (select the word)",
                    allowedValues: DoubleClickWords),
                new CommandParameter(ReaderPortArgument, CommandParameterType.Int, required: false, defaultValue: null,
                    "Telnet or TN3270: the TCP port of the host's card reader that SUBMIT sends a job to, 1 to 65535; "
                    + "0 means no card reader (the default)"),
                new CommandParameter(ReaderHostArgument, CommandParameterType.String, required: false, defaultValue: null,
                    "Telnet or TN3270: the card reader's host; empty means the connection's own host (the default)")
            };
        }

        /// <summary>
        /// The IBM 3287 printer settings, the window's Printer tab. CONNECT does not take them:
        /// see <see cref="PrinterArgumentNames"/>.
        /// </summary>
        /// <returns>
        /// New parameter descriptions.
        /// </returns>
        public static CommandParameter[] PrinterParameters()
        {
            return new[]
            {
                new CommandParameter(PrinterOutputArgument, CommandParameterType.String, required: false, defaultValue: null,
                    "IBM-3287-1 printer only: where each print job goes - Text (a text file, the default), PDF, "
                    + "or Printer (a PDF handed to the system's print command)",
                    allowedValues: ConnectionFactory.PrinterOutputs.Choices),
                new CommandParameter(PrinterFolderArgument, CommandParameterType.String, required: false, defaultValue: null,
                    "IBM-3287-1 printer only: the folder for the job files; empty means Documents\\RetroTerm\\Printer (the default)"),
                new CommandParameter(PrinterDeletePdfArgument, CommandParameterType.Bool, required: false, defaultValue: null,
                    "IBM-3287-1 printer only, with printer_output=Printer: true deletes the PDF once it has been "
                    + "handed to the printer; false keeps it (the default)"),
                new CommandParameter(PrinterQuietSecondsArgument, CommandParameterType.Int, required: false, defaultValue: null,
                    "IBM-3287-1 printer only: seconds with no data from the host that end a print job, 0 for never; "
                    + "the default is 5"),
                new CommandParameter(PrinterLinesPerPageArgument, CommandParameterType.Int, required: false, defaultValue: null,
                    "IBM-3287-1 printer only, PDF: start a new sheet after this many lines as well as on a form feed "
                    + "(TK5 needs 56); 0 means a new sheet only on a form feed (the default)"),
                new CommandParameter(PrinterTractorArgument, CommandParameterType.Bool, required: false, defaultValue: null,
                    "IBM-3287-1 printer only, PDF: true draws each sheet as tractor feed paper (bands and sprocket holes); "
                    + "false is plain paper (the default)"),
                new CommandParameter(PrinterBandColourArgument, CommandParameterType.String, required: false, defaultValue: null,
                    "IBM-3287-1 printer only, PDF with printer_tractor: the colour of the bands - Blue (the default) or Green",
                    allowedValues: ConnectionFactory.TractorPaperColours.Choices),
                new CommandParameter(PrinterAssociateArgument, CommandParameterType.String, required: false, defaultValue: null,
                    "IBM-3287-1 printer only, TN3270E: the LU name of a display (the name the server gave that display, "
                    + "shown on its line 25); the printer asks for that display's partner printer (RFC 2355 ASSOCIATE). "
                    + "1 to 8 letters or digits; empty asks for any printer (the default). Plain TN3270 hosts ignore it")
            };
        }

        /// <summary>
        /// The printer argument names. CONNECT does not declare them, so it refuses them as unknown:
        /// a 3287 tab builds where its jobs go when the tab opens (the desktop's TabSession, from
        /// the connection), and CONNECT, which keeps the tab it runs in, cannot rebuild that from
        /// this layer. Saying "connected" with the printer still writing to the old place would be
        /// a success that is not one. Save them with CONNSAVE, or give them to terminal_open.
        /// </summary>
        public static readonly string[] PrinterArgumentNames =
        {
            PrinterOutputArgument, PrinterFolderArgument, PrinterDeletePdfArgument, PrinterQuietSecondsArgument,
            PrinterLinesPerPageArgument, PrinterTractorArgument, PrinterBandColourArgument, PrinterAssociateArgument
        };

        /// <summary>
        /// Several parameter lists as one, in order.
        /// </summary>
        /// <param name="lists">
        /// The lists.
        /// </param>
        /// <returns>
        /// A new array holding every parameter of every list.
        /// </returns>
        public static CommandParameter[] Join(params CommandParameter[][] lists)
        {
            if (lists == null) throw new ArgumentNullException(nameof(lists));
            int count = 0;
            for (int i = 0; i < lists.Length; i++)
            {
                count += lists[i].Length;
            }

            var all = new CommandParameter[count];
            int at = 0;
            for (int i = 0; i < lists.Length; i++)
            {
                Array.Copy(lists[i], 0, all, at, lists[i].Length);
                at += lists[i].Length;
            }

            return all;
        }

        /// <summary>
        /// Reads and checks the double-click, card reader and printer arguments: the ranges the
        /// window's boxes take, and whether each one fits the protocol and the terminal.
        /// </summary>
        /// <param name="args">
        /// The arguments. Types and words are already checked against the declarations.
        /// </param>
        /// <param name="protocol">
        /// The protocol the connection uses.
        /// </param>
        /// <param name="terminal">
        /// The terminal the connection uses (IBM-3287-1 for a printer), or null when not known.
        /// </param>
        /// <param name="choices">
        /// What was given, for <see cref="Ibm3270Choices.ApplyTo(HostConfiguration)"/> and
        /// <see cref="Ibm3270Choices.ApplyTo(ConnectionFactory.ConnectionParameters)"/>.
        /// </param>
        /// <returns>
        /// Null when every argument fits, else what is wrong, naming the argument.
        /// </returns>
        public static string? ReadChoices(CommandArgs args, ConnectionFactory.ProtocolType protocol, string? terminal,
            out Ibm3270Choices choices)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            choices = new Ibm3270Choices();
            bool isTn3270 = protocol == ConnectionFactory.ProtocolType.TN3270;
            bool isPrinter = isTn3270 && string.Equals(terminal, ConnectionFactory.Ibm3287, StringComparison.OrdinalIgnoreCase);

            if (args.Contains(DoubleClickArgument))
            {
                if (!isTn3270)
                {
                    return "'" + DoubleClickArgument + "' only applies with protocol=tn3270";
                }
                if (isPrinter)
                {
                    return "'" + DoubleClickArgument + "' is for a 3278 display, not the " + ConnectionFactory.Ibm3287 + " printer";
                }

                choices.DoubleClickGiven = true;

                // Stored the way the window's box stores it: the default (press Enter) is null.
                choices.DoubleClickPressesEnter = string.Equals(args.GetString(DoubleClickArgument), DoubleClickSelect, StringComparison.OrdinalIgnoreCase)
                    ? false
                    : null;
            }

            // The window shows the card reader boxes on a Telnet and a TN3270 connection only.
            if (args.Contains(ReaderPortArgument) || args.Contains(ReaderHostArgument))
            {
                if (protocol != ConnectionFactory.ProtocolType.Telnet && !isTn3270)
                {
                    return "'" + (args.Contains(ReaderPortArgument) ? ReaderPortArgument : ReaderHostArgument)
                        + "' only applies with protocol=telnet or tn3270";
                }
            }

            if (args.Contains(ReaderPortArgument))
            {
                int port = args.GetInt(ReaderPortArgument, -1);
                if (port < 0 || port > HighestPort)
                {
                    return "'" + ReaderPortArgument + "' must be 1 to " + HighestPort.ToString(CultureInfo.InvariantCulture)
                        + ", or 0 for no card reader, got " + args.GetString(ReaderPortArgument);
                }

                choices.ReaderPortGiven = true;
                choices.ReaderPort = port == 0 ? null : port;
            }

            if (args.Contains(ReaderHostArgument))
            {
                string? host = args.GetString(ReaderHostArgument);
                choices.ReaderHostGiven = true;
                choices.ReaderHost = string.IsNullOrWhiteSpace(host) ? null : host!.Trim();
            }

            for (int i = 0; i < PrinterArgumentNames.Length; i++)
            {
                if (!args.Contains(PrinterArgumentNames[i]))
                {
                    continue;
                }
                if (!isTn3270)
                {
                    return "'" + PrinterArgumentNames[i] + "' only applies with protocol=tn3270";
                }
                if (!isPrinter)
                {
                    return "'" + PrinterArgumentNames[i] + "' is for the " + ConnectionFactory.Ibm3287 + " printer, and this "
                        + "connection's terminal is " + (terminal ?? "not a printer") + " - give emulator=" + ConnectionFactory.Ibm3287;
                }
            }

            // Every printer value is stored the way the window's Printer tab stores it, so a
            // connection saved here and one set up in the window are the same file: the first
            // entry of a box, an unticked box and an empty text box are all null.
            if (args.Contains(PrinterOutputArgument))
            {
                string output = ConnectionFactory.PrinterOutputs.Normalise(args.GetString(PrinterOutputArgument));
                choices.PrinterOutputGiven = true;
                choices.PrinterOutput = output == ConnectionFactory.PrinterOutputs.Text ? null : output;
            }

            if (args.Contains(PrinterFolderArgument))
            {
                string? folder = args.GetString(PrinterFolderArgument);
                choices.PrinterFolderGiven = true;
                choices.PrinterFolder = string.IsNullOrWhiteSpace(folder) ? null : folder!.Trim();
            }

            if (args.Contains(PrinterDeletePdfArgument))
            {
                choices.PrinterDeletePdfGiven = true;
                choices.PrinterDeletePdf = args.GetBool(PrinterDeletePdfArgument, false) ? true : null;
            }

            if (args.Contains(PrinterQuietSecondsArgument))
            {
                // The window's box takes a whole number with no sign: 0 and up.
                int seconds = args.GetInt(PrinterQuietSecondsArgument, -1);
                if (seconds < 0)
                {
                    return "'" + PrinterQuietSecondsArgument + "' must be 0 (never) or more seconds, got "
                        + args.GetString(PrinterQuietSecondsArgument);
                }

                choices.PrinterQuietSecondsGiven = true;
                choices.PrinterQuietSeconds = seconds;
            }

            if (args.Contains(PrinterLinesPerPageArgument))
            {
                // The window's box takes 1 and up, and empty for none; 0 stands for the empty box.
                int lines = args.GetInt(PrinterLinesPerPageArgument, -1);
                if (lines < 0)
                {
                    return "'" + PrinterLinesPerPageArgument + "' must be 1 or more lines, or 0 for a new sheet only on a form feed, got "
                        + args.GetString(PrinterLinesPerPageArgument);
                }

                choices.PrinterLinesPerPageGiven = true;
                choices.PrinterLinesPerPage = lines == 0 ? null : lines;
            }

            if (args.Contains(PrinterTractorArgument))
            {
                choices.PrinterTractorGiven = true;
                choices.PrinterTractorPaper = args.GetBool(PrinterTractorArgument, false) ? true : null;
            }

            if (args.Contains(PrinterBandColourArgument))
            {
                string colour = ConnectionFactory.TractorPaperColours.Normalise(args.GetString(PrinterBandColourArgument));
                choices.PrinterBandColourGiven = true;
                choices.PrinterTractorColour = colour == ConnectionFactory.TractorPaperColours.Blue ? null : colour;
            }

            if (args.Contains(PrinterAssociateArgument))
            {
                string? lu = args.GetString(PrinterAssociateArgument);
                lu = string.IsNullOrWhiteSpace(lu) ? null : lu!.Trim();
                if (lu != null)
                {
                    if (lu.Length > LuNameLength)
                    {
                        return "'" + PrinterAssociateArgument + "' is an LU name of at most 8 characters (RFC 2355 section 7.1.1), got " + lu;
                    }

                    for (int i = 0; i < lu.Length; i++)
                    {
                        char c = lu[i];
                        bool letterOrDigit = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                        if (!letterOrDigit && c != '@' && c != '#' && c != '$')
                        {
                            // Letters, digits and the three national characters: what an SNA
                            // network name is made of (GA23-0061-2 does not hold the rule; the
                            // RFC says only "NVT ASCII"). A space or a control byte would end or
                            // break the subnegotiation.
                            return "'" + PrinterAssociateArgument + "' may hold letters, digits, @, # and $ only, got " + lu;
                        }
                    }
                }

                choices.PrinterAssociateGiven = true;
                choices.PrinterAssociate = lu;
            }

            return null;
        }

        /// <summary>
        /// Puts a connection's double click and card reader on a session, by the one rule every way
        /// of opening uses: the desktop tab (TabSession.LastConnectionParameters), the headless host
        /// and CONNECT. No card reader port means no reader, and SUBMIT refuses; no double-click
        /// choice means the terminal's default, press Enter.
        /// </summary>
        /// <param name="session">
        /// The session.
        /// </param>
        /// <param name="parameters">
        /// The connection, or null.
        /// </param>
        /// <remarks>
        /// The double-click choice is a plain flag the canvas reads on the UI thread; it is not
        /// screen or buffer state, so it is set here directly, as the desktop tab always has, and
        /// not posted to the session thread.
        /// </remarks>
        public static void ApplyToSession(TerminalSession session, ConnectionFactory.ConnectionParameters? parameters)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            (session.CardReaderHost, session.CardReaderPort) = ConnectionFactory.CardReaderAddress(parameters);
            if (session.Emulator is IPointerCursorTerminal pointer)
            {
                pointer.DoubleClickPressesEnter = parameters?.DoubleClickPressesEnter ?? true;
            }
        }

        /// <summary>
        /// Writes the double click, card reader and printer settings of a stored connection, the
        /// lines CONNSHOW prints, each only where it means something.
        /// </summary>
        /// <param name="config">
        /// The stored connection.
        /// </param>
        /// <param name="sb">
        /// Where to write.
        /// </param>
        public static void Describe(HostConfiguration config, System.Text.StringBuilder sb)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (sb == null) throw new ArgumentNullException(nameof(sb));
            var protocol = ConnectionFactory.ParseProtocolType(config.Protocol);
            bool isTn3270 = protocol == ConnectionFactory.ProtocolType.TN3270;
            bool isPrinter = isTn3270 && string.Equals(config.EmulatorType, ConnectionFactory.Ibm3287, StringComparison.OrdinalIgnoreCase);

            if (isTn3270 && !isPrinter)
            {
                // The screen the host gets on Erase/Write Alternate, and the name it is told.
                string model = ConnectionFactory.EmulatorTypeFor(ConnectionFactory.ProtocolType.TN3270, config.EmulatorType);
                if (string.IsNullOrWhiteSpace(config.Ibm3270ScreenSize))
                {
                    sb.Append("screen: ").Append(ModelScreen(model)).Append(" (the model's own)\n");
                }
                else
                {
                    sb.Append("screen: ").Append(config.Ibm3270ScreenSize!.Trim())
                      .Append(" (larger than the model's ").Append(ModelScreen(model)).Append(", announced as IBM-DYNAMIC)\n");
                }

                sb.Append("double click: ")
                  .Append(config.DoubleClickPressesEnter == false ? DoubleClickSelect + " (select the word)" : DoubleClickEnter + " (move the cursor there and press Enter)")
                  .Append('\n');
            }

            if (protocol == ConnectionFactory.ProtocolType.Telnet || isTn3270)
            {
                sb.Append("card reader: ");
                var (host, port) = ConnectionFactory.CardReaderAddress(config.ToConnectionParameters());
                if (port is int p)
                {
                    sb.Append(host).Append(':').Append(p.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append("none");
                }
                sb.Append('\n');
            }

            if (isPrinter)
            {
                sb.Append("printer output: ").Append(ConnectionFactory.PrinterOutputs.Normalise(config.PrinterOutput)).Append('\n');
                sb.Append("printer folder: ").Append(config.PrinterFolder ?? "the default (Documents\\RetroTerm\\Printer)").Append('\n');
                sb.Append("delete the PDF after printing: ").Append(config.PrinterDeletePdf == true ? "yes" : "no").Append('\n');
                sb.Append("seconds with no data that end a job: ")
                  .Append(config.PrinterQuietSeconds?.ToString(CultureInfo.InvariantCulture) ?? "the printer's default (5)").Append('\n');
                sb.Append("PDF lines per page: ")
                  .Append(config.PrinterLinesPerPage?.ToString(CultureInfo.InvariantCulture) ?? "none (a new sheet only on a form feed)").Append('\n');
                sb.Append("tractor paper: ").Append(config.PrinterTractorPaper == true ? "yes" : "no").Append('\n');
                sb.Append("band colour: ").Append(ConnectionFactory.TractorPaperColours.Normalise(config.PrinterTractorColour)).Append('\n');
                sb.Append("TN3270E partner printer of display: ").Append(config.PrinterAssociate ?? "none (any printer)").Append('\n');
            }
        }
    }

    /// <summary>
    /// The double click, card reader and printer arguments one call gave, read and checked by
    /// <see cref="Ibm3270ConnectionSettings.ReadChoices"/>. Each value is already in the form the
    /// window stores (null for the default), and each has a flag saying whether it was given, so
    /// a CONNSAVE update changes only what it was told to.
    /// </summary>
    public sealed class Ibm3270Choices
    {
        /// <summary>
        /// Whether doubleclick was given.
        /// </summary>
        public bool DoubleClickGiven { get; set; }

        /// <summary>
        /// False for select the word; null for the default, press Enter.
        /// </summary>
        public bool? DoubleClickPressesEnter { get; set; }

        /// <summary>
        /// Whether reader_port was given.
        /// </summary>
        public bool ReaderPortGiven { get; set; }

        /// <summary>
        /// The card reader port, or null for none.
        /// </summary>
        public int? ReaderPort { get; set; }

        /// <summary>
        /// Whether reader_host was given.
        /// </summary>
        public bool ReaderHostGiven { get; set; }

        /// <summary>
        /// The card reader host, or null for the connection's own.
        /// </summary>
        public string? ReaderHost { get; set; }

        /// <summary>
        /// Whether printer_output was given.
        /// </summary>
        public bool PrinterOutputGiven { get; set; }

        /// <summary>
        /// PDF or Printer, or null for a text file.
        /// </summary>
        public string? PrinterOutput { get; set; }

        /// <summary>
        /// Whether printer_folder was given.
        /// </summary>
        public bool PrinterFolderGiven { get; set; }

        /// <summary>
        /// The job folder, or null for the default.
        /// </summary>
        public string? PrinterFolder { get; set; }

        /// <summary>
        /// Whether printer_delete_pdf was given.
        /// </summary>
        public bool PrinterDeletePdfGiven { get; set; }

        /// <summary>
        /// True to delete the PDF after printing, null to keep it.
        /// </summary>
        public bool? PrinterDeletePdf { get; set; }

        /// <summary>
        /// Whether printer_quiet_seconds was given.
        /// </summary>
        public bool PrinterQuietSecondsGiven { get; set; }

        /// <summary>
        /// The seconds with no data that end a job.
        /// </summary>
        public int? PrinterQuietSeconds { get; set; }

        /// <summary>
        /// Whether printer_lines_per_page was given.
        /// </summary>
        public bool PrinterLinesPerPageGiven { get; set; }

        /// <summary>
        /// The PDF lines per page, or null for a new sheet only on a form feed.
        /// </summary>
        public int? PrinterLinesPerPage { get; set; }

        /// <summary>
        /// Whether printer_tractor was given.
        /// </summary>
        public bool PrinterTractorGiven { get; set; }

        /// <summary>
        /// True for tractor feed paper, null for plain.
        /// </summary>
        public bool? PrinterTractorPaper { get; set; }

        /// <summary>
        /// Whether printer_band_colour was given.
        /// </summary>
        public bool PrinterBandColourGiven { get; set; }

        /// <summary>
        /// Green, or null for blue.
        /// </summary>
        public string? PrinterTractorColour { get; set; }

        /// <summary>
        /// Whether printer_associate was given.
        /// </summary>
        public bool PrinterAssociateGiven { get; set; }

        /// <summary>
        /// The display LU for TN3270E ASSOCIATE, or null for any printer.
        /// </summary>
        public string? PrinterAssociate { get; set; }

        /// <summary>
        /// Puts what was given on a stored connection, leaving the rest as it is (CONNSAVE).
        /// </summary>
        /// <param name="config">
        /// The stored connection.
        /// </param>
        public void ApplyTo(HostConfiguration config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (DoubleClickGiven) config.DoubleClickPressesEnter = DoubleClickPressesEnter;
            if (ReaderPortGiven) config.CardReaderPort = ReaderPort;
            if (ReaderHostGiven) config.CardReaderHost = ReaderHost;
            if (PrinterOutputGiven) config.PrinterOutput = PrinterOutput;
            if (PrinterFolderGiven) config.PrinterFolder = PrinterFolder;
            if (PrinterDeletePdfGiven) config.PrinterDeletePdf = PrinterDeletePdf;
            if (PrinterQuietSecondsGiven) config.PrinterQuietSeconds = PrinterQuietSeconds;
            if (PrinterLinesPerPageGiven) config.PrinterLinesPerPage = PrinterLinesPerPage;
            if (PrinterTractorGiven) config.PrinterTractorPaper = PrinterTractorPaper;
            if (PrinterBandColourGiven) config.PrinterTractorColour = PrinterTractorColour;
            if (PrinterAssociateGiven) config.PrinterAssociate = PrinterAssociate;
        }

        /// <summary>
        /// Puts the values on a connection about to be opened (terminal_open, CONNECT). What was
        /// not given stays null, the default.
        /// </summary>
        /// <param name="parameters">
        /// The connection.
        /// </param>
        public void ApplyTo(ConnectionFactory.ConnectionParameters parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            parameters.DoubleClickPressesEnter = DoubleClickPressesEnter;
            parameters.CardReaderPort = ReaderPort;
            parameters.CardReaderHost = ReaderHost;
            parameters.PrinterOutput = PrinterOutput;
            parameters.PrinterFolder = PrinterFolder;
            parameters.PrinterDeletePdf = PrinterDeletePdf;
            parameters.PrinterQuietSeconds = PrinterQuietSeconds;
            parameters.PrinterLinesPerPage = PrinterLinesPerPage;
            parameters.PrinterTractorPaper = PrinterTractorPaper;
            parameters.PrinterTractorColour = PrinterTractorColour;
            parameters.PrinterAssociate = PrinterAssociate;
        }
    }
}
