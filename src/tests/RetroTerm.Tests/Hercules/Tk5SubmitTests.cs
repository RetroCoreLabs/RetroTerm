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

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// SUBMIT against the live TK5 host: a three-card IEFBR14 job with a job name used by no
    /// other run is sent to TK5's socket card reader, and TSO is then asked whether it ran.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The proof that the job ran is on the host, read through TSO as the test user: STATUS says
    /// NOT FOUND before the submit (so nothing of that name was there), and then "ON OUTPUT
    /// QUEUE" once JES2 has run the job (MSGCLASS=X keeps the output held, TK5 guide 04,
    /// section 6). OUTPUT jobname PRINT(*) then shows the job log, which must hold
    /// "IEF142I jobname STEP1 - STEP WAS EXECUTED - COND CODE 0000", and removes it, so the run
    /// leaves nothing behind.
    /// </para>
    /// <para>
    /// The job card carries no USER or PASSWORD: IEFBR14 touches no data set, and leaving them out
    /// keeps the password out of the deck file. The job name is the user id and two letters from
    /// the clock; TSO's STATUS and OUTPUT find a user's jobs by that prefix.
    /// </para>
    /// <para>
    /// Needs TK5 and the settings in <see cref="Tk5Settings"/>, including cardReaderPort; outside
    /// the routine gate (Category Hercules), and FAILS when the host does not answer.
    /// </para>
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Tk5SubmitTests
    {
        private readonly ITestOutputHelper _output;

        public Tk5SubmitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ASubmittedJobRunsAndItsOutputIsHeldForTheUser()
        {
            Tk5Settings settings = Tk5Settings.Load();
            if (settings.CardReaderPort == 0)
            {
                throw new Xunit.Sdk.XunitException("The SUBMIT test needs \"cardReaderPort\" in %USERPROFILE%\\.retroterm\\tk5-tests.json: the port of the 3505 sockdev line in TK5's conf/tk5.cnf.");
            }

            long tick = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            string job = settings.User + (char)('A' + (tick / 26 % 26)) + (char)('A' + (tick % 26));
            string path = Path.Combine(Path.GetTempPath(), "RetroTermSubmit-" + Guid.NewGuid().ToString("N") + ".jcl");
            File.WriteAllText(path,
                "//" + job + " JOB (ACCT),'RTT SUBMIT',CLASS=A,MSGCLASS=X,MSGLEVEL=(1,1)\r\n" +
                "//STEP1    EXEC PGM=IEFBR14\r\n" +
                "//\r\n", Encoding.ASCII);
            try
            {
                await using RetroTermTk5Terminal terminal = await RetroTermTk5Terminal.OpenAsync(settings);
                terminal.Session.CardReaderHost = settings.Host;
                terminal.Session.CardReaderPort = settings.CardReaderPort;

                var log = new StringBuilder();
                CommandResult? submitted = null;
                var steps = new List<Tk5Step>();
                Tk5CommonSteps.AddLogon(steps, settings);
                steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"), s => s.Contains("READY"), "READY", false));
                steps.Add(Tk5CommonSteps.Tso("STATUS before", "STATUS " + job, "JOB " + job + " NOT FOUND", false));
                steps.Add(new Tk5Step("SUBMIT", async t =>
                {
                    submitted = await new SubmitCommand().ExecuteAsync(terminal.Session,
                        new CommandArgs().Set("path", path).Set("timeout", "30000"), TestContext.Current.CancellationToken);
                }, s => true, "the screen after SUBMIT", false));
                steps.Add(new Tk5Step("STATUS until on the output queue", async t =>
                {
                    for (int i = 0; i < 60; i++)
                    {
                        await t.PressAsync("CLEAR");
                        await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                        await t.TypeAsync("STATUS " + job);
                        await t.PressAsync("ENTER");
                        Tk5Screen s = await t.WaitUntilAsync(Tk5CommonSteps.LastLineIsReady, "READY after STATUS", Tk5Scenario.HostTimeoutMs);
                        if (s.Contains("ON OUTPUT QUEUE"))
                        {
                            return;
                        }

                        // Waits for JES2, not for the screen: the next STATUS is a new question.
                        await Task.Delay(1000);
                    }
                }, s => s.Contains("JOB " + job + "(JOB") && s.Contains("ON OUTPUT QUEUE"), "the job on the output queue", false));
                steps.Add(new Tk5Step("OUTPUT PRINT(*)", async t =>
                {
                    await t.PressAsync("CLEAR");
                    await t.WaitUntilAsync(s => true, "the cleared screen", Tk5Scenario.HostTimeoutMs);
                    await t.TypeAsync("OUTPUT " + job + " PRINT(*)");
                    await t.PressAsync("ENTER");

                    // Line mode: a full screen ends in *** and waits for Enter.
                    for (int i = 0; i < 40; i++)
                    {
                        Tk5Screen s = await t.WaitUntilAsync(x => true, "a page of the job's output", Tk5Scenario.HostTimeoutMs);
                        log.Append(s.Text()).Append('\n');
                        if (Tk5CommonSteps.LastLineIsReady(s))
                        {
                            return;
                        }

                        await t.PressAsync("ENTER");
                    }
                }, Tk5CommonSteps.LastLineIsReady, "READY after OUTPUT", false));
                steps.Add(Tk5CommonSteps.Tso("STATUS after", "STATUS " + job, "JOB " + job + " NOT FOUND", false));
                steps.Add(Tk5CommonSteps.Logoff());

                await Tk5Scenario.RunAsync(terminal, steps, _output);

                Assert.NotNull(submitted);
                _output.WriteLine("SUBMIT: " + (submitted!.Success ? submitted.Output : submitted.Error) + " in " + submitted.Elapsed.TotalMilliseconds + " ms");
                _output.WriteLine("The job's output:\n" + log);
                Assert.True(submitted.Success, submitted.Error);
                Assert.Contains("3 cards", submitted.Output);
                Assert.Contains("IEF142I " + job + " STEP1 - STEP WAS EXECUTED - COND CODE 0000", log.ToString());
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
