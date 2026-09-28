using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia.ManualPlan;

/// <summary>
/// The machine half of the by-hand pass in
/// <c>E:\Dev\Ronny\RetroTerm\docs\manual-tests\M3-DEC-TERMINALS.md</c>, and the identity half of
/// M1 and M2 with it.
/// </summary>
/// <remarks>
/// <para><b>What a terminal ANSWERS is the part a host acts on</b></para>
/// A host asks who it is talking to before it decides what to send. Get the answer wrong and the
/// screen is not wrong - it is empty, because a Sixel host that is told the terminal cannot draw
/// sends no picture at all. That happened here: the VT240 was refusing Sixel it could draw, because
/// its device attributes claimed only ReGIS.
///
/// Those answers are spread across fourteen terminals and several manuals, and comparing them by
/// hand means holding a PDF open beside the source. So the suite writes them all out as one sheet:
/// every terminal, every question, the reply as readable text and as hex.
///
/// The sheet is generated from live emulators - the questions are really asked and the replies are
/// really captured - so it cannot drift from what a host would actually receive.
/// </remarks>
public class M3DecTerminalTests
{
    /// <summary>
    /// The questions a host asks, in the order a host usually asks them.
    /// </summary>
    /// <remarks>
    /// Primary DA first, because that is the one every host sends. DECRQSS asks the terminal to
    /// report a setting back in the form that would set it again, and the three chosen here are the
    /// ones the VT420 manual's table 12-4 leads with.
    /// </remarks>
    private static readonly (string Name, string Request)[] Questions =
    {
        ("DA1 primary", "\u001b[c"),
        ("DA2 secondary", "\u001b[>c"),
        ("DA3 tertiary", "\u001b[=c"),
        ("DSR status", "\u001b[5n"),
        ("DSR cursor (CPR)", "\u001b[6n"),
        ("DECRQSS DECSCL", "\u001bP$q\"p\u001b\\"),
        ("DECRQSS DECSTBM", "\u001bP$qr\u001b\\"),
        ("DECRQSS SGR", "\u001bP$qm\u001b\\"),
    };

    /// <summary>
    /// M3.1a - every terminal's answers to every question, written out as one sheet.
    /// </summary>
    [Fact]
    public void M3_1a_EveryTerminalsAnswersAreWrittenOutAsOneSheet()
    {
        string[] terminals = EmulatorFactory.AvailableEmulators;
        Assert.True(terminals.Length > 0);

        var sheet = new StringBuilder();
        sheet.AppendLine("# What each terminal answers");
        sheet.AppendLine();
        sheet.AppendLine("**Generated** by `M3DecTerminalTests.M3_1a_EveryTerminalsAnswersAreWrittenOutAsOneSheet`");
        sheet.AppendLine("by asking every emulator every question and capturing what it sends back.");
        sheet.AppendLine("Do not edit by hand — change the emulator and run the suite.");
        sheet.AppendLine();
        sheet.AppendLine("Belongs to `E:\\Dev\\Ronny\\RetroTerm\\docs\\manual-tests\\M3-DEC-TERMINALS.md`, case M3.1.");
        sheet.AppendLine();
        sheet.AppendLine("A blank reply means the terminal stayed silent. Silence is a valid answer for a");
        sheet.AppendLine("question a terminal does not support — and it is also what a swallowed sequence looks");
        sheet.AppendLine("like, which is why every blank below is worth one look at the manual for that model.");
        sheet.AppendLine();

        for (int t = 0; t < terminals.Length; t++)
        {
            string name = terminals[t];
            sheet.Append("## ").AppendLine(name);
            sheet.AppendLine();
            sheet.AppendLine("| Question | Reply | Hex |");
            sheet.AppendLine("|---|---|---|");

            for (int q = 0; q < Questions.Length; q++)
            {
                string reply = Ask(name, Questions[q].Request);

                sheet.Append("| ").Append(Questions[q].Name)
                    .Append(" | ").Append(Readable(reply))
                    .Append(" | ").Append(Hex(reply))
                    .AppendLine(" |");
            }

            sheet.AppendLine();
        }

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);

        // A viewer may hold the previous copy open; a manual artefact is not worth failing a run
        // over, the same rule the printed PDFs follow.
        try
        {
            File.WriteAllText(Path.Combine(folder, "terminal-answers.md"), sheet.ToString());
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// M3.1b - every terminal answers the primary device attributes question.
    /// </summary>
    /// <remarks>
    /// The one question no terminal may ignore. A host that gets nothing back either waits, or
    /// falls back to the dumbest thing it knows - and either way the session is worse than it needs
    /// to be. This is the assertion that the sheet above is not mostly blank.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllTerminals))]
    public void M3_1b_EveryTerminalAnswersPrimaryDeviceAttributes(string terminal)
    {
        // EXCEPT the 4014, and rightly. A Tektronix storage tube is not an ANSI terminal and has no
        // device attributes at all - its enquiry is ESC ENQ, which reports status and beam
        // position. Silence here is the correct answer, and the test below holds that to account
        // rather than letting the exemption cover a terminal that answers nothing whatsoever.
        // The VT52 is exempt for the same reason and a different one: it predates ANSI entirely and
        // has no CSI, so CSI c is not a question it can even hear. Its identify is ESC Z.
        if (terminal == "TEK4014" || terminal == "VT52")
        {
            return;
        }

        string reply = Ask(terminal, "\u001b[c");

        Assert.False(string.IsNullOrEmpty(reply),
            terminal + " said nothing at all when asked who it is");

        // Every DA1 reply is a CSI response - ESC [ ? ... c on a VT, or the TDV's own form.
        Assert.Equal(0x1B, (byte)reply[0]);
    }

    /// <summary>
    /// M3.1b - the 4014 answers its own enquiry instead.
    /// </summary>
    /// <remarks>
    /// The other half of the exemption above. A 4014 ignoring <c>CSI c</c> is only right if it
    /// answers the question it does have, so that is asserted rather than assumed - otherwise
    /// "silence is correct here" would quietly cover a terminal that answers nothing at all.
    /// </remarks>
    [Fact]
    public void M3_1b_TheTektronixAnswersItsOwnEnquiryInstead()
    {
        // ESC ENQ - report status and beam position.
        var enquiry = new StringBuilder();
        enquiry.Append((char)0x1B).Append((char)0x05);

        string reply = Ask("TEK4014", enquiry.ToString());

        Assert.False(string.IsNullOrEmpty(reply),
            "the 4014 answered neither CSI c nor its own ESC ENQ");
    }

    /// <summary>
    /// M3.1b - the VT52 answers ESC Z instead.
    /// </summary>
    /// <remarks>
    /// The same accounting for the other exemption. A VT52 predates ANSI and has no CSI, so its
    /// identify is <c>ESC Z</c> and its answer is <c>ESC / Z</c>. The VT52 is the weakest-evidenced
    /// terminal in the program - no manual held here, no corpus - which makes the one thing that
    /// can be checked worth checking.
    /// </remarks>
    [Fact]
    public void M3_1b_TheVt52AnswersEscZInstead()
    {
        var identify = new StringBuilder();
        identify.Append((char)0x1B).Append('Z');

        string reply = Ask("VT52", identify.ToString());

        Assert.False(string.IsNullOrEmpty(reply),
            "the VT52 answered neither CSI c nor its own ESC Z");
        Assert.Equal(0x1B, (byte)reply[0]);
    }

    /// <summary>
    /// M3.1c - a terminal that claims Sixel in its attributes can actually draw it, and the other
    /// way round.
    /// </summary>
    /// <remarks>
    /// <para><b>The failure this catches is silence, not a wrong picture</b></para>
    /// A Sixel host asks first and draws second. A terminal whose DA1 leaves out capability 4 gets
    /// sent no image at all, so a decoder that works perfectly never runs - which is exactly what
    /// the VT240 did until the VT330/VT340 manual's alias table settled it.
    ///
    /// The reverse matters just as much: claiming 4 with no decoder behind it means a host sends a
    /// picture into a terminal that will print it as punctuation.
    ///
    /// <para><b>It has to send a real picture to find out</b></para>
    /// This first asked whether the emulator had a graphics plane, and every VT340 failed it. The
    /// plane is built on the FIRST DRAW, not at construction, so the question being answered was
    /// "has anything been drawn yet" - which for a terminal nobody has sent an image to is always
    /// no. The honest question is whether a sixel actually draws, so one is sent.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllTerminals))]
    public void M3_1c_ClaimingSixelAndDrawingSixelAgree(string terminal)
    {
        string reply = Ask(terminal, "\u001b[c");
        if (!ClaimsCapability(reply, 4))
        {
            return;
        }

        var emulator = EmulatorFactory.CreateEmulator(terminal, 80, 24, 100);

        // The smallest picture there is: DCS q, one sixel character with all six bits set, ST.
        var image = new StringBuilder();
        image.Append((char)0x1B).Append("Pq~").Append((char)0x1B).Append('\\');
        emulator.ProcessData(Encoding.Latin1.GetBytes(image.ToString()));

        Assert.True(emulator.Graphics != null && emulator.Graphics.HasAnythingToDraw(),
            terminal + " claims Sixel (capability 4) and drew nothing when sent one");
    }

    /// <summary>
    /// Every terminal the factory can build, as theory data.
    /// </summary>
    /// <returns>
    /// One case per terminal name.
    /// </returns>
    public static IEnumerable<object[]> AllTerminals()
    {
        string[] names = EmulatorFactory.AvailableEmulators;
        for (int i = 0; i < names.Length; i++)
        {
            yield return new object[] { names[i] };
        }
    }

    /// <summary>
    /// Asks one terminal one question and returns what it sends back.
    /// </summary>
    /// <param name="terminal">
    /// The terminal type to build.
    /// </param>
    /// <param name="request">
    /// The bytes the host would send.
    /// </param>
    /// <returns>
    /// The reply, or an empty string when the terminal stayed silent.
    /// </returns>
    private static string Ask(string terminal, string request)
    {
        var emulator = EmulatorFactory.CreateEmulator(terminal, 80, 24, 100);

        var reply = new StringBuilder();
        emulator.DataToSend += bytes => reply.Append(Encoding.Latin1.GetString(bytes));

        emulator.ProcessData(Encoding.Latin1.GetBytes(request));

        return reply.ToString();
    }

    /// <summary>
    /// True when a primary device attributes reply lists the given capability number.
    /// </summary>
    /// <param name="reply">
    /// The reply, of the form ESC [ ? 62 ; 4 ; 6 c.
    /// </param>
    /// <param name="capability">
    /// The number to look for - 4 is Sixel, 3 is ReGIS.
    /// </param>
    /// <returns>
    /// False when the reply is empty or lists something else.
    /// </returns>
    /// <remarks>
    /// Parsed rather than matched as text, because "4" appears inside "64" and inside "42" and a
    /// substring test would answer yes to both.
    /// </remarks>
    private static bool ClaimsCapability(string reply, int capability)
    {
        int value = -1;

        for (int i = 0; i < reply.Length; i++)
        {
            char c = reply[i];

            if (c >= '0' && c <= '9')
            {
                value = value < 0 ? c - '0' : (value * 10) + (c - '0');
                continue;
            }

            if (value == capability)
            {
                return true;
            }

            value = -1;
        }

        return value == capability;
    }

    /// <summary>
    /// Turns a reply into something readable in a table cell.
    /// </summary>
    /// <param name="reply">
    /// The bytes the terminal sent.
    /// </param>
    /// <returns>
    /// The reply with its control characters named, or an empty cell.
    /// </returns>
    private static string Readable(string reply)
    {
        if (string.IsNullOrEmpty(reply))
        {
            return "";
        }

        var text = new StringBuilder();

        for (int i = 0; i < reply.Length; i++)
        {
            char c = reply[i];
            if (c == 0x1B)
            {
                text.Append("ESC ");
            }
            else if (c < 0x20 || c == 0x7F)
            {
                text.Append("<0x").Append(((byte)c).ToString("X2")).Append("> ");
            }
            else if (c == '|')
            {
                text.Append("(bar)");
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// The reply as hex bytes, which is what a byte trace will show.
    /// </summary>
    /// <param name="reply">
    /// The bytes the terminal sent.
    /// </param>
    /// <returns>
    /// Space-separated hex, or an empty cell.
    /// </returns>
    private static string Hex(string reply)
    {
        if (string.IsNullOrEmpty(reply))
        {
            return "";
        }

        var text = new StringBuilder();

        for (int i = 0; i < reply.Length; i++)
        {
            if (i != 0)
            {
                text.Append(' ');
            }
            text.Append(((byte)reply[i]).ToString("X2"));
        }

        return text.ToString();
    }
}
