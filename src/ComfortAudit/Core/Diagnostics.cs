using System.Collections.Generic;
using System.Text;
using ComfortAudit.Model;

namespace ComfortAudit.Core
{
    /// <summary>
    /// One-shot report written to the BepInEx log the first time a world is scanned.
    ///
    /// Exists because the two things this mod cannot verify by inspection — the serialized
    /// SE_Rested timings, and whether the cost table's item tokens match this install — are both
    /// only knowable at runtime. Writing them to the log means they can be checked by reading a
    /// file rather than by someone reading numbers off the screen.
    /// </summary>
    public static class Diagnostics
    {
        private static bool _reported;

        private static bool _emptyReported;

        public static void Reset()
        {
            _reported = false;
            _emptyReported = false;
        }

        /// <summary>
        /// An empty recommendation list is a valid outcome, but it looks identical to a broken
        /// filter from the outside. Log the rejection tallies once per transition into that state
        /// so the two can be told apart without guessing.
        /// </summary>
        public static void ReportEmptyRecommendations(ComfortSnapshot snap)
        {
            if (snap == null || !snap.Valid || !snap.RecommendationsReady)
                return;

            if (snap.Recommendations.Count > 0)
            {
                _emptyReported = false;
                return;
            }

            if (_emptyReported)
                return;

            _emptyReported = true;
            ComfortAuditPlugin.Log.LogInfo(
                "no recommendations at comfort " + snap.ComfortLevel
                + " [filter=" + PluginConfig.Filter.Value + "] "
                + (snap.RecStats != null ? snap.RecStats.ToString() : "(no stats)"));
        }

        public static void ReportOnce(ComfortSnapshot snap)
        {
            if (_reported || snap == null || !snap.Valid)
                return;

            // Player.GetComfortLevel() returns its cached m_comfortLevel, which stays 0 until
            // UpdateBaseValue first runs (up to 2 s after spawn). CalculateComfortLevel can never
            // return 0, so a 0 here means "not computed yet", not a disagreement — wait for it,
            // otherwise the report opens with a mismatch that isn't one.
            if (snap.VanillaComfortLevel <= 0)
                return;

            _reported = true;
            var log = ComfortAuditPlugin.Log;

            log.LogInfo("---- ComfortAudit diagnostics ----");
            log.LogInfo(string.Format(
                "SE_Rested: baseTTL={0}s perLevel={1}s source={2}",
                snap.BaseTTL, snap.TTLPerLevel,
                snap.TtlFromLiveEffect ? "live effect" : "ObjectDB prefab"));
            log.LogInfo(string.Format(
                "comfort={0} (game says {1}) shelter={2} cover={3:0.00} pieces in range={4} rested={5}s",
                snap.ComfortLevel, snap.VanillaComfortLevel, snap.InShelter,
                snap.CoverPercentage, snap.PieceCount, snap.RestedSeconds));

            log.LogInfo(string.Format("ceiling: {0} unlocked, {1} with everything (valid={2})",
                snap.CeilingUnlocked, snap.CeilingAll, snap.CeilingValid));

            ReportBreakdown(snap, log);
            ReportCatalogue(log);
            ReportGroups(log);
            ReportCostTokens(log);

            log.LogInfo("---- end diagnostics ----");
        }

        /// <summary>
        /// The live attribution: what is in range and what each piece is actually doing. This is
        /// the mod's core output, so it belongs in the report — without it the diagnostics covered
        /// the inputs (catalogue, costs) but not the result.
        /// </summary>
        public static void ReportBreakdown(ComfortSnapshot snap, BepInEx.Logging.ManualLogSource log)
        {
            if (snap == null || !snap.Valid)
            {
                log.LogInfo("breakdown: no valid snapshot");
                return;
            }

            log.LogInfo(string.Format("breakdown: comfort {0}, shelter {1} (cover {2:0.00}, roof {3})",
                snap.ComfortLevel, snap.InShelter, snap.CoverPercentage, snap.UnderRoof));

            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                log.LogInfo(string.Format("  {0,-14} {1,-22} {2,-16} {3}{4}",
                    ComfortGroups.Name(e.Group),
                    e.DisplayName,
                    e.Status,
                    e.Status == PieceStatus.Counted ? "+" + e.Comfort : "beaten by " + (e.ShadowedBy ?? "?"),
                    e.Inactive ? "  [unlit, designed " + e.RawComfort + "]" : string.Empty));
            }

            if (snap.Recommendations != null)
            {
                for (int i = 0; i < snap.Recommendations.Count; i++)
                {
                    Recommendation r = snap.Recommendations[i];
                    log.LogInfo(string.Format("  rec #{0}: +{1} {2} [{3}] cost {4:0.0}",
                        i + 1, r.Gain, r.DisplayName, r.Kind, r.CostScore));
                }
            }
        }

        private static void ReportCatalogue(BepInEx.Logging.ManualLogSource log)
        {
            List<PieceCatalog.Entry> entries = PieceCatalog.Entries();
            if (entries == null)
            {
                log.LogInfo("catalogue: not built yet");
                return;
            }

            log.LogInfo("catalogue: " + entries.Count + " comfort pieces");
        }

        /// <summary>
        /// Per-group counts, and every ungrouped piece by name. The ungrouped list is the direct
        /// evidence for the stacking behaviour the recommendation maths depends on.
        /// </summary>
        private static void ReportGroups(BepInEx.Logging.ManualLogSource log)
        {
            List<PieceCatalog.Entry> entries = PieceCatalog.Entries();
            if (entries == null)
                return;

            var counts = new Dictionary<Piece.ComfortGroup, int>();
            var best = new Dictionary<Piece.ComfortGroup, int>();
            var ungrouped = new List<string>();

            for (int i = 0; i < entries.Count; i++)
            {
                PieceCatalog.Entry e = entries[i];

                int n;
                counts.TryGetValue(e.Group, out n);
                counts[e.Group] = n + 1;

                int m;
                if (!best.TryGetValue(e.Group, out m) || e.Comfort > m)
                    best[e.Group] = e.Comfort;

                if (e.Group == Piece.ComfortGroup.None)
                {
                    // Display name matters as much as prefab name here: the game deduplicates
                    // ungrouped pieces by m_name, so two prefabs sharing a name do NOT stack.
                    ungrouped.Add(e.PrefabName + " [" + e.DisplayName + "] (" + e.Comfort + ")");
                }
            }

            foreach (KeyValuePair<Piece.ComfortGroup, int> kv in counts)
            {
                log.LogInfo(string.Format("  group {0,-12} {1,3} pieces, best comfort {2}",
                    kv.Key, kv.Value, best[kv.Key]));
            }

            if (ungrouped.Count > 0)
                log.LogInfo("  ungrouped (these stack): " + string.Join(", ", ungrouped.ToArray()));
        }

        private static void ReportCostTokens(BepInEx.Logging.ManualLogSource log)
        {
            List<PieceCatalog.Entry> entries = PieceCatalog.Entries();
            if (entries == null)
                return;

            var used = new SortedDictionary<string, int>();

            for (int i = 0; i < entries.Count; i++)
            {
                Piece.Requirement[] reqs = entries[i].Piece.m_resources;
                if (reqs == null) continue;

                for (int j = 0; j < reqs.Length; j++)
                {
                    string token = PieceCatalog.ItemToken(reqs[j]);
                    if (string.IsNullOrEmpty(token)) continue;

                    int n;
                    used.TryGetValue(token, out n);
                    used[token] = n + 1;
                }
            }

            var known = new List<string>();
            var unknown = new List<string>();

            foreach (KeyValuePair<string, int> kv in used)
            {
                if (CostTable.HasWeight(kv.Key))
                    known.Add(kv.Key + "=" + CostTable.Weight(kv.Key).ToString("0.#"));
                else
                    unknown.Add(kv.Key);
            }

            log.LogInfo("cost tokens in use: " + used.Count
                        + " (" + known.Count + " weighted, " + unknown.Count + " defaulted)");

            if (known.Count > 0)
                log.LogInfo("  weighted: " + string.Join(", ", known.ToArray()));

            if (unknown.Count > 0)
            {
                log.LogInfo("  NO WEIGHT (scoring 1.0): " + string.Join(", ", unknown.ToArray()));

                var sb = new StringBuilder();
                sb.Append("  paste-ready: ");
                for (int i = 0; i < unknown.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append('"').Append(unknown[i]).Append("\": 1.0");
                }
                log.LogInfo(sb.ToString());
            }
        }
    }
}
