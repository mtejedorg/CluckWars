# generate-uss-tokens.ps1 — CW_THEME (JSX) -> CluckWarsTokens.uss bridge
# =======================================================================
# Parses the design token JSX files (single source of truth) with regex and
# emits USS custom properties on :root. Also PRINTS (does not apply) the
# ColorSchemeSO field/value list per docs/ART.md §6.9 — the SO update itself
# happens in a later stage.
#
# Sources:
#   Design/cluckwars-tokens-v2.jsx  — CW_THEME, CW_PLAYER_COLORS
#   Design/cluckwars-tokens-v3.jsx  — CW_ABILITIES_V3, CW_ABILITY_CATS, CW_CLASSES_V3
# Output:
#   Assets/UI/Styles/CluckWarsTokens.uss  (GENERATED — do not hand-edit)
#
# Usage:  pwsh tools/generate-uss-tokens.ps1

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$V2   = Get-Content (Join-Path $RepoRoot 'Design\cluckwars-tokens-v2.jsx') -Raw
$V3   = Get-Content (Join-Path $RepoRoot 'Design\cluckwars-tokens-v3.jsx') -Raw
$Out  = Join-Path $RepoRoot 'Assets\UI\Styles\CluckWarsTokens.uss'

function To-Kebab([string]$name) {
    ($name -creplace '(?<=[a-z0-9])(?=[A-Z])', '-').Replace('_', '-').ToLowerInvariant()
}

# Friendlier names for terse CW_THEME keys
$KeyRename = @{
    t1 = 'textPrimary'; t2 = 'textSecondary'; t3 = 'accent'
    hp = 'hpFill'; cargo = 'cargoFill'
    hpFlat = 'hpFillFlat'; cargoFlat = 'cargoFillFlat'
}

$lines = [System.Collections.Generic.List[string]]::new()

function Add-Token([string]$key, [string]$rawValue) {
    # Extract every color literal (hex or rgb/rgba) from the raw JS value
    $colors = @([regex]::Matches($rawValue, '#[0-9a-fA-F]{6}\b|rgba?\([^)]*\)') | ForEach-Object { $_.Value })
    $kebab = To-Kebab $key
    if ($colors.Count -eq 1) {
        $lines.Add("    --cw-${kebab}: $($colors[0]);")
    } elseif ($colors.Count -gt 1) {
        for ($i = 0; $i -lt $colors.Count; $i++) {
            $lines.Add("    --cw-${kebab}-$($i + 1): $($colors[$i]);")
        }
    }
}

# ── CW_THEME (v2) ────────────────────────────────────────────────────────────
$themeBlock = [regex]::Match($V2, 'const CW_THEME = \{(.*?)\n\};', 'Singleline').Groups[1].Value
if (-not $themeBlock) { throw 'CW_THEME block not found in cluckwars-tokens-v2.jsx' }

$lines.Add('    /* -- CW_THEME (tokens-v2) -- */')
foreach ($m in [regex]::Matches($themeBlock, "(?m)^\s*(\w+):\s*(.+?),?\s*$")) {
    $key = $m.Groups[1].Value
    $val = $m.Groups[2].Value
    if ($val -match '=>') { continue }                       # skip function tokens (tGlow)
    if ($KeyRename.ContainsKey($key)) { $key = $KeyRename[$key] }
    if ($val -match '^\d+$') {                                # numeric radii (panelR etc.)
        $kebab = (To-Kebab $key) -replace '-r$', '-radius'
        $lines.Add("    --cw-${kebab}: ${val}px;")
        continue
    }
    Add-Token $key $val
}

# ── CW_PLAYER_COLORS (v2) ────────────────────────────────────────────────────
$lines.Add('')
$lines.Add('    /* -- Player colors (Okabe-Ito) -- */')
$playersRaw = [regex]::Match($V2, "const CW_PLAYER_COLORS = \[(.*?)\];").Groups[1].Value
$playerColors = [regex]::Matches($playersRaw, "#[0-9a-fA-F]{6}") | ForEach-Object { $_.Value }
if ($playerColors.Count -ne 4) { throw "expected 4 player colors, got $($playerColors.Count)" }
for ($i = 0; $i -lt $playerColors.Count; $i++) {
    $lines.Add("    --cw-player-$($i + 1): $($playerColors[$i]);")
}

# ── Class colors (v3) ────────────────────────────────────────────────────────
$lines.Add('')
$lines.Add('    /* -- Class colors (tokens-v3) -- */')
foreach ($m in [regex]::Matches($V3, "(?s)(\w+):\s*\{[^{}]*?color:\s*'(#[0-9a-fA-F]{6})',\s*dark:\s*'(#[0-9a-fA-F]{6})',\s*light:\s*'(#[0-9a-fA-F]{6})'")) {
    $cls = To-Kebab $m.Groups[1].Value
    $lines.Add("    --cw-class-${cls}: $($m.Groups[2].Value);")
    $lines.Add("    --cw-class-${cls}-dark: $($m.Groups[3].Value);")
    $lines.Add("    --cw-class-${cls}-light: $($m.Groups[4].Value);")
}

# ── Ability category colors (v3) ─────────────────────────────────────────────
$lines.Add('')
$lines.Add('    /* -- Ability category colors (tokens-v3) -- */')
$catsBlock = [regex]::Match($V3, 'const CW_ABILITY_CATS = \{(.*?)\n\};', 'Singleline').Groups[1].Value
foreach ($m in [regex]::Matches($catsBlock, "(\w+):\s*\{[^}]*?color:\s*'(#[0-9a-fA-F]{6})'")) {
    $lines.Add("    --cw-cat-$(To-Kebab $m.Groups[1].Value): $($m.Groups[2].Value);")
}

# ── Per-ability accent colors (v3) ───────────────────────────────────────────
$lines.Add('')
$lines.Add('    /* -- Ability accent colors (tokens-v3, CW_ABILITIES_V3) -- */')
$abBlock = [regex]::Match($V3, 'const CW_ABILITIES_V3 = \[(.*?)\n\];', 'Singleline').Groups[1].Value
$abMatches = [regex]::Matches($abBlock, "id:'(\w+)'[^\r\n]*?color:'(#[0-9a-fA-F]{6})'")
if ($abMatches.Count -ne 14) { throw "expected 14 abilities, got $($abMatches.Count)" }
foreach ($m in $abMatches) {
    $lines.Add("    --cw-ability-$(To-Kebab $m.Groups[1].Value): $($m.Groups[2].Value);")
}

# ── Emit ─────────────────────────────────────────────────────────────────────
$uss = @"
/* GENERATED by tools/generate-uss-tokens.ps1 — do not hand-edit.
 * Source of truth: Design/cluckwars-tokens-v2.jsx (CW_THEME, CW_PLAYER_COLORS)
 *                  Design/cluckwars-tokens-v3.jsx (classes, categories, abilities)
 * Gradient tokens are emitted as one variable per color stop (-1, -2, ...).
 */
:root {
$($lines -join "`n")
}
"@
[IO.File]::WriteAllText($Out, $uss.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $Out ($($lines.Count) lines)"

# ── ColorSchemeSO field list (ART §6.9) — printed only, applied in a later stage ──
Write-Host @'

ColorSchemeSO values per docs/ART.md §6.9 (DO NOT APPLY YET — later stage):
  PanelBackground : rgba(0.29, 0.19, 0.09, 0.92)        (warm dark wood)
  ButtonNormal    : top #f5c842 / bottom #b88a14         (gold gradient)
  ButtonHover     : top #ffe066 / bottom #c89818         (brighter gold)
  ButtonActive    : #f5c842                              (flat fallback)
  StartButton     : top #5ac54f / bottom #228b22         (green gradient)
  TextOnButton    : #fef5e0
  TextOnActive    : #1a0e04
  JoystickBase    : rgba(1,1,1,0.06) center -> rgba(1,1,1,0.02) edge
  JoystickKnob    : rgba(1,1,1,0.25) highlight -> rgba(1,1,1,0.08)
  AttackNormal    : legacy — retained but unused (no attack button in v0.3)
  AbilityNormal   : per-ability AccentColor 3-stop gradient; neutral blue when empty
  CooldownDim     : rgba(0,0,0,0.6)
'@
