using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;

namespace SummonTracker
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Purely a client-side HUD: reads synced data only, so nobody else needs the mod.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class SummonTrackerPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.summontracker";
        public const string Name = "SummonTracker";
        public const string Version = "1.0.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        internal static ConfigEntry<bool> ShowDistance;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<float> FarDistance;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ShowDistance = Config.Bind("1 - Display", "ShowDistance", true,
                "Show how far away each summon is.");
            Scale = Config.Bind("1 - Display", "Scale", 1f,
                new ConfigDescription("Size multiplier for the summon list.",
                    new AcceptableValueRange<float>(0.5f, 2f)));
            FarDistance = Config.Bind("1 - Display", "FarDistance", 40f,
                new ConfigDescription("Summons farther than this (meters) get a highlighted distance, so stragglers stand out.",
                    new AcceptableValueRange<float>(5f, 200f)));
            DebugLogging = Config.Bind("2 - Debug", "DebugLogging", false,
                "Log tracked/untracked summons to BepInEx/LogOutput.log.");

            _harmony = Harmony.CreateAndPatchAll(typeof(SpawnPatches), Guid);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            SummonHud.Destroy();
        }

        private void Update()
        {
            SummonRegistry.Update();
        }

        private void LateUpdate()
        {
            SummonHud.Update();
        }

        internal static void Debug(string msg)
        {
            if (DebugLogging.Value) Log.LogInfo(msg);
        }
    }

    [HarmonyPatch]
    internal static class SpawnPatches
    {
        // SpawnAbility.Spawn() calls SetupAoe(spawnedCharacter, point) once for every creature it creates,
        // right after the creature is instantiated and leveled. That's the one hook that covers every staff summon.
        [HarmonyPatch(typeof(SpawnAbility), "SetupAoe")]
        [HarmonyPrefix]
        private static void SetupAoe_Prefix(SpawnAbility __instance, Character owner,
            Character ___m_owner, ItemDrop.ItemData ___m_weapon)
        {
            if (!owner || !___m_owner || ___m_owner != Player.m_localPlayer) return;
            SummonRegistry.TrackNewSummon(owner, ___m_weapon);
        }
    }
}
