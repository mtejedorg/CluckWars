using System;
using System.Text;

namespace CluckWars.Gameplay
{
    /// <summary>The independent local conditions that make the arena fully ready to be shown to the player.</summary>
    public enum ArenaGate
    {
        /// <summary>The scene's content is in: the manager, the local chicken, every chicken's model, all bases, all piles.</summary>
        Scene = 0,
        /// <summary>The render tier is applied and the tier-dependent swaps (fence twins, surround haze) have run once.</summary>
        Tier = 1,
        /// <summary>The overview warm-up behind the cover ran (<see cref="ArenaWarmupSequence"/>).</summary>
        Warmup = 2,
        /// <summary>The camera sits on the local chicken at its final yaw with no SmoothDamp travel left.</summary>
        Camera = 3,
    }

    /// <summary>
    /// "Is this peer's arena fully ready to be revealed?" (presentation only, nothing networked). Read by
    /// <c>GameManager</c> (this peer does not report "settled" before it) and by the arena cover
    /// (<c>ArenaCoverRule</c>). Bound in <c>GameInstaller</c>; <see cref="NullArenaReadiness"/> is the always-ready stand-in.
    /// </summary>
    public interface IArenaReadiness
    {
        /// <summary>True once every gate is done, or the cap (<see cref="ArenaReadinessTracker.MaxSeconds"/>) passed.
        /// Briefly false again while a late warm-up re-run is in flight.</summary>
        bool IsReady { get; }

        /// <summary>True when <see cref="IsReady"/> came from the cap with gates still pending.</summary>
        bool TimedOut { get; }

        /// <summary>Seconds since the scene started at which the arena became ready.</summary>
        float ReadyAtSeconds { get; }

        /// <summary>Name of the gate that completed last (empty before any).</summary>
        string LastGate { get; }

        /// <summary>Comma-separated names of the gates still pending (empty when none).</summary>
        string PendingGates { get; }
    }

    /// <summary>Always ready: for scenes / tests with no arena to warm (and the fallback when the binding is missing).</summary>
    public sealed class NullArenaReadiness : IArenaReadiness
    {
        public static readonly NullArenaReadiness Instance = new NullArenaReadiness();
        public bool IsReady => true;
        public bool TimedOut => false;
        public float ReadyAtSeconds => 0f;
        public string LastGate => string.Empty;
        public string PendingGates => string.Empty;
    }

    /// <summary>
    /// The latching gate bookkeeping behind <see cref="IArenaReadiness"/>: four named gates, each recorded with the time (seconds
    /// since the scene started) it first held and never un-done after that; <see cref="IsReady"/> when all four are done, or
    /// <see cref="MaxSeconds"/> after the scene started (then <see cref="TimedOut"/> and <see cref="PendingGates"/> say what
    /// was missing). Pure: the driver feeds it the gate inputs once per rendered frame.
    /// </summary>
    public sealed class ArenaReadinessTracker
    {
        /// <summary>Hard cap since scene start. MUST stay below <c>IntroArmGate.MaxWaitSeconds</c> minus
        /// <c>SettleTracker.SettleSeconds</c> (pinned by a test): the state authority's own cap must never arm the intro
        /// while its own cover is still up.</summary>
        public const float MaxSeconds = 15f;

        public const int GateCount = 4;

        private readonly float[] _doneAt = { -1f, -1f, -1f, -1f };
        private int _doneCount;
        private int _lastGate = -1;

        public bool IsReady { get; private set; }
        public bool TimedOut { get; private set; }
        public float ReadyAtSeconds { get; private set; }

        /// <summary>Name of the gate that completed last (empty before any).</summary>
        public string LastGate => _lastGate < 0 ? string.Empty : ((ArenaGate)_lastGate).ToString();

        /// <summary>Raised once per gate when it first holds: (gate, seconds since scene start).</summary>
        public event Action<ArenaGate, float> GateCompleted;

        public bool IsDone(ArenaGate gate) => _doneAt[(int)gate] >= 0f;

        /// <summary>Seconds since scene start at which <paramref name="gate"/> completed, or -1 while pending.</summary>
        public float DoneAt(ArenaGate gate) => _doneAt[(int)gate];

        /// <summary>Comma-separated names of the gates still pending (empty when none).</summary>
        public string PendingGates
        {
            get
            {
                var sb = new StringBuilder();
                for (int g = 0; g < GateCount; g++)
                {
                    if (_doneAt[g] >= 0f) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append((ArenaGate)g);
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Feeds one rendered frame. <paramref name="sceneSeconds"/> is the time since the scene started; the four flags are
        /// whether each gate's condition holds right now. Returns <see cref="IsReady"/>. A latched tracker ignores later calls.
        /// </summary>
        public bool Tick(float sceneSeconds, bool scene, bool tier, bool warmup, bool camera)
        {
            if (IsReady) return true;
            Latch(ArenaGate.Scene, scene, sceneSeconds);
            Latch(ArenaGate.Tier, tier, sceneSeconds);
            Latch(ArenaGate.Warmup, warmup, sceneSeconds);
            Latch(ArenaGate.Camera, camera, sceneSeconds);

            if (_doneCount == GateCount)
            {
                ReadyAtSeconds = sceneSeconds;
                return IsReady = true;
            }
            if (sceneSeconds >= MaxSeconds)
            {
                TimedOut = true;
                ReadyAtSeconds = sceneSeconds;
                return IsReady = true;
            }
            return false;
        }

        private void Latch(ArenaGate gate, bool holds, float sceneSeconds)
        {
            int g = (int)gate;
            if (!holds || _doneAt[g] >= 0f) return;
            _doneAt[g] = sceneSeconds;
            _doneCount++;
            _lastGate = g;
            GateCompleted?.Invoke(gate, sceneSeconds);
        }
    }
}
