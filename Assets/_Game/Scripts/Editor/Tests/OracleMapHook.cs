using CluckWars.Balance;
using UnityEditor;

namespace CluckWars.Tests
{
    /// <summary>
    /// Gives <see cref="BalanceSctCheck"/> the shipped map in the Editor, so the dev-tools balance editor can run the SCT axiom
    /// on a proposed delta there. A player build cannot read the Game scene or the prefabs, has no provider, and so blocks a
    /// proposal that touches an SCT input (see <see cref="BalanceSctCheck"/>). The map is <see cref="ShippedMap.OracleMap"/>,
    /// the same one <c>BalanceOracleTests</c> asserts against, so the editor and the suite cannot disagree about the arena.
    /// </summary>
    internal static class OracleMapHook
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            if (BalanceSctCheck.MapProvider == null) BalanceSctCheck.MapProvider = ShippedMap.OracleMap;
        }

        // With Enter Play Mode options that skip the domain reload, InitializeOnLoad does not run again: register here too.
        [InitializeOnEnterPlayMode]
        private static void RegisterOnEnterPlayMode(EnterPlayModeOptions options) => Register();
    }
}
