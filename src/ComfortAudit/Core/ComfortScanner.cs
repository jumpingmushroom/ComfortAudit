using System.Collections.Generic;
using ComfortAudit.Model;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Rebuilds SE_Rested.CalculateComfortLevel locally so every piece can be attributed.
    ///
    /// We deliberately do not patch the game's method: patching would put this attribution work
    /// on the shared 2 s tick for no benefit, and would fight any other comfort mod. Re-deriving
    /// also lets us cross-check the vanilla result and report disagreement instead of silently
    /// inheriting it.
    /// </summary>
    public static class ComfortScanner
    {
        /// <summary>Hardcoded 10f in SE_Rested.GetNearbyComfortPieces, matching c_ComfortRadius.</summary>
        public const float ComfortRadius = 10f;

        private static readonly List<Piece> Buffer = new List<Piece>(128);
        private static readonly List<ComfortWalk.Item> Items = new List<ComfortWalk.Item>(128);
        private static readonly List<ComfortWalk.Outcome> Outcomes = new List<ComfortWalk.Outcome>(128);

        public static ComfortSnapshot Scan(Player player)
        {
            var snap = new ComfortSnapshot { Time = Time.time };

            if (player == null || player.m_nview == null)
                return snap;

            Vector3 pos = player.transform.position;

            snap.Gate = RestGate.Evaluate(player);
            snap.InShelter = player.InShelter();

            // Read the player's own cached cover rather than recomputing it. Player.UpdateCover
            // casts from GetCenterPoint() (chest height) once a second, and InShelter() tests
            // exactly these two fields — so recomputing from transform.position (the feet)
            // produced a percentage that disagreed with the shelter verdict shown beside it.
            // Using the game's values is consistent by construction, and saves 17 raycasts
            // per scan.
            snap.CoverPercentage = player.m_coverPercentage;
            snap.UnderRoof = player.m_underRoof;

            Buffer.Clear();
            Piece.GetAllComfortPiecesInRadius(pos, ComfortRadius, Buffer);

            // Build the walk input, keeping our index so outcomes can be mapped back.
            Items.Clear();
            for (int i = 0; i < Buffer.Count; i++)
            {
                Piece piece = Buffer[i];
                snap.Pieces.Add(Describe(piece, pos));

                Items.Add(new ComfortWalk.Item
                {
                    Group = piece.m_comfortGroup,
                    Comfort = piece.GetComfort(),
                    NameToken = piece.m_name,
                    Source = i
                });
            }

            Outcomes.Clear();
            int total = ComfortWalk.Run(Items, snap.InShelter, Outcomes);

            for (int i = 0; i < Outcomes.Count; i++)
            {
                ComfortWalk.Outcome o = Outcomes[i];
                if (o.Source < 0 || o.Source >= snap.Pieces.Count)
                    continue;

                PieceEntry entry = snap.Pieces[o.Source];
                entry.Status = o.Status;

                if (o.ShadowedBySource >= 0 && o.ShadowedBySource < snap.Pieces.Count)
                    entry.ShadowedBy = snap.Pieces[o.ShadowedBySource].DisplayName;
            }

            snap.ComfortLevel = total;
            snap.VanillaComfortLevel = player.GetComfortLevel();

            // The game refreshes its cache only every 2 s (Player.UpdateBaseValue), so a
            // difference is normal right after a change. Flag it only once that has settled.
            snap.Mismatch = snap.VanillaComfortLevel > 0
                            && snap.ComfortLevel != snap.VanillaComfortLevel
                            && Time.time - LastChangeTime > 2.5f;

            if (snap.ComfortLevel != LastComfort)
            {
                LastComfort = snap.ComfortLevel;
                LastChangeTime = Time.time;
            }

            snap.PotentialIfSheltered = snap.InShelter
                ? snap.ComfortLevel
                : SE_Rested.CalculateComfortLevel(true, pos);

            ComputeMissingGroups(snap);
            RestedMath.Fill(snap);

            snap.Valid = true;
            Recommender.Fill(snap, player);

            Ceiling.Result ceiling = Ceiling.Get(player);
            snap.CeilingValid = ceiling.Valid;
            snap.CeilingUnlocked = ceiling.Unlocked;
            snap.CeilingAll = ceiling.All;
            return snap;
        }

        private static int LastComfort = -1;
        private static float LastChangeTime;

        private static void ComputeMissingGroups(ComfortSnapshot snap)
        {
            var present = new HashSet<Piece.ComfortGroup>();
            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                if (e.Contributing)
                    present.Add(e.Group);
            }

            for (int i = 0; i < ComfortGroups.Real.Length; i++)
            {
                if (!present.Contains(ComfortGroups.Real[i]))
                    snap.MissingGroups.Add(ComfortGroups.Real[i]);
            }
        }

        private static PieceEntry Describe(Piece piece, Vector3 from)
        {
            int comfort = piece.GetComfort();

            return new PieceEntry
            {
                PrefabName = piece.gameObject != null ? piece.gameObject.name : "?",
                DisplayName = DisplayName(piece),
                NameToken = piece.m_name,
                Group = piece.m_comfortGroup,
                GroupKnown = ComfortGroups.IsKnown(piece.m_comfortGroup),
                Comfort = comfort,
                RawComfort = piece.m_comfort,
                // GetComfort() returns 0 when m_comfortObject is switched off — an unlit fire.
                Inactive = comfort == 0 && piece.m_comfort > 0,
                Distance = Vector3.Distance(from, piece.transform.position),
                Icon = piece.m_icon
            };
        }

        private static string DisplayName(Piece piece)
        {
            if (piece == null)
                return "?";

            return Names.Display(piece, piece.gameObject != null ? piece.gameObject.name : null);
        }
    }
}
