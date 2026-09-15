using BepInEx;
using BepInEx.Logging;
using ComfortAudit.Core;
using ComfortAudit.L10n;
using ComfortAudit.Model;
using ComfortAudit.UI;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace ComfortAudit
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim.x86_64")]
    public sealed class ComfortAuditPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jumpingmushroom.comfortaudit";
        public const string PluginName = "ComfortAudit";
        public const string PluginVersion = "0.4.0";

        internal static ManualLogSource Log;

        private readonly ComfortPanel _panel = new ComfortPanel();
        private Harmony _harmony;

        private ComfortSnapshot _snapshot = new ComfortSnapshot();
        private float _nextScan;
        private bool _guiReady;
        private Player _lastPlayer;

        private void Awake()
        {
            Log = Logger;

            PluginConfig.Bind(base.Config);
            Strings.Register();
            CostTable.Load();
            ConsoleCommands.Register();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(ComfortAuditPlugin).Assembly);

            GUIManager.OnCustomGUIAvailable += OnGuiAvailable;
            PluginConfig.LayoutChanged += OnLayoutChanged;
            PluginConfig.ContentChanged += OnContentChanged;

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            GUIManager.OnCustomGUIAvailable -= OnGuiAvailable;
            PluginConfig.LayoutChanged -= OnLayoutChanged;
            PluginConfig.ContentChanged -= OnContentChanged;
            _panel.Destroy();
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }

        private void OnGuiAvailable()
        {
            if (GUIManager.IsHeadless())
                return;

            _panel.Create();
            _guiReady = _panel.Created;

            if (PluginConfig.Verbose.Value)
                Log.LogDebug("panel created: " + _guiReady);
        }

        private void OnLayoutChanged()
        {
            _panel.ApplyLayout();
        }

        /// <summary>Text-only settings: force the next Render to rebuild rather than dedupe.</summary>
        private void OnContentChanged()
        {
            _panel.InvalidateText();
        }

        /// <summary>Logged out or returned to the menu: close up and forget the world.</summary>
        private void LocalPlayerGone()
        {
            _snapshot = new ComfortSnapshot();
            SnapshotService.Clear();
            _panel.SetOpen(false);
            _nextScan = 0f;

            // Prefabs are per-world; drop the catalogue so a different world rebuilds it.
            PieceCatalog.Invalidate();
            Diagnostics.Reset();
        }

        /// <summary>A new local player: a fresh world, or a respawn into a new instance.</summary>
        private void LocalPlayerArrived()
        {
            _snapshot = new ComfortSnapshot();
            SnapshotService.Clear();
            _nextScan = 0f;
            PieceCatalog.Invalidate();
            Diagnostics.Reset();
        }

        private void Update()
        {
            if (!_guiReady)
                return;

            Player player = Player.m_localPlayer;

            // Detect logout / world change here rather than from Player.OnDestroy: that method
            // nulls m_localPlayer inside its own body, so a postfix comparing against it always
            // sees null and never fires. Polling the field is also robust to teardown ordering
            // we do not control.
            if (player != _lastPlayer)
            {
                if (player == null)
                    LocalPlayerGone();
                else
                    LocalPlayerArrived();

                _lastPlayer = player;
            }

            // The panel is an in-world tool; with no player there is nothing to audit.
            if (player == null)
                return;

            if (PluginConfig.ToggleKey.Value.IsDown() && !InputBlocked())
            {
                _panel.Toggle();
            }

            // Closed panel costs one key check per frame and nothing else — no scan, no draw.
            if (!_panel.IsOpen)
                return;

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + Mathf.Max(0.1f, PluginConfig.ScanInterval.Value);
                _snapshot = SnapshotService.Get(Mathf.Max(0.1f, PluginConfig.ScanInterval.Value));
                Diagnostics.ReportOnce(_snapshot);
                Diagnostics.ReportEmptyRecommendations(_snapshot);

                if (PluginConfig.Verbose.Value)
                {
                    Log.LogDebug(string.Format(
                        "comfort={0} vanilla={1} shelter={2} cover={3:0.00} pieces={4}",
                        _snapshot.ComfortLevel, _snapshot.VanillaComfortLevel,
                        _snapshot.InShelter, _snapshot.CoverPercentage, _snapshot.PieceCount));
                }
            }

            // Per frame rather than per scan: the ghost moves continuously, and a half-second
            // lag on the delta feels broken while sweeping a piece around. The work is a walk over
            // the handful of pieces already in range.
            if (PluginConfig.ShowPlacementPreview.Value)
                _panel.SetPreview(PlacementPreview.Compute(player, _snapshot));
            else
                _panel.SetPreview(null);

            _panel.Render(_snapshot);
        }

        /// <summary>Never toggle while the player is typing — the classic Valheim-mod papercut.</summary>
        private static bool InputBlocked()
        {
            if (Console.IsVisible()) return true;
            if (Menu.IsVisible()) return true;
            if (TextInput.IsVisible()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            return false;
        }
    }
}
