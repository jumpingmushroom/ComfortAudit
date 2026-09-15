using ComfortAudit.Core;
using ComfortAudit.Model;
using HarmonyLib;
using UnityEngine;

namespace ComfortAudit.Patches
{
    /// <summary>
    /// Puts comfort on the Rested icon's own label, where it is visible during play.
    ///
    /// The hover tooltip is only reachable when the cursor is free — GameCamera.UpdateMouseCapture
    /// locks and hides it unless the inventory, map, menu or a dialog is open. That makes a
    /// hover-only summary close to useless while actually playing, so the headline number goes
    /// here instead and the tooltip carries the detail.
    ///
    /// SE_Rested does not override GetIconText, so the base implementation runs for it; the patch
    /// filters by type because this method is shared with every other effect.
    /// </summary>
    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.GetIconText))]
    public static class StatusEffect_GetIconText_Patch
    {
        /// <summary>How long a panel scan stays preferable to the game's own cached comfort.</summary>
        private const float FreshScanSeconds = 2.5f;

        private static int _comfort = -1;
        private static int _ceiling = -1;
        private static string _suffix = string.Empty;

        private static void Postfix(StatusEffect __instance, ref string __result)
        {
            if (!PluginConfig.ShowComfortOnIcon.Value || !(__instance is SE_Rested))
                return;

            Refresh();

            if (_suffix.Length == 0)
                return;

            __result = string.IsNullOrEmpty(__result) ? _suffix : __result + "  " + _suffix;
        }

        /// <summary>
        /// Called once per frame while Rested is active, so this must stay cheap. It used to
        /// request a snapshot every 2 s, which ran the full scan — piece walk, recommender,
        /// station lookups — with the panel closed, contradicting the promise that a closed
        /// panel costs nothing. Now it reads the panel's scan only when one is fresh, and
        /// otherwise the game's own cached comfort plus the 10 s-cached ceiling.
        /// </summary>
        private static void Refresh()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                Set(0, 0);
                return;
            }

            int comfort;
            int ceiling;

            ComfortSnapshot snap = SnapshotService.Current;
            if (snap.Valid && Time.time - SnapshotService.LastScanTime < FreshScanSeconds)
            {
                // The panel is open and scanning: reuse its numbers so both surfaces agree.
                comfort = snap.ComfortLevel;
                ceiling = snap.CeilingValid ? snap.CeilingUnlocked : 0;
            }
            else
            {
                // Player.GetComfortLevel is the game's cache, refreshed every 2 s — the same
                // cadence the old scan ran at, for none of the cost. It reads 0 until the first
                // UpdateBaseValue, in which case nothing is appended.
                comfort = player.GetComfortLevel();
                Ceiling.Result c = Ceiling.Get(player);
                ceiling = c.Valid ? c.Unlocked : 0;
            }

            Set(comfort, ceiling);
        }

        /// <summary>Rebuild the string only when a number actually changes.</summary>
        private static void Set(int comfort, int ceiling)
        {
            if (comfort == _comfort && ceiling == _ceiling)
                return;

            _comfort = comfort;
            _ceiling = ceiling;

            if (comfort <= 0)
                _suffix = string.Empty;
            else if (ceiling > 0)
                _suffix = comfort + "/" + ceiling;
            else
                _suffix = comfort.ToString();
        }
    }
}
