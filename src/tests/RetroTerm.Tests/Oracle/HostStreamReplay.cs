//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace RetroTerm.Tests.Oracle
{
    /// <summary>
    /// What one replay of a recording to one client gave: the screen after every host record,
    /// and every unit the client sent.
    /// </summary>
    internal sealed class ReplayResult
    {
        public List<string> Labels { get; } = new List<string>();

        public List<OracleScreen> Screens { get; } = new List<OracleScreen>();

        public List<byte[]> Records { get; set; } = new List<byte[]>();

        public List<string> Telnet { get; set; } = new List<string>();

        /// <summary>
        /// What each IND$FILE transfer gave, in order (<see cref="IOracleClient.FinishTransferAsync"/>):
        /// for a download, the bytes of the PC file the client wrote.
        /// </summary>
        public List<string> Transfers { get; } = new List<string>();

        /// <summary>
        /// Places where the client sent a record whose AID is not the one the recording client
        /// sent at that point: the two have gone out of step with the recording.
        /// </summary>
        public List<string> OutOfStep { get; } = new List<string>();
    }

    /// <summary>
    /// Plays a <see cref="HostRecording"/> to one client through a <see cref="ReplayHost"/>, in
    /// lock step: each host unit is sent in its turn, and after each 3270 record the host sends
    /// a timing mark and the screen is read once the client has answered it. Client lines are
    /// waited for; typed text and keys are given to the client.
    /// </summary>
    internal static class HostStreamReplay
    {
        public const int TimeoutMs = 20000;

        public static async Task<ReplayResult> RunAsync(HostRecording recording, IOracleClient client)
        {
            List<RecordingLine> lines = InOrderOfEffect(recording.Lines);
            var result = new ReplayResult();
            await using ReplayHost host = ReplayHost.Start();
            await client.ConnectAsync(host.Port);
            await host.AcceptAsync(TimeoutMs);
            int hostRecords = 0;

            // True from a key that sends an AID until a host write restores the keyboard. s3270
            // takes no command in that time (it answers the AID only once unlocked), so no screen
            // is read then, from either client: the rule depends only on the recorded bytes, so
            // both are read at the same points.
            bool aidOutstanding = false;

            // True from a transfer line until the next key, text or transfer. s3270 takes no
            // other command while its Transfer() runs, so no screen is read in that time, from
            // either client. The screen is read once when the transfer has finished, at a point
            // that again depends only on the recorded bytes: every host unit before the next key
            // has been sent and read.
            bool transferOutstanding = false;
            for (int i = 0; i < lines.Count; i++)
            {
                RecordingLine line = lines[i];
                if (transferOutstanding && line.Kind != RecordingLineKind.Host && line.Kind != RecordingLineKind.Client)
                {
                    await FinishTransferAsync(client, result);
                    transferOutstanding = false;
                }

                switch (line.Kind)
                {
                    case RecordingLineKind.Host:
                        await host.SendAsync(line.Unit!);
                        if (line.Unit!.Kind == TelnetUnitKind.Record)
                        {
                            hostRecords++;
                            await host.SyncAsync(TimeoutMs);
                            await client.SettleAsync(host.RecordsSent);
                            if (aidOutstanding && RestoresKeyboard(line.Unit.Payload))
                            {
                                await client.AidFinishedAsync();
                                aidOutstanding = false;
                            }

                            if (!aidOutstanding && !transferOutstanding)
                            {
                                result.Labels.Add("after host record " + hostRecords + " (" + Describe(line.Unit) + ")");
                                result.Screens.Add(await client.ReadAsync());
                            }
                        }

                        break;
                    case RecordingLineKind.Client:
                        TelnetUnit sent = await host.WaitForLikeAsync(line.Unit!, TimeoutMs);
                        if (sent.Kind == TelnetUnitKind.Record && line.Unit!.Payload.Length > 0
                            && (sent.Payload.Length == 0 || sent.Payload[0] != line.Unit.Payload[0]))
                        {
                            result.OutOfStep.Add("before host record " + (hostRecords + 1) + ": " + client.Name + " sent " +
                                Head(sent.Payload) + " where the recording client sent " + Head(line.Unit.Payload));
                        }

                        break;
                    case RecordingLineKind.Type:
                        await client.TypeAsync(line.Text);
                        break;
                    case RecordingLineKind.Transfer:
                        await client.StartTransferAsync(TransferLine.Parse(line.Text));
                        transferOutstanding = true;
                        break;
                    default:
                        await client.PressAsync(line.Text);
                        aidOutstanding |= SendsAid(line.Text);
                        break;
                }
            }

            if (transferOutstanding)
            {
                await FinishTransferAsync(client, result);
            }

            await client.AidFinishedAsync();

            result.Records = host.RecordsReceived();
            result.Telnet = host.TelnetReceived();
            return result;
        }

        /// <summary>
        /// The recording's lines with each run of keys and typed text moved down past any host
        /// units that follow it, to just before the next client unit.
        /// </summary>
        /// <remarks>
        /// The recorder marks a key the moment the test presses it; a host write already on its
        /// way can still arrive after the mark (the logon panel comes in more than one write,
        /// Tk5CommonSteps). The host cannot have answered the key before the client sent
        /// something, so the key acted after those host units. Replaying it before them would
        /// type into a screen the host is about to replace, at a moment that depends on thread
        /// timing; after them, every client sees the same screen when the key comes.
        /// </remarks>
        /// <param name="lines">
        /// The recording's lines.
        /// </param>
        /// <returns>
        /// The lines in the order the replay plays them.
        /// </returns>
        public static List<RecordingLine> InOrderOfEffect(List<RecordingLine> lines)
        {
            var ordered = new List<RecordingLine>(lines.Count);
            var held = new List<RecordingLine>();
            for (int i = 0; i < lines.Count; i++)
            {
                RecordingLine line = lines[i];
                // A transfer is marked the moment it starts, as a key is, and acts on the screen the
                // way a key does: the client types the IND$FILE command.
                if (line.Kind == RecordingLineKind.Type || line.Kind == RecordingLineKind.Key || line.Kind == RecordingLineKind.Transfer)
                {
                    held.Add(line);
                    continue;
                }

                if (line.Kind == RecordingLineKind.Client && held.Count > 0)
                {
                    ordered.AddRange(held);
                    held.Clear();
                }

                ordered.Add(line);
            }

            ordered.AddRange(held);
            return ordered;
        }

        private static async Task FinishTransferAsync(IOracleClient client, ReplayResult result)
        {
            result.Transfers.Add(await client.FinishTransferAsync());
            result.Labels.Add("after IND$FILE transfer " + result.Transfers.Count);
            result.Screens.Add(await client.ReadAsync());
        }

        // The keys that send an AID to the host (GA23-0059-4 p. 3-2 and chapter 6).
        private static bool SendsAid(string key)
        {
            string k = key.ToUpperInvariant();
            return k == "ENTER" || k == "CLEAR" || k.StartsWith("PF", StringComparison.Ordinal) || k.StartsWith("PA", StringComparison.Ordinal);
        }

        // A Write, Erase/Write or Erase/Write Alternate whose WCC has keyboard restore (bit 6,
        // X'02', GA23-0059-4 p. 3-6).
        private static bool RestoresKeyboard(byte[] record)
        {
            if (record.Length < 2)
            {
                return false;
            }

            byte c = record[0];
            bool write = c == 0xF1 || c == 0xF5 || c == 0x7E || c == 0x01 || c == 0x05 || c == 0x0D;
            return write && (record[1] & 0x02) != 0;
        }

        private static string Describe(TelnetUnit unit)
        {
            byte command = unit.Payload.Length > 0 ? unit.Payload[0] : (byte)0;
            string name = command switch
            {
                0xF1 or 0x01 => "Write",
                0xF5 or 0x05 => "Erase/Write",
                0x7E or 0x0D => "Erase/Write Alternate",
                0x6F or 0x0F => "Erase All Unprotected",
                0xF3 or 0x11 => "Write Structured Field",
                0xF2 or 0x02 => "Read Buffer",
                0xF6 or 0x06 => "Read Modified",
                0x6E => "Read Modified All",
                _ => "command X'" + command.ToString("X2", CultureInfo.InvariantCulture) + "'",
            };
            return name + ", " + unit.Payload.Length.ToString(CultureInfo.InvariantCulture) + " bytes";
        }

        private static string Head(byte[] record)
        {
            // Masked like every print of what a client sent (see PasswordMask).
            string hex = PasswordMask.Text(TelnetUnit.Hex(record));
            return hex.Length > 48 ? hex.Substring(0, 48) + " ..." : hex;
        }
    }
}
