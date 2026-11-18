using System;
using CluckWars.Diagnostics;
using CluckWars.Logging;

namespace CluckWars.Balance
{
    /// <summary>What <see cref="BalanceLocalOverride"/> did with a request.</summary>
    public enum LocalOverrideOutcome : byte
    {
        /// <summary>Validated, stored on this device and in force now.</summary>
        Applied = 0,
        /// <summary>Validated and stored, not in force: a lobby or match started this launch, so it applies at next launch.</summary>
        StoredForNextLaunch = 1,
        /// <summary>Not accepted (invalid delta, or the file could not be written): nothing changed.</summary>
        Refused = 2,
        /// <summary>Dev tools are off: nothing was read, written or applied.</summary>
        NotAllowed = 3,
        /// <summary>The stored override is deleted and the remote / cached / shipped balance is back in force now.</summary>
        Cleared = 4,
        /// <summary>The stored override is deleted; the one in force keeps running until next launch (the apply window is closed).</summary>
        ClearedNextLaunch = 5,
    }

    public sealed class LocalOverrideResult
    {
        public LocalOverrideOutcome Outcome;
        /// <summary>Why it was refused or not allowed; null otherwise.</summary>
        public string Reason;
        /// <summary>The validated delta (applied, stored, or the one that failed validation).</summary>
        public BalanceVerdict Verdict;
    }

    /// <summary>
    /// The dev-tools local balance override: apply a delta on this device only, keep it across launches in
    /// <see cref="BalanceLocalStore"/>, and put the remote / cached balance back with <see cref="Reset"/>. No network, no
    /// credential, no publish: the owner copies the delta and publishes it with <c>/balance publish dev</c> on the PC.
    /// Constructed by <c>ProjectInstaller.InstallBalance</c> only when <see cref="DevTools.Available"/> (and every entry
    /// point checks it again). While a local override is in force the remote delta is cached but not applied
    /// (<see cref="BalanceRefresher"/>).
    /// </summary>
    public sealed class BalanceLocalOverride
    {
        private readonly BalanceBook _book;
        private readonly BalanceLocalStore _store;
        private readonly BalanceCache _cache;
        private readonly ILogService _log;

        /// <param name="cache">The remote delta's cache, or null when the remote source is off.</param>
        public BalanceLocalOverride(BalanceBook book, BalanceLocalStore store, BalanceCache cache, ILogService log)
        {
            _book = book;
            _store = store;
            _cache = cache;
            _log = log;
        }

        /// <summary>Where the override is stored, for the editor to show.</summary>
        public string StorePath => _store.Path;

        /// <summary>True when a local override is in force this launch.</summary>
        public bool InForce => _book.Origin == BalanceOrigin.Local;

        /// <summary>Boot: applies the stored override, if any, on top of whatever the cache applied. Never throws.</summary>
        public void ApplyAtBoot()
        {
            if (!BalanceBook.LocalAllowed(DevTools.Available)) return;
            string json;
            try
            {
                json = _store.Read();
            }
            catch (Exception e)
            {
                _log.Warn(BalanceBook.Source, $"Balance local override {_store.Path} unreadable ({e.GetType().Name}: {e.Message}); ignoring it.");
                return;
            }
            if (json == null) return;

            var verdict = _book.Evaluate(json, "Local file");
            if (!verdict.IsAccepted)
            {
                _log.Warn(BalanceBook.Source, $"Balance local override {_store.Path} not applied ({verdict.Reason}); delete it from the balance editor.");
                return;
            }
            if (verdict.IsEmpty)
            {
                _log.Warn(BalanceBook.Source, $"Balance local override {_store.Path} holds no change; deleting it.");
                try { _store.Clear(); }
                catch (Exception e) { _log.Warn(BalanceBook.Source, $"Balance local override {_store.Path} not deleted ({e.GetType().Name}: {e.Message})."); }
                return;
            }
            if (_book.ApplyLocal(verdict))
                _log.Info(BalanceBook.Source, $"Balance: local override in force (bal={_book.Hash}); the remote delta is cached but not applied this launch.");
        }

        /// <summary>
        /// Validates <paramref name="json"/> (<see cref="BalanceBook.Evaluate"/>), stores it, and applies it now if the apply
        /// window is open, else leaves it for next launch. An empty delta is the same as <see cref="Reset"/>.
        /// </summary>
        public LocalOverrideResult Apply(string json)
        {
            if (!BalanceBook.LocalAllowed(DevTools.Available)) return NotAllowed();
            var verdict = _book.Evaluate(json, "Local");
            if (!verdict.IsAccepted)
                return new LocalOverrideResult { Outcome = LocalOverrideOutcome.Refused, Reason = verdict.Reason, Verdict = verdict };
            if (verdict.IsEmpty) return Reset();

            try
            {
                _store.Write(_book.PublishJsonOf(verdict));
            }
            catch (Exception e)
            {
                _log.Error(BalanceBook.Source, $"Balance local override {_store.Path} not stored; nothing applied.", e);
                return new LocalOverrideResult { Outcome = LocalOverrideOutcome.Refused, Reason = $"{e.GetType().Name}: {e.Message}", Verdict = verdict };
            }

            if (!_book.ApplyWindowOpen)
            {
                _log.Info(BalanceBook.Source, $"Balance local override {verdict.Hash} stored; a lobby or match started this launch, so it applies at next launch.");
                return new LocalOverrideResult { Outcome = LocalOverrideOutcome.StoredForNextLaunch, Verdict = verdict };
            }
            return _book.ApplyLocal(verdict)
                ? new LocalOverrideResult { Outcome = LocalOverrideOutcome.Applied, Verdict = verdict }
                : new LocalOverrideResult { Outcome = LocalOverrideOutcome.Refused, Reason = "the book did not apply it (see the log)", Verdict = verdict };
        }

        /// <summary>
        /// Deletes the stored override. With the apply window open the shipped assets come back and the cached remote delta
        /// (the latest one this device fetched, even while the override was in force) is applied again; with it closed the
        /// running balance is left alone and the change takes effect at next launch.
        /// </summary>
        public LocalOverrideResult Reset()
        {
            if (!BalanceBook.LocalAllowed(DevTools.Available)) return NotAllowed();
            try
            {
                _store.Clear();
            }
            catch (Exception e)
            {
                _log.Error(BalanceBook.Source, $"Balance local override {_store.Path} not deleted; nothing changed.", e);
                return new LocalOverrideResult { Outcome = LocalOverrideOutcome.Refused, Reason = $"{e.GetType().Name}: {e.Message}" };
            }

            if (!_book.ApplyWindowOpen)
            {
                _log.Info(BalanceBook.Source, $"Balance local override deleted; a lobby or match started this launch, so bal={_book.Hash} ({_book.Origin}) runs until next launch.");
                return new LocalOverrideResult { Outcome = LocalOverrideOutcome.ClearedNextLaunch };
            }

            if (_book.Origin == BalanceOrigin.Local)
            {
                _book.RestoreBaseline();
                if (_cache != null) BalanceRefresher.ApplyCached(_book, _cache, _log);
            }
            _log.Info(BalanceBook.Source, $"Balance local override deleted; running bal={_book.Label} ({_book.Origin}).");
            return new LocalOverrideResult { Outcome = LocalOverrideOutcome.Cleared };
        }

        private LocalOverrideResult NotAllowed()
        {
            _log.Error(BalanceBook.Source, "Balance: the local override is refused: it exists only in dev-tools builds.");
            return new LocalOverrideResult { Outcome = LocalOverrideOutcome.NotAllowed, Reason = "dev tools are off" };
        }
    }
}
