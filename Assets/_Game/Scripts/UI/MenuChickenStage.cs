using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CluckWars.UI
{
    /// <summary>
    /// The menu's live 3D chicken stage (menu overhaul Phase 3, Decision 7): the real class models,
    /// idling and turning on a turntable, each rendered by its own small camera into a square
    /// RenderTexture that a UI Toolkit element shows as its background image.
    /// </summary>
    /// <remarks>
    /// <b>Owned by <see cref="MenuUiController"/></b>, which creates it only while Performance Mode
    /// is OFF and disposes it when Performance Mode turns ON, when the menu is disabled and when
    /// Bootstrap unloads. It is a plain object, not a scene component, so nothing has to be wired
    /// in Bootstrap.unity and nothing outlives the menu: <see cref="Dispose"/> destroys the rig
    /// (models, cameras, lights) and releases every RenderTexture.
    ///
    /// <b>Isolation.</b> The rig sits 5 km below the world origin with one model per slot 50 m
    /// apart, and every stage camera has a 30 m far plane, so no stage camera sees the menu scene
    /// or another slot and the menu camera never sees the stage. Lighting is the hero renders'
    /// four-light studio rig (key / fill / rim / front). Those lights are enabled only while a
    /// stage camera renders (URP begin/end camera callbacks), and every other light in the scene is
    /// switched off for exactly that window, so the chickens look like the static renders whatever
    /// the scene's own light does, and the scene never receives the studio lights.
    ///
    /// <b>Animation</b> is <see cref="AnimationClip.SampleAnimation"/> on the class's own clips
    /// (the same path the static hero renders used): the Idle loop, and on a hop the Flare cast
    /// clip as a cheer (there is no cheer clip). No Animator, no controller asset.
    /// </remarks>
    public sealed class MenuChickenStage : IDisposable
    {
        private const string Source = "MenuStage";

        private static readonly Vector3 Origin = new Vector3(0f, -5000f, 0f);
        private const float SlotSpacing = 50f;
        private const float FieldOfView = 22f;
        private const float CameraPitch = 10f;        // degrees above the target, as the hero renders
        private const float CheerDuration = 0.8f;     // Flare clip played over this, as the hop's cheer
        private const float CheerClipSpan = 0.75f;    // normalised end of the Flare clip used for it
        private const float HopPeak = 0.06f;          // hop height, fraction of the chicken's height (fits the frame's headroom)

        private readonly ChickenClassRegistrySO _registry;
        private readonly ILogService _log;
        private readonly Func<float> _pixelsPerPoint;
        private readonly Slot[] _slots;
        private readonly List<Light> _studioLights = new List<Light>(4);
        private readonly List<Light> _switchedOff = new List<Light>();
        private Light[] _sceneLights;
        private int _sceneLightsFrame = -1;
        private GameObject _root;
        private int _activeStageCameras;
        private bool _disposed;

        private sealed class Slot
        {
            public int Index;
            public VisualElement Target;
            public Transform Pivot;
            public Camera Camera;
            public RenderTexture Texture;
            public GameObject Model;
            public ChickenClass Class;
            public AnimationClip Idle, Cheer;
            public float Height, Radius;
            public Transform Head;         // the rig's "Head" bone (null: the crown anchor uses the model's top centre)
            public float HeadTopAbove;     // rest-pose height of the model's top above the Head bone
            public float RenderScale = 1f; // the element is drawn this much bigger (USS scale): texture resolution follows
            public float HopStart = -1f;
            public float ShownAt;          // turntable time origin: a chicken coming on screen starts at the 3/4 view
            public bool Rendering;
            public bool Sway;
            public Color Clear;
        }

        /// <param name="slotCount">How many independent chickens the menu can show at once.</param>
        /// <param name="pixelsPerPoint">Screen pixels per panel point, for sizing the textures.</param>
        public MenuChickenStage(ChickenClassRegistrySO registry, ILogService log, int slotCount, Func<float> pixelsPerPoint)
        {
            _registry = registry ? registry : throw new ArgumentNullException(nameof(registry), "ChickenClassRegistrySO not injected.");
            _log = log;
            _pixelsPerPoint = pixelsPerPoint ?? (() => 1f);
            _slots = new Slot[slotCount];

            _root = new GameObject("MenuChickenStage");
            _root.transform.position = Origin;
            AddStudioLight("Key",   new Color(1.00f, 0.93f, 0.80f), 1.05f, new Vector3(38f, -35f, 0f));
            AddStudioLight("Fill",  new Color(0.75f, 0.85f, 1.00f), 0.55f, new Vector3(15f, 140f, 0f));
            AddStudioLight("Rim",   new Color(1.00f, 0.90f, 0.70f), 0.85f, new Vector3(25f, 170f, 0f));
            AddStudioLight("Front", new Color(1.00f, 1.00f, 1.00f), 0.20f, new Vector3(5f, 0f, 0f));

            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void AddStudioLight(string name, Color color, float intensity, Vector3 euler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.enabled = false;    // on only while a stage camera renders
            _studioLights.Add(l);
        }

        /// <summary>Number of RenderTextures the stage currently holds (leak checks, tests).</summary>
        public int TextureCount
        {
            get { int n = 0; foreach (var s in _slots) if (s?.Texture != null) n++; return n; }
        }

        /// <summary>
        /// Shows <paramref name="cls"/> in <paramref name="slot"/>, drawn into <paramref name="target"/>.
        /// Re-showing the same class is a no-op unless <paramref name="hop"/> asks for a select hop.
        /// </summary>
        /// <param name="sway">Rock around the 3/4 view instead of a full turntable (lineup seats).</param>
        /// <param name="clear">Colour the texture is cleared to (alpha 0): the panel behind the
        /// element, so anti-aliased edges fade into it instead of into black.</param>
        /// <exception cref="InvalidOperationException">The class has no model or Idle clip.</exception>
        public void Show(int slot, VisualElement target, ChickenClass cls, bool hop, bool sway, Color clear)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MenuChickenStage));
            var s = _slots[slot] ??= CreateSlot(slot);
            s.Sway = sway;
            s.Clear = new Color(clear.r, clear.g, clear.b, 0f);
            s.Camera.backgroundColor = s.Clear;

            if (s.Target != target)
            {
                if (s.Target != null) s.Target.style.backgroundImage = StyleKeyword.Null;
                s.Target = target;
                if (s.Texture != null) target.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(s.Texture));
            }

            if (s.Model == null || s.Class != cls) SwapModel(s, cls);
            if (hop && !PlayerPreferences.ReducedMotionEnabled) s.HopStart = Time.unscaledTime;
        }

        /// <summary>Empties <paramref name="slot"/>: the element falls back to its USS image (hidden
        /// for an empty seat), the camera stops and the model is put away.</summary>
        public void Clear(int slot)
        {
            var s = _slots[slot];
            if (s == null) return;
            if (s.Target != null) s.Target.style.backgroundImage = StyleKeyword.Null;
            s.Target = null;
            if (s.Model != null) s.Model.SetActive(false);
            s.Camera.enabled = false;
        }

        private Slot CreateSlot(int index)
        {
            var s = new Slot { Index = index };
            var pivot = new GameObject("Slot" + index);
            pivot.transform.SetParent(_root.transform, false);
            pivot.transform.localPosition = new Vector3(index * SlotSpacing, 0f, 0f);
            s.Pivot = pivot.transform;

            var camGo = new GameObject("Camera" + index);
            camGo.transform.SetParent(_root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.fieldOfView = FieldOfView;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            cam.depth = -50f;
            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;   // keeps the clear alpha in the texture
            urp.antialiasing = AntialiasingMode.None;
            urp.renderShadows = false;
            urp.requiresColorTexture = false;
            urp.requiresDepthTexture = false;
            s.Camera = cam;
            return s;
        }

        private void SwapModel(Slot s, ChickenClass cls)
        {
            if (!_registry.TryGet(cls, out var e) || e.ModelPrefab == null || e.Clips.Idle == null)
                throw new InvalidOperationException($"ChickenClassRegistry has no model or Idle clip for {cls}.");

            if (s.Model != null) Object.Destroy(s.Model);
            var model = Object.Instantiate(e.ModelPrefab, s.Pivot, false);
            model.name = e.ModelPrefab.name;
            // The imported .fbx carries its own Animator; the clips are sampled directly instead.
            var nested = model.GetComponent<Animator>();
            if (nested != null) { nested.enabled = false; Object.Destroy(nested); }

            // Same class wash the match applies (ChickenVisuals.ApplyTint: white -> tint by strength).
            var tint = Color.Lerp(Color.white, e.TintColor, Mathf.Clamp01(e.TintStrength));
            tint.a = 1f;
            var mpb = new MaterialPropertyBlock();
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                r.SetPropertyBlock(mpb);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            s.Model = model;
            s.Class = cls;
            s.ShownAt = Time.unscaledTime;
            s.Idle = e.Clips.Idle;
            s.Cheer = e.Clips.CastFor(CastArchetype.Flare);
            if (s.Cheer == null)
                _log?.Warn(Source, $"{cls} has no Flare cast clip; its select hop has no cheer pose.");

            s.Pivot.localRotation = Quaternion.identity;
            s.Idle.SampleAnimation(model, 0f);
            Measure(s, model);
            s.Head = FindDeep(model.transform, "Head");
            if (s.Head != null) s.HeadTopAbove = s.Pivot.position.y + s.Height - s.Head.position.y;
            else _log?.Warn(Source, $"{cls} model has no 'Head' bone; the winner's crown is anchored to the model's top centre.");
            _log?.Debug(Source, $"Slot {s.Index}: {cls} height={s.Height:0.00} radius={s.Radius:0.00}.");
            Frame(s);
        }

        /// <summary>
        /// Height above the feet and turntable radius of the posed model, from its baked skinned
        /// vertices: renderer bounds of a skinned mesh are padded (~15-40% here) and framed the
        /// chicken visibly smaller than the static renders. Origin is at the feet (registry contract).
        /// </summary>
        private static void Measure(Slot s, GameObject model)
        {
            var origin = s.Pivot.position;
            float top = 0.1f, radius = 0.1f;
            var mesh = new Mesh();
            var verts = new List<Vector3>();
            try
            {
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.BakeMesh(mesh, true);
                    mesh.GetVertices(verts);
                    var m = smr.transform.localToWorldMatrix;
                    foreach (var v in verts)
                    {
                        var w = m.MultiplyPoint3x4(v) - origin;
                        top = Mathf.Max(top, w.y);
                        radius = Mathf.Max(radius, new Vector2(w.x, w.z).magnitude);
                    }
                }
            }
            finally
            {
                Object.Destroy(mesh);
            }
            s.Height = top;
            s.Radius = radius;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindDeep(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Where the top of the head is in <paramref name="slot"/>'s texture this frame, as (x from the
        /// left, y from the top), 0..1: the Head bone (it follows the idle, the turntable and the hop)
        /// raised by the rest-pose distance to the top of the model. False while the slot has no model
        /// or texture (not rendering yet).
        /// </summary>
        public bool TryGetHeadAnchor(int slot, out Vector2 anchor)
        {
            anchor = default;
            if (_disposed || slot < 0 || slot >= _slots.Length) return false;
            var s = _slots[slot];
            if (s == null || s.Model == null || s.Texture == null || !s.Rendering) return false;
            var top = s.Head != null
                ? s.Head.position + Vector3.up * s.HeadTopAbove
                : s.Model.transform.position + Vector3.up * s.Height;
            var vp = s.Camera.WorldToViewportPoint(top);
            anchor = new Vector2(vp.x, 1f - vp.y);
            return true;
        }

        /// <summary>
        /// The element of <paramref name="slot"/> is drawn <paramref name="scale"/> times its layout
        /// size (USS scale, the post-match winner): its texture is sized for that, so it stays sharp.
        /// </summary>
        public void SetRenderScale(int slot, float scale)
        {
            if (slot < 0 || slot >= _slots.Length) return;
            var s = _slots[slot] ??= CreateSlot(slot);
            s.RenderScale = Mathf.Max(1f, scale);
        }

        /// <summary>
        /// Frames the chicken like the static hero PNGs: a square whose side is the chicken's height
        /// plus the hop's headroom, x 1.06, feet 5% above the bottom edge. The turntable radius only
        /// wins for a chicken much wider than tall, so a tail may graze the edge side-on for a moment
        /// rather than every chicken shrinking for the sake of that one frame.
        /// </summary>
        private static void Frame(Slot s)
        {
            float side = Mathf.Max(s.Height * (1f + HopPeak), s.Radius * 1.45f) * 1.06f;
            var look = s.Pivot.position + Vector3.up * (side * 0.45f);
            float dist = side * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            s.Camera.transform.position = look + Quaternion.Euler(-CameraPitch, 0f, 0f) * Vector3.forward * dist;
            s.Camera.transform.LookAt(look);
        }

        /// <summary>
        /// Per-frame: sizes each slot's texture to its element, runs its camera only while the
        /// element is on screen, and animates (idle loop, turntable / sway, select hop).
        /// </summary>
        public void Tick()
        {
            if (_disposed) return;
            float now = Time.unscaledTime;
            bool reduced = PlayerPreferences.ReducedMotionEnabled;
            float ppp = _pixelsPerPoint();

            foreach (var s in _slots)
            {
                if (s == null) continue;
                bool visible = s.Target != null && IsOnScreen(s.Target);
                int side = visible ? MenuStagePolicy.TextureSide(s.Target.layout.width, s.Target.layout.height, ppp * s.RenderScale) : 0;
                bool render = MenuStagePolicy.ShouldRender(true, s.Model != null && s.Target != null, visible, side);
                if (render && MenuStagePolicy.NeedsRealloc(s.Texture != null ? s.Texture.width : 0, side))
                    Reallocate(s, side);

                if (render && !s.Rendering) s.ShownAt = now;
                s.Rendering = render;
                s.Camera.enabled = render;
                if (s.Model != null && s.Model.activeSelf != render) s.Model.SetActive(render);
                if (!render) continue;

                Animate(s, now, reduced);
            }
        }

        private void Animate(Slot s, float now, bool reduced)
        {
            float sinceHop = s.HopStart >= 0f ? now - s.HopStart : -1f;
            bool cheering = !reduced && sinceHop >= 0f && sinceHop < CheerDuration;
            if (!cheering) s.HopStart = -1f;

            if (cheering && s.Cheer != null)
                s.Cheer.SampleAnimation(s.Model, sinceHop / CheerDuration * CheerClipSpan * s.Cheer.length);
            else
                s.Idle.SampleAnimation(s.Model, Mathf.Repeat(now, s.Idle.length));

            float hop = cheering ? MenuStagePolicy.HopHeight(sinceHop) * HopPeak * s.Height : 0f;
            s.Model.transform.localPosition = new Vector3(0f, hop, 0f);
            // Phase per slot so the lineup does not rock in lockstep.
            s.Pivot.localRotation = Quaternion.Euler(0f, MenuStagePolicy.Yaw(now - s.ShownAt, s.Index * 1.7f, s.Sway, reduced), 0f);
        }

        private void Reallocate(Slot s, int side)
        {
            ReleaseTexture(s);
            s.Texture = new RenderTexture(side, side, 16, RenderTextureFormat.ARGB32)
            {
                name = "MenuStageRT" + s.Index,
                antiAliasing = 4,
            };
            if (!s.Texture.Create())
                throw new InvalidOperationException($"Could not create the {side}px stage texture for slot {s.Index}.");
            s.Camera.targetTexture = s.Texture;
            s.Camera.aspect = 1f;
            s.Target.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(s.Texture));
        }

        private static void ReleaseTexture(Slot s)
        {
            if (s.Texture == null) return;
            if (s.Camera != null) s.Camera.targetTexture = null;
            s.Texture.Release();
            Object.Destroy(s.Texture);
            s.Texture = null;
        }

        /// <summary>True while the element and all its ancestors are displayed in a live panel.</summary>
        private static bool IsOnScreen(VisualElement ve)
        {
            if (ve.panel == null) return false;
            for (var e = ve; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        private bool IsStageCamera(Camera cam)
        {
            foreach (var s in _slots) if (s != null && s.Camera == cam) return true;
            return false;
        }

        // Studio lighting window: only the stage's lights for a stage camera, then put back.
        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_disposed || !IsStageCamera(cam)) return;
            if (_activeStageCameras++ > 0) return;
            _switchedOff.Clear();
            // Up to five stage cameras render per frame; scan the scene's lights once per frame.
            if (_sceneLightsFrame != Time.frameCount || _sceneLights == null)
            {
                _sceneLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                _sceneLightsFrame = Time.frameCount;
            }
            foreach (var l in _sceneLights)
            {
                if (l == null) continue;
                if (!l.enabled || _studioLights.Contains(l)) continue;
                l.enabled = false;
                _switchedOff.Add(l);
            }
            foreach (var l in _studioLights) l.enabled = true;
        }

        private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!IsStageCamera(cam) || _activeStageCameras == 0) return;
            if (--_activeStageCameras > 0) return;
            RestoreSceneLights();
        }

        private void RestoreSceneLights()
        {
            foreach (var l in _studioLights) if (l != null) l.enabled = false;
            foreach (var l in _switchedOff) if (l != null) l.enabled = true;
            _switchedOff.Clear();
            _activeStageCameras = 0;
        }

        /// <summary>Destroys the rig, releases every texture and gives each element back its USS image.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RestoreSceneLights();
            foreach (var s in _slots)
            {
                if (s == null) continue;
                if (s.Target != null) s.Target.style.backgroundImage = StyleKeyword.Null;
                ReleaseTexture(s);
            }
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }
    }
}
