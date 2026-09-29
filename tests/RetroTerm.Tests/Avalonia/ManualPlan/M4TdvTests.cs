using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Avalonia.ManualPlan;

/// <summary>
/// The machine half of the by-hand pass in
/// <c>docs\manual-tests\M4-TDV.md</c>.
/// </summary>
/// <remarks>
/// <para><b>The keyboard is the part that needs a person, and this is what it hands them</b></para>
/// Every other area in the manual plan can be judged from a picture. The TDV keyboard cannot: the
/// only way to know that a key sends what a real TDV2200 sends is to press it on real hardware and
/// watch the bytes. That check has been blocked on a machine for a long time, and when the machine
/// appears the pass has to be quick or it will not happen.
///
/// So this writes the whole key map out as a sheet to print - every key the registry knows, its
/// grid position, its virtual key code, and the bytes it sends in extended and in simple mode, in
/// hex. Ticking a printed sheet against a real keyboard is a job for an afternoon; working it out
/// from the source at the machine is a job for a week.
///
/// The sheet is GENERATED from the registry, so it cannot drift from the code the way a
/// hand-written table would.
/// </remarks>
public class M4TdvTests
{
    /// <summary>
    /// M4.2a - the whole TDV2200 key map, written out as a sheet to print and tick off.
    /// </summary>
    [Fact]
    public void M4_2a_TheKeyMapIsWrittenOutAsASheetToTickOff()
    {
        var keys = TDV2200KeyRegistry.AllKeys;
        Assert.True(keys.Count > 0, "the registry is empty - there is no keyboard to write out");

        // Grid positions sort as text, which groups the rows the way the keyboard is laid out.
        var positions = new List<string>(keys.Count);
        var enumerator = keys.GetEnumerator();
        while (enumerator.MoveNext())
        {
            positions.Add(enumerator.Current.Key);
        }
        positions.Sort(StringComparer.Ordinal);

        var sheet = new StringBuilder();
        sheet.AppendLine("# TDV2200 key sheet — press each key and tick the byte");
        sheet.AppendLine();
        sheet.AppendLine("**Generated** by `M4TdvTests.M4_2a_TheKeyMapIsWrittenOutAsASheetToTickOff`");
        sheet.AppendLine("from `TDV2200KeyRegistry`. Do not edit it by hand — edit the registry and run the suite.");
        sheet.AppendLine();
        sheet.AppendLine("Belongs to `docs\\manual-tests\\M4-TDV.md`, case M4.2.");
        sheet.AppendLine();
        sheet.AppendLine("`EXT` is extended mode, `SIMPLE` is simple ASCII mode. Bytes are hex.");
        sheet.AppendLine("A blank means the key sends nothing in that mode, which is itself worth checking.");
        sheet.AppendLine();
        sheet.AppendLine("| Grid | Key | VK | EXT | EXT hex | SHIFT+EXT | SIMPLE | SIMPLE hex | OK? |");
        sheet.AppendLine("|---|---|---|---|---|---|---|---|---|");

        int withSequences = 0;

        for (int i = 0; i < positions.Count; i++)
        {
            string grid = positions[i];
            var key = keys[grid];

            string? extended = TDV2200KeyRegistry.GetSequence(grid, extendedMode: true, numPadFuncMode: false);
            string? shifted = TDV2200KeyRegistry.GetSequence(grid, extendedMode: true, numPadFuncMode: false,
                shift: true);
            string? simple = TDV2200KeyRegistry.GetSequence(grid, extendedMode: false, numPadFuncMode: false);

            if (extended != null || simple != null)
            {
                withSequences++;
            }

            // A shift variant that is the same as the unshifted one is noise on a sheet meant to be
            // read a row at a time.
            string shiftedColumn = shifted != null && shifted != extended ? Readable(shifted) : "";

            sheet.Append("| ").Append(grid)
                .Append(" | ").Append(Cell(TDV2200KeyRegistry.GetEnglishName(grid) ?? key.Name))
                .Append(" | ").Append(key.VirtualKeyCode == 0 ? "" : key.VirtualKeyCode.ToString())
                .Append(" | ").Append(Readable(extended))
                .Append(" | ").Append(Hex(extended))
                .Append(" | ").Append(shiftedColumn)
                .Append(" | ").Append(Readable(simple))
                .Append(" | ").Append(Hex(simple))
                .AppendLine(" | |");
        }

        sheet.AppendLine();
        sheet.AppendLine($"{positions.Count} keys, {withSequences} of which send something.");

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);

        // A viewer may hold the previous copy open; a manual artefact is not worth failing a run
        // over, the same rule the printed PDFs follow.
        try
        {
            File.WriteAllText(Path.Combine(folder, "tdv2200-key-sheet.md"), sheet.ToString());
        }
        catch (IOException)
        {
        }

        // 46 of the registry's keys send a fixed sequence, counted on 2026-08-17. The rest are
        // ordinary letters, modifiers, and the programmable keys - which deliberately have no fixed
        // sequence at all, since a host defines what they send. The floor guards against the sheet
        // quietly emptying; a number that grows is worth noticing rather than failing on.
        Assert.True(withSequences >= 46,
            $"only {withSequences} keys send anything - 46 did on 2026-08-17, so the sheet has lost keys");
    }

    /// <summary>
    /// M4.2b - the five keys whose codes are quoted in the documents.
    /// </summary>
    /// <remarks>
    /// The arrows and HOME are marked AlwaysSameCode, so they send the same C0 byte in extended and
    /// in simple mode. These five are the ones written down in
    /// <c>docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md</c> and in the project memory, and they are the
    /// first thing to check on real hardware because a VT220 fallback would produce a whole escape
    /// sequence instead of one byte.
    /// </remarks>
    [Theory]
    [InlineData("UP", (byte)0x1C)]
    [InlineData("DOWN", (byte)0x0B)]
    [InlineData("LEFT", (byte)0x08)]
    [InlineData("RIGHT", (byte)0x18)]
    public void M4_2b_TheArrowsSendOneC0Byte(string name, byte expected)
    {
        string? grid = TDV2200KeyRegistry.GetGridForName(name);
        Assert.NotNull(grid);

        string? extended = TDV2200KeyRegistry.GetSequence(grid!, extendedMode: true, numPadFuncMode: false);
        string? simple = TDV2200KeyRegistry.GetSequence(grid!, extendedMode: false, numPadFuncMode: false);

        Assert.NotNull(extended);
        Assert.Equal(1, extended!.Length);
        Assert.Equal(expected, (byte)extended[0]);

        // AlwaysSameCode: the same byte in simple mode, not a fallback to something else.
        Assert.NotNull(simple);
        Assert.Equal(extended, simple);
    }

    /// <summary>
    /// M4.2d - HOME sends GS in both modes, like the four arrows. Settled from documentation,
    /// not from hardware - what a real keyboard sends is still open.
    /// </summary>
    /// <remarks>
    /// <para><b>The registry briefly disagreed with itself, twice, in one day</b></para>
    /// HOME carries the same <c>AlwaysSameCode</c> flag as the four arrows, which already implied
    /// it should repeat its byte in both modes. For a long time the registry's SimpleAscii for
    /// HOME was <c>0x10</c> (DLE) anyway, disagreeing with its own flag, sourced from
    /// <c>docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md</c> - which, read directly, cites no ND manual
    /// for that specific value and elsewhere in its own tables gives HOME only one code, not two.
    ///
    /// Settled 31 August 2026 by reading <c>spec\Keyboards\keyboard-spec.md</c> section 6.8.3
    /// directly, which cites the TDV-2200/9 User's Guide (ND-30.003.04 EN) and documents its own
    /// OCR-correction history: an early, uncorrected OCR pass of User's Guide section 7.2 misread
    /// HOME's simple-mode byte as DLE; a later, cross-checked OCR of section 9.1 marks B47/B48/B49
    /// as "is always" keys and gives HOME GS (<c>0x1D</c>) in both modes, explicitly superseding
    /// the DLE reading. The registry's SimpleAscii is corrected to <c>0x1D</c> to match.
    ///
    /// What is still open is whether a real physical TDV2200 keyboard agrees with the User's
    /// Guide - that is case M4.2d of the manual document, and still needs the hardware.
    /// </remarks>
    [Fact]
    public void M4_2d_HomeSendsGsInBothModes()
    {
        string? grid = TDV2200KeyRegistry.GetGridForName("HOME");
        Assert.NotNull(grid);

        string? extended = TDV2200KeyRegistry.GetSequence(grid!, extendedMode: true, numPadFuncMode: false);
        string? simple = TDV2200KeyRegistry.GetSequence(grid!, extendedMode: false, numPadFuncMode: false);

        Assert.NotNull(extended);
        Assert.Equal(0x1D, (byte)extended![0]);

        Assert.NotNull(simple);
        Assert.Equal(0x1D, (byte)simple![0]);
    }

    /// <summary>
    /// M4.2d, the other half - a real keypress agrees with what the registry says it should send.
    /// </summary>
    /// <remarks>
    /// <see cref="M4_2d_HomeSendsGsInBothModes"/> reads <see cref="TDV2200KeyRegistry"/> directly.
    /// <see cref="TDV2200KeyboardMapper.MapKey"/> is the function a real keypress, a
    /// virtual-keyboard click, or <c>terminal_localkey</c> actually goes through, and it carries
    /// its OWN hardcoded C0 table for the fixed keys, independent of the registry - this pins that
    /// the two stay in agreement, which briefly broke on 31 August 2026 in both directions (see
    /// the commit history for that day for the detail) before both were settled together.
    /// </remarks>
    [Fact]
    public void M4_2d_TheRealKeypressAgreesWithTheRegistry()
    {
        string? grid = TDV2200KeyRegistry.GetGridForName("HOME");
        Assert.NotNull(grid);
        Assert.True(TDV2200KeyRegistry.TryGetKey(grid!, out var def));
        int vk = def.VirtualKeyCode;

        string? registryExtended = TDV2200KeyRegistry.GetSequence(grid!, extendedMode: true, numPadFuncMode: false);
        string? registrySimple = TDV2200KeyRegistry.GetSequence(grid!, extendedMode: false, numPadFuncMode: false);

        var mapper = new TDV2200KeyboardMapper();
        string? keypressExtended = mapper.MapKey(vk, KeyModifiers.None, TerminalModes.None);
        string? keypressSimple = mapper.MapKey(vk, KeyModifiers.None, TerminalModes.TDV2115Mode);

        Assert.Equal(registryExtended, keypressExtended);
        Assert.Equal(registrySimple, keypressSimple);
    }

    /// <summary>
    /// M4.2c - the PC's F1 sends the TDV's OWN F1, and nothing sends a VT220 function key.
    /// </summary>
    /// <remarks>
    /// <para><b>This test used to overclaim, and its name was the whole problem</b></para>
    /// It was called <c>M4_2c_F1IsHjelpAndNotAVt220FunctionKey</c> and asserted only that grid G53
    /// sends <c>ESC [ 4 6 _</c> - true, and true whatever the F1 KEY does, because it never went
    /// near a keypress or a virtual key code. Measured against the live D100 on 1 September 2026,
    /// pressing F1 sends <c>ESC [ 5 0 _</c>, and the registry says why: G53 HJELP carries virtual
    /// key code 0, so no PC key reaches it, while F51 - the TDV's own key labelled F1 - carries
    /// 112 and sends <c>ESC [ 5 0 _</c>.
    /// <para>
    /// That mapping is right, and is Ronny's call of 2 September 2026: a PC F1 belongs on the
    /// terminal's F1. HJELP is a separate key on that keyboard with no PC equivalent, and stays
    /// reachable through the virtual keyboard or a user binding.
    /// </para>
    /// <para><b>What actually matters here</b></para>
    /// The real risk this case exists for is a VT220 fallback, <c>ESC [ 1 1 ~</c>, which a SINTRAN
    /// host ignores in silence. So the key is driven through the real mapper and checked for the
    /// ND form rather than the VT220 one.
    /// </remarks>
    [Fact]
    public void M4_2c_TheF1KeySendsTheTerminalsOwnF1AndNeverAVt220Sequence()
    {
        // The key a PC keyboard actually has.
        Assert.True(TDV2200KeyRegistry.TryGetKey("F51", out var f1));
        Assert.Equal(112, f1.VirtualKeyCode);

        var mapper = new TDV2200KeyboardMapper();
        string? pressed = mapper.MapKey(112, KeyModifiers.None, TerminalModes.None);

        Assert.Equal("\u001b[50_", pressed);
        Assert.Equal(
            TDV2200KeyRegistry.GetSequence("F51", extendedMode: true, numPadFuncMode: false),
            pressed);

        // HJELP is a real key with a real sequence - it simply has no PC key of its own.
        Assert.True(TDV2200KeyRegistry.TryGetKey("G53", out var hjelp));
        Assert.Equal(0, hjelp.VirtualKeyCode);
        Assert.Equal("\u001b[46_",
            TDV2200KeyRegistry.GetSequence("G53", extendedMode: true, numPadFuncMode: false));

        // The failure this case was written to catch: a VT220 function key, which a SINTRAN host
        // ignores without a word.
        Assert.DoesNotContain("11~", pressed);
        Assert.DoesNotContain("~", pressed);
    }


    /// <summary>
    /// Makes any text safe to sit in a table cell.
    /// </summary>
    /// <param name="text">
    /// The text, which may be null.
    /// </param>
    /// <returns>
    /// The text with anything that would end the cell replaced.
    /// </returns>
    /// <remarks>
    /// Some keys carry a bar in their own label - the vertical-bar key does, and so does a label
    /// that pairs two characters - and one of those in the name column pushed every following
    /// column of that row one place left. The sheet is read a row at a time by somebody holding a
    /// keyboard, so a row that lies is worse than a row that is missing.
    /// </remarks>
    private static string Cell(string? text)
        => string.IsNullOrEmpty(text) ? "" : text!.Replace("|", "(bar)");

    /// <summary>
    /// Turns a sequence into something readable in a table cell.
    /// </summary>
    /// <param name="sequence">
    /// The bytes the key sends, or null.
    /// </param>
    /// <returns>
    /// The sequence with its control characters named, or an empty cell.
    /// </returns>
    private static string Readable(string? sequence)
    {
        if (string.IsNullOrEmpty(sequence))
        {
            return "";
        }

        var text = new StringBuilder();

        for (int i = 0; i < sequence!.Length; i++)
        {
            char c = sequence[i];
            if (c == 0x1B)
            {
                text.Append("ESC ");
            }
            else if (c < 0x20 || c == 0x7F)
            {
                text.Append('<').Append(ControlName(c)).Append("> ");
            }
            else if (c == '|')
            {
                // A bare pipe would end the table cell it is sitting in.
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
    /// The usual short name for a C0 control, or its number when it has none worth printing.
    /// </summary>
    /// <param name="c">
    /// The control character.
    /// </param>
    /// <returns>
    /// A short name.
    /// </returns>
    private static string ControlName(char c)
    {
        switch ((byte)c)
        {
            case 0x08: return "BS";
            case 0x09: return "HT";
            case 0x0A: return "LF";
            case 0x0B: return "VT";
            case 0x0C: return "FF";
            case 0x0D: return "CR";
            case 0x0E: return "SO";
            case 0x0F: return "SI";
            case 0x18: return "CAN";
            case 0x1C: return "FS";
            case 0x1D: return "GS";
            case 0x1E: return "RS";
            case 0x1F: return "US";
            case 0x7F: return "DEL";
            default: return "0x" + ((byte)c).ToString("X2");
        }
    }

    /// <summary>
    /// The sequence as hex bytes, which is what a byte trace will show.
    /// </summary>
    /// <param name="sequence">
    /// The bytes the key sends, or null.
    /// </param>
    /// <returns>
    /// Space-separated hex, or an empty cell.
    /// </returns>
    private static string Hex(string? sequence)
    {
        if (string.IsNullOrEmpty(sequence))
        {
            return "";
        }

        var text = new StringBuilder();

        for (int i = 0; i < sequence!.Length; i++)
        {
            if (i != 0)
            {
                text.Append(' ');
            }
            text.Append(((byte)sequence[i]).ToString("X2"));
        }

        return text.ToString();
    }
}
