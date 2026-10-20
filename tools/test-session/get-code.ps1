<#
.SYNOPSIS
  Print the auto-host join code ("[AutoTest] AutoTest code=XXXXXX") from the session's logs.
  No-UI channel: works for a phone auto-host (phone.log), an EXE auto-host (client*.log) and the
  Editor (Editor.log from the offset recorded at session start).

.EXAMPLE
  .\tools\test-session\get-code.ps1                  # newest session, waits up to 120 s
  .\tools\test-session\get-code.ps1 -TimeoutSec 0    # single look, no wait
  $code = .\tools\test-session\get-code.ps1 -Quiet
#>
param(
    [string]$Session,
    [int]$TimeoutSec = 120,
    [switch]$Quiet
)
. "$PSScriptRoot\lib.ps1"
if (-not $Session) { $Session = Get-LatestSessionDir }
if (-not $Session -or -not (Test-Path $Session)) { Write-Error 'No session dir (run start.ps1 first or pass -Session).'; exit 2 }

if ($TimeoutSec -le 0) { $code = Get-AutoTestCode -Session $Session }
else { $code = Wait-AutoTestCode -Session $Session -TimeoutSec $TimeoutSec }

if (-not $code) { if (-not $Quiet) { Write-Warning "No 'AutoTest code=' line found in $Session" }; exit 1 }
if (-not $Quiet) { Write-Host "JOIN CODE: $code" }
$code
