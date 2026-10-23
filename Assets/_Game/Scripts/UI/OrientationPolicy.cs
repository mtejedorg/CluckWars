using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>The four <c>Screen.autorotateTo*</c> switches as one value (read, compared, written back).</summary>
    public readonly struct AutorotateFlags
    {
        public readonly bool LandscapeLeft, LandscapeRight, Portrait, PortraitUpsideDown;

        public AutorotateFlags(bool landscapeLeft, bool landscapeRight, bool portrait, bool portraitUpsideDown)
        {
            LandscapeLeft = landscapeLeft; LandscapeRight = landscapeRight;
            Portrait = portrait; PortraitUpsideDown = portraitUpsideDown;
        }

        public static AutorotateFlags Read() => new AutorotateFlags(
            Screen.autorotateToLandscapeLeft, Screen.autorotateToLandscapeRight,
            Screen.autorotateToPortrait, Screen.autorotateToPortraitUpsideDown);

        public void Write()
        {
            Screen.autorotateToLandscapeLeft = LandscapeLeft;
            Screen.autorotateToLandscapeRight = LandscapeRight;
            Screen.autorotateToPortrait = Portrait;
            Screen.autorotateToPortraitUpsideDown = PortraitUpsideDown;
        }

        public override string ToString() =>
            $"LL={(LandscapeLeft ? 1 : 0)} LR={(LandscapeRight ? 1 : 0)} P={(Portrait ? 1 : 0)} PUD={(PortraitUpsideDown ? 1 : 0)}";
    }

    /// <summary>
    /// Pure rules of the round orientation lock (DECISIONS_2026-10-21 C4/C5): free rotation in the menus and the
    /// waiting room, locked to the orientation the player is holding from GET READY through the results.
    /// </summary>
    public static class OrientationLockRules
    {
        /// <summary>
        /// True from GET READY (<see cref="MatchState.Starting"/>) through the round and its results
        /// (<see cref="MatchState.Ended"/>). The waiting room (<see cref="MatchState.WaitingForPlayers"/>) and "no match
        /// object" (menus, before the master spawns it, between sessions) rotate freely.
        /// </summary>
        public static bool ShouldLock(bool hasMatch, MatchState state) =>
            hasMatch && (state == MatchState.Starting || state == MatchState.Active || state == MatchState.Ended);

        /// <summary>
        /// The orientation to hold: the one the platform reports when it is a concrete orientation that agrees with
        /// the screen's shape, else the shape decides (wide = landscape left, tall = portrait). The shape wins a
        /// disagreement because it is what the player is actually looking at.
        /// </summary>
        public static ScreenOrientation LockTarget(ScreenOrientation reported, int width, int height)
        {
            bool tall = height > width;
            switch (reported)
            {
                case ScreenOrientation.LandscapeLeft:
                case ScreenOrientation.LandscapeRight:
                    if (!tall) return reported;
                    break;
                case ScreenOrientation.Portrait:
                case ScreenOrientation.PortraitUpsideDown:
                    if (tall) return reported;
                    break;
            }
            return tall ? ScreenOrientation.Portrait : ScreenOrientation.LandscapeLeft;
        }

        /// <summary>Autorotation allowed to <paramref name="orientation"/> only.</summary>
        public static AutorotateFlags OnlyAllow(ScreenOrientation orientation) => new AutorotateFlags(
            orientation == ScreenOrientation.LandscapeLeft,
            orientation == ScreenOrientation.LandscapeRight,
            orientation == ScreenOrientation.Portrait,
            orientation == ScreenOrientation.PortraitUpsideDown);
    }

    /// <summary>
    /// The lock's state machine, pure: what to write to <c>Screen.autorotateTo*</c> when the wanted lock changes. Locking
    /// remembers the switches it found and allows only the held orientation; unlocking gives back exactly what was found.
    /// A null result means "write nothing".
    /// </summary>
    public sealed class OrientationLockState
    {
        public bool Locked { get; private set; }
        public AutorotateFlags Restore { get; private set; }
        public ScreenOrientation LockedTo { get; private set; }

        public AutorotateFlags? Update(bool wanted, AutorotateFlags current, ScreenOrientation reported, int width, int height)
        {
            if (wanted == Locked) return null;
            if (!wanted) return Release();
            Restore = current;
            LockedTo = OrientationLockRules.LockTarget(reported, width, height);
            Locked = true;
            return OrientationLockRules.OnlyAllow(LockedTo);
        }

        /// <summary>Ends a lock (round over, or the component going away); null when nothing is locked.</summary>
        public AutorotateFlags? Release()
        {
            if (!Locked) return null;
            Locked = false;
            return Restore;
        }
    }

    /// <summary>
    /// Applies <see cref="OrientationLockRules"/> on this peer from the replicated <see cref="GameManager.State"/>
    /// (read only; every peer locks its own screen, nothing is networked). Locks by narrowing
    /// <c>Screen.autorotateTo*</c> to the held orientation and restores the switches it found when the lock ends or
    /// this component goes away (leaving the Game scene). Added by <see cref="MatchHudController"/>, so it exists
    /// exactly while a match HUD does. On desktop the switches have no effect; the transitions are still logged.
    /// </summary>
    public sealed class OrientationPolicy : MonoBehaviour
    {
        private const string Source = "OrientationPolicy";

        private ILogService _log;
        private readonly OrientationLockState _lock = new OrientationLockState();

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            bool hasMatch = gm != null && gm.Object != null && gm.Object.IsValid;
            var state = hasMatch ? gm.State : MatchState.WaitingForPlayers;
            bool wanted = OrientationLockRules.ShouldLock(hasMatch, state);
            if (wanted == _lock.Locked) return;

            var write = _lock.Update(wanted, AutorotateFlags.Read(), Screen.orientation, Screen.width, Screen.height);
            write?.Write();
            if (_lock.Locked)
                _log?.Info(Source, $"Orientation locked to {_lock.LockedTo} ({state}, {Screen.width}x{Screen.height}); restores {_lock.Restore}.");
            else
                _log?.Info(Source, $"Orientation unlocked ({(hasMatch ? state.ToString() : "no match")}): {_lock.Restore}.");
        }

        private void OnDisable()
        {
            var write = _lock.Release();
            if (write == null) return;
            write.Value.Write();
            _log?.Info(Source, $"Orientation unlocked (left the match): {_lock.Restore}.");
        }
    }
}
