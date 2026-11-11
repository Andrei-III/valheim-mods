using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace HarpoonReel
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Both players and the server need the mod: the pull runs on whichever machine owns the creature.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class HarpoonReelPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.harpoonreel";
        public const string Name = "HarpoonReel";
        public const string Version = "1.0.0";

        // Flag on the player's ZDO so the machine that owns the harpooned creature knows we're reeling.
        internal static readonly int ReelingZdoKey = "HarpoonReel_Reeling".GetStableHashCode();

        internal static BepInEx.Logging.ManualLogSource Log;

        // Local (per player)
        internal static ConfigEntry<KeyCode> ReelKey;

        // Gameplay (synced from the server so every machine computes the pull the same way)
        internal static ConfigEntry<bool> AutoReel;
        internal static ConfigEntry<float> ReelSpeed;
        internal static ConfigEntry<float> MinDistance;
        internal static ConfigEntry<float> StaminaMultiplier;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ReelKey = Config.Bind("1 - Controls", "ReelKey", KeyCode.LeftAlt,
                "Hold this key while a creature is harpooned to reel it in.");

            var admin = new ConfigurationManagerAttributes { IsAdminOnly = true };

            AutoReel = Config.Bind("2 - Reeling", "AutoReel", false,
                new ConfigDescription("Reel in automatically without holding the key.", null, admin));
            ReelSpeed = Config.Bind("2 - Reeling", "ReelSpeed", 6f,
                new ConfigDescription("How fast the line shortens while reeling (meters per second).",
                    new AcceptableValueRange<float>(0.5f, 30f), admin));
            MinDistance = Config.Bind("2 - Reeling", "MinDistance", 1.5f,
                new ConfigDescription("Reeling stops when the creature is this close (meters).",
                    new AcceptableValueRange<float>(0.5f, 10f), admin));
            StaminaMultiplier = Config.Bind("2 - Reeling", "StaminaMultiplier", 1f,
                new ConfigDescription("Multiplier on the vanilla harpoon stamina drain while reeling (0 = free).",
                    new AcceptableValueRange<float>(0f, 5f), admin));
            DebugLogging = Config.Bind("3 - Debug", "DebugLogging", false,
                "Log harpoon values (pull force, mass, distance) to BepInEx/LogOutput.log for tuning.");

            _harmony = Harmony.CreateAndPatchAll(typeof(HarpoonedPatches), Guid);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private void Update()
        {
            ReelInput.PublishLocalState();
        }
    }

    internal static class ReelInput
    {
        private static bool _lastPublished;

        /// <summary>True if the local player is holding the reel key and isn't typing / in a menu.</summary>
        internal static bool LocalWantsReel()
        {
            if (HarpoonReelPlugin.AutoReel.Value) return true;
            if (Chat.instance && Chat.instance.HasFocus()) return false;
            if (Console.IsVisible() || Menu.IsVisible() || TextInput.IsVisible()
                || InventoryGui.IsVisible() || Minimap.IsOpen()) return false;
            return ZInput.GetKey(HarpoonReelPlugin.ReelKey.Value, false);
        }

        /// <summary>Write the local reel state to our player's ZDO (only when it changes, to avoid network spam).</summary>
        internal static void PublishLocalState()
        {
            var player = Player.m_localPlayer;
            if (!player) return;
            var nview = player.GetComponent<ZNetView>();
            if (!nview || !nview.IsValid() || !nview.IsOwner()) return;

            bool reeling = LocalWantsReel();
            var zdo = nview.GetZDO();
            if (reeling != _lastPublished || zdo.GetBool(HarpoonReelPlugin.ReelingZdoKey) != reeling)
            {
                zdo.Set(HarpoonReelPlugin.ReelingZdoKey, reeling);
                _lastPublished = reeling;
            }
        }

        /// <summary>Is the given attacker currently reeling? Works for local and remote players.</summary>
        internal static bool IsReeling(Character attacker)
        {
            if (!attacker) return false;
            if (HarpoonReelPlugin.AutoReel.Value) return true;
            if (attacker == Player.m_localPlayer) return LocalWantsReel();
            var nview = attacker.GetComponent<ZNetView>();
            return nview && nview.IsValid() && nview.GetZDO().GetBool(HarpoonReelPlugin.ReelingZdoKey);
        }
    }
}
