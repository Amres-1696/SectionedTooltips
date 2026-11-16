using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 画在物品 tooltip 上的图形：物品图标和实心进度条。挂在 tooltip 文字的 TextMeshPro 下面，按 TMP 自己的行和字符度量摆放，正好落在排版留出的空白上。
    internal static class Overlay
    {
        // GetTooltip 刚生成的 RichText，以及它对应的排版。
        private static RichText pendingText;
        /// 游戏放置这个 tooltip 时的出发点（屏幕坐标下的鼠标位置），如果它给了的话。
        internal static Vector2? Cursor;
        private static LayoutResult pendingResult;
        private static GameItemElement pendingItem;

        private static RectTransform root;
        private static TMP_Text rootText;
        private static LayoutResult shownResult;
        private static GameItemElement shownItem;
        private static Vector2 shownSize;

        // 读度量时文字矩形的左上角；所有东西都相对它摆放。
        private static float originX, originY;
        private static readonly List<Image> pool = new List<Image>();
        private static int used;

        private static FieldInfo fTooltipElement;
        private static bool reported;

        /// 游戏是否用物品自己的 tooltip（比如背包里的人物卡）顶掉基础 tooltip：那时显示的文字不是排版用的那份，就不动它。
        private static bool Replaced(GameItemElement item)
        {
            // 每帧都问；只有换了物品答案才会变，而换物品会隐藏 tooltip。
            if (ReferenceEquals(item, replacedItem)) return replaced;
            replacedItem = item;
            replaced = ReplacedNow(item);
            return replaced;
        }

        private static GameItemElement replacedItem;
        private static bool replaced;

        private static bool ReplacedNow(GameItemElement item)
        {
            try
            {
                if (item == null || item.parentInventory == null) return false;
                // 走反射：老编译器读不了这个属性的 Func 类型。
                if (tooltipFunc == null) tooltipFunc = AccessTools.PropertyGetter(typeof(GameItemFunc), "tooltipRichTextFunc");
                return tooltipFunc != null && tooltipFunc.Invoke(item, null) != null;
            }
            catch { return false; }
        }

        private static MethodInfo tooltipFunc;

        internal static void Note(GameItemElement item, RichText text)
        {
            if (text != null && ReferenceEquals(Layout.LastItem, item) && Layout.LastResult != null && !Replaced(item))
            {
                pendingText = text;
                pendingResult = Layout.LastResult;
                pendingItem = item;
                if (!Settings.IsTicket && Settings.On(Settings.Enabled) && !Fuse.IsOpen(Fuse.LayoutKey)) EvenLines(text);
            }
            else pendingText = null;
        }

        /// 经典风格的分节底条画在单独一层文字上（RichTextElement 的 textBackgroundNode），和正文各排各的行。
        /// 正文某行有回退字体里的字（俄文字母、部分符号）时那一行会变高，底色层那一行不会，往下就越错越多。
        /// 几层都固定成主字体的行高，行就一一对上。
        private const string EvenLine = "<line-height=100%>";

        private static void EvenLines(RichText t)
        {
            try
            {
                if (layerSetters == null)
                {
                    string[] names = { "richTextForeground", "richTextBackground", "richTextUnderline", "richTextStrikethrough" };
                    layerSetters = new MethodInfo[names.Length];
                    for (int i = 0; i < names.Length; i++) layerSetters[i] = AccessTools.PropertySetter(typeof(RichText), names[i]);
                }
                string[] now = { t.richTextForeground, t.richTextBackground, t.richTextUnderline, t.richTextStrikethrough };
                for (int i = 0; i < layerSetters.Length; i++)
                    if (layerSetters[i] != null) layerSetters[i].Invoke(t, new object[] { Even(now[i]) });
            }
            catch { }
        }

        /// RichText 四层文字的 setter 是私有的。
        private static MethodInfo[] layerSetters;

        private static string Even(string s)
        {
            return string.IsNullOrEmpty(s) || s.StartsWith(EvenLine, StringComparison.Ordinal) ? s : EvenLine + s;
        }

        internal static void OnShow(OverlayHandler handler, RichText text)
        {
            try
            {
                if (text == null || !ReferenceEquals(text, pendingText) || !Settings.On(Settings.Enabled))
                {
                    Hide();
                    return;
                }
                LayoutResult r = pendingResult;
                TMP_Text tmp = r != null ? TooltipText(handler) : null;
                if (tmp == null)
                {
                    Hide();
                    return;
                }
                if (Settings.IsTicket && r.Blocks != null)
                {
                    HideInline();
                    Panels.Hide();
                    Ticket.Show(handler.GetTooltipWindow(), tmp, r, pendingItem);
                    return;
                }
                Ticket.Hide();
                if (r.Bars.Count == 0 && r.IconLine < 0) HideInline();
                // 之后窗口布局可能改变文字矩形的大小；变了就重新测。
                else if (!(root != null && root.gameObject.activeSelf && ReferenceEquals(r, shownResult)
                    && ReferenceEquals(pendingItem, shownItem) && ReferenceEquals(tmp, rootText)
                    && tmp.rectTransform.rect.size == shownSize))
                    Build(tmp, r, pendingItem);
                Panels.Show(handler.GetTooltipWindow(), tmp, r, pendingItem);
            }
            catch (Exception e)
            {
                try { Hide(); }
                catch { }
                if (!reported)
                {
                    reported = true;
                    SectionedTooltipsMod.Error("tooltip graphics failed, showing text only: " + e);
                }
            }
        }

        internal static void Hide()
        {
            replacedItem = null;
            HideInline();
            Panels.Hide();
            Ticket.Hide();
        }

        private static void HideInline()
        {
            shownResult = null;
            shownItem = null;
            if (root != null) root.gameObject.SetActive(false);
        }

        /// 把 sprite 装进 w×h 后的大小；放大时取整数倍。
        internal static void Fit(Sprite sprite, float w, float h, out float outW, out float outH)
        {
            float sw = sprite.rect.width, sh = sprite.rect.height;
            if (sw <= 0f || sh <= 0f || w <= 0f || h <= 0f) { outW = outH = 0f; return; }
            float scale = Mathf.Min(w / sw, h / sh);
            if (scale >= 1f) scale = Mathf.Floor(scale);
            outW = Mathf.Round(sw * scale);
            outH = Mathf.Round(sh * scale);
        }

        private static TMP_Text TooltipText(OverlayHandler handler)
        {
            if (handler == null) return null;
            if (fTooltipElement == null) fTooltipElement = AccessTools.Field(typeof(OverlayHandler), "tooltipElement");
            if (fTooltipElement == null) return null;
            RichTextElement element = fTooltipElement.GetValue(handler) as RichTextElement;
            if (element == null) return null;
            TreeNodeRender node = element.GetText();
            return node != null ? node.text : null;
        }

        private static void Build(TMP_Text tmp, LayoutResult r, GameItemElement item)
        {
            EnsureRoot(tmp);
            used = 0;
            tmp.ForceMeshUpdate();
            TMP_TextInfo ti = tmp.textInfo;
            Rect rect = tmp.rectTransform.rect;
            originX = rect.xMin;
            originY = rect.yMax;
            shownSize = rect.size;

            if (r.IconLine >= 0 && Settings.On(Settings.Icon))
            {
                Sprite icon = Icons.IconOf(item);
                if (icon != null) DrawIcon(ti, r, icon);
            }
            for (int i = 0; i < r.Bars.Count; i++) DrawBar(ti, r.Bars[i]);

            if (Settings.Debug != null && Settings.Debug.Value) DumpMetrics(tmp, ti, r);

            for (int i = used; i < pool.Count; i++) if (pool[i] != null) pool[i].gameObject.SetActive(false);
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
            shownResult = r;
            shownItem = item;
            rootText = tmp;
        }

        private static void DrawIcon(TMP_TextInfo ti, LayoutResult r, Sprite icon)
        {
            if (r.IconLine + 1 >= ti.lineCount || r.IconChars <= 0) return;
            TMP_LineInfo a = ti.lineInfo[r.IconLine];
            TMP_LineInfo b = ti.lineInfo[r.IconLine + 1];
            int first = a.firstCharacterIndex;
            int last = first + r.IconChars - 1;
            if (first < 0 || last >= ti.characterCount) return;

            float left = ti.characterInfo[first].origin;
            float roomW = ti.characterInfo[last].xAdvance - left - 4f; // 名字前面留一点空隙
            float top = a.ascender, bottom = b.descender;
            float roomH = top - bottom;
            if (roomW < 4f || roomH < 4f) return;
            float w, h;
            Sprite sprite = Icons.Fit(icon, roomW, roomH, Icons.PxOf(root), out w, out h);
            if (sprite == null)
            {
                sprite = icon;
                Fit(icon, roomW, roomH, out w, out h);
            }
            if (w <= 0f) return;

            // 挨着名字：顶对齐；比第一行矮就居中在第一行。
            float lineH = a.ascender - a.descender;
            float y = h < lineH ? a.descender + Mathf.Floor((lineH - h) / 2f) : top - h;

            Image img = Take();
            img.sprite = sprite;
            img.color = Color.white;
            Place(img, left, y, w, h);
        }

        private static void DrawBar(TMP_TextInfo ti, OverlayBar bar)
        {
            if (bar.Line >= ti.lineCount) return;
            TMP_LineInfo li = ti.lineInfo[bar.Line];
            int i0 = li.firstCharacterIndex + bar.Char;
            int i1 = i0 + bar.Count - 1;
            if (i0 < 0 || i1 >= ti.characterCount) return;

            float x0 = Mathf.Round(ti.characterInfo[i0].origin);
            float x1 = Mathf.Round(ti.characterInfo[i1].xAdvance);
            float h = li.ascender - li.descender;
            float bh = Mathf.Max(4f, Mathf.Round(h * 0.45f));
            float y = Mathf.Round(li.descender + (h - bh) / 2f);
            if (x1 - x0 < 4f) return;

            Image track = Take();
            track.color = ToColor(bar.Background, Palette.VeryDarkGray);
            Place(track, x0, y, x1 - x0, bh);

            float fill = Mathf.Round((x1 - x0 - 2f) * bar.Ratio);
            if (fill >= 1f)
            {
                Image img = Take();
                img.color = ToColor(bar.Color, Palette.White);
                Place(img, x0 + 1f, y + 1f, fill, bh - 2f);
            }
        }

        private static void DumpMetrics(TMP_Text tmp, TMP_TextInfo ti, LayoutResult r)
        {
            RectTransform rt = tmp.rectTransform;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format("overlay: rect={0} pivot={1} align={2} lines={3} chars={4} scale={5}",
                rt.rect, rt.pivot, tmp.alignment, ti.lineCount, ti.characterCount, rt.lossyScale));
            for (int i = 0; i < ti.lineCount && i < 12; i++)
            {
                TMP_LineInfo li = ti.lineInfo[i];
                float x = li.firstCharacterIndex < ti.characterCount ? ti.characterInfo[li.firstCharacterIndex].origin : float.NaN;
                sb.AppendLine(string.Format("  line {0}: first={1} asc={2:F1} desc={3:F1} x0={4:F1}", i, li.firstCharacterIndex, li.ascender, li.descender, x));
            }
            sb.AppendLine(string.Format("  icon line {0} ({1} chars), {2} bar(s)", r.IconLine, r.IconChars, r.Bars.Count));
            for (int i = 0; i < r.Bars.Count; i++)
                sb.AppendLine(string.Format("  bar line {0} char {1}+{2} ratio {3:F2}", r.Bars[i].Line, r.Bars[i].Char, r.Bars[i].Count, r.Bars[i].Ratio));
            for (int i = 0; i < used; i++)
                sb.AppendLine(string.Format("  image {0}: pos={1} size={2}", i, pool[i].rectTransform.anchoredPosition, pool[i].rectTransform.sizeDelta));
            SectionedTooltipsMod.Log(sb.ToString());
        }

        internal static Color ToColor(Palette p, Palette fallback)
        {
            if (p == Palette.None) p = fallback;
            try { return RenderHandler.GetColorFromPalette(p); }
            catch { return Color.gray; }
        }

        private static void EnsureRoot(TMP_Text tmp)
        {
            if (root == null || root.parent != tmp.rectTransform)
            {
                if (root != null) UnityEngine.Object.Destroy(root.gameObject);
                pool.Clear();
                GameObject go = new GameObject("SectionedTooltipsOverlay", typeof(RectTransform));
                go.layer = tmp.gameObject.layer;
                root = (RectTransform)go.transform;
                root.SetParent(tmp.rectTransform, false);
                // 钉在文字矩形的左上角，也就是 tooltip 文字开始的地方；窗口之后改大小它也不会跑。
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(0f, 1f);
                root.pivot = new Vector2(0f, 1f);
                root.sizeDelta = Vector2.zero;
                root.anchoredPosition = Vector2.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
            }
        }

        private static Image Take()
        {
            Image img = used < pool.Count ? pool[used] : null;
            if (img == null)
            {
                GameObject go = new GameObject("st", typeof(RectTransform), typeof(Image));
                go.layer = root.gameObject.layer;
                go.transform.SetParent(root, false);
                img = go.GetComponent<Image>();
                img.raycastTarget = false;
                RectTransform rt = img.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = Vector2.zero;
                if (used < pool.Count) pool[used] = img;
                else pool.Add(img);
            }
            used++;
            img.gameObject.SetActive(true);
            img.sprite = null;
            img.preserveAspect = false;
            img.type = Image.Type.Simple;
            return img;
        }

        /// 把 TMP 局部坐标的度量换算到文字矩形左上角再摆放。
        private static void Place(Image img, float x, float y, float w, float h)
        {
            RectTransform rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(Mathf.Round(x - originX), Mathf.Round(y - originY));
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
