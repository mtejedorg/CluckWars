<#
.SYNOPSIS
  Unattended 4-seat scenario: Windows EXE host + Pixel (auto-join) + 2 more EXE clients, N looped rounds,
  periodic screenshots, then stop + merge. Needs a dev-tools build of both Windows and Android installed.

.PARAMETER Rounds   Rounds to play (cw_autoloop). Default 3.
.PARAMETER ShotEvery Seconds between screenshot pairs. Default 25.
.PARAMETER Phone    Seat for the phone: 'join' (default) or 'host' (phone hosts, EXEs join).
#>
param(
    [int]$Rounds = 3,
    [int]$ShotEvery = 25,
    [ValidateSet('join','host')][string]$Phone = 'join',
    [string]$Package = 'com.cluckwars.game',
    [int]$MaxMinutes = 12
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$root = $script:RepoRoot
$exe  = Join-Path $root 'Builds\Windows\CluckWars.exe'
$adb  = Get-AdbPath
$ser  = Get-PhoneSerial -Adb $adb -Override $null

Get-Process CluckWars -ErrorAction SilentlyContinue | Stop-Process -Force
& "$PSScriptRoot\start.ps1" | Out-Null
$S = (Get-ChildItem (Join-Path $root 'Captures') -Directory | Sort-Object Name | Select-Object -Last 1).FullName
Write-Host "session: $S"

function Start-Exe([string]$name, [string[]]$extra) {
    $a = @('-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',"`"$S\$name.log`"",'-cw_autoplay','-cw_hud','-cw_profile',$name) + $extra
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
function Phone-Launch([string[]]$extras) {
    & $adb -s $ser shell am force-stop $Package | Out-Null
    $e = @(); foreach ($x in $extras) { $k,$v = $x -split '=',2; $e += @('-e',$k,$v) }
    & $adb -s $ser shell am start -S -n "$Package/com.unity3d.player.UnityPlayerGameActivity" @e | Select-Object -Last 1
}

if ($Phone -eq 'join') {
    Start-Exe 'client1' @('-cw_autohost','-cw_class','Assassin','-cw_autostart','4','-cw_autostart_timeout','120',"-cw_autoloop","$Rounds")
    $code = Wait-Code "$S\client1.log"
} else {
    Phone-Launch @('cw_autohost=1','cw_class=Assassin','cw_autostart=4','cw_autostart_timeout=120',"cw_autoloop=$Rounds",'cw_autoplay=1','cw_hud=1','cw_devmode=1','cw_profile=phone')
    $code = Wait-Code "$S\phone.log"
}
if (-not $code) { throw 'no join code appeared' }
Write-Host "join code: $code"
if ($Phone -eq 'join') {
    Phone-Launch @("cw_autojoin=$code",'cw_class=Fatty','cw_autoplay=1','cw_hud=1','cw_devmode=1','cw_profile=phone',"cw_autoloop=$Rounds")
    Start-Exe 'client2' @('-cw_autojoin',$code,'-cw_class','Speedy',"-cw_autoloop","$Rounds")
    Start-Exe 'client3' @('-cw_autojoin',$code,'-cw_class','Warrior',"-cw_autoloop","$Rounds")
} else {
    Start-Exe 'client1' @('-cw_autojoin',$code,'-cw_class','Speedy',"-cw_autoloop","$Rounds")
    Start-Exe 'client2' @('-cw_autojoin',$code,'-cw_class','Fatty',"-cw_autoloop","$Rounds")
    Start-Exe 'client3' @('-cw_autojoin',$code,'-cw_class','Warrior',"-cw_autoloop","$Rounds")
}

$deadline = (Get-Date).AddMinutes($MaxMinutes); $n = 0
$mainLog = if ($Phone -eq 'join') { "$S\client1.log" } else { "$S\phone.log" }
while ((Get-Date) -lt $deadline) {
    Start-Sleep $ShotEvery
    $n++
    & "$PSScriptRoot\shot.ps1" -All -Label "auto$n" -Session $S | Out-Null
    $done = (Select-String -Path $mainLog -Pattern 'AutoTest loop done|loop round=\d+/\d+.*(done|finished)' -ErrorAction SilentlyContinue | Measure-Object).Count
    $ended = (Select-String -Path $mainLog -Pattern 'Match ended' -ErrorAction SilentlyContinue | Measure-Object).Count
    Write-Host ("t+{0}s shots={1} matches_ended={2}" -f ($n * $ShotEvery), $n, $ended)
    if ($done -gt 0 -or $ended -ge $Rounds + 1) { Start-Sleep 10; break }
}
Get-Process CluckWars -ErrorAction SilentlyContinue | Stop-Process -Force
& "$PSScriptRoot\stop.ps1" -Session $S -StopApp | Select-Object -Last 3
Write-Host "findings: $S\findings.txt"
