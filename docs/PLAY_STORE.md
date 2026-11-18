# Google Play store listing

Limits and drafts for the Play Console listing (package `com.wugnar.games.wars.cluck`). Check the limits here before writing any store text or exporting any store graphic.

## Text limits

| Field | Limit | Notes |
|---|---|---|
| Short description | **80 characters** | One line, shown on the store card. Set by Maestro 2026-11-18. |
| Full description | **4000 characters** | Plain text, line breaks allowed. Set by Maestro 2026-11-18. |
| Release notes ("What's new") | 500 characters per language | Pasted in the release, wrapped in a language tag: `<en-US>...</en-US>`, `<es-ES>...</es-ES>`. |
| App name | 30 characters | Confirmed on the console (Cluck Wars is 10/30). |

Count characters with `len(text)` before pasting; line breaks count.

## Graphic limits (read from the Play Console screen, 2026-11-18)

| Asset | Size | Format |
|---|---|---|
| App icon | 512 x 512 px | PNG or JPEG, up to 1 MB (ours is an opaque square, Play rounds the corners) |
| Feature graphic | 1024 x 500 px | PNG or JPEG, up to 15 MB |
| Phone screenshots | 2 to 8 | PNG or JPEG, up to 8 MB each, aspect ratio exactly 16:9 or 9:16, each side 320 to 3840 px |
| 7-inch tablet screenshots | up to 8 | same as phone |
| 10-inch tablet screenshots | up to 8 | PNG or JPEG, up to 8 MB, 16:9 or 9:16, each side 1080 to 7680 px |
| Foldable screenshots | 3 to 8 (optional) | PNG or JPEG, up to 8 MB, each side 960 to 3840 px; one 1:1 and one 4:3 unfolded, one 10:16 folded |
| Video | optional | public or unlisted YouTube URL, ads off, no age restriction |

The console wants at least 2 screenshots across phone and tablet. For the game to be eligible for promotion on Play it wants at least 4 screenshots, 3 of them 16:9 or 9:16 and at least 1080 px.

## Files in the repo

| What | Where |
|---|---|
| App icon 512 x 512 | `art-src/play-store/app_icon_512x512.png` (the in-game icon `Assets/_Game/Art/AppIcon/Icon_Default.png`, halved) |
| Feature graphic | `art-src/play-store/feature_graphic_1024x500.png`, rebuilt by `python art-src/play-store/make_feature_graphic.py` (Bg_MainMenu, the four `Hero_*_cheer.png`, Lilita One) |
| Android bundle and symbols | `Builds/Android/CluckWars-<semver>-<build>.aab` and `...-IL2CPP.symbols.zip` (upload the symbols under App bundle explorer, Downloads, Native debug symbols) |

## Cutting and uploading a build (tips)

- **Build:** close the Unity Editor first (a batch build cannot run while the Editor holds `Temp/UnityLockfile`), then `Unity.exe -batchmode -quit -projectPath . -executeMethod CluckWars.EditorTools.CluckWarsBuildMenu.BuildPlayStore -logFile <log>` (Unity 6000.3.14f1 under the Hub). `BuildAll` builds Windows and the Android APK as well. Check the exit code and the log for compile errors; a wrapper task can report "completed" while Unity is still running, so look at `tasklist` and the log.
- **Unity rewrites settings during a build:** `ProjectSettings/ProjectSettings.asset` and `UnityConnectSettings.asset` (analytics switched on) and `Packages/manifest.json` (a package bump) come back modified. Revert them with `git checkout --` on explicit paths before committing; never stage `UnityConnectSettings.asset`.
- **What to upload:** `Builds/Android/CluckWars-<semver>-<build>.aab` in the release, and `CluckWars-<semver>-<build>-<semver>-v<build>-IL2CPP.symbols.zip` under App bundle explorer, Downloads, Assets, Native debug symbols (the Play warning about native symbols, BL-182). Not the `.apk` (direct install only) and not older bundles. The "no deobfuscation file" warning is left as is: minify is off, there is no mapping file.
- **Version code** = the build number in the tag (`v<semver>+<build>`). Play refuses a code it has already seen, so every upload needs a new build.
- **Release notes:** paste with the language tags, 500 characters per language.
- **Will the phone update by itself?** Play updates in the background, normally on wifi while charging, and a new release or a testing track can take hours. Force it: Play Store, profile picture, Manage apps and device, Updates available. For a testing track the account must be in the testers list and have opened the opt-in link. Check what is installed over USB: `adb shell dumpsys package com.wugnar.games.wars.cluck` (`versionCode`, `installerPackageName=com.android.vending`).
- **Console gotchas (2026-11-18):** "Changes could not be saved" and "some languages have errors" appeared while screenshots were missing: the phone, 7-inch and 10-inch tablet sections all carry the required asterisk. A 1920 x 1080 image is exactly 16:9 and fits all three, so one set of captures covers them. Check that the default language of the listing is en-US, since the text is English.
- **Screenshots:** `python tools/ui_capture/capture_orientation.py --sizes 1920x1080 --no-zones` plays the Windows build and writes `Captures/orient-<utc>/1920x1080/` (menu pages and in-match shots, judged by the UiProbe). Pick 4 to 8 without debug overlays (the FPS strip, the zone outlines).

## Drafts (en-US)

Short description (72 characters):

```
Fast 4-player chicken arena! Grab food, bank it, and outwit your rivals.
```

The full description draft is in the session that wrote it; regenerate it from `docs/GDD.md` and `MatchConfig.asset` (40 food, 45 seconds) when the listing is next edited. Never quote the numbers from the GDD prose, read the asset.

## Release notes drafts (alpha)

```
<en-US>
Alpha playtest build.
- Fixed kits: each chicken specialization has its own four moves.
- Eleven abilities, one per job, with the same name and icon on every chicken.
- Hunt: mark a rival, then pounce in the last second to take their food.
- Clearer feedback: badges show who is stunned, slowed, shelled or marked, and the hunter gets arrows, distance and a screen tint.
- Live balance: numbers can now be tuned without a new build.
Please send us your feedback!
</en-US>
```

```
<es-ES>
Versión alfa de pruebas.
- Kits fijos: cada gallina tiene sus cuatro movimientos.
- Once habilidades, una por función.
- Cazar: marca a un rival y salta sobre él para quitarle la comida.
- Más avisos en pantalla: aturdido, ralentizado, en huevo o marcado.
¡Cuéntanos qué te parece!
</es-ES>
```
