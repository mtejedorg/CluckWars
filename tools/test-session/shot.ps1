<#
.SYNOPSIS
  Timestamped screenshots that line up with merged.log: phone (adb screencap) and/or desktop.

.DESCRIPTION
  File name = <what>-<UTC yyyyMMddTHHmmssfffZ>[-label].png, e.g. phone-20261020T173501123Z.png.
  Output dir: -OutDir, else -Session, else the newest Captures\session-*\ (subfolder shots\), else Captures\shots\.
  Phone capture streams `adb exec-out screencap -p` straight into the file via Start-Process
  redirection (binary-safe, no PowerShell pipeline re-encoding) and verifies the PNG signature.
  Desktop capture grabs the whole virtual screen (all monitors), DPI-aware.

.EXAMPLE
  .\tools\test-session\shot.ps1 -All
  .\tools\test-session\shot.ps1 -Phone -Label pink-fence
#>
param(
    [switch]$Phone,
    [switch]$Desktop,
    [switch]$All,
    [string]$Session,
    [string]$OutDir,
    [string]$Serial,
    [string]$Label,
    [Alias('WhatIf')][switch]$DryRun
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
if ($All) { $Phone = $true; $Desktop = $true }
if (-not ($Phone -or $Desktop)) { throw 'Specify -Phone, -Desktop or -All.' }

if (-not $OutDir) {
    if (-not $Session) { $Session = Get-LatestSessionDir }
    if ($Session) { $OutDir = Join-Path $Session 'shots' } else { $OutDir = Join-Path $script:RepoRoot 'Captures\shots' }
}
if (-not $DryRun) { New-Item -ItemType Directory -Force $OutDir | Out-Null }

function New-ShotPath([string]$what) {
    $suffix = ''; if ($Label) { $suffix = '-' + ($Label -replace '[^\w\-]', '_') }
    return Join-Path $OutDir ('{0}-{1:yyyyMMddTHHmmssfff}Z{2}.png' -f $what, [DateTime]::UtcNow, $suffix)
}

# Phone first (the slow one); each file is stamped right before its own capture.
$results = @()

if ($Phone) {
    $adb = Get-AdbPath
    $dev = Get-PhoneSerial -Adb $adb -Override $Serial
    if (-not $dev) { Write-Warning 'No physical adb device - phone screenshot skipped.' }
    else {
        $path = New-ShotPath 'phone'
        if ($DryRun) { Write-Host "[dry-run] adb -s $dev exec-out screencap -p > $path" }
        else {
            $p = Start-Process -FilePath $adb -ArgumentList @('-s', $dev, 'exec-out', 'screencap', '-p') `
                -RedirectStandardOutput $path -RedirectStandardError ($path + '.err') -WindowStyle Hidden -PassThru -Wait
            Remove-Item ($path + '.err') -ErrorAction SilentlyContinue
            $ok = $false
            if (Test-Path $path) {
                $b = [IO.File]::ReadAllBytes($path)
                $ok = ($b.Length -gt 8 -and $b[0] -eq 0x89 -and $b[1] -eq 0x50 -and $b[2] -eq 0x4E -and $b[3] -eq 0x47)
            }
            if ($ok) { $results += $path; Write-Host "phone   -> $path" }
            else { Write-Warning "Phone screencap did not produce a valid PNG ($path) - screen locked or device busy?" }
        }
    }
}

if ($Desktop) {
    $path = New-ShotPath 'desktop'
    if ($DryRun) { Write-Host "[dry-run] System.Drawing CopyFromScreen (virtual screen) -> $path" }
    else {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        if (-not ('CwDpi' -as [type])) {
            Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class CwDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
        }
        [void][CwDpi]::SetProcessDPIAware()
        $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = New-Object System.Drawing.Bitmap($vs.Width, $vs.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CopyFromScreen($vs.Left, $vs.Top, 0, 0, $bmp.Size)
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $g.Dispose(); $bmp.Dispose() }
        $results += $path
        Write-Host "desktop -> $path"
    }
}
