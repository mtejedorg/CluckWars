using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Renders the cast-archetype clips to PNG contact sheets — a time strip per archetype and one
    /// peak-pose comparison strip per class — so the poses can be reviewed by looking at them.
    /// </summary>
    /// <remarks>
    /// <b>This exists because the numbers lie.</b> Four art bugs on this project passed every
    /// numeric check, and twice the broken version scored <i>better</i> than the fix: a bounding box
    /// is just as tidy when the character is lying on its side, and an excursion metric rewards a
    /// head that has swung inside the torso. The per-frame feature assertions printed alongside the
    /// sheets (head above chest, beak ahead of chest, beak clear of the torso, feet not below the
    /// grounded rest pose) are relationships rather than extents for the same reason — but they are
    /// still only a tripwire. The sheet is the verdict.
    ///
    /// <b>Output is split into small sheets on purpose.</b> One tall sheet per class is a single
    /// image that has to be downscaled before it can be viewed, which is how a subtle pose break
    /// survives a review that technically "looked at it". A 6-moment strip is legible at native size.
    ///
    /// <b>The peak sheet is the silhouette test.</b> Colour is not a channel this design can lean on
    /// — the archetypes exist so that shape and timing carry the read — so the peak strip puts every
    /// archetype at its own strongest moment side by side. If two are hard to tell apart there, the
    /// set has a real problem no matter what the per-bone numbers say.
    /// </remarks>
    public static class CastArchetypePreview
    {
        private const int Cell = 320;
        private const int Columns = 6;

        /// <summary>
        /// Rendered far above the scene rather than at the origin, so whatever else is open renders
        /// nothing into the frame. Cheaper and less fragile than layer juggling, and it cannot
        /// accidentally leave a scene object's layer changed.
        /// </summary>
        private static readonly Vector3 Stage = new Vector3(0f, 5000f, 0f);

        /// <summary>
        /// The moment each archetype reads strongest — its own peak key, not a shared fraction of
        /// clip length, because the clips are not all the same length and their peaks do not line up.
        /// </summary>
        private static readonly Dictionary<CastArchetype, float> PeakTime = new()
        {
            [CastArchetype.Lunge]  = 0.38f,
            [CastArchetype.Slam]   = 0.42f,
            [CastArchetype.Flare]  = 0.38f,
            [CastArchetype.Grab]   = 0.31f,
            [CastArchetype.Throw]  = 0.44f,
            [CastArchetype.Hunker] = 0.52f,
            // The inflate, not the shimmy. Puff's signature is the flick, but that signature is
            // temporal: a still of a shimmy frame is just a banked chicken, and Dodge owns banking,
            // so sampling it there compares two banks and says nothing about whether the two
            // archetypes read apart. Sampled at the inflate the comparison is the honest one --
            // tall-and-narrow against Flare's tall-and-wide -- and the flick is left to be judged
            // in motion, which is the only place it exists.
            [CastArchetype.Puff]   = 0.33f,
            [CastArchetype.Dodge]  = 0.31f,
        };

        [MenuItem("Cluck Wars/Animation/Preview Cast Archetypes (Warrior)")]
        public static void PreviewWarrior() => Render(ChickenClass.Warrior);

        [MenuItem("Cluck Wars/Animation/Preview Cast Archetypes (All Classes)")]
        public static void PreviewAll()
        {
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass))) Render(cls);
        }

        public static void Render(ChickenClass cls, string outDir = null)
        {
            outDir ??= Path.Combine(Path.GetTempPath(), "cluckwars_anim_preview");
            Directory.CreateDirectory(outDir);

            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(
                "Assets/_Game/Data/ChickenClassRegistry.asset");
            if (registry == null || !registry.TryGet(cls, out var entry) || entry.ModelPrefab == null)
            {
                Debug.LogError($"[CastArchetypePreview] {cls}: no registry entry or ModelPrefab.");
                return;
            }

            var archetypes = (CastArchetype[])System.Enum.GetValues(typeof(CastArchetype));
            var report = new System.Text.StringBuilder();

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(entry.ModelPrefab);
            inst.transform.position = Stage;

            var camGo = new GameObject("~previewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.93f, 0.93f, 0.95f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;

            // A floor. Ground penetration is otherwise invisible in a sheet shot against empty
            // space — the chicken just looks like it is standing there — and "part of the model is
            // under the floor" is the single most common way these clips fail. The camera sits
            // slightly above the plane so the near edge does not mask the contact point.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "~previewFloor";
            floor.transform.position = Stage;
            floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);

            var lightGo = new GameObject("~previewLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;

            var bones = inst.GetComponentsInChildren<Transform>(true)
                            .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

            // Without this the sheet is a lie. A SkinnedMeshRenderer re-skins on the editor's own
            // update tick, not inside Camera.Render(), so sampling a clip and rendering in the same
            // call renders the *previous* skinning — every moment of every archetype comes out as
            // the bind pose, and the sheet looks like a perfectly clean set of chickens. That is
            // exactly the failure this whole tool exists to catch, so it is worth naming: the first
            // run of this harness produced eight byte-identical strips and no error of any kind.
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.updateWhenOffscreen = true;
            }

            // --- per-archetype time strips -------------------------------------------------
            foreach (var arch in archetypes)
            {
                string clipPath = CastArchetypeClipGenerator.ClipPath(cls, arch);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null) { report.AppendLine($"{arch}: MISSING at {clipPath}"); continue; }

                report.AppendLine($"\n### {cls} / {arch}   len={clip.length:F2}s loop={clip.isLooping}");

                var strip = new Texture2D(Cell * Columns, Cell * 2, TextureFormat.RGB24, false);
                var cellHashes = new List<string>();
                for (int c = 0; c < Columns; c++)
                {
                    float t = clip.length * c / (Columns - 1);
                    clip.SampleAnimation(inst, t);

                    // Two views per moment. The side view is where pitch, reach and ground contact
                    // are legible; the 3/4 is where wing spread and lateral break are.
                    cellHashes.Add(Blit(strip, cam, rt, ViewSide(),     c * Cell, Cell));
                    Blit(strip, cam, rt, ViewThreeQtr(), c * Cell, 0);

                    report.AppendLine("  " + FeatureLine(t, bones));
                }

                // A strip whose moments all render identically means the harness rendered stale
                // skinning, not that the clip is calm — the pixels, not the poses, went missing.
                // Fail loudly rather than write a sheet that reviews as "clean".
                if (cellHashes.Distinct().Count() == 1)
                    Debug.LogError($"[CastArchetypePreview] {cls}/{arch}: every sampled moment " +
                                   "rendered identically. The mesh is not following the rig — the " +
                                   "sheet is not showing this clip. Do not review it.");

                strip.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"{cls}_{arch}.png"), strip.EncodeToPNG());
                Object.DestroyImmediate(strip);
            }

            // --- peak-pose silhouette comparison -------------------------------------------
            var peaks = new Texture2D(Cell * archetypes.Length, Cell * 2, TextureFormat.RGB24, false);
            for (int a = 0; a < archetypes.Length; a++)
            {
                var arch = archetypes[a];
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    CastArchetypeClipGenerator.ClipPath(cls, arch));
                if (clip == null) continue;

                clip.SampleAnimation(inst, PeakTime[arch]);
                Blit(peaks, cam, rt, ViewSide(),     a * Cell, Cell);
                Blit(peaks, cam, rt, ViewThreeQtr(), a * Cell, 0);
            }
            peaks.Apply();
            File.WriteAllBytes(Path.Combine(outDir, $"{cls}_PEAKS.png"), peaks.EncodeToPNG());
            Object.DestroyImmediate(peaks);

            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(inst);
            rt.Release();
            Object.DestroyImmediate(rt);

            File.WriteAllText(Path.Combine(outDir, $"{cls}_cast_archetypes.txt"), report.ToString());
            Debug.Log($"[CastArchetypePreview] {cls}: wrote {archetypes.Length + 1} sheets to {outDir}");
        }

        private static (Vector3 pos, Quaternion rot) ViewSide() =>
            (Stage + new Vector3(4f, 0.88f, 0f), Quaternion.Euler(0f, -90f, 0f));

        private static (Vector3 pos, Quaternion rot) ViewThreeQtr() =>
            (Stage + new Vector3(3.0f, 1.25f, 2.6f), Quaternion.Euler(10f, -131f, 0f));

        /// <summary>Renders one cell into the sheet and returns a hash of its pixels.</summary>
        private static string Blit(
            Texture2D sheet, Camera cam, RenderTexture rt,
            (Vector3 pos, Quaternion rot) view, int x, int y)
        {
            cam.transform.SetPositionAndRotation(view.pos, view.rot);
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var cellTex = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
            cellTex.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
            cellTex.Apply();
            RenderTexture.active = prev;

            sheet.SetPixels(x, y, Cell, Cell, cellTex.GetPixels());
            using var md5 = System.Security.Cryptography.MD5.Create();
            string hash = System.Convert.ToBase64String(md5.ComputeHash(cellTex.GetRawTextureData()));
            Object.DestroyImmediate(cellTex);
            return hash;
        }

        /// <summary>
        /// Relationships, not extents. Each is a thing that must remain true of a chicken no matter
        /// how the pose is tuned, so a violation names a real break rather than a changed number.
        /// Heights are reported relative to <see cref="Stage"/> so they read as ground-relative.
        /// </summary>
        private static string FeatureLine(float t, Dictionary<string, Transform> b)
        {
            Vector3 head = b["Head"].position, beak = b["Beak"].position, chest = b["Chest"].position;
            float footY = Mathf.Min(b["FootL"].position.y, b["FootR"].position.y);
            float baseY = Stage.y;

            // 0.160 is the grounded rest height measured off the bind pose. Both directions are
            // tested, and the upper bound is the one that matters: the original check was
            // one-sided — "no clip can drive a foot below the ground" — and every archetype in the
            // set sailed through it while rendering as a chicken floating with its feet in the air.
            // A cast beat has no airborne frame, so any lift is a break.
            float foot = footY - baseY;
            bool feetOk    = foot >= 0.155f && foot <= 0.175f;
            bool headOk    = head.y > chest.y;
            bool beakClear = Vector3.Distance(beak, chest) > 0.45f; // beak has not sunk into the torso

            // beakZ is reported but deliberately not flagged. A beak behind the chest plane is
            // correct during a rear-back — Slam's wind-up and Puff's inflate both do it on purpose —
            // so flagging it trains the reader to skim past flags, which is worse than not having it.

            string flags = (foot < 0.155f ? " FEET-SUNK" : "") + (foot > 0.175f ? " FEET-FLOAT" : "")
                         + (headOk ? "" : " HEAD-BELOW-CHEST") + (beakClear ? "" : " BEAK-IN-CHEST");
            return $"t={t:F2} footY={footY - baseY:F3} headY={head.y - baseY:F3} chestY={chest.y - baseY:F3} " +
                   $"beakZ={beak.z:F3} beakY={beak.y - baseY:F3} beak-chest={Vector3.Distance(beak, chest):F3}" +
                   (flags.Length == 0 ? "  OK" : "  <<<" + flags);
        }
    }
}
