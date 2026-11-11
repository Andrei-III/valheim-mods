using HarmonyLib;
using UnityEngine;

namespace HarpoonReel
{
    /// <summary>
    /// Vanilla SE_Harpooned remembers the distance at the moment of the hit (m_baseDistance) and only
    /// pulls when the creature is farther than that - it's a leash, not a reel. While reeling we shrink
    /// m_baseDistance toward MinDistance so the vanilla pull keeps dragging the creature in.
    ///
    /// This code runs on whichever machine owns the creature (you, your friend, or the server).
    /// </summary>
    internal static class HarpoonedPatches
    {
        private static readonly AccessTools.FieldRef<SE_Harpooned, Character> Attacker =
            AccessTools.FieldRefAccess<SE_Harpooned, Character>("m_attacker");
        private static readonly AccessTools.FieldRef<SE_Harpooned, float> BaseDistance =
            AccessTools.FieldRefAccess<SE_Harpooned, float>("m_baseDistance");
        private static readonly AccessTools.FieldRef<SE_Harpooned, bool> Broken =
            AccessTools.FieldRefAccess<SE_Harpooned, bool>("m_broken");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SE_Harpooned), nameof(SE_Harpooned.SetAttacker))]
        private static void SetAttacker_Postfix(SE_Harpooned __instance)
        {
            if (!HarpoonReelPlugin.DebugLogging.Value || !__instance.m_character) return;
            HarpoonReelPlugin.Log.LogInfo(
                $"Harpooned {__instance.m_character.m_name}: mass={__instance.m_character.GetMass():0.##} " +
                $"baseDist={BaseDistance(__instance):0.##} pullForce={__instance.m_pullForce} " +
                $"forcePower={__instance.m_forcePower} pullSpeed={__instance.m_pullSpeed} " +
                $"smooth={__instance.m_smoothDistance} breakDist={__instance.m_breakDistance} " +
                $"staminaDrain={__instance.m_staminaDrain} broken={Broken(__instance)}");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SE_Harpooned), nameof(SE_Harpooned.UpdateStatusEffect))]
        private static void UpdateStatusEffect_Prefix(SE_Harpooned __instance, float dt, out float __state)
        {
            __state = -1f;

            var attacker = Attacker(__instance);
            var target = __instance.m_character;
            if (!attacker || !target || Broken(__instance)) return;
            if (target.GetStandingOnShip() != null || target.IsAttached()) return;
            if (!ReelInput.IsReeling(attacker)) return;

            float dist = Vector3.Distance(attacker.transform.position, target.transform.position);
            float minDist = HarpoonReelPlugin.MinDistance.Value;
            ref float baseDist = ref BaseDistance(__instance);

            // If the creature already came closer on its own, take up the slack immediately.
            if (baseDist > dist) baseDist = dist;

            // Shorten the line, but never more than smoothDistance below the current distance:
            // that's where vanilla pull is at full strength, and it keeps us well inside breakDistance
            // so reeling itself never snaps the line.
            float floor = Mathf.Max(minDist, dist - __instance.m_smoothDistance);
            if (baseDist > floor)
                baseDist = Mathf.Max(floor, baseDist - HarpoonReelPlugin.ReelSpeed.Value * dt);

            // Scale stamina drain only while we're actively reeling; restored in the postfix.
            __state = __instance.m_staminaDrain;
            __instance.m_staminaDrain *= HarpoonReelPlugin.StaminaMultiplier.Value;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SE_Harpooned), nameof(SE_Harpooned.UpdateStatusEffect))]
        private static void UpdateStatusEffect_Postfix(SE_Harpooned __instance, float __state)
        {
            if (__state >= 0f) __instance.m_staminaDrain = __state;
        }
    }
}
