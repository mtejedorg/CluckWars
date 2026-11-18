using System;
using System.IO;
using CluckWars.Diagnostics;

namespace CluckWars.Balance
{
    /// <summary>
    /// The dev-tools local balance override on this device (<c>&lt;persistentDataPath&gt;/balance/local.json</c>): the delta the
    /// balance editor applied or stored, in the form the publisher accepts. Same I/O style as <see cref="BalanceCache"/>
    /// (throws on an I/O failure, the caller logs it). Every read, write and delete is gated on
    /// <see cref="BalanceBook.LocalAllowed"/> HERE, not only where the editor is bound: a production build never opens the
    /// file, even if one is left on the device by a development build.
    /// </summary>
    public sealed class BalanceLocalStore
    {
        public string Path { get; }

        public BalanceLocalStore(string root)
        {
            Path = System.IO.Path.Combine(root, "balance", "local.json");
        }

        /// <summary>The stored delta, or null when there is none or dev tools are off. Throws on an I/O failure.</summary>
        public string Read() => Read(DevTools.Available);

        /// <summary><see cref="Read()"/> with the dev-tools answer given (the gate as a pure input, for tests).</summary>
        public string Read(bool devToolsAvailable)
        {
            if (!BalanceBook.LocalAllowed(devToolsAvailable)) return null;
            return File.Exists(Path) ? File.ReadAllText(Path) : null;
        }

        /// <summary>Stores <paramref name="json"/> (null or blank deletes the file). Throws without dev tools or on an I/O failure.</summary>
        public void Write(string json) => Write(json, DevTools.Available);

        public void Write(string json, bool devToolsAvailable)
        {
            if (!BalanceBook.LocalAllowed(devToolsAvailable))
                throw new InvalidOperationException("the local balance override exists only in dev-tools builds");
            if (string.IsNullOrWhiteSpace(json)) { Clear(devToolsAvailable); return; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? throw new InvalidOperationException("no balance directory"));
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(Path)) File.Delete(Path);
            File.Move(tmp, Path);
        }

        /// <summary>Deletes the stored override. Throws without dev tools or on an I/O failure.</summary>
        public void Clear() => Clear(DevTools.Available);

        public void Clear(bool devToolsAvailable)
        {
            if (!BalanceBook.LocalAllowed(devToolsAvailable))
                throw new InvalidOperationException("the local balance override exists only in dev-tools builds");
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
