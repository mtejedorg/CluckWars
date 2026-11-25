<#
.SYNOPSIS
  BL-208 runs on Windows EXE clients only (no adb): accelerated solo runs and the four-client fighter session.

.DESCRIPTION
  Modes (dev-tools Windows build, looped via PLAY AGAIN; every run ends with stop.ps1 and verify.ps1, then the session folder is
  copied to Captures\bots-<date>\<Label>):
    timescale  one EXE, Play Solo vs 3 bots, fixed seed and fixed kits, at -Scale (cw_timescale). Run it at 1, 5 and 10 with the
               same -Seed / -Perk / -BotPerks / -Personality and compare the per-seat banked food with tools\test-session\bots-compare.py.
    fighters   four EXE clients (host + 3 joiners), each with its own kit and personality:
               client1 Assassin_Spoiler/fighter (host), client2 Warrior_Bully/fighter, client3 Fatty_Hoarder/runner,
               client4 Speedy_Slippery/runner. The Telemetry lines (steal, peckcut, kill, contact) land in every client log.

.PARAMETER Mode         timescale | fighters
.PARAMETER Scale        cw_timescale for -Mode timescale (1..20); solo only, ignored by the game in shared matches.
.PARAMETER Seed         cw_seed for -Mode timescale: fixes the map, corners and bot kit lineup.
.PARAMETER Perk         -Mode timescale: the human seat's specialization (ChickenSubclass name).
.PARAMETER BotPerks     -Mode timescale: the bots by index, e.g. 'Warrior_Bully,Fatty_Hoarder,Speedy_Slippery'.
.PARAMETER Personality  -Mode timescale: cw_personality of the human seat (aggressive | runner | balanced | fighter).
.PARAMETER Rounds       rounds to play (cw_autoloop).
.PARAMETER Label        folder name under Captures\bots-<date>\ (default: mode and scale).
.PARAMETER MaxMinutes   hard stop for the run.
#>
param(
    [ValidateSet('timescale','fighters')][string]$Mode = 'timescale',
    [double]$Scale = 1,
    [int]$Seed = 20261121,
    [string]$Perk = 'Warrior_Enforcer',
    [string]$BotPerks = 'Warrior_Bully,Fatty_Hoarder,Speedy_Slippery',
    [string]$Personality = 'runner',
    [int]$Rounds = 3,
    [string]$Label = '',
    [int]$MaxMinutes = 10,
    [int]$Width = 960,
    [int]$Height = 540,
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$root = $script:RepoRoot
$exe  = Join-Path $root 'Builds\Windows\CluckWars.exe'
if (-not (Test-Path $exe)) { throw "no Windows build at $exe (batch BuildWindows with -cwDevTools first)" }
if (-not $OutDir) { $OutDir = Join-Path $root 'Captures\bots-2026-11-21' }
if (-not $Label) { $Label = if ($Mode -eq 'timescale') { "timescale-x$Scale" } else { 'fighters' } }

Get-Process CluckWars -ErrorAction SilentlyContinue | Stop-Process -Force
& "$PSScriptRoot\start.ps1" -NoRecord | Out-Null
$S = (Get-ChildItem (Join-Path $root 'Captures') -Directory | Where-Object { $_.Name -like 'session-*' } | Sort-Object Name | Select-Object -Last 1).FullName
Write-Host "session: $S   mode: $Mode   label: $Label"

function Start-Exe([string]$name, [string[]]$extra) {
    $a = @('-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'-logFile',"`"$S\$name.log`"",'-cw_autoplay','-cw_hud','-cw_profile',$name) + $extra
    Start-Process $exe -ArgumentList $a | Out-Null
}
function Wait-Code([string]$log) {
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep 2
        $m = Select-String -Path $log -Pattern 'AutoTest code=(\w+)' -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($m) { return $m.Matches[0].Groups[1].Value }
    }
    return $null
}

if ($Mode -eq 'timescale') {
    Start-Exe 'client1' @('-cw_autosolo','-cw_autoloop',"$Rounds",'-cw_perk',$Perk,'-cw_botperks',$BotPerks,'-cw_personality',$Personality,
        '-cw_seed',"$Seed",'-cw_timescale',"$Scale")
} else {
    Start-Exe 'client1' @('-cw_autohost','-cw_perk','Assassin_Spoiler','-cw_personality','fighter','-cw_autostart','4','-cw_autostart_timeout','120','-cw_autoloop',"$Rounds")
    $code = Wait-Code "$S\client1.log"
    if (-not $code) { throw 'no join code appeared' }
    Write-Host "join code: $code"
    Start-Exe 'client2' @('-cw_autojoin',$code,'-cw_perk','Warrior_Bully','-cw_personality','fighter','-cw_autoloop',"$Rounds")
    Start-Exe 'client3' @('-cw_autojoin',$code,'-cw_perk','Fatty_Hoarder','-cw_personality','runner','-cw_autoloop',"$Rounds")
    Start-Exe 'client4' @('-cw_autojoin',$code,'-cw_perk','Speedy_Slippery','-cw_personality','runner','-cw_autoloop',"$Rounds")
}

$deadline = (Get-Date).AddMinutes($MaxMinutes)
$main = "$S\client1.log"
while ((Get-Date) -lt $deadline) {
    Start-Sleep 5
    $ended = (Select-String -Path $main -Pattern 'Match ended' -ErrorAction SilentlyContinue | Measure-Object).Count
    $done  = (Select-String -Path $main -Pattern 'AutoTest loop done|loop round=\d+/\d+.*done' -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($done -gt 0 -or $ended -ge $Rounds) { Start-Sleep 8; break }
}
Get-Process CluckWars -ErrorAction SilentlyContinue | Stop-Process -Force
& "$PSScriptRoot\stop.ps1" -Session $S | Out-Null
& "$PSScriptRoot\verify.ps1" -Session $S -RequireUi:$false
$verdict = $LASTEXITCODE
New-Item -ItemType Directory -Force (Join-Path $OutDir $Label) | Out-Null
Copy-Item (Join-Path $S '*') (Join-Path $OutDir $Label) -Recurse -Force
Write-Host "copied to $(Join-Path $OutDir $Label)"
exit $verdict
