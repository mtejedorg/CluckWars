using System;
using System.Text.RegularExpressions;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Pure build-time decisions for <see cref="CluckWarsBuildMenu"/> (CW-200, CW-201). No Editor or
    /// PlayerSettings access here, so EditMode tests pin every rule.
    /// </summary>
    public static class BuildGuards
    {
        /// <summary>The Unity Cloud project id UGS needs at runtime (Application.cloudProjectId).</summary>
        public const string UgsProjectId = "14249009-abbe-423c-8c0c-8391b8974857";

        /// <summary>Environment variable that overrides <see cref="UgsProjectId"/> when ProjectSettings has no valid id.</summary>
        public const string CloudProjectIdEnvVar = "CW_CLOUD_PROJECT_ID";

        /// <summary>Pinned Android / iOS application identifier. Changing it makes installed builds a different app.</summary>
        public const string AppIdentifier = "com.cluckwars.game";

        private static readonly Regex ReverseDns =
            new Regex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

        /// <summary>
        /// Null when <paramref name="id"/> is a usable cloud project id (a canonical, non-empty GUID with no
        /// surrounding whitespace), otherwise a message saying what is wrong.
        /// </summary>
        public static string ValidateCloudProjectId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "cloudProjectId is empty: a player built without it cannot initialise UGS (every Host/Join fails " +
                       $"with 'Some services couldn't be initialized'). Set {CloudProjectIdEnvVar} or sign in / link the project in the Editor.";
            if (id != id.Trim() || !Guid.TryParseExact(id, "D", out var guid))
                return $"cloudProjectId '{id}' is not a bare GUID in the canonical 8-4-4-4-12 form (no braces or whitespace).";
            if (guid == Guid.Empty)
                return "cloudProjectId is the all-zero GUID.";
            return null;
        }

        /// <summary>
        /// Picks the id to bake into the build. A valid <paramref name="current"/> (ProjectSettings) is kept. Otherwise a
        /// non-blank <paramref name="envOverride"/> wins, then <see cref="UgsProjectId"/>. An env value is returned even when
        /// malformed so <see cref="ValidateCloudProjectId"/> rejects it, rather than silently falling back to the constant.
        /// </summary>
        public static string ResolveCloudProjectId(string current, string envOverride, out string source)
        {
            if (ValidateCloudProjectId(current) == null) { source = "ProjectSettings"; return current; }
            if (!string.IsNullOrWhiteSpace(envOverride)) { source = CloudProjectIdEnvVar; return envOverride.Trim(); }
            source = "BuildGuards.UgsProjectId";
            return UgsProjectId;
        }

        /// <summary>
        /// Null when <paramref name="id"/> is a deliberately chosen reverse-DNS application identifier, otherwise a message.
        /// Rejects empty, malformed, and Unity's template/default ids (<c>com.DefaultCompany.*</c>, <c>com.UnityTechnologies.*</c>).
        /// </summary>
        public static string ValidateApplicationIdentifier(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "applicationIdentifier is empty.";
            if (!ReverseDns.IsMatch(id))
                return $"applicationIdentifier '{id}' is not a valid reverse-DNS identifier.";
            if (id.StartsWith("com.DefaultCompany.", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("com.UnityTechnologies.", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("com.Unity-Technologies.", StringComparison.OrdinalIgnoreCase))
                return $"applicationIdentifier '{id}' is a Unity default; pin it to {AppIdentifier}.";
            return null;
        }
    }
}
