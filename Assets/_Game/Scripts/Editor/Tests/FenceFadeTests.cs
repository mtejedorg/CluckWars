using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 7f (round-3 finding 9): the near fence could hide the local bird down to its comb at
    /// "3" / GO! and in play. Pins the see-through-fence rules (<see cref="FenceFadeRules"/>), that every fence
    /// renderer in the shipped <c>Map.unity</c> is on the fade shader (and nothing else is), and the GET READY
    /// YOU-marker rule (<see cref="YouMarkerRules"/>).
    /// </summary>
    public class FenceFadeTests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";

        /// <summary>Hand-placed fence / wall props: the four boundary runs and the pinwheel arm walls.</summary>
        private static readonly Regex FencePropName = new Regex(@"^(Wall_[NSEW]|InnerWall_[0-9]+)_[0-9]+$");

        /// <summary>The coop at each base corner. The spawn point sits against it, so at "3" / GO! the coop
        /// (not the boundary fence) is what hid the bird down to its comb: it fades like the fence.</summary>
        private static readonly Regex CoopPropName = new Regex(@"^Base[0-9]+_Coop$");

        // ── Fragment fade (C# twin of CW_FenceFadeAmount) ───────────────────

        [Test]
        public void FragmentFade_InFrontOfTheBirdAtItsCentre_IsTheFullPeakFade()
        {
            float fade = FenceFadeRules.FragmentFade(1f, 0f, fragmentEyeDepth: 10f, birdEyeDepth: 12f);
            Assert.That(fade, Is.EqualTo(FenceFadeRules.MaxFade).Within(1e-5f));
            Assert.That(FenceFadeRules.MaxFade, Is.LessThan(1f),
                "A faint screen-door of fence must remain: it still blocks movement and should still read as a wall.");
        }

        [Test]
        public void FragmentFade_BehindTheBird_StaysSolid()
        {
            // The far fence (and anything at or behind the bird's depth) must never open.
            Assert.That(FenceFadeRules.FragmentFade(1f, 0f, fragmentEyeDepth: 14f, birdEyeDepth: 12f), Is.EqualTo(0f));
            Assert.That(FenceFadeRules.FragmentFade(1f, 0f, fragmentEyeDepth: 12f, birdEyeDepth: 12f), Is.EqualTo(0f));
        }

        [Test]
        public void FragmentFade_OutsideTheOuterRadius_StaysSolid_AndRampsBetweenTheRadii()
        {
            Assert.That(FenceFadeRules.FragmentFade(1f, FenceFadeRules.OuterRadius + 0.01f, 10f, 12f), Is.EqualTo(0f));

            float inner = FenceFadeRules.FragmentFade(1f, FenceFadeRules.InnerRadius, 10f, 12f);
            float mid = FenceFadeRules.FragmentFade(1f, 0.5f * (FenceFadeRules.InnerRadius + FenceFadeRules.OuterRadius), 10f, 12f);
            Assert.That(inner, Is.EqualTo(FenceFadeRules.MaxFade).Within(1e-5f));
            Assert.That(mid, Is.GreaterThan(0f).And.LessThan(inner), "The hole's edge must be soft, not a hard cut.");
            Assert.That(FenceFadeRules.InnerRadius, Is.LessThan(FenceFadeRules.OuterRadius));
        }

        [Test]
        public void FragmentFade_ScalesWithStrength_AndIsZeroWhenNothingIsCovered()
        {
            Assert.That(FenceFadeRules.FragmentFade(0f, 0f, 10f, 12f), Is.EqualTo(0f));
            Assert.That(FenceFadeRules.FragmentFade(0.5f, 0f, 10f, 12f),
                Is.EqualTo(0.5f * FenceFadeRules.MaxFade).Within(1e-5f));
        }

        [Test]
        public void FragmentFade_HuggingTheFence_RampsInOverTheDepthSoftness()
        {
            float half = FenceFadeRules.FragmentFade(1f, 0f, 12f - 0.5f * FenceFadeRules.DepthSoftness, 12f);
            Assert.That(half, Is.EqualTo(0.5f * FenceFadeRules.MaxFade).Within(1e-4f));
        }

        [Test]
        public void ShaderParams_MatchTheRules()
        {
            var p = FenceFadeRules.ShaderParams;
            Assert.That(p, Is.EqualTo(new Vector4(FenceFadeRules.InnerRadius, FenceFadeRules.OuterRadius,
                FenceFadeRules.MaxFade, FenceFadeRules.DepthSoftness)));
        }

        // ── Time easing ─────────────────────────────────────────────────────

        [Test]
        public void StepStrength_FadesInAndOutOverFadeSeconds()
        {
            float half = FenceFadeRules.StepStrength(0f, true, FenceFadeRules.FadeSeconds * 0.5f);
            Assert.That(half, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(FenceFadeRules.StepStrength(half, true, FenceFadeRules.FadeSeconds), Is.EqualTo(1f));
            Assert.That(FenceFadeRules.StepStrength(1f, false, FenceFadeRules.FadeSeconds), Is.EqualTo(0f));
            Assert.That(FenceFadeRules.FadeSeconds, Is.InRange(0.1f, 0.2f), "Spec: ~0.15 s.");
        }

        [Test]
        public void StepStrength_SimulatedAt30Fps_ReachesFullWithinFiveFrames()
        {
            float s = 0f;
            for (int i = 0; i < 5; i++) s = FenceFadeRules.StepStrength(s, true, 1f / 30f);
            Assert.That(s, Is.EqualTo(1f));
        }

        // ── CPU occlusion test ──────────────────────────────────────────────

        // Match-camera-like view: 30° pitch, looking toward +Z, so "toward the camera" is up and back (-Z).
        private static readonly Vector3 TowardCamera = -(Quaternion.Euler(30f, 0f, 0f) * Vector3.forward);

        // A fence panel 1.5 m tall and 0.5 m deep whose centre plane is at z = -1 (between bird and camera).
        private static readonly Bounds NearFence = new Bounds(new Vector3(0f, 0.75f, -1f), new Vector3(4f, 1.5f, 0.5f));

        [Test]
        public void IsOccluded_BirdJustBehindTheNearFence_IsCovered()
        {
            Assert.IsTrue(FenceFadeRules.IsOccluded(Vector3.zero, 1f, TowardCamera, 30f, new List<Bounds> { NearFence }));
        }

        [Test]
        public void IsOccluded_BirdInFrontOfTheFence_IsNotCovered()
        {
            // Bird between the camera and the fence (fence behind it).
            var farFence = new Bounds(new Vector3(0f, 0.75f, 1f), new Vector3(4f, 1.5f, 0.5f));
            Assert.IsFalse(FenceFadeRules.IsOccluded(Vector3.zero, 1f, TowardCamera, 30f, new List<Bounds> { farFence }));
        }

        [Test]
        public void IsOccluded_BirdWellBackFromTheFence_RayClearsTheTop()
        {
            // 6 m behind the fence the 30° view ray passes above a 1.5 m fence even from the feet.
            Assert.IsFalse(FenceFadeRules.IsOccluded(new Vector3(0f, 0f, 5f), 1f, TowardCamera, 30f, new List<Bounds> { NearFence }));
        }

        [Test]
        public void IsOccluded_OnlyTheCombShowing_StillCounts()
        {
            // The reported case: the fence covers feet and body, the comb pokes over. Any covered sample counts.
            var lowFence = new Bounds(new Vector3(0f, 0.35f, -0.6f), new Vector3(4f, 0.7f, 0.3f));
            Assert.IsTrue(FenceFadeRules.IsOccluded(Vector3.zero, 1f, TowardCamera, 30f, new List<Bounds> { lowFence }));
        }

        [Test]
        public void IsOccluded_NoFence_IsNotCovered()
        {
            Assert.IsFalse(FenceFadeRules.IsOccluded(Vector3.zero, 1f, TowardCamera, 30f, new List<Bounds>()));
            Assert.IsFalse(FenceFadeRules.IsOccluded(Vector3.zero, 1f, TowardCamera, 30f, null));
        }

        // ── Shader + shipped map guard ──────────────────────────────────────

        [Test]
        public void FenceFadeShader_ExistsAndCompiles()
        {
            var shader = Shader.Find(FenceFadeRules.ShaderName);
            Assert.IsNotNull(shader, $"Shader '{FenceFadeRules.ShaderName}' not found.");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "The fence fade shader has compile errors.");
            Assert.IsTrue(shader.isSupported, "The fence fade shader is not supported on the Editor's graphics API.");
        }

        [Test]
        public void EveryFenceAndCoopRendererInTheShippedMap_UsesTheFenceFadeShader_AndNothingElseDoes()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            try
            {
                var renderers = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                    .ToList();

                bool IsFence(Renderer r) =>
                    FencePropName.IsMatch(r.name) || (r.transform.parent != null && FencePropName.IsMatch(r.transform.parent.name));
                bool UsesFade(Renderer r) =>
                    r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name == FenceFadeRules.ShaderName);

                var fence = renderers.Where(IsFence).ToList();
                Assert.That(fence.Count, Is.GreaterThanOrEqualTo(4 * 10),
                    "Fewer fence renderers than four sides of the authored 10-post runs — the name pattern is stale.");

                var coops = renderers.Where(r => r.transform.parent != null && CoopPropName.IsMatch(r.transform.parent.name)).ToList();
                Assert.That(coops.Count, Is.EqualTo(4), "One coop per base corner — the coop name pattern is stale.");
                fence.AddRange(coops);

                var notFaded = fence.Where(r => !r.sharedMaterials.All(m =>
                        m != null && m.shader != null && m.shader.name == FenceFadeRules.ShaderName))
                    .Select(Path).ToList();
                Assert.IsEmpty(notFaded,
                    "These fence renderers are not on the fence fade shader, so they can still hide the local bird.");

                var strays = renderers.Where(r => !fence.Contains(r) && UsesFade(r)).Select(Path).ToList();
                Assert.IsEmpty(strays,
                    "Only the fence and the base coops may open around the bird — ground, piles, nests and other props must stay solid.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static string Path(Renderer r) =>
            (r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name;

        // ── GET READY YOU marker ────────────────────────────────────────────

        [Test]
        public void YouMarker_ShowsDuringGetReadyAndTheCountdownOnly()
        {
            Assert.IsTrue(YouMarkerRules.IsGetReady(MatchState.Starting, false), "GET READY");
            Assert.IsTrue(YouMarkerRules.IsGetReady(MatchState.Active, true), "3-2-1 countdown");
            Assert.IsFalse(YouMarkerRules.IsGetReady(MatchState.Active, false), "in play");
            Assert.IsFalse(YouMarkerRules.IsGetReady(MatchState.WaitingForPlayers, false), "waiting room");
            Assert.IsFalse(YouMarkerRules.IsGetReady(MatchState.Ended, false), "end screen");
        }

        [Test]
        public void YouMarker_ReducedMotion_IsStaticAndSnaps()
        {
            Assert.That(YouMarkerRules.StepEmphasis(0f, true, 0.001f, reducedMotion: true), Is.EqualTo(1f));
            Assert.That(YouMarkerRules.StepEmphasis(1f, false, 0.001f, reducedMotion: true), Is.EqualTo(0f));
            float a = YouMarkerRules.RingScaleAt(1f, 0.1f, reducedMotion: true);
            float b = YouMarkerRules.RingScaleAt(1f, 0.5f, reducedMotion: true);
            Assert.That(a, Is.EqualTo(b), "No pulse under Reduced Motion.");
            Assert.That(a, Is.EqualTo(YouMarkerRules.RingScale), "Still emphasised: the bigger ring is wayfinding.");
        }

        [Test]
        public void YouMarker_Pulses_AndSettlesBackToTheNormalRing()
        {
            float t0 = YouMarkerRules.RingScaleAt(1f, 0f, reducedMotion: false);
            float tq = YouMarkerRules.RingScaleAt(1f, 0.25f / YouMarkerRules.RingPulseHz, reducedMotion: false);
            Assert.That(tq, Is.GreaterThan(t0), "The ring pulses when motion is allowed.");
            Assert.That(YouMarkerRules.RingScaleAt(0f, 0.3f, false), Is.EqualTo(1f));
            Assert.That(YouMarkerRules.RingAlphaAt(0f, 0.75f), Is.EqualTo(0.75f));
            Assert.That(YouMarkerRules.RingAlphaAt(1f, 0.75f), Is.EqualTo(YouMarkerRules.RingAlpha));
            Assert.That(YouMarkerRules.NameplateScaleAt(0f), Is.EqualTo(1f));
            Assert.That(YouMarkerRules.NameplateScaleAt(1f), Is.GreaterThan(1f));
            float eased = YouMarkerRules.StepEmphasis(0f, true, YouMarkerRules.EaseSeconds * 0.5f, reducedMotion: false);
            Assert.That(eased, Is.EqualTo(0.5f).Within(1e-4f));
        }
    }
}
