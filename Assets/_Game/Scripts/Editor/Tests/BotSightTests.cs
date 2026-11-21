using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro 2026-11-22: Hide must mean something to a bot. A hidden rival is remembered where it was last seen, the
    /// bot acts on that spot, and drops it once it has searched there. Pins the pure rule (<see cref="BotSightRules"/>)
    /// and that <c>BotController</c> reads a rival's position only through it.
    /// </summary>
    public class BotSightTests
    {
        private const float Arrival = 1.5f;      // any radius: the rule takes it as a parameter
        private const float Visible = 1f;
        private static readonly Vector3 Far = new Vector3(40f, 0f, 40f);

        private static float ShippedHideOpacity()
        {
            var so = AssetDatabase.LoadAssetAtPath<InvisibilityAbilitySO>("Assets/_Game/Data/Abilities/Invisibility.asset");
            Assert.That(so, Is.Not.Null);
            return so.Opacity;
        }

        private static RivalSighting Look(RivalSighting? prev, Vector3 rivalAt, float opacity, float now, Vector3? self = null) =>
            BotSightRules.Observe(prev, rivalAt, opacity, self ?? Far, now, Arrival);

        [Test]
        public void VisibleRival_IsReadLive()
        {
            var a = Look(null, new Vector3(1, 0, 2), Visible, 0f);
            var b = Look(a, new Vector3(5, 0, 6), Visible, 0.3f);
            Assert.That(b.Position, Is.EqualTo(new Vector3(5, 0, 6)));
            Assert.That(b.Hidden, Is.False);
            Assert.That(b.GivenUp, Is.False);
        }

        [Test]
        public void TheShippedHideOpacity_CountsAsHiddenAndTheUnsetDefaultDoesNot()
        {
            Assert.That(BotSightRules.IsHidden(ShippedHideOpacity()), Is.True);
            Assert.That(BotSightRules.IsHidden(0f), Is.False, "0 is the [Networked] unset default, not invisible");
            Assert.That(BotSightRules.IsHidden(1f), Is.False);
            for (float op = 0f; op <= 1.0001f; op += 0.01f)
                Assert.That(BotSightRules.IsHidden(op), Is.EqualTo(ControlStateVFX.IsFadedOpacity(op)), $"opacity {op}");
        }

        [Test]
        public void HiddenRival_IsRememberedAtItsLastSeenPosition_WhileItMovesAway()
        {
            float hide = ShippedHideOpacity();
            var seen = new Vector3(10, 0, 10);
            var s = Look(null, seen, Visible, 0f);
            s = Look(s, new Vector3(30, 0, 30), hide, 0.3f);
            s = Look(s, new Vector3(-20, 0, 5), hide, 0.6f);
            Assert.That(s.Hidden, Is.True);
            Assert.That(s.GivenUp, Is.False);
            Assert.That(s.Position, Is.EqualTo(seen), "the bot acts on the spot it last saw the rival");
        }

        [Test]
        public void ReappearingUpdatesTheMemoryAtOnce()
        {
            float hide = ShippedHideOpacity();
            var s = Look(null, new Vector3(10, 0, 10), Visible, 0f);
            s = Look(s, new Vector3(30, 0, 30), hide, 0.3f);
            var back = new Vector3(-7, 0, 3);
            s = Look(s, back, Visible, 0.6f);
            Assert.That(s.Position, Is.EqualTo(back));
            Assert.That(s.Hidden, Is.False);
            Assert.That(s.GivenUp, Is.False);
        }

        [Test]
        public void ReappearingAfterGivingUp_PicksTheRivalUpAgain()
        {
            float hide = ShippedHideOpacity();
            var spot = new Vector3(10, 0, 10);
            var s = Look(null, spot, Visible, 0f);
            s = Look(s, spot, hide, 0.3f, self: spot);
            Assert.That(s.GivenUp, Is.True);
            s = Look(s, new Vector3(2, 0, 2), Visible, 0.6f);
            Assert.That(s.GivenUp, Is.False);
            Assert.That(s.Position, Is.EqualTo(new Vector3(2, 0, 2)));
        }

        [Test]
        public void ArrivingAtAnEmptySpot_DropsTheTarget_AndStaysDropped()
        {
            float hide = ShippedHideOpacity();
            var spot = new Vector3(10, 0, 10);
            var s = Look(null, spot, Visible, 0f);
            s = Look(s, new Vector3(30, 0, 30), hide, 0.3f, self: Far);
            Assert.That(s.GivenUp, Is.False, "still on its way");
            s = Look(s, new Vector3(30, 0, 30), hide, 0.6f, self: spot + new Vector3(Arrival * 0.5f, 0, 0));
            Assert.That(s.GivenUp, Is.True, "arrived and nothing there");
            s = Look(s, new Vector3(30, 0, 30), hide, 0.9f, self: Far);
            Assert.That(s.GivenUp, Is.True, "walking away does not bring the target back");
        }

        [Test]
        public void ANeverReachedSpot_IsDroppedAfterTheTimeout()
        {
            float hide = ShippedHideOpacity();
            var spot = new Vector3(10, 0, 10);
            var s = Look(null, spot, Visible, 0f);
            s = Look(s, Far, hide, 1f);                                              // noticed hidden at t=1
            s = Look(s, Far, hide, 1f + BotSightRules.GiveUpSeconds - 0.05f);
            Assert.That(s.GivenUp, Is.False);
            s = Look(s, Far, hide, 1f + BotSightRules.GiveUpSeconds);
            Assert.That(s.GivenUp, Is.True);
        }

        [Test]
        public void ARivalHiddenBeforeTheFirstLook_IsUnknown_NotLocated()
        {
            var s = Look(null, new Vector3(3, 0, 3), ShippedHideOpacity(), 0f);
            Assert.That(s.GivenUp, Is.True, "the bot never saw it, so it has nothing to act on");
            Assert.That(s.Position, Is.Not.EqualTo(new Vector3(3, 0, 3)), "the real position must not leak");
        }

        [Test]
        public void ArrivalIsPlanar()
        {
            float hide = ShippedHideOpacity();
            var spot = new Vector3(10, 0, 10);
            var s = Look(null, spot, Visible, 0f);
            s = Look(s, Far, hide, 0.3f, self: spot + new Vector3(0, 25f, 0));
            Assert.That(s.GivenUp, Is.True);
        }

        // ---- BotController reads rival positions only through the sight rule ---------------------------------------------

        [Test]
        public void BotController_ReadsARivalPositionOnlyThroughTheSightRule()
        {
            string src = File.ReadAllText("Assets/_Game/Scripts/Gameplay/BotController.cs");
            string code = string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///")));

            var reads = Regex.Matches(code, @"\b(?:c|target|rival|mark|_nearestRival|_preyTarget)\.transform\.position")
                .Cast<Match>().ToList();
            Assert.That(reads.Count, Is.EqualTo(1), "exactly one raw read of a rival position is allowed: the call that feeds BotSightRules");
            int lineStart = code.LastIndexOf('\n', reads[0].Index) + 1;
            int lineEnd = code.IndexOf('\n', reads[0].Index);
            StringAssert.Contains("SIGHT-SOURCE", code.Substring(lineStart, lineEnd - lineStart));
            StringAssert.Contains("BotSightRules.Observe", code.Substring(Mathf.Max(0, lineStart - 120), 200));
        }
    }
}
