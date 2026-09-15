using System.Collections.Generic;
using ComfortAudit.Model;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Works out the cheapest way to raise comfort from here.
    ///
    /// Ranking is: free gains first, then comfort gain descending, then weighted material cost
    /// ascending, then a chain of deterministic tie-breaks. Determinism matters — a top
    /// recommendation that flickers between two equal options every half second reads as a bug.
    /// </summary>
    public static class Recommender
    {
        public static void Fill(ComfortSnapshot snap, Player player)
        {
            if (player == null || !snap.Valid)
                return;

            List<PieceCatalog.Entry> catalog = PieceCatalog.Entries();
            if (catalog == null)
                return;

            snap.RecommendationsReady = true;
            var stats = new RecommendationStats { Catalogue = catalog.Count };
            snap.RecStats = stats;

            // What each group currently contributes, and which display names are already counted
            // (None-group pieces stack, so they are deduplicated by name rather than by group).
            var counted = new Dictionary<Piece.ComfortGroup, int>();
            var countedNames = new HashSet<string>();
            var winners = new Dictionary<Piece.ComfortGroup, string>();

            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                if (!e.Contributing)
                    continue;

                countedNames.Add(e.NameToken);

                int existing;
                if (!counted.TryGetValue(e.Group, out existing) || e.Comfort > existing)
                {
                    counted[e.Group] = e.Comfort;
                    winners[e.Group] = e.DisplayName;
                }
            }

            var results = new List<Recommendation>();

            AddFreeGains(snap, results, counted, countedNames);
            AddShelter(snap, results);
            AddPieceUpgrades(snap, player, catalog, results, counted, countedNames, winners, stats);

            results.Sort(Compare);

            int max = Mathf.Max(1, PluginConfig.MaxRecommendations.Value);
            if (results.Count > max)
                results.RemoveRange(max, results.Count - max);

            snap.Recommendations = results;
        }

        /// <summary>
        /// Pieces already placed but switched off. GetComfort() returns 0 for them, so they cost
        /// nothing to "build" — you just light them.
        /// </summary>
        private static void AddFreeGains(ComfortSnapshot snap, List<Recommendation> results,
            Dictionary<Piece.ComfortGroup, int> counted, HashSet<string> countedNames)
        {
            var seen = new HashSet<string>();

            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                if (!e.Inactive || e.RawComfort <= 0)
                    continue;

                if (!seen.Add(e.DisplayName))
                    continue;

                int gain = GainFor(e.Group, e.RawComfort, e.NameToken, counted, countedNames);
                if (gain <= 0)
                    continue;

                results.Add(new Recommendation
                {
                    Kind = RecommendationKind.LightIt,
                    DisplayName = e.DisplayName,
                    PrefabName = e.PrefabName,
                    Group = e.Group,
                    GroupKnown = e.GroupKnown,
                    Gain = gain,
                    CostScore = 0f,
                    HaveAllMaterials = true,
                    StationInRange = true
                });
            }
        }

        /// <summary>
        /// Unsheltered, comfort is a hard 1 and no furniture counts. That makes a roof almost
        /// always the single largest available gain, so it is surfaced as its own recommendation.
        /// </summary>
        private static void AddShelter(ComfortSnapshot snap, List<Recommendation> results)
        {
            if (snap.InShelter)
                return;

            int gain = snap.PotentialIfSheltered - snap.ComfortLevel;
            if (gain <= 0)
                return;

            results.Add(new Recommendation
            {
                Kind = RecommendationKind.Shelter,
                DisplayName = L10n.Strings.Get("$comfortaudit_rec_roof"),
                Group = Piece.ComfortGroup.None,
                GroupKnown = true,
                Gain = gain,
                CostScore = 0f,
                HaveAllMaterials = true,
                StationInRange = true
            });
        }

        private static void AddPieceUpgrades(ComfortSnapshot snap, Player player,
            List<PieceCatalog.Entry> catalog, List<Recommendation> results,
            Dictionary<Piece.ComfortGroup, int> counted, HashSet<string> countedNames,
            Dictionary<Piece.ComfortGroup, string> winners, RecommendationStats stats)
        {
            // Only the best candidate per group is worth showing; five ways to fix the Chair
            // group is noise, not advice.
            var bestPerGroup = new Dictionary<Piece.ComfortGroup, Recommendation>();
            var bestUngrouped = new List<Recommendation>();

            for (int i = 0; i < catalog.Count; i++)
            {
                PieceCatalog.Entry c = catalog[i];

                int gain = GainFor(c.Group, c.Comfort, c.NameToken, counted, countedNames);
                if (gain <= 0)
                {
                    stats.NoGain++;
                    continue;
                }

                if (!player.HaveRequirements(c.Piece, Player.RequirementMode.IsKnown))
                {
                    stats.RecipeUnknown++;
                    continue;
                }

                bool stationInRange = StationInRange(player, c);
                if (PluginConfig.Filter.Value != RecommendationFilter.KnownOnly && !stationInRange)
                {
                    stats.StationOutOfRange++;
                    continue;
                }

                if (PluginConfig.Filter.Value == RecommendationFilter.Buildable &&
                    !player.HaveRequirements(c.Piece, Player.RequirementMode.CanBuild))
                {
                    stats.MaterialsShort++;
                    continue;
                }

                stats.Candidates++;

                var rec = new Recommendation
                {
                    Kind = ComfortGroups.Stacks(c.Group)
                        ? RecommendationKind.Stacking
                        : (counted.ContainsKey(c.Group)
                            ? RecommendationKind.Upgrade
                            : RecommendationKind.NewGroup),
                    DisplayName = c.DisplayName,
                    PrefabName = c.PrefabName,
                    Group = c.Group,
                    GroupKnown = c.GroupKnown,
                    Gain = gain,
                    CostScore = c.CostScore,
                    StationInRange = stationInRange
                };

                if (rec.Kind == RecommendationKind.Upgrade)
                    winners.TryGetValue(c.Group, out rec.Replaces);

                FillMaterials(player, c.Piece, rec);

                if (c.Group == Piece.ComfortGroup.None)
                {
                    bestUngrouped.Add(rec);
                }
                else
                {
                    Recommendation existing;
                    if (!bestPerGroup.TryGetValue(c.Group, out existing) || Compare(rec, existing) < 0)
                        bestPerGroup[c.Group] = rec;
                }
            }

            results.AddRange(bestPerGroup.Values);

            // Ungrouped pieces stack, so several genuinely can be recommended at once — but keep
            // it to the best few rather than every banner in the game.
            bestUngrouped.Sort(Compare);
            for (int i = 0; i < bestUngrouped.Count && i < 3; i++)
                results.Add(bestUngrouped[i]);
        }

        /// <summary>
        /// Grouped pieces must beat the incumbent, so they are worth the difference. Ungrouped
        /// pieces stack, so they are worth their full value unless that exact name already counts.
        /// </summary>
        private static int GainFor(Piece.ComfortGroup group, int comfort, string nameToken,
            Dictionary<Piece.ComfortGroup, int> counted, HashSet<string> countedNames)
        {
            // Deduplicate on the raw token: the game's duplicate-name clause compares m_name, and
            // two distinct tokens can localize to the same string (or fail to localize at all).
            if (ComfortGroups.Stacks(group))
                return countedNames.Contains(nameToken) ? 0 : comfort;

            int current;
            counted.TryGetValue(group, out current);
            return Mathf.Max(0, comfort - current);
        }

        private static bool StationInRange(Player player, PieceCatalog.Entry c)
        {
            if (string.IsNullOrEmpty(c.StationName))
                return true;

            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
                return true;

            return CraftingStation.HaveBuildStationInRange(c.StationName, player.transform.position) != null;
        }

        private static void FillMaterials(Player player, Piece piece, Recommendation rec)
        {
            rec.HaveAllMaterials = true;

            if (piece.m_resources == null)
                return;

            Inventory inv = player.GetInventory();

            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                Piece.Requirement req = piece.m_resources[i];
                if (req == null || req.m_resItem == null || req.m_amount <= 0)
                    continue;

                string token = PieceCatalog.ItemToken(req);
                int have = inv != null && token != null ? inv.CountItems(token) : 0;

                rec.Materials.Add(new MaterialLine
                {
                    DisplayName = token != null ? Localization.instance.Localize(token) : "?",
                    Amount = req.m_amount,
                    Have = have
                });

                if (have < req.m_amount)
                    rec.HaveAllMaterials = false;
            }
        }

        private static int Compare(Recommendation a, Recommendation b)
        {
            // Free gains always outrank anything that costs materials.
            if (a.Free != b.Free)
                return a.Free ? -1 : 1;

            // A roof is structural: it unlocks every piece at once.
            bool aShelter = a.Kind == RecommendationKind.Shelter;
            bool bShelter = b.Kind == RecommendationKind.Shelter;
            if (aShelter != bShelter)
                return aShelter ? -1 : 1;

            if (a.Gain != b.Gain)
                return b.Gain.CompareTo(a.Gain);

            if (!Mathf.Approximately(a.CostScore, b.CostScore))
                return a.CostScore.CompareTo(b.CostScore);

            if (a.HaveAllMaterials != b.HaveAllMaterials)
                return a.HaveAllMaterials ? -1 : 1;

            if (a.StationInRange != b.StationInRange)
                return a.StationInRange ? -1 : 1;

            if (a.Materials.Count != b.Materials.Count)
                return a.Materials.Count.CompareTo(b.Materials.Count);

            // Last resort, so the list never reorders between identical scans.
            return string.CompareOrdinal(a.DisplayName, b.DisplayName);
        }
    }
}
