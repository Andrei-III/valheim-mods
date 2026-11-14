using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace QuietAoe
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Only removes a log line on whichever machine runs the physics contact; no gameplay or network change.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class QuietAoePlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.quietaoe";
        public const string Name = "QuietAoe";
        public const string Version = "1.0.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Report;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("1 - General", "Enabled", true,
                "Skip vanilla's 'AOE got OnTriggerStay but trigger damage is disabled' warning, which can be " +
                "written every physics tick (and cause stutter) while something touches such an object.");
            Report = Config.Bind("2 - Debug", "DebugLogging", false,
                "Log each offending object once (prefab, position, what touched it) and a count of skipped warnings every 30 s.");
            _harmony = Harmony.CreateAndPatchAll(typeof(AoePatches), Guid);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private void Update() => AoePatches.FlushCounts();
    }

    [HarmonyPatch]
    internal static class AoePatches
    {
        private static readonly HashSet<int> Reported = new HashSet<int>();
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static float _nextFlush;

        // Vanilla Aoe.CauseTriggerDamage: when m_useTriggers is false the only thing it ever does is
        // ZLog.LogWarning(...). Skipping the method in that case changes nothing but the log.
        [HarmonyPatch(typeof(Aoe), "CauseTriggerDamage")]
        [HarmonyPrefix]
        private static bool CauseTriggerDamage_Prefix(Aoe __instance, Collider collider)
        {
            if (__instance.m_useTriggers || !QuietAoePlugin.Enabled.Value) return true;
            if (QuietAoePlugin.Report.Value) Note(__instance, collider);
            return false;
        }

        private static void Note(Aoe aoe, Collider other)
        {
            string prefab = PrefabName(aoe.gameObject);
            Counts[prefab] = Counts.TryGetValue(prefab, out int n) ? n + 1 : 1;
            if (!Reported.Add(aoe.GetInstanceID())) return;

            QuietAoePlugin.Log.LogInfo(
                $"Aoe without trigger damage touched: object '{Path(aoe.transform)}' prefab '{prefab}' " +
                $"at {aoe.transform.position:F1}, touched by '{(other ? PrefabName(other.gameObject) : "?")}' " +
                $"({(other ? Path(other.transform) : "?")}, layer {(other ? LayerMask.LayerToName(other.gameObject.layer) : "?")})");
        }

        internal static void FlushCounts()
        {
            if (Time.time < _nextFlush) return;
            _nextFlush = Time.time + 30f;
            if (Counts.Count == 0) return;
            var sb = new StringBuilder("Skipped Aoe warnings in the last 30 s:");
            foreach (var kv in Counts) sb.Append($" {kv.Key} x{kv.Value};");
            QuietAoePlugin.Log.LogInfo(sb.ToString());
            Counts.Clear();
        }

        private static string PrefabName(GameObject go)
        {
            var nview = go.GetComponentInParent<ZNetView>();
            if (nview) return Utils.GetPrefabName(nview.gameObject);
            return go.transform.root.name;
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null && parts.Count < 6; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
