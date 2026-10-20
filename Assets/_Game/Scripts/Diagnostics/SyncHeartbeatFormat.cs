using System.Globalization;
using System.Text;

namespace CluckWars.Diagnostics
{
    /// <summary>One chicken in a <c>Sync</c> heartbeat. Humans are keyed by PlayerId, bots by their home corner.</summary>
    public struct SyncChicken
    {
        public bool Bot;
        /// <summary>PlayerId for a human, home corner for a bot.</summary>
        public int Key;
        public float X, Z;
        public int Hp, Cargo;
        public string ControlState;
    }

    /// <summary>One base: its corner and its banked food (floored).</summary>
    public struct SyncBase
    {
        public int Corner, Food;
    }

    /// <summary>One food pile: where it stands (its stable sort key) and how much it holds.</summary>
    public struct SyncPile
    {
        public float X, Z, Amount;
    }

    /// <summary>
    /// The <c>Sync</c> heartbeat line (test-session item 3), which the merge script diffs across peers:
    /// <c>Sync t=&lt;boundaryTick&gt; st=&lt;state&gt; rem=&lt;0.0&gt; | &lt;id&gt;:x=0.0,z=0.0,hp=0,cg=0,cs=Free[,bot] ; … |
    /// bases=&lt;corner&gt;=&lt;food&gt;,… | piles=&lt;0.0&gt;,…</c>. Pure, so the ordering and the fixed decimals are pinned
    /// by tests: humans first by PlayerId (id <c>P&lt;n&gt;</c>), then bots by home corner (<c>B&lt;corner&gt;</c>); bases
    /// by corner; piles by x then z (rounded to 0.1 m so peers sort the same pile identically). Sorting is in place
    /// (insertion sort on the caller's preallocated arrays): no allocation besides the line itself.
    /// </summary>
    public static class SyncHeartbeatFormat
    {
        public static string Id(in SyncChicken c) => (c.Bot ? "B" : "P") + c.Key.ToString(CultureInfo.InvariantCulture);

        public static void Build(StringBuilder sb, int boundaryTick, string state, float remaining,
            SyncChicken[] chickens, int chickenCount, SyncBase[] bases, int baseCount, SyncPile[] piles, int pileCount)
        {
            SortChickens(chickens, chickenCount);
            SortBases(bases, baseCount);
            SortPiles(piles, pileCount);

            sb.Append("Sync t=").Append(boundaryTick.ToString(CultureInfo.InvariantCulture))
              .Append(" st=").Append(state)
              .Append(" rem=").Append(F1(remaining))
              .Append(" | ");
            for (int i = 0; i < chickenCount; i++)
            {
                if (i > 0) sb.Append(" ; ");
                var c = chickens[i];
                sb.Append(Id(c))
                  .Append(":x=").Append(F1(c.X))
                  .Append(",z=").Append(F1(c.Z))
                  .Append(",hp=").Append(c.Hp.ToString(CultureInfo.InvariantCulture))
                  .Append(",cg=").Append(c.Cargo.ToString(CultureInfo.InvariantCulture))
                  .Append(",cs=").Append(string.IsNullOrEmpty(c.ControlState) ? "None" : c.ControlState);
                if (c.Bot) sb.Append(",bot");
            }
            sb.Append(" | bases=");
            for (int i = 0; i < baseCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(bases[i].Corner.ToString(CultureInfo.InvariantCulture)).Append('=')
                  .Append(bases[i].Food.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(" | piles=");
            for (int i = 0; i < pileCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(F1(piles[i].Amount));
            }
        }

        /// <summary>One decimal, invariant culture, and never <c>-0.0</c> (it would diff against <c>0.0</c>).</summary>
        public static string F1(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return v.ToString(CultureInfo.InvariantCulture);
            var s = v.ToString("0.0", CultureInfo.InvariantCulture);
            return s == "-0.0" ? "0.0" : s;
        }

        private static int Compare(in SyncChicken a, in SyncChicken b)
        {
            if (a.Bot != b.Bot) return a.Bot ? 1 : -1;
            return a.Key.CompareTo(b.Key);
        }

        private static void SortChickens(SyncChicken[] a, int n)
        {
            for (int i = 1; i < n; i++)
            {
                var v = a[i];
                int j = i - 1;
                while (j >= 0 && Compare(a[j], v) > 0) { a[j + 1] = a[j]; j--; }
                a[j + 1] = v;
            }
        }

        private static void SortBases(SyncBase[] a, int n)
        {
            for (int i = 1; i < n; i++)
            {
                var v = a[i];
                int j = i - 1;
                while (j >= 0 && a[j].Corner > v.Corner) { a[j + 1] = a[j]; j--; }
                a[j + 1] = v;
            }
        }

        private static int ComparePiles(in SyncPile a, in SyncPile b)
        {
            int ax = Round10(a.X), bx = Round10(b.X);
            if (ax != bx) return ax.CompareTo(bx);
            return Round10(a.Z).CompareTo(Round10(b.Z));
        }

        private static int Round10(float v) => (int)System.Math.Round(v * 10f);

        private static void SortPiles(SyncPile[] a, int n)
        {
            for (int i = 1; i < n; i++)
            {
                var v = a[i];
                int j = i - 1;
                while (j >= 0 && ComparePiles(a[j], v) > 0) { a[j + 1] = a[j]; j--; }
                a[j + 1] = v;
            }
        }
    }

    /// <summary>
    /// Fires once per heartbeat boundary on the simulation tick (<c>tick % period == 0</c>), keyed by the boundary tick, so
    /// every peer logs the same ticks. A frame that skipped past the boundary tick logs at the first tick after it, still
    /// keyed by the boundary. Attaching mid-period does not fire for the boundary already passed (another peer may have
    /// logged it with different state); a tick that goes backwards (a new runner) starts over.
    /// </summary>
    public sealed class TickBoundary
    {
        private int _last;
        private bool _attached;

        public void Reset() => _attached = false;

        public bool TryCross(int tick, int period, out int boundary)
        {
            boundary = 0;
            if (period <= 0 || tick < 0) return false;
            int b = tick - tick % period;
            if (!_attached || tick < _last)
            {
                _attached = true;
                _last = b;
                if (tick != b) return false;
                boundary = b;
                return true;
            }
            if (b <= _last) return false;
            _last = b;
            boundary = b;
            return true;
        }
    }
}
