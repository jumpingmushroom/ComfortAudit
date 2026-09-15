using System;
using System.Collections.Generic;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Safe handling of Piece.ComfortGroup. The enum is int-backed and other mods can hold values
    /// outside it, so every display path goes through here rather than calling ToString() directly.
    /// </summary>
    public static class ComfortGroups
    {
        /// <summary>Every vanilla group except None, in enum order.</summary>
        public static readonly Piece.ComfortGroup[] Real =
        {
            Piece.ComfortGroup.Fire,
            Piece.ComfortGroup.Bed,
            Piece.ComfortGroup.Banner,
            Piece.ComfortGroup.Chair,
            Piece.ComfortGroup.Table,
            Piece.ComfortGroup.Carpet,
            Piece.ComfortGroup.Display,
            Piece.ComfortGroup.Decor,
            Piece.ComfortGroup.Garland,
            Piece.ComfortGroup.Lantern,
            Piece.ComfortGroup.Leisure
        };

        private static readonly Dictionary<Piece.ComfortGroup, string> Tokens =
            new Dictionary<Piece.ComfortGroup, string>();

        static ComfortGroups()
        {
            Tokens[Piece.ComfortGroup.None] = "$comfortaudit_group_none";
            for (int i = 0; i < Real.Length; i++)
                Tokens[Real[i]] = "$comfortaudit_group_" + Real[i].ToString().ToLowerInvariant();
        }

        public static bool IsKnown(Piece.ComfortGroup g)
        {
            return Enum.IsDefined(typeof(Piece.ComfortGroup), g);
        }

        /// <summary>
        /// Localized group name. Unknown values from other mods render as "Other" rather than
        /// leaking a raw integer into the UI or throwing.
        /// </summary>
        public static string Name(Piece.ComfortGroup g)
        {
            if (!IsKnown(g))
                return L10n.Strings.Get("$comfortaudit_group_other");

            string token;
            return Tokens.TryGetValue(g, out token)
                ? L10n.Strings.Get(token)
                : L10n.Strings.Get("$comfortaudit_group_other");
        }

        /// <summary>
        /// None-group pieces stack: the game skips the group-equality test for them, leaving only
        /// the duplicate-name clause. They add their full value instead of a delta.
        /// </summary>
        public static bool Stacks(Piece.ComfortGroup g)
        {
            return g == Piece.ComfortGroup.None;
        }
    }
}
