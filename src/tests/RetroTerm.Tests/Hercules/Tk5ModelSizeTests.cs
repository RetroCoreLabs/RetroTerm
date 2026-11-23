//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Threading.Tasks;

using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// Which screen TK5 uses for each 3278 model: logged in as models 2 to 5, the size of the
    /// screen once ISPF's menu is up (an open question in PLAN.md, "On TK5: do models 3-5 get the
    /// bigger screen").
    /// </summary>
    /// <remarks>
    /// Needs TK5 running; category Hercules, outside the routine gate. The logon steps read only
    /// the top-left 24 x 80 of the screen (Tk5Screen), which is where the logon panel and the
    /// ISPF menu title are on every model.
    /// </remarks>
    [Trait("Category", "Hercules")]
    [Collection("Hercules")]
    public class Tk5ModelSizeTests
    {
        private readonly ITestOutputHelper _output;

        public Tk5ModelSizeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task IspfRunsOnTheModelsAlternateScreen(int model)
        {
            Tk5Settings settings = Tk5Settings.Load();
            var steps = new List<Tk5Step>();
            Tk5CommonSteps.AddLogon(steps, settings);
            steps.Add(new Tk5Step("PF3 to READY", t => t.PressAsync("PF3"),
                s => s.Contains("READY"), "READY", false));
            steps.Add(Tk5CommonSteps.Logoff());

            await using RetroTermTk5Terminal terminal = await RetroTermTk5Terminal.OpenAsync(settings, model);
            int rows = 0;
            int columns = 0;
            var withSize = new List<Tk5Step>();
            for (int i = 0; i < steps.Count; i++)
            {
                Tk5Step step = steps[i];
                withSize.Add(step);
                if (step.Name == "ISPF menu")
                {
                    withSize.Add(new Tk5Step("read the size", async t =>
                    {
                        (rows, columns) = await terminal.Session.RunOnSessionThreadAsync(() => (terminal.Emulator.Display.Rows, terminal.Emulator.Display.Columns));
                    }, s => true, "the size", false));
                }
            }

            await Tk5Scenario.RunAsync(terminal, withSize, _output);

            string alternate = Ibm3270Emulator.AlternateScreenSize(model);
            string used = columns + "x" + rows;
            _output.WriteLine("model " + model + ": ISPF on " + used + ", alternate " + alternate);
            Assert.Equal(alternate, used);
        }
    }
}
