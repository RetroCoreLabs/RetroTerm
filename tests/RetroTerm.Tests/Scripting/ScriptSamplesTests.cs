using System;
using System.IO;
using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Scripting;
using Xunit;

namespace RetroTerm.Tests.Scripting;

/// <summary>
/// The shipped sample scripts are the tutorial — every one of them must parse
/// cleanly against the builtin command registry. A language or command change
/// that breaks a sample breaks HERE, not in front of the user.
/// </summary>
public class ScriptSamplesTests
{
    private static ScriptParser MakeParser()
    {
        var registry = new CommandRegistry();
        BuiltinCommands.RegisterAll(registry);
        return new ScriptParser(registry);
    }

    [Fact]
    public void EverySample_ParsesWithoutErrors()
    {
        var parser = MakeParser();
        for (int i = 0; i < ScriptSamples.All.Count; i++)
        {
            var sample = ScriptSamples.All[i];
            var parsed = parser.Parse(sample.Content);
            Assert.True(parsed.IsValid,
                $"sample '{sample.Name}' has parse errors: {(parsed.Errors.Count > 0 ? parsed.Errors[0].ToString() : "?")}");
            Assert.True(parsed.Steps.Count > 0, $"sample '{sample.Name}' has no steps");
        }
    }

    [Fact]
    public void SeedIfEmpty_FillsEmptyLibrary_LeavesNonEmptyAlone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "retroterm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var library = new ScriptLibrary(dir);

        ScriptSamples.SeedIfEmpty(library);
        Assert.Equal(ScriptSamples.All.Count, library.List().Count);

        // A library the user has touched is never re-seeded: delete everything but
        // one script and seed again — nothing comes back.
        var names = library.List();
        for (int i = 1; i < names.Count; i++)
        {
            library.Delete(names[i]);
        }
        ScriptSamples.SeedIfEmpty(library);
        Assert.Single(library.List());

        Directory.Delete(dir, recursive: true);
    }
}
