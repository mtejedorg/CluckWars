# export-design-assets.ps1 — Cluck Wars design->Unity sprite pipeline (Stage 0)
# ==============================================================================
# Renders design atoms from Design/export.html (headless Chrome) into PNG
# sprites under Assets/_Game/Art/UI/, and authors Unity .meta importer files
# (TextureType=Sprite, no mipmaps, 9-slice borders, RGBA32 where gradients
# would band under compression).
#
# Usage:  pwsh tools/export-design-assets.ps1 [-Only <name-substring>]
#
# Requirements:
#   • Google Chrome at the default install path.
#   • The Design/ static server on http://127.0.0.1:8791 (auto-started if down).
#
# Re-run safety: existing .meta files are never rewritten (GUID stability —
# see docs/CONVENTIONS.md). PNGs are always re-exported. If you change a
# manifest Border value, delete the corresponding .meta to regenerate it.
#
# Manifest fields:
#   Name    asset file name (no extension)
#   Comp    window-global component name in export.html
#   Props   hashtable of props (JSON-encoded into the URL), or $null
#   W, H    logical component size in px (CSS pixels before scaling)
#   Scale   CSS zoom; PNG size = W*Scale x H*Scale (default 2 for crisp sprites)
#   Out     subfolder under Assets/_Game/Art/UI
#   Border  9-slice border in FINAL PNG pixels: @(left, bottom, right, top), or $null
#   Raw     $true -> RGBA32 / uncompressed (smooth gradients); $false -> default compression
#   Notes   why the asset exists / tint strategy

param(
    [string]$Only = ''
)

$ErrorActionPreference = 'Stop'

$RepoRoot  = Split-Path -Parent $PSScriptRoot
$DesignDir = Join-Path $RepoRoot 'Design'
$OutRoot   = Join-Path $RepoRoot 'Assets\_Game\Art\UI'
$Chrome    = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
$BaseUrl   = 'http://127.0.0.1:8791/export.html'
# Fresh profile per run: a stale/locked profile dir makes Chrome exit(21)
# and silently write nothing (hit 2026-07-11 after a killed run).
$UserData  = Join-Path $env:TEMP "cluckwars-export-chrome-$([guid]::NewGuid().ToString('N').Substring(0,8))"

# ─── Manifest ────────────────────────────────────────────────────────────────
$Manifest = @(
    # ── Atoms (tintable where noted; 9-slice borders protect corner bevels) ──
    @{ Name='PanelFrame';      Comp='XPanel';         Props=$null; W=300; H=300; Scale=2; Out='Atoms'; Border=@(96,96,96,96); Raw=$true;  Notes='Double-layer glossy panel, final colors. 9-slice.' }
    @{ Name='CardBg';          Comp='XCard';          Props=$null; W=200; H=200; Scale=2; Out='Atoms'; Border=@(56,56,56,56); Raw=$true;  Notes='Card gradient + border (ART 6.1), final colors. 9-slice.' }
    @{ Name='CardGlowFrame';   Comp='XCardGlow';      Props=$null; W=200; H=200; Scale=2; Out='Atoms'; Border=@(72,72,72,72); Raw=$true;  Notes='Gold selection ring, transparent center. 9-slice.' }
    @{ Name='Ribbon';          Comp='XRibbon';        Props=$null; W=320; H=64;  Scale=2; Out='Atoms'; Border=@(92,32,92,32); Raw=$true;  Notes='NEUTRAL gray ribbon w/ tails — tint in-engine (gold titles, winner colors). 9-slice.' }
    @{ Name='ButtonGrayscale'; Comp='XButton';        Props=$null; W=220; H=64;  Scale=2; Out='Atoms'; Border=@(52,52,52,52); Raw=$true;  Notes='3-stop gradient button, neutral — tint to gold/green/danger. 9-slice.' }
    @{ Name='HexGlossy';       Comp='XHex';           Props=$null; W=150; H=174; Scale=2; Out='Atoms'; Border=$null;          Raw=$true;  Notes='Neutral glossy hex (ability buttons tint via AccentColor). Not sliceable.' }
    @{ Name='BarTrough';       Comp='XBarTrough';     Props=$null; W=120; H=24;  Scale=2; Out='Atoms'; Border=@(20,20,20,20); Raw=$true;  Notes='Dark inset trough, final color (never tinted). 9-slice.' }
    @{ Name='BarFill';         Comp='XBarFill';       Props=$null; W=120; H=20;  Scale=2; Out='Atoms'; Border=@(12,12,12,12); Raw=$true;  Notes='Grayscale bevel fill — tint to HP red / cargo gold. 9-slice.' }
    @{ Name='JoystickBase';    Comp='XJoystickBase';  Props=$null; W=160; H=160; Scale=2; Out='Atoms'; Border=$null;          Raw=$true;  Notes='White/alpha circle per ART 6.7 — usable untinted or tinted.' }
    @{ Name='JoystickKnob';    Comp='XJoystickKnob';  Props=$null; W=64;  H=64;  Scale=2; Out='Atoms'; Border=$null;          Raw=$true;  Notes='Glossy white/alpha knob, highlight upper-left.' }
    @{ Name='PlayerDotGlossy'; Comp='XPlayerDot';     Props=$null; W=64;  H=64;  Scale=2; Out='Atoms'; Border=$null;          Raw=$true;  Notes='Grayscale glossy sphere — tinted per player color.' }
    @{ Name='GlossOverlay';    Comp='XGlossOverlay';  Props=$null; W=200; H=80;  Scale=2; Out='Atoms'; Border=@(28,28,28,28); Raw=$true;  Notes='White sheen strip, layered over any surface. 9-slice.' }
    @{ Name='RadialGlow';      Comp='XRadialGlow';    Props=$null; W=256; H=256; Scale=2; Out='Atoms'; Border=$null;          Raw=$true;  Notes='White radial glow (countdown / platform) — tintable.' }
    @{ Name='CodeTile';        Comp='XCodeTile';      Props=$null; W=64;  H=72;  Scale=2; Out='Atoms'; Border=@(40,40,40,40); Raw=$true;  Notes='Lobby invite-code letter tile, green badge treatment. 9-slice.' }

    # ── Chickens (class silhouettes, color, transparent) ──
    @{ Name='Chicken_fatty';    Comp='CWChicken'; Props=@{ classKey='fatty';    size=200 }; W=200; H=220; Scale=2; Out='Chickens'; Border=$null; Raw=$true; Notes='Class art, radial-gradient shading.' }
    @{ Name='Chicken_speedy';   Comp='CWChicken'; Props=@{ classKey='speedy';   size=200 }; W=200; H=220; Scale=2; Out='Chickens'; Border=$null; Raw=$true; Notes='Class art.' }
    @{ Name='Chicken_warrior';  Comp='CWChicken'; Props=@{ classKey='warrior';  size=200 }; W=200; H=220; Scale=2; Out='Chickens'; Border=$null; Raw=$true; Notes='Class art.' }
    @{ Name='Chicken_assassin'; Comp='CWChicken'; Props=@{ classKey='assassin'; size=200 }; W=200; H=220; Scale=2; Out='Chickens'; Border=$null; Raw=$true; Notes='Class art.' }

    # ── Ability icons (emoji glyphs from CW_ABILITIES_V3, color PNGs) ──
    @{ Name='Icon_FlyingPeck';   Comp='XEmoji'; Props=@{ glyph='🪽' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='fly_peck' }
    @{ Name='Icon_CluckShock';   Comp='XEmoji'; Props=@{ glyph='⚡' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='cluck' }
    @{ Name='Icon_Peck';         Comp='XEmoji'; Props=@{ glyph='🐦' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='peck' }
    @{ Name='Icon_RollAndPush';  Comp='XEmoji'; Props=@{ glyph='🌀' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='roll' }
    @{ Name='Icon_FeatherTrap';  Comp='XEmoji'; Props=@{ glyph='🪤' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='trap' }
    @{ Name='Icon_FeatherAura';  Comp='XEmoji'; Props=@{ glyph='💨' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='aura (same glyph as burst by design)' }
    @{ Name='Icon_RootEgg';      Comp='XEmoji'; Props=@{ glyph='🌱' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='root' }
    @{ Name='Icon_EggShell';     Comp='XEmoji'; Props=@{ glyph='🥚' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='shell' }
    @{ Name='Icon_TurtleMode';   Comp='XEmoji'; Props=@{ glyph='🐢' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='turtle' }
    @{ Name='Icon_SpineCoat';    Comp='XEmoji'; Props=@{ glyph='🦔' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='spine' }
    @{ Name='Icon_SpeedBurst';   Comp='XEmoji'; Props=@{ glyph='💨' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='burst' }
    @{ Name='Icon_Invisibility'; Comp='XEmoji'; Props=@{ glyph='👻' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='invis' }
    @{ Name='Icon_Doppelganger'; Comp='XEmoji'; Props=@{ glyph='👥' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='doppel' }
    @{ Name='Icon_SneakySteal';  Comp='XEmoji'; Props=@{ glyph='🤏' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='steal' }

    # ── Misc icons ──
    @{ Name='Crown';    Comp='XEmoji';     Props=@{ glyph='👑'; glow='rgba(245,200,66,0.8)' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='Winner crown w/ gold glow (ART 6.5).' }
    @{ Name='Medal_1';  Comp='XEmoji';     Props=@{ glyph='🥇' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='Leaderboard 1st.' }
    @{ Name='Medal_2';  Comp='XEmoji';     Props=@{ glyph='🥈' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='Leaderboard 2nd.' }
    @{ Name='Medal_3';  Comp='XEmoji';     Props=@{ glyph='🥉' }; W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='Leaderboard 3rd.' }
    @{ Name='FoodIcon'; Comp='CWFoodIcon'; Props=@{ size=128 };   W=128; H=128; Scale=2; Out='Icons'; Border=$null; Raw=$false; Notes='Design food icon (CWFoodIcon SVG).' }

    # ── Backgrounds (scale 1 — full-screen surfaces) ──
    @{ Name='ScreenBgRadial';  Comp='XScreenBg';       Props=$null; W=960; H=540; Scale=1; Out='Backgrounds'; Border=$null; Raw=$true; Notes='#2a1a0c->#0e0804 radial (ART 6.1). Stretched full-screen.' }
    @{ Name='HudTopGradient';  Comp='XHudTopGradient'; Props=$null; W=512; H=96;  Scale=1; Out='Backgrounds'; Border=$null; Raw=$true; Notes='HUD top fade strip.' }
)

# ─── Helpers ─────────────────────────────────────────────────────────────────

function Write-Lf([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

function Ensure-Server {
    try {
        $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8791/' -Method Head -TimeoutSec 3 -SkipHttpErrorCheck
        if ($r.StatusCode -eq 200) { Write-Host '[server] 8791 up'; return }
    } catch {}
    Write-Host '[server] starting python http.server on 8791...'
    Start-Process -FilePath 'python' -ArgumentList '-m','http.server','8791','--bind','127.0.0.1' -WorkingDirectory $DesignDir -WindowStyle Hidden
    Start-Sleep -Seconds 2
    $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8791/' -Method Head -TimeoutSec 5
    if ($r.StatusCode -ne 200) { throw 'Design server failed to start on 127.0.0.1:8791' }
}

function New-FolderMeta([string]$FolderPath) {
    $meta = "$FolderPath.meta"
    if (Test-Path $meta) { return }
    $guid = [guid]::NewGuid().ToString('N')
    # Trailing spaces after the null fields + a trailing newline are REQUIRED —
    # Unity's YAML parser rejects a folder meta without them (hit 2026-07-12).
    Write-Lf $meta @"
fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:{0}
  assetBundleName:{0}
  assetBundleVariant:{0}

"@.Replace('{0}', ' ')
}

# NOTE: sprite import settings (TextureType, 9-slice border, compression) are
# NOT authored here. Hand-authoring TextureImporter .meta YAML corrupted three
# different ways on 2026-07-12 (truncated writes, invalid-GUID parse failures).
# Unity generates the .meta itself on import, and the committed AssetPostprocessor
# Assets/_Game/Scripts/Editor/UiSpriteImportSettings.cs stamps the sprite settings
# from a per-atom border table. The exporter only produces PNGs + folder metas.

# ─── Main ────────────────────────────────────────────────────────────────────

if (-not $Chrome) { throw 'Chrome not found in any standard install location' }
Ensure-Server

# Folder scaffolding (+ folder metas so Unity keeps our GUIDs)
New-FolderMeta $OutRoot
foreach ($sub in 'Atoms','Icons','Chickens','Backgrounds') {
    $dir = Join-Path $OutRoot $sub
    New-Item -ItemType Directory -Force $dir | Out-Null
    New-FolderMeta $dir
}

$results = @()
foreach ($item in $Manifest) {
    if ($Only -and ($item.Name -notlike "*$Only*")) { continue }

    $pngW = [int]($item.W * $item.Scale)
    $pngH = [int]($item.H * $item.Scale)
    $outDir = Join-Path $OutRoot $item.Out
    $outPng = Join-Path $outDir "$($item.Name).png"

    $url = "$BaseUrl`?comp=$($item.Comp)&w=$($item.W)&h=$($item.H)&scale=$($item.Scale)"
    if ($item.Props) {
        $json = $item.Props | ConvertTo-Json -Compress
        $url += "&props=$([uri]::EscapeDataString($json))"
    }

    Write-Host "[export] $($item.Name)  ($($item.Comp)  ${pngW}x${pngH})"
    # Delete the previous PNG first so a failed capture can't pass as a stale success.
    if (Test-Path $outPng) { Remove-Item $outPng -Force }
    $chromeArgs = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars',
        '--virtual-time-budget=20000',
        '--default-background-color=00000000',
        "--user-data-dir=$UserData",
        "--window-size=$pngW,$pngH",
        "--screenshot=$outPng",
        $url
    )
    # Watchdog: a hung Chrome once blocked -Wait for ~7 h (2026-07-12). Cap each
    # capture at 60 s, kill the whole tree on timeout, and retry once with a
    # fresh profile dir (a killed run can leave the shared profile locked).
    $attempts = 0
    while ($attempts -lt 2 -and -not (Test-Path $outPng)) {
        $attempts++
        $proc = Start-Process -FilePath $Chrome -ArgumentList $chromeArgs -WindowStyle Hidden -PassThru
        if (-not $proc.WaitForExit(60000)) {
            Write-Warning "TIMEOUT (attempt $attempts): killing Chrome for $($item.Name)"
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" |
                Where-Object { $_.CommandLine -match [regex]::Escape($UserData) } |
                ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            $UserData = Join-Path $env:TEMP "cluckwars-export-chrome-$([guid]::NewGuid().ToString('N').Substring(0,8))"
            $chromeArgs = $chromeArgs -replace '^--user-data-dir=.*$', "--user-data-dir=$UserData"
        }
    }

    if (-not (Test-Path $outPng)) {
        Write-Warning "FAILED: no PNG produced for $($item.Name)"
        continue
    }
    # Import settings (border/raw) applied on import by UiSpriteImportSettings.cs.
    # The manifest still carries them as the source-of-truth border table (kept in
    # sync with the postprocessor's Borders dict) and for this summary.
    $results += [pscustomobject]@{
        Asset  = "$($item.Out)/$($item.Name).png"
        Size   = "${pngW}x${pngH}"
        Bytes  = (Get-Item $outPng).Length
        Slice  = if ($item.Border) { ($item.Border -join ',') } else { '-' }
        Format = if ($item.Raw) { 'RGBA32' } else { 'default' }
    }
}

Remove-Item -Recurse -Force $UserData -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize
Write-Host "`nExported $($results.Count) assets to $OutRoot"
