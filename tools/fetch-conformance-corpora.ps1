<#
.SYNOPSIS
    Fetches the outside conformance corpora the tests run against.

.DESCRIPTION
    These corpora belong to other projects and are NOT committed to this repository. Nothing from
    another codebase lives here: the emulator is written from manuals and specifications, and these
    files are fetched only to be run against it.

    What is fetched:

      xterm.js escape-sequence fixtures  MIT   76 byte streams + 77 expected screens, captured
                                               from a real xterm at 80x25
      libvterm test scripts              MIT   44 hand-written conformance scripts
      vt340test sixel fixtures           CC0   .six streams plus the reference pictures of what
                                               a REAL VT340/VT240 made of them

    Both land under tests\RetroTerm.Tests\Conformance\, which .gitignore keeps out of the
    repository. The tests skip themselves when a corpus is absent, so a clean clone still builds
    and runs; it simply has fewer outside opinions to check against.

.PARAMETER Force
    Re-download even when the files are already present.

.EXAMPLE
    pwsh tools\fetch-conformance-corpora.ps1
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$conformance = Join-Path $repoRoot 'tests\RetroTerm.Tests\Conformance'

function Save-File {
    param([string]$Url, [string]$Path)

    if ((Test-Path $Path) -and -not $Force) {
        return $false
    }

    Invoke-WebRequest -Uri $Url -OutFile $Path -TimeoutSec 120 -UserAgent 'RetroTerm-corpus-fetch'
    return $true
}

# ── xterm.js escape-sequence fixtures ────────────────────────────────────────────────────
$xtermDir = Join-Path $conformance 'xtermjs'
New-Item -ItemType Directory -Force $xtermDir | Out-Null

Write-Host 'Fetching xterm.js escape-sequence fixtures...'
$api = 'https://api.github.com/repos/xtermjs/xterm.js/contents/test/fixtures/escape_sequence_files'
$listing = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'RetroTerm-corpus-fetch' } -TimeoutSec 120

$fetched = 0
foreach ($entry in $listing) {
    if ($entry.name -like '*.in' -or $entry.name -like '*.text' -or $entry.name -eq 'NOTES') {
        if (Save-File -Url $entry.download_url -Path (Join-Path $xtermDir $entry.name)) { $fetched++ }
    }
}

# The licence travels with the files it covers.
Save-File -Url 'https://raw.githubusercontent.com/xtermjs/xterm.js/master/LICENSE' `
          -Path (Join-Path $xtermDir 'LICENSE-xtermjs.txt') | Out-Null

$inCount = (Get-ChildItem $xtermDir -Filter *.in).Count
Write-Host "  $inCount .in files present ($fetched newly fetched)"

# ── libvterm conformance scripts ─────────────────────────────────────────────────────────
$vtermDir = Join-Path $conformance 'libvterm'
New-Item -ItemType Directory -Force $vtermDir | Out-Null

Write-Host 'Fetching libvterm test scripts...'
$api = 'https://api.github.com/repos/neovim/libvterm/contents/t'
$listing = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'RetroTerm-corpus-fetch' } -TimeoutSec 120

$fetched = 0
foreach ($entry in $listing) {
    if ($entry.name -like '*.test') {
        if (Save-File -Url $entry.download_url -Path (Join-Path $vtermDir $entry.name)) { $fetched++ }
    }
}

Save-File -Url 'https://raw.githubusercontent.com/neovim/libvterm/master/LICENSE' `
          -Path (Join-Path $vtermDir 'LICENSE-libvterm.txt') | Out-Null

$testCount = (Get-ChildItem $vtermDir -Filter *.test).Count
Write-Host "  $testCount .test files present ($fetched newly fetched)"

# ── vt340test Sixel fixtures ─────────────────────────────────────────────────────────────
#
# hackerb9's vt340test is a collection of streams run against a REAL VT340 (and a real VT240),
# with the resulting screen photographed or captured beside each one. That pairing is what makes
# it worth having: the .six is the input, the .png is what the hardware actually did with it.
#
# The .png files are NOT pixel-comparison targets. Several are photographs of a CRT, so they
# differ from any emulator's output in geometry, focus and colour temperature. They are here to
# be LOOKED AT next to ours.
#
# CC0 1.0 - public domain dedication, so there is no licence question about fetching them. They
# are still not committed: nothing from another project lives in this repository.
$sixelDir = Join-Path $conformance 'vt340test'
New-Item -ItemType Directory -Force $sixelDir | Out-Null

Write-Host 'Fetching vt340test Sixel fixtures...'
$api = 'https://api.github.com/repos/hackerb9/vt340test/contents/sixeltests'
$listing = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'RetroTerm-corpus-fetch' } -TimeoutSec 120

$fetched = 0
foreach ($entry in $listing) {
    if ($entry.type -ne 'file') { continue }
    if ($entry.name -like '*.six' -or $entry.name -like '*.png' -or $entry.name -eq 'encoding.md' -or $entry.name -eq 'sixelcomments.md') {
        if (Save-File -Url $entry.download_url -Path (Join-Path $sixelDir $entry.name)) { $fetched++ }
    }
}

Save-File -Url 'https://raw.githubusercontent.com/hackerb9/vt340test/master/LICENSE' `
          -Path (Join-Path $sixelDir 'LICENSE-vt340test.txt') | Out-Null

$sixCount = (Get-ChildItem $sixelDir -Filter *.six).Count
Write-Host "  $sixCount .six files present ($fetched newly fetched)"

# ── vt340test ReGIS fixtures ─────────────────────────────────────────────────────────────
#
# The same repository's OTHER half, and the one that judges the text command. hersheydemo.regis is
# 44 KB of Hershey font output - far more text than anything written here exercises - and
# registest.sh writes the grid, checkerboard, bitplane and raster pictures whose reference captures
# sit beside it as registest-*.png.
#
# Same rules as the Sixel half: CC0, fetched rather than committed, and the captures are there to
# be LOOKED AT rather than compared pixel for pixel.
$regisDir = Join-Path $conformance 'vt340regis'
New-Item -ItemType Directory -Force $regisDir | Out-Null

Write-Host 'Fetching vt340test ReGIS fixtures...'
$api = 'https://api.github.com/repos/hackerb9/vt340test/contents/regis'
$regisFetched = 0

try {
    $listing = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'RetroTerm-corpus-fetch' } -TimeoutSec 120

    foreach ($entry in $listing) {
        if ($entry.type -ne 'file') { continue }
        if ($entry.name -like '*.regis' -or $entry.name -like '*.png' -or $entry.name -like '*.sh' -or $entry.name -like '*.md') {
            if (Save-File -Url $entry.download_url -Path (Join-Path $regisDir $entry.name)) { $regisFetched++ }
        }
    }

    $regisCount = (Get-ChildItem $regisDir -Filter *.regis).Count
    Write-Host "  $regisCount .regis files present ($regisFetched newly fetched)"

    # ── registest.sh, run once so its drawings become fixtures ───────────────────────────
    #
    # Four of the pictures in this corpus have reference captures taken from a real VT340 -
    # registest-grid.png, -checkerboard.png, -bitplane.png and -raf.png - and the ReGIS that
    # produced them lives inside a SHELL SCRIPT with loops and variables rather than in a file.
    #
    # So the script is RUN and its output kept. Transcribing it into C# instead would mean copying
    # somebody else's test content into this repository, which the rule here forbids; running it is
    # the same thing the author does, and what comes out is data.
    #
    # Bash comes with Git for Windows. When it is missing this step is skipped and says so - the
    # tests skip any fixture that is not there, so nothing breaks.
    # GIT's bash, by preference and by name. The `bash` on PATH here is WSL's, which cannot see a
    # Windows drive letter at all - it answered "cd: E:/...: No such file or directory" - so asking
    # the PATH first gets the wrong shell on any machine with WSL installed.
    #
    # Written the long way round for the same reason as the loop below: this script has to run under
    # Windows PowerShell 5.1 as well as pwsh, and 5.1 has no null-conditional operator.
    $bash = $null
    foreach ($candidate in @(
        "$env:ProgramFiles\Git\bin\bash.exe",
        "${env:ProgramFiles(x86)}\Git\bin\bash.exe",
        "$env:LOCALAPPDATA\Programs\Git\bin\bash.exe")) {
        if (Test-Path $candidate) { $bash = $candidate; break }
    }

    if (-not $bash) {
        Write-Host '  Git bash not found, so registest.sh was not run - its four pictures will be skipped'
    }
    elseif (Test-Path (Join-Path $regisDir 'registest.sh')) {
        Write-Host 'Running registest.sh to capture the drawings its reference pictures came from...'
        $made = 0

        foreach ($picture in @('grid', 'checkerboard', 'bitplane', 'raf', 'plaid')) {
            $out = Join-Path $regisDir "registest-$picture.regis"
            if ((Test-Path $out) -and -not $Force) { continue }

            # The script reads a keypress in one of its functions, so stdin is closed rather than
            # left to wait. Its own diagnostics go to stderr and are dropped; only the ReGIS matters.
            $drawing = & $bash -c "cd '$($regisDir -replace '\\', '/')' && ./registest.sh $picture </dev/null 2>/dev/null"

            # A function that produced nothing leaves no file at all, rather than an empty one for
            # the tests to puzzle over. Empty output also means Set-Content writes nothing, so the
            # existence check has to come before Get-Item or it throws.)
            if ($drawing) {
                $drawing -join "`n" | Set-Content -Path $out -NoNewline -Encoding ascii
                if ((Test-Path $out) -and (Get-Item $out).Length -gt 0) { $made++ }
            }
        }

        Write-Host "  $made drawings captured"
    }
}
catch {
    # A missing directory upstream is not a failure of this script. The tests skip a corpus that is
    # not there, so say what happened and carry on rather than stopping the whole fetch.
    Write-Host "  ReGIS fixtures could not be fetched: $($_.Exception.Message)"
}

Write-Host ''
Write-Host 'Done. Run the tests as usual; the conformance runners will pick these up.'
