using System.Collections.Generic;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Every comfort-bearing piece prefab known to this install, built once from ZNetScene.
    ///
    /// Built at runtime rather than shipped as a table, because the per-piece comfort values live
    /// in Unity prefab data rather than in the game assembly — and because scanning the live
    /// prefab list picks up pieces added by other mods for free.
    /// </summary>
    public static class PieceCatalog
    {
        public sealed class Entry
        {
            public Piece Piece;
            public string PrefabName;
            public string DisplayName;
            public string NameToken;
            public Piece.ComfortGroup Group;
            public bool GroupKnown;
            public int Comfort;
            public float CostScore;
            public string StationName;

            /// <summary>
            /// Listed in a build tool's piece table (ObjectDB.GetAllBuildPieces). A prefab with
            /// comfort that no hammer offers cannot be recommended, whatever its recipe says.
            /// </summary>
            public bool InBuildMenu;

            /// <summary>
            /// Piece.m_enabled. The maypole and yule tree ship disabled and are switched on by
            /// the seasonal group, so a disabled piece is only buildable while in season.
            /// </summary>
            public bool Enabled;
        }

        private static List<Entry> _entries;

        /// <summary>
        /// Whether the hammer would actually offer this piece to <paramref name="player"/> right
        /// now, ignoring recipe knowledge and materials. Mirrors PieceTable.UpdateAvailable.
        /// </summary>
        public static bool Available(Entry e, Player player)
        {
            if (!e.InBuildMenu)
                return false;

            if (e.Enabled)
                return true;

            return player != null
                   && player.CurrentSeason != null
                   && e.Piece != null
                   && player.CurrentSeason.Pieces.Contains(e.Piece.gameObject);
        }

        /// <summary>
        /// Prefab names of every piece in any build tool's table, or null when ObjectDB cannot
        /// answer, in which case nothing is filtered rather than everything.
        ///
        /// Walks the tables directly rather than calling ObjectDB.GetAllBuildPieces: that helper
        /// drops tables without m_canRemovePieces and pieces flagged m_canRockJade, and caches
        /// its first answer for the session — which excluded the armour stands and the blackwood
        /// bench, all of them plainly in the hammer.
        /// </summary>
        private static HashSet<string> BuildMenuNames()
        {
            if (ObjectDB.instance == null || ObjectDB.instance.m_items == null)
                return null;

            var names = new HashSet<string>();
            var tables = new List<string>();
            List<GameObject> items = ObjectDB.instance.m_items;
            TableEntries.Clear();

            for (int i = 0; i < items.Count; i++)
            {
                GameObject item = items[i];
                if (item == null)
                    continue;

                var drop = item.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                    continue;

                PieceTable table = drop.m_itemData.m_shared.m_buildPieces;
                if (table == null || table.m_pieces == null)
                    continue;

                int added = 0;
                for (int j = 0; j < table.m_pieces.Count; j++)
                {
                    GameObject go = table.m_pieces[j];
                    if (go == null)
                        continue;
                    if (names.Add(go.name))
                        added++;
                    TableEntries.Add(new KeyValuePair<GameObject, string>(go, item.name));
                }
                tables.Add(item.name + ":" + table.m_pieces.Count + (added < table.m_pieces.Count ? "(+" + added + ")" : ""));
            }

            if (names.Count == 0)
                return null;

            ComfortAuditPlugin.Log.LogInfo("build tables: " + string.Join(", ", tables.ToArray()));
            return names;
        }

        /// <summary>Every (prefab, tool) pair seen in the tables, kept for the exclusion report.</summary>
        private static readonly List<KeyValuePair<GameObject, string>> TableEntries =
            new List<KeyValuePair<GameObject, string>>();

        /// <summary>
        /// For a comfort piece no table lists by prefab name, say what the tables hold that
        /// looks like it: same name ignoring case, same m_name token, or the same prefab object
        /// under a different name. Diagnoses whether the filter or the install is at odds.
        /// </summary>
        private static string NearestTableEntries(Entry e)
        {
            var hits = new List<string>();
            for (int i = 0; i < TableEntries.Count; i++)
            {
                GameObject go = TableEntries[i].Key;
                string why = null;

                if (e.Piece != null && ReferenceEquals(go, e.Piece.gameObject))
                    why = "same object";
                else if (string.Equals(go.name, e.PrefabName, System.StringComparison.OrdinalIgnoreCase))
                    why = "name differs by case";
                else
                {
                    var p = go.GetComponent<Piece>();
                    if (p != null && !string.IsNullOrEmpty(e.NameToken) && p.m_name == e.NameToken)
                        why = "same token";
                }

                if (why != null)
                    hits.Add(go.name + " in " + TableEntries[i].Value + " (" + why + ")");
            }

            return hits.Count == 0 ? "nothing similar in any table" : string.Join("; ", hits.ToArray());
        }

        public static bool Ready => _entries != null;
        public static int Count => _entries == null ? 0 : _entries.Count;

        public static void Invalidate()
        {
            _entries = null;
            Ceiling.Invalidate();
        }

        public static List<Entry> Entries()
        {
            if (_entries != null)
                return _entries;

            if (ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null)
                return null;

            var list = new List<Entry>(128);
            List<GameObject> prefabs = ZNetScene.instance.m_prefabs;
            HashSet<string> menu = BuildMenuNames();
            var notInMenu = new List<string>();
            var disabled = new List<string>();

            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject go = prefabs[i];
                if (go == null)
                    continue;

                var piece = go.GetComponent<Piece>();
                if (piece == null || piece.m_comfort <= 0)
                    continue;

                bool inMenu = menu == null || menu.Contains(go.name);
                if (!inMenu) notInMenu.Add(go.name);
                if (!piece.m_enabled) disabled.Add(go.name);

                list.Add(new Entry
                {
                    Piece = piece,
                    PrefabName = go.name,
                    DisplayName = Names.Display(piece, go.name),
                    NameToken = piece.m_name,
                    Group = piece.m_comfortGroup,
                    GroupKnown = ComfortGroups.IsKnown(piece.m_comfortGroup),
                    Comfort = piece.m_comfort,
                    CostScore = ScoreCost(piece),
                    StationName = piece.m_craftingStation != null ? piece.m_craftingStation.m_name : null,
                    InBuildMenu = inMenu,
                    Enabled = piece.m_enabled
                });
            }

            _entries = list;
            ComfortAuditPlugin.Log.LogInfo(
                "catalogued " + list.Count + " comfort pieces from " + prefabs.Count + " prefabs"
                + " (" + notInMenu.Count + " in no build menu, " + disabled.Count + " disabled/seasonal"
                + (menu == null ? ", build menu unavailable" : "") + ")");

            // Named, so an over-eager filter is visible in the log rather than as a silently
            // missing recommendation.
            if (notInMenu.Count > 0)
            {
                ComfortAuditPlugin.Log.LogInfo("  not in any build menu: " + string.Join(", ", notInMenu.ToArray()));
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].InBuildMenu)
                        ComfortAuditPlugin.Log.LogInfo("    " + list[i].PrefabName + " [" + list[i].NameToken + "]: " + NearestTableEntries(list[i]));
                }
            }
            if (disabled.Count > 0)
                ComfortAuditPlugin.Log.LogInfo("  disabled unless in season: " + string.Join(", ", disabled.ToArray()));

            return _entries;
        }

        private static float ScoreCost(Piece piece)
        {
            if (piece.m_resources == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                Piece.Requirement req = piece.m_resources[i];

                // The game itself skips these, so mirror it rather than dereferencing.
                if (req == null || req.m_resItem == null || req.m_amount <= 0)
                    continue;

                total += req.m_amount * CostTable.Weight(ItemToken(req));
            }

            return total;
        }

        public static string ItemToken(Piece.Requirement req)
        {
            if (req == null || req.m_resItem == null || req.m_resItem.m_itemData == null ||
                req.m_resItem.m_itemData.m_shared == null)
                return null;

            return req.m_resItem.m_itemData.m_shared.m_name;
        }

        /// <summary>Number of distinct materials, used as a late tie-break.</summary>
        public static int MaterialCount(Piece piece)
        {
            if (piece.m_resources == null)
                return 0;

            int n = 0;
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                Piece.Requirement req = piece.m_resources[i];
                if (req != null && req.m_resItem != null && req.m_amount > 0)
                    n++;
            }
            return n;
        }
    }
}
