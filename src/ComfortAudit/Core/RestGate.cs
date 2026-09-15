using ComfortAudit.Model;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Reproduces the Resting test from Player.UpdateEnvStatusEffects:
    ///
    ///   canRest = !sensed &amp;&amp; (sitting || sheltered) &amp;&amp; !cold &amp;&amp; !freezing
    ///             &amp;&amp; (!wet || warmCozy) &amp;&amp; !burning &amp;&amp; nearFire
    ///
    /// The game surfaces only the outcome. Reporting each condition separately is most of the
    /// value of this mod: "near a fire under a roof but still not resting" is nearly always wet,
    /// sensed, or cold, and nothing in the vanilla UI says so.
    /// </summary>
    public static class RestGate
    {
        public static RestGateResult Evaluate(Player player)
        {
            var r = new RestGateResult();
            var se = player.GetSEMan();

            // The internal flags are mirrored into status effects in the same tick, so these
            // public reads track UpdateEnvStatusEffects exactly without touching privates.
            bool nearFire = se.HaveStatusEffect(SEMan.s_statusEffectCampFire);
            bool sheltered = player.InShelter();
            bool sitting = player.IsSitting();
            bool sensed = player.IsSensed();
            bool burning = se.HaveStatusEffect(SEMan.s_statusEffectBurning);
            bool wet = se.HaveStatusEffect(SEMan.s_statusEffectWet);
            bool cold = se.HaveStatusEffect(SEMan.s_statusEffectCold);
            bool freezing = se.HaveStatusEffect(SEMan.s_statusEffectFreezing);
            bool warmCozy = EffectArea.IsPointInsideArea(
                player.transform.position, EffectArea.Type.WarmCozyArea, 1f) != null;

            r.NearFire = nearFire;
            r.Sheltered = sheltered;
            r.Sitting = sitting;

            r.Conditions.Add(new GateCondition("$comfortaudit_gate_fire", nearFire));
            // Shelter OR sitting satisfies this one — which is why you can rest at a campfire
            // under open sky, at comfort 1, with a furnished hall contributing nothing.
            r.Conditions.Add(new GateCondition("$comfortaudit_gate_shelter_or_sit", sheltered || sitting));
            r.Conditions.Add(new GateCondition("$comfortaudit_gate_notsensed", !sensed));
            r.Conditions.Add(new GateCondition("$comfortaudit_gate_dry", !wet || warmCozy));
            r.Conditions.Add(new GateCondition("$comfortaudit_gate_notcold", !cold));
            r.Conditions.Add(new GateCondition("$comfortaudit_gate_notfreezing", !freezing));
            r.Conditions.Add(new GateCondition("$comfortaudit_gate_notburning", !burning));

            r.CanRest = r.AllMet;
            return r;
        }
    }
}
