//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Steps every TK5 scenario shares: logging on to the ISPF menu (guide 01 sections 3 and 4),
    /// running one TSO command at READY, and logging off.
    /// </summary>
    internal static class Tk5CommonSteps
    {
        /// <summary>
        /// Adds the steps from connecting to the ISPF primary option menu.
        /// </summary>
        /// <param name="steps">
        /// The list the steps are added to.
        /// </param>
        /// <param name="settings">
        /// The user and password to log on with.
        /// </param>
        public static void AddLogon(List<Tk5Step> steps, Tk5Settings settings)
        {
            string user = settings.User;

            // The first screen is the logon panel, or Hercules' information panel when the
            // connect comes straight after another client disconnected. Nothing is pressed on the
            // Hercules panel: the host replaces it with the logon panel ON ITS OWN. Measured 23
            // November 2026 (was BUGS.md B7), three of three times: Hercules panel, then one logon
            // panel, with nothing sent. Pressing Clear there, as guide 01 says, makes the host send
            // the logon panel a SECOND time in answer to the Clear, and a user id typed after the
            // first copy is thrown away by the second; that is why the first user id was ignored.
            steps.Add(new Tk5Step("connect", t => Task.CompletedTask,
                s => s.Contains("Logon ===>") || s.Contains("Hercules"), "the logon or Hercules panel", false));
            steps.Add(new Tk5Step("logon panel", t => Task.CompletedTask,
                s => CursorIsInTheLogonField(s), "'Logon ===>' with the cursor in its field", true));

            // User, password, welcome, fortune cookie, ISPF.
            steps.Add(new Tk5Step("user id", async t =>
            {
                await t.TypeAsync(user);
                await t.PressAsync("ENTER");
            }, s => s.Contains("ENTER CURRENT PASSWORD FOR " + user), "the password prompt", true));
            steps.Add(new Tk5Step("password", async t =>
            {
                await t.TypeAsync(settings.Password);
                await t.PressAsync("ENTER");
            }, s => s.Contains("LOGON IN PROGRESS") && s.Contains("***"), "the welcome screen", true));
            steps.Add(new Tk5Step("fortune cookie", t => t.PressAsync("ENTER"),
                s => s.Contains("***") && !s.Contains("LOGON IN PROGRESS"), "the fortune cookie", false));
            steps.Add(new Tk5Step("ISPF menu", t => t.PressAsync("ENTER"),
                s => s.Contains("ISPF primary option menu"), "the ISPF menu", true));
        }

        /// <summary>
        /// A step that clears the screen, types one TSO command at READY and presses Enter.
        /// </summary>
        /// <param name="name">
        /// The step's name in the test output.
        /// </param>
        /// <param name="command">
        /// The command, as typed.
        /// </param>
        /// <param name="expect">
        /// Text the answer must contain before the READY that ends it.
        /// </param>
        /// <param name="compare">
        /// Whether the screen is compared with the reference terminal.
        /// </param>
        /// <returns>
        /// The step.
        /// </returns>
        public static Tk5Step Tso(string name, string command, string expect, bool compare)
        {
            return new Tk5Step(name, async t =>
            {
                await t.PressAsync("CLEAR");
                await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                await t.TypeAsync(command);
                await t.PressAsync("ENTER");
            }, s => s.Contains(expect) && LastLineIsReady(s), "'" + expect + "' and READY", compare);
        }

        /// <summary>
        /// The LOGOFF step, from READY back to the logon panel.
        /// </summary>
        /// <returns>
        /// The step.
        /// </returns>
        public static Tk5Step Logoff()
        {
            return new Tk5Step("LOGOFF", async t =>
            {
                await t.TypeAsync("LOGOFF");
                await t.PressAsync("ENTER");
            }, s => s.Contains("Logon ===>"), "the logon panel after LOGOFF", true);
        }

        /// <summary>
        /// Whether the last line with anything on it is READY: TSO has finished the command.
        /// </summary>
        /// <param name="s">
        /// The screen.
        /// </param>
        /// <returns>
        /// True when the last non-blank line is READY.
        /// </returns>
        public static bool LastLineIsReady(Tk5Screen s)
        {
            for (int r = Tk5Screen.Rows - 1; r >= 0; r--)
            {
                string line = s.Lines[r].Trim();
                if (line.Length > 0)
                {
                    return line == "READY";
                }
            }

            return false;
        }

        // The logon panel can arrive in more than one write after Reset and Clear: typing before
        // the write that puts the cursor in the field types onto the wrong place (found 22 November
        // 2026, when s3270's first run typed too early and the host simply showed the panel again).
        private static bool CursorIsInTheLogonField(Tk5Screen s)
        {
            for (int r = 0; r < Tk5Screen.Rows; r++)
            {
                int at = s.Lines[r].IndexOf("Logon ===>", System.StringComparison.Ordinal);
                if (at >= 0)
                {
                    return s.CursorRow == r && s.CursorColumn > at + 10;
                }
            }

            return false;
        }
    }
}
