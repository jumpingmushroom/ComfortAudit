using System;
using System.Collections.Generic;
using ComfortAudit.Model;

namespace ComfortAudit.Core
{
    /// <summary>
    /// The one implementation of SE_Rested's comfort algorithm.
    ///
    /// Both the live scan and the placement preview run through here. Keeping a single copy
    /// matters more than usual: reproducing the game's sort-then-adjacency-walk exactly is this
    /// mod's whole claim to correctness, and a second copy written for the preview would be free
    /// to drift away from it.
    /// </summary>
    public static class ComfortWalk
    {
        /// <summary>A candidate for the walk. Comfort is the *effective* value, i.e. GetComfort().</summary>
        public struct Item
        {
            public Piece.ComfortGroup Group;
            public int Comfort;

            /// <summary>The raw m_name token. The game compares these, not localized names.</summary>
            public string NameToken;

            /// <summary>Caller's index into its own collection; -1 for a hypothetical piece.</summary>
            public int Source;
        }

        public struct Outcome
        {
            public int Source;
            public PieceStatus Status;

            /// <summary>Source index of the item that shadowed this one, or -1.</summary>
            public int ShadowedBySource;
        }

        /// <summary>
        /// Replica of SE_Rested.PieceComfortSort: group ascending, comfort DESCENDING, then
        /// m_name descending. The descending comfort is what makes the adjacency walk below
        /// behave like "highest per group".
        /// </summary>
        public static readonly Comparison<Item> Sort = delegate (Item x, Item y)
        {
            if (x.Group != y.Group)
                return x.Group.CompareTo(y.Group);

            if (x.Comfort != y.Comfort)
                return ((float)y.Comfort).CompareTo((float)x.Comfort);

            return string.CompareOrdinal(y.NameToken ?? string.Empty, x.NameToken ?? string.Empty);
        };

        /// <summary>
        /// Sorts <paramref name="items"/> in place and returns the resulting comfort level.
        /// Optionally reports what happened to each item.
        /// </summary>
        public static int Run(List<Item> items, bool inShelter, List<Outcome> outcomes = null)
        {
            if (outcomes != null)
                outcomes.Clear();

            int total = 1;
            if (!inShelter)
            {
                // Unsheltered the game never enters the loop: comfort is a hard 1 and no
                // furniture counts. Still report the items so callers can show what *would* count.
                if (outcomes != null)
                    ReportOnly(items, outcomes);
                return total;
            }

            total++; // shelter itself is +1
            items.Sort(Sort);

            for (int i = 0; i < items.Count; i++)
            {
                Item cur = items[i];

                if (i > 0)
                {
                    Item prev = items[i - 1];

                    if (cur.Group != Piece.ComfortGroup.None && cur.Group == prev.Group)
                    {
                        Add(outcomes, cur.Source, PieceStatus.ShadowedByGroup, prev.Source);
                        continue;
                    }

                    // Applied regardless of group, so this can fire across a group boundary.
                    if (cur.NameToken == prev.NameToken)
                    {
                        Add(outcomes, cur.Source, PieceStatus.ShadowedByName, prev.Source);
                        continue;
                    }
                }

                Add(outcomes, cur.Source, PieceStatus.Counted, -1);
                total += cur.Comfort;
            }

            return total;
        }

        private static void ReportOnly(List<Item> items, List<Outcome> outcomes)
        {
            items.Sort(Sort);
            for (int i = 0; i < items.Count; i++)
            {
                Item cur = items[i];
                if (i > 0)
                {
                    Item prev = items[i - 1];
                    if (cur.Group != Piece.ComfortGroup.None && cur.Group == prev.Group)
                    {
                        Add(outcomes, cur.Source, PieceStatus.ShadowedByGroup, prev.Source);
                        continue;
                    }
                    if (cur.NameToken == prev.NameToken)
                    {
                        Add(outcomes, cur.Source, PieceStatus.ShadowedByName, prev.Source);
                        continue;
                    }
                }
                Add(outcomes, cur.Source, PieceStatus.Counted, -1);
            }
        }

        private static void Add(List<Outcome> outcomes, int source, PieceStatus status, int by)
        {
            if (outcomes == null)
                return;

            outcomes.Add(new Outcome { Source = source, Status = status, ShadowedBySource = by });
        }
    }
}
