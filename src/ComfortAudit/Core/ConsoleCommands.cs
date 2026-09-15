using System.Collections.Generic;
using System.Text;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Diagnostics for verifying the mod's own data against a live install.
    ///
    /// The cost weights in costs.json are keyed by item localization tokens, which are prefab
    /// data rather than anything in the game assembly — so they cannot be verified by inspection.
    /// "comfortaudit costs" lists the tokens this install actually uses, which makes the table
    /// checkable in one command instead of trusted on faith.
    /// </summary>
    public static class ConsoleCommands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;

            _registered = true;

            new Terminal.ConsoleCommand("comfortaudit",
                "ComfortAudit diagnostics: pieces | costs | groups",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "help";

                    switch (sub)
                    {
                        case "pieces": DumpPieces(args.Context); break;
                        case "costs": DumpCosts(args.Context); break;
                        case "groups": DumpGroups(args.Context); break;
                        case "now": DumpNow(args.Context); break;
                        default:
                            args.Context.AddString("comfortaudit pieces  - every comfort piece prefab, group, comfort, cost");
                            args.Context.AddString("comfortaudit costs   - material tokens in use, and which have no weight");
                            args.Context.AddString("comfortaudit groups  - comfort groups and how many pieces each has");
                            args.Context.AddString("comfortaudit now     - live breakdown here and now, also written to the log");
                            break;
                    }
                });
        }

        /// <summary>
        /// Forces a fresh scan and prints the live attribution. Also goes to the BepInEx log, so
        /// it can be read back without anyone having to retype what was on screen.
        /// </summary>
        private static void DumpNow(Terminal ctx)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                ctx.AddString("ComfortAudit: no local player.");
                return;
            }

            SnapshotService.Clear();
            Model.ComfortSnapshot snap = SnapshotService.Get(0f);

            if (!snap.Valid)
            {
                ctx.AddString("ComfortAudit: no valid snapshot.");
                return;
            }

            ctx.AddString(string.Format("Comfort {0} (game says {1}), shelter {2}, cover {3:0.00}, {4} piece(s) in range",
                snap.ComfortLevel, snap.VanillaComfortLevel, snap.InShelter, snap.CoverPercentage, snap.PieceCount));

            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                Model.PieceEntry e = snap.Pieces[i];
                ctx.AddString(string.Format("  {0,-12} {1,-20} {2,-16} {3}",
                    ComfortGroups.Name(e.Group), e.DisplayName, e.Status,
                    e.Status == Model.PieceStatus.Counted ? "+" + e.Comfort : "beaten by " + (e.ShadowedBy ?? "?")));
            }

            for (int i = 0; i < snap.Recommendations.Count; i++)
            {
                Model.Recommendation r = snap.Recommendations[i];
                ctx.AddString(string.Format("  next: +{0} {1} [{2}]", r.Gain, r.DisplayName, r.Kind));
            }

            Diagnostics.ReportBreakdown(snap, ComfortAuditPlugin.Log);
        }

        private static List<PieceCatalog.Entry> Catalog(Terminal ctx)
        {
            List<PieceCatalog.Entry> entries = PieceCatalog.Entries();
            if (entries == null)
                ctx.AddString("ComfortAudit: no catalogue yet - load a world first.");
            return entries;
        }

        private static void DumpPieces(Terminal ctx)
        {
            List<PieceCatalog.Entry> entries = Catalog(ctx);
            if (entries == null) return;

            entries.Sort(delegate (PieceCatalog.Entry a, PieceCatalog.Entry b)
            {
                if (a.Group != b.Group) return a.Group.CompareTo(b.Group);
                if (a.Comfort != b.Comfort) return b.Comfort.CompareTo(a.Comfort);
                return string.CompareOrdinal(a.PrefabName, b.PrefabName);
            });

            ctx.AddString("ComfortAudit: " + entries.Count + " comfort pieces");
            for (int i = 0; i < entries.Count; i++)
            {
                PieceCatalog.Entry e = entries[i];
                ctx.AddString(string.Format("{0,-10} {1,2}  {2,-28} cost {3,6:0.0}  [{4}]",
                    ComfortGroups.Name(e.Group), e.Comfort, e.DisplayName, e.CostScore, e.PrefabName));
            }
        }

        private static void DumpCosts(Terminal ctx)
        {
            List<PieceCatalog.Entry> entries = Catalog(ctx);
            if (entries == null) return;

            var used = new Dictionary<string, int>();

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

            ctx.AddString("ComfortAudit: " + used.Count + " material tokens used by comfort pieces ("
                          + CostTable.Count + " weights loaded)");

            var missing = new List<string>();
            foreach (KeyValuePair<string, int> kv in used)
            {
                float w = CostTable.Weight(kv.Key);
                bool known = CostTable.HasWeight(kv.Key);
                ctx.AddString(string.Format("{0,-28} weight {1,5:0.0} {2}  used by {3} piece(s)",
                    kv.Key, w, known ? "       " : "(default)", kv.Value));
                if (!known) missing.Add(kv.Key);
            }

            if (missing.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("No weight for: ");
                for (int i = 0; i < missing.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(missing[i]);
                }
                ctx.AddString(sb.ToString());
                ctx.AddString("These score 1.0, so ranking falls back to raw item count for them.");
            }
        }

        private static void DumpGroups(Terminal ctx)
        {
            List<PieceCatalog.Entry> entries = Catalog(ctx);
            if (entries == null) return;

            var perGroup = new Dictionary<Piece.ComfortGroup, int>();
            var maxComfort = new Dictionary<Piece.ComfortGroup, int>();

            for (int i = 0; i < entries.Count; i++)
            {
                PieceCatalog.Entry e = entries[i];

                int n;
                perGroup.TryGetValue(e.Group, out n);
                perGroup[e.Group] = n + 1;

                int m;
                if (!maxComfort.TryGetValue(e.Group, out m) || e.Comfort > m)
                    maxComfort[e.Group] = e.Comfort;
            }

            foreach (KeyValuePair<Piece.ComfortGroup, int> kv in perGroup)
            {
                ctx.AddString(string.Format("{0,-12} {1,3} pieces, best comfort {2}",
                    ComfortGroups.Name(kv.Key), kv.Value, maxComfort[kv.Key]));
            }
        }
    }
}
