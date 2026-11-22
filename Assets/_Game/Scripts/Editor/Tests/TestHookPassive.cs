using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>A passive whose hooks the test sets (null leaves a hook at its default). Used only by the match kit tests.</summary>
    internal sealed class TestHookPassive : PassiveAbilitySO
    {
        public Func<float, float> Knockback;
        public Func<float, ControlKind, float> Control;

        public override float ModifyKnockback(float strength, ChickenController self) =>
            Knockback != null ? Knockback(strength) : strength;

        public override float ModifyControlDuration(float seconds, ControlKind kind, ChickenController self) =>
            Control != null ? Control(seconds, kind) : seconds;
    }
}
