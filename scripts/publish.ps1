# Publish RetroTerm to a VERSIONED folder, then point publish\current at it.
#
# WHY THIS EXISTS
# ---------------
# publish\ used to be flat: one folder holding RetroTerm.Desktop.exe. A self-contained single-file
# exe holds its own image open while it runs, so republishing over a running RetroTerm fails - and
# the workaround became a paragraph in the by-hand run sheet headed "Before we start - one thing
# needs you", whose entire content is asking Ronny to close the app by hand. A workaround promoted
# to a procedure.
#
# Publishing into publish\versions\<version>-<n>\ never touches a folder anybody is running from.
# The junction publish\current is then repointed, which SUCCEEDS while the old build runs: a
# running process holds the resolved file, not the link that found it.
#
# WHAT THIS DOES NOT DO
# ---------------------
# It does not close, kill or restart anything. The old build keeps running until Ronny closes it
# himself. The never-kill rule stands; this script removes the REASON the question kept being asked,
# not the rule.
#
# The legacy publish\RetroTerm.Desktop.exe is left exactly where it is and is never written to.
#
# USAGE
#   .\scripts\publish.ps1              # publish, repoint current, print the path
#   .\scripts\publish.ps1 -KeepLast 5  # prune older versioned folders, keeping the newest 5
#
# A junction (not a symlink) is used on purpose: junctions need no administrator rights and no
# developer mode, and every Windows API resolves them.

[CmdletBinding()]
param(
    # How many versioned folders to keep. Older ones are deleted after a successful publish.
    # A folder that is in use cannot be deleted, and that failure is reported, not swallowed.
    [int]$KeepLast = 3,

    # Build configuration.
    [string]$Configuration = "Release",

    # Runtime identifier.
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent $PSScriptRoot
$project    = Join-Path $repoRoot "src\RetroTerm.Desktop\RetroTerm.Desktop.csproj"
$publishDir = Join-Path $repoRoot "publish"
$versionDir = Join-Path $publishDir "versions"
$current    = Join-Path $publishDir "current"

if (-not (Test-Path $project)) { throw "cannot find $project" }

# The version comes from Directory.Build.props, which is the single owner of it. Read it rather
# than passing one in, so a published folder can never be named something the binary disagrees with.
$propsPath = Join-Path $repoRoot "Directory.Build.props"
[xml]$props = Get-Content $propsPath
$version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "no <Version> in $propsPath" }

# Two publishes of the same version must not collide - the second would be writing into a folder
# the first might still be running from. Suffix with the first free number.
$n = 1
while (Test-Path (Join-Path $versionDir "$version-$n")) { $n++ }
$target = Join-Path $versionDir "$version-$n"

Write-Host "Publishing $version to $target"

# -nodeReuse:false because MSBuild worker nodes otherwise pile up and survive build-server shutdown.
& dotnet publish $project -c $Configuration -r $Runtime -o $target -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $target "RetroTerm.Desktop.exe"
if (-not (Test-Path $exe)) { throw "publish reported success but $exe is not there" }

# Repoint the junction. Removing it deletes the LINK, never the target contents - a junction's
# contents live at its target, not inside the link itself.
#
# Reproduced three times, 1 September 2026: Remove-Item $current -Force threw
# "NullReferenceException" on this exact junction every time, with RetroTerm running from it.
# [System.IO.Directory]::Delete($current, $false) removes the same link without going through
# whatever Remove-Item's -Force path does that trips over it. Confirmed to still refuse to
# touch a link's contents (the $false is non-recursive, same as Remove-Item without -Recurse).
if (Test-Path $current) {
    $existing = Get-Item $current -Force
    if ($existing.LinkType -ne "Junction") {
        throw "$current exists and is not a junction. Move it aside by hand; this script will not delete a real folder."
    }
    [System.IO.Directory]::Delete($current, $false)
}
New-Item -ItemType Junction -Path $current -Target $target | Out-Null

Write-Host ""
Write-Host "Published:  $exe"
Write-Host "Launch via: $current\RetroTerm.Desktop.exe"
Write-Host ""
Write-Host "The build that is already running is untouched and still running. Close it when you"
Write-Host "are ready; nothing here will close it for you."

# Prune, oldest first. A folder still in use will refuse to delete - say so rather than hiding it,
# because a folder that will not delete is usually a RetroTerm somebody forgot is open.
$all = Get-ChildItem $versionDir -Directory | Sort-Object CreationTime -Descending
if ($all.Count -gt $KeepLast) {
    $stale = $all | Select-Object -Skip $KeepLast
    for ($i = 0; $i -lt $stale.Count; $i++) {
        $folder = $stale[$i]
        try {
            Remove-Item $folder.FullName -Recurse -Force -ErrorAction Stop
            Write-Host "Pruned $($folder.Name)"
        }
        catch {
            Write-Host "Could not prune $($folder.Name) - something is using it (a running RetroTerm?)"
        }
    }
}
