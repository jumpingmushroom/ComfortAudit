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

            // Which groups are filled, and by what — for labelling a candidate as an upgrade or
            // a new group. Gains themselves come from the walk, not from these.
            var counted = new Dictionary<Piece.ComfortGroup, int>();
            var winners = new Dictionary<Piece.ComfortGroup, string>();

            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                if (!e.Contributing)
                    continue;

                int existing;
                if (!counted.TryGetValue(e.Group, out existing) || e.Comfort > existing)
                {
                    counted[e.Group] = e.Comfort;
                    winners[e.Group] = e.DisplayName;
                }
            }

            var results = new List<Recommendation>();

            // Unsheltered, nothing but a roof can move the number: lighting a fire or placing a
            // better chair changes comfort by exactly zero until there is a roof, so advertising
            // those gains — and ranking "light it" above the roof — described a state the player
            // is not in. The roof is the only recommendation until it is built.
            if (!snap.InShelter)
            {
                AddShelter(snap, results);
                snap.Recommendations = results;
                return;
            }

            // The pieces in range as walk input, so every candidate's gain is the game's own
            // algorithm run with it added — the adjacency dedup makes per-group maxima and a set
            // of counted names wrong whenever equal names are involved.
            BaseItems.Clear();
            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                BaseItems.Add(new ComfortWalk.Item
                {
                    Group = e.Group,
                    Comfort = e.Comfort,
                    NameToken = e.NameToken,
                    Source = i
                });
            }
            int baseline = ComfortWalk.Total(BaseItems);

            AddFreeGains(snap, results);
            AddPieceUpgrades(snap, player, catalog, results, counted, winners, stats, baseline);

            results.Sort(Compare);

            int max = Mathf.Max(1, PluginConfig.MaxRecommendations.Value);
            if (results.Count > max)
                results.RemoveRange(max, results.Count - max);

            snap.Recommendations = results;
        }

        private static readonly List<ComfortWalk.Item> BaseItems = new List<ComfortWalk.Item>(128);

        /// <summary>
        /// Pieces already placed but switched off. GetComfort() returns 0 for them, so they cost
        /// nothing to "build" — you just light them. The gain is the scanner's walk with the piece
        /// lit, so it is what lighting actually adds, not the piece's designed value.
        /// </summary>
        private static void AddFreeGains(ComfortSnapshot snap, List<Recommendation> results)
        {
            var seen = new HashSet<string>();

            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                if (!e.Inactive || e.RawComfort <= 0)
                    continue;

                int gain = e.LightGain;
                if (gain <= 0)
                    continue;

                // Keyed on the raw token, checked after the gain: two unlit copies of one piece
                // are one suggestion, but a gainless piece must not hide a useful namesake.
                if (!seen.Add(e.NameToken ?? e.DisplayName))
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
            Dictionary<Piece.ComfortGroup, int> counted,
            Dictionary<Piece.ComfortGroup, string> winners, RecommendationStats stats, int baseline)
        {
            // Only the best candidate per group is worth showing; five ways to fix the Chair
            // group is noise, not advice.
            var bestPerGroup = new Dictionary<Piece.ComfortGroup, Recommendation>();
            var bestUngrouped = new List<Recommendation>();

            for (int i = 0; i < catalog.Count; i++)
            {
                PieceCatalog.Entry c = catalog[i];

                // Not offered by any build tool right now (no table lists it, or it is a
                // seasonal piece out of season). Known recipe or not, the hammer will not
                // place it, so it is not advice.
                if (!PieceCatalog.Available(c, player))
                {
                    stats.NotInMenu++;
                    continue;
                }

                int gain = ComfortWalk.TotalWith(BaseItems, new ComfortWalk.Item
                {
                    Group = c.Group,
                    Comfort = c.Comfort,
                    NameToken = c.NameToken,
                    Source = -1
                }) - baseline;
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
