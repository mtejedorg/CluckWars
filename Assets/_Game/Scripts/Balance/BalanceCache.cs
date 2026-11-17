using System;
using System.IO;

namespace CluckWars.Balance
{
    /// <summary>
    /// The last good delta on this device, one file per Remote Config environment
    /// (<c>&lt;persistentDataPath&gt;/balance/&lt;environment&gt;.json</c>), so a dev cache never reaches a production read.
    /// It holds the delta exactly as fetched and is validated again on every load (the build may have changed).
    /// </summary>
    public sealed class BalanceCache
    {
        public string Path { get; }

        public BalanceCache(string root, string environment)
        {
            Path = System.IO.Path.Combine(root, "balance", environment + ".json");
        }

        /// <summary>The cached delta, or null when there is none. Throws on an I/O failure (the caller logs it).</summary>
        public string Read() => File.Exists(Path) ? File.ReadAllText(Path) : null;

        /// <summary>Stores <paramref name="json"/>; null or blank deletes the file ("no delta published").</summary>
        public void Write(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                if (File.Exists(Path)) File.Delete(Path);
                return;
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? throw new InvalidOperationException("no cache directory"));
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(Path)) File.Delete(Path);
            File.Move(tmp, Path);
        }
    }
}
