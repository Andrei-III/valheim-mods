using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace SummonTracker
{
    /// <summary>
    /// Vertical list under the minimap: one row per summon with icon, name, stars, distance and a health bar.
    /// Built with plain uGUI under Hud.m_rootObject so it hides together with the rest of the HUD.
    /// </summary>
    internal static class SummonHud
    {
        private const float Width = 210f;
        private const float RowHeight = 38f;
        private const float IconSize = 32f;
        private const float TextLeft = IconSize + 6f;
        private const float Gap = 6f;     // space between the minimap and the list
        private const float Margin = 20f; // distance from the screen corner when the minimap is hidden

        private static readonly Color HealthGood = new Color(0.36f, 0.75f, 0.25f);
        private static readonly Color HealthLow = new Color(0.85f, 0.22f, 0.15f);
        private static readonly Color FarColor = new Color(1f, 0.75f, 0.3f);

        private class Row
        {
            public GameObject Go;
            public CanvasGroup Group;
            public Image Icon;
            public Text Name;
            public Text Info;
            public RectTransform Fill;
            public Image FillImage;
            public Image[] Stars;
        }

        private static RectTransform s_root;
        private static readonly List<Row> s_rows = new List<Row>();
        private static readonly Vector3[] s_corners = new Vector3[4];
        private static Sprite s_starSprite;

        internal static void Update()
        {
            var summons = SummonRegistry.Summons;
            if (summons.Count == 0 || !Hud.instance)
            {
                if (s_root) s_root.gameObject.SetActive(false);
                return;
            }
            if (!s_root && !Build()) return;

            s_root.gameObject.SetActive(true);
            float scale = SummonTrackerPlugin.Scale.Value;
            s_root.localScale = new Vector3(scale, scale, 1f);
            Position();

            while (s_rows.Count < summons.Count) s_rows.Add(CreateRow(s_rows.Count));
            for (int i = 0; i < s_rows.Count; i++)
            {
                bool used = i < summons.Count;
                s_rows[i].Go.SetActive(used);
                if (used) Fill(s_rows[i], summons[i]);
            }
            s_root.sizeDelta = new Vector2(Width, RowHeight * summons.Count);
        }

        internal static void Destroy()
        {
            if (s_root) Object.Destroy(s_root.gameObject);
            s_root = null;
            s_rows.Clear();
        }

        private static bool Build()
        {
            var parent = Hud.instance.m_rootObject;
            if (!parent || !GUIManager.Instance.AveriaSerifBold) return false;

            var go = new GameObject("SummonTracker", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            s_root = (RectTransform)go.transform;
            s_root.anchorMin = s_root.anchorMax = s_root.pivot = new Vector2(1f, 1f);
            s_rows.Clear();

            // Borrow the vanilla level star from the enemy health bar prefab.
            var level2 = EnemyHud.instance && EnemyHud.instance.m_baseHud
                ? EnemyHud.instance.m_baseHud.transform.Find("level_2") : null;
            var starImage = level2 ? level2.GetComponentInChildren<Image>(true) : null;
            s_starSprite = starImage ? starImage.sprite : null;
            return true;
        }

        /// <summary>Right-align the list under the small minimap, or in the top-right corner when it's hidden.</summary>
        private static void Position()
        {
            var small = Minimap.instance ? Minimap.instance.m_smallRoot : null;
            if (small && small.activeInHierarchy)
            {
                ((RectTransform)small.transform).GetWorldCorners(s_corners);
                Vector3 bottomRight = s_corners[3];
                s_root.position = bottomRight + Vector3.down * (Gap * s_root.lossyScale.y);
            }
            else
            {
                s_root.anchoredPosition = new Vector2(-Margin, -Margin);
            }
        }

        private static void Fill(Row row, TrackedSummon s)
        {
            row.Icon.sprite = s.Icon;
            row.Icon.enabled = s.Icon;
            row.Name.text = s.Name;

            // Stars follow the name, like on the vanilla enemy bar (level 2 = 1 star, level 3 = 2 stars).
            int stars = Mathf.Clamp(s.Level - 1, 0, row.Stars.Length);
            float nameWidth = Mathf.Min(row.Name.preferredWidth, row.Name.rectTransform.rect.width);
            for (int i = 0; i < row.Stars.Length; i++)
            {
                bool on = i < stars && row.Stars[i].sprite;
                row.Stars[i].enabled = on;
                if (on) row.Stars[i].rectTransform.anchoredPosition = new Vector2(TextLeft + nameWidth + 2f + i * 12f, -2f);
            }

            string info = "";
            if (SummonTrackerPlugin.ShowDistance.Value)
            {
                string dist = Mathf.RoundToInt(s.Distance) + "m";
                info = s.Distance > SummonTrackerPlugin.FarDistance.Value
                    ? $"<color=#{ColorUtility.ToHtmlStringRGB(FarColor)}>{dist}</color>" : dist;
            }
            float remaining = s.Remaining;
            if (remaining >= 0f)
            {
                int secs = Mathf.CeilToInt(remaining);
                string timer = $"{secs / 60}:{secs % 60:00}";
                info = info.Length > 0 ? timer + "  " + info : timer;
            }
            row.Info.text = info;

            float pct = s.MaxHealth > 0f ? Mathf.Clamp01(s.Health / s.MaxHealth) : 1f;
            row.Fill.anchorMax = new Vector2(pct, 1f);
            row.FillImage.color = pct < 0.3f ? HealthLow : HealthGood;

            // Out of the loaded area the data stops updating, so dim the row to show it's last-known.
            row.Group.alpha = s.Loaded ? 1f : 0.5f;
        }

        private static Row CreateRow(int index)
        {
            var row = new Row();
            row.Go = new GameObject("Summon" + index, typeof(RectTransform));
            var rt = (RectTransform)row.Go.transform;
            rt.SetParent(s_root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(Width, RowHeight);
            rt.anchoredPosition = new Vector2(0f, -index * RowHeight);
            row.Group = row.Go.AddComponent<CanvasGroup>();
            row.Group.interactable = row.Group.blocksRaycasts = false;

            // Dark backing so the text reads over bright terrain.
            var bg = NewImage("Background", rt, new Color(0f, 0f, 0f, 0.35f));
            Place(bg.rectTransform, 0f, -1f, Width, RowHeight - 2f);

            row.Icon = NewImage("Icon", rt, Color.white);
            row.Icon.preserveAspect = true;
            Place(row.Icon.rectTransform, 2f, -(RowHeight - IconSize) / 2f, IconSize, IconSize);

            row.Info = NewText("Info", rt, 12, TextAnchor.UpperRight);
            Place(row.Info.rectTransform, Width - 74f, -3f, 70f, 18f);

            row.Name = NewText("Name", rt, 14, TextAnchor.UpperLeft);
            Place(row.Name.rectTransform, TextLeft, -2f, Width - TextLeft - 74f - 26f, 18f);
            row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;

            row.Stars = new Image[2];
            for (int i = 0; i < row.Stars.Length; i++)
            {
                row.Stars[i] = NewImage("Star" + i, rt, Color.white);
                row.Stars[i].sprite = s_starSprite;
                row.Stars[i].preserveAspect = true;
                Place(row.Stars[i].rectTransform, 0f, -2f, 12f, 12f);
            }

            var barBg = NewImage("HealthBg", rt, new Color(0f, 0f, 0f, 0.7f));
            Place(barBg.rectTransform, TextLeft, -23f, Width - TextLeft - 4f, 9f);
            row.FillImage = NewImage("HealthFill", barBg.rectTransform, HealthGood);
            row.Fill = row.FillImage.rectTransform;
            row.Fill.anchorMin = Vector2.zero;
            row.Fill.anchorMax = Vector2.one;
            row.Fill.offsetMin = new Vector2(1f, 1f);
            row.Fill.offsetMax = new Vector2(-1f, -1f);
            row.Fill.pivot = new Vector2(0f, 0.5f);
            return row;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text NewText(string name, Transform parent, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            GUIManager.Instance.ApplyTextStyle(text, GUIManager.Instance.AveriaSerifBold, Color.white, size, true);
            text.alignment = align;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        /// <summary>Position a child by its top-left corner relative to the row's top-left.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
