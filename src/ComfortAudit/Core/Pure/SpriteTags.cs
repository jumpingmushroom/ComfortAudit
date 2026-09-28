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
        /// Null or empty input, or input that is only Unity's "(Clone)" suffix, returns null.
        /// </summary>
        public static string GlyphName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return null;

            const string clone = "(Clone)";
            if (prefabName.EndsWith(clone))
                prefabName = prefabName.Substring(0, prefabName.Length - clone.Length);

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
