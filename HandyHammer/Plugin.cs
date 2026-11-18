using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace HandyHammer
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency(BuildCameraPatches.BuildCamGuid, BepInDependency.DependencyFlags.SoftDependency)]
    // Only changes the local player's build selection and hover target; nothing is synced.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class HandyHammerPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.handyhammer";
        public const string Name = "HandyHammer";
        public const string Version = "1.1.1";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> RepairOnEquip;
        internal static ConfigEntry<bool> InteractInBuildMode;
        internal static ConfigEntry<float> CameraInteractDistance;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            RepairOnEquip = Config.Bind("1 - General", "RepairOnEquip", true,
                "Every time you equip the hammer (or another build tool that has a repair mode), it starts in repair mode " +
                "instead of on the last piece you built.");
            InteractInBuildMode = Config.Bind("1 - General", "InteractInBuildMode", true,
                "With a build tool equipped, E still uses whatever you look at (doors, portals, chests, stations, items...), " +
                "like with any other item in hand. Also works with Atze's Build Camera on.");
            CameraInteractDistance = Config.Bind("1 - General", "BuildCameraInteractDistance", 10f,
                new ConfigDescription("With Atze's Build Camera on: how far from the camera, in meters, E reaches. " +
                    "Pickables and dropped items keep using that mod's own setting.",
                    new AcceptableValueRange<float>(2f, 50f)));
            _harmony = Harmony.CreateAndPatchAll(typeof(HammerPatches), Guid);
            _harmony.PatchAll(typeof(BuildCameraPatches)); // skipped (Prepare) when Build Camera isn't installed
            _harmony.PatchAll(typeof(BuildCameraFreeze));
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }

    [HarmonyPatch]
    internal static class HammerPatches
    {
        // EquipItem calls SetupEquipment -> SetPlaceMode -> UpdateAvailablePiecesList before returning,
        // so the tool's piece lists are ready here. ShowHandItems (unsheathing) also goes through EquipItem.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        [HarmonyPostfix]
        private static void EquipItem_Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || !HandyHammerPlugin.RepairOnEquip.Value) return;
            if (!(__instance is Player player) || player != Player.m_localPlayer) return;
            PieceTable table = item?.m_shared?.m_buildPieces;
            if (!table || !player.InPlaceMode()) return;

            Piece repair = null;
            foreach (GameObject go in table.m_pieces)
            {
                Piece p = go ? go.GetComponent<Piece>() : null;
                if (p && p.m_repairPiece) { repair = p; break; }
            }
            if (repair) player.SetSelectedPiece(repair);
        }

        private delegate void FindHoverDelegate(Player player, out GameObject hover, out Character hoverCreature);

        private static readonly FindHoverDelegate FindHoverObject =
            AccessTools.MethodDelegate<FindHoverDelegate>(AccessTools.Method(typeof(Player), "FindHoverObject"));
        private static readonly AccessTools.FieldRef<Player, GameObject> Hovering =
            AccessTools.FieldRefAccess<Player, GameObject>("m_hovering");
        private static readonly AccessTools.FieldRef<Player, Character> HoveringCreature =
            AccessTools.FieldRefAccess<Player, Character>("m_hoveringCreature");

        // Vanilla clears the hover target whenever a build tool is out, which is what makes E do nothing.
        // Run the normal hover raycast instead; Player.Interact itself has no build-mode check.
        [HarmonyPatch(typeof(Player), "UpdateHover")]
        [HarmonyPrefix]
        private static bool UpdateHover_Prefix(Player __instance)
        {
            if (!HandyHammerPlugin.InteractInBuildMode.Value || !__instance.InPlaceMode()) return true;
            if (__instance.IsDead() || __instance.GetDoodadController() != null) return true; // vanilla clears

            FindHoverObject(__instance, out GameObject hover, out Character creature);
            Hovering(__instance) = hover;
            HoveringCreature(__instance) = creature;
            return false;
        }
    }

    // Atze's Build Camera (soft dependency). While its camera is on, its Player.Update prefix replaces the
    // whole vanilla Update (so UpdateHover above never runs) and does its own hover + E, but only for
    // Pickables and ItemDrops. This prefix runs right after it and handles everything else.
    [HarmonyPatch]
    internal static class BuildCameraPatches
    {
        internal const string BuildCamGuid = "AtzesBuildCamMod";

        private static readonly Func<bool> IsCameraOn = MakeIsCameraOn();
        private static readonly Func<bool> CamHandlesPickables = MakeCamHandlesPickables();

        private static Func<bool> MakeIsCameraOn()
        {
            MethodInfo m = AccessTools.Method(AccessTools.TypeByName("Valheim_Build_Camera.gosub"), "isInBuildMode");
            return m == null ? null : AccessTools.MethodDelegate<Func<bool>>(m);
        }

        // Build Camera's own "Switch_Interactibility_PickUp" setting: when on, it already uses pickables/items on E.
        private static Func<bool> MakeCamHandlesPickables()
        {
            Type load = AccessTools.TypeByName("Valheim_Build_Camera.load");
            if (load == null) return () => false;
            return () =>
            {
                try { return Traverse.Create(load).Field("getCfg").Property("boolCamInteractEnable").GetValue<bool>(); }
                catch { return false; }
            };
        }

        internal static bool CameraOn()
        {
            if (IsCameraOn == null) return false;
            try { return IsCameraOn(); } catch { return false; }
        }

        private static bool Prepare() => IsCameraOn != null;

        private delegate void InteractDelegate(Player player, GameObject go, bool hold, bool alt);
        private static readonly InteractDelegate Interact =
            AccessTools.MethodDelegate<InteractDelegate>(AccessTools.Method(typeof(Player), "Interact"));
        private static readonly Func<Player, bool> TakeInput =
            AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));
        private static readonly AccessTools.FieldRef<Player, int> InteractMask =
            AccessTools.FieldRefAccess<Player, int>("m_interactMask");
        private static readonly AccessTools.FieldRef<Player, GameObject> Hovering =
            AccessTools.FieldRefAccess<Player, GameObject>("m_hovering");
        private static readonly AccessTools.FieldRef<Player, Character> HoveringCreature =
            AccessTools.FieldRefAccess<Player, Character>("m_hoveringCreature");

        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly System.Collections.Generic.IComparer<RaycastHit> HitComparer =
            System.Collections.Generic.Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        [HarmonyPatch(typeof(Player), "Update")]
        [HarmonyPrefix]
        [HarmonyAfter(BuildCamGuid)]
        private static void Update_Prefix(Player __instance)
        {
            if (!HandyHammerPlugin.InteractInBuildMode.Value || __instance != Player.m_localPlayer) return;
            if (!CameraOn() || !__instance.InPlaceMode() || __instance.IsDead() || !GameCamera.instance) return;
            if (!TakeInput(__instance)) return; // chat, menus, inventory open (Build Camera returns early too)

            bool camPickables = CamHandlesPickables();
            if (camPickables && Hovering(__instance)) return; // Build Camera found a pickable/item and handled E

            GameObject hover = FindHover(__instance, camPickables);
            Hovering(__instance) = hover;
            HoveringCreature(__instance) = null;
            if (!hover || Hud.InRadial()) return;

            bool alt = ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive()
                ? ZInput.GetButton("JoyAltKeys")
                : ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace");
            if (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse"))
                Interact(__instance, hover, false, alt);
            else if (ZInput.GetButton("Use") || ZInput.GetButton("JoyUse"))
                Interact(__instance, hover, true, alt);
        }

        // Same as vanilla FindHoverObject, but the distance is measured from the camera, not the character.
        private static GameObject FindHover(Player player, bool skipPickables)
        {
            Transform cam = GameCamera.instance.transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, Hits, 50f, InteractMask(player));
            Array.Sort(Hits, 0, n, HitComparer);
            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = Hits[i];
                Collider col = hit.collider;
                if (col.attachedRigidbody && col.attachedRigidbody.gameObject == player.gameObject) continue;

                Hoverable hoverable = col.GetComponentInParent<Hoverable>();
                if (hoverable == null) return null;
                if (skipPickables && (col.GetComponentInParent<Pickable>() || col.GetComponentInParent<ItemDrop>()))
                    return null;
                if (Vector3.Distance(cam.position, hit.point) >= HandyHammerPlugin.CameraInteractDistance.Value + hoverable.GetHoverOffset())
                    return null;
                if (col.GetComponent<Hoverable>() != null) return col.gameObject;
                return col.attachedRigidbody ? col.attachedRigidbody.gameObject : col.gameObject;
            }
            return null;
        }

        // Vanilla closes an open chest when the character is more than 4 m from it. With the camera on, the
        // character can be far away, so don't auto-close then (the vanilla rule applies again once it's off).
        [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
        [HarmonyPrefix]
        private static void UpdateContainer_Prefix(InventoryGui __instance, out float __state)
        {
            __state = -1f;
            if (!HandyHammerPlugin.InteractInBuildMode.Value || !CameraOn()) return;
            __state = __instance.m_autoCloseDistance;
            __instance.m_autoCloseDistance = float.MaxValue;
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
        [HarmonyFinalizer]
        private static void UpdateContainer_Finalizer(InventoryGui __instance, float __state)
        {
            if (__state >= 0f) __instance.m_autoCloseDistance = __state;
        }
    }

    // Build Camera moves and turns its camera from the mouse and WASD/Space/Ctrl every frame, only pausing for
    // the game menu, console and build menu. With a chest open (or inventory, chat, map, a sign's text box...)
    // that made the view follow the cursor and Ctrl (crouch) sink it into the ground. Skip its camera update
    // whenever vanilla wouldn't let the character take input either (Player.TakeInput).
    [HarmonyPatch]
    internal static class BuildCameraFreeze
    {
        private static readonly MethodInfo Target =
            AccessTools.Method(AccessTools.TypeByName("Valheim_Build_Camera.gosub"), "updateBuildCamera");
        private static readonly Func<Player, bool> TakeInput =
            AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        private static bool Prepare() => Target != null;
        private static MethodBase TargetMethod() => Target;

        private static bool Prefix()
        {
            Player player = Player.m_localPlayer;
            return !player || TakeInput(player);
        }
    }
}
