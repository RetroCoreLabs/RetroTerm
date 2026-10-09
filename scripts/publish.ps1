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
# Publishing into publish\versions\<version>\ (replacing the folder of the same version) and then
# repointing the junction publish\current SUCCEEDS while a RetroTerm runs from the junction: a
# running process holds the resolved file, not the link that found it. Replacing the VERSION FOLDER
# does not succeed while a RetroTerm runs from that folder itself - see below.
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

# The folder is named by the version alone: publish\versions\<version>\. Publishing the same
# version again REPLACES that folder (changed 8 October 2026; it used to add -1, -2, ... so a second
# publish never touched a folder somebody might be running from).
#
# The new build is made in a staging folder first, so a failed build leaves the old folder alone.
# Only then is the old folder deleted and the staging folder renamed into its place. If the old
# folder cannot be deleted because a RetroTerm is running from it (its exe is locked), the script
# puts the junction back where it was, removes the staging folder and stops with a message.
$target  = Join-Path $versionDir $version
$staging = Join-Path $versionDir "$version.new"

if (-not (Test-Path $versionDir)) { New-Item -ItemType Directory -Path $versionDir | Out-Null }
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host "Publishing $version to $target"

# -nodeReuse:false because MSBuild worker nodes otherwise pile up and survive build-server shutdown.
& dotnet publish $project -c $Configuration -r $Runtime -o $staging -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if (-not (Test-Path (Join-Path $staging "RetroTerm.Desktop.exe"))) {
    throw "publish reported success but there is no RetroTerm.Desktop.exe in $staging"
}

# The junction publish\current may point at the folder about to be replaced, so take it down first
# and remember where it pointed. Removing it deletes the LINK, never the target contents - a
# junction's contents live at its target, not inside the link itself.
#
# Reproduced three times, 1 September 2026: Remove-Item $current -Force threw
# "NullReferenceException" on this exact junction every time, with RetroTerm running from it.
# [System.IO.Directory]::Delete($current, $false) removes the same link without going through
# whatever Remove-Item's -Force path does that trips over it. Confirmed to still refuse to
# touch a link's contents (the $false is non-recursive, same as Remove-Item without -Recurse).
$previousTarget = $null
if (Test-Path $current) {
    $existing = Get-Item $current -Force
    if ($existing.LinkType -ne "Junction") {
        throw "$current exists and is not a junction. Move it aside by hand; this script will not delete a real folder."
    }
    $previousTarget = $existing.Target
    if ($previousTarget -is [array]) { $previousTarget = $previousTarget[0] }
    [System.IO.Directory]::Delete($current, $false)
}

if (Test-Path $target) {
    # RENAME the old folder out of the way instead of deleting it in place. A recursive delete
    # removes the files it can and stops at the one that is locked, leaving a folder with half its
    # files gone; a rename either happens whole or not at all. Found by testing it with a file held
    # open, 8 October 2026: the folder was left with one file where it had had three.
    $aside = Join-Path $versionDir "$version.old"
    if (Test-Path $aside) { Remove-Item $aside -Recurse -Force }
    try {
        # [System.IO.Directory]::Move is one rename. Move-Item on a folder moves it file by file
        # and was seen to leave the locked exe behind with the other files gone.
        [System.IO.Directory]::Move($target, $aside)
    }
    catch {
        # Put things back as they were, so the app can still be launched from publish\current.
        if ($previousTarget -and (Test-Path $previousTarget)) {
            New-Item -ItemType Junction -Path $current -Target $previousTarget | Out-Null
        }
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        throw "Cannot replace $target - a RetroTerm is running from it. Close that RetroTerm and run this script again. Nothing was changed."
    }

    Move-Item $staging $target
    try { Remove-Item $aside -Recurse -Force -ErrorAction Stop }
    catch { Write-Host "Could not remove $aside - something is using it. Delete it by hand later." }
}
else {
    Move-Item $staging $target
}
New-Item -ItemType Junction -Path $current -Target $target | Out-Null
$exe = Join-Path $target "RetroTerm.Desktop.exe"

Write-Host ""
Write-Host "Published:  $exe"
Write-Host "Launch via: $current\RetroTerm.Desktop.exe"
Write-Host ""
Write-Host "A RetroTerm that was already open is a copy of an OLDER build (this publish would have"
Write-Host "stopped if one were running from this version's folder). Close it and start it again"
Write-Host "from the path above to get this one; nothing here will close it for you."

# Prune, oldest first. A folder still in use will refuse to delete - say so rather than hiding it,
# because a folder that will not delete is usually a RetroTerm somebody forgot is open.
$all = Get-ChildItem $versionDir -Directory | Where-Object { $_.Name -notlike "*.new" -and $_.Name -notlike "*.old" } | Sort-Object CreationTime -Descending
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
