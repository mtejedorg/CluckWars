using System;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Maps a <see cref="ChickenClass"/> to its tunables and presentation metadata.
    /// One asset for the whole game lives at <c>Assets/_Game/Data/ChickenClassRegistry.asset</c>.
    /// </summary>
    /// <remarks>
    /// Beyond stats + tint, each entry carries the class's art: a model prefab, the
    /// <see cref="Avatar"/> that goes with it, and the five skeletal clips that drive it.
    /// <see cref="ChickenController.Spawned"/> instantiates the model under the chicken
    /// root, hands the avatar to the root <see cref="Animator"/>, and applies the clips
    /// through a runtime <see cref="AnimatorOverrideController"/>.
    ///
    /// The <em>state machine</em> stays shared — one <c>Chicken.controller</c> authors the
    /// five states and their transitions for every class. What differs per class is the
    /// skeleton (avatar) and the motion on each state (clips), because each rig was
    /// exported with its own copy of the animation. Ability load-outs are authored elsewhere.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ChickenClassRegistry",
        menuName = "Cluck Wars/Chicken Class Registry",
        order = 2)]
    public sealed class ChickenClassRegistrySO : ScriptableObject
    {
        /// <summary>
        /// The five skeletal clips that drive one class's rig, mirroring the five states on
        /// the shared <c>Chicken.controller</c>. Applied at spawn through a runtime
        /// <see cref="AnimatorOverrideController"/> — see <c>ChickenController.AttachClassModel</c>.
        /// </summary>
        /// <remarks>
        /// Every clip MUST be a sub-asset of the same .fbx as the entry's
        /// <see cref="Entry.ModelPrefab"/>. A clip authored against a different class's rig
        /// references bone paths that do not exist on this skeleton, so it binds to nothing and
        /// the chicken silently freezes in bind pose rather than erroring.
        /// <c>DataIntegrityTests.ClassRegistry_EveryEntry_HasFiveClipsFromItsOwnFbx</c> enforces
        /// the same-file rule; the ordering here is deliberate and matches the controller's
        /// override keys.
        ///
        /// These are referenced from the registry rather than discovered from the instantiated
        /// model at runtime because the clips are *sibling sub-assets* of the .fbx that nothing
        /// in the spawned hierarchy points at — enumerating them needs <c>AssetDatabase</c>,
        /// which does not exist in a player build.
        /// </remarks>
        [Serializable]
        public struct ClassClips
        {
            public AnimationClip Idle;
            public AnimationClip Walk;
            public AnimationClip Cast;
            public AnimationClip Hit;
            public AnimationClip Stunned;

            /// <summary>True only when all five slots are populated. A partial set is treated as
            /// no set at all: overriding three of five states leaves the others on the empty
            /// placeholder, which reads as a chicken that freezes on those beats.</summary>
            public bool IsComplete =>
                Idle != null && Walk != null && Cast != null && Hit != null && Stunned != null;

            /// <summary>Names the first missing slot, for error messages. Null when complete.</summary>
            public string FirstMissing =>
                Idle    == null ? nameof(Idle)
                : Walk  == null ? nameof(Walk)
                : Cast  == null ? nameof(Cast)
                : Hit   == null ? nameof(Hit)
                : Stunned == null ? nameof(Stunned)
                : null;
        }

        [Serializable]
        public struct Entry
        {
            public ChickenClass Class;
            public ChickenStatsSO Stats;
            [ColorUsage(showAlpha: false, hdr: false)]
            public Color TintColor;

            [Tooltip("How strongly TintColor washes over the model's baked albedo atlas. " +
                     "0 = pure texture, no class wash at all. 1 = the old full-strength flat tint, " +
                     "which multiplies the atlas into mud — the four tints are near-identical in hue " +
                     "to the four textures, so at high values the baked art is washed out and every " +
                     "class reads as one flat colour. ChickenVisuals.ApplyTint lerps white→TintColor " +
                     "by this amount before pushing it. Authored ~0.25: enough hue cue to read a class " +
                     "at ortho-iso distance when the nameplate is small or occluded, without eating " +
                     "the atlas. Note this is CLASS identity, not player identity — player colour is " +
                     "carried by ChickenNameplate.PlayerColors.")]
            [Range(0f, 1f)]
            public float TintStrength;

            public string LoreQuote;

            [Tooltip("Model prefab (the imported .fbx) instantiated under the chicken root at spawn. " +
                     "Must be authored with its origin at the feet and a height matching the " +
                     "CharacterController's — see ChickenController.AttachClassModel.")]
            public GameObject ModelPrefab;

            [Tooltip("GENERIC Avatar sub-asset of ModelPrefab's .fbx, assigned to the root Animator. " +
                     "Must not be Humanoid: humanoid retargeting reshapes the rig to human " +
                     "proportions, which collapses the chicken's rest pose and sinks it ~1m " +
                     "through the floor. Measured 2026-08-07 — see DataIntegrityTests.")]
            public Avatar ModelAvatar;

            [Tooltip("The five skeletal AnimationClips for this class, taken from the SAME .fbx as " +
                     "ModelPrefab. They override the placeholder motions on the shared " +
                     "Chicken.controller at spawn. All five are required — a partial set is " +
                     "rejected and the chicken falls back to procedural motion.")]
            public ClassClips Clips;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>Look up an entry by class. Returns false if missing.</summary>
        public bool TryGet(ChickenClass cls, out Entry entry)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Class == cls)
                {
                    entry = _entries[i];
                    return true;
                }
            }
            entry = default;
            return false;
        }

        /// <summary>Defaults to Warrior if the requested class is missing.</summary>
        public Entry GetOrDefault(ChickenClass cls)
        {
            return TryGet(cls, out var entry) ? entry
                : TryGet(ChickenClass.Warrior, out var fallback) ? fallback
                : default;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Catch the obvious authoring slip: two entries for the same class.
            if (_entries == null) return;
            for (int i = 0; i < _entries.Length; i++)
            for (int j = i + 1; j < _entries.Length; j++)
            {
                if (_entries[i].Class == _entries[j].Class)
                {
                    Debug.LogWarning(
                        $"[ChickenClassRegistry] Duplicate entry for {_entries[i].Class} at indices {i} and {j}.",
                        this);
                }
            }
        }
#endif
    }
}
