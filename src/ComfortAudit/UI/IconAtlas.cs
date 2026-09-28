using System.Collections.Generic;
using ComfortAudit.Core;
using ComfortAudit.Core.Pure;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace ComfortAudit.UI
{
    /// <summary>
    /// Piece icons as TextMeshPro sprites, so the panel can stay one rich-text block and still
    /// show a piece's icon inline.
    ///
    /// Icons already live in the game's sprite textures, so each distinct texture gets a sprite
    /// asset whose glyphs point at the icons' existing rects — nothing is copied. The first asset
    /// is the panel's; the rest are its fallbacks, and TMP searches those when resolving a
    /// <c>&lt;sprite name=…&gt;</c> tag, so it does not matter which texture holds an icon.
    /// </summary>
    public static class IconAtlas
    {
        // Glyph geometry in the asset's own units; TMP scales by fontSize / pointSize, so every
        // icon renders one line tall whatever its pixel size.
        private const float PointSize = 100f;
        private const float Ascent = 80f;
        private const float Advance = 105f;

        private static readonly List<TMP_SpriteAsset> Assets = new List<TMP_SpriteAsset>();
        private static readonly HashSet<string> Glyphs = new HashSet<string>();
        private static bool _built;
        private static Shader _shader;

        public static TMP_SpriteAsset Primary => Assets.Count > 0 ? Assets[0] : null;

        public static string Tag(string prefabName)
        {
            if (!_built || !PluginConfig.ShowIcons.Value)
                return "";

            string glyph = SpriteTags.GlyphName(prefabName);
            return glyph != null && Glyphs.Contains(glyph) ? SpriteTags.Tag(glyph) : "";
        }

        public static void Invalidate()
        {
            for (int i = 0; i < Assets.Count; i++)
            {
                if (Assets[i] == null) continue;
                if (Assets[i].material != null) Object.Destroy(Assets[i].material);
                Object.Destroy(Assets[i]);
            }
            Assets.Clear();
            Glyphs.Clear();
            _built = false;
        }

        /// <summary>Builds once the catalogue exists; a no-op afterwards and while icons are off.</summary>
        public static void EnsureBuilt()
        {
            if (_built || !PluginConfig.ShowIcons.Value || !PieceCatalog.Ready)
                return;

            _built = true;   // one attempt per catalogue: a failure must not retry every frame

            if (_shader == null)
                _shader = Shader.Find("TextMeshPro/Sprite");
            if (_shader == null)
            {
                ComfortAuditPlugin.Log.LogWarning("icons: TextMeshPro/Sprite shader not found; panel will show no icons");
                return;
            }

            var byTexture = new Dictionary<Texture2D, List<KeyValuePair<string, Rect>>>();
            int skipped = 0;

            List<PieceCatalog.Entry> entries = PieceCatalog.Entries();
            for (int i = 0; i < entries.Count; i++)
            {
                PieceCatalog.Entry e = entries[i];
                Sprite icon = e.Piece != null ? e.Piece.m_icon : null;
                string glyph = SpriteTags.GlyphName(e.PrefabName);
                if (icon == null || icon.texture == null || glyph == null || Glyphs.Contains(glyph))
                    continue;

                Rect rect;
                try
                {
                    rect = icon.textureRect;   // throws for tightly-packed atlas sprites
                }
                catch (System.Exception)
                {
                    skipped++;
                    continue;
                }

                List<KeyValuePair<string, Rect>> list;
                if (!byTexture.TryGetValue(icon.texture, out list))
                    byTexture[icon.texture] = list = new List<KeyValuePair<string, Rect>>();
                list.Add(new KeyValuePair<string, Rect>(glyph, rect));
                Glyphs.Add(glyph);
            }

            foreach (KeyValuePair<Texture2D, List<KeyValuePair<string, Rect>>> kv in byTexture)
                Assets.Add(Build(kv.Key, kv.Value));

            if (Assets.Count > 1)
                Assets[0].fallbackSpriteAssets = Assets.GetRange(1, Assets.Count - 1);

            ComfortAuditPlugin.Log.LogInfo(string.Format(
                "icons: {0} glyph(s) over {1} texture(s), {2} skipped", Glyphs.Count, Assets.Count, skipped));
        }

        private static TMP_SpriteAsset Build(Texture2D texture, List<KeyValuePair<string, Rect>> icons)
        {
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            // hashCode is derived lazily from the name by TMP itself; do not assign it.
            asset.name = "ComfortAudit_" + texture.name;

            // Set before the material: an empty version makes UpdateLookupTables "upgrade" the
            // asset from its legacy list, clearing the tables built below.
            asset.version = "1.1.0";
            asset.spriteSheet = texture;

            var face = new FaceInfo();
            face.pointSize = (int)PointSize;
            face.scale = 1f;
            face.lineHeight = PointSize;
            face.ascentLine = Ascent;
            face.descentLine = Ascent - PointSize;
            asset.faceInfo = face;

            for (int i = 0; i < icons.Count; i++)
            {
                Rect r = icons[i].Value;
                var glyph = new TMP_SpriteGlyph(
                    (uint)i,
                    new GlyphMetrics(PointSize, PointSize, 0f, Ascent, Advance),
                    new GlyphRect((int)r.x, (int)r.y, (int)r.width, (int)r.height),
                    1f, 0);
                asset.spriteGlyphTable.Add(glyph);

                var character = new TMP_SpriteCharacter(0xFFFE, asset, glyph);
                character.name = icons[i].Key;
                asset.spriteCharacterTable.Add(character);
            }

            var material = new Material(_shader);
            material.SetTexture(ShaderUtilities.ID_MainTex, texture);
            asset.material = material;

            asset.UpdateLookupTables();
            return asset;
        }
    }
}
