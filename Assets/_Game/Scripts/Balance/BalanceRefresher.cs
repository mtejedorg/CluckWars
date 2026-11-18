using System;
using System.Threading;
using System.Threading.Tasks;
using CluckWars.Logging;
using CluckWars.Services;
using Zenject;

namespace CluckWars.Balance
{
    /// <summary>
    /// Boot side of the balance layer. <see cref="ApplyCached"/> runs synchronously in <c>ProjectInstaller</c> (so the
    /// Session line already carries the hash); <see cref="Initialize"/> then starts one background fetch from the bound
    /// <see cref="IBalanceSource"/>. A new accepted delta is applied whenever it lands while the apply window is open
    /// (until the first lobby or match, <see cref="BalanceBook.CloseApplyWindow"/>) and cached either way ("applies at
    /// next launch" after that); a refused one keeps the current state and is not cached; a failure keeps the cache /
    /// baseline and warns. <see cref="TimeoutSeconds"/> only logs a "still waiting" Warn: a cold boot (UGS init, sign-in,
    /// fetch) often takes longer, and the window, not the clock, is what keeps a delta out of a match. Every
    /// exception is caught and logged: no unobserved task.
    /// </summary>
    public sealed class BalanceRefresher : IInitializable, IDisposable
    {
        public const float TimeoutSeconds = 3f;

        private readonly BalanceBook _book;
        private readonly IBalanceSource _source;
        private readonly BalanceCache _cache;
        private readonly ILogService _log;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed;

        /// <param name="cache">Null when the remote source is off (Editor default): nothing is read or written.</param>
        public BalanceRefresher(BalanceBook book, IBalanceSource source, ILogService log, BalanceCache cache)
        {
            _book = book;
            _source = source;
            _log = log;
            _cache = cache;
        }

        /// <summary>Applies this device's last good delta, if any. Never throws.</summary>
        public static void ApplyCached(BalanceBook book, BalanceCache cache, ILogService log)
        {
            string json;
            try
            {
                json = cache.Read();
            }
            catch (Exception e)
            {
                log.Warn(BalanceBook.Source, $"Balance cache {cache.Path} unreadable ({e.GetType().Name}: {e.Message}); starting from the shipped assets.");
                return;
            }
            if (json == null)
            {
                log.Info(BalanceBook.Source, $"Balance: no cached delta ({cache.Path}); starting from the shipped assets.");
                return;
            }
            book.TryApply(json, BalanceOrigin.Cache);
        }

        public void Initialize() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            try
            {
                var fetch = _source.FetchAsync(_cts.Token);
                var finished = await Task.WhenAny(fetch, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), _cts.Token));
                if (_disposed) { Observe(fetch); return; }
                if (finished != fetch)
                    _log.Warn(BalanceBook.Source, $"Balance fetch from {_source.Name} still waiting after {TimeoutSeconds:0} s; running bal={_book.Label} " +
                                                  $"({_book.Origin}) meanwhile. The answer still applies if no match or lobby has started by then.");

                // The source's own request timeouts bound this wait; the apply window decides whether the answer applies.
                var result = await fetch;
                if (_disposed) return;
                if (!result.Ok)
                {
                    _log.Warn(BalanceBook.Source, $"Balance fetch from {_source.Name} failed ({result.Error}); keeping bal={_book.Label} ({_book.Origin}).");
                    return;
                }
                Handle(result.Json);
            }
            catch (OperationCanceledException)
            {
                // Disposed while waiting (ProjectContext teardown): nothing to apply.
            }
            catch (Exception e)
            {
                _log.Error(BalanceBook.Source, $"Balance refresh from {_source.Name} failed; keeping bal={_book.Label} ({_book.Origin}).", e);
            }
        }

        private void Handle(string json)
        {
            var verdict = _book.Evaluate(json, _source.Name);
            if (!verdict.IsAccepted) return; // logged by Evaluate; the current state and the cache stay

            WriteCache(verdict.IsEmpty ? null : json);

            // A dev-tools local override outranks the remote delta this launch (the cache above still keeps it for Reset
            // and for the next launch); the owner sees this line instead of a silent switch of numbers.
            if (_book.Origin == BalanceOrigin.Local)
            {
                _log.Info(BalanceBook.Source, $"Balance {verdict.Hash} from {_source.Name} cached, NOT applied: a local override (bal={_book.Hash}) is in force this launch.");
                return;
            }

            if (verdict.Hash == _book.Hash)
            {
                _log.Info(BalanceBook.Source, $"Balance from {_source.Name} unchanged: bal={_book.Label} ({_book.Origin}).");
                return;
            }
            if (!_book.ApplyWindowOpen)
            {
                _log.Info(BalanceBook.Source, $"Balance {verdict.Hash} from {_source.Name} arrived after a lobby or match started; cached, applies at next launch.");
                return;
            }
            _book.Apply(verdict, verdict.IsEmpty ? BalanceOrigin.Baseline : BalanceOrigin.Remote);
        }

        private void WriteCache(string json)
        {
            if (_cache == null) return;
            try
            {
                _cache.Write(json);
            }
            catch (Exception e)
            {
                _log.Warn(BalanceBook.Source, $"Balance cache {_cache.Path} not written ({e.GetType().Name}: {e.Message}); this launch is unaffected.");
            }
        }

        /// <summary>Keeps an abandoned fetch from surfacing as an unobserved task exception, and logs what it ended with.</summary>
        private void Observe(Task<BalanceFetchResult> fetch) =>
            fetch.ContinueWith(t =>
            {
                if (t.IsFaulted) _log.Warn(BalanceBook.Source, $"Abandoned balance fetch from {_source.Name} faulted later: {t.Exception?.GetBaseException().Message}");
            }, TaskScheduler.Default);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
