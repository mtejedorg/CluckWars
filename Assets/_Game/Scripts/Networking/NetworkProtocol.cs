namespace CluckWars.Networking
{
    /// <summary>
    /// The game's network protocol version (BL-218): ONE integer, bumped BY HAND in the same commit that changes a <c>[Networked]</c> property or an
    /// <c>[Rpc]</c> signature, so builds whose networked layouts differ never share a room. It is not the build number: two dev builds of the same
    /// protocol must still meet. <c>FusionNetworkService</c> hands <see cref="AppVersion"/> to Photon as the app version, and Photon only matches
    /// clients that carry the same one. The guard test <c>NetworkProtocolTests</c> fails when the set of networked members changes and this number does not.
    /// </summary>
    public static class NetworkProtocol
    {
        /// <summary>
        /// 1: every build before the loot mark (its app version was empty). 2: <c>ThiefLoot</c> on the chicken, seven new networked properties and the
        /// marked-steal RPCs (BL-217). 3: the Peck's fresh state on every chicken (<c>AbilityController.FedTick</c>, <c>FedStateId</c>, <c>FreshStateId</c>, <c>BittenStateId</c>, <c>RPC_MarkBitten</c>) for the Enforcer and its hit-steal sibling, plus the state of <c>ChickenHitSteal</c> (<c>HitStealEventId</c>, <c>LastHitVictim</c>, <c>LastHitTake</c>) and <c>EnforcerDebts</c> (the debt arrays, the three event ids, one victim field per event id, <c>LastCashBonus</c>) (BL-226). Bump it, then update the fingerprint in <c>NetworkProtocolTests</c> (the failing test prints the new value).
        /// </summary>
        public const int Version = 3;

        /// <summary>What Photon matches on: <c>p</c> plus <see cref="Version"/>.</summary>
        public static string AppVersion => "p" + Version;
    }
}
