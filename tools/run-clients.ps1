<#
.SYNOPSIS
  Launch N windowed Cluck Wars Windows clients for local multiplayer testing.

.DESCRIPTION
  Emulators are not viable on this machine (Hyper-V + ES 3.1 conflict — see
  docs/TESTING.md "Known gotchas"), so multiplayer testing is done with multiple
  windowed Windows builds. Each instance gets its own log file under
  Builds/Windows/logs/ so per-client logs never interleave.

  Build first: Ctrl+Shift+W in the Editor (Cluck Wars / Build / Windows).

.EXAMPLE
  .\tools\run-clients.ps1            # 2 clients
  .\tools\run-clients.ps1 -Count 4   # full lobby
  .\tools\run-clients.ps1 -Tail 1    # tail client 1's log
#>
param(
    [int]$Count = 2,
    [string]$ExePath = "$PSScriptRoot\..\Builds\Windows\CluckWars.exe",
    [int]$Width = 960,
    [int]$Height = 540,
    [int]$Tail = 0
)

$logDir = Join-Path (Split-Path $ExePath) 'logs'

if ($Tail -gt 0) {
    Get-Content (Join-Path $logDir "client$Tail.log") -Tail 40 -Wait
    return
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Build not found at $ExePath — run 'Cluck Wars / Build / Windows' (Ctrl+Shift+W) in the Editor first."
    return
}

New-Item -ItemType Directory -Force $logDir | Out-Null

for ($i = 1; $i -le $Count; $i++) {
    $log = Join-Path $logDir "client$i.log"
    Start-Process $ExePath -ArgumentList @(
        '-screen-fullscreen', '0',
        '-screen-width', $Width,
        '-screen-height', $Height,
        '-logFile', "`"$log`""
    )
    Write-Host "Client $i launched → log: $log"
    Start-Sleep -Milliseconds 700   # stagger so Photon/UGS init doesn't race
}

Write-Host "`nFlow: client 1 = HOST (pick class, H, SPACE) → read join code → others JOIN with code."
Write-Host "Tail a log live:  .\tools\run-clients.ps1 -Tail 1"
