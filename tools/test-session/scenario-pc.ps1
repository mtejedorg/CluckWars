<#
.SYNOPSIS
  PC-only, zero-interaction scenario runner with a PASS/FAIL verdict (exit code 0 = pass).

.DESCRIPTION
  Modes (all Windows EXE dev-tools builds, looped via PLAY AGAIN):
    solo      one EXE, Play Solo vs 3 bots                       (cw_autosolo)
    shared2   host + 1 joiner
    shared4   host + 3 joiners (the multi-peer reference run)
    hostleave shared4, the host EXE is killed mid-round-1 (master migration)
  Every run ends with stop.ps1 (merge) and verify.ps1 (verdict). The verdict is what counts:
  a log line saying something happened is NOT evidence, merge.ps1's INVARIANT checks look at world STATE.

.PARAMETER Mode    solo | shared2 | shared4 | hostleave
.PARAMETER Rounds  rounds to play (auto-loop). Default 3.
.PARAMETER AutoZone  B10: every EXE gets -cw_autozone (each peer casts its zone ability ~1.5 s before every round end and
           PLAY AGAIN follows 1.5 s later), so the next round-start Invariant line must read zn=0. Root Egg (Fatty) is the
           reliable carrier (8 s): in solo the class becomes Fatty unless -Class is given; shared2's joiner is a Speedy (Feather
           Trap, 5 s), shared4/hostleave add a Fatty (joiner 2) and an Assassin (Doppelganger decoy, 4 s, may expire first).
.PARAMETER AutoAura  WP4: the Speedy joiner (client2) gets -cw_autoaura: every round it walks into the nearest rival, casts
           Feather Aura there and keeps walking into it; every peer's Invariant probe samples that rival's replicated
           Snared flag, and merge.ps1 requires one 'Invariant aura ... remote=1 ... verdict=ok' line once an aura was cast.
           In solo the class becomes Speedy unless -Class is given (victims are bots on the same peer: remote=0, so the
           remote-victim rule can only be satisfied in shared2/shared4/hostleave). Not together with -AutoZone.
.PARAMETER Width   window width of every EXE (default 960). Use 540x960 / 1080x2400 for portrait checks.
.PARAMETER Height  window height of every EXE (default 540).
.PARAMETER ExtraArgs  one space-separated string appended to EVERY EXE's args, e.g. HUD band screenshots (dev-tools build):
           -ExtraArgs '-cw_shots 15,22,29,36 -cw_band 290 -cw_zones -cw_shottag band290'  (PNGs land in <session>\shots)
           Rival arrows: every build paints them on a floor ring round your chicken (decision 2a); -cw_edgearrows (dev tools
           only) switches back to the screen-edge chevrons for comparisons. A/B, same seconds, one run each:
           -ExtraArgs '-cw_shots 15,22,29,36 -cw_zones -cw_shottag floor'                  (default: floor arrows)
           -ExtraArgs '-cw_shots 15,22,29,36 -cw_zones -cw_shottag edge -cw_edgearrows'    (edge chevrons)
           The log says which one ran ("AutoTest arrows=floor|edge") and the ring radius in use ("Floor ring radius=").
           WP8 layout keys (dev tools): -cw_touchhud (check the phone's thumb-arc HUD instead of the desktop strip),
           -cw_menushots [main,settings,disconnect,class,gear,coop,coopjoin] (menu-<page>.png + one UiProbe scan per page
           before the menu flow starts), -cw_safearea l,b,r,t (simulated insets, screen px). tools\ui_capture\capture_orientation.py
           runs the size matrix with them.
.PARAMETER NoUiRequirement  do not require an in-match HUD layout scan (WP8 UiProbe) in the logs, for builds older than
           WP8. UI offenders still fail the run: every scope=hud 'ui offenders=N' line with N > 0 is a UI finding, at any
           -Width/-Height (portrait included).
#>
param(
    [ValidateSet('solo','shared2','shared4','hostleave')][string]$Mode = 'shared4',
    [int]$Rounds = 3,
    [int]$MaxMinutes = 8,
    [string]$Class = 'Warrior',
    [string]$JoinerExtra = '',  # extra EXE args (space separated) for every joiner, e.g. -JoinerExtra '-cw_autodisconnect 25'
    [switch]$AutoZone,
    [switch]$AutoAura,
    [int]$Width = 960,
    [int]$Height = 540,
    [string]$ExtraArgs = '',    # extra EXE args (space separated) for every EXE, e.g. -ExtraArgs '-cw_shots 15,22 -cw_band 290 -cw_zones'
    [switch]$NoUiRequirement
)
if ($AutoZone -and $Mode -eq 'solo' -and -not $PSBoundParameters.ContainsKey('Class')) { $Class = 'Fatty' }
$zoneArgs = @(if ($AutoZone) { '-cw_autozone' })
if ($AutoAura -and $AutoZone) { throw '-AutoAura and -AutoZone both reserve one ability slot; run them separately' }
if ($AutoAura -and $Mode -eq 'solo' -and -not $PSBoundParameters.ContainsKey('Class')) { $Class = 'Speedy' }
$auraArgs = @(if ($AutoAura) { '-cw_autoaura' })   # solo: client1; shared modes: the Speedy joiner (client2) only
$allExtra = @($ExtraArgs -split '\s+' | Where-Object { $_ })
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$root = $script:RepoRoot
$exe  = Join-Path $root 'Builds\Windows\CluckWars.exe'
if (-not (Test-Path $exe)) { throw "no Windows build at $exe (build with the Test Session menu first)" }

Get-Process CluckWars -ErrorAction SilentlyContinue | Stop-Process -Force
# PC-only: never touch a phone (no logcat / screenrecord)
& "$PSScriptRoot\start.ps1" -NoRecord | Out-Null
$S = (Get-ChildItem (Join-Path $root 'Captures') -Directory | Sort-Object Name | Select-Object -Last 1).FullName
Write-Host "session: $S   mode: $Mode   rounds: $Rounds   window: ${Width}x${Height}$(if ($AutoZone) { '   autozone' })$(if ($AutoAura) { '   autoaura' })$(if ($allExtra) { '   extra: ' + ($allExtra -join ' ') })"

function Start-Exe([string]$name, [string[]]$extra) {
    $a = @('-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'-logFile',"`"$S\$name.log`"",'-cw_autoplay','-cw_hud','-cw_profile',$name,'-progressionDir',"`"$S\progression-$name`"") + $zoneArgs + $allExtra + $extra
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

$classes = @('Speedy','Fatty','Assassin')
switch ($Mode) {
    'solo' {
        Start-Exe 'client1' (@('-cw_autosolo','-cw_class',$Class,'-cw_autoloop',"$Rounds") + $auraArgs)
    }
    default {
        $joiners = switch ($Mode) { 'shared2' { 1 } default { 3 } }
        $humans = $joiners + 1
        Start-Exe 'client1' @('-cw_autohost','-cw_class',$Class,'-cw_autostart',"$humans",'-cw_autostart_timeout','120','-cw_autoloop',"$Rounds")
        $code = Wait-Code "$S\client1.log"
        if (-not $code) { throw 'no join code appeared' }
        Write-Host "join code: $code"
        for ($j = 1; $j -le $joiners; $j++) {
            $jAura = @(if ($j -eq 1) { $auraArgs })   # joiner 1 is the Speedy ($classes[0])
            Start-Exe "client$($j + 1)" (@('-cw_autojoin',$code,'-cw_class',$classes[($j - 1) % 3],'-cw_autoloop',"$Rounds") + $jAura + @($JoinerExtra -split '\s+' | Where-Object { $_ }))
        }
    }
}

$killed = $false
$deadline = (Get-Date).AddMinutes($MaxMinutes)
$main = "$S\client1.log"
while ((Get-Date) -lt $deadline) {
    Start-Sleep 5
    $started = (Select-String -Path $main -Pattern 'Match started' -ErrorAction SilentlyContinue | Measure-Object).Count
    $ended   = (Select-String -Path $main -Pattern 'Match ended'   -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($Mode -eq 'hostleave' -and -not $killed -and $started -ge 1) {
        Start-Sleep 15
        $h = Get-CimInstance Win32_Process -Filter "Name='CluckWars.exe'" | Where-Object { $_.CommandLine -match 'cw_autohost' }
        if ($h) { Stop-Process -Id $h.ProcessId -Force; $killed = $true; Write-Host 'host killed (master migration test)'; $main = "$S\client2.log" }
    }
    $done = (Select-String -Path $main -Pattern 'AutoTest loop done|loop round=\d+/\d+.*done' -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($done -gt 0 -or $ended -ge $Rounds) { Start-Sleep 8; break }
}
Get-Process CluckWars -ErrorAction SilentlyContinue | Stop-Process -Force
& "$PSScriptRoot\stop.ps1" -Session $S | Out-Null
& "$PSScriptRoot\verify.ps1" -Session $S -RequireUi:(-not $NoUiRequirement)
exit $LASTEXITCODE
