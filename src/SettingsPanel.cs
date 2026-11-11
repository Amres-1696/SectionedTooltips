using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SectionedTooltips
{
    /// 挂在我们设置面板上：在滑块右边显示数值，在纸色列表旁显示色块，还有一张跟着控件走的示例估价单。它每帧读控件而不是监听，所以“恢复默认值”这种静默改动也能反映出来。
    internal sealed class SettingsPanel : MonoBehaviour
    {
        private sealed class SliderValue
        {
            public string Key;
            public Slider Slider;
            public TextMeshProUGUI Text;
            public int Step;
            public string Suffix;
            public float Shown = float.NaN;
        }

        private sealed class Swatch
        {
            public TMP_Dropdown List;
            public Image Edge, Paper;
            public int Shown = -1;
            /// 这是标签色列表，不是纸色列表。
            public bool Tag;
        }

        private CustomUIWindow window;
        private TMP_Text font;
        private readonly List<SliderValue> sliders = new List<SliderValue>();
        private readonly List<Swatch> swatches = new List<Swatch>();
        private RectTransform preview;
        private LayoutElement previewSize;
        /// 示例放在设置左边的独立一栏里，不放在列表中间。
        private bool aside;

        /// 示例显示的样子，从控件读出来。每帧逐字段比较，所以读它不产生分配。
        private sealed class Look
        {
            public bool Classic, Icon, Prices, Description, Footer, Contents, Tree, TagWash;
            public int Paper, NotOwnedPaper, MaxChars;
            public float Scale, PriceScale, Opacity, Width, Height;
            public readonly int[] Tags = new int[Ticket.TagGroups.Length];

            public bool Same(Look o)
            {
                if (Classic != o.Classic || Icon != o.Icon || Prices != o.Prices || Description != o.Description || Footer != o.Footer || Contents != o.Contents
                    || Tree != o.Tree || TagWash != o.TagWash || Paper != o.Paper || NotOwnedPaper != o.NotOwnedPaper
                    || MaxChars != o.MaxChars || Scale != o.Scale || PriceScale != o.PriceScale || Opacity != o.Opacity
                    || Width != o.Width || Height != o.Height) return false;
                for (int i = 0; i < Tags.Length; i++) if (Tags[i] != o.Tags[i]) return false;
                return true;
            }

            public void CopyFrom(Look o)
            {
                Classic = o.Classic; Icon = o.Icon; Prices = o.Prices; Description = o.Description; Footer = o.Footer; Contents = o.Contents;
                Tree = o.Tree; TagWash = o.TagWash; Paper = o.Paper; NotOwnedPaper = o.NotOwnedPaper; MaxChars = o.MaxChars;
                Scale = o.Scale; PriceScale = o.PriceScale; Opacity = o.Opacity; Width = o.Width; Height = o.Height;
                Array.Copy(o.Tags, Tags, Tags.Length);
            }
        }

        // 示例要跟随的控件，只查一次。
        private CustomUIElement cStyle, cPaper, cNotOwnedPaper, cIcon, cPrices, cDescription, cFooter, cContents, cTree, cMaxWidth, cScale, cPriceSize, cOpacity, cTagBackground;
        private readonly CustomUIElement[] cTagColors = new CustomUIElement[Ticket.TagGroups.Length];
        /// 经典风格在风格列表里的位置（列表就是枚举的名字，按顺序排）。
        private int classicIndex = -1;

        // 这一帧的控件值、上次看到变化的、以及示例正显示着的。
        private readonly Look now = new Look(), seen = new Look(), shown = new Look();
        private bool drawn;
        /// 控件最后一次变化的时间。拖滑块时每帧都在变，示例等它停下来再画。
        private float changedAt;
        private const float RedrawAfter = 0.1f;

        internal static void Decorate(CustomUIWindow window)
        {
            CustomUIElement label = window.Get("label_Enabled");
            if (label == null || label.text == null || window.root == null) return;
            // 已经装饰过了（游戏可能再次显示同一个窗口）：加两遍会让数值和色块都翻倍。
            if (window.root.GetComponent<SettingsPanel>() != null) return;
            SettingsPanel p = window.root.AddComponent<SettingsPanel>();
            p.window = window;
            p.font = label.text;
            p.AddValue("TicketScale", 5, "%");
            p.AddValue("PriceSize", 10, "%");
            p.AddValue("ModsColumnLines", 1, "");
            p.AddValue("TicketOpacity", 5, "%");
            p.AddValue("ContentRows", 1, "");
            p.AddValue("MaxWidth", 1, "");
            p.AddSwatch("PaperColor");
            p.AddSwatch("NotOwnedPaperColor");
            for (int i = 0; i < Ticket.TagGroups.Length; i++) p.AddSwatch("TagColor" + Ticket.TagGroups[i], true);
            p.AddPreview();
            p.FindControls();
        }

        private void FindControls()
        {
            cStyle = Control("Style");
            cPaper = Control("PaperColor");
            cNotOwnedPaper = Control("NotOwnedPaperColor");
            cIcon = Control("ItemIcon");
            cPrices = Control("ShowPriceDetails");
            cDescription = Control("ShowDescription");
            cFooter = Control("ShowFooter");
            cContents = Control("ContentsPanel");
            cTree = Control("TreeLines");
            cMaxWidth = Control("MaxWidth");
            cScale = Control("TicketScale");
            cPriceSize = Control("PriceSize");
            cOpacity = Control("TicketOpacity");
            cTagBackground = Control("TagBackground");
            for (int i = 0; i < cTagColors.Length; i++) cTagColors[i] = Control("TagColor" + Ticket.TagGroups[i]);
            classicIndex = Array.IndexOf(Enum.GetNames(typeof(TooltipStyle)), TooltipStyle.Classic.ToString());
        }

        private CustomUIElement Control(string key)
        {
            CustomUIElement e = window.Get(key);
            return e != null && e.root != null ? e : null;
        }

        /// 控件右边的一段文字，用面板的标签字体。
        private TextMeshProUGUI NewText(Transform row, int index, float width, TextAlignmentOptions align)
        {
            GameObject go = new GameObject("value", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.layer = row.gameObject.layer;
            go.transform.SetParent(row, false);
            go.transform.SetSiblingIndex(index);
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            t.font = font.font;
            t.fontSharedMaterial = font.fontSharedMaterial;
            t.fontSize = font.fontSize;
            t.color = font.color;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            return t;
        }

        private void AddValue(string key, int step, string suffix)
        {
            CustomUIElement e = Control(key);
            if (e == null || e.slider == null) return;
            Transform w = e.root.transform;
            SliderValue v = new SliderValue();
            v.Key = key;
            v.Slider = e.slider;
            v.Step = step;
            v.Suffix = suffix;
            v.Text = NewText(w.parent, w.GetSiblingIndex() + 1, Mathf.Round(font.fontSize * 3f), TextAlignmentOptions.MidlineRight);
            sliders.Add(v);
        }

        private void AddSwatch(string key, bool tag = false)
        {
            CustomUIElement e = Control(key);
            if (e == null || e.dropdown == null) return;
            Transform w = e.root.transform;
            float h = Mathf.Round(font.fontSize * 1.1f), wd = Mathf.Round(h * 1.5f);
            GameObject go = new GameObject("swatch", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.layer = w.gameObject.layer;
            go.transform.SetParent(w.parent, false);
            go.transform.SetSiblingIndex(w.GetSiblingIndex());
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = wd;
            le.minHeight = le.preferredHeight = h;
            Swatch s = new Swatch();
            s.List = e.dropdown;
            s.Tag = tag;
            s.Edge = go.GetComponent<Image>();
            s.Edge.raycastTarget = false;
            GameObject inner = new GameObject("paper", typeof(RectTransform), typeof(Image));
            inner.layer = go.layer;
            inner.transform.SetParent(go.transform, false);
            s.Paper = inner.GetComponent<Image>();
            s.Paper.raycastTarget = false;
            Ui.Stretch(s.Paper, 2f, 2f, 2f, 2f);
            swatches.Add(s);
        }

        /// 设置左边的一栏，用来画示例：不管滚到哪个设置都看得见。窗口相应加宽，设置区宽度不变。
        private bool AddPreviewAside()
        {
            CustomUIElement head = Control("section_" + Settings.SlipSection);
            RectTransform holder = window.ContentRoot;
            ScrollRect scroll = head != null ? head.root.GetComponentInParent<ScrollRect>() : null;
            if (scroll == null || holder == null || window.Rect == null) return false;
            // 按窗口的布局找滚动视图：content 下面那个持有滚动的子物体。
            Transform list = scroll.transform;
            while (list != null && list.parent != holder) list = list.parent;
            if (list == null || holder.GetComponent<VerticalLayoutGroup>() == null) return false;
            LayoutElement listSize = list.GetComponent<LayoutElement>();
            if (listSize == null) return false;

            float col = Mathf.Round(font.fontSize * 12f), gap = Mathf.Round(font.fontSize);
            GameObject split = new GameObject("preview split", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            split.layer = list.gameObject.layer;
            split.transform.SetParent(holder, false);
            split.transform.SetSiblingIndex(list.GetSiblingIndex());
            HorizontalLayoutGroup g = split.GetComponent<HorizontalLayoutGroup>();
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = true;
            g.childAlignment = TextAnchor.UpperLeft;
            g.spacing = gap;
            // 这一行占掉滚动视图原来的空间。
            LayoutElement splitSize = split.GetComponent<LayoutElement>();
            splitSize.minHeight = listSize.minHeight;
            splitSize.preferredHeight = listSize.preferredHeight;
            splitSize.flexibleHeight = listSize.flexibleHeight;
            splitSize.flexibleWidth = 1f;

            GameObject go = new GameObject("preview", typeof(RectTransform), typeof(LayoutElement), typeof(RectMask2D));
            go.layer = split.layer;
            go.transform.SetParent(split.transform, false);
            preview = (RectTransform)go.transform;
            previewSize = go.GetComponent<LayoutElement>();
            previewSize.minWidth = previewSize.preferredWidth = col;
            previewSize.flexibleWidth = 0f;
            list.SetParent(split.transform, false);
            listSize.flexibleWidth = 1f;

            window.Rect.sizeDelta += new Vector2(col + gap, 0f);
            LayoutRebuilder.MarkLayoutForRebuild(holder);
            aside = true;
            return true;
        }

        /// 估价单那一节标题下面的方框，用来画示例：挨着它跟随最多的设置，改完不用往上翻。窗口布局不符合预期、分不出独立一栏时用这个。
        private void AddPreview()
        {
            if (AddPreviewAside()) return;
            CustomUIElement head = Control("section_" + Settings.SlipSection);
            if (head == null) return;
            Transform row = head.root.transform.parent;
            Transform list = row != null ? row.parent : null;
            if (list == null) return;
            GameObject go = new GameObject("preview", typeof(RectTransform), typeof(LayoutElement));
            go.layer = row.gameObject.layer;
            go.transform.SetParent(list, false);
            // 放在标题那一行和它下面的分隔线之后。
            go.transform.SetSiblingIndex(Mathf.Min(list.childCount - 1, row.GetSiblingIndex() + 2));
            preview = (RectTransform)go.transform;
            previewSize = go.GetComponent<LayoutElement>();
            // 列表按每行自己要的高度排，不拉伸：没有可伸缩宽度时这个方框一点宽度都没有，示例根本画不出来。
            previewSize.flexibleWidth = 1f;
        }

        private void Update()
        {
            try
            {
                for (int i = 0; i < sliders.Count; i++) UpdateValue(sliders[i]);
                for (int i = 0; i < swatches.Count; i++) UpdateSwatch(swatches[i]);
                if (preview != null) UpdatePreview();
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Error("settings panel preview stopped: " + e.Message);
                enabled = false;
            }
        }

        private static void UpdateValue(SliderValue v)
        {
            if (v.Slider == null || v.Text == null) return;
            float value = v.Slider.value;
            // 百分比滑块按步长走（估价单 5%，价格 10%）：更细的步长肉眼看不出区别。
            if (v.Step > 1)
            {
                float snapped = Mathf.Round(value / v.Step) * v.Step;
                if (snapped != value) v.Slider.value = value = Mathf.Clamp(snapped, v.Slider.minValue, v.Slider.maxValue);
            }
            if (value == v.Shown) return;
            v.Shown = value;
            v.Text.text = Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) + v.Suffix;
        }

        private static void UpdateSwatch(Swatch s)
        {
            if (s.List == null || s.List.value == s.Shown) return;
            s.Shown = s.List.value;
            Color paper, edge;
            // 标签色是浅色底上一块实色。
            if (s.Tag) Ticket.TagSwatch(s.Shown, out paper, out edge);
            else Ticket.PaperSwatch(s.Shown, out paper, out edge);
            s.Edge.color = edge;
            s.Paper.color = paper;
        }

        private static int Index(CustomUIElement e, int fallback)
        {
            return e != null && e.dropdown != null ? e.dropdown.value : fallback;
        }

        private static bool Switch(CustomUIElement e)
        {
            return e == null || e.toggle == null || e.toggle.isOn;
        }

        private static float Number(CustomUIElement e, float fallback)
        {
            return e != null && e.slider != null ? e.slider.value : fallback;
        }

        /// 把控件当前的值读进 l。
        private void Read(Look l, float width, float height)
        {
            l.Classic = classicIndex >= 0 && Index(cStyle, -1) == classicIndex;
            l.Paper = Index(cPaper, 0);
            l.NotOwnedPaper = Index(cNotOwnedPaper, 1);
            l.Icon = Switch(cIcon);
            l.Prices = Switch(cPrices);
            l.Description = Switch(cDescription);
            l.Footer = Switch(cFooter);
            l.Contents = Switch(cContents);
            l.Tree = Switch(cTree);
            l.MaxChars = Mathf.RoundToInt(Number(cMaxWidth, 46f));
            l.Scale = Number(cScale, 100f) / 100f;
            l.PriceScale = Number(cPriceSize, 220f) / 100f;
            l.Opacity = Number(cOpacity, 100f) / 100f;
            l.TagWash = Switch(cTagBackground);
            // 每组的列表按顺序装 Ticket.TagColorNames；列表缺失就用该组的默认色。
            for (int i = 0; i < l.Tags.Length; i++) l.Tags[i] = Index(cTagColors[i], Ticket.TagDefaults[i]);
            l.Width = Mathf.Round(width);
            l.Height = Mathf.Round(height);
        }

        /// 它跟随的控件一变就重画示例：两张估价单（自己的物品、未拥有的物品），或者选了经典风格时画在原版窗口里的样子。
        private void UpdatePreview()
        {
            float width = preview.rect.width, height = aside ? preview.rect.height : 0f;
            if (width <= 0f || (aside && height <= 0f)) return;
            Read(now, width, height);
            if (!now.Same(seen))
            {
                seen.CopyFrom(now);
                changedAt = Time.unscaledTime;
            }
            if (drawn && (shown.Same(seen) || Time.unscaledTime - changedAt < RedrawAfter)) return;
            shown.CopyFrom(seen);
            if (!drawn && Settings.Debug != null && Settings.Debug.Value)
                SectionedTooltipsMod.Log(string.Format("settings preview {0}: {1:F0}x{2:F0}, window {3:F0}x{4:F0}",
                    aside ? "aside" : "in the list", width, preview.rect.height, window.Rect.rect.width, window.Rect.rect.height));
            drawn = true;
            Draw(shown);
        }

        private void Draw(Look l)
        {
            float width = l.Width;
            SampleLook own = new SampleLook(), other = new SampleLook();
            own.Paper = l.Paper;
            other.Paper = l.NotOwnedPaper;
            other.NotOwned = true;
            own.Icon = other.Icon = l.Icon;
            own.Prices = other.Prices = l.Prices;
            own.Description = other.Description = l.Description;
            own.Footer = other.Footer = l.Footer;
            own.Contents = other.Contents = l.Contents;
            own.PriceScale = other.PriceScale = l.PriceScale;
            own.Opacity = other.Opacity = l.Opacity;
            own.TagWash = other.TagWash = l.TagWash;
            own.TagColors = other.TagColors = (int[])l.Tags.Clone();
            bool classic = l.Classic, tree = l.Tree;
            int maxChars = l.MaxChars;
            float scale = l.Scale;

            ClearPreview();
            float captionSize = font.fontSize * 0.8f;
            float top = Mathf.Round(captionSize * 1.6f) + 6f;
            if (classic)
            {
                float size = font.fontSize * 0.75f;
                Vector2 s = ClassicSample.Draw(preview, font, size, 0f, top, own, tree, maxChars);
                if (aside && (s.x > width || top + s.y > l.Height))
                {
                    ClearPreview();
                    size *= Mathf.Min(width / s.x, (l.Height - top) / s.y) * 0.98f;
                    s = ClassicSample.Draw(preview, font, size, 0f, top, own, tree, maxChars);
                }
                float left = Mathf.Max(0f, Mathf.Round((width - s.x) / 2f));
                ((RectTransform)preview.GetChild(0)).anchoredPosition += new Vector2(left, 0f);
                Caption(SectionedTooltipsMod.L("preview_classic"), left, 0f, s.x, captionSize);
                if (!aside) Fit(top + s.y + 12f);
                return;
            }
            if (aside)
            {
                Stack(own, other, font.fontSize * 0.75f * scale, width, l.Height, top, captionSize);
                return;
            }
            // 估价单大小设置会缩放示例，但两张始终并排放得进面板。
            float gap = Mathf.Round(font.fontSize * 1.5f);
            float body = font.fontSize * 0.75f * scale;
            Vector2 a = Ticket.Sample(preview, font, body, 0f, top, own);
            Vector2 b = Ticket.Sample(preview, font, body, a.x + gap, top, other);
            float total = a.x + gap + b.x;
            if (total > width)
            {
                ClearPreview();
                body *= width / total * 0.98f;
                a = Ticket.Sample(preview, font, body, 0f, top, own);
                b = Ticket.Sample(preview, font, body, a.x + gap, top, other);
                total = a.x + gap + b.x;
            }
            // 居中：两张一起挪。
            float shift = Mathf.Max(0f, Mathf.Round((width - total) / 2f));
            for (int i = 0; i < preview.childCount; i++)
            {
                RectTransform rt = (RectTransform)preview.GetChild(i);
                rt.anchoredPosition += new Vector2(shift, 0f);
            }
            Caption(SectionedTooltipsMod.L("preview_own"), shift, 0f, a.x, captionSize);
            Caption(SectionedTooltipsMod.L("preview_not_owned"), shift + a.x + gap, 0f, b.x, captionSize);
            Fit(top + Mathf.Max(a.y, b.y) + 12f);
        }

        /// 设置旁边那一栏里上下叠放的两张估价单。大小设置会缩放它们，但两张始终放得进这一栏。
        private void Stack(SampleLook own, SampleLook other, float body, float width, float height, float top, float captionSize)
        {
            float gap = Mathf.Round(font.fontSize * 1.2f);
            Vector2 a = Ticket.Sample(preview, font, body, 0f, top, own);
            Vector2 b = Ticket.Sample(preview, font, body, 0f, top + a.y + gap + top, other);
            float w = Mathf.Max(a.x, b.x), room = height - 2f * top - gap;
            if (w > width || a.y + b.y > room)
            {
                ClearPreview();
                body *= Mathf.Min(width / w, room / (a.y + b.y)) * 0.98f;
                a = Ticket.Sample(preview, font, body, 0f, top, own);
                b = Ticket.Sample(preview, font, body, 0f, top + a.y + gap + top, other);
            }
            // 各自在这一栏里居中。
            float ax = Mathf.Max(0f, Mathf.Round((width - a.x) / 2f)), bx = Mathf.Max(0f, Mathf.Round((width - b.x) / 2f));
            ((RectTransform)preview.GetChild(0)).anchoredPosition += new Vector2(ax, 0f);
            ((RectTransform)preview.GetChild(1)).anchoredPosition += new Vector2(bx, 0f);
            Caption(SectionedTooltipsMod.L("preview_own"), ax, 0f, a.x, captionSize);
            Caption(SectionedTooltipsMod.L("preview_not_owned"), bx, top + a.y + gap, b.x, captionSize);
        }

        /// 清掉示例。Destroy 要到帧末才生效，所以顺便解除父子关系：否则这一帧新画的示例会排在它们后面，GetChild(0) 拿到的还是旧的。
        private void ClearPreview()
        {
            for (int i = preview.childCount - 1; i >= 0; i--)
            {
                GameObject old = preview.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            preview.DetachChildren();
        }

        /// 把方框调整到画出来的内容那么高。
        private void Fit(float h)
        {
            previewSize.minHeight = previewSize.preferredHeight = h;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)preview.parent);
        }

        private void Caption(string text, float x, float y, float width, float size)
        {
            TextMeshProUGUI t = Ui.NewText(preview, font, size, text, font.color, 0f);
            t.alignment = TextAlignmentOptions.TopLeft;
            Ui.Move(t.rectTransform, x + Mathf.Round((width - t.rectTransform.sizeDelta.x) / 2f), y);
        }
    }
}
