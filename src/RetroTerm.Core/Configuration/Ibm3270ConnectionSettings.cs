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
    /// screen-size choice; width and height do not apply to one.
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
    }
}
