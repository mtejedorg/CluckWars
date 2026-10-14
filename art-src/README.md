# art-src

Source art kept OUT of the Unity import path (Unity only imports under `Assets/`), so it costs no import time or
build size. Nothing in the project references these files (guid + name search, Phase 8 A11, 2026-10-14).

- `Generated/*_comfyui.glb` - raw ComfyUI/TRELLIS mesh outputs the shipped `.fbx` props in `Assets/Generated/` were
  cleaned up from.
- `Generated/{assassin,fatty,speedy,warrior}_chicken.glb` - pre-rig chicken meshes (the game uses the `*_rigged.fbx`).

The `.meta` files are kept next to them so a file moved back into `Assets/` keeps its original guid.
