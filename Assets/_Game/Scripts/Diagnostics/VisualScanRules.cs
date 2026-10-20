using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>The offender categories of a Visual scan, in the order the summary prints them.</summary>
    public enum VisualCategory : byte { ErrShader = 0, Unsupported = 1, NullMat = 2, Nan = 3, OutOfBounds = 4, ChickensNoRenderer = 5 }

    /// <summary>
    /// The visual-integrity probe's classification rules (test-session item 4), pure so tests pin them: which shader
    /// names mean "this renders pink / cyan", what counts as a broken transform or an object outside the arena, and the
    /// exact summary / offender line text the merge script greps.
    /// </summary>
    public static class VisualScanRules
    {
        public const int CategoryCount = 6;
        public const string InternalErrorShader = "Hidden/InternalErrorShader";
        /// <summary>Offender paths printed per category before <c>+K more</c>.</summary>
        public const int MaxPathsPerCategory = 8;
        /// <summary>Metres beyond the arena edge before a renderer counts as outside (covers the surround art).</summary>
        public const float BoundsMargin = 20f;
        public const float MinY = -20f, MaxY = 80f;

        /// <summary>
        /// A shader whose output is the error / fallback colour: Unity's error shader, URP's fallback error, or a
        /// Built-in RP shader that URP cannot render (it shows magenta: CONVENTIONS "primitiveDefault renders magenta").
        /// </summary>
        public static bool IsErrorShaderName(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return true;
            if (shaderName == InternalErrorShader) return true;
            if (shaderName.IndexOf("FallbackError", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (shaderName.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Built-in RP surface shaders: no URP pass, magenta under URP.
            if (shaderName == "Standard" || shaderName == "Standard (Specular setup)" || shaderName == "Autodesk Interactive") return true;
            if (shaderName.StartsWith("Legacy Shaders/", StringComparison.Ordinal)) return true;
            if (shaderName == "Particles/Standard Unlit" || shaderName == "Particles/Standard Surface") return true;
            return false;
        }

        public static bool IsNonFinite(float v) => float.IsNaN(v) || float.IsInfinity(v);

        public static bool IsNonFinite(Vector3 v) => IsNonFinite(v.x) || IsNonFinite(v.y) || IsNonFinite(v.z);

        public static bool IsNonFinite(Quaternion q) => IsNonFinite(q.x) || IsNonFinite(q.y) || IsNonFinite(q.z) || IsNonFinite(q.w);

        /// <summary>Outside the square arena (half size + <see cref="BoundsMargin"/>) or far above / below the ground.</summary>
        public static bool IsOutOfBounds(Vector3 centre, float arenaHalfSize)
        {
            if (IsNonFinite(centre)) return true;
            float limit = arenaHalfSize + BoundsMargin;
            return Mathf.Abs(centre.x) > limit || Mathf.Abs(centre.z) > limit || centre.y < MinY || centre.y > MaxY;
        }

        /// <summary>Only what the match camera can see may count as out of bounds (the podium stage renders elsewhere on its own layer).</summary>
        public static bool IsLayerVisible(int cullingMask, int layer) => layer >= 0 && layer < 32 && (cullingMask & (1 << layer)) != 0;

        public static string CategoryName(VisualCategory c) => c switch
        {
            VisualCategory.ErrShader          => "errShader",
            VisualCategory.Unsupported        => "unsupported",
            VisualCategory.NullMat            => "nullMat",
            VisualCategory.Nan                => "nan",
            VisualCategory.OutOfBounds        => "outOfBounds",
            VisualCategory.ChickensNoRenderer => "chickensNoRenderer",
            _                                 => c.ToString(),
        };

        /// <summary>
        /// <c>Visual scan#N reason=.. renderers=R ms=.. errShader=a unsupported=b nullMat=c nan=d outOfBounds=e
        /// chickensNoRenderer=f cams=g mask=0x.. cover=0|1</c>.
        /// </summary>
        public static string Summary(int scan, string reason, int renderers, float ms, int[] counts, int cams, int mask, bool cover)
        {
            var sb = new StringBuilder(200);
            sb.Append("Visual scan#").Append(scan.ToString(CultureInfo.InvariantCulture))
              .Append(" reason=").Append(reason)
              .Append(" renderers=").Append(renderers.ToString(CultureInfo.InvariantCulture))
              .Append(" ms=").Append(ms.ToString("0.0", CultureInfo.InvariantCulture));
            for (int i = 0; i < CategoryCount; i++)
                sb.Append(' ').Append(CategoryName((VisualCategory)i)).Append('=')
                  .Append((counts != null && i < counts.Length ? counts[i] : 0).ToString(CultureInfo.InvariantCulture));
            sb.Append(" cams=").Append(cams.ToString(CultureInfo.InvariantCulture))
              .Append(" mask=0x").Append(mask.ToString("X8", CultureInfo.InvariantCulture))
              .Append(" cover=").Append(cover ? '1' : '0');
            return sb.ToString();
        }

        /// <summary><c>Visual &lt;category&gt; n=N: path1, path2 … +K more</c> (at most <see cref="MaxPathsPerCategory"/> paths).</summary>
        public static string OffenderLine(VisualCategory category, int total, IReadOnlyList<string> paths)
        {
            var sb = new StringBuilder(256);
            sb.Append("Visual ").Append(CategoryName(category)).Append(" n=").Append(total.ToString(CultureInfo.InvariantCulture)).Append(':');
            int shown = 0;
            if (paths != null)
                for (; shown < paths.Count && shown < MaxPathsPerCategory; shown++)
                    sb.Append(shown == 0 ? " " : ", ").Append(paths[shown]);
            if (total > shown) sb.Append(" +").Append((total - shown).ToString(CultureInfo.InvariantCulture)).Append(" more");
            return sb.ToString();
        }

        /// <summary>The hierarchy path of <paramref name="t"/> (<c>Root/Child/Leaf</c>).</summary>
        public static string PathOf(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, '/').Insert(0, p.name);
            return sb.ToString();
        }
    }
}
