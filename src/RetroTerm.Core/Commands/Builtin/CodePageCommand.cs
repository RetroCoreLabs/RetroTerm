//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Input;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// CODEPAGE — show or change the EBCDIC code page of a 3270 tab.
/// </summary>
/// <remarks>
/// The code page decides what a byte from the host shows as and what byte a typed character
/// becomes, so a Norwegian host needs 277 for æ, ø and å. With no number it lists the pages.
/// </remarks>
public sealed class CodePageCommand : ISessionCommand
{
    public string Name => "CODEPAGE";
    public string Summary => "Show the 3270 tab's EBCDIC code page and the ones to choose from, or change it (277 for Denmark and Norway)";
    public string Example => "CODEPAGE 277";
    public bool ProducesOutput => true;

    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("number", CommandParameterType.Int, required: false, defaultValue: "0",
            "The IBM code page number; leave it out to list them"),
    };

    public async Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args, CancellationToken cancellationToken)
    {
        if (session.Emulator is not ICodePageTerminal terminal)
        {
            return CommandResult.Fail("this tab's terminal (" + session.Emulator.GetType().Name + ") has no EBCDIC code page");
        }

        int number = args.GetInt("number", 0);
        if (number == 0)
        {
            string list = await session.RunOnSessionThreadAsync(() =>
            {
                var text = new StringBuilder();
                text.Append("code page ").Append(terminal.CodePage.ToString(CultureInfo.InvariantCulture)).Append(", choose from:");
                IReadOnlyList<string> pages = terminal.CodePages;
                for (int i = 0; i < pages.Count; i++)
                {
                    text.Append('\n').Append("  ").Append(pages[i]);
                }

                return text.ToString();
            }).ConfigureAwait(false);
            return CommandResult.Ok(list);
        }

        string? problem = await session.RunOnSessionThreadAsync(() => terminal.SetCodePage(number)).ConfigureAwait(false);
        return problem == null
            ? CommandResult.Ok("code page " + number.ToString(CultureInfo.InvariantCulture))
            : CommandResult.Fail(problem);
    }
}
