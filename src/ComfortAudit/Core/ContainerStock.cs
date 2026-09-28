using System.Collections.Generic;
using ComfortAudit.Core.Pure;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Materials in chests and carts near the player, for the have/need check.
    ///
    /// Found with a physics query because the game keeps no registry of containers. Refreshed at
    /// most every two seconds and only when asked — the recommender asks during a scan, and
    /// scans run only while the panel is open — so a closed panel costs nothing.
    ///
    /// Reads contents only. Containers reload their inventory from the ZDO once a second on
    /// every client that has them loaded, so this sees what the owner sees without any RPC.
    /// </summary>
    public static class ContainerStock
    {
        private const float RefreshSeconds = 2f;

        private static StockTally _tally = StockTally.Empty;
        private static float _stamp = float.NegativeInfinity;
        private static readonly Collider[] Hits = new Collider[256];
        private static readonly HashSet<Container> Seen = new HashSet<Container>();
        private static int _mask;

        /// <summary>One line per container found on the last refresh, for the console.</summary>
        public static readonly List<string> LastReport = new List<string>();

        public static void Reset()
        {
            _tally = StockTally.Empty;
            _stamp = float.NegativeInfinity;
            LastReport.Clear();
        }

        public static StockTally Get(Player player)
        {
            float radius = PluginConfig.ChestRadius.Value;
            if (player == null || radius <= 0f)
                return StockTally.Empty;

            if (Time.time - _stamp < RefreshSeconds)
                return _tally;

            _stamp = Time.time;
            _tally = Collect(player, radius);
            return _tally;
        }

        private static StockTally Collect(Player player, float radius)
        {
            if (_mask == 0)
                _mask = LayerMask.GetMask("piece", "piece_nonsolid", "vehicle");

            var tally = new StockTally();
            LastReport.Clear();
            Seen.Clear();

            Vector3 origin = player.transform.position;
            long playerId = player.GetPlayerID();

            int n = Physics.OverlapSphereNonAlloc(origin, radius, Hits, _mask, QueryTriggerInteraction.Collide);
            if (n == Hits.Length)
                ComfortAuditPlugin.Log.LogWarning("chest scan hit its collider cap; some chests may be missed");

            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i];
                if (c == null)
                    continue;

                Container container = c.GetComponentInParent<Container>();
                if (container == null || !Seen.Add(container))
                    continue;

                string name = container.gameObject.name;
                string skip = SkipReason(container, playerId);
                if (skip != null)
                {
                    LastReport.Add("  skip  " + name + "  (" + skip + ")");
                    continue;
                }

                Inventory inv = container.GetInventory();
                tally.CountContainer();

                int stacks = 0;
                List<ItemDrop.ItemData> items = inv.GetAllItems();
                for (int k = 0; k < items.Count; k++)
                {
                    ItemDrop.ItemData item = items[k];
                    if (item == null || item.m_shared == null)
                        continue;
                    tally.Add(item.m_shared.m_name, item.m_stack);
                    stacks++;
                }

                LastReport.Add(string.Format("  count {0}  {1:0.0} m, {2} stack(s)",
                    name, Vector3.Distance(origin, container.transform.position), stacks));
            }

            return tally;
        }

        /// <summary>Null when the player could open this container; otherwise why not.</summary>
        private static string SkipReason(Container container, long playerId)
        {
            if (container.GetInventory() == null)
                return "no inventory";

            // A grave holds the dead player's gear, not base stock.
            if (container.GetComponent<TombStone>() != null)
                return "tombstone";

            if (container.m_checkGuardStone &&
                !PrivateArea.CheckAccess(container.transform.position, 0f, false))
                return "warded";

            if (container.m_privacy == Container.PrivacySetting.Private &&
                (container.m_piece == null || container.m_piece.GetCreator() != playerId))
                return "private";

            // Group privacy is unused in vanilla and CheckAccess returns false for it.
            if (container.m_privacy == Container.PrivacySetting.Group)
                return "group";

            return null;
        }
    }
}
