namespace CluckWars.Abilities
{
    /// <summary>
    /// The body action a cast reads as, independent of what the ability mechanically does.
    /// Every ability declares one; <c>ChickenAnimator.TriggerAbilityCast</c> routes it to the
    /// matching state on the shared <c>Chicken.controller</c>, which an
    /// <see cref="UnityEngine.AnimatorOverrideController"/> has filled with that class's own clip.
    /// </summary>
    /// <remarks>
    /// <b>Why an enum and not a per-ability <c>AnimationClip</c> reference.</b> The field this
    /// replaces (<c>AbilityAnimationClip</c>) was declared in Phase 6 and never read, and it could
    /// not have been: an <see cref="UnityEngine.AnimationClip"/> binds by bone <i>path</i>
    /// (<c>warrior_chicken_rig/Root/Hips/…</c>), so a single clip hung off a shared ability asset
    /// resolves against exactly one of the four class rigs and silently binds to nothing on the
    /// other three — a frozen bind-pose chicken with no error. Ability assets are shared across
    /// classes, so the reference has to be indirect. An archetype ordinal is that indirection: the
    /// ability names a <i>kind of motion</i>, and each class supplies its own clip for it.
    ///
    /// <b>Backing type is byte</b> to match the project's convention for enums that reach a
    /// serialised asset (see <c>ChickenClass</c>), and values are explicit and append-only —
    /// they are persisted by ordinal in 29 <c>.asset</c> files, so reordering them silently
    /// re-animates abilities rather than failing.
    ///
    /// <b>Peck is deliberately absent.</b> It already owns a dedicated animator state and a
    /// dedicated trigger, because foraging is frequent enough that sharing a combat beat reads as
    /// a bug. It is not an archetype and never routes through this enum.
    /// </remarks>
    public enum CastArchetype : byte
    {
        /// <summary>
        /// Body drives forward along the facing axis, beak leading, wings swept back.
        /// Reads as "I am committing at you / going there". Gap-closers and forward shoves.
        /// </summary>
        Lunge = 0,

        /// <summary>
        /// Rears up, then drives body and both wings down into the ground.
        /// Reads as "the ground just got hit". Stomps and ground-directed bursts.
        /// </summary>
        Slam = 1,

        /// <summary>
        /// Compresses, then bursts outward — chest out, wings thrown wide, head back.
        /// Reads as "something just came off me in every direction". Auras and radial bursts.
        /// </summary>
        Flare = 2,

        /// <summary>
        /// Neck darts out, beak closes, then yanks back sharply.
        /// Reads as "I took something off you". Steals and single-target marks.
        /// </summary>
        Grab = 3,

        /// <summary>
        /// One wing cocks back and whips across while the body counter-leans.
        /// Reads as "I put something out there". Placed zones and spawned decoys.
        /// </summary>
        Throw = 4,

        /// <summary>
        /// Drops into a braced stance — body low, wings clamped, head pulled into the shoulders.
        /// Reads as "you cannot move me". Damage soaks and immunities.
        /// </summary>
        Hunker = 5,

        /// <summary>
        /// Dips, then puffs up and shakes out — wings flick open and settle, head high.
        /// Reads as "something changed about me". Self-buffs.
        /// </summary>
        Puff = 6,

        /// <summary>
        /// Sharp lateral weight break — body banks hard, wings counter-balance, head counter-leans.
        /// Reads as "I just went sideways". Evades and blinks.
        /// </summary>
        Dodge = 7,

        /// <summary>
        /// Declares that this ability has no cast motion because it is never cast.
        /// </summary>
        /// <remarks>
        /// <b>For the eight specializations.</b> <see cref="PassiveAbilitySO"/> is an
        /// <see cref="AbilityBaseSO"/>, so it inherits the archetype field, but a passive is
        /// permanent and "expresses itself through hooks, never through OnActivate" — there is no
        /// activation moment to animate.
        ///
        /// It exists rather than letting those eight assets sit on the zero default, which is
        /// <see cref="Lunge"/>. They did, silently, for exactly as long as it took to read the
        /// values back: nothing is wrong with a passive that claims to lunge until the day someone
        /// makes a specialization activatable, at which point it lunges and no test notices. This
        /// project has already shipped that bug once under the name Quick Drop. A value that means
        /// "unset" is better than a value that means something plausible and wrong.
        ///
        /// Reaching <c>ChickenAnimator.TriggerAbilityCast</c> with this is a wiring bug, not a
        /// legal state, and is warned about rather than silently ignored.
        /// </remarks>
        None = 8,
    }
}
