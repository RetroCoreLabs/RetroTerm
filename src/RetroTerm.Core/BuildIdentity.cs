using System;
using System.Reflection;

namespace RetroTerm.Core;

/// <summary>
/// Which build of RetroTerm is actually running - version, git commit, and when it was built.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, 27 August 2026</b></para>
/// Two copies of RetroTerm were running from the same folder. Only the first got the MCP port, and
/// the second reported that it could not start its server. Anything driving the terminal was one
/// launch away from testing an OLD binary while believing it had the new one - and a stale binary
/// that passes is worse than one that fails, because the pass is believed.
///
/// Ronny's instruction the same day: put a build id, git hash and build date on the MCP startup so
/// the caller can tell at a glance whether it reached the right build.
///
/// <para><b>It describes the RUNNING program where it can</b></para>
/// The stamp is read from the entry assembly - the executable that was launched - because that is
/// what the question is about. When that assembly is not one of ours, which is what happens under a
/// test runner, it falls back to this library. See <c>Stamped</c> for why that fallback earns its
/// keep rather than being a fudge.
///
/// <para><b>Never throws</b></para>
/// Every value falls back to "unknown". A missing stamp is a small loss of information; an
/// exception while reporting who you are would take the whole server down at startup.
/// </remarks>
public static class BuildIdentity
{
    /// <summary>
    /// What a value reads when the build did not record it.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The assembly whose stamp is reported.
    /// </summary>
    /// <remarks>
    /// <para><b>The running program first, this library second</b></para>
    /// The entry assembly is the executable somebody launched, which is what "which build am I
    /// talking to" is really asking about. But not every host is one of ours: under a test runner
    /// the entry assembly is the runner itself and carries no stamp of ours at all, and reporting
    /// "unknown" there would make this untestable - a reporter of build identity that cannot be
    /// checked is precisely the wrong thing to have.
    ///
    /// So the entry assembly is used when it carries our stamp, and this library is used when it
    /// does not. Both are built from the same tree by the same rules in Directory.Build.props.
    /// </remarks>
    private static readonly Assembly Stamped = ChooseStampedAssembly();

    /// <summary>
    /// Picks the assembly to read the stamp from.
    /// </summary>
    /// <returns>
    /// The entry assembly when it is one of ours, otherwise this library.
    /// </returns>
    private static Assembly ChooseStampedAssembly()
    {
        var self = typeof(BuildIdentity).Assembly;

        try
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry == null) return self;

            return HasOurStamp(entry) ? entry : self;
        }
        catch
        {
            return self;
        }
    }

    /// <summary>
    /// Whether an assembly carries the git stamp this class looks for.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to inspect.
    /// </param>
    /// <returns>
    /// True when it was built by this repository's rules.
    /// </returns>
    private static bool HasOurStamp(Assembly assembly)
    {
        try
        {
            var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
            if (attributes == null) return false;

            var all = new System.Collections.Generic.List<AssemblyMetadataAttribute>(attributes).ToArray();
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].Key, "GitCommit", StringComparison.Ordinal)) return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The assembly version, as set in Directory.Build.props.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// The copyright line, as set in Directory.Build.props.
    /// </summary>
    /// <remarks>
    /// Read from the stamped assembly for the same reason the version is: until 28 September 2026
    /// the welcome screen carried its own copy, "2025 RetroCore Labs", beside a version of its own,
    /// "1.0.0-alpha", and both had drifted from the build. One source, read at run time.
    /// </remarks>
    public static string Copyright { get; } = ReadAttribute<AssemblyCopyrightAttribute>(a => a.Copyright);

    /// <summary>
    /// The company, as set in Directory.Build.props.
    /// </summary>
    public static string Company { get; } = ReadAttribute<AssemblyCompanyAttribute>(a => a.Company);

    /// <summary>
    /// Reads one string-valued attribute off the stamped assembly.
    /// </summary>
    /// <typeparam name="T">
    /// The attribute type.
    /// </typeparam>
    /// <param name="read">
    /// Picks the string out of the attribute.
    /// </param>
    /// <returns>
    /// The value, or "unknown" when the attribute is absent or empty.
    /// </returns>
    private static string ReadAttribute<T>(Func<T, string?> read) where T : Attribute
    {
        try
        {
            var attribute = Stamped.GetCustomAttribute<T>();
            if (attribute == null) return Unknown;
            var value = read(attribute);
            return string.IsNullOrEmpty(value) ? Unknown : value!;
        }
        catch
        {
            return Unknown;
        }
    }

    /// <summary>
    /// The short git commit the build came from, or "unknown".
    /// </summary>
    public static string Commit { get; } = ReadMetadata("GitCommit");

    /// <summary>
    /// The branch the build came from, or "unknown".
    /// </summary>
    public static string Branch { get; } = ReadMetadata("GitBranch");

    /// <summary>
    /// True when the working tree had uncommitted changes at build time.
    /// </summary>
    /// <remarks>
    /// This matters more than the hash. A commit alone claims the build is exactly that commit, and
    /// during ordinary work it usually is not - so a hash without this flag is a confident lie.
    /// </remarks>
    public static bool IsDirty { get; } =
        string.Equals(ReadMetadata("GitDirty"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The date the build was made, as recorded by MSBuild.
    /// </summary>
    public static string BuildDate { get; } = ReadMetadata("BuildDate");

    /// <summary>
    /// The time the build was made, as recorded by MSBuild.
    /// </summary>
    public static string BuildTime { get; } = ReadMetadata("BuildTime");

    /// <summary>
    /// One short line naming this build, for a version field or a log.
    /// </summary>
    /// <remarks>
    /// Deliberately compact, because it lands in places with little room - the MCP server's version
    /// field among them. The plus sign on a dirty build follows the usual convention for a version
    /// that is not exactly its commit.
    /// </remarks>
    public static string Short { get; } = BuildShort();

    /// <summary>
    /// The whole story in one sentence, for a startup line somebody actually reads.
    /// </summary>
    public static string Describe()
    {
        var dirty = IsDirty ? " (uncommitted changes)" : string.Empty;
        return $"RetroTerm {Version}, commit {Commit} on {Branch}{dirty}, built {BuildDate} {BuildTime}";
    }

    /// <summary>
    /// Reads the stamped assembly's version.
    /// </summary>
    /// <returns>
    /// The version, or "unknown".
    /// </returns>
    private static string ReadVersion()
    {
        try
        {
            var informational = Stamped.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational != null && !string.IsNullOrEmpty(informational.InformationalVersion))
            {
                // THE PART AFTER THE PLUS IS THROWN AWAY. The SDK appends the full forty-character
                // commit to this attribute on its own, which would make every line read
                // "1.0.26.2+2ee66a97015ec57d6cf65c0c56cc28453f9b4d6a, commit 2ee66a97015e" - the
                // same hash twice, once unreadably. The commit is reported properly beside it.
                var text = informational.InformationalVersion;
                int plus = text.IndexOf('+');
                return plus > 0 ? text.Substring(0, plus) : text;
            }

            var version = Stamped.GetName().Version;
            return version == null ? Unknown : version.ToString();
        }
        catch
        {
            return Unknown;
        }
    }

    /// <summary>
    /// Reads one build-time metadata value from the stamped assembly.
    /// </summary>
    /// <param name="key">
    /// The metadata key, as spelled in Directory.Build.props.
    /// </param>
    /// <returns>
    /// The value, or "unknown".
    /// </returns>
    /// <remarks>
    /// An indexed loop over the attributes rather than LINQ, and the whole thing is guarded: this
    /// runs once at startup and must not be the reason a server fails to come up.
    /// </remarks>
    private static string ReadMetadata(string key)
    {
        try
        {
            var attributes = Stamped.GetCustomAttributes<AssemblyMetadataAttribute>();
            if (attributes == null) return Unknown;

            // GetCustomAttributes hands back an enumerable, so this is materialised once into an
            // array and then walked by index - the codebase's rule, and it keeps the loop plain.
            var all = new System.Collections.Generic.List<AssemblyMetadataAttribute>(attributes).ToArray();
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].Key, key, StringComparison.Ordinal))
                {
                    return string.IsNullOrEmpty(all[i].Value) ? Unknown : all[i].Value!;
                }
            }

            return Unknown;
        }
        catch
        {
            return Unknown;
        }
    }

    /// <summary>
    /// Builds the compact name.
    /// </summary>
    /// <returns>
    /// Version, commit and build time in one short string.
    /// </returns>
    private static string BuildShort()
    {
        var dirty = IsDirty ? "+" : string.Empty;
        return $"{Version}+{Commit}{dirty} ({BuildDate} {BuildTime})";
    }
}
