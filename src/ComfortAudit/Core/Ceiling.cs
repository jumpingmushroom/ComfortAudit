using System.Collections.Generic;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// The highest comfort reachable at a sheltered spot, derived from the live catalogue.
    ///
    /// Two numbers, because they answer different questions. "Unlocked" is what you could build
    /// today and is the actionable target; "all" includes recipes you have not discovered and is
    /// the long-term ceiling. Showing only the second would be dispiriting early on; showing only
    /// the first hides that the game has more to give.
    /// </summary>
    public static class Ceiling
    {
        public struct Result
        {
            public int Unlocked;
            public int All;
            public bool Valid;
        }

        private const float TtlSeconds = 10f;

        private static Result _cached;
        private static float _computedAt = -999f;

        public static void Invalidate()
        {
            _computedAt = -999f;
        }

        /// <summary>
        /// Cached briefly: it walks the whole catalogue and asks HaveRequirements per piece, which
        /// is cheap but pointless to repeat on every half-second scan. Known recipes change rarely.
        /// </summary>
        public static Result Get(Player player)
        {
            if (Time.time - _computedAt < TtlSeconds)
                return _cached;

            _cached = Compute(player);
            _computedAt = Time.time;
            return _cached;
        }

        private static Result Compute(Player player)
        {
            var result = new Result();

            List<PieceCatalog.Entry> catalog = PieceCatalog.Entries();
            if (catalog == null || player == null)
                return result;

            // Best comfort per real group.
            var bestAll = new Dictionary<Piece.ComfortGroup, int>();
            var bestKnown = new Dictionary<Piece.ComfortGroup, int>();

            // Ungrouped pieces stack, but are deduplicated by display name — so the ceiling is the
            // sum over distinct names, not over prefabs.
            var ungroupedAll = new Dictionary<string, int>();
            var ungroupedKnown = new Dictionary<string, int>();

            for (int i = 0; i < catalog.Count; i++)
            {
                PieceCatalog.Entry e = catalog[i];
                bool known = player.HaveRequirements(e.Piece, Player.RequirementMode.IsKnown);

                if (ComfortGroups.Stacks(e.Group))
                {
                    // Keyed on the raw token, matching the game's duplicate-name rule.
                    Accumulate(ungroupedAll, e.NameToken, e.Comfort);
                    if (known)
                        Accumulate(ungroupedKnown, e.NameToken, e.Comfort);
                }
                else
                {
                    Accumulate(bestAll, e.Group, e.Comfort);
                    if (known)
                        Accumulate(bestKnown, e.Group, e.Comfort);
                }
            }

            // 1 floor + 1 for being sheltered, matching SE_Rested.CalculateComfortLevel.
            const int baseline = 2;

            result.All = baseline + Sum(bestAll) + Sum(ungroupedAll);
            result.Unlocked = baseline + Sum(bestKnown) + Sum(ungroupedKnown);
            result.Valid = true;
            return result;
        }

        private static void Accumulate<TKey>(Dictionary<TKey, int> map, TKey key, int value)
        {
            if (key == null)
                return;

            int existing;
            if (!map.TryGetValue(key, out existing) || value > existing)
                map[key] = value;
        }

        private static int Sum<TKey>(Dictionary<TKey, int> map)
        {
            int total = 0;
            foreach (KeyValuePair<TKey, int> kv in map)
                total += kv.Value;
            return total;
        }
    }
}
