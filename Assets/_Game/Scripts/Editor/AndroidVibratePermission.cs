using System.IO;
using System.Xml;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Adds <c>android.permission.VIBRATE</c> to the exported Android manifest (Phase 6 chunk 6, A9).
    /// </summary>
    /// <remarks>
    /// Unity adds the permission by itself only when it sees <c>Handheld.Vibrate</c> used; the haptics here go through
    /// <c>android.os.Vibrator</c> over JNI (<c>AndroidHapticDevice</c>), which Unity cannot see, so without this the
    /// buzz would silently do nothing on a device. The project has no custom main manifest, so the permission is patched
    /// into the generated <c>unityLibrary</c> manifest after Gradle project generation; it merges into the app manifest.
    /// The patch itself (<see cref="EnsurePermission"/>) is plain XML and runs under EditMode tests on any build target;
    /// the build callback only exists when the Android module's types do.
    /// </remarks>
    public static class AndroidVibratePermission
    {
        public const string Permission = "android.permission.VIBRATE";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        /// <summary>
        /// Returns <paramref name="manifestXml"/> with a <c>uses-permission</c> for <paramref name="permission"/> as the
        /// first child of <c>&lt;manifest&gt;</c>; unchanged when it is already declared. Throws
        /// <see cref="XmlException"/> on a manifest that is not XML or has no <c>manifest</c> root: a broken manifest
        /// must fail the build loudly, not ship without the permission.
        /// </summary>
        public static string EnsurePermission(string manifestXml, string permission = Permission)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(manifestXml);

            var root = doc.DocumentElement;
            if (root == null || root.Name != "manifest")
                throw new XmlException("AndroidManifest.xml has no <manifest> root element.");

            foreach (XmlNode child in root.ChildNodes)
            {
                if (child is XmlElement e && e.Name == "uses-permission" &&
                    e.GetAttribute("name", AndroidNs) == permission)
                    return manifestXml;
            }

            var perm = doc.CreateElement("uses-permission");
            // Reuse the manifest's own android: prefix; fall back to the conventional one (the writer declares it).
            string prefix = root.GetPrefixOfNamespace(AndroidNs);
            perm.SetAttributeNode(doc.CreateAttribute(string.IsNullOrEmpty(prefix) ? "android" : prefix, "name", AndroidNs)).Value = permission;
            root.InsertBefore(perm, root.FirstChild);
            root.InsertAfter(doc.CreateWhitespace("\n    "), perm);

            // A StringWriter reports UTF-16, which XmlWriter would stamp into the declaration; the file is written
            // back as UTF-8, and Gradle's manifest merger rejects a utf-16 declaration on UTF-8 bytes.
            using var sw = new Utf8StringWriter();
            using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = !manifestXml.TrimStart().StartsWith("<?xml") }))
                doc.Save(xw);
            return sw.ToString();
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
        }

#if UNITY_ANDROID
        private sealed class Patcher : IPostGenerateGradleAndroidProject
        {
            public int callbackOrder => 0;

            public void OnPostGenerateGradleAndroidProject(string path)
            {
                string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException("Cannot add the VIBRATE permission: no AndroidManifest.xml in the generated Gradle project.", manifestPath);

                string patched = EnsurePermission(File.ReadAllText(manifestPath));
                File.WriteAllText(manifestPath, patched);
            }
        }
#endif
    }
}
