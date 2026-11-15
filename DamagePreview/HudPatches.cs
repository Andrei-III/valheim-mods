using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DamagePreview
{
    // Draws two segments over the vanilla enemy health bar, from its left edge:
    //   guaranteed (lowest roll) on top, possible (highest roll) behind it, both clamped to the remaining health.
    // Also forces the target's bar to show at bow range (vanilla only shows non-boss bars within 10 m).
    [HarmonyPatch]
    internal static class HudPatches
    {
        private const string PossibleName = "DamagePreview_possible";
        private const string GuaranteedName = "DamagePreview_guaranteed";

        private static readonly FieldInfo HudsField = AccessTools.Field(typeof(EnemyHud), "m_huds");
        private static readonly Type HudDataType = AccessTools.Inner(typeof(EnemyHud), "HudData");
        private static readonly FieldInfo GuiField = AccessTools.Field(HudDataType, "m_gui");
        private static readonly FieldInfo FastField = AccessTools.Field(HudDataType, "m_healthFast");
        private static readonly FieldInfo FastFriendlyField = AccessTools.Field(HudDataType, "m_healthFastFriendly");
        private static readonly FieldInfo HoverTimerField = AccessTools.Field(HudDataType, "m_hoverTimer");
        private static readonly AccessTools.FieldRef<GuiBar, float> BarWidth =
            AccessTools.FieldRefAccess<GuiBar, float>("m_width");

        private static Prediction _current;
        private static bool _hasPrediction;
        private static RectTransform _shownPossible, _shownGuaranteed;
        private static string _lastLog;

        // Runs before TestShow/ShowHud/UpdateHuds in the same frame.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyHud), "LateUpdate")]
        private static void ComputePrediction()
        {
            _hasPrediction = false;
            var player = Player.m_localPlayer;
            if (!DamagePreviewPlugin.Enabled.Value || player == null || player.IsDead()) return;
            float range = DamagePreviewPlugin.MaxRange.Value;
            Character target;
            Collider col;
            if (DamagePreviewPlugin.UseArrowArc.Value && ArrowArcLink.TryGetArcHit(out var arcHit))
            {
                if (!DamageCalc.TryTargetFromArc(player, arcHit, range, out target, out col)) return;
            }
            else if (!DamageCalc.TryFindTarget(player, range, out target, out col)) return;
            if (!DamageCalc.TryPredict(player, target, col, out _current)) return;
            _hasPrediction = true;

            if (DamagePreviewPlugin.DebugLogging.Value)
            {
                string msg = $"{target.m_name} hp {target.GetHealth():0}/{target.GetMaxHealth():0} " +
                             $"[{col?.name}]: {_current.Min:0}-{_current.Max:0} direct, {_current.DotMin:0}-{_current.DotMax:0} over time";
                if (msg != _lastLog) DamagePreviewPlugin.Debug(_lastLog = msg);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EnemyHud), "TestShow")]
        private static void ShowTargetAtRange(Character c, ref bool __result)
        {
            if (!__result && _hasPrediction && c == _current.Target) __result = true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static void KeepTargetVisible(EnemyHud __instance)
        {
            var hud = GetHud(__instance);
            if (hud != null) HoverTimerField.SetValue(hud, 0f);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static void DrawOverlay(EnemyHud __instance)
        {
            var hud = GetHud(__instance);
            var gui = hud != null ? GuiField.GetValue(hud) as GameObject : null;
            if (gui == null || !gui.activeSelf)
            {
                Hide();
                return;
            }

            // Whichever health bar vanilla is showing (enemy red or friendly green).
            var bar = FastField.GetValue(hud) as GuiBar;
            var friendly = FastFriendlyField.GetValue(hud) as GuiBar;
            if (friendly != null && friendly.gameObject.activeSelf) bar = friendly;
            if (bar == null || bar.m_bar == null)
            {
                Hide();
                return;
            }

            var possible = GetOrCreate(bar.m_bar, PossibleName, 1);
            var guaranteed = GetOrCreate(bar.m_bar, GuaranteedName, 2);
            if (_shownPossible != possible) Hide();
            _shownPossible = possible;
            _shownGuaranteed = guaranteed;

            var target = _current.Target;
            float maxHp = Mathf.Max(target.GetMaxHealth(), 0.01f);
            float remaining = bar.GetSmoothValue();
            float width = BarWidth(bar);
            float gFrac = Mathf.Min(_current.Min / maxHp, remaining);
            float pFrac = Mathf.Min(_current.Max / maxHp, remaining);
            bool kill = _current.Min >= target.GetHealth();

            SetSegment(possible, width * pFrac, DamagePreviewPlugin.PossibleColor.Value);
            SetSegment(guaranteed, width * gFrac,
                kill ? DamagePreviewPlugin.KillColor.Value : DamagePreviewPlugin.GuaranteedColor.Value);
        }

        private static object GetHud(EnemyHud hudManager)
        {
            if (!_hasPrediction || _current.Target == null) return null;
            var huds = HudsField.GetValue(hudManager) as IDictionary;
            return huds != null && huds.Contains(_current.Target) ? huds[_current.Target] : null;
        }

        // A copy of the bar's own rect (same anchors/pivot/position), so width 0..m_width lines up with it.
        private static RectTransform GetOrCreate(RectTransform bar, string name, int offset)
        {
            var parent = bar.parent;
            var existing = parent.Find(name) as RectTransform;
            if (existing != null) return existing;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = bar.anchorMin;
            rt.anchorMax = bar.anchorMax;
            rt.pivot = bar.pivot;
            rt.anchoredPosition = bar.anchoredPosition;
            rt.sizeDelta = bar.sizeDelta;
            rt.localScale = bar.localScale;
            rt.localRotation = bar.localRotation;
            rt.SetSiblingIndex(bar.GetSiblingIndex() + offset);
            go.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        private static void SetSegment(RectTransform rt, float width, Color color)
        {
            if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, width));
            rt.GetComponent<Image>().color = color;
        }

        private static void Hide()
        {
            if (_shownPossible != null) _shownPossible.gameObject.SetActive(false);
            if (_shownGuaranteed != null) _shownGuaranteed.gameObject.SetActive(false);
            _shownPossible = _shownGuaranteed = null;
        }
    }
}
