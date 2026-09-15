using System.Text;
using ComfortAudit.Core;
using ComfortAudit.L10n;
using ComfortAudit.Model;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ComfortAudit.UI
{
    /// <summary>
    /// A single Jotunn woodpanel holding one rich-text block. Jotunn supplies the panel art,
    /// the Valheim TMP font and dragging, so there is no style harvesting to break on a patch.
    /// </summary>
    public sealed class ComfortPanel
    {
        private const float Padding = 18f;

        private GameObject _panel;
        private RectTransform _panelRect;
        private RectTransform _bodyRect;
        private TextMeshProUGUI _body;
        private string _lastText;
        private Vector2 _lastSeenPosition;
        private float _positionSettleAt;
        private bool _suppressLayoutApply;
        private bool _needsCentre;

        private const float PositionSettleDelay = 0.4f;

        public bool IsOpen { get; private set; }

        public bool Created => _panel != null;

        public void Create()
        {
            if (_panel != null || GUIManager.CustomGUIFront == null)
                return;

            float width = PluginConfig.PanelWidth.Value;

            _panel = GUIManager.Instance.CreateWoodpanel(
                GUIManager.CustomGUIFront.transform,
                new Vector2(0f, 1f),   // anchor top-left
                new Vector2(0f, 1f),
                PluginConfig.PanelPosition.Value,
                width,
                200f,
                true);                  // draggable, free from Jotunn

            _panel.name = "ComfortAuditPanel";
            _panelRect = _panel.GetComponent<RectTransform>();

            // CreateWoodpanel sets the anchors but leaves the pivot at DefaultControls.CreatePanel's
            // (0.5, 0.5). With a top-left anchor that makes anchoredPosition the panel's *centre*,
            // so a small offset pushes the top-left half off-screen. Pin the pivot to the top-left
            // corner so the position means what the config says it means.
            _panelRect.pivot = new Vector2(0f, 1f);

            // The woodpanel is a busy mid-brown texture; coloured text on it reads poorly no
            // matter which hues we pick. A translucent dark plate inset inside the panel gives
            // every colour a consistent dark ground to sit on, and stretches with the panel.
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdropGo.transform.SetParent(_panel.transform, false);

            var backdropRect = backdropGo.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = new Vector2(9f, 9f);
            backdropRect.offsetMax = new Vector2(-9f, -9f);

            var backdrop = backdropGo.GetComponent<Image>();
            backdrop.color = new Color(0.04f, 0.03f, 0.02f, 0.82f);
            backdrop.raycastTarget = false;   // let drags fall through to the panel

            // Added after the backdrop, so it draws on top of it.
            var textGo = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(_panel.transform, false);

            RectTransform rect = _bodyRect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Padding, -Padding);
            rect.sizeDelta = new Vector2(width - Padding * 2f, 0f);

            _body = textGo.GetComponent<TextMeshProUGUI>();
            _body.font = GUIManager.Instance.TMP_Norse;
            _body.fontSize = 15f;
            _body.color = BodyColor;
            _body.raycastTarget = false;
            _body.richText = true;
            _body.alignment = TextAlignmentOptions.TopLeft;
            _body.textWrappingMode = TextWrappingModes.Normal;
            _body.overflowMode = TextOverflowModes.Overflow;

            // Jotunn re-raises OnCustomGUIAvailable when the GUI is rebuilt (main menu <-> world).
            // Our panel is parented under CustomGUIFront, so it is destroyed with it and rebuilt
            // here. Carry the open state across instead of silently reverting to closed.
            _panel.SetActive(IsOpen);
            _lastText = null;

            ApplyLayout();
        }

        /// <summary>
        /// Push the current width/position config onto the live panel. Called on creation and
        /// whenever ConfigurationManager changes a layout setting, so edits apply immediately.
        /// </summary>
        public void ApplyLayout()
        {
            if (_panelRect == null || _suppressLayoutApply)
                return;

            // Vector2.zero is the documented "not positioned yet" value: centre instead of
            // jamming the panel into the top-left corner. Also how the Reset button works.
            if (PluginConfig.PanelPosition.Value == Vector2.zero)
                _needsCentre = true;

            float width = PluginConfig.PanelWidth.Value;
            _panelRect.sizeDelta = new Vector2(width, _panelRect.sizeDelta.y);

            if (_bodyRect != null)
                _bodyRect.sizeDelta = new Vector2(width - Padding * 2f, 0f);

            _panelRect.anchoredPosition = ClampToCanvas(PluginConfig.PanelPosition.Value, width);
            _lastSeenPosition = _panelRect.anchoredPosition;
            _positionSettleAt = 0f;

            // Force a rebuild so the new width re-wraps the text.
            _lastText = null;
        }

        /// <summary>
        /// The panel lives under Jotunn's CustomGUIFront, whose canvas is scaled — so Screen.width
        /// and height are the wrong units here. Measure the parent RectTransform instead, and fall
        /// back to Screen only if it is not ready yet.
        /// </summary>
        private Vector2 CanvasSize()
        {
            RectTransform parent = _panelRect != null ? _panelRect.parent as RectTransform : null;
            if (parent != null && parent.rect.width > 1f && parent.rect.height > 1f)
                return new Vector2(parent.rect.width, parent.rect.height);

            return new Vector2(Screen.width, Screen.height);
        }

        /// <summary>
        /// Keep a usable amount of the panel reachable. Without this, a saved position from a
        /// wider monitor (or a resolution change) can strand the panel off-screen with no way
        /// back except editing the config by hand.
        /// </summary>
        private Vector2 ClampToCanvas(Vector2 pos, float width)
        {
            const float margin = 60f;
            Vector2 canvas = CanvasSize();

            return new Vector2(
                Mathf.Clamp(pos.x, margin - width, Mathf.Max(margin - width, canvas.x - margin)),
                Mathf.Clamp(pos.y, -Mathf.Max(0f, canvas.y - margin), 0f));
        }

        /// <summary>
        /// Centre the panel on the canvas using its real height, which is only known after the
        /// first Render has laid the text out.
        /// </summary>
        private void CentreOnCanvas()
        {
            if (_panelRect == null)
                return;

            Vector2 canvas = CanvasSize();
            Vector2 size = _panelRect.sizeDelta;

            var centred = new Vector2(
                Mathf.Round((canvas.x - size.x) * 0.5f),
                -Mathf.Round((canvas.y - size.y) * 0.5f));

            _panelRect.anchoredPosition = centred;
            _lastSeenPosition = centred;
            _positionSettleAt = 0f;
            _needsCentre = false;

            // Write the concrete coordinates back, so the config always holds a real position
            // rather than a sentinel the user cannot interpret.
            _suppressLayoutApply = true;
            try { PluginConfig.PanelPosition.Value = centred; }
            finally { _suppressLayoutApply = false; }
        }

        /// <summary>
        /// Jotunn's draggable panel moves the RectTransform directly, so the config only learns
        /// about a drag if we copy it back. BepInEx flushes the config file on every
        /// SettingChanged, so this waits for the drag to settle rather than writing to disk —
        /// and re-entering ApplyLayout — on every frame of the drag.
        /// </summary>
        private void PersistPositionWhenSettled()
        {
            if (_panelRect == null)
                return;

            Vector2 current = _panelRect.anchoredPosition;

            if (current != _lastSeenPosition)
            {
                _lastSeenPosition = current;
                _positionSettleAt = Time.unscaledTime + PositionSettleDelay;
                return;
            }

            if (_positionSettleAt <= 0f || Time.unscaledTime < _positionSettleAt)
                return;

            _positionSettleAt = 0f;

            if (current != PluginConfig.PanelPosition.Value)
            {
                _suppressLayoutApply = true;
                try { PluginConfig.PanelPosition.Value = current; }
                finally { _suppressLayoutApply = false; }
            }
        }

        /// <summary>Drop the change-detection cache so the next Render rebuilds the text.</summary>
        public void InvalidateText()
        {
            _lastText = null;
        }

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (_panel != null)
                _panel.SetActive(open);
        }

        public void Destroy()
        {
            if (_panel != null)
                Object.Destroy(_panel);
            _panel = null;
            _panelRect = null;
            _bodyRect = null;
            _body = null;
            _lastText = null;
            IsOpen = false;
        }


        private PlacementPreviewResult _preview;

        public void SetPreview(PlacementPreviewResult preview)
        {
            // Cheap structural comparison: the preview changes as the ghost moves, and rebuilding
            // the whole panel string on every frame of that would be wasteful.
            if (Same(_preview, preview))
                return;

            _preview = preview;
            _lastText = null;
        }

        private static bool Same(PlacementPreviewResult a, PlacementPreviewResult b)
        {
            if (a == null || b == null)
                return a == null && b == null;

            return a.Active == b.Active
                   && a.Delta == b.Delta
                   && a.InRange == b.InRange
                   && a.NewTotal == b.NewTotal
                   && a.DisplayName == b.DisplayName
                   && a.BlockedBy == b.BlockedBy;
        }

        public void Render(ComfortSnapshot snap)
        {
            if (_body == null || !IsOpen)
                return;

            PersistPositionWhenSettled();

            string text = Build(snap);

            // Only touch TMP when the content actually changed; otherwise a static base would
            // force a re-layout several times a second for nothing.
            if (text == _lastText)
                return;

            _lastText = text;
            _body.text = text;

            float width = PluginConfig.PanelWidth.Value;
            float h = _body.GetPreferredValues(text, width - Padding * 2f, 0f).y;
            if (_panelRect != null)
            {
                _panelRect.sizeDelta = new Vector2(width, h + Padding * 2f);

                if (_needsCentre)
                    CentreOnCanvas();
            }
        }

        // ---- text assembly -------------------------------------------------

        // Tuned for the dark backdrop above, not for bare wood. Dimmed text is a muted warm
        // grey rather than reduced alpha — transparency let the panel texture show through the
        // glyphs, which is what made it hard to read.
        private static readonly Color BodyColor = new Color32(0xF2, 0xE9, 0xD8, 0xFF);

        private const string Dim = "<color=#BCAF97>";
        private const string Reset = "</color>";

        private static string Orange(string s)
        {
            Color c = GUIManager.Instance.ValheimOrange;
            return string.Format("<color=#{0}>{1}</color>", ColorUtility.ToHtmlStringRGB(c), s);
        }

        private static string Good(string s) { return "<color=#9BD97A>" + s + "</color>"; }
        private static string Bad(string s) { return "<color=#FF8A6B>" + s + "</color>"; }

        private string Build(ComfortSnapshot snap)
        {
            var sb = new StringBuilder(1024);

            sb.Append(Orange("<b>" + Strings.Get("$comfortaudit_panel_title") + "</b>"));
            sb.Append('\n');

            if (!snap.Valid)
            {
                sb.Append(Dim).Append(Strings.Get("$comfortaudit_not_resting")).Append(Reset);
                return sb.ToString();
            }

            AppendHeadline(sb, snap);
            sb.Append('\n');
            AppendPreview(sb, snap);
            AppendShelter(sb, snap);
            sb.Append('\n');
            AppendGate(sb, snap);
            sb.Append('\n');
            AppendPieces(sb, snap);
            sb.Append('\n');
            AppendRecommendations(sb, snap);

            if (snap.Mismatch)
            {
                sb.Append('\n').Append(Bad(Strings.Get("$comfortaudit_mismatch",
                    snap.VanillaComfortLevel, snap.ComfortLevel)));
            }

            return sb.ToString();
        }

        private void AppendHeadline(StringBuilder sb, ComfortSnapshot snap)
        {
            sb.Append("<b>").Append(Strings.Get("$comfortaudit_comfort")).Append(" ")
              .Append(snap.ComfortLevel).Append("</b>");

            sb.Append("   ").Append(Strings.Get("$comfortaudit_rested")).Append(' ')
              .Append(RestedMath.Format(snap.RestedSeconds));

            if (!snap.TtlFromLiveEffect)
                sb.Append(' ').Append(Dim).Append('(').Append(Strings.Get("$comfortaudit_from_prefab")).Append(')').Append(Reset);

            sb.Append('\n').Append(Dim);
            sb.Append(snap.InShelter
                ? Strings.Get("$comfortaudit_baseline")
                : Strings.Get("$comfortaudit_baseline_noshelter"));
            sb.Append(Reset).Append('\n');

            AppendCeiling(sb, snap);
        }

        /// <summary>
        /// Two ceilings: what is reachable with current recipes, and what the game holds in total.
        /// The first is the actionable target; the second stops the first from looking like the
        /// end of the road.
        /// </summary>
        private void AppendCeiling(StringBuilder sb, ComfortSnapshot snap)
        {
            if (!PluginConfig.ShowCeiling.Value || !snap.CeilingValid || snap.CeilingUnlocked <= 0)
                return;

            sb.Append(Dim).Append("  ");

            string reachable = RestedMath.Format(
                snap.BaseTTL + (snap.CeilingUnlocked - 1) * snap.TTLPerLevel);

            if (snap.ComfortLevel >= snap.CeilingUnlocked)
            {
                sb.Append(Strings.Get("$comfortaudit_ceiling", snap.CeilingUnlocked, reachable))
                  .Append(" — ").Append(Strings.Get("$comfortaudit_ceiling_atmax"));
            }
            else
            {
                sb.Append(Strings.Get("$comfortaudit_ceiling", snap.CeilingUnlocked, reachable));
            }

            if (snap.CeilingAll > snap.CeilingUnlocked)
            {
                string all = RestedMath.Format(
                    snap.BaseTTL + (snap.CeilingAll - 1) * snap.TTLPerLevel);
                sb.Append(", ").Append(Strings.Get("$comfortaudit_ceiling_all", snap.CeilingAll, all));
            }

            sb.Append(Reset).Append('\n');
        }

        private void AppendPreview(StringBuilder sb, ComfortSnapshot snap)
        {
            if (_preview == null || !_preview.Active)
                return;

            sb.Append(Orange(Strings.Get("$comfortaudit_preview"))).Append('\n').Append("  ");

            if (!_preview.InRange)
            {
                sb.Append(Dim)
                  .Append(Strings.Get("$comfortaudit_preview_outofrange",
                      _preview.DisplayName, _preview.Distance, (int)ComfortScanner.ComfortRadius))
                  .Append(Reset).Append('\n').Append('\n');
                return;
            }

            if (_preview.Delta > 0)
            {
                sb.Append(Good(Strings.Get("$comfortaudit_preview_gain",
                    _preview.DisplayName, _preview.Delta, _preview.NewTotal)));
            }
            else
            {
                sb.Append(Dim).Append(Strings.Get("$comfortaudit_preview_nogain", _preview.DisplayName));

                if (!snap.InShelter)
                    sb.Append(" — ").Append(Strings.Get("$comfortaudit_preview_noshelter"));
                else if (!string.IsNullOrEmpty(_preview.BlockedBy))
                    sb.Append(" — ").Append(Strings.Get("$comfortaudit_preview_blocked", _preview.BlockedBy));

                sb.Append(Reset);
            }

            sb.Append('\n').Append('\n');
        }

        private void AppendShelter(StringBuilder sb, ComfortSnapshot snap)
        {
            int pct = Mathf.RoundToInt(snap.CoverPercentage * 100f);

            if (snap.InShelter)
            {
                sb.Append(Good(Strings.Get("$comfortaudit_shelter_ok", pct))).Append('\n');
                return;
            }

            string roof = Strings.Get(snap.UnderRoof ? "$comfortaudit_yes" : "$comfortaudit_no");
            sb.Append(Bad(Strings.Get("$comfortaudit_shelter_no", pct, roof))).Append('\n');
            sb.Append(Bad(Strings.Get("$comfortaudit_shelter_warn", snap.PotentialIfSheltered))).Append('\n');
        }

        private void AppendGate(StringBuilder sb, ComfortSnapshot snap)
        {
            if (snap.Gate == null)
                return;

            sb.Append(Orange(Strings.Get("$comfortaudit_gate_header"))).Append('\n');

            for (int i = 0; i < snap.Gate.Conditions.Count; i++)
            {
                GateCondition c = snap.Gate.Conditions[i];
                sb.Append(c.Met ? Good("  + ") : Bad("  - "));
                sb.Append(c.Met ? Dim : string.Empty);
                sb.Append(Strings.Get(c.Token));
                sb.Append(c.Met ? Reset : string.Empty);
                sb.Append('\n');
            }
        }

        private void AppendPieces(StringBuilder sb, ComfortSnapshot snap)
        {
            if (snap.PieceCount == 0)
            {
                sb.Append(Dim)
                  .Append(Strings.Get("$comfortaudit_no_pieces", (int)ComfortScanner.ComfortRadius))
                  .Append(Reset).Append('\n');
                AppendMissing(sb, snap);
                return;
            }

            // Contributing
            sb.Append(Orange(Strings.Get("$comfortaudit_contributing"))).Append('\n');
            bool any = false;
            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                if (e.Status != PieceStatus.Counted)
                    continue;

                any = true;
                sb.Append("  ").Append(ComfortGroups.Name(e.Group)).Append(": ")
                  .Append(e.DisplayName);

                if (e.Inactive)
                {
                    sb.Append("  ").Append(Bad(Strings.Get("$comfortaudit_unlit_hint", e.RawComfort)));
                }
                else
                {
                    sb.Append("  ").Append(Orange("+" + e.Comfort));
                }

                AppendSuffix(sb, e);
                sb.Append('\n');
            }
            if (!any)
                sb.Append(Dim).Append("  —").Append(Reset).Append('\n');

            // Ignored
            bool header = false;
            for (int i = 0; i < snap.Pieces.Count; i++)
            {
                PieceEntry e = snap.Pieces[i];
                if (e.Status == PieceStatus.Counted)
                    continue;

                if (!header)
                {
                    sb.Append(Orange(Strings.Get("$comfortaudit_ignored"))).Append('\n');
                    header = true;
                }

                sb.Append(Dim).Append("  ").Append(ComfortGroups.Name(e.Group)).Append(": ")
                  .Append(e.DisplayName).Append("  ");

                sb.Append(e.Status == PieceStatus.ShadowedByGroup
                    ? Strings.Get("$comfortaudit_beaten_by", e.ShadowedBy)
                    : Strings.Get("$comfortaudit_dupe_of", e.ShadowedBy));

                sb.Append(" — ").Append(Strings.Get("$comfortaudit_safe_to_remove"));
                AppendSuffix(sb, e);
                sb.Append(Reset).Append('\n');
            }

            AppendMissing(sb, snap);
        }

        private void AppendRecommendations(StringBuilder sb, ComfortSnapshot snap)
        {
            sb.Append(Orange(Strings.Get("$comfortaudit_recommend"))).Append('\n');

            if (!snap.RecommendationsReady)
            {
                sb.Append(Dim).Append("  ").Append(Strings.Get("$comfortaudit_rec_loading"))
                  .Append(Reset).Append('\n');
                return;
            }

            if (snap.Recommendations.Count == 0)
            {
                AppendNothingToDo(sb, snap);
                return;
            }

            for (int i = 0; i < snap.Recommendations.Count; i++)
                AppendRecommendation(sb, snap.Recommendations[i]);
        }

        /// <summary>
        /// An empty list usually means the base is optimal for what the player has unlocked,
        /// which is worth saying outright — "nothing available" reads like a failure, and hides
        /// the genuinely useful fact that more pieces exist further up the progression.
        /// </summary>
        private void AppendNothingToDo(StringBuilder sb, ComfortSnapshot snap)
        {
            RecommendationStats st = snap.RecStats;

            if (st == null || st.Catalogue == 0)
            {
                sb.Append(Dim).Append("  ").Append(Strings.Get("$comfortaudit_rec_none"))
                  .Append(Reset).Append('\n');
                return;
            }

            if (st.NoGain > 0)
            {
                sb.Append("  ").Append(Good(Strings.Get("$comfortaudit_rec_none_best"))).Append('\n');
            }
            else
            {
                sb.Append(Dim).Append("  ").Append(Strings.Get("$comfortaudit_rec_none"))
                  .Append(Reset).Append('\n');
            }

            if (st.RecipeUnknown > 0)
                AppendNote(sb, Strings.Get("$comfortaudit_rec_locked", st.RecipeUnknown));

            if (st.StationOutOfRange > 0)
                AppendNote(sb, Strings.Get("$comfortaudit_rec_needstation", st.StationOutOfRange));

            if (st.MaterialsShort > 0)
                AppendNote(sb, Strings.Get("$comfortaudit_rec_needmaterials", st.MaterialsShort));
        }

        private void AppendNote(StringBuilder sb, string text)
        {
            sb.Append(Dim).Append("  ").Append(text).Append(Reset).Append('\n');
        }

        private void AppendRecommendation(StringBuilder sb, Recommendation r)
        {
            sb.Append("  ").Append(Orange("+" + r.Gain)).Append("  ").Append(r.DisplayName);

            if (r.Kind == RecommendationKind.LightIt)
                sb.Append("  ").Append(Good(Strings.Get("$comfortaudit_rec_light")));

            if (!string.IsNullOrEmpty(r.Replaces))
                sb.Append("  ").Append(Dim).Append(Strings.Get("$comfortaudit_rec_replaces", r.Replaces)).Append(Reset);
            else if (r.Kind == RecommendationKind.Stacking)
                sb.Append("  ").Append(Dim).Append(Strings.Get("$comfortaudit_rec_stacking")).Append(Reset);
            else if (r.Kind == RecommendationKind.NewGroup && r.GroupKnown)
                sb.Append("  ").Append(Dim).Append(ComfortGroups.Name(r.Group)).Append(Reset);

            sb.Append('\n');

            if (r.Free || r.Kind == RecommendationKind.Shelter)
                return;

            if (!r.StationInRange)
            {
                sb.Append("      ").Append(Bad(Strings.Get("$comfortaudit_rec_nostation"))).Append('\n');
                return;
            }

            if (!PluginConfig.ShowMaterials.Value || r.Materials.Count == 0)
                return;

            sb.Append("      ");
            for (int i = 0; i < r.Materials.Count; i++)
            {
                MaterialLine m = r.Materials[i];
                if (i > 0) sb.Append(Dim).Append(", ").Append(Reset);

                string line = m.Amount + " " + m.DisplayName;
                sb.Append(m.Enough ? Dim + line + Reset : Bad(line + " (" + m.Have + ")"));
            }
            sb.Append('\n');
        }

        private void AppendSuffix(StringBuilder sb, PieceEntry e)
        {
            if (PluginConfig.ShowDistances.Value)
                sb.Append(Dim).Append(string.Format("  {0:0.0}m", e.Distance)).Append(Reset);

            if (PluginConfig.ShowPrefabNames.Value)
                sb.Append(Dim).Append("  [").Append(e.PrefabName).Append(']').Append(Reset);
        }

        private void AppendMissing(StringBuilder sb, ComfortSnapshot snap)
        {
            if (snap.MissingGroups.Count == 0)
                return;

            sb.Append(Orange(Strings.Get("$comfortaudit_missing"))).Append('\n').Append(Dim).Append("  ");
            for (int i = 0; i < snap.MissingGroups.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ComfortGroups.Name(snap.MissingGroups[i]));
            }
            sb.Append(Reset).Append('\n');
        }
    }
}
