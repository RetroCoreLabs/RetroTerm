# Walks the M1-M8 by-hand documents, finds every case whose Result line is still blank, and
# reports what machine cover it claims. Nothing is judged here - the point is to separate
# "covered by a test that exists" from "nobody has looked at this at all".

$docs = Get-ChildItem 'E:\Dev\Ronny\RetroTerm\docs\manual-tests\M*.md' | Sort-Object Name
$testRoot = 'E:\Dev\Ronny\RetroTerm\tests'

# Every test method and class name in the suite, so a claimed cover can be checked for existence.
$allTestText = New-Object System.Text.StringBuilder
Get-ChildItem $testRoot -Recurse -Filter *.cs | ForEach-Object {
    [void]$allTestText.Append([IO.File]::ReadAllText($_.FullName))
}
$suite = $allTestText.ToString()

$rows = @()

foreach ($doc in $docs) {
    $lines = [IO.File]::ReadAllLines($doc.FullName)
    $currentCase = '(before any case)'
    $cover = ''
    $judge = ''

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]

        if ($line -match '^#{2,4}\s+(M[0-9][^\s]*)\s*[-—–]?\s*(.*)$') {
            $currentCase = $Matches[1]
            $cover = ''
            $judge = ''
            continue
        }

        if ($line -match '^\*\*Machine cover:\*\*\s*(.*)$') { $cover = $Matches[1] }
        if ($line -match '^\*\*Judge:\*\*\s*(.*)$') { $judge = $Matches[1] }

        if ($line -match '^\*\*Result:\*\*\s+_{4,}') {
            # Pull the identifiers out of the cover text - backticked names are the convention.
            $names = [regex]::Matches($cover, '`([A-Za-z0-9_][A-Za-z0-9_.]*)`') | ForEach-Object { $_.Groups[1].Value }
            $found = @()
            $missing = @()
            foreach ($rawName in $names) {
                $n = ($rawName -split '\.')[0]
                if ($n.Length -lt 4) { continue }
                if ($suite.Contains($n)) { $found += $n } else { $missing += $n }
            }

            $state = if ($names.Count -eq 0) { 'NO COVER CLAIMED' }
                     elseif ($missing.Count -gt 0) { 'COVER CLAIMED BUT MISSING' }
                     else { 'covered' }

            $rows += [pscustomobject]@{
                Doc     = $doc.Name -replace '\.md$',''
                Case    = $currentCase
                State   = $state
                Judge   = $judge
                Missing = ($missing -join ', ')
            }
        }
    }
}

"TOTAL blank-result cases: $($rows.Count)"
""
"By state:"
$rows | Group-Object State | Sort-Object Count -Descending | ForEach-Object { "  {0,-28} {1}" -f $_.Name, $_.Count }
""
"=== COVER CLAIMED BUT THE NAMED TEST DOES NOT EXIST ==="
$rows | Where-Object { $_.State -eq 'COVER CLAIMED BUT MISSING' } | ForEach-Object {
    "  {0} {1}  -> missing: {2}" -f $_.Doc, $_.Case, $_.Missing
}
""
"=== NO COVER CLAIMED AT ALL ==="
$rows | Where-Object { $_.State -eq 'NO COVER CLAIMED' } | ForEach-Object {
    "  {0} {1}  (judge: {2})" -f $_.Doc, $_.Case, $_.Judge
}
