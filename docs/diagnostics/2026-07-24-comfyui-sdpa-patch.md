# Diagnostic — ComfyUI cannot generate any Flux image

**Date:** 2026-07-24
**Severity:** blocks all 2D art generation (`assetforge` ops `image`, `brainstorm`, `model`, `polish`; `modelgen`'s concept stage)
**Status:** root cause identified · **working workaround found and used in anger** (see
[§4a](#4a-workaround-verified-zero-modification)) · permanent fix **NOT applied** — needs
Maestro's go-ahead (see [Decision required](#decision-required))
**Install:** `D:\AI\ComfyUI\ComfyUI_windows_portable_stable` (launcher self-describes as "golden stable environment")

---

## 1. Symptom

Every Flux text-to-image job fails during **text encoding**, before sampling starts.
Nothing is written to disk. Reproduced on 3 consecutive runs with different prompts,
sizes and seeds.

```
node_type: CLIPTextEncode
AssertionError: Input tensors must be in dtype of torch.float16 or torch.bfloat16
  sageattention/core.py:725  in sageattn_qk_int8_pv_fp8_cuda
```

Abridged call stack, top of the interesting part downward:

```
comfy/sd1_clip.py:279        transformer(..., dtype=torch.float32)      ← T5 runs fp32
comfy/text_encoders/t5.py:164  optimized_attention(q, k, v, ...)
comfy/ldm/modules/attention.py:520  attention_pytorch(...)
comfy/ops.py:60              torch.nn.functional.scaled_dot_product_attention(...)
sageattention/core.py:149    sageattn(...)                             ← ⚠ should be torch's builtin
sageattention/core.py:725    assert dtype in [float16, bfloat16]       ← fails on fp32
```

The anomaly is line 4→5: `comfy/ops.py` calls **`torch.nn.functional.scaled_dot_product_attention`**
and lands in **`sageattn`**. Torch's own SDPA has been replaced process-wide.

---

## 2. Root cause

`ComfyUI-3D-Pack` monkeypatches torch's SDPA **as a side effect of a capability
probe**, at import time, unconditionally.

`my_config/custom_nodes/ComfyUI-3D-Pack/Gen_3D_Modules/Stable3DGen/trellis/backend_config.py:47`

```python
def _try_import_sageattention() -> bool:
    try:
        import torch.nn.functional as F
        from sageattention import sageattn
        F.scaled_dot_product_attention = sageattn     # ← line 51: THE BUG
        return True
    except ImportError:
        return False
```

The function is named, and used, as a *question* — "is sageattention available?" — but
it **mutates global interpreter state to answer it**, and never restores it.

It is reached unconditionally:

```python
# backend_config.py:71
def get_available_backends() -> Dict[str, bool]:
    return {
        'xformers':   _try_import_xformers(),
        'flash_attn': _try_import_flash_attn(),
        'sage':       _try_import_sageattention(),   # ← runs even when sage is not selected
        'naive': True,
        'sdpa':  True,
    }

# trellis/modules/attention/full_attn.py:18  — module scope, so it runs on import
available_backends = get_available_backends()
```

### Chain of events

1. ComfyUI starts and imports every custom node.
2. `ComfyUI-3D-Pack` imports the TRELLIS backend.
3. `full_attn.py` calls `get_available_backends()` at module scope.
4. The dict builder calls `_try_import_sageattention()` to fill in one boolean.
5. `torch.nn.functional.scaled_dot_product_attention` is now `sageattn` — **globally,
   permanently, for every model in the process.**
6. A Flux job runs. ComfyUI's T5 encoder computes in fp32 (`sd1_clip.py:279`, hardcoded).
7. sageattn v2's fp8 CUDA kernel asserts fp16/bf16 → `AssertionError`.

The two *other* patch sites are legitimate and are **not** the problem — they are
explicitly gated or explicitly invoked:

| File | Line | Verdict |
|---|---|---|
| `trellis/backend_config.py` | 51 | 🔴 **bug** — side effect inside an availability probe |
| `trellis/modules/attention/full_attn.py` | 27 | ✅ fine — inside `elif BACKEND == "sage"` |
| `trellis/modules/attention_utils.py` | 17 | ✅ fine — inside `enable_sage_attention()`, called deliberately |

---

## 3. Why the three obvious fixes did not work

| Attempt | Result | Why |
|---|---|---|
| Relaunch with `--use-pytorch-cross-attention` | ❌ still fails | ComfyUI *did* honour it — the startup log says `Using pytorch attention`, and `attention_pytorch` is in the stack. But `attention_pytorch` calls `torch.nn.functional.scaled_dot_product_attention`, which **is** sageattn. ComfyUI's attention flag selects which *wrapper* to call; it cannot undo a patch to torch itself. Flag verified applied via the live process command line. |
| Add `--fp16-text-enc` | ❌ still fails | That flag governs text-encoder *weight* dtype. The failing value is the *compute* dtype, hardcoded at `comfy/sd1_clip.py:279` as `dtype=torch.float32`. |
| Set `ATTN_BACKEND=sdpa` (not attempted — ruled out by reading) | would not help | `get_available_backends()` calls the probe to build the dict **regardless** of the selected backend. Choosing a different backend does not prevent the patch. |

---

## 4a. Workaround (VERIFIED, zero modification)

**Launch ComfyUI with `--disable-all-custom-nodes` for 2D work.**

3D-Pack is a custom node, so not loading it means the probe never runs and torch's
SDPA is never replaced. Confirmed end-to-end on 2026-07-24 — **SDXL and Flux both
generated successfully**, and the artwork now shipping as the Cluck Wars menu
backdrop (`Assets/_Game/Art/UI/Backgrounds/MenuBackground.png`) was produced this way.

```
python_embeded\python.exe -s ComfyUI\main.py --windows-standalone-build ^
  --user-directory ..\my_config\user ^
  --extra-model-paths-config ..\my_config\extra_model_paths.yaml ^
  --input-directory ..\my_config\input --output-directory output ^
  --disable-auto-launch --preview-method none ^
  --disable-all-custom-nodes --port 8188
```

Nothing on disk changes — it is purely how the process is started.

**Limits.** Only workflows built from **core** nodes run. That covers assetforge's
`image` / `brainstorm` and any plain txt2img (`CheckpointLoaderSimple`,
`CLIPTextEncode`, `EmptyLatentImage` / `EmptySD3LatentImage`, `KSampler`, `VAEDecode`,
`SaveImage`, `ControlNetLoader`). It does **not** cover `refine`'s IP-Adapter path
(`ComfyUI_IPAdapter_plus`) or `mesh` (TRELLIS lives in 3D-Pack — and TRELLIS *wants*
sage anyway). If a specific node is needed, `--whitelist-custom-nodes <folder>` loads
named nodes alongside the flag; just never whitelist `ComfyUI-3D-Pack` for a 2D job.

---

## 4. Decision required

The bug is one line, but it is in **Maestro's ComfyUI install**, not this repo, and the
launcher explicitly calls it a "golden stable environment". Nothing was changed:
`run_comfyui_headless.bat`, `config.json` and all custom-node code are **untouched**,
and ComfyUI was left **stopped**, exactly as found.

### Option A — remove the side effect (recommended, 1 line)

In `backend_config.py`, delete line 51 from the probe:

```diff
 def _try_import_sageattention() -> bool:
     try:
         import torch.nn.functional as F
         from sageattention import sageattn
-        F.scaled_dot_product_attention = sageattn
         return True
     except ImportError:
         return False
```

A probe should report, not mutate. TRELLIS still gets sage when it actually asks for
it, via the two legitimate sites in the table above. **Caveat:** this edits a
third-party custom node, so a `ComfyUI-3D-Pack` update will silently revert it — note
it somewhere durable, or keep it as a patch file.

### Option B — separate profiles

Run 2D (Flux/SDXL) and 3D (TRELLIS) from installs with different `custom_nodes`
directories. No third-party edits, but doubles the install and the maintenance.

### Option C — uninstall `sageattention`

Kills the patch at the source, but also disables `--use-sage-attention` (a real
performance feature) and 3D-Pack's sage path. Heaviest cost of the three.

---

## 5. Verification

After applying a fix, this should produce four PNGs rather than an assertion:

```bash
D:\AI\ComfyUI\ComfyUI_windows_portable_stable\python_embeded\python.exe C:\Users\MARCO\.claude\skills\assetforge\comfy_client.py brainstorm "test farmyard background, no characters, no text" --name smoketest --count 4 --size 1344x768
```

A one-liner that proves the patch is gone without generating anything:

```bash
D:\AI\ComfyUI\ComfyUI_windows_portable_stable\python_embeded\python.exe -s -c "import torch,torch.nn.functional as F; import sys,os; sys.path.insert(0,r'D:\AI\ComfyUI\ComfyUI_windows_portable_stable\ComfyUI'); print('before:',F.scaled_dot_product_attention); import trellis.backend_config as b; b.get_available_backends(); print('after :',F.scaled_dot_product_attention)"
```

`after:` must still read `<built-in function scaled_dot_product_attention>`.

---

## 6. Scope notes

- **SDXL is broken too — confirmed by test, not assumption.** An earlier draft of this
  doc guessed SDXL "may be fine because its CLIP encoders might run fp16". That was
  wrong. A plain SDXL txt2img (Juggernaut X Hyper) fails identically:
  `sdxl_clip.py:59` → CLIP-G → **the same** hardcoded `dtype=torch.float32` at
  `sd1_clip.py:279`. **Every** ComfyUI text encoder funnels through that line, so the
  breakage is model-agnostic: Flux, SDXL, and anything else built on `sd1_clip`.
- **3D/mesh ops are probably unaffected** — TRELLIS *wants* sage, and the mesh path
  feeds it fp16. Not retested here.
- **`--use-sage-attention` in the launcher is currently redundant for 2D work** — sage
  is forced globally whether or not the flag is passed.
- Environment: ComfyUI portable · Python 3.11.6 · torch 2.7.1+cu128 ·
  sageattention 2.2.0+cu128torch2.7.1.post3 · ~50 custom nodes under
  `D:\AI\ComfyUI\my_config\custom_nodes` (note: **not** the in-tree
  `ComfyUI\custom_nodes`, which holds only stock files — an easy place to look and
  wrongly conclude there are no custom nodes).
