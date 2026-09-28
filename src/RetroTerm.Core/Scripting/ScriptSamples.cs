using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// One sample script: library name + .rts content.
/// </summary>
public sealed class ScriptSample
{
    public string Name { get; }
    public string Content { get; }

    public ScriptSample(string name, string content)
    {
        Name = name;
        Content = content;
    }
}

/// <summary>
/// The sample scripts seeded into an EMPTY script library on first run — they are
/// the tutorial: each one demonstrates a slice of the language with comments.
/// A unit test parses every sample against the builtin registry, so a language or
/// command change that breaks a sample breaks the build, not the user.
/// </summary>
public static class ScriptSamples
{
    public static readonly IReadOnlyList<ScriptSample> All = new[]
    {
        new ScriptSample("sample-language-tour",
            "# Language tour — control flow, variables and subroutines.\n" +
            "# Runs entirely locally (ECHO only), safe to try without a connection.\n" +
            "\n" +
            "SET name \"world\"\n" +
            "\n" +
            "GOSUB greet          # runs the subroutine below, RETURN comes back here\n" +
            "SET name \"again\"\n" +
            "GOSUB greet\n" +
            "\n" +
            "IF $name == \"again\"\n" +
            "ECHO \"IF works — name is '$name'\"\n" +
            "ELSE\n" +
            "ECHO \"this branch never runs\"\n" +
            "ENDIF\n" +
            "\n" +
            "GOTO end             # GOTO never returns\n" +
            "\n" +
            "LABEL greet\n" +
            "ECHO \"hello $name\"\n" +
            "RETURN\n" +
            "\n" +
            "LABEL end\n" +
            "ECHO \"done — GOSUB ran twice, IF branched, GOTO skipped here\"\n"),

        new ScriptSample("sample-sintran-login",
            "# SINTRAN login — the canonical wake-up + login flow.\n" +
            "# No fixed delays anywhere: every step waits for text on the RENDERED\n" +
            "# screen. EDIT the user and password before running!\n" +
            "\n" +
            "SET user \"MY-USER\"\n" +
            "SET password \"MY-PASSWORD\"\n" +
            "\n" +
            "# Keep sending ESC until the ENTER prompt shows up — a timeout with an\n" +
            "# ontimeout= target is a planned branch, not a failure (retry loop).\n" +
            "LABEL wake\n" +
            "SENDRAW ESC\n" +
            "WAITFOR \"ENTER\" where=screen timeout=2000 ontimeout=wake\n" +
            "\n" +
            "# SEND sends EXACTLY the text — the carriage return is written as \\r.\n" +
            "SEND \"$user\\r\"\n" +
            "WAITFOR \"PASSWORD:\" where=screen timeout=15000\n" +
            "SEND \"$password\\r\"\n" +
            "\n" +
            "# Prompt at the END of the output = tail match (the default where).\n" +
            "WAITFOR \"@\" timeout=15000\n" +
            "ECHO \"logged in as $user\"\n"),

        new ScriptSample("sample-read-into-variable",
            "# Pull an answer off the screen into a variable and branch on it.\n" +
            "# regex group 1 — the (…) part — is what lands in the variable.\n" +
            "\n" +
            "SENDRAW ESC\n" +
            "WAITFOR \"SINTRAN\" where=screen timeout=10000 optional=true\n" +
            "WAITFOR \"VERSION ([A-Z])\" regex=true where=screen timeout=3000 into=ver optional=true\n" +
            "\n" +
            "IF $ver == \"\"\n" +
            "ECHO \"could not read a version letter off the screen\"\n" +
            "ELSE\n" +
            "ECHO \"SINTRAN version: $ver\"\n" +
            "ENDIF\n" +
            "\n" +
            "# READSCREEN is a checkpoint: the screen text lands in the run transcript.\n" +
            "READSCREEN\n"),

        new ScriptSample("sample-capture-listing",
            "# Capture a LONG listing that scrolls past: log raw traffic to a file\n" +
            "# while it runs, and pull new scrollback into the transcript afterwards.\n" +
            "# NOTE the doubled backslashes — \\ starts an escape inside strings.\n" +
            "\n" +
            "LOGSTART \"C:\\\\temp\\\\listing.txt\" format=text\n" +
            "READNEW              # set the scrollback cursor BEFORE the listing\n" +
            "\n" +
            "SEND \"LIST-FILES\\r\"\n" +
            "# Wait until the screen stops changing for a second — the listing is done.\n" +
            "WAITIDLE 1000 timeout=60000\n" +
            "\n" +
            "READNEW              # everything that scrolled past since the first READNEW\n" +
            "LOGSTOP\n" +
            "ECHO \"listing captured — transcript above, raw log in C:\\\\temp\\\\listing.txt\"\n"),

        new ScriptSample("sample-opcom-debug",
            "# OPCOM debugger tour — halt the ND-100, look at registers and memory,\n" +
            "# then let it run again. Needs a connection to an OPCOM Simulator or a\n" +
            "# real ND-100 in OPCOM mode; without one every action= round trip fails.\n" +
            "#\n" +
            "# EVERYTHING here is OCTAL: addresses, register levels and the values you\n" +
            "# read back are all base-8, exactly like the OPCOM console.\n" +
            "\n" +
            "# status is local — it never touches the wire, so it is safe to run first\n" +
            "# just to see whether an OPCOM handler is attached and what the CPU is doing.\n" +
            "OPCOM action=status\n" +
            "\n" +
            "# Halt the processor so the machine state stops moving under us.\n" +
            "OPCOM action=stop\n" +
            "\n" +
            "# Read the program counter (P register) on program level 0 straight into a\n" +
            "# variable. into= captures the octal value the command printed.\n" +
            "OPCOM action=readreg level=0 reg=P into=pc\n" +
            "ECHO \"halted at P = $pc (octal)\"\n" +
            "\n" +
            "# Dump every working register on level 0 in one shot (S D P B L A T X).\n" +
            "OPCOM action=dumpregs level=0\n" +
            "\n" +
            "# Examine a block of physical memory — octal addresses 1000 through 1010.\n" +
            "OPCOM action=dumpmem addr=1000 endaddr=1010\n" +
            "\n" +
            "# Deposit an octal word, read it back, and prove it stuck.\n" +
            "OPCOM action=writemem addr=1000 value=177\n" +
            "OPCOM action=readmem addr=1000 into=word\n" +
            "IF $word == \"000177\"\n" +
            "ECHO \"deposit confirmed — 1000 now holds $word\"\n" +
            "ELSE\n" +
            "ECHO \"unexpected read-back: got $word, wanted 000177\"\n" +
            "ENDIF\n" +
            "\n" +
            "# Let the CPU continue from where it was halted.\n" +
            "OPCOM action=start addr=$pc\n" +
            "ECHO \"restarted at $pc\"\n")
    };

    /// <summary>
    /// Writes the samples into the library — only when it holds NO scripts at all,
    /// so a user who deleted or renamed things is never second-guessed.
    /// </summary>
    public static void SeedIfEmpty(ScriptLibrary library)
    {
        if (library == null) throw new ArgumentNullException(nameof(library));
        if (library.List().Count > 0)
        {
            return;
        }
        for (int i = 0; i < All.Count; i++)
        {
            library.Save(All[i].Name, All[i].Content);
        }
    }
}
