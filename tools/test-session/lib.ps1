# Shared helpers for tools/test-session/*.ps1  (dot-source: . "$PSScriptRoot\lib.ps1")
# Compatible with Windows PowerShell 5.1 and PowerShell 7 (no ?? / ternary).

$script:RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:DefaultAdb = 'C:\Users\MARCO\Documents\platform-tools\adb.exe'
$script:AppPackage = 'com.UnityTechnologies.com.unity.template.urpblank'
$script:AppActivity = 'com.unity3d.player.UnityPlayerGameActivity'

function Get-AdbPath {
    if (Test-Path $script:DefaultAdb) { return $script:DefaultAdb }
    $c = Get-Command adb -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    throw "adb.exe not found (expected $($script:DefaultAdb))"
}

# Real phone = first 'device'-state entry whose serial does not start with emulator-
# (a ghost 'emulator-5562 offline' from a port collision is always listed). -Override wins.
function Get-PhoneSerial {
    param([string]$Adb, [string]$Override)
    if ($Override) { return $Override }
    foreach ($l in (& $Adb devices)) {
        if ($l -match '^(\S+)\s+device\s*$' -and $Matches[1] -notlike 'emulator-*') { return $Matches[1] }
    }
    return $null
}

function Write-Utf8 {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# Phone extras: hashtable / IDictionary, or 'k=v k2=v2' string (or string[]), -> ordered dictionary.
function Convert-Extras {
    param($Extras)
    $d = New-Object System.Collections.Specialized.OrderedDictionary
    if ($null -eq $Extras) { return $d }
    if ($Extras -is [System.Collections.IDictionary]) {
        foreach ($k in $Extras.Keys) { $d[[string]$k] = [string]$Extras[$k] }
        return $d
    }
    $tokens = @(([string[]]$Extras) -join ' ' -split '\s+') | Where-Object { $_ }
    foreach ($t in $tokens) {
        $i = $t.IndexOf('=')
        if ($i -lt 1) { throw "Bad phone extra '$t' (expected key=value)" }
        $d[$t.Substring(0, $i)] = $t.Substring($i + 1)
    }
    return $d
}

# Read a file that another process may be appending to; optional byte offset (ignored if past EOF,
# which means the file was truncated/rotated, e.g. the Editor restarted).
function Read-SharedLines {
    param([string]$Path, [long]$Offset = 0)
    if (-not (Test-Path $Path)) { return @() }
    $fs = New-Object IO.FileStream($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        if ($Offset -gt 0 -and $Offset -le $fs.Length) { [void]$fs.Seek($Offset, [IO.SeekOrigin]::Begin) }
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        $lines = New-Object System.Collections.Generic.List[string]
        while ($null -ne ($l = $sr.ReadLine())) { $lines.Add($l) }
        return ,$lines.ToArray()
    } finally { $fs.Dispose() }
}

function Read-SessionJson {
    param([string]$Session)
    $p = Join-Path $Session 'session.json'
    if (-not (Test-Path $p)) { return $null }
    return (Get-Content $p -Raw | ConvertFrom-Json)
}

# The no-UI channel for the join code: the auto-host logs "[AutoTest] AutoTest code=XXXXXX".
# Scans phone.log, client*.log, editor.log (sliced) and the live Editor.log from the recorded offset.
function Get-AutoTestCode {
    param([string]$Session)
    $rx = [regex]'AutoTest code=([A-Za-z0-9]+)'
    $sources = New-Object System.Collections.Generic.List[object]
    foreach ($f in (Get-ChildItem $Session -Filter '*.log' -ErrorAction SilentlyContinue)) {
        if ($f.Name -in 'phone.log', 'editor.log' -or $f.Name -like 'client*.log') { $sources.Add(@{ Path = $f.FullName; Offset = 0 }) }
    }
    $sj = Read-SessionJson $Session
    if ($sj -and $sj.editorLog -and $sj.editorLog.path -and -not (Test-Path (Join-Path $Session 'editor.log'))) {
        $sources.Add(@{ Path = $sj.editorLog.path; Offset = [long]$sj.editorLog.startOffset })
    }
    $found = $null
    foreach ($s in $sources) {
        foreach ($l in (Read-SharedLines $s.Path $s.Offset)) {
            $m = $rx.Match($l)
            if ($m.Success) { $found = $m.Groups[1].Value }   # last one wins (autoloop re-hosts)
        }
    }
    return $found
}

function Wait-AutoTestCode {
    param([string]$Session, [int]$TimeoutSec = 120, [int]$PollMs = 750)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $c = Get-AutoTestCode -Session $Session
        if ($c) { return $c }
        Start-Sleep -Milliseconds $PollMs
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-LatestSessionDir {
    $cap = Join-Path $script:RepoRoot 'Captures'
    if (-not (Test-Path $cap)) { return $null }
    $d = Get-ChildItem $cap -Directory -Filter 'session-*' | Sort-Object Name -Descending | Select-Object -First 1
    if ($d) { return $d.FullName }
    return $null
}
