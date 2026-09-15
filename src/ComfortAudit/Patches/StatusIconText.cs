using ComfortAudit.Core;
using ComfortAudit.Model;
using HarmonyLib;

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
        private static int _revision = -1;
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

        /// <summary>Called once per frame per effect, so build the string only when it changes.</summary>
        private static void Refresh()
        {
            ComfortSnapshot snap = SnapshotService.Get(2f);

            if (SnapshotService.Revision == _revision)
                return;

            _revision = SnapshotService.Revision;

            if (!snap.Valid)
            {
                _suffix = string.Empty;
                return;
            }

            _suffix = snap.CeilingValid && snap.CeilingUnlocked > 0
                ? snap.ComfortLevel + "/" + snap.CeilingUnlocked
                : snap.ComfortLevel.ToString();
        }
    }
}
