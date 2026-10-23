using System.IO;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Every UXML file must be well-formed XML. A malformed file (for example "--" inside a comment, which XML forbids)
    /// does not fail the Editor import loudly: UI Toolkit just drops the rest of the page, and a build then misses
    /// elements such as #GearChicken or #SlotBox0 (found live in the WP9 run; no other test parsed the markup).
    /// </summary>
    public sealed class UxmlWellFormedTests
    {
        [Test]
        public void EveryUxmlFile_IsWellFormedXml()
        {
            string assets = Application.dataPath;
            var files = Directory.GetFiles(assets, "*.uxml", SearchOption.AllDirectories)
                                 .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Photon" + Path.DirectorySeparatorChar))
                                 .ToArray();
            Assert.That(files.Length, Is.GreaterThan(5), "no UXML files found under " + assets);

            var broken = new System.Collections.Generic.List<string>();
            foreach (var f in files)
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(f);
                }
                catch (XmlException e)
                {
                    broken.Add($"{Path.GetFileName(f)} line {e.LineNumber}, col {e.LinePosition}: {e.Message}");
                }
            }
            Assert.That(broken, Is.Empty, "malformed UXML (UI Toolkit silently drops the rest of the page):\n" + string.Join("\n", broken));
        }
    }
}
