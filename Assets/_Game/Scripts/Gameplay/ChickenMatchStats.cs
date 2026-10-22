using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Per-chicken match statistics: kills credited, food deposited and stuns taken this round.
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

        /// <summary>
        /// Stuns this chicken received this round that started or extended its stun timer (a
        /// shorter stun swallowed by a longer running one is not counted). Written on this
        /// chicken's state authority by <c>ChickenController.RPC_ApplyStun</c>; read by the
        /// host's MATCH SUMMARY (decision F7, 2026-10-22). <b>Protocol change:</b> one more
        /// networked int on this behaviour, so all peers must run the same build.
        /// </summary>
        [Networked] public int StunsTaken { get; set; }

        // Executes this chicken pressed whose kill report has not arrived yet. Local to this
        // chicken's state authority, never replicated; gates RPC_CreditKill (CW-230). Lost on a host
        // migration (a master-owned bot moving authority mid-execute), so that one kill is refused.
        private readonly PendingStealLedger<NetworkBehaviourId> _pendingKills =
            ExecuteRules.NewKillCreditLedger<NetworkBehaviourId>();

        // One rejection line per sender per 2 s, shared by every chicken.
        private static readonly CluckWars.Diagnostics.PerKeyRateLimiter RejectionLimiter =
            new CluckWars.Diagnostics.PerKeyRateLimiter(2f);

        private ILogService _log;

        [Inject]
        public void Construct(ILogService log)
        {
            _log = log;
        }

        public override void Spawned()
        {
            ActiveStats.Add(this);
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveStats.Remove(this);
        }

        /// <summary>
        /// Assassin side of an execute, called by <c>AssassinExecute.Press</c> on this chicken's
        /// state authority BEFORE it sends <c>RPC_ExecuteRemoval</c>: one kill report from
        /// <paramref name="victim"/> is now expected. Recorded first because on a shared authority
        /// (solo, bots) the removal and the report back run synchronously inside that call.
        /// </summary>
        public void ExpectKillCredit(ChickenController victim)
        {
            if (!HasStateAuthority || victim == null)
            {
                // Press is authority-gated and validates the victim first, so this is a wiring bug.
                _log?.Error(Source, $"{name}: ExpectKillCredit ignored (authority={HasStateAuthority}, " +
                    $"victim={(victim != null ? victim.name : "null")}) - this execute's kill will be refused.");
                return;
            }

            ExecuteRules.ExpectKillCredit(_pendingKills, victim.Id, Time.unscaledTime);
        }

        /// <summary>
        /// Credit one kill. Sent by <see cref="ChickenCombat"/>'s kill credit on the victim's state
        /// authority after an execute removal; Fusion routes it to this chicken's authority.
        /// </summary>
        /// <remarks>
        /// <b>Receiver-validated (CW-230).</b> <c>RpcSources.All</c>, so every report is a claim,
        /// checked like <c>ChickenCargo.RPC_CreditExecuteLoot</c>: the sender must speak for the
        /// named victim (local invoke: victim simulated here; off the wire: the victim's state
        /// authority), and the report must match an execute this chicken recorded in
        /// <see cref="ExpectKillCredit"/>, consumed once, inside
        /// <see cref="ExecuteRules.KillCreditWindowSeconds"/>.
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_CreditKill(NetworkBehaviourId victimId, RpcInfo info = default)
        {
            var claim = new ExecuteRules.KillCreditClaim();
            string victimName = victimId.ToString();
            if (Runner != null && Runner.TryFindBehaviour(victimId, out ChickenController victim) &&
                victim != null && victim.Object != null && victim.Object.IsValid)
            {
                victimName = victim.name;
                claim.VictimResolved = true;
                claim.VictimIsSelf = victim.Object == Object;
                claim.VictimIsDecoy = victim.IsDecoy;
                claim.SenderSpeaksForVictim = info.IsInvokeLocal
                    ? victim.Object.HasStateAuthority
                    : victim.Object.StateAuthority == info.Source;
            }

            var verdict = ExecuteRules.EvaluateKillCredit(_pendingKills, claim, victimId, Time.unscaledTime);
            if (verdict != ExecuteRules.KillCreditVerdict.Accepted)
            {
                if (RejectionLimiter.Allow(info.Source.RawEncoded, Time.realtimeSinceStartup, out int held))
                    _log?.Warn(Source, $"{name}: rejected RPC_CreditKill(victim={victimName}) from {info.Source} " +
                        $"({verdict}) - {ExecuteRules.Describe(verdict)}" +
                        (held > 0 ? $" (+{held} similar suppressed)" : string.Empty));
                return;
            }

            Kills++;
            _log?.Info(Source, $"{name}: kill credited for executing {victimName} (Kills={Kills}).");
        }

        /// <summary>
        /// Record deposited food. Called by <see cref="ChickenCargo"/> on the
        /// depositing chicken's StateAuthority each time food is dropped at a base.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AddDeposit(float amount) { FoodDeposited += amount; }

        /// <summary>
        /// Count one applied stun. Called by <c>ChickenController.RPC_ApplyStun</c>, which already
        /// runs on this chicken's state authority (same NetworkObject), so no RPC is needed.
        /// </summary>
        public void RecordStunTaken()
        {
            if (!HasStateAuthority)
            {
                // RPC_ApplyStun targets the state authority, so reaching here elsewhere is a wiring bug.
                _log?.Error(Source, $"{name}: RecordStunTaken called without state authority - stun not counted.");
                return;
            }
            StunsTaken++;
        }

        /// <summary>
        /// Reset stats for a new round. Called by <see cref="GameManager.ResetWorldForNewRound"/>.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetStats()
        {
            Kills         = 0;
            FoodDeposited = 0f;
            StunsTaken    = 0;
            _pendingKills.Clear();
        }
    }
}
