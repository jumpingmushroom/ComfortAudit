namespace ComfortAudit.Core.Pure
{
    /// <summary>
    /// Names and tags for piece icons rendered inline by TextMeshPro. Glyphs are named after the
    /// prefab, which is unique per piece; the tag is looked up by that name across the primary
    /// sprite asset and its fallbacks.
    /// </summary>
    public static class SpriteTags
    {
        /// <summary>
        /// The prefab name with any character that would end the tag's quoted attribute or the
        /// tag itself replaced. Vanilla prefab names never contain these; modded ones might.
        /// </summary>
        public static string GlyphName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return null;

            return prefabName.Replace('"', '_').Replace('<', '_').Replace('>', '_');
        }

        /// <summary>The inline tag, with a trailing space so the icon never touches the text.</summary>
        public static string Tag(string glyphName)
        {
            return string.IsNullOrEmpty(glyphName) ? "" : "<sprite name=\"" + glyphName + "\"> ";
        }
    }
}
