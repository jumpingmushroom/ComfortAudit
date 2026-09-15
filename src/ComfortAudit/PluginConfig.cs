using System;
using BepInEx.Configuration;
using UnityEngine;

namespace ComfortAudit
{
    public enum RecommendationFilter
    {
        /// <summary>Recipe and materials discovered. Broadest; may suggest things you cannot place here.</summary>
        KnownOnly,

        /// <summary>Discovered, and the required crafting station is within build range.</summary>
        KnownAndStationInRange,

        /// <summary>Placeable right now: station in range and the materials in your inventory.</summary>
        Buildable
    }

    public static class PluginConfig
    {
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<Vector2> PanelPosition;
        public static ConfigEntry<float> PanelWidth;
        public static ConfigEntry<float> ScanInterval;
        public static ConfigEntry<bool> ShowDistances;
        public static ConfigEntry<bool> ShowPrefabNames;
        public static ConfigEntry<bool> Verbose;
        public static ConfigEntry<RecommendationFilter> Filter;
        public static ConfigEntry<int> MaxRecommendations;
        public static ConfigEntry<int> MaxIgnored;
        public static ConfigEntry<bool> ShowMaterials;
        public static ConfigEntry<bool> ShowCeiling;
        public static ConfigEntry<bool> ShowComfortOnIcon;
        public static ConfigEntry<bool> ShowPlacementPreview;

        /// <summary>
        /// Vector2.zero means "not positioned yet — centre it". Any other value is a literal
        /// pixel offset of the panel's top-left corner from the top-left of the screen.
        /// </summary>
        public static readonly Vector2 DefaultPosition = Vector2.zero;

        /// <summary>
        /// Raised when a setting that affects panel layout changes, so ConfigurationManager edits
        /// take effect immediately instead of silently waiting for a restart.
        /// </summary>
        public static event Action LayoutChanged;

        /// <summary>Raised when a setting that only affects rendered text changes.</summary>
        public static event Action ContentChanged;

        // ConfigurationManagerAttributes is supplied by Jotunn (global namespace). Config
        // managers match it by type name via reflection, so no dependency on any particular
        // ConfigurationManager build is implied and nothing breaks if none is installed.
        private static ConfigurationManagerAttributes Attr(int order, bool advanced = false)
        {
            return new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced };
        }

        public static void Bind(ConfigFile cfg)
        {
            // ConfigurationManager already draws KeyboardShortcut with an interactive rebind
            // widget and Vector2 with an x/y editor, so neither needs a custom drawer.
            ToggleKey = cfg.Bind("General", "ToggleKey",
                new KeyboardShortcut(KeyCode.F7),
                new ConfigDescription(
                    "Key that shows and hides the Comfort Audit panel.",
                    null,
                    Attr(100)));

            ScanInterval = cfg.Bind("General", "ScanInterval", 0.5f,
                new ConfigDescription(
                    "Seconds between rescans while the panel is open. The game itself only " +
                    "recomputes comfort every 2 s, so values below that show fresher numbers " +
                    "than the vanilla HUD.",
                    new AcceptableValueRange<float>(0.1f, 5f),
                    Attr(90)));

            PanelPosition = cfg.Bind("Panel", "Position", DefaultPosition,
                new ConfigDescription(
                    "Panel offset from the top-left of the screen, in pixels, measured to the " +
                    "panel's top-left corner. (0, 0) means 'centre on screen' and is what the " +
                    "reset button restores. Dragging the panel updates this value.",
                    null,
                    Attr(80)));

            PanelWidth = cfg.Bind("Panel", "Width", 420f,
                new ConfigDescription(
                    "Panel width in pixels.",
                    new AcceptableValueRange<float>(280f, 900f),
                    Attr(70)));

            // Its own entry rather than replacing the Position editor, so you keep both the
            // numeric fields and a one-click recovery when the panel ends up off-screen after a
            // resolution change or a monitor being unplugged.
            cfg.Bind("Panel", "ResetPosition", false,
                new ConfigDescription("", null,
                    new ConfigurationManagerAttributes
                    {
                        Order = 60,
                        HideSettingName = true,
                        HideDefaultButton = true,
                        CustomDrawer = DrawResetButton
                    }));

            ShowDistances = cfg.Bind("Panel", "ShowDistances", false,
                new ConfigDescription(
                    "Show each piece's distance from you, for checking the 10 m radius.",
                    null,
                    Attr(50)));

            ShowPrefabNames = cfg.Bind("Panel", "ShowPrefabNames", false,
                new ConfigDescription(
                    "Show prefab names alongside display names. Useful when diagnosing modded pieces.",
                    null,
                    Attr(40, advanced: true)));

            Filter = cfg.Bind("Recommendations", "Filter",
                RecommendationFilter.KnownAndStationInRange,
                new ConfigDescription(
                    "Which pieces count as available to you. KnownOnly is the broadest. " +
                    "KnownAndStationInRange matches what your hammer would actually let you " +
                    "place here. Buildable additionally requires the materials on hand, which " +
                    "is the most actionable but often empties the list.",
                    null,
                    Attr(35)));

            MaxRecommendations = cfg.Bind("Recommendations", "MaxShown", 5,
                new ConfigDescription(
                    "How many suggestions to list.",
                    new AcceptableValueRange<int>(1, 15),
                    Attr(30)));

            ShowMaterials = cfg.Bind("Recommendations", "ShowMaterials", true,
                new ConfigDescription(
                    "List each suggestion's material cost, and whether you have enough.",
                    null,
                    Attr(25)));

            ShowCeiling = cfg.Bind("Panel", "ShowCeiling", true,
                new ConfigDescription(
                    "Show the highest comfort reachable at a sheltered spot: what you could " +
                    "build today, and what exists once everything is unlocked.",
                    null,
                    Attr(45)));

            MaxIgnored = cfg.Bind("Panel", "MaxIgnoredShown", 8,
                new ConfigDescription(
                    "How many ignored pieces to list before collapsing the rest into a count. " +
                    "This is the only list with no natural bound, and it is what made the " +
                    "panel outgrow the screen in a hall full of duplicate chairs.",
                    new AcceptableValueRange<int>(1, 50),
                    Attr(48)));

            ShowComfortOnIcon = cfg.Bind("Panel", "ShowComfortOnIcon", true,
                new ConfigDescription(
                    "Append current comfort and your ceiling to the Rested icon's label. This is " +
                    "the only always-visible surface: Valheim locks the cursor during play, so a " +
                    "hover tooltip would only be reachable with the inventory or map open.",
                    null,
                    Attr(18)));

            ShowPlacementPreview = cfg.Bind("Panel", "ShowPlacementPreview", true,
                new ConfigDescription(
                    "While building, show what the held piece would add if placed where the " +
                    "ghost is, and what already beats it if the answer is nothing.",
                    null,
                    Attr(55)));

            Verbose = cfg.Bind("Logging", "Verbose", false,
                new ConfigDescription(
                    "Log scan details to the BepInEx log. Off by default; nothing is logged per tick.",
                    null,
                    Attr(10, advanced: true)));

            PanelPosition.SettingChanged += (s, e) => Raise(LayoutChanged);
            PanelWidth.SettingChanged += (s, e) => Raise(LayoutChanged);
            ShowDistances.SettingChanged += (s, e) => Raise(ContentChanged);
            Filter.SettingChanged += (s, e) => Raise(ContentChanged);
            MaxRecommendations.SettingChanged += (s, e) => Raise(ContentChanged);
            ShowMaterials.SettingChanged += (s, e) => Raise(ContentChanged);
            ShowCeiling.SettingChanged += (s, e) => Raise(ContentChanged);
            ShowPlacementPreview.SettingChanged += (s, e) => Raise(ContentChanged);
            ShowPrefabNames.SettingChanged += (s, e) => Raise(ContentChanged);
            MaxIgnored.SettingChanged += (s, e) => Raise(ContentChanged);
            Verbose.SettingChanged += (s, e) => Raise(ContentChanged);
        }

        private static void DrawResetButton(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Centre panel on screen", GUILayout.ExpandWidth(true)))
                PanelPosition.Value = DefaultPosition;
        }

        private static void Raise(Action a)
        {
            if (a == null)
                return;

            try
            {
                a();
            }
            catch (Exception ex)
            {
                ComfortAuditPlugin.Log.LogWarning("config apply failed: " + ex.Message);
            }
        }
    }
}
