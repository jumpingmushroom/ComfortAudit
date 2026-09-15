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
        }

        private static List<Entry> _entries;

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

            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject go = prefabs[i];
                if (go == null)
                    continue;

                var piece = go.GetComponent<Piece>();
                if (piece == null || piece.m_comfort <= 0)
                    continue;

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
                    StationName = piece.m_craftingStation != null ? piece.m_craftingStation.m_name : null
                });
            }

            _entries = list;
            ComfortAuditPlugin.Log.LogInfo(
                "catalogued " + list.Count + " comfort pieces from " + prefabs.Count + " prefabs");

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
