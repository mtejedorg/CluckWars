using System.Linq;
using CluckWars.Gameplay;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 chunk D item D7 (audit finding L07, the pennant): the flag renders in its player colour on every seat (an UNLIT cloth lifted
    /// to read like the sunlit ring, never above 1), it never turns edge-on to any seat's match camera, and its pole stands clear of the
    /// 2.35-2.65 m deposit ring. The pole / fence / coop clearances live in <see cref="Phase8ChunkS2Tests"/>; the rendered flag-vs-ring dE00
    /// is measured in the live match camera by <c>tools/ui_capture/capture_d7_pennant.py</c> (an EditMode test cannot render).
    /// </summary>
    /// <remarks>
    /// Facing is judged on the YAW only: the match camera is a fixed 30 degree pitch for every seat, and an upright flag's plane is vertical, so
    /// the pitch adds the same constant to every pair. The camera yaw per seat comes from <see cref="MatchCamera.TryYawForCorner"/> (the rule the
    /// camera itself runs), the base yaw from <see cref="PlayerBase.KitYawFor"/> (what <c>Spawned()</c> applies), the flag plane from the prefab mesh axes.
    /// </remarks>
    public class Phase8ChunkD7PennantTests
    {
        private const string FlagMaterial = "Assets/_Game/Art/Materials/BaseKit_PennantFlag.mat";
        private const string ClothTexture = "Assets/Generated/Env/Textures/pennant_cloth_hem.png";
        private const float PoleRadiusMin = 2.9f;

        /// <summary>
        /// The worst yaw angle between a flag plane and any seat's camera view plane that is acceptable (0 = face-on, 90 = edge-on). Four seats
        /// look along the four diagonals and every flag is rotated by the same quarter turns, so a single static yaw cannot be face-on for everyone:
        /// the flag plane sits on an axis, 45 degrees off every camera, the best possible worst case (the old placement was 0 for two bases, 90 for the other two).
        /// </summary>
        private const float MaxFlagAzimuthDeg = 45.5f;

        private static GameObject BasePrefab() => TestAssets.Load<GameObject>(TestAssets.PlayerBasePrefabPath);
        private static PlayerBase Base() => BasePrefab().GetComponent<PlayerBase>();
        private static Renderer Named(string n) => BasePrefab().GetComponentsInChildren<Renderer>(true).Single(r => r.name == n);

        private static Vector3 BasePosition(int corner) => MapGenerator.SpawnPointNominal(ShippedMap.Corners()[corner]);

        // ---- flag colour -------------------------------------------------------------------------------

        [Test]
        public void FlagMaterial_IsUnlit_WithTheClothTexture_AndAWhiteBase()
        {
            var m = TestAssets.Load<Material>(FlagMaterial);
            Assert.AreEqual("Universal Render Pipeline/Unlit", m.shader.name, "unlit: the sun and the side the camera sees must not decide the colour");
            Assert.AreEqual(TestAssets.Load<Texture2D>(ClothTexture), m.GetTexture("_BaseMap"), "the cloth + hem texture is kept");
            Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), "white base so the property block carries the player colour");
            Assert.IsTrue(m.enableInstancing);
            Assert.AreEqual(0f, m.GetFloat("_Surface"), "opaque");
            Assert.IsFalse(m.IsKeywordEnabled("_EMISSION"), "no emission channel on an unlit cloth");
        }

        [Test]
        public void FlagColour_CanNeverReachBloom_OnAnySeatOrLift()
        {
            // Output = cloth texture (<= 1 per channel) x the property-block colour, and FlagColorFor caps every colour channel at 1, so the PC
            // bloom threshold (1) is never reached however the lift is set.
            for (int corner = 0; corner < PlayerPalette.Count; corner++)
            foreach (var lift in new[] { 0.2f, 1f, PlayerBase.DefaultFlagLift, PlayerBase.MaxFlagLift, 50f })
            {
                var c = PlayerBase.FlagColorFor(PlayerPalette.ForCorner(corner), lift);
                Assert.LessOrEqual(Mathf.Max(c.r, c.g, c.b), 1f, $"corner {corner} lift {lift}: a channel above 1 would reach the PC bloom threshold");
                Assert.AreEqual(1f, c.a);
            }
            var shipped = PlayerBase.FlagColorFor(PlayerPalette.ForCorner(0), PlayerBase.DefaultFlagLift);
            Assert.LessOrEqual(Mathf.Max(shipped.r, shipped.g, shipped.b), 1f);
        }

        [Test]
        public void FlagLift_IsNeverADarkening_AndIsClampedToItsRange()
        {
            var colour = new Color(0.2f, 0.4f, 0.6f);
            Assert.AreEqual(colour.r, PlayerBase.FlagColorFor(colour, 0.1f).r, 1e-6f, "a lift below 1 clamps to 1: the flag is never darker than the player colour");
            Assert.AreEqual(colour.g * PlayerBase.MaxFlagLift, PlayerBase.FlagColorFor(colour, 99f).g, 1e-5f, "a huge lift clamps to MaxFlagLift");
            Assert.AreEqual(colour.b * 1.25f, PlayerBase.FlagColorFor(colour, 1.25f).b, 1e-5f);
        }

        [Test]
        public void PrefabLift_IsTheShippedDefault_InsideItsRange()
        {
            float lift = TestAssets.PrivateField<float>(Base(), "_flagLift");
            Assert.AreEqual(PlayerBase.DefaultFlagLift, lift, 1e-5f, "prefab and constant must agree (the capture tool measured this value)");
            Assert.That(lift, Is.InRange(1f, PlayerBase.MaxFlagLift));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void FlagTakesTheLiftedPlayerColour_RingTheExactOne(int corner)
        {
            var pb = Base();
            var identity = TestAssets.PrivateField<Renderer[]>(pb, "_identityRenderers");
            var flags = TestAssets.PrivateField<Renderer[]>(pb, "_flagRenderers");
            float lift = TestAssets.PrivateField<float>(pb, "_flagLift");
            var colour = PlayerPalette.ForCorner(corner);
            var flag = PlayerBase.FlagColorFor(colour, lift);

            Assert.AreEqual(1, flags.Length);
            Assert.AreEqual("Pennant_Flag", flags[0].name);
            Assert.AreNotEqual(colour, flag, "the lift must do something (palette colours are not all at 1)");
            // Hue stays the player's: lifting scales channels, so the channel order cannot change (a clamp can only flatten the top channel).
            Assert.AreEqual(colour.r >= colour.g, flag.r >= flag.g - 1e-6f, "hue order r/g");
            Assert.AreEqual(colour.g >= colour.b, flag.g >= flag.b - 1e-6f, "hue order g/b");
            CollectionAssert.DoesNotContain(identity, flags[0], "the flag is no longer an identity (exact-colour) renderer");
        }

        // ---- flag facing -------------------------------------------------------------------------------

        /// <summary>World-space (yaw only) plane normal of the prefab flag when its base sits on <paramref name="corner"/>.</summary>
        private static Vector3 FlagNormal(int corner)
        {
            var t = Named("Pennant_Flag").transform;
            var u = t.TransformDirection(Vector3.left);     // mesh -x: out from the pole along the cloth
            var v = t.TransformDirection(Vector3.forward);  // mesh +z: up the cloth (the FBX is imported lying down; the prefab node stands it up)
            var n = Vector3.Cross(u, v).normalized;
            var p = BasePosition(corner);
            n = Quaternion.Euler(0f, PlayerBase.KitYawFor(p), 0f) * n;
            n.y = 0f;
            return n.normalized;
        }

        private static Vector3 CameraForward(int seat)
        {
            Assert.IsTrue(MatchCamera.TryYawForCorner(seat, out float yaw), $"seat {seat}");
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        [Test]
        public void MatchCameraYaw_IsTheKnownDiagonalPerSeat()
        {
            var expected = new[] { 225f, 135f, 45f, 315f };
            for (int s = 0; s < 4; s++)
            {
                Assert.IsTrue(MatchCamera.TryYawForCorner(s, out float yaw));
                Assert.AreEqual(0f, Mathf.DeltaAngle(expected[s], yaw), 1e-3f, $"seat {s}");
            }
            Assert.IsFalse(MatchCamera.TryYawForCorner(4, out _));
            Assert.IsFalse(MatchCamera.TryYawForCorner(-1, out _));
        }

        [Test]
        public void Flag_NeverTurnsEdgeOn_ToAnySeatsCamera_OnAnyBase()
        {
            float worst = 0f; string worstPair = "";
            for (int seat = 0; seat < 4; seat++)
            for (int corner = 0; corner < 4; corner++)
            {
                var n = FlagNormal(corner);
                var f = CameraForward(seat); f.y = 0f; f.Normalize();
                float angle = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(n, -f)))) * Mathf.Rad2Deg; // the cloth is double-sided: the sign does not matter
                if (angle > worst) { worst = angle; worstPair = $"seat {seat} looking at base {corner}"; }
                Assert.LessOrEqual(angle, MaxFlagAzimuthDeg, $"seat {seat} sees base {corner}'s flag {angle:0.0} degrees off face-on (90 = edge-on)");
            }
            Assert.Greater(worst, 0f, worstPair); // 45 with the shipped yaw: documents that the compromise is real, not a lucky zero
        }

        [Test]
        public void FlagMesh_IsDoubleSided_SoTheFarSideIsNeverCulled()
        {
            // Seen from behind (half the seat/base pairs) the cloth must still draw: either the material is two-sided or the mesh carries both faces.
            var mesh = Named("Pennant_Flag").GetComponent<MeshFilter>().sharedMesh;
            var normals = mesh.normals;
            Assert.IsTrue(normals.Any(n => n.y > 0.5f) && normals.Any(n => n.y < -0.5f), "the flag mesh has front and back faces");
            Assert.AreEqual(1, Named("Pennant_Flag").sharedMaterials.Length);
        }

        // ---- pole ---------------------------------------------------------------------------------------

        [Test]
        public void Pole_StandsBeyondTheRing()
        {
            var kit = TestAssets.PrivateField<Transform>(Base(), "_kit");
            var local = kit.InverseTransformPoint(Named("Pennant_Pole").transform.position); // the prefab root sits off the origin; the kit is the pad frame
            Assert.GreaterOrEqual(new Vector2(local.x, local.z).magnitude, PoleRadiusMin, "the pole used to stand at r 2.67 m, on the 2.35-2.65 m deposit ring");
        }
    }
}
