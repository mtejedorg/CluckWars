using NUnit.Framework;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class ControlStateTests
    {
        [Test]
        public void Free_AllowsEverything()
        {
            Assert.IsTrue(ControlRules.CanMove(ControlState.Free));
            Assert.IsTrue(ControlRules.CanCast(ControlState.Free));
            Assert.IsTrue(ControlRules.CanCollect(ControlState.Free));
        }

        [Test]
        public void Slowed_AllowsCastAndCollect()
        {
            Assert.IsTrue(ControlRules.CanMove(ControlState.Slowed));   // move, just reduced
            Assert.IsTrue(ControlRules.CanCast(ControlState.Slowed));
            Assert.IsTrue(ControlRules.CanCollect(ControlState.Slowed));
        }

        [Test]
        public void Rooted_BlocksMoveAndCollect_AllowsCast()
        {
            Assert.IsFalse(ControlRules.CanMove(ControlState.Rooted));
            Assert.IsTrue(ControlRules.CanCast(ControlState.Rooted));
            Assert.IsFalse(ControlRules.CanCollect(ControlState.Rooted));
        }

        [Test]
        public void Stunned_BlocksEverything()
        {
            Assert.IsFalse(ControlRules.CanMove(ControlState.Stunned));
            Assert.IsFalse(ControlRules.CanCast(ControlState.Stunned));   // the ONLY state that locks casting
            Assert.IsFalse(ControlRules.CanCollect(ControlState.Stunned));
        }
    }
}
