using System.Collections.Generic;
using ComfortAudit.Model;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// What the currently-held build piece would be worth if placed where the ghost is.
    ///
    /// Cheap because SE_Rested exposes the calculation as a public static taking an arbitrary
    /// position, and because the walk is shared with the live scan — the preview is the same
    /// algorithm over the same pieces plus one hypothetical entry.
    ///
    /// Note the ghost is on the "ghost" layer, which Piece.GetAllComfortPiecesInRadius filters
    /// out, so it never double-counts itself.
    /// </summary>
    public static class PlacementPreview
    {
        private static readonly List<ComfortWalk.Item> Items = new List<ComfortWalk.Item>(128);
        private static readonly List<Piece> Buffer = new List<Piece>(128);
        private static readonly List<ComfortWalk.Outcome> Outcomes = new List<ComfortWalk.Outcome>(128);

        /// <summary>Index used for the hypothetical piece, distinct from any real piece index.</summary>
        private const int GhostSource = -2;

        public static PlacementPreviewResult Compute(Player player, ComfortSnapshot snap)
        {
            var result = new PlacementPreviewResult();

            if (player == null || snap == null || !snap.Valid)
                return result;

            GameObject ghost = player.m_placementGhost;
            if (ghost == null || !ghost.activeInHierarchy)
                return result;

            var piece = ghost.GetComponent<Piece>();
            if (piece == null || piece.m_comfort <= 0)
                return result;   // holding something, but not a comfort piece

            result.Active = true;
            result.DisplayName = Names.Display(piece, ghost.name);
            result.Group = piece.m_comfortGroup;
            result.GroupKnown = ComfortGroups.IsKnown(piece.m_comfortGroup);

            // The piece's designed value: a hearth ghost is unlit, so GetComfort() would read 0,
            // but what the player wants to know is what the piece is worth once in use.
            result.PieceComfort = piece.m_comfort;

            Vector3 playerPos = player.transform.position;
            Vector3 ghostPos = ghost.transform.position;
            result.Distance = Vector3.Distance(playerPos, ghostPos);

            // Comfort is always evaluated at the resting spot, so a piece only helps if it lands
            // within the radius of where the player is standing.
            result.InRange = result.Distance < ComfortScanner.ComfortRadius;
            if (!result.InRange)
                return result;

            Items.Clear();
            Buffer.Clear();
            Piece.GetAllComfortPiecesInRadius(playerPos, ComfortScanner.ComfortRadius, Buffer);

            for (int i = 0; i < Buffer.Count; i++)
            {
                Piece p = Buffer[i];
                Items.Add(new ComfortWalk.Item
                {
                    Group = p.m_comfortGroup,
                    Comfort = p.GetComfort(),
                    NameToken = p.m_name,
                    Source = i
                });
            }

            // Baseline from the same fresh buffer, not from the cached snapshot: the snapshot can
            // be up to ScanInterval old, so right after placing a chair the walk-with-ghost saw
            // the new chair while the baseline did not, and the next identical ghost reported
            // the same gain again until the snapshot caught up.
            int baseline = ComfortWalk.Run(Items, snap.InShelter);

            Items.Add(new ComfortWalk.Item
            {
                Group = piece.m_comfortGroup,
                Comfort = piece.m_comfort,
                NameToken = piece.m_name,
                Source = GhostSource
            });

            Outcomes.Clear();
            result.NewTotal = ComfortWalk.Run(Items, snap.InShelter, Outcomes);
            result.Delta = result.NewTotal - baseline;

            if (result.Delta <= 0)
                result.BlockedBy = FindBlocker(Buffer);

            return result;
        }

        /// <summary>
        /// When the hypothetical piece adds nothing, name what beat it — that is the question the
        /// player is actually asking when the number reads +0.
        /// </summary>
        private static string FindBlocker(List<Piece> real)
        {
            for (int i = 0; i < Outcomes.Count; i++)
            {
                ComfortWalk.Outcome o = Outcomes[i];
                if (o.Source != GhostSource || o.Status == PieceStatus.Counted)
                    continue;

                int by = o.ShadowedBySource;
                if (by >= 0 && by < real.Count)
                    return Names.Display(real[by], real[by].gameObject != null ? real[by].gameObject.name : null);

                return null;
            }

            return null;
        }
    }
}
