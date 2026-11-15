using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace DamagePreview
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency("andrei.valheim.arrowarc", BepInDependency.DependencyFlags.SoftDependency)]
    // Purely visual: reads data the client already has and draws on the local HUD, so nobody else needs it.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class DamagePreviewPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.damagepreview";
        public const string Name = "DamagePreview";
        public const string Version = "1.1.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> IncludeCrossbows;
        internal static ConfigEntry<float> MaxRange;
        internal static ConfigEntry<bool> UseArrowArc;
        internal static ConfigEntry<Color> GuaranteedColor;
        internal static ConfigEntry<Color> PossibleColor;
        internal static ConfigEntry<Color> KillColor;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1 - General", "Enabled", true,
                "Show the predicted damage of your next arrow/bolt on the health bar of the creature under your crosshair.");
            IncludeCrossbows = Config.Bind("1 - General", "IncludeCrossbows", true,
                "Also preview crossbow bolts (shown whenever the crossbow is loaded).");
            MaxRange = Config.Bind("1 - General", "MaxRange", 60f,
                new ConfigDescription("How far away (in meters) a creature can be and still get a preview.",
                    new AcceptableValueRange<float>(5f, 200f)));
            UseArrowArc = Config.Bind("1 - General", "UseArrowArc", true,
                "When the ArrowArc mod is installed and drawing an arc, preview the creature the arc lands on " +
                "(accounts for arrow drop) instead of the one under the crosshair.");

            GuaranteedColor = Config.Bind("2 - Colors", "GuaranteedColor", new Color(1f, 0.85f, 0.1f, 0.95f),
                "Segment for the damage you'll deal even on the lowest roll.");
            PossibleColor = Config.Bind("2 - Colors", "PossibleColor", new Color(1f, 0.85f, 0.1f, 0.4f),
                "Segment for the extra damage you might deal, up to the highest roll.");
            KillColor = Config.Bind("2 - Colors", "KillColor", new Color(1f, 1f, 1f, 0.95f),
                "Guaranteed segment colour when even the lowest roll kills.");

            DebugLogging = Config.Bind("3 - Debug", "DebugLogging", false,
                "Log the predicted damage whenever the target or the prediction changes.");

            _harmony = Harmony.CreateAndPatchAll(typeof(HudPatches), Guid);
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
}
