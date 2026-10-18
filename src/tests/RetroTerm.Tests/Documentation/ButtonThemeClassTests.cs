using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace RetroTerm.Tests.Documentation;

/// <summary>
/// Every push button in the desktop app takes its look from a theme class, not from brushes
/// written on the button itself.
///
/// Ronny asked on 8 September 2026 whether there is a standard for buttons and tabs and whether
/// it is being followed. There is one - DarkTheme.axaml defines Button.Primary, Button.Secondary,
/// Button.Danger and a handful of named variants - and on that day only three windows used it.
/// The OPCOM window was swept first; the remaining eleven files, 29 buttons between them, were
/// swept on 9 September.
///
/// WHY IT MATTERS, and it is not tidiness. A brush set on the element BEATS a style selector in
/// Avalonia. Setting only the background by hand gets the colour roughly right and silently drops
/// the border, the hover colour and the hand cursor the class also carries, so the button looks
/// nearly right, stops following the theme, and nothing goes red. That is the same trap this repo
/// already recorded for ComboBox, where a status-bar control rendered light on a dark window while
/// fourteen tests passed.
///
/// This reads the SOURCE rather than building the windows, because several of them need a live
/// session or a connection to construct. OpcomButtonStyleTests does the stronger check - real
/// controls, real binding priorities - on the one window that can be built with no arguments.
/// </summary>
public class ButtonThemeClassTests
{
    /// <summary>
    /// The repository root, found from the assembly rather than the working directory.
    /// </summary>
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(ButtonThemeClassTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", ".."));

    private static string DesktopFolder => Path.Combine(RepositoryRoot, "src", "RetroTerm.Desktop");

    /// <summary>
    /// One opening Button tag, and where in the file it was.
    /// </summary>
    private readonly struct ButtonTag
    {
        public ButtonTag(string file, int line, string text)
        {
            File = file;
            Line = line;
            Text = text;
        }

        public string File { get; }

        public int Line { get; }

        public string Text { get; }

        public override string ToString() => File + ":" + Line.ToString();
    }

    /// <summary>
    /// Matches an opening Button tag, self-closing or not.
    /// </summary>
    private static readonly Regex TagPattern =
        new Regex(@"<Button\b.*?(?:/>|>)", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Matches a brush written on the element itself.
    /// </summary>
    private static readonly Regex InlineBrushPattern =
        new Regex(@"\b(Background|Foreground|BorderBrush)\s*=", RegexOptions.Compiled);

    /// <summary>
    /// Reads every Button tag out of every markup file in the desktop project.
    /// </summary>
    /// <returns>
    /// One entry per opening tag, in file order.
    /// </returns>
    private static List<ButtonTag> AllButtonTags()
    {
        var found = new List<ButtonTag>();
        string folder = DesktopFolder;
        if (!Directory.Exists(folder)) return found;

        string[] files = Directory.GetFiles(folder, "*.axaml", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        for (int i = 0; i < files.Length; i++)
        {
            string text = File.ReadAllText(files[i]);
            string relative = files[i].Substring(RepositoryRoot.Length).TrimStart(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (Match m in TagPattern.Matches(text))
            {
                // Line number the cheap way. There are a few dozen tags in the whole project.
                int line = 1;
                for (int c = 0; c < m.Index; c++)
                {
                    if (text[c] == '\n') line++;
                }

                found.Add(new ButtonTag(relative, line, m.Value));
            }
        }

        return found;
    }

    private static string Describe(ButtonTag tag)
    {
        var name = Regex.Match(tag.Text, "Name=\"([^\"]*)\"");
        var content = Regex.Match(tag.Text, "Content=\"([^\"]*)\"");
        string what = name.Success ? name.Groups[1].Value
            : (content.Success ? content.Groups[1].Value : "(unnamed)");
        return tag.File + ":" + tag.Line.ToString() + " " + what;
    }

    [Fact]
    public void NoButtonInTheDesktopProjectSetsABrushOnItself()
    {
        var tags = AllButtonTags();
        Assert.True(tags.Count > 30,
            "expected to find the project's buttons, found " + tags.Count.ToString()
            + " - has the desktop folder moved?");

        var offenders = new List<string>();
        for (int i = 0; i < tags.Count; i++)
        {
            if (InlineBrushPattern.IsMatch(tags[i].Text)) offenders.Add(Describe(tags[i]));
        }

        var message = new StringBuilder();
        message.Append("these buttons set a brush on the element, which outranks the theme class ");
        message.Append("and silently drops its border, hover colour and cursor:");
        for (int i = 0; i < offenders.Count; i++)
        {
            message.Append(Environment.NewLine).Append("  ").Append(offenders[i]);
        }

        Assert.True(offenders.Count == 0, message.ToString());
    }

    [Fact]
    public void EveryButtonInTheDesktopProjectCarriesAThemeClass()
    {
        var tags = AllButtonTags();
        var bare = new List<string>();

        for (int i = 0; i < tags.Count; i++)
        {
            if (tags[i].Text.IndexOf("Classes=", StringComparison.Ordinal) < 0)
            {
                bare.Add(Describe(tags[i]));
            }
        }

        var message = new StringBuilder();
        message.Append("these buttons carry no theme class, so they render in the stock Fluent ");
        message.Append("look rather than the app's:");
        for (int i = 0; i < bare.Count; i++)
        {
            message.Append(Environment.NewLine).Append("  ").Append(bare[i]);
        }

        Assert.True(bare.Count == 0, message.ToString());
    }
}
