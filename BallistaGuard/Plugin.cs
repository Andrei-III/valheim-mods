using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace BallistaGuard
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Target selection runs only on the machine that owns the turret; nothing is sent over the network.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class BallistaGuardPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.ballistaguard";
        public const string Name = "BallistaGuard";
        public const string Version = "1.1.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> TargetPlayers;
        internal static ConfigEntry<bool> TargetTamed;
        internal static ConfigEntry<bool> TargetPassiveAnimals;
        internal static ConfigEntry<bool> TargetDverger;
        internal static ConfigEntry<string> HoverLabel;
        internal static ConfigEntry<string> AlwaysTarget;
        internal static ConfigEntry<bool> FullCircle;
        internal static ConfigEntry<float> TurnRateMultiplier;
        internal static ConfigEntry<float> FireRateMultiplier;
        internal static ConfigEntry<int> MaxAmmo;
        internal static readonly HashSet<string> AlwaysTargetNames =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private static void ParseAlwaysTarget()
        {
            AlwaysTargetNames.Clear();
            foreach (string s in AlwaysTarget.Value.Split(','))
                if (s.Trim().Length > 0) AlwaysTargetNames.Add(s.Trim());
        }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("1 - General", "Enabled", true,
                "When a turret (ballista) has no trophy set, shoot only hostile creatures instead of everything. " +
                "Turrets with trophies keep the vanilla behavior.");
            TargetPlayers = Config.Bind("2 - Targets", "TargetPlayers", false,
                "Also shoot players.");
            TargetTamed = Config.Bind("2 - Targets", "TargetTamed", false,
                "Also shoot tamed creatures and staff summons.");
            TargetPassiveAnimals = Config.Bind("2 - Targets", "TargetPassiveAnimals", false,
                "Also shoot animals of the AnimalsVeg faction (deer, boars, chickens, ...) that are not tamed.");
            TargetDverger = Config.Bind("2 - Targets", "TargetDverger", false,
                "Also shoot Dvergr that are not angry at players. Angry Dvergr are always shot.");
            AlwaysTarget = Config.Bind("2 - Targets", "AlwaysTarget", "Boar,Neck,Greyling",
                "Comma-separated creature prefab names that are always shot (unless tamed), whatever their faction. " +
                "Examples: Boar, Neck, Greyling, Deer, Hare, Hen.");
            AlwaysTarget.SettingChanged += (_, __) => ParseAlwaysTarget();
            ParseAlwaysTarget();
            FullCircle = Config.Bind("1 - General", "FullCircle", true,
                "Turrets (with or without trophies) see and aim all around them instead of only 50 degrees to " +
                "each side of where they face. Range is unchanged.");
            TurnRateMultiplier = Config.Bind("1 - General", "TurnRateMultiplier", 2f,
                new ConfigDescription("Multiplies how fast turrets turn (vanilla ballista: 45 degrees per second). " +
                    "1 = vanilla, 2 = a half turn in about 2 seconds instead of 4.",
                    new AcceptableValueRange<float>(0.5f, 5f)));
            FireRateMultiplier = Config.Bind("1 - General", "FireRateMultiplier", 1.25f,
                new ConfigDescription("Multiplies how often turrets fire (vanilla ballista: one bolt every 2 s). " +
                    "1 = vanilla, 1.25 = every 1.6 s, 2 = every 1 s. Ammo runs out faster accordingly.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            MaxAmmo = Config.Bind("1 - General", "MaxAmmo", 100,
                new ConfigDescription("How many bolts a turret holds (vanilla ballista: 40). 0 = vanilla. " +
                    "The cap is checked by whoever loads the turret, so players without the mod still stop at 40.",
                    new AcceptableValueRange<int>(0, 1000)));
            HoverLabel = Config.Bind("1 - General", "HoverLabel", "hostiles",
                "Shown as the turret's target in its hover text when no trophy is set (vanilla shows 'everything').");
            _harmony = Harmony.CreateAndPatchAll(typeof(TurretPatches), Guid);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }

    [HarmonyPatch]
    internal static class TurretPatches
    {
        private static readonly AccessTools.FieldRef<Turret, List<Character>> TargetCharacters =
            AccessTools.FieldRefAccess<Turret, List<Character>>("m_targetCharacters");

        // Set while Turret.UpdateTarget runs, so the FindClosestCreature patch only affects turrets.
        private static Turret _current;

        private static readonly AccessTools.FieldRef<Turret, Character> Target =
            AccessTools.FieldRefAccess<Turret, Character>("m_target");

        // Vanilla only compares against m_maxAmmo when loading (Turret.UseItem, on the loading player's
        // machine); the owner's RPC_AddAmmo just adds one. So raising it locally is all that's needed.
        private static readonly Dictionary<Turret, int> VanillaMaxAmmo = new Dictionary<Turret, int>();

        [HarmonyPatch(typeof(Turret), "Awake")]
        [HarmonyPostfix]
        private static void Awake_Postfix(Turret __instance) => ApplyMaxAmmo(__instance);

        // Re-applied on hover so config changes take effect without rebuilding the turret.
        internal static void ApplyMaxAmmo(Turret t)
        {
            if (!VanillaMaxAmmo.TryGetValue(t, out int vanilla))
            {
                if (VanillaMaxAmmo.Count > 256)
                    foreach (Turret dead in new List<Turret>(VanillaMaxAmmo.Keys)) if (!dead) VanillaMaxAmmo.Remove(dead);
                vanilla = t.m_maxAmmo;
                VanillaMaxAmmo[t] = vanilla;
            }
            if (vanilla <= 0) return; // 0 = unlimited ammo, leave alone
            int wanted = BallistaGuardPlugin.MaxAmmo.Value;
            t.m_maxAmmo = wanted > 0 ? Mathf.Max(wanted, vanilla) : vanilla;
        }

        [HarmonyPatch(typeof(Turret), "UpdateTarget")]
        [HarmonyPrefix]
        private static void UpdateTarget_Prefix(Turret __instance, out float __state)
        {
            _current = __instance;
            __state = WidenArc(__instance);
        }

        [HarmonyPatch(typeof(Turret), "UpdateTarget")]
        [HarmonyFinalizer]
        private static void UpdateTarget_Finalizer(Turret __instance, float __state)
        {
            _current = null;
            __instance.m_horizontalAngle = __state;
        }

        // FullCircle: vanilla uses m_horizontalAngle as the half-width of the search cone, the aim clamp,
        // the idle sweep and the ground marker. We widen it to 180 only while searching, while aiming at a
        // target and while drawing the marker. The idle sweep keeps the vanilla angle (at 180 both sweep
        // ends would point straight backwards).
        private static float WidenArc(Turret t)
        {
            float original = t.m_horizontalAngle;
            if (BallistaGuardPlugin.FullCircle.Value && t.m_targetEnemies && original >= 0f)
                t.m_horizontalAngle = 180f;
            return original;
        }

        [HarmonyPatch(typeof(Turret), "UpdateTurretRotation")]
        [HarmonyPrefix]
        private static void UpdateTurretRotation_Prefix(Turret __instance, out Vector2 __state)
        {
            // x = arc half-width, y = turn rate (deg/s); both restored in the finalizer.
            __state = new Vector2(__instance.m_horizontalAngle, __instance.m_turnRate);
            if (Target(__instance)) WidenArc(__instance);
            if (__instance.m_targetEnemies) __instance.m_turnRate *= BallistaGuardPlugin.TurnRateMultiplier.Value;
        }

        [HarmonyPatch(typeof(Turret), "UpdateTurretRotation")]
        [HarmonyFinalizer]
        private static void UpdateTurretRotation_Finalizer(Turret __instance, Vector2 __state)
        {
            __instance.m_horizontalAngle = __state.x;
            __instance.m_turnRate = __state.y;
        }

        // Every cooldown check (firing, aiming pause, armed/unarmed visual, reload sound) goes through
        // IsCoolingDown, so shortening m_attackCooldown just for this call speeds all of them up consistently.
        [HarmonyPatch(typeof(Turret), nameof(Turret.IsCoolingDown))]
        [HarmonyPrefix]
        private static void IsCoolingDown_Prefix(Turret __instance, out float __state)
        {
            __state = __instance.m_attackCooldown;
            if (__instance.m_targetEnemies)
                __instance.m_attackCooldown /= BallistaGuardPlugin.FireRateMultiplier.Value;
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.IsCoolingDown))]
        [HarmonyFinalizer]
        private static void IsCoolingDown_Finalizer(Turret __instance, float __state) =>
            __instance.m_attackCooldown = __state;

        [HarmonyPatch(typeof(Turret), "UpdateMarker")]
        [HarmonyPrefix]
        private static void UpdateMarker_Prefix(Turret __instance, out float __state) => __state = WidenArc(__instance);

        [HarmonyPatch(typeof(Turret), "UpdateMarker")]
        [HarmonyFinalizer]
        private static void UpdateMarker_Finalizer(Turret __instance, float __state) =>
            __instance.m_horizontalAngle = __state;

        // Same search as vanilla BaseAI.FindClosestCreature (closest sensed, awake, living character),
        // with the turret's include flags replaced by the hostility filter below.
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindClosestCreature))]
        [HarmonyPrefix]
        private static bool FindClosestCreature_Prefix(ref Character __result, Transform me, Vector3 eyePoint,
            float hearRange, float viewRange, float viewAngle, bool alerted, bool mistVision, bool passiveAggresive,
            bool includeEnemies, List<Character> onlyTargets)
        {
            if (_current == null || !BallistaGuardPlugin.Enabled.Value) return true;
            if (onlyTargets != null && onlyTargets.Count > 0) return true; // trophy set: vanilla
            if (!includeEnemies) return true; // not a defensive turret

            Character best = null;
            float bestDist = float.MaxValue;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null) continue;
                float d = Vector3.Distance(c.transform.position, me.position);
                if (d > viewRange || d >= bestDist) continue; // cheap reject first
                if (c.IsDead() || !IsHostile(c)) continue;
                BaseAI ai = c.GetBaseAI();
                if (ai != null && ai.IsSleeping()) continue;
                if (!BaseAI.CanSenseTarget(me, eyePoint, hearRange, viewRange, viewAngle, alerted, mistVision, c,
                        passiveAggresive, isTamed: false)) continue;
                best = c; // closer than the previous best (checked above)
                bestDist = d;
            }
            __result = best;
            return false;
        }

        private static bool IsHostile(Character c)
        {
            if (c is Player) return BallistaGuardPlugin.TargetPlayers.Value;
            if (c.IsTamed()) return BallistaGuardPlugin.TargetTamed.Value;
            if (BallistaGuardPlugin.AlwaysTargetNames.Count > 0 &&
                BallistaGuardPlugin.AlwaysTargetNames.Contains(Utils.GetPrefabName(c.gameObject))) return true;
            switch (c.GetFaction())
            {
                case Character.Faction.Players:
                case Character.Faction.TrainingDummy:
                    return false;
                case Character.Faction.PlayerSpawned:
                    return BallistaGuardPlugin.TargetTamed.Value;
                case Character.Faction.AnimalsVeg:
                    return BallistaGuardPlugin.TargetPassiveAnimals.Value;
                case Character.Faction.Dverger:
                    BaseAI ai = c.GetBaseAI();
                    return BallistaGuardPlugin.TargetDverger.Value || (ai != null && ai.IsAggravated());
                default:
                    return true; // monsters of every biome, sea monsters, bosses
            }
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.GetHoverText))]
        [HarmonyPrefix]
        private static void GetHoverText_Prefix(Turret __instance) => ApplyMaxAmmo(__instance);

        [HarmonyPatch(typeof(Turret), nameof(Turret.GetHoverText))]
        [HarmonyPostfix]
        private static void GetHoverText_Postfix(Turret __instance, ref string __result)
        {
            if (!BallistaGuardPlugin.Enabled.Value || string.IsNullOrEmpty(__result)) return;
            if (!__instance.m_targetEnemies || TargetCharacters(__instance).Count > 0) return;
            string everything = Localization.instance.Localize("$piece_turret_target_everything");
            if (everything.Length > 0)
                __result = __result.Replace(everything, BallistaGuardPlugin.HoverLabel.Value);
        }
    }
}
