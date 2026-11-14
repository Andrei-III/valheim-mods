using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;

namespace StayLoaded
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // The loaded state lives on the local player (synced through the player's own ZDO), so nobody else needs the mod.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class StayLoadedPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.stayloaded";
        public const string Name = "StayLoaded";
        public const string Version = "1.0.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1 - General", "Enabled", true,
                "Keep reload weapons (crossbows etc.) loaded when you sheathe them or switch to another weapon.");
            DebugLogging = Config.Bind("2 - Debug", "DebugLogging", false,
                "Log when a weapon's loaded state is remembered, restored or used up.");

            _harmony = Harmony.CreateAndPatchAll(typeof(LoadPatches), Guid);
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

    // Vanilla keeps a single Player.m_weaponLoaded and clears it in two places when the weapon goes away:
    //   - Humanoid.UnequipItem -> ResetLoadedWeapon()   (sheathing with R, switching weapons)
    //   - Player.UpdateWeaponLoading(null / non-reload weapon) -> SetWeaponLoaded(null)   (every frame while sheathed)
    // Firing clears it through Attack -> ResetLoadedWeapon(). We remember each loaded item separately,
    // forget it only when it's fired, and put it back when that item is drawn again.
    [HarmonyPatch]
    internal static class LoadPatches
    {
        // Items that were loaded and haven't been fired since. Weak keys: dropped/destroyed items just disappear.
        private static readonly ConditionalWeakTable<ItemDrop.ItemData, object> Loaded =
            new ConditionalWeakTable<ItemDrop.ItemData, object>();
        private static readonly object Marker = new object();

        private static readonly AccessTools.FieldRef<Player, ItemDrop.ItemData> WeaponLoaded =
            AccessTools.FieldRefAccess<Player, ItemDrop.ItemData>("m_weaponLoaded");

        private static readonly System.Action<Player, ItemDrop.ItemData> SetWeaponLoaded =
            AccessTools.MethodDelegate<System.Action<Player, ItemDrop.ItemData>>(
                AccessTools.Method(typeof(Player), "SetWeaponLoaded"));

        private static readonly System.Action<Player> CancelReloadAction =
            AccessTools.MethodDelegate<System.Action<Player>>(
                AccessTools.Method(typeof(Player), "CancelReloadAction"));

        // Depth counter: true while inside UnequipItem, where a reset means "put away", not "fired".
        private static int _unequipping;

        private static bool IsLocal(Player p) => p && p == Player.m_localPlayer;

        // Remember every weapon that finishes loading.
        [HarmonyPatch(typeof(Player), "SetWeaponLoaded")]
        [HarmonyPostfix]
        private static void SetWeaponLoaded_Postfix(Player __instance, ItemDrop.ItemData weapon)
        {
            if (weapon == null || !IsLocal(__instance)) return;
            if (!Loaded.TryGetValue(weapon, out _))
            {
                Loaded.Add(weapon, Marker);
                StayLoadedPlugin.Debug($"Remembered loaded: {weapon.m_shared.m_name}");
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        [HarmonyPrefix]
        private static void UnequipItem_Prefix() => _unequipping++;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        [HarmonyFinalizer]
        private static void UnequipItem_Finalizer() => _unequipping--;

        // A reset outside of unequipping means the shot was fired (or something else really emptied it): forget it.
        [HarmonyPatch(typeof(Player), nameof(Player.ResetLoadedWeapon))]
        [HarmonyPrefix]
        private static void ResetLoadedWeapon_Prefix(Player __instance)
        {
            if (_unequipping > 0 || !IsLocal(__instance)) return;
            ItemDrop.ItemData weapon = WeaponLoaded(__instance);
            if (weapon != null && Loaded.Remove(weapon))
                StayLoadedPlugin.Debug($"Fired, no longer loaded: {weapon.m_shared.m_name}");
        }

        // Runs every frame with the weapon in hand. If it's one we remember as loaded, mark it loaded
        // before vanilla gets a chance to queue a reload.
        [HarmonyPatch(typeof(Player), "UpdateWeaponLoading")]
        [HarmonyPrefix]
        private static void UpdateWeaponLoading_Prefix(Player __instance, ItemDrop.ItemData weapon)
        {
            if (!StayLoadedPlugin.Enabled.Value || !IsLocal(__instance)) return;
            if (weapon == null || !weapon.m_shared.m_attack.m_requiresReload) return;
            if (WeaponLoaded(__instance) == weapon || !Loaded.TryGetValue(weapon, out _)) return;

            CancelReloadAction(__instance);
            SetWeaponLoaded(__instance, weapon);
            StayLoadedPlugin.Debug($"Restored loaded: {weapon.m_shared.m_name}");
        }
    }
}
