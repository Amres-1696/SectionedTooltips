using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SectionedTooltips
{
    /// 设置面板的外观：游戏的模组设置是统一的样子（大写的节标题、整块青色的开关和下拉框、粗滑块、每行一个“ⓘ”），
    /// 这里在游戏建好窗口后换一套更清爽的：节标题是一行小青字加一条细线，开关是小拨动开关，滑块是细线，下拉框是细边框，
    /// 说明挪到鼠标停在标签上时显示。游戏的控件本身不动（读写设置、恢复默认值都还走它们），只把它们的图藏起来、盖上我们画的。
    /// 哪一步对不上（窗口结构不是预期的样子）就跳过那一步，那一处保持游戏原样。
    internal sealed partial class SettingsPanel
    {
        private static readonly Color Accent = new Color32(0x8C, 0xC7, 0xBF, 0xFF);
        private static readonly Color Line = new Color32(0x4A, 0x41, 0x52, 0xFF);
        private static readonly Color FieldEdge = new Color32(0x5B, 0x51, 0x63, 0xFF);
        private static readonly Color Knob = new Color32(0x2B, 0x24, 0x33, 0xFF);

        /// 拨动开关：游戏的 Toggle 照常收点击，我们按它的 isOn 画底和圆钮。
        private sealed class Switcher
        {
            public Toggle Toggle;
            public Image Track;
            public RectTransform Knob;
            public float Travel;
            public int Shown = -1;
        }

        private readonly List<Switcher> switchers = new List<Switcher>();

        private static Sprite pill, outline;

        /// 圆头的底（九宫格，拉长就是胶囊，正方形就是圆）。
        private static Sprite Pill
        {
            get
            {
                if (pill != null) return pill;
                const int n = 16;
                Texture2D tex = NewTexture(n);
                float r = n / 2f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x + 0.5f - r, dy = y + 0.5f - r;
                        tex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? Color.white : Color.clear);
                    }
                tex.Apply();
                pill = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(7, 7, 7, 7));
                return pill;
            }
        }

        /// 一像素的方框（九宫格，中间透明）。
        private static Sprite Outline
        {
            get
            {
                if (outline != null) return outline;
                const int n = 4;
                Texture2D tex = NewTexture(n);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        tex.SetPixel(x, y, x == 0 || y == 0 || x == n - 1 || y == n - 1 ? Color.white : Color.clear);
                tex.Apply();
                outline = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
                return outline;
            }
        }

        private static Texture2D NewTexture(int n)
        {
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private void Restyle()
        {
#if DEVDUMP
            DumpTree(window.root.transform, 0);
#endif
            Step("headers", RestyleHeaders);
            foreach (ConfigEntry e in Entries())
            {
                string key = e.Key;
                Step("label " + key, delegate { RestyleLabel(Control("label_" + key)); });
                CustomUIElement c = Control(key);
                if (c == null) continue;
                // 先认下拉框：它的弹出列表模板里也有 Toggle，CustomUIElement.toggle 会指向那里。
                if (c.dropdown != null) Step("dropdown " + key, delegate { RestyleDropdown(c.dropdown); });
                else if (c.slider != null) Step("slider " + key, delegate { RestyleSlider(c, key); });
                else if (c.toggle != null) Step("toggle " + key, delegate { RestyleToggle(c.toggle); });
            }
        }

        private static void Step(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { SectionedTooltipsMod.Error("settings style: " + what + " kept as the game drew it: " + e.Message); }
        }

        private static IEnumerable<ConfigEntry> Entries()
        {
            ModConfig cfg = ModConfig.Find(SectionedTooltipsMod.Id);
            if (cfg == null) yield break;
            IReadOnlyList<ConfigEntry> list = cfg.Entries;
            for (int i = 0; i < list.Count; i++) yield return list[i];
        }

        /// 节标题：换成本地化的名字，小一号的青字，右边一条细线；游戏在标题下画的分隔线藏掉。
        private void RestyleHeaders()
        {
            for (int s = 0; s < Settings.Sections.GetLength(0); s++)
            {
                CustomUIElement head = Control("section_" + Settings.Sections[s, 0]);
                if (head == null || head.text == null) continue;
                TextMeshProUGUI t = head.text;
                string name = SectionedTooltipsMod.L(Settings.Sections[s, 1]);
                if (!string.IsNullOrEmpty(name) && name != Settings.Sections[s, 1]) t.text = name;
                t.fontStyle &= ~(FontStyles.UpperCase | FontStyles.SmallCaps | FontStyles.Bold);
                t.fontSize = Mathf.Round(font.fontSize * 0.85f);
                t.color = Accent;

                Transform row = head.root.transform.parent;
                if (row == null || row.GetComponent<HorizontalLayoutGroup>() == null) continue;
                // 一条细线：一个能伸缩的格子，线在格子里竖直居中。
                GameObject cell = new GameObject("rule", typeof(RectTransform), typeof(LayoutElement));
                cell.layer = row.gameObject.layer;
                cell.transform.SetParent(row, false);
                LayoutElement le = cell.GetComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.minHeight = le.preferredHeight = Mathf.Round(t.fontSize * 1.2f);
                Image line = Ui.NewImage((RectTransform)cell.transform, Line);
                RectTransform lr = line.rectTransform;
                lr.anchorMin = new Vector2(0f, 0.5f);
                lr.anchorMax = new Vector2(1f, 0.5f);
                lr.pivot = new Vector2(0.5f, 0.5f);
                lr.offsetMin = new Vector2(Mathf.Round(font.fontSize * 0.5f), -0.5f);
                lr.offsetMax = new Vector2(0f, 0.5f);
                // 游戏在标题那一行后面接一条分隔线：有了细线就不要它了。分隔线下面那块没有文字也没有控件，认错了就不动。
                Transform list = row.parent;
                int next = row.GetSiblingIndex() + 1;
                if (list != null && next < list.childCount)
                {
                    Transform sep = list.GetChild(next);
                    if (sep.GetComponentInChildren<TMP_Text>(true) == null && sep.GetComponentInChildren<Selectable>(true) == null)
                        sep.gameObject.SetActive(false);
                }
            }
        }

        private static readonly Regex InfoLink = new Regex("\\s*<link=\"([^\"]*)\"><sprite[^>]*></link>\\s*$");

        /// 标签：去掉行尾的“ⓘ”，整个标签变成那个链接，鼠标停在标签上就出说明。不折行，太长时截断加省略号。
        private static void RestyleLabel(CustomUIElement label)
        {
            if (label == null || label.text == null) return;
            TextMeshProUGUI t = label.text;
            Match m = InfoLink.Match(t.text ?? "");
            if (m.Success) t.text = "<link=\"" + m.Groups[1].Value + "\">" + t.text.Substring(0, m.Index) + "</link>";
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// 拨动开关：藏起游戏的方框和勾（方框只是变透明，还接着收点击），盖上胶囊底和圆钮。
        private void RestyleToggle(Toggle toggle)
        {
            Image box = toggle.targetGraphic as Image;
            Graphic check = toggle.graphic;
            RectTransform host = box != null ? box.rectTransform : (RectTransform)toggle.transform;
            float h = Mathf.Round(font.fontSize * 0.9f), w = Mathf.Round(h * 1.9f), k = h - 4f;
            if (box != null) box.color = Color.clear;
            if (check != null) check.enabled = false;

            Image track = Ui.NewImage(host, Line);
            track.sprite = Pill;
            track.type = Image.Type.Sliced;
            track.raycastTarget = true;
            Center(track.rectTransform, w, h);
            Image knob = Ui.NewImage(track.rectTransform, Knob);
            knob.sprite = Pill;
            knob.type = Image.Type.Sliced;
            Center(knob.rectTransform, k, k);
            Switcher s = new Switcher();
            s.Toggle = toggle;
            s.Track = track;
            s.Knob = knob.rectTransform;
            s.Travel = (w - h) / 2f;
            switchers.Add(s);
        }

        /// 滑块：底、填充、手柄都换成细线和一个小竖块；同一行左边游戏自带的数值（物体名 label_value_键名）藏掉，右边有我们写的数值。
        private void RestyleSlider(CustomUIElement c, string key)
        {
            Slider slider = c.slider;
            Transform row = c.root.transform.parent;
            Transform value = row != null ? row.Find("label_value_" + key) : null;
            if (value != null && value.GetComponentInChildren<Selectable>(true) == null) value.gameObject.SetActive(false);
            Image fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            Image handle = slider.handleRect != null ? slider.handleRect.GetComponent<Image>() : null;
            Image[] all = slider.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Image img = all[i];
                if (img == fill || img == handle) continue;
                if (img.transform.IsChildOf(slider.transform)) img.color = Color.clear;
            }
            RectTransform area = (RectTransform)slider.transform;
            Image track = Ui.NewImage(area, Line);
            Band(track.rectTransform, 2f);
            track.transform.SetAsFirstSibling();
            if (fill != null)
            {
                fill.color = Color.clear;
                Image bar = Ui.NewImage(fill.rectTransform, Accent);
                Band(bar.rectTransform, 2f);
            }
            if (handle != null)
            {
                handle.color = Color.clear;
                Image grip = Ui.NewImage(handle.rectTransform, Accent);
                Center(grip.rectTransform, Mathf.Max(4f, Mathf.Round(font.fontSize * 0.35f)), Mathf.Round(font.fontSize * 0.8f));
            }
        }

        /// 下拉框：整块青色换成细边框，字用面板的字色。箭头和弹出的列表保持游戏原样。
        private void RestyleDropdown(TMP_Dropdown dropdown)
        {
            Image face = dropdown.targetGraphic as Image;
            if (face == null) face = dropdown.GetComponent<Image>();
            RectTransform host = (RectTransform)dropdown.transform;
            if (face != null)
            {
                face.color = Color.clear;
                host = face.rectTransform;
            }
            Image edge = Ui.NewImage(host, FieldEdge);
            edge.sprite = Outline;
            edge.type = Image.Type.Sliced;
            RectTransform er = edge.rectTransform;
            er.anchorMin = Vector2.zero;
            er.anchorMax = Vector2.one;
            er.pivot = new Vector2(0.5f, 0.5f);
            er.offsetMin = er.offsetMax = Vector2.zero;
            edge.transform.SetAsFirstSibling();
            if (dropdown.captionText != null) dropdown.captionText.color = font.color;
        }

        /// 每帧把拨动开关画成 Toggle 现在的状态（恢复默认值这种静默改动也跟得上）。
        private void UpdateStyle()
        {
            for (int i = 0; i < switchers.Count; i++)
            {
                Switcher s = switchers[i];
                if (s.Toggle == null || s.Track == null) continue;
                int on = s.Toggle.isOn ? 1 : 0;
                if (on == s.Shown) continue;
                s.Shown = on;
                s.Track.color = on == 1 ? Accent : Line;
                s.Knob.anchoredPosition = new Vector2(on == 1 ? s.Travel : -s.Travel, 0f);
            }
        }

        private static void Center(RectTransform rt, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
        }

        /// 横向铺满、竖直居中、高 h 的一条。
        private static void Band(RectTransform rt, float h)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, -h / 2f);
            rt.offsetMax = new Vector2(0f, h / 2f);
        }

#if DEVDUMP
        /// dev 构建：把设置窗口的结构写进日志（只写前几层和每种控件的第一个），对照样式改得对不对。
        private static void DumpTree(Transform t, int depth)
        {
            if (depth > 9) return;
            RectTransform rt = t as RectTransform;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
            if (rt != null) sb.AppendFormat(" {0:F0}x{1:F0}", rt.rect.width, rt.rect.height);
            Component[] cs = t.GetComponents<Component>();
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == null || cs[i] is Transform) continue;
                sb.Append(" [").Append(cs[i].GetType().Name);
                Image img = cs[i] as Image;
                if (img != null) sb.Append(" ").Append(img.sprite != null ? img.sprite.name : "-").Append(" #").Append(ColorUtility.ToHtmlStringRGBA(img.color));
                TMP_Text tx = cs[i] as TMP_Text;
                if (tx != null) sb.AppendFormat(" {0:F0}pt \"{1}\"", tx.fontSize, (tx.text ?? "").Length > 40 ? tx.text.Substring(0, 40) : tx.text);
                LayoutElement le = cs[i] as LayoutElement;
                if (le != null) sb.AppendFormat(" min {0:F0} pref {1:F0} flex {2:F0}", le.minWidth, le.preferredWidth, le.flexibleWidth);
                sb.Append("]");
            }
            SectionedTooltipsMod.Log("[settings] " + sb);
            for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), depth + 1);
        }
#endif
    }
}
