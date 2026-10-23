<#
.SYNOPSIS
  PASS/FAIL verdict for a merged test session (reads findings.txt written by merge.ps1). Exit code 0 = PASS.

.DESCRIPTION
  A session FAILS on any of: ERROR, EXCEPTION, SYNC (cross-peer mismatch), GHOST, INVARIANT (world-state rule broken
  at a round start), UI (an in-match HUD layout offender, WP8 U1-U6), SHA mismatch between sources, or fewer than
  -MinRounds completed matches in the logs. With -RequireUi it also fails when no source logged a single in-match HUD
  layout scan (a build without the WP8 probe proves nothing about the layout); scenario-pc.ps1 passes it.
  WARN/PERF/VISUAL never fail a run by themselves (they are listed for triage). The point of this script is that a
  human (or an agent) does NOT decide by skimming logs: the rules decide, and every new bug class that gets a rule in
  merge.ps1 / the in-game Invariant probe becomes a permanent gate.
#>
param(
    [Parameter(Mandatory)][string]$Session,
    [int]$MinRounds = 2,
    [switch]$RequireUi
)
$f = Join-Path $Session 'findings.txt'
if (-not (Test-Path $f)) { Write-Host "no findings.txt in $Session (run stop.ps1/merge.ps1)"; exit 2 }
$txt = Get-Content $f -Raw
$cats = @{}
foreach ($m in [regex]::Matches($txt, '(?m)^\s*per category : (.*)$')) {
    foreach ($kv in ($m.Groups[1].Value -split '\s+' | Where-Object { $_ -match '=' })) { $k, $v = $kv -split '='; $cats[$k] = [int]$v }
}
$merged = Join-Path $Session 'merged.log'
$rounds = 0
if (Test-Path $merged) { $rounds = (Select-String -Path $merged -Pattern 'Match ended' | ForEach-Object { $_.Line -replace '^\S+ \S+ \[(\S+)\s*\].*', '$1' } | Group-Object | Sort-Object Count -Descending | Select-Object -First 1).Count }
$fails = New-Object System.Collections.Generic.List[string]
foreach ($c in 'ERROR', 'EXCEPTION', 'SYNC', 'GHOST', 'INVARIANT', 'UI') { if ($cats[$c] -gt 0) { $fails.Add("$c=$($cats[$c])") } }
if ($txt -match 'SHA MISMATCH') { $fails.Add('SHA mismatch between sources') }
if ($rounds -lt $MinRounds) { $fails.Add("only $rounds completed match(es) seen, expected >= $MinRounds") }
$uiScans = 0
$um = [regex]::Match($txt, '(?m)^\s*UI hud scans : (\d+)')
if ($um.Success) { $uiScans = [int]$um.Groups[1].Value }
if ($RequireUi -and $uiScans -eq 0) { $fails.Add('no UiProbe hud scan (build without the WP8 probe, heavy probes off, or no Active round reached)') }

Write-Host ''
Write-Host ('categories : ' + (($cats.Keys | Sort-Object | ForEach-Object { "$_=$($cats[$_])" }) -join '  '))
Write-Host "matches    : $rounds (most-reporting source)"
Write-Host "ui scans   : $uiScans in-match HUD layout scan(s)$(if ($RequireUi) { ' (required)' })"
if ($fails.Count -eq 0) { Write-Host 'VERDICT: PASS' -ForegroundColor Green; exit 0 }
Write-Host ('VERDICT: FAIL - ' + ($fails -join '; ')) -ForegroundColor Red
foreach ($l in (Select-String -Path $f -Pattern '^\[(INVARIANT|ERROR|EXCEPTION|SYNC|GHOST|UI)\]' | Select-Object -First 8)) { Write-Host ('  ' + $l.Line.Substring(0, [Math]::Min(200, $l.Line.Length))) }
exit 1
