using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace DamagePreview
{
    // Optional link to the ArrowArc mod: when it is installed and drawing an arc, the creature the arc lands on is
    // the preview target (accounts for arrow drop, unlike the straight crosshair ray). Looked up by reflection so
    // DamagePreview still works on its own.
    internal static class ArrowArcLink
    {
        private const string ArrowArcGuid = "andrei.valheim.arrowarc";

        private delegate bool HitGetter(out Collider collider);

        private static HitGetter _getter;
        private static bool _resolved;

        // True when ArrowArc is showing an arc this frame; `collider` is what it lands on (null for nothing).
        public static bool TryGetArcHit(out Collider collider)
        {
            collider = null;
            if (!_resolved) Resolve();
            return _getter != null && _getter(out collider);
        }

        private static void Resolve()
        {
            _resolved = true;
            if (!Chainloader.PluginInfos.TryGetValue(ArrowArcGuid, out var info) || info.Instance == null) return;
            try
            {
                var type = info.Instance.GetType().Assembly.GetType("ArrowArc.ArrowArcApi");
                var method = type?.GetMethod("TryGetPredictedHit", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    DamagePreviewPlugin.Log.LogWarning("ArrowArc found but its API is missing; using the crosshair instead.");
                    return;
                }
                _getter = (HitGetter)Delegate.CreateDelegate(typeof(HitGetter), method);
                DamagePreviewPlugin.Log.LogInfo("ArrowArc found: previewing the creature the arc lands on.");
            }
            catch (Exception e)
            {
                DamagePreviewPlugin.Log.LogWarning($"Could not link to ArrowArc ({e.Message}); using the crosshair instead.");
            }
        }
    }
}
