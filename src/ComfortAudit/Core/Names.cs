using System.Text;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Display names for pieces, with a fallback for missing translations.
    ///
    /// Localization.Translate returns "[token]" verbatim when a token has no entry for the current
    /// language — vanilla 1.0.12 ships at least one comfort piece in that state
    /// (ArmorStand_Male / $piece_armorstand_male). Rendering that raw in the panel looks broken,
    /// so an untranslated token falls back to a readable form of the prefab name.
    /// </summary>
    public static class Names
    {
        public static string Display(Piece piece, string prefabName)
        {
            if (piece == null)
                return Prettify(prefabName);

            if (string.IsNullOrEmpty(piece.m_name))
                return Prettify(prefabName);

            string localized = Localization.instance != null
                ? Localization.instance.Localize(piece.m_name)
                : piece.m_name;

            return IsUntranslated(localized) ? Prettify(prefabName) : localized;
        }

        /// <summary>A token the game could not translate comes back wrapped in square brackets.</summary>
        private static bool IsUntranslated(string localized)
        {
            return string.IsNullOrEmpty(localized)
                   || (localized.Length > 1 && localized[0] == '[' && localized[localized.Length - 1] == ']');
        }

        /// <summary>"ArmorStand_Male" -> "Armor Stand Male"; "piece_maypole" -> "Maypole".</summary>
        public static string Prettify(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return "?";

            string s = prefabName;
            if (s.StartsWith("piece_"))
                s = s.Substring(6);

            var sb = new StringBuilder(s.Length + 8);
            bool newWord = true;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '_' || c == '-')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                        sb.Append(' ');
                    newWord = true;
                    continue;
                }

                // Split CamelCase, but not runs of capitals or digit groups.
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]) && sb.Length > 0 &&
                    sb[sb.Length - 1] != ' ')
                {
                    sb.Append(' ');
                    newWord = true;
                }

                sb.Append(newWord ? char.ToUpperInvariant(c) : c);
                newWord = false;
            }

            return sb.ToString().Trim();
        }
    }
}
