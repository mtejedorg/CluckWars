using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Generates the per-class cast-archetype <see cref="AnimationClip"/> assets into
    /// <c>Assets/_Game/Art/Animations/Generated</c>. One clip per (class × <see cref="CastArchetype"/>),
    /// so 4 × 8 = 32. Idempotent — re-running overwrites in place.
    /// </summary>
    /// <remarks>
    /// <b>Why generated rather than exported from Blender.</b> Re-exporting the four rigs re-triggers
    /// the <c>externalObjects</c> material remap on each .fbx, which can drop silently and leave
    /// untextured chickens with nothing logged. The Peck clip was derived the same way and for the
    /// same reason. Nothing here touches an .fbx or its .meta.
    ///
    /// <b>Poses are expressed as local-euler deltas from each rig's own bind pose</b>
    /// (<c>localRotation = bindPose * Quaternion.Euler(delta)</c>), never as absolute rotations. The
    /// four rigs are fitted to four differently-proportioned meshes, so their bind poses differ; a
    /// delta lands the same *gesture* on each one, an absolute rotation would land the same numbers
    /// and four different gestures.
    ///
    /// <b>The sign conventions below were measured off the shipped clips, not guessed.</b> Sampling
    /// <c>warrior_chicken_rig|Cast/Idle/Walk/Hit/Stunned</c> and reading back each bone's delta from
    /// bind pose gives:
    /// <list type="bullet">
    /// <item>Spine/Chest/Neck/Head local <b>+X pitches forward and down</b>, −X rears back and up.</item>
    /// <item><b>Wings.</b> Local <b>X, symmetric</b> (the same sign on both wings) is elevation:
    /// +X lifts the wings off the flanks, −X clamps them down and draws the tips in (measured, at
    /// −45 the tip-to-tip span closes from 0.717 to 0.495). Local <b>Z, mirrored</b> is the fore/aft
    /// sweep: WingL −Z with WingR +Z sweeps back, the inverse sweeps forward. Local <b>Y does
    /// nothing at all</b> — see the warning below.</item>
    /// <item>Thighs: antisymmetric ±X is the walk stride. Symmetric +X does <b>not</b> fold the legs
    /// into a crouch, which an earlier revision here assumed — measured, it swings the whole leg
    /// backward: at Thigh +26 / Shin +19 the ankle ends up 32 cm behind the hips. See the note on
    /// legs below.</item>
    /// <item>Tail local <b>−X raises the tail</b> — it goes more negative as the body pitches forward,
    /// which is the counterbalance.</item>
    /// <item>Local Z on <b>Spine and Chest</b> is the lateral lean (Stunned's drunken sway rides on
    /// it). On the <b>Neck and Head</b> it is inert — see below.</item>
    /// <item>Tail local <b>Z</b> is a lateral fan and is by far its strongest axis (hull spread 0.255
    /// against 0.073 for X). Tail X is the up/down carry and is what the symmetric archetypes use.</item>
    /// </list>
    ///
    /// <b>Three axes on this rig are inert, and every one of them was being keyed.</b> Rotating a bone
    /// about its own long axis moves nothing — the child bone sits <i>on</i> that axis, so it lands
    /// exactly where it started. Probing each bone at ±35°–±40° and reading back the child's
    /// position and the baked mesh hull gives:
    /// <list type="bullet">
    /// <item><b>Wing Y — dead.</b> WingL Y+40 and Y−40 both leave WingTipL at exactly its bind
    /// position (−0.36, 0.42, −0.03). It is the wing's roll axis.</item>
    /// <item><b>WingTip Y — near-dead</b> (hull spread 0.006, against 0.051 for Z).</item>
    /// <item><b>Neck Z and Head Z — near-dead</b> (0.002 and 0.001, against 0.407 and 0.437 for X).</item>
    /// </list>
    /// This matters more than a tuning note. The first version of this table put <i>every</i> wing
    /// delta on Y and built Puff's shimmy and Dodge's head counter-lean on Head/Neck Z, so the wings
    /// never moved in any of the 32 clips and two of the shimmies were invisible. The design leans on
    /// wings for most of its silhouette separation, so with them inert the eight archetypes collapsed
    /// into two shapes — "pitched forward" and "reared back" — and five of them were mutually
    /// indistinguishable. Nothing detected it: no error, no warning, and the per-frame feature
    /// assertions passed on all four classes, because a wing that never moves cannot violate a
    /// relationship. Only rendering the peaks side by side and looking at them showed it.
    ///
    /// <b>So: never put a cast delta on Wing Y, WingTip Y, Neck Z or Head Z.</b> They are silent
    /// no-ops, and a no-op is indistinguishable from a pose that is merely too subtle.
    ///
    /// <b>The neck rule, and what it actually is.</b> Project lore records "neck rotation stays
    /// negative"; the measured reason is more useful than the rule. The neck pivot sits inside the
    /// torso, so pitching the neck <i>forward</i> (+X) drives the head into the chest — which is why
    /// +34 made the head vanish. Reach is produced the way the shipped Idle's own peck beat produces
    /// it: <b>neck negative, head positive</b> (Idle peaks at Neck −27 / Head +40). Positive neck is
    /// legal but only as compression, and only small — Stunned's +26 slump is the shipped maximum, so
    /// nothing here exceeds it.
    ///
    /// <b>The legs do not move, and that is the fix rather than a limitation.</b> The first pass gave
    /// most archetypes a symmetric thigh/shin fold as a "dip" or "brace". Rendered, every one of them
    /// was wrong, and wrong in a way no number caught:
    /// <list type="bullet">
    /// <item>The hips are the root of the leg chain, so a rotation-only clip cannot lower the body.
    /// Folding the thighs lifted the feet instead — the wind-ups read as a chicken floating
    /// nose-down with its legs trailing behind it.</item>
    /// <item>Compensating by keying the hips down (a revision that lived here briefly) pinned the
    /// <i>ankle bone</i> at rest height and looked perfect in the report — while the toes, which are
    /// what actually touch the floor, went 29 cm through it. The ankle was a proxy for ground
    /// contact and the proxy is what got satisfied.</item>
    /// <item>Symmetric thigh is not a fold at all (see the sign list above), so the "crouch" was a
    /// backward leg swing the whole time.</item>
    /// </list>
    /// With the leg deltas removed the legs sit at bind pose in every frame of every clip, so the
    /// feet are planted by construction — no compensation, no position curves, nothing to drift. The
    /// dip is not missed: silhouette separation lives in the spine, wings, neck and tail, which is
    /// where it was already doing all the work. A real crouch needs the knee to bend while the foot
    /// stays put, which is two-bone IK; if one is ever wanted, solve it, do not approximate it with
    /// symmetric thigh rotation.
    /// </remarks>
    public static class CastArchetypeClipGenerator
    {
        public const string GeneratedDir = "Assets/_Game/Art/Animations/Generated";
        private const string RegistryPath = "Assets/_Game/Data/ChickenClassRegistry.asset";
        private const float FrameRate = 24f;

        /// <summary>
        /// The bones every archetype clip keys, in hierarchy order. <b>All of them, every clip</b> —
        /// including the ones a given archetype never moves.
        /// </summary>
        /// <remarks>
        /// Keying only the moving bones would be lighter but wrong: with a single animator layer, a
        /// bone that a state does not key holds whatever the <i>previous</i> state left on it. A
        /// Lunge that skipped the tail would inherit Stunned's tail and keep it for the whole cast.
        /// The shipped .fbx clips are fully baked for the same reason; matching that makes every
        /// state a complete pose that cannot inherit drift.
        ///
        /// Root, Hips and the feet appear here but are never given a non-zero delta by any pose —
        /// they are keyed at bind pose so they are pinned rather than merely unmentioned. The model
        /// root itself is deliberately absent: it belongs to <c>ChickenAnimator</c>, and a clip that
        /// keyed it would fight the lean/bank write.
        /// </remarks>
        private static readonly string[] KeyedBones =
        {
            "Root", "Hips", "Spine", "Chest", "Neck", "Head", "Beak",
            "WingL", "WingTipL", "WingR", "WingTipR", "Tail",
            "ThighL", "ShinL", "FootL", "ThighR", "ShinR", "FootR",
        };

        /// <summary>
        /// The neutral the shipped Idle sits at (tail carried up; everything else at bind pose).
        /// Every archetype opens and closes here rather than at raw bind pose, so the blend in and
        /// out of Idle has nothing to travel.
        /// </summary>
        private static readonly (string Bone, float X, float Y, float Z)[] Neutral =
        {
            ("Tail", -14f, 0f, 0f),
        };

        private readonly struct Pose
        {
            public readonly float Time;
            public readonly (string Bone, float X, float Y, float Z)[] Deltas;
            public Pose(float time, params (string, float, float, float)[] deltas)
            {
                Time = time;
                Deltas = deltas;
            }
        }

        /// <summary>
        /// The eight archetypes. Times are seconds; each clip's length is its last key. Lengths sit
        /// in the 0.65–0.85 s band the shipped Cast (0.833 s) already occupies, so the existing
        /// state exit timing and the ability activation feel stay comparable.
        /// </summary>
        /// <remarks>
        /// <b>Every archetype owns one dominant axis, and no two share theirs.</b> The rig gives only
        /// rotation on a rounded body with small wings, so the pose space is genuinely tight and two
        /// archetypes that lead with the same axis will read as the same shape no matter how their
        /// other bones differ. Measured by pairwise silhouette IoU over side / three-quarter / front
        /// renders of each peak, the assignment is:
        /// <list type="bullet">
        /// <item><b>Lunge</b> — body pitch forward, at the set maximum (+28).</item>
        /// <item><b>Slam</b> — wing elevation minimum (−62), reached from the maximum wind-up (+62).</item>
        /// <item><b>Flare</b> — wing elevation maximum (+72), body reared.</item>
        /// <item><b>Grab</b> — neck extension maximum (−36 / +52), body upright, wings still.</item>
        /// <item><b>Throw</b> — left/right wing disagreement (fore-aft split).</item>
        /// <item><b>Hunker</b> — neck compression (+24), wings drawn in, and the set's only raised tail.</item>
        /// <item><b>Puff</b> — vertical inflate plus the lateral shimmy, which nothing else has.</item>
        /// <item><b>Dodge</b> — lateral bank maximum (+38), wings at uneven elevation.</item>
        /// </list>
        /// Two collisions were found and fixed this way rather than by eye, and both had passed every
        /// per-frame feature assertion: Slam once shared Hunker's clamp (0.912 IoU) by ending on
        /// wings-down with the tail up, and Lunge and Slam shared a forward body (0.910 on Fatty)
        /// until Lunge gave up the low wings and Slam gave up the pitch.
        /// </remarks>
        private static readonly Dictionary<CastArchetype, Pose[]> Archetypes = new()
        {
            // Coil back, then drive the whole body forward: beak leading, wings swept BACK and
            // slightly down so the silhouette narrows to an arrow. Reach is neck-negative /
            // head-positive. Wings-back is Lunge's alone — Slam drives them down, Flare throws them
            // up and forward.
            [CastArchetype.Lunge] = new[]
            {
                new Pose(0.00f),
                new Pose(0.17f, ("Spine",-10,0,0), ("Chest",-6,0,0), ("Neck",4,0,0), ("Head",-8,0,0),
                                ("WingL",-8,0,14), ("WingR",-8,0,-14), ("Tail",-6,0,0)),
                new Pose(0.38f, ("Spine",28,0,0), ("Chest",17,0,0), ("Neck",-22,0,0), ("Head",34,0,0),
                                ("WingL",4,0,-44), ("WingR",4,0,44),
                                ("WingTipL",0,0,-24), ("WingTipR",0,0,24), ("Tail",-26,0,0)),
                new Pose(0.58f, ("Spine",16,0,0), ("Chest",10,0,0), ("Neck",-12,0,0), ("Head",20,0,0),
                                ("WingL",-10,0,-18), ("WingR",-10,0,18), ("Tail",-22,0,0)),
                new Pose(0.75f),
            },

            // Rear up with the wings thrown HIGH, then drive body and wings down into the floor. The
            // signature is the vertical wing travel — +62 to -62 is the largest excursion in the set,
            // and Slam owns the bottom of that range outright.
            //
            // The tail stays DOWN and the wings sweep FORWARD at the peak, and both of those are
            // load-bearing. A revision that ended Slam on wings-down-and-back with the tail thrown up
            // scored 0.912 silhouette IoU against Hunker — the worst collision the set has had —
            // because "wings low, drawn in, tail up" is precisely Hunker's clamp. Slam is a strike, so
            // it commits forward: body pitched, head driving down and out (neck negative / head
            // positive), wings low but open. Hunker owns tail-up; nothing else may take it.
            [CastArchetype.Slam] = new[]
            {
                new Pose(0.00f),
                new Pose(0.21f, ("Spine",-20,0,0), ("Chest",-14,0,0), ("Neck",-10,0,0), ("Head",-14,0,0),
                                ("WingL",62,0,10), ("WingR",62,0,-10),
                                ("WingTipL",20,0,0), ("WingTipR",20,0,0), ("Tail",-4,0,0)),
                new Pose(0.42f, ("Spine",18,0,0), ("Chest",12,0,0), ("Neck",-9,0,0), ("Head",19,0,0),
                                ("WingL",-62,0,20), ("WingR",-62,0,-20),
                                ("WingTipL",-22,0,16), ("WingTipR",-22,0,-16), ("Tail",-30,0,0)),
                new Pose(0.58f, ("Spine",8,0,0), ("Chest",5,0,0), ("Head",6,0,0),
                                ("WingL",8,0,0), ("WingR",8,0,0), ("Tail",-18,0,0)),
                new Pose(0.75f),
            },

            // Compress inward, then burst: the body rears and the wings go to their MAXIMUM elevation
            // and sweep forward — the tallest, most open shape in the set.
            //
            // The head barely participates, which is the whole trick. An earlier tuning threw the
            // head back with the chest (Neck -16 / Head -24 on top of Spine -22) and the result was
            // unmistakably a rooster crowing — the three negatives stack down the chain, the neck
            // stretches into a vertical stalk and the beak ends up pointing up and behind the comb.
            // Keeping the head level while everything under it opens up is what reads as a burst
            // coming off the body.
            [CastArchetype.Flare] = new[]
            {
                new Pose(0.00f),
                new Pose(0.17f, ("Spine",12,0,0), ("Chest",8,0,0), ("Neck",10,0,0), ("Head",6,0,0),
                                ("WingL",-22,0,-10), ("WingR",-22,0,10), ("Tail",-4,0,0)),
                new Pose(0.38f, ("Spine",-18,0,0), ("Chest",-14,0,0), ("Neck",-4,0,0), ("Head",-6,0,0),
                                ("WingL",72,0,30), ("WingR",72,0,-30),
                                ("WingTipL",26,0,34), ("WingTipR",26,0,-34), ("Tail",4,0,0)),
                new Pose(0.58f, ("Spine",-7,0,0), ("Chest",-5,0,0), ("Neck",-2,0,0), ("Head",-3,0,0),
                                ("WingL",34,0,14), ("WingR",34,0,-14),
                                ("WingTipL",12,0,14), ("WingTipR",12,0,-14), ("Tail",-8,0,0)),
                new Pose(0.75f),
            },

            // Wind up small, dart the head out, close, then yank it back with the whole body.
            // Grab is deliberately the one archetype whose WINGS STAY PUT: it is a neck gesture, and
            // holding everything else still is what makes the neck read. Reach is the largest in the
            // set (Neck -36 / Head +52), which is the shipped Idle peck beat pushed further.
            [CastArchetype.Grab] = new[]
            {
                new Pose(0.00f),
                new Pose(0.15f, ("Spine",-6,0,0), ("Neck",8,0,0), ("Head",-12,0,0),
                                ("WingL",-6,0,8), ("WingR",-6,0,-8), ("Tail",-8,0,0)),
                new Pose(0.31f, ("Spine",8,0,0), ("Chest",5,0,0), ("Neck",-36,0,0), ("Head",52,0,0),
                                ("WingL",-8,0,-12), ("WingR",-8,0,12), ("Tail",-28,0,0)),
                new Pose(0.46f, ("Spine",9,0,0), ("Chest",6,0,0), ("Neck",-31,0,0), ("Head",45,0,0),
                                ("WingL",-6,0,-8), ("WingR",-6,0,8), ("Tail",-28,0,0)),
                new Pose(0.60f, ("Spine",-18,0,0), ("Chest",-12,0,0), ("Neck",10,0,0), ("Head",-22,0,0),
                                ("WingL",16,0,10), ("WingR",16,0,-10), ("Tail",-2,0,0)),
                new Pose(0.70f),
            },

            // Asymmetric: the right wing cocks back and whips across while the body counter-leans,
            // then the head follows the release out and down. The asymmetry is the signature — it is
            // the only archetype whose two wings disagree — so the gain goes into the wing split and
            // the counter-lean rather than into pitch, which would only make it a weaker Lunge.
            [CastArchetype.Throw] = new[]
            {
                new Pose(0.00f),
                new Pose(0.21f, ("Spine",-9,0,-19), ("Chest",-5,0,-11), ("Neck",-6,0,0), ("Head",-8,0,0),
                                ("WingR",40,0,-55), ("WingTipR",14,0,-28), ("WingL",-12,0,6),
                                ("Tail",-6,0,-13)),
                new Pose(0.44f, ("Spine",14,0,34), ("Chest",9,0,20), ("Neck",-16,0,0), ("Head",25,0,0),
                                ("WingR",10,0,50), ("WingTipR",6,0,30), ("WingL",-20,0,-10),
                                ("Tail",-24,0,24)),
                new Pose(0.62f, ("Spine",7,0,8), ("Chest",5,0,5), ("Neck",-7,0,0), ("Head",11,0,0),
                                ("WingR",4,0,14), ("WingL",-2,0,-4), ("Tail",-19,0,6)),
                new Pose(0.80f),
            },

            // Drop into a braced stance and hold it: wings clamped hard DOWN and drawn in, head pulled
            // into the shoulders, tail up. Neck positive is compression here and is capped at
            // Stunned's shipped 26.
            //
            // Spine pitch is small on purpose. The first tuning braced with Spine +16/+18 and, with
            // the hips pinned, that was not a crouch at all — the whole bird rotated forward over its
            // hips and read as tripping. Hunker therefore does not go *down*: with the legs at bind
            // pose there is no way to lower the body (see the class note), so the brace is carried
            // entirely by the clamp. Wing X -52 is the set's minimum elevation and closes the tip
            // span to roughly 0.50 against a 0.72 bind — a genuinely narrower silhouette, and narrow
            // is the read that matters, since faking depth with spine pitch only bought a stumble.
            [CastArchetype.Hunker] = new[]
            {
                new Pose(0.00f),
                new Pose(0.25f, ("Spine",7,0,0), ("Chest",5,0,0), ("Neck",20,0,0), ("Head",4,0,0),
                                ("WingL",-46,0,-14), ("WingR",-46,0,14),
                                ("WingTipL",-18,0,-8), ("WingTipR",-18,0,8), ("Tail",4,0,0)),
                new Pose(0.52f, ("Spine",8,0,0), ("Chest",6,0,0), ("Neck",24,0,0), ("Head",2,0,0),
                                ("WingL",-52,0,-18), ("WingR",-52,0,18),
                                ("WingTipL",-20,0,-10), ("WingTipR",-20,0,10), ("Tail",6,0,0)),
                new Pose(0.80f),
            },

            // Dip, puff up tall, then shake it out — two mirrored lateral flicks on the way down.
            //
            // Deliberately kept off Flare's territory. Both are "compress, then expand", so if Puff
            // also threw its wings to full elevation the two were the same pose at different
            // amplitudes. Puff is a *vertical* inflate: the body stands up tall, the head stays high
            // and level, and the wings only just leave the flanks (X +26 against Flare's +72). The
            // read is carried by the shimmy, which no other archetype has.
            //
            // The shimmy rides on Spine/Chest Z, NOT on Head/Neck Z: those are inert on this rig (see
            // the class note), and the first version of this shake was built on them and did nothing.
            [CastArchetype.Puff] = new[]
            {
                new Pose(0.00f),
                new Pose(0.15f, ("Spine",10,0,0), ("Chest",7,0,0), ("Neck",8,0,0), ("Head",4,0,0),
                                ("WingL",-14,0,6), ("WingR",-14,0,-6), ("Tail",-6,0,0)),
                new Pose(0.33f, ("Spine",-16,0,0), ("Chest",-11,0,0), ("Neck",-2,0,0), ("Head",8,0,0),
                                ("WingL",16,0,6), ("WingR",16,0,-6),
                                ("WingTipL",4,0,4), ("WingTipR",4,0,-4), ("Tail",-28,0,0)),
                new Pose(0.46f, ("Spine",-14,0,8), ("Chest",-9,0,5), ("Neck",-2,0,0), ("Head",6,0,0),
                                ("WingL",20,0,14), ("WingR",20,0,2), ("Tail",-26,0,6)),
                new Pose(0.58f, ("Spine",-14,0,-8), ("Chest",-9,0,-5), ("Neck",-2,0,0), ("Head",6,0,0),
                                ("WingL",20,0,-2), ("WingR",20,0,-14), ("Tail",-26,0,-6)),
                new Pose(0.75f),
            },

            // Load onto one side, break hard across, recover. Lives on Spine/Chest Z, the lateral-lean
            // axis, and is the only archetype that banks. The wings counter-balance at UNEVEN
            // elevation (outside wing higher), a different kind of asymmetry from Throw's fore/aft
            // wing split.
            [CastArchetype.Dodge] = new[]
            {
                new Pose(0.00f),
                new Pose(0.13f, ("Spine",6,0,-15), ("Chest",4,0,-8), ("Neck",0,0,0), ("Head",0,0,0),
                                ("WingL",-10,0,4), ("WingR",-10,0,-4), ("Tail",-8,0,-10)),
                new Pose(0.31f, ("Spine",-7,0,38), ("Chest",-5,0,20), ("Neck",-4,0,0), ("Head",-8,0,0),
                                ("WingL",48,0,16), ("WingR",20,0,-16),
                                ("WingTipL",18,0,0), ("WingTipR",6,0,0), ("Tail",-20,0,19)),
                new Pose(0.48f, ("Spine",0,0,16), ("Chest",0,0,8), ("Neck",0,0,0), ("Head",0,0,0),
                                ("WingL",20,0,7), ("WingR",9,0,-7), ("Tail",-13,0,6)),
                new Pose(0.65f),
            },
        };

        [MenuItem("Cluck Wars/Animation/Generate Cast Archetype Clips")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[CastArchetypeClipGenerator] Not available in Play Mode — asset edits " +
                               "made now are reverted when Play Mode exits, so this would appear to " +
                               "succeed and silently write nothing. Stop Play Mode and re-run.");
                return;
            }

            var problems = new List<string>();
            var clamped = new List<string>();
            int written = 0;

            if (!AssetDatabase.IsValidFolder(GeneratedDir))
                AssetDatabase.CreateFolder("Assets/_Game/Art/Animations", "Generated");

            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(RegistryPath);
            if (registry == null)
            {
                Debug.LogError($"[CastArchetypeClipGenerator] No ChickenClassRegistrySO at '{RegistryPath}'.");
                return;
            }

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                if (!registry.TryGet(cls, out var entry) || entry.ModelPrefab == null)
                {
                    problems.Add($"{cls}: no registry entry or no ModelPrefab — skipped.");
                    continue;
                }

                // Bind poses come from the .fbx prefab asset itself, never from a scene instance:
                // an instance may already be mid-pose from a previous SampleAnimation.
                var bindPose = new Dictionary<string, (string Path, Quaternion Rot)>();
                var root = entry.ModelPrefab.transform;
                foreach (var t in entry.ModelPrefab.GetComponentsInChildren<Transform>(true))
                {
                    if (t == root) continue;
                    if (bindPose.ContainsKey(t.name)) continue;
                    bindPose[t.name] = (RelativePath(root, t), t.localRotation);
                }

                var missing = KeyedBones.Where(b => !bindPose.ContainsKey(b)).ToArray();
                if (missing.Length > 0)
                {
                    problems.Add($"{cls}: rig '{entry.ModelPrefab.name}' is missing bone(s) " +
                                 $"[{string.Join(", ", missing)}] — skipped. Expected the 18-bone bird rig.");
                    continue;
                }

                // A live instance of this class's rig, used to bake the skinned mesh and check that
                // no pose drives geometry through the floor. See FindSafeScale.
                var rigInst = (GameObject)PrefabUtility.InstantiatePrefab(entry.ModelPrefab);
                rigInst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var smrs = rigInst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs)
                {
                    smr.forceMatrixRecalculationPerRender = true;
                    smr.updateWhenOffscreen = true;
                }
                float restY = LowestVertex(smrs);

                foreach (var kv in Archetypes)
                {
                    string path = ClipPath(cls, kv.Key);
                    float scale = FindSafeScale(kv.Value, bindPose, rigInst, smrs, restY);
                    if (scale < 0.999f)
                    {
                        clamped.Add($"{cls}/{kv.Key} scaled to {scale:P0}");
                        if (scale < 0.55f)
                            problems.Add($"{cls}/{kv.Key}: had to be scaled to {scale:P0} to keep its " +
                                         "geometry above the floor. That is no longer the authored " +
                                         "gesture \u2014 retune the pose for this rig instead of shipping it.");
                    }
                    BuildClip(kv.Value, bindPose, path, scale);
                    written++;
                }

                Object.DestroyImmediate(rigInst);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (clamped.Count > 0)
                Debug.LogWarning("[CastArchetypeClipGenerator] Ground-clamped (rig geometry hangs lower " +
                                 "than the pose assumed, so the gesture was scaled down for that class " +
                                 "only):\n  - " + string.Join("\n  - ", clamped));

            if (problems.Count > 0)
                Debug.LogError("[CastArchetypeClipGenerator] Completed WITH PROBLEMS:\n  - " +
                               string.Join("\n  - ", problems));
            else
                Debug.Log($"[CastArchetypeClipGenerator] Wrote {written} cast-archetype clips " +
                          $"({Archetypes.Count} archetypes x 4 classes) into '{GeneratedDir}'.");
        }

        /// <summary>
        /// The asset path for one class/archetype clip. Shared with
        /// <see cref="ChickenAnimationSetup"/> so the generator and the wiring pass cannot
        /// disagree about where a clip lives.
        /// </summary>
        public static string ClipPath(ChickenClass cls, CastArchetype archetype) =>
            $"{GeneratedDir}/{cls.ToString().ToLowerInvariant()}_Cast_{archetype}.anim";

        /// <summary>
        /// Path from <paramref name="root"/> down to <paramref name="t"/>, excluding the root's own
        /// name. That is the binding origin Unity uses for a Generic avatar, and it is what the
        /// shipped .fbx clips (and the derived Peck clips) already use — <c>warrior_chicken_rig/Root/…</c>,
        /// not <c>warrior_chicken_rigged/warrior_chicken_rig/Root/…</c>.
        /// </summary>
        private static string RelativePath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent) parts.Insert(0, cur.name);
            return string.Join("/", parts);
        }

        private static void BuildClip(
            Pose[] poses,
            Dictionary<string, (string Path, Quaternion Rot)> bindPose,
            string assetPath,
            float scale = 1f)
        {
            float length = poses[poses.Length - 1].Time;

            // One rotation track per keyed bone, sampled at every pose time. Bones a pose does not
            // mention resolve to the neutral delta, so every track is fully defined at every key.
            var tracks = KeyedBones.ToDictionary(b => b, _ => new List<(float T, Quaternion Q)>());

            foreach (var pose in poses)
            {
                foreach (var bone in KeyedBones)
                {
                    Vector3 d = Delta(pose, bone) * scale;
                    tracks[bone].Add((pose.Time, bindPose[bone].Rot * Quaternion.Euler(d)));
                }
            }

            // Named to match the asset file. EditorUtility.CopySerialized below copies the whole
            // object including its name, so an unnamed in-memory clip silently blanks the name of
            // the asset it overwrites -- which is what produced 32 "Main Object Name '' does not
            // match filename" warnings on every regenerate, and left every clip's name empty when
            // read back through the registry.
            var clip = new AnimationClip
            {
                frameRate = FrameRate,
                name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
            };
            WriteTracks(clip, tracks, bindPose);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;   // every cast beat is one-shot
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (existing != null)
            {
                // Overwrite in place rather than delete+create: the .anim's GUID is referenced from
                // ChickenClassRegistry.asset, and replacing the file would break those references
                // and quietly re-introduce the shared-Cast fallback.
                EditorUtility.CopySerialized(clip, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(clip);
            }
            else
            {
                AssetDatabase.CreateAsset(clip, assetPath);
            }
        }

        /// <summary>Builds the same tracks BuildClip would, into an existing (unsaved) clip.</summary>
        private static void BuildCurves(
            Pose[] poses,
            Dictionary<string, (string Path, Quaternion Rot)> bindPose,
            AnimationClip clip,
            float scale)
        {
            var tracks = KeyedBones.ToDictionary(b => b, _ => new List<(float T, Quaternion Q)>());
            foreach (var pose in poses)
                foreach (var bone in KeyedBones)
                    tracks[bone].Add((pose.Time,
                        bindPose[bone].Rot * Quaternion.Euler(Delta(pose, bone) * scale)));
            WriteTracks(clip, tracks, bindPose);
        }

        /// <summary>Writes one rotation track per keyed bone, fixing quaternion sign continuity first.</summary>
        private static void WriteTracks(
            AnimationClip clip,
            Dictionary<string, List<(float T, Quaternion Q)>> tracks,
            Dictionary<string, (string Path, Quaternion Rot)> bindPose)
        {
            foreach (var bone in KeyedBones)
            {
                var keys = tracks[bone];

                // Quaternion continuity: a rotation and its negation are the same orientation, but
                // the four component curves are interpolated independently, so a sign flip between
                // consecutive keys makes the bone take the long way round — a full spin mid-cast.
                for (int i = 1; i < keys.Count; i++)
                {
                    if (Quaternion.Dot(keys[i - 1].Q, keys[i].Q) < 0f)
                    {
                        var q = keys[i].Q;
                        keys[i] = (keys[i].T, new Quaternion(-q.x, -q.y, -q.z, -q.w));
                    }
                }

                string path = bindPose[bone].Path;
                SetCurve(clip, path, "m_LocalRotation.x", keys.Select(k => (k.T, k.Q.x)));
                SetCurve(clip, path, "m_LocalRotation.y", keys.Select(k => (k.T, k.Q.y)));
                SetCurve(clip, path, "m_LocalRotation.z", keys.Select(k => (k.T, k.Q.z)));
                SetCurve(clip, path, "m_LocalRotation.w", keys.Select(k => (k.T, k.Q.w)));
            }
        }

        /// <summary>Lowest world-space vertex across every skinned mesh, with the rig at the origin.</summary>
        private static float LowestVertex(SkinnedMeshRenderer[] smrs)
        {
            float lowest = float.MaxValue;
            foreach (var smr in smrs)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var tr = smr.transform;
                foreach (var v in baked.vertices)
                {
                    float y = tr.TransformPoint(v).y;
                    if (y < lowest) lowest = y;
                }
                Object.DestroyImmediate(baked);
            }
            return lowest;
        }

        /// <summary>
        /// The largest fraction of the authored gesture this rig can perform without pushing geometry
        /// through the floor. 1.0 whenever the pose is already safe, which is the usual answer.
        /// </summary>
        /// <remarks>
        /// <b>This exists because the four rigs share one pose table but not one silhouette.</b> The
        /// Assassin carries long chest and underside plumes, and pitching its body forward swings them
        /// down: measured, it breaks the floor at Spine +22 while the other three are still clean at
        /// +40. Lunge's authored +28 put 15 cm of the Assassin's mesh underground.
        ///
        /// Nothing caught that. It is invisible to a bone check — the feet are pinned at bind pose in
        /// every frame by construction, so every foot assertion passed — and invisible to a contact
        /// sheet rendered against empty space, because a chicken with its chin through an invisible
        /// floor just looks like a chicken. It took rendering against an actual floor plane and then
        /// baking the mesh to find it. Bones are not geometry, and only geometry can clip.
        ///
        /// Scaling the whole gesture uniformly is deliberate: it keeps the pose's shape and timing and
        /// only reduces its amplitude, so a clamped clip still reads as the same archetype. A clamp
        /// below 55% is reported as a problem rather than applied quietly, because at that point the
        /// gesture has been shrunk past recognition and the pose wants authoring for that rig, not
        /// scaling.
        /// </remarks>
        private static float FindSafeScale(
            Pose[] poses,
            Dictionary<string, (string Path, Quaternion Rot)> bindPose,
            GameObject rig,
            SkinnedMeshRenderer[] smrs,
            float restY)
        {
            // 1 cm, about 0.6% of a chicken's height. Tight enough that the Assassin's 15 cm
            // Lunge is caught by a factor of fifteen, loose enough that the Speedy rig -- whose
            // toe geometry already rests exactly on y=0, so any pose dips it a few millimetres --
            // is not punished for noise. At 3 mm it was: Speedy/Slam got scaled to 29% and
            // Speedy/Throw to 28% to chase a 6 mm dip nobody could see, which is a far worse
            // outcome than the dip.
            const float Tolerance = 0.010f;

            if (Penetration(1f) <= Tolerance) return 1f;

            // Bisect. Eight steps lands within ~0.4%, which is far finer than the pose is authored to.
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 8; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Penetration(mid) <= Tolerance) lo = mid; else hi = mid;
            }
            // Round down to whole percent for a readable report, then re-verify: flooring can
            // land a hair on the wrong side of the tolerance, and a generator that emits a clip
            // its own check would reject is worse than one that clamps a percent harder.
            float safe = Mathf.Floor(lo * 100f) / 100f;
            while (safe > 0.01f && Penetration(safe) > Tolerance) safe -= 0.01f;
            return safe;

            float Penetration(float scale)
            {
                var probe = new AnimationClip { frameRate = FrameRate };
                BuildCurves(poses, bindPose, probe, scale);
                float length = poses[poses.Length - 1].Time;
                // Sampled at twice the clip's frame rate. At 1/24 s this check ran at exactly the
                // key rate and stepped over intermediate frames, passing a pose that a 1/48 s sweep
                // then caught — the generator was signing off on a clip its own verification
                // rejected. Undersampling a continuous signal is how the defect hides, not how it
                // gets cheaper.
                float worst = 0f;
                for (float t = 0f; t <= length + 1e-4f; t += 1f / 48f)
                {
                    probe.SampleAnimation(rig, t);
                    worst = Mathf.Max(worst, restY - LowestVertex(smrs));
                }
                Object.DestroyImmediate(probe);
                return worst;
            }
        }

        /// <summary>This pose's delta for one bone: its own entry, else the shared neutral, else zero.</summary>
        private static Vector3 Delta(Pose pose, string bone)
        {
            foreach (var d in pose.Deltas)
                if (d.Bone == bone) return new Vector3(d.X, d.Y, d.Z);
            foreach (var n in Neutral)
                if (n.Bone == bone) return new Vector3(n.X, n.Y, n.Z);
            return Vector3.zero;
        }

        private static void SetCurve(
            AnimationClip clip, string path, string property, IEnumerable<(float T, float V)> keys)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.T, k.V)).ToArray());
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(
                clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
        }
    }
}
