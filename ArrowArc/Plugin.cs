using BepInEx;
using BepInEx.Configuration;
using Jotunn.Utils;
using UnityEngine;

namespace ArrowArc
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Purely visual: simulates the shot locally and draws it in the local scene, so nobody else needs it.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class ArrowArcPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.arrowarc";
        public const string Name = "ArrowArc";
        public const string Version = "0.3.2";

        internal static BepInEx.Logging.ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowForBows;
        internal static ConfigEntry<bool> ShowForCrossbows;
        internal static ConfigEntry<bool> ShowForGrapplingHook;
        internal static ConfigEntry<bool> OnlyAtFullDraw;
        internal static ConfigEntry<float> MaxDistance;
        internal static ConfigEntry<bool> ShowImpactMarker;
        internal static ConfigEntry<bool> MarkerShowsSpread;
        internal static ConfigEntry<float> LineWidth;
        internal static ConfigEntry<Color> LineColor;
        internal static ConfigEntry<Color> CreatureHitColor;
        internal static ConfigEntry<Color> NoHitColor;
        internal static ConfigEntry<bool> DebugLogging;

        private ArcRenderer _renderer;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1 - General", "Enabled", true,
                "Draw the predicted flight path of your shots. Master switch; the ShowFor* options pick the weapons.");
            ShowForBows = Config.Bind("1 - General", "ShowForBows", true,
                "Draw the arc for bows (shown while drawing). When off, DamagePreview targets the creature under " +
                "the crosshair instead of where the arc lands.");
            ShowForCrossbows = Config.Bind("1 - General", "ShowForCrossbows", true,
                "Draw the arc for crossbows (shown whenever the crossbow is loaded). When off, DamagePreview " +
                "targets the creature under the crosshair instead of where the arc lands.");
            ShowForGrapplingHook = Config.Bind("1 - General", "ShowForGrapplingHook", true,
                "Draw the arc for the grappling hook (shown whenever it is loaded). The arc stops where the target " +
                "would be out of the rope's reach.");
            OnlyAtFullDraw = Config.Bind("1 - General", "OnlyAtFullDraw", false,
                "Only show the arc once the bow is fully drawn. When false, the arc grows as the draw charges.");
            MaxDistance = Config.Bind("1 - General", "MaxDistance", 200f,
                new ConfigDescription("Stop drawing the arc after this many meters of flight.",
                    new AcceptableValueRange<float>(10f, 500f)));

            ShowImpactMarker = Config.Bind("2 - Look", "ShowImpactMarker", true,
                "Draw a ring where the arrow will land.");
            MarkerShowsSpread = Config.Bind("2 - Look", "MarkerShowsSpread", true,
                "Size the ring by the shot's random spread at that distance (it shrinks as the draw charges). " +
                "When false, the ring has a fixed size.");
            LineWidth = Config.Bind("2 - Look", "LineWidth", 0.015f,
                new ConfigDescription("Width of the arc in meters.", new AcceptableValueRange<float>(0.005f, 0.3f)));
            LineColor = Config.Bind("2 - Look", "LineColor", new Color(1f, 1f, 1f, 0.6f),
                "Colour of the arc and of the ring when it lands on terrain, buildings, etc.");
            CreatureHitColor = Config.Bind("2 - Look", "CreatureHitColor", new Color(1f, 0.3f, 0.2f, 0.85f),
                "Colour of the arc and ring when the arrow will hit a creature.");
            NoHitColor = Config.Bind("2 - Look", "NoHitColor", new Color(0.3f, 0.6f, 1f, 0.7f),
                "Colour of the arc when the shot is out of range and will hit nothing (it runs out of flight time " +
                "or MaxDistance in mid-air, or the target is beyond the grappling hook's rope).");

            DebugLogging = Config.Bind("3 - Debug", "DebugLogging", false,
                "Log the launch values (speed, gravity, drag) whenever they change.");

            _renderer = gameObject.AddComponent<ArcRenderer>();
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            if (_renderer != null) Destroy(_renderer);
        }

        internal static void Debug(string msg)
        {
            if (DebugLogging.Value) Log.LogInfo(msg);
        }
    }
}
