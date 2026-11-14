using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace RepairAll
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Durability lives in the local player's inventory, so this is purely client-side.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class RepairAllPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.repairall";
        public const string Name = "RepairAll";
        public const string Version = "1.0.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<MessageStyle> Message;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1 - General", "Enabled", true,
                "Repair every item the current station can repair with one click. Set to false for the vanilla one-item-per-click behavior.");
            Message = Config.Bind("1 - General", "Message", MessageStyle.TopLeft,
                "Where to show the repair summary: TopLeft (small, in the pickup log), Center (big, vanilla style) or None.");
            DebugLogging = Config.Bind("2 - Debug", "DebugLogging", false,
                "Log each repaired item to BepInEx/LogOutput.log.");

            _harmony = Harmony.CreateAndPatchAll(typeof(RepairPatches), Guid);
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

    internal enum MessageStyle { TopLeft, Center, None }

    [HarmonyPatch]
    internal static class RepairPatches
    {
        private static readonly Func<InventoryGui, bool> HaveRepairableItems =
            AccessTools.MethodDelegate<Func<InventoryGui, bool>>(
                AccessTools.Method(typeof(InventoryGui), "HaveRepairableItems"));

        private static readonly Func<InventoryGui, ItemDrop.ItemData, bool> CanRepair =
            AccessTools.MethodDelegate<Func<InventoryGui, ItemDrop.ItemData, bool>>(
                AccessTools.Method(typeof(InventoryGui), "CanRepair"));

        // Goes through Harmony's patched method, so calling it re-enters our prefix (which then lets vanilla run).
        private static readonly Action<InventoryGui> RepairOneItem =
            AccessTools.MethodDelegate<Action<InventoryGui>>(
                AccessTools.Method(typeof(InventoryGui), "RepairOneItem"));

        // True while we drive vanilla RepairOneItem in a loop: vanilla runs as-is, but its per-item
        // center message and sound are swallowed so the player gets one summary instead of a burst.
        private static bool _repairingAll;

        private static readonly List<ItemDrop.ItemData> Worn = new List<ItemDrop.ItemData>();

        // The repair button calls RepairOneItem() once per click. Replace that with "repeat until done",
        // reusing the vanilla method each time, so station checks, skill gain and other mods' patches still apply.
        [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
        [HarmonyPrefix]
        private static bool RepairOneItem_Prefix(InventoryGui __instance)
        {
            if (_repairingAll || !RepairAllPlugin.Enabled.Value) return true;

            Player player = Player.m_localPlayer;
            if (!player || !HaveRepairableItems(__instance)) return true; // vanilla shows "No more item to repair"

            CraftingStation station = player.GetCurrentCraftingStation();
            EffectList stationEffects = station ? station.m_repairItemDoneEffects : null;

            int repaired = 0;
            string lastName = null;
            _repairingAll = true;
            try
            {
                if (station) station.m_repairItemDoneEffects = new EffectList();

                // Each vanilla call repairs exactly one item; the cap only guards against a mod
                // that makes an item permanently "repairable" without ever fixing it.
                int cap = CountWorn(player) + 1;
                while (repaired < cap && HaveRepairableItems(__instance))
                {
                    ItemDrop.ItemData next = FirstRepairable(__instance, player);
                    RepairOneItem(__instance);
                    if (next != null && next.m_durability >= next.GetMaxDurability())
                    {
                        lastName = next.m_shared.m_name;
                        RepairAllPlugin.Debug($"Repaired {Localization.instance.Localize(lastName)}");
                    }
                    repaired++;
                }
            }
            finally
            {
                _repairingAll = false;
                if (station) station.m_repairItemDoneEffects = stationEffects;
            }

            if (repaired > 0 && station)
                stationEffects?.Create(station.transform.position, Quaternion.identity);

            switch (RepairAllPlugin.Message.Value)
            {
                case MessageStyle.TopLeft:
                    // The top-left log shows one line per message, so split the summary into lines.
                    foreach (string line in Summary(__instance, player, repaired, lastName).Split('\n'))
                        player.Message(MessageHud.MessageType.TopLeft, line);
                    break;
                case MessageStyle.Center:
                    player.Message(MessageHud.MessageType.Center, Summary(__instance, player, repaired, lastName));
                    break;
            }
            return false;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Message))]
        [HarmonyPrefix]
        private static bool Message_Prefix(MessageHud.MessageType type)
        {
            return !(_repairingAll && type == MessageHud.MessageType.Center);
        }

        private static string Summary(InventoryGui gui, Player player, int repaired, string lastName)
        {
            string text = repaired == 1 && lastName != null
                ? Localization.instance.Localize("$msg_repaired", lastName)
                : $"Repaired {repaired} items";

            // Worn items this station can't handle (wrong station type or too low level).
            int leftOver = 0;
            Worn.Clear();
            player.GetInventory().GetWornItems(Worn);
            foreach (ItemDrop.ItemData item in Worn)
                if (item.m_shared.m_canBeReparied && !CanRepair(gui, item)) leftOver++;
            Worn.Clear();

            if (leftOver > 0)
                text += $"\n{leftOver} item{(leftOver == 1 ? "" : "s")} can't be repaired here";
            return text;
        }

        private static ItemDrop.ItemData FirstRepairable(InventoryGui gui, Player player)
        {
            Worn.Clear();
            player.GetInventory().GetWornItems(Worn);
            ItemDrop.ItemData found = null;
            foreach (ItemDrop.ItemData item in Worn)
            {
                if (CanRepair(gui, item)) { found = item; break; }
            }
            Worn.Clear();
            return found;
        }

        private static int CountWorn(Player player)
        {
            Worn.Clear();
            player.GetInventory().GetWornItems(Worn);
            int n = Worn.Count;
            Worn.Clear();
            return n;
        }
    }
}
