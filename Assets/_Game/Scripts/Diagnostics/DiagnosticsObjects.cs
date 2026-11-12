using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>Keeps a probe alive across scene loads (Zenject parents it under the ProjectContext; this is the fallback).</summary>
    /// <remarks>Internal to the diagnostics layer (the probes and drivers). It lived in <c>DiagnosticsState.cs</c> until
    /// audit batch 4 moved that file to <c>Diagnostics/Shared</c>, which lower layers may reference.</remarks>
    internal static class DiagnosticsObjects
    {
        public static void KeepAcrossScenes(GameObject go)
        {
            if (go != null && go.transform.parent == null) Object.DontDestroyOnLoad(go);
        }
    }
}
