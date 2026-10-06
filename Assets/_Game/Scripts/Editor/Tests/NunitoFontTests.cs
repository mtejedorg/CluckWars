using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the menu body fonts against regressing to a VARIABLE font. Unity's UI Toolkit
    /// text renderer ignores the <c>fvar</c> axes and draws the font's default instance, and
    /// the Nunito variable font defaults to weight 200 (ExtraLight) - so a "Bold" file that is
    /// really the variable font shipped hairline body text. Both files must be static instances.
    /// </summary>
    /// <remarks>
    /// Parses the sfnt table directory by hand (no font package): a table record is
    /// tag[4], checksum, offset, length, and OS/2's <c>usWeightClass</c> is the u16 at byte 4
    /// of that table (version u16, xAvgCharWidth i16, usWeightClass u16).
    /// </remarks>
    public sealed class NunitoFontTests
    {
        private static string PathOf(string file) =>
            Path.Combine(Application.dataPath, "_Game", "Resources", "Fonts", file);

        private static ushort U16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
        private static uint U32(byte[] b, int o) =>
            ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

        private static (bool hasFvar, int weightClass) Inspect(string file)
        {
            string path = PathOf(file);
            Assert.IsTrue(File.Exists(path), $"{file} is missing from Resources/Fonts.");
            var b = File.ReadAllBytes(path);
            int numTables = U16(b, 4);
            bool fvar = false;
            int weight = -1;
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                string tag = System.Text.Encoding.ASCII.GetString(b, rec, 4);
                int offset = (int)U32(b, rec + 8);
                if (tag == "fvar") fvar = true;
                if (tag == "OS/2") weight = U16(b, offset + 4);
            }
            return (fvar, weight);
        }

        [TestCase("Nunito-Bold.ttf", 700)]
        [TestCase("Nunito-ExtraBold.ttf", 800)]
        public void NunitoFiles_AreStaticInstances_AtTheirNamedWeight(string file, int minWeight)
        {
            var (hasFvar, weight) = Inspect(file);

            Assert.IsFalse(hasFvar,
                $"{file} still has an fvar table, so it is the variable font and Unity will render " +
                "its default instance (weight 200). Re-instance it with fontTools varLib.instancer " +
                "(--static) at the wanted weight; keep the .meta so the GUID stays.");
            Assert.GreaterOrEqual(weight, minWeight,
                $"{file} has OS/2 usWeightClass {weight}; a file named for weight {minWeight} " +
                "must declare at least that.");
        }
    }
}
