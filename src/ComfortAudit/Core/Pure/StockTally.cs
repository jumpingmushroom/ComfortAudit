using System;
using System.Collections.Generic;
using System.Linq;

namespace ComfortAudit.Core.Pure
{
    /// <summary>
    /// Item counts summed over a set of containers, keyed by the item's shared-name token
    /// (e.g. "$item_finewood") — the same key Inventory.CountItems uses, compared ordinally.
    /// </summary>
    public sealed class StockTally
    {
        /// <summary>
        /// A shared, permanently-empty instance for the "nothing to report" case. It is mutable
        /// like any StockTally, but is never added to — treat it as read-only.
        /// </summary>
        public static readonly StockTally Empty = new StockTally();

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Containers { get; private set; }

        public void CountContainer()
        {
            Containers++;
        }

        public void Add(string token, int count)
        {
            if (string.IsNullOrEmpty(token) || count <= 0)
                return;

            int existing;
            _counts.TryGetValue(token, out existing);
            _counts[token] = existing + count;
        }

        public int Count(string token)
        {
            int n;
            return token != null && _counts.TryGetValue(token, out n) ? n : 0;
        }

        /// <summary>Sorted, so console output and logs do not reorder between identical scans.</summary>
        public IEnumerable<KeyValuePair<string, int>> Items
        {
            get { return _counts.OrderBy(kv => kv.Key, StringComparer.Ordinal); }
        }
    }
}
