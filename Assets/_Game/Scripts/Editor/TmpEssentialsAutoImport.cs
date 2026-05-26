#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Imports TextMeshPro's Essential Resources automatically the first time the
    /// project is opened/compiled, so TMP text (used for the ability emoji icon
    /// glyphs — supplementary-plane characters legacy <c>UnityEngine.UI.Text</c>
    /// can't render) works without the manual
    /// <c>Window ▸ TextMeshPro ▸ Import TMP Essential Resources</c> click.
    /// </summary>
    [InitializeOnLoad]
    public static class TmpEssentialsAutoImport
    {
        static TmpEssentialsAutoImport()
        {
            // Defer to after the asset DB is ready, then import once if missing.
            EditorApplication.delayCall += TryImport;
        }

        private static void TryImport()
        {
            // Already imported (TMP Settings asset exists)?
            if (TMP_Settings.instance != null) return;
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return;

            Debug.Log("[CluckWars] Importing TMP Essential Resources (one-time, automatic)…");
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            Debug.Log("[CluckWars] TMP Essential Resources imported.");
        }
    }
}
#endif
