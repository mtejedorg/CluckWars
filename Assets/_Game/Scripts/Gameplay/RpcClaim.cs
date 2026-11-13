using CluckWars.Diagnostics;
using Fusion;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The one "who may send this" rule for an <c>RpcSources.All</c> RPC that names a network object (A19). Every such
    /// RPC is a claim: the receiver checks that the sender is entitled to speak for the object the call names.
    /// </summary>
    /// <remarks>
    /// <b>The rule.</b> On a local invoke the object must be simulated on this peer (solo, the master's bots, a peer's
    /// own chicken): <c>HasStateAuthority</c>, never a <c>PlayerRef</c> comparison, because
    /// <c>Object.StateAuthority</c> reads <c>[Player:None]</c> in <c>GameMode.Single</c>. Off the wire the sender must
    /// be the object's state authority, which for a bot is the master. A remote peer cannot forge
    /// <c>IsInvokeLocal</c>.
    /// <para>
    /// <b>Not input authority.</b> Two older copies (the base's depositor and the steal's thief) compared the sender
    /// with the chicken's <i>input</i> authority off the wire. For a human's chicken the two are the same peer; a bot
    /// has input authority <c>None</c>, so that copy refused every bot whose call arrived off the wire (a bot robbing a
    /// remote human). Both now use this rule; see <c>RpcClaimTests</c>.
    /// </para>
    /// </remarks>
    public static class RpcClaim
    {
        /// <summary>The rule on plain facts, for EditMode.</summary>
        /// <param name="isInvokeLocal">The RPC was invoked on this peer, not received.</param>
        /// <param name="simulatedHere">This peer holds the named object's state authority.</param>
        /// <param name="senderIsStateAuthority">The sender is the named object's state authority.</param>
        public static bool SpeaksFor(bool isInvokeLocal, bool simulatedHere, bool senderIsStateAuthority) =>
            isInvokeLocal ? simulatedHere : senderIsStateAuthority;

        /// <summary>True when the sender of <paramref name="info"/> may speak for <paramref name="named"/>. False for a null or despawned object.</summary>
        public static bool SenderSpeaksFor(NetworkObject named, in RpcInfo info) =>
            named != null && named.IsValid &&
            SpeaksFor(info.IsInvokeLocal, named.HasStateAuthority, named.StateAuthority == info.Source);

        /// <summary><see cref="SenderSpeaksFor(NetworkObject, in RpcInfo)"/> for the object a behaviour sits on.</summary>
        public static bool SenderSpeaksFor(NetworkBehaviour named, in RpcInfo info) =>
            named != null && SenderSpeaksFor(named.Object, info);
    }

    /// <summary>
    /// The one rate limit for "rejected RPC" lines (A19): at most one line per sender per
    /// <see cref="IntervalSeconds"/>, with a note of how many were held back since. Each RPC family keeps its own
    /// instance, so a flood on one does not hide a rejection on another.
    /// </summary>
    public sealed class RpcRejectionLimiter
    {
        public const float IntervalSeconds = 2f;

        private readonly PerKeyRateLimiter _limiter = new PerKeyRateLimiter(IntervalSeconds);

        /// <summary>True when a line about <paramref name="sender"/> may go out now; <paramref name="suppressedNote"/>
        /// is the text to append to it (empty when nothing was held back).</summary>
        public bool Allow(PlayerRef sender, out string suppressedNote) =>
            Allow(sender.RawEncoded, Time.realtimeSinceStartup, out suppressedNote);

        /// <summary>Pure form, for EditMode.</summary>
        public bool Allow(int senderKey, float now, out string suppressedNote)
        {
            bool allowed = _limiter.Allow(senderKey, now, out int held);
            suppressedNote = allowed ? SuppressedNote(held) : string.Empty;
            return allowed;
        }

        public static string SuppressedNote(int held) => held > 0 ? $" (+{held} similar suppressed)" : string.Empty;
    }
}
