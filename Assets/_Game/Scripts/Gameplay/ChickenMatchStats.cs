using CluckWars.Logging;
using CluckWars.Progression;
using Fusion;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

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
        private const string Source = "MatchStats";

        public static readonly System.Collections.Generic.List<ChickenMatchStats> ActiveStats = new System.Collections.Generic.List<ChickenMatchStats>();

        /// <summary>Number of kills this chicken scored this round.</summary>
        [Networked] public int Kills { get; set; }

        /// <summary>Total food this chicken successfully deposited at its base this round.</summary>
        [Networked] public float FoodDeposited { get; set; }

        private IMatchEventSink _matchEvents;
        private ILogService _log;

        [Inject]
        public void Construct(IMatchEventSink matchEvents, ILogService log)
        {
            _matchEvents = matchEvents;
            _log = log;
        }

        public override void Spawned()
        {
            // Both dependencies are project-bound, so ProjectContext alone resolves them.
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            if (_matchEvents == null)
            {
                _log?.Error(Source, $"{name}: IMatchEventSink was not injected, so this chicken's kills will not be " +
                    "announced to progression. Check the IMatchEventSink binding in ProjectInstaller.");
            }

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
        public void RPC_CreditKill()
        {
            Kills++;

            // Runs on the attacker's state authority, so the actor is this chicken. No
            // Runner.IsForward guard, unlike the tick-driven emit sites: Fusion executes an RPC
            // once, on delivery, and never re-runs it in a resimulation
            // (RpcLocalInvokeResult.NotInvokableDuringResim).
            _matchEvents?.OpponentDisabled(MatchActorId.Of(Object));
        }

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
