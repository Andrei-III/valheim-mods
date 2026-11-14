using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace QuickFill
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Uses the vanilla add-ore / add-fuel calls and RPCs, so nobody else needs the mod.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class QuickFillPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.quickfill";
        public const string Name = "QuickFill";
        public const string Version = "1.0.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowHint;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1 - General", "Enabled", true,
                "Shift + E on a smelter, kiln, blast furnace, etc. adds as many items as it can take. Plain E keeps the vanilla one-at-a-time behavior.");
            ShowHint = Config.Bind("1 - General", "ShowHint", true,
                "Add a \"Shift + E  Fill\" line to the hover text.");
            DebugLogging = Config.Bind("2 - Debug", "DebugLogging", false,
                "Log each fill to BepInEx/LogOutput.log.");

            _harmony = Harmony.CreateAndPatchAll(typeof(FillPatches), Guid);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        internal static void Debug(string msg)
        {
            if (DebugLogging.Value) Log.LogInfo(msg);
        }
    }

    [HarmonyPatch]
    internal static class FillPatches
    {
        private delegate bool AddCallback(Smelter smelter, Switch sw, Humanoid user, ItemDrop.ItemData item);

        // These go through Harmony's patched methods, so other mods' patches (e.g. pulling ore from
        // nearby chests) run on every single add, exactly as if the player had pressed E each time.
        private static readonly AddCallback OnAddOre =
            AccessTools.MethodDelegate<AddCallback>(AccessTools.Method(typeof(Smelter), "OnAddOre"));
        private static readonly AddCallback OnAddFuel =
            AccessTools.MethodDelegate<AddCallback>(AccessTools.Method(typeof(Smelter), "OnAddFuel"));
        private static readonly Func<Smelter, int> GetQueueSize =
            AccessTools.MethodDelegate<Func<Smelter, int>>(AccessTools.Method(typeof(Smelter), "GetQueueSize"));
        private static readonly Func<Smelter, float> GetFuel =
            AccessTools.MethodDelegate<Func<Smelter, float>>(AccessTools.Method(typeof(Smelter), "GetFuel"));

        private const string AddedPrefix = "$msg_added ";

        // While filling, vanilla's per-item center messages are captured here instead of shown.
        private static bool _filling;
        private static readonly List<string> Captured = new List<string>();

        // Player.Interact passes alt = the AltPlace key (Left Shift by default) to every interactable,
        // but Switch ignores it. Shift + E on a smelter's ore or fuel switch becomes "fill".
        [HarmonyPatch(typeof(Switch), nameof(Switch.Interact))]
        [HarmonyPrefix]
        private static bool Interact_Prefix(Switch __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (!alt || hold || !QuickFillPlugin.Enabled.Value) return true;
            if (!character || character != Player.m_localPlayer) return true;

            Smelter smelter = __instance.GetComponentInParent<Smelter>();
            if (!smelter) return true;

            bool isOre = smelter.m_addOreSwitch == __instance;
            bool isFuel = smelter.m_addWoodSwitch == __instance;
            if (!isOre && !isFuel) return true;

            // Free slots from the smelter's synced state. Vanilla's own "is it full" check inside each add can
            // lag behind when another machine owns the smelter, so this cap is what keeps us from overfilling.
            int free = isOre
                ? smelter.m_maxOre - GetQueueSize(smelter)
                : Mathf.FloorToInt(smelter.m_maxFuel - GetFuel(smelter));
            if (free <= 0) return true; // vanilla says "It's full"

            AddCallback add = isOre ? OnAddOre : OnAddFuel;
            int added = 0;
            Captured.Clear();
            _filling = true;
            try
            {
                while (added < free && add(smelter, __instance, character, null))
                    added++;
            }
            finally
            {
                _filling = false;
            }

            __result = added > 0;
            character.Message(MessageHud.MessageType.Center, Summary(added));
            QuickFillPlugin.Debug($"Filled {smelter.m_name} ({(isOre ? "ore" : "fuel")}): {added} of {free} free");
            Captured.Clear();
            return false;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Message))]
        [HarmonyPrefix]
        private static bool Message_Prefix(MessageHud.MessageType type, string msg)
        {
            if (!_filling || type != MessageHud.MessageType.Center) return true;
            Captured.Add(msg);
            return false;
        }

        // "$msg_added Copper ore x7, Tin ore x3". If nothing was added, vanilla's reason
        // (no processable items, don't have any coal, ...) is passed through.
        private static string Summary(int added)
        {
            if (added == 0) return Captured.LastOrDefault() ?? "";

            IEnumerable<string> parts = Captured
                .Where(m => m.StartsWith(AddedPrefix, StringComparison.Ordinal))
                .Select(m => m.Substring(AddedPrefix.Length))
                .GroupBy(n => n)
                .Select(g => $"{g.Key} x{g.Count()}");
            return AddedPrefix + string.Join(", ", parts);
        }

        // A fill sends one vanilla add RPC per item, and the machine that owns the station plays the
        // "added" sound/effect for each one, e.g. 50 overlapping sounds for a windmill full of barley.
        // On the owner, play the effect for the first add of a burst and mute the rest.
        // Short enough not to mute normal E presses; a fill's RPCs are sent in one frame and arrive together.
        private const float EffectCooldown = 0.1f;
        private static readonly EffectList Silent = new EffectList();
        private static readonly Dictionary<Smelter, float> LastOreEffect = new Dictionary<Smelter, float>();
        private static readonly Dictionary<Smelter, float> LastFuelEffect = new Dictionary<Smelter, float>();

        private static bool Throttle(Smelter smelter, Dictionary<Smelter, float> last)
        {
            float now = Time.time;
            bool mute = last.TryGetValue(smelter, out float t) && now - t < EffectCooldown;
            if (!mute) last[smelter] = now;
            if (last.Count > 64) // forget destroyed stations
                foreach (Smelter dead in last.Keys.Where(k => !k).ToList()) last.Remove(dead);
            return mute;
        }

        [HarmonyPatch(typeof(Smelter), "RPC_AddOre")]
        [HarmonyPrefix]
        private static void RPC_AddOre_Prefix(Smelter __instance, out EffectList __state)
        {
            __state = null;
            if (!Throttle(__instance, LastOreEffect)) return;
            __state = __instance.m_oreAddedEffects;
            __instance.m_oreAddedEffects = Silent;
        }

        [HarmonyPatch(typeof(Smelter), "RPC_AddOre")]
        [HarmonyFinalizer]
        private static void RPC_AddOre_Finalizer(Smelter __instance, EffectList __state)
        {
            if (__state != null) __instance.m_oreAddedEffects = __state;
        }

        [HarmonyPatch(typeof(Smelter), "RPC_AddFuel")]
        [HarmonyPrefix]
        private static void RPC_AddFuel_Prefix(Smelter __instance, out EffectList __state)
        {
            __state = null;
            if (!Throttle(__instance, LastFuelEffect)) return;
            __state = __instance.m_fuelAddedEffects;
            __instance.m_fuelAddedEffects = Silent;
        }

        [HarmonyPatch(typeof(Smelter), "RPC_AddFuel")]
        [HarmonyFinalizer]
        private static void RPC_AddFuel_Finalizer(Smelter __instance, EffectList __state)
        {
            if (__state != null) __instance.m_fuelAddedEffects = __state;
        }

        [HarmonyPatch(typeof(Smelter), "OnHoverAddOre")]
        [HarmonyPostfix]
        private static void OnHoverAddOre_Postfix(ref string __result) => AppendHint(ref __result);

        [HarmonyPatch(typeof(Smelter), "OnHoverAddFuel")]
        [HarmonyPostfix]
        private static void OnHoverAddFuel_Postfix(ref string __result) => AppendHint(ref __result);

        private static void AppendHint(ref string text)
        {
            if (!QuickFillPlugin.Enabled.Value || !QuickFillPlugin.ShowHint.Value) return;
            text += Localization.instance.Localize("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Fill");
        }
    }
}
