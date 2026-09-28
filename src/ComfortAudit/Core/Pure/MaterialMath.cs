using System;

namespace ComfortAudit.Core.Pure
{
    /// <summary>Whether a suggestion's material can be covered, and from where.</summary>
    public enum MaterialState
    {
        /// <summary>Enough in the inventory — what building actually needs.</summary>
        Enough,

        /// <summary>
        /// Not enough carried, but enough once nearby chests are counted. Vanilla cannot build
        /// from a chest, so this is "go and fetch it", not "ready".
        /// </summary>
        EnoughWithChests,

        /// <summary>Not enough even with the chests.</summary>
        Short
    }

    /// <summary>Which count, if any, is worth printing after a material.</summary>
    public enum MaterialAnnotation
    {
        None,

        /// <summary>"(have N)" — some carried, not enough, none in chests.</summary>
        Have,

        /// <summary>"(N + M in chests)" — chests hold some of it.</summary>
        HaveAndChests
    }

    /// <summary>
    /// Material have/need rules, kept free of Unity so they can be unit-tested. The panel only
    /// decides colours and wording from these answers.
    /// </summary>
    public static class MaterialMath
    {
        public static MaterialState Classify(int amount, int have, int inChests)
        {
            if (have >= amount)
                return MaterialState.Enough;

            if (have + Math.Max(0, inChests) >= amount)
                return MaterialState.EnoughWithChests;

            return MaterialState.Short;
        }

        /// <summary>
        /// A count is printed only when it tells the player something: never when the inventory
        /// already covers it, and not "(have 0)" — red already says that.
        /// </summary>
        public static MaterialAnnotation Annotate(int amount, int have, int inChests)
        {
            if (have >= amount)
                return MaterialAnnotation.None;

            if (inChests > 0)
                return MaterialAnnotation.HaveAndChests;

            return have > 0 ? MaterialAnnotation.Have : MaterialAnnotation.None;
        }
    }
}
