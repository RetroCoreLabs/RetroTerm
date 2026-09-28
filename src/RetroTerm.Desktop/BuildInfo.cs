using RetroTerm.Core;

namespace RetroTerm.Desktop;

/// <summary>
/// Build information for the welcome screen and the status line, read from the assembly stamp.
/// </summary>
/// <remarks>
/// <para><b>Nothing here is a second copy of anything</b></para>
/// Until 28 September 2026 this class held its own version string, "1.0.0-alpha", and a
/// "Phase 4 - Canvas Rendering" label, and took the build time from the exe's last-write time.
/// The assembly meanwhile said 1.10.26.9, so the welcome screen and Help, About disagreed. Every
/// value now comes from <see cref="BuildIdentity"/>, which reads what Directory.Build.props
/// stamped into the assembly at build time: the version, the copyright, and the build date and
/// time MSBuild recorded, plus the commit the build came from.
/// </remarks>
public static class BuildInfo
{
    /// <summary>
    /// The version, from Directory.Build.props.
    /// </summary>
    public static string Version => BuildIdentity.Version;

    /// <summary>
    /// The build date and time MSBuild recorded, as "dd.MMM.yyyy HH:mm:ss".
    /// </summary>
    public static string BuildDateTimeString => BuildIdentity.BuildDate + " " + BuildIdentity.BuildTime;

    /// <summary>
    /// The copyright line, from Directory.Build.props.
    /// </summary>
    public static string Copyright => BuildIdentity.Copyright;

    /// <summary>
    /// One line naming the build: version, commit, and whether the tree was clean.
    /// </summary>
    public static string FullVersion => BuildIdentity.Describe();
}
