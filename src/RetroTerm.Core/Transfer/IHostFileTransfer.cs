//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.IO;

namespace RetroTerm.Core.Transfer
{
    /// <summary>
    /// The end of a host file transfer: the host's closing message, or why the PC gave up.
    /// </summary>
    /// <param name="success">
    /// True when the file arrived whole.
    /// </param>
    /// <param name="message">
    /// The host's message (for IND$FILE, for example "TRANS03 File transfer complete"), or the
    /// reason the transfer ended.
    /// </param>
    public delegate void HostFileTransferFinished(bool success, string message);

    /// <summary>
    /// A terminal that moves files with a program on the host which talks to it over the screen
    /// connection itself: IND$FILE on an IBM 3270. The transfer is started by typing the host
    /// program's command, and it ends when the host says so.
    /// </summary>
    /// <remarks>
    /// Called on the session's pump thread (SessionPump.cs, the thread rule), and the finished
    /// event is raised on it.
    /// </remarks>
    public interface IHostFileTransfer
    {
        /// <summary>
        /// Raised once per started transfer, when it ends.
        /// </summary>
        event HostFileTransferFinished? HostFileTransferFinished;

        /// <summary>
        /// Gets ready to move one file and types the host command that starts it, then Enter.
        /// </summary>
        /// <param name="upload">
        /// True to send the file to the host, false to receive one from it.
        /// </param>
        /// <param name="file">
        /// Where the bytes come from or go to. The terminal does not close it.
        /// </param>
        /// <param name="command">
        /// The host command, typed as it stands, for example "IND$FILE GET HELLO.TEXT ASCII CRLF".
        /// </param>
        /// <returns>
        /// Null when the command was sent, or the reason nothing was started.
        /// </returns>
        string? StartHostFileTransfer(bool upload, Stream file, string command);

        /// <summary>
        /// Gives up on a transfer the host has not started yet. A transfer the host has opened is
        /// left to end with the host's message.
        /// </summary>
        void CancelHostFileTransfer();
    }
}
