<#
.SYNOPSIS
  Background worker started by start.ps1: rolling `adb shell screenrecord` chunks.

.DESCRIPTION
  Android's screenrecord caps a single file at 180 s, so this loops: record a chunk to
  /sdcard/cw_rec_<k>.mp4, pull it to <SessionDir>\screenrec\rec_<k>_<chunkStartUtc>.mp4, delete it
  from the device, repeat. Exits when <SessionDir>\stop.flag exists (stop.ps1 creates it and sends
  `pkill -INT screenrecord` so the in-flight chunk is finalized, then this loop pulls it).
  The chunk's start time is in the file name so video time can be lined up with merged.log.
#>
param(
    [Parameter(Mandatory)][string]$Adb,
    [Parameter(Mandatory)][string]$Serial,
    [Parameter(Mandatory)][string]$SessionDir,
    [int]$ChunkSeconds = 180
)

$ErrorActionPreference = 'Continue'
if ($ChunkSeconds -gt 180) { $ChunkSeconds = 180 }   # screenrecord hard limit
$outDir = Join-Path $SessionDir 'screenrec'
New-Item -ItemType Directory -Force $outDir | Out-Null
$stopFlag = Join-Path $SessionDir 'stop.flag'
$recLog = Join-Path $outDir 'rec.log'

function Log([string]$m) { Add-Content -Path $recLog -Value ("{0:HH:mm:ss.fff}Z {1}" -f [DateTime]::UtcNow, $m) }

$k = 0
$fails = 0
while (-not (Test-Path $stopFlag)) {
    $k++
    $remote = "/sdcard/cw_rec_$k.mp4"
    $startUtc = [DateTime]::UtcNow
    Log "chunk $k start -> $remote"
    & $Adb -s $Serial shell screenrecord --time-limit $ChunkSeconds $remote 2>&1 | ForEach-Object { Log "screenrecord: $_" }
    Start-Sleep -Milliseconds 1500   # let the mp4 finalize after SIGINT / time limit
    $local = Join-Path $outDir ('rec_{0:000}_{1:yyyyMMddTHHmmssfff}Z.mp4' -f $k, $startUtc)
    & $Adb -s $Serial pull $remote $local 2>&1 | ForEach-Object { Log "pull: $_" }
    if (Test-Path $local) {
        & $Adb -s $Serial shell rm -f $remote | Out-Null
        $fails = 0
        Log ("chunk $k saved ({0:N1} MB)" -f ((Get-Item $local).Length / 1MB))
    } else {
        $fails++
        Log "chunk $k FAILED to pull ($fails consecutive)"
        if ($fails -ge 20) { Log 'giving up after 20 consecutive failures'; break }
        Start-Sleep -Seconds 2
    }
}
Log 'loop exit'
