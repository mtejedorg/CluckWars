using System.Collections.Generic;
using System.IO;

namespace CluckWars.Audio
{
    /// <summary>
    /// A08 (architecture audit 2026-11-12): a sound that cannot play is said once, not never and not every time.
    /// One line per distinct call site (a null clip) or per sound id (no clip in the registry). Pure, tested.
    /// </summary>
    public sealed class MissingSfxReporter
    {
        private readonly HashSet<(string File, int Line)> _sites = new HashSet<(string, int)>();
        private readonly HashSet<SfxId> _ids = new HashSet<SfxId>();

        /// <summary>The line to log for a null clip from this call site, or null when that site was already reported.</summary>
        public string NullClip(string callerFile, int callerLine)
        {
            if (!_sites.Add((callerFile ?? string.Empty, callerLine))) return null;
            string site = string.IsNullOrEmpty(callerFile) ? "an unknown call site" : $"{Path.GetFileName(callerFile)}:{callerLine}";
            return $"PlaySFX got a null clip from {site}: nothing played. An audio registry slot is empty or the registry " +
                   "was not injected there (reported once per call site).";
        }

        /// <summary>The line to log for an id with no clip, or null when that id was already reported.</summary>
        public string MissingId(SfxId id, bool hasRegistry)
        {
            if (!_ids.Add(id)) return null;
            return hasRegistry
                ? $"PlaySFX({id}): the audio registry has no clip for this sound id: nothing played (reported once per id)."
                : $"PlaySFX({id}): no audio registry to resolve the sound id from: nothing played (reported once per id).";
        }
    }
}
