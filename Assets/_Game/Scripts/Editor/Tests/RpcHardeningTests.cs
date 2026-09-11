using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Locks on the receiver-side hardening of the two RPCs that could rewrite the match from
    /// any peer, and on the rollback-safety of the state <c>ChickenMovement</c> integrates.
    /// </summary>
    /// <remarks>
    /// These assert on shape rather than behaviour on purpose: the branches themselves need a
    /// live <c>NetworkRunner</c> and four peers, which EditMode has none of. What EditMode
    /// <em>can</em> guarantee is that the validation hook is still wired in — an
    /// <c>RpcInfo</c> parameter quietly dropped in a refactor takes the whole check with it and
    /// leaves code that still compiles, still runs, and trusts every caller again.
    /// </remarks>
    public sealed class RpcHardeningTests
    {
        private const string ChickenControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const string ChickenMovementPath   = "Assets/_Game/Scripts/Gameplay/ChickenMovement.cs";

        private static MethodInfo PublicMethod(System.Type type, string name)
        {
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .FirstOrDefault(m => m.Name == name);
            Assert.IsNotNull(method, $"{type.Name}.{name} was renamed or removed.");
            return method;
        }

        private static void AssertTakesRpcInfo(System.Type type, string method, string why)
        {
            bool takesInfo = PublicMethod(type, method)
                .GetParameters()
                .Any(p => p.ParameterType == typeof(Fusion.RpcInfo));

            Assert.IsTrue(takesInfo,
                $"{type.Name}.{method} no longer takes an RpcInfo parameter, so it cannot tell who " +
                $"called it. {why}");
        }

        [Test]
        public void RpcAddFood_StillReceivesTheSenderIdentity()
        {
            AssertTakesRpcInfo(typeof(PlayerBase), nameof(PlayerBase.RPC_AddFood),
                "It is RpcSources.All: without the sender, any connected client can bank food into " +
                "any base and win instantly.");
        }

        [Test]
        public void RpcDrainStolen_StillReceivesTheSenderIdentity()
        {
            AssertTakesRpcInfo(typeof(ChickenCargo), nameof(ChickenCargo.RPC_DrainStolen),
                "It is RpcSources.All: without the sender, any connected client can drain any " +
                "chicken's cargo from anywhere on the map.");
        }

        [Test]
        public void RpcDrainStolen_StillNamesItsThief()
        {
            // RpcInfo alone is not enough here, and this is the difference from RPC_AddFood.
            // info.Source is a PlayerRef, and every bot shares PlayerRef.None, so a sender-only
            // rule cannot tell one bot thief from another. The NetworkBehaviourId is what makes
            // the proximity and self-steal checks answerable at all.
            bool namesThief = PublicMethod(typeof(ChickenCargo), nameof(ChickenCargo.RPC_DrainStolen))
                .GetParameters()
                .Any(p => p.ParameterType == typeof(Fusion.NetworkBehaviourId));

            Assert.IsTrue(namesThief,
                "ChickenCargo.RPC_DrainStolen no longer takes a NetworkBehaviourId, so the victim's " +
                "authority cannot resolve WHICH chicken is claiming the steal. Bots all share " +
                "PlayerRef.None, so the sender alone cannot identify a thief and both the " +
                "self-steal and proximity checks lose their subject.");
        }

        [Test]
        public void RpcTeleportTo_StillReceivesTheSenderIdentity()
        {
            AssertTakesRpcInfo(typeof(ChickenController), nameof(ChickenController.RPC_TeleportTo),
                "It is RpcSources.All: without the sender, any peer can teleport any chicken anywhere.");
        }

        [Test]
        public void MatchEndBonus_DoesNotGoThroughTheClientFacingRpc()
        {
            // GameManager runs on the state authority and banks a bonus into a base the chicken
            // may be nowhere near, so it has no proximity to prove. Routing it back through
            // RPC_AddFood would mean either losing the bonus or weakening the validation.
            const string gameManagerPath = "Assets/_Game/Scripts/Gameplay/GameManager.cs";
            Assert.IsTrue(File.Exists(gameManagerPath), $"{gameManagerPath} not found on disk.");

            string source = File.ReadAllText(gameManagerPath);
            Assert.IsFalse(source.Contains("RPC_AddFood("),
                "GameManager calls RPC_AddFood again. Match-end bonuses must use " +
                "PlayerBase.AddFoodAuthoritative — the RPC now requires a depositor standing at " +
                "the base, which an authority-side award can never produce.");
            Assert.IsTrue(source.Contains("AddFoodAuthoritative("),
                "GameManager no longer banks match-end bonuses through PlayerBase.AddFoodAuthoritative.");
        }

        [Test]
        public void VerticalVelocity_IsNetworkedOnTheController()
        {
            // Source-scanned rather than reflected: Fusion's ILWeaver rewrites [Networked]
            // property bodies, so the declaration in source is the honest thing to assert on.
            Assert.IsTrue(File.Exists(ChickenControllerPath), $"{ChickenControllerPath} not found on disk.");

            string source = File.ReadAllText(ChickenControllerPath);
            Assert.IsTrue(source.Contains("[Networked] public float VerticalVelocity"),
                "ChickenController.VerticalVelocity is no longer a [Networked] property. As plain " +
                "state it sits outside Fusion's predicted state, so every resimulated tick " +
                "re-accumulates gravity on a value rollback never restored and the chicken sinks.");
        }

        [Test]
        public void ChickenMovement_KeepsNoPerTickStateOfItsOwn()
        {
            // The class is pure C# and cannot carry [Networked]. Any accumulator that survives
            // between ticks therefore has to live on the owner, or it drifts under resimulation.
            var fields = typeof(ChickenMovement)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly)
                .Select(f => f.Name)
                .ToList();

            Assert.IsEmpty(fields,
                "ChickenMovement gained mutable instance field(s): " + string.Join(", ", fields) +
                ". It is a pure C# class ticked from FixedUpdateNetwork, so anything it carries " +
                "across ticks is invisible to rollback. Put the state on ChickenController — " +
                "[Networked] if it is simulation state — and read it through _owner.");
        }

        [Test]
        public void ChickenMovement_ReadsVerticalVelocityFromTheOwner()
        {
            Assert.IsTrue(File.Exists(ChickenMovementPath), $"{ChickenMovementPath} not found on disk.");

            string source = File.ReadAllText(ChickenMovementPath);
            Assert.IsTrue(source.Contains("_owner.VerticalVelocity"),
                "ChickenMovement no longer integrates gravity through ChickenController.VerticalVelocity.");
        }
    }
}
