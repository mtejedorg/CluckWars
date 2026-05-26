using Fusion;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Per-chicken match statistics: kills credited and food deposited this round.
    /// Both values are <c>[Networked]</c> so the match-end overlay reads the same
    /// data on every peer. RPCs always target <c>StateAuthority</c> so only the
    /// chicken's owning peer mutates its own stats.
    /// </summary>
    /// <remarks>
    /// Maestro: add this component to the Chicken prefab alongside
    /// <see cref="ChickenController"/>. No Inspector wiring needed — it self-resets
    /// via <see cref="RPC_ResetStats"/> at the start of each round.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenMatchStats : NetworkBehaviour
    {
        public static readonly System.Collections.Generic.List<ChickenMatchStats> ActiveStats = new System.Collections.Generic.List<ChickenMatchStats>();

        /// <summary>Number of kills this chicken scored this round.</summary>
        [Networked] public int Kills { get; set; }

        /// <summary>Total food this chicken successfully deposited at its base this round.</summary>
        [Networked] public float FoodDeposited { get; set; }

        public override void Spawned()
        {
            ActiveStats.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveStats.Remove(this);
        }

        /// <summary>
        /// Credit one kill. Called by <see cref="ChickenCombat.CreditKillToAttacker"/>
        /// running on the victim's StateAuthority; any peer can initiate the call and
        /// Fusion routes it to this chicken's authority to mutate the value.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_CreditKill() { Kills++; }

        /// <summary>
        /// Record deposited food. Called by <see cref="ChickenCargo"/> on the
        /// depositing chicken's StateAuthority each time food is dropped at a base.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AddDeposit(float amount) { FoodDeposited += amount; }

        /// <summary>
        /// Reset stats for a new round. Called by <see cref="GameManager.RestartMatch"/>.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetStats()
        {
            Kills         = 0;
            FoodDeposited = 0f;
        }
    }
}
