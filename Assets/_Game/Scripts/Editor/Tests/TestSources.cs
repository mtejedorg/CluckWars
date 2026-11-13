using System.IO;
using System.Linq;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Reads a runtime source file for the source-scan guards, as ONE class rather than one file.
    /// </summary>
    /// <remarks>
    /// A21 (architecture audit 2026-11-12) splits the oversized MonoBehaviours into partial-class files:
    /// the component keeps its type, name, namespace and serialized fields, and part of its body moves to
    /// a sibling <c>Name.Something.cs</c>. Every guard that asks "does this class still route through the
    /// shared helper / still pair its subscriptions / still read this one source of truth" is asking about
    /// the CLASS, so it must see all of its text — otherwise splitting a file silently satisfies a guard
    /// that was written to catch a missing line.
    /// <para>
    /// <see cref="ReadClass"/> therefore returns the named file plus every sibling whose name is that file's
    /// name, a dot, and more (<c>TouchControlsController.cs</c> + <c>TouchControlsController.Hud.cs</c>).
    /// A guard that is deliberately about ONE FILE keeps using <c>File.ReadAllText</c>.
    /// </para>
    /// </remarks>
    public static class TestSources
    {
        /// <summary>The folder that holds <c>Assets/</c>.</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>
        /// The whole text of the class declared in <paramref name="projectRelativePath"/>: that file, then each
        /// of its partial siblings in name order. Paths are relative to <see cref="ProjectRoot"/>.
        /// </summary>
        public static string ReadClass(string projectRelativePath)
        {
            string full = Path.Combine(ProjectRoot, projectRelativePath);
            string dir = Path.GetDirectoryName(full);
            string name = Path.GetFileNameWithoutExtension(full);

            var parts = Directory.GetFiles(dir, name + ".*.cs").OrderBy(p => p, System.StringComparer.Ordinal);
            return File.ReadAllText(full) + string.Concat(parts.Select(p => "\n" + File.ReadAllText(p)));
        }
    }
}
