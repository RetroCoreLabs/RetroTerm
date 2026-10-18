using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;

namespace RetroTerm.Tests.Documentation;

/// <summary>
/// Structural faults in XML doc comments that the compiler cannot see.
/// </summary>
/// <remarks>
/// <para><b>Why a test and not the compiler</b></para>
/// No project here sets <c>GenerateDocumentationFile</c>, so CS1570 to CS1574 are never emitted and
/// the XML comment standard is unenforced. Turning it on for one throwaway build on 28 August 2026
/// found seventeen real defects on the seam types.
///
/// That check is manual, and it needs a CS1591 suppression that must never be committed - so it will
/// be run when somebody remembers. This test needs neither, runs every time, and catches the fault
/// the compiler misses entirely.
///
/// <para><b>The fault: a doc block stranded from its member</b></para>
/// A method gets moved and its comment stays behind. A complete summary with all its param tags then
/// sits on the next member down, which ends up with TWO summaries - and the member the text was
/// written for has none at all.
///
/// Nine of the seventeen were this, and the compiler only noticed the ones whose param names
/// happened to disagree with the signature it landed on. A stranded block with no params, or with
/// params that happen to match, is invisible to it. The first scan for this pattern found ELEVEN
/// more that the compiler check had passed, including the whole architectural summary of
/// <c>IGraphicsSurface</c> sitting on the small static class above it while the interface itself had
/// nothing.
///
/// <para><b>Why more than one summary is always wrong</b></para>
/// A member has one summary. Two in one block means two members' worth of documentation on one
/// member, which is the stranded-block signature and nothing else - there is no legitimate reason to
/// write it.
///
/// <para><b>The second check: raw control bytes</b></para>
/// A C# hex escape is greedy, so typing a backslash-x escape into a heredoc that unescapes it first
/// writes a REAL control byte into the source. It has put raw ESC bytes into C# files and a raw BEL
/// into a Markdown document that then beeped when it was catted. It was still doing it on
/// 28 August 2026, inside the comment in <c>TektronixCharacterSizeTests</c> that WARNS about the
/// trap. Zero today; this keeps it there.
/// </remarks>
public class XmlCommentStructureTests
{
    /// <summary>
    /// The repository root, found from the assembly rather than the working directory.
    /// </summary>
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(XmlCommentStructureTests).Assembly.Location) ?? "",
        "..", "..", "..", "..", ".."));

    /// <summary>
    /// One run of consecutive documentation-comment lines, and where it starts.
    /// </summary>
    private readonly struct DocBlock
    {
        /// <summary>
        /// Path of the file the block came from.
        /// </summary>
        public readonly string File;

        /// <summary>
        /// One-based line number of the block's first line.
        /// </summary>
        public readonly int Line;

        /// <summary>
        /// The block's text, newlines included.
        /// </summary>
        public readonly string Text;

        /// <summary>
        /// Creates a block.
        /// </summary>
        /// <param name="file">
        /// Path of the file.
        /// </param>
        /// <param name="line">
        /// One-based line number of the first line.
        /// </param>
        /// <param name="text">
        /// The block's text.
        /// </param>
        public DocBlock(string file, int line, string text)
        {
            File = file;
            Line = line;
            Text = text;
        }
    }

    [Fact]
    public void NoDocCommentBlockCarriesTwoSummaries()
    {
        var blocks = AllDocBlocks();
        Assert.True(blocks.Count > 1000,
            "only " + blocks.Count.ToString(CultureInfo.InvariantCulture)
            + " doc blocks found - the scanner is wrong, not the code");

        var offenders = new StringBuilder();
        int count = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            int summaries = CountOccurrences(blocks[i].Text, "<summary>");
            if (summaries <= 1)
            {
                continue;
            }

            count++;
            offenders.Append("  ")
                     .Append(Relative(blocks[i].File))
                     .Append(':')
                     .Append(blocks[i].Line.ToString(CultureInfo.InvariantCulture))
                     .Append("  (")
                     .Append(summaries.ToString(CultureInfo.InvariantCulture))
                     .Append(" summaries)")
                     .Append('\n');
        }

        Assert.True(count == 0,
            count.ToString(CultureInfo.InvariantCulture)
            + " documentation block(s) carry more than one summary. That means a doc block was left "
            + "behind when its member moved, so it now sits on the wrong member - and the member it "
            + "was written for has no documentation at all. Move the stranded block back to the "
            + "member it describes; do not delete it.\n" + offenders.ToString());
    }

    [Fact]
    public void NoDocCommentContainsARawControlByte()
    {
        var blocks = AllDocBlocks();
        var offenders = new StringBuilder();
        int count = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            string text = blocks[i].Text;
            for (int c = 0; c < text.Length; c++)
            {
                char ch = text[c];
                if (ch >= ' ' || ch == '\r' || ch == '\n' || ch == '\t')
                {
                    continue;
                }

                count++;
                offenders.Append("  ")
                         .Append(Relative(blocks[i].File))
                         .Append(':')
                         .Append(blocks[i].Line.ToString(CultureInfo.InvariantCulture))
                         .Append("  contains 0x")
                         .Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture))
                         .Append('\n');
                break;
            }
        }

        Assert.True(count == 0,
            count.ToString(CultureInfo.InvariantCulture)
            + " documentation comment(s) contain a raw control byte. A backslash-x escape typed into "
            + "a shell heredoc is unescaped before the interpreter sees it, so the real byte lands in "
            + "the file. Write the characters in words instead.\n" + offenders.ToString());
    }

    /// <summary>
    /// Every documentation-comment block under <c>src</c> and <c>tests</c>.
    /// </summary>
    /// <returns>
    /// The blocks, in no particular order.
    /// </returns>
    /// <remarks>
    /// A block is a run of consecutive lines whose first non-blank characters are three slashes.
    /// Code between two runs separates them, which is what makes a stranded block detectable at all.
    /// </remarks>
    private static List<DocBlock> AllDocBlocks()
    {
        var blocks = new List<DocBlock>();
        var roots = new[] { Path.Combine(RepositoryRoot, "src"), Path.Combine(RepositoryRoot, "tests") };

        for (int r = 0; r < roots.Length; r++)
        {
            Assert.True(Directory.Exists(roots[r]), "could not find " + roots[r]);

            string[] files = Directory.GetFiles(roots[r], "*.cs", SearchOption.AllDirectories);
            for (int f = 0; f < files.Length; f++)
            {
                // Generated and intermediate output is not ours to police.
                if (files[f].Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || files[f].Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                CollectFrom(files[f], blocks);
            }
        }

        return blocks;
    }

    /// <summary>
    /// Adds one file's documentation blocks to a list.
    /// </summary>
    /// <param name="path">
    /// The file to read.
    /// </param>
    /// <param name="into">
    /// Where to put what is found.
    /// </param>
    private static void CollectFrom(string path, List<DocBlock> into)
    {
        string[] lines = File.ReadAllLines(path);

        int i = 0;
        while (i < lines.Length)
        {
            if (!lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            int start = i;
            var text = new StringBuilder();
            while (i < lines.Length && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                text.Append(lines[i]).Append('\n');
                i++;
            }

            into.Add(new DocBlock(path, start + 1, text.ToString()));
        }
    }

    /// <summary>
    /// Counts non-overlapping occurrences of a needle.
    /// </summary>
    /// <param name="haystack">
    /// Text to search.
    /// </param>
    /// <param name="needle">
    /// What to look for.
    /// </param>
    /// <returns>
    /// How many times it appears.
    /// </returns>
    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    /// <summary>
    /// A path shown relative to the repository root, so a failure message is readable.
    /// </summary>
    /// <param name="path">
    /// Absolute path.
    /// </param>
    /// <returns>
    /// The path without the repository prefix.
    /// </returns>
    private static string Relative(string path)
    {
        string root = RepositoryRoot;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar)
            : path;
    }
}
