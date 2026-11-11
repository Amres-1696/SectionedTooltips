using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 紧挨物品 tooltip 的侧边面板：液位计和容器内容物网格。它们和 tooltip 窗口同级、同缩放，复制它的边框图片让自己像游戏自己的窗口，并每帧跟着它。
    internal static class Panels
    {
        private const int MaxColumns = 5;

        private struct FramePart
        {
            public Image Source;
            public float Left, Bottom, Right, Top;
        }

        internal sealed class Metrics
        {
            public TMP_Text Src;
            public float Line, Font, Pad, Gap;
        }

        private static RectTransform root;
        /// tooltip 的可见方框：它最大的边框图片。PixelWindow 自己的 RectTransform 没有尺寸（窗口是子物体画的），不能拿来算边界。
        private static RectTransform box;
        private static LayoutResult builtFor;
        private static GameItemElement builtItem;
        private static Vector2 builtSize;
        private static readonly Vector3[] corners = new Vector3[4];

        internal static void Show(PixelWindow window, TMP_Text tmp, LayoutResult r, GameItemElement item)
        {
            bool liquid = r != null && r.Liquid != null;
            bool contents = r != null && r.Contents != null && r.Contents.Entries.Count > 0;
            RectTransform win = window != null ? window.rectTransform : null;
            RectTransform parent = win != null ? win.parent as RectTransform : null;
            if ((!liquid && !contents) || tmp == null || parent == null)
            {
                Hide();
                return;
            }
            bool fresh = EnsureRoot(parent, win);
            if (fresh || !ReferenceEquals(builtFor, r) || !ReferenceEquals(builtItem, item)) Build(win, tmp, r, item);
            Position(parent, win);
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
        }

        internal static void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        private static bool EnsureRoot(RectTransform parent, RectTransform win)
        {
            bool fresh = false;
            if (root == null || root.parent != parent)
            {
                if (root != null) UnityEngine.Object.Destroy(root.gameObject);
                GameObject go = new GameObject("SectionedTooltipsPanels", typeof(RectTransform));
                go.layer = win.gameObject.layer;
                root = (RectTransform)go.transform;
                root.SetParent(parent, false);
                root.anchorMin = new Vector2(0.5f, 0.5f);
                root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0f, 1f);
                root.sizeDelta = Vector2.zero;
                fresh = true;
            }
            // 和窗口同缩放，复制来的边框和 tooltip 的度量在这里才对得上。
            if (root.localScale != win.localScale) { root.localScale = win.localScale; fresh = true; }
            root.localRotation = Quaternion.identity;
            return fresh;
        }

        // ---------------------------------------------------------------- 构建

        private static void Build(RectTransform win, TMP_Text tmp, LayoutResult r, GameItemElement item)
        {
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
            // 全部建完才标记完成：建到一半失败的面板会被重建，不会留着。
            builtFor = null;
            builtItem = null;
            builtSize = Vector2.zero;

            Metrics m = Measure(tmp, root);
            box = FindBox(win, tmp);
            List<FramePart> frame = FrameParts(win, tmp);
            if (Settings.Debug != null && Settings.Debug.Value)
                SectionedTooltipsMod.Log(string.Format("panels: window rect={0}, box={1} rect={2}, {3} frame part(s), line={4} font={5}",
                    win.rect, box != null ? box.name : "none", box != null ? box.rect.ToString() : "-", frame.Count, m.Line, m.Font));
            float y = 0f, width = 0f;

            if (r.Liquid != null)
            {
                RectTransform p = LiquidPanel(r.Liquid, m, frame);
                Place(p, 0f, y, p.sizeDelta.x, p.sizeDelta.y);
                y += p.sizeDelta.y + m.Gap;
                width = Mathf.Max(width, p.sizeDelta.x);
            }
            if (r.Contents != null && r.Contents.Entries.Count > 0)
            {
                RectTransform p = ContentsPanel(r.Contents, m, frame);
                Place(p, 0f, y, p.sizeDelta.x, p.sizeDelta.y);
                y += p.sizeDelta.y + m.Gap;
                width = Mathf.Max(width, p.sizeDelta.x);
            }
            builtSize = new Vector2(width, Mathf.Max(0f, y - m.Gap));
            builtFor = r;
            builtItem = item;
        }

        private static TMP_FontAsset lineFont;
        private static float lineFontSize = -1f, lineHeight;

        /// tooltip 文字第一行的高度，用文字自己的单位。读它要整个重排一遍（ForceMeshUpdate），满容器时这占了估价单大半构建时间，而它只跟字体和字号有关：每种算一次。
        private static float LineHeight(TMP_Text tmp)
        {
            if (ReferenceEquals(tmp.font, lineFont) && tmp.fontSize == lineFontSize) return lineHeight;
            tmp.ForceMeshUpdate();
            TMP_TextInfo ti = tmp.textInfo;
            if (ti == null || ti.lineCount == 0) return tmp.fontSize * 1.25f; // 还没有排版结果：不缓存
            lineFont = tmp.font;
            lineFontSize = tmp.fontSize;
            lineHeight = ti.lineInfo[0].ascender - ti.lineInfo[0].descender;
            return lineHeight;
        }

        /// tooltip 文字的行高和字号，换算到 target 的单位，画出来和游戏的一样大；size 用来放大或缩小。
        internal static Metrics Measure(TMP_Text tmp, RectTransform target, float size = 1f)
        {
            Metrics m = new Metrics();
            m.Src = tmp;
            float a = Mathf.Abs(tmp.rectTransform.lossyScale.y), b = Mathf.Abs(target.lossyScale.y);
            float k = (a > 0f && b > 0f ? a / b : 1f) * size;
            float line = LineHeight(tmp);
            m.Line = Mathf.Round(Mathf.Max(8f, line * k));
            m.Font = tmp.fontSize * k;
            m.Pad = Mathf.Round(m.Line * 0.5f);
            m.Gap = Mathf.Max(2f, Mathf.Round(m.Line * 0.2f));
            return m;
        }

        /// tooltip 的可见方框：窗口下包住正文中心的最大可见图片（文字和我们自己的东西以外）。
        /// 其他模组挂进窗口的大图（比如自己画的一整块面板）不包住正文，不会被当成方框；一张都不包住时才退回最大的那张。
        internal static RectTransform FindBox(RectTransform win, TMP_Text tmp)
        {
            RectTransform best = null, around = null;
            float bestArea = 0f, aroundArea = 0f;
            Vector3 center = tmp.rectTransform.TransformPoint(tmp.rectTransform.rect.center);
            Image[] images = win.GetComponentsInChildren<Image>(false);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null || !img.enabled || img.transform.IsChildOf(tmp.transform) || Ours(img.transform, win)) continue;
                RectTransform rt = img.rectTransform;
                Rect r = rt.rect;
                Vector3 s = rt.lossyScale;
                float area = Mathf.Abs(r.width * s.x * r.height * s.y);
                if (area > bestArea) { bestArea = area; best = rt; }
                if (area > aroundArea && r.Contains(rt.InverseTransformPoint(center))) { aroundArea = area; around = rt; }
            }
            return around ?? best;
        }

        /// 是我们自己挂在窗口下的东西（名字都以 SectionedTooltips 开头）。
        private static bool Ours(Transform t, Transform win)
        {
            for (; t != null && t != win; t = t.parent)
                if (t.name.StartsWith("SectionedTooltips", StringComparison.Ordinal)) return true;
            return false;
        }

        /// 窗口的边框图片（覆盖方框大半、在文字以外），连同各自的内缩。
        private static List<FramePart> FrameParts(RectTransform win, TMP_Text tmp)
        {
            List<FramePart> parts = new List<FramePart>();
            if (box == null) return parts;
            box.GetWorldCorners(corners);
            Vector3 wMin = win.InverseTransformPoint(corners[0]);
            Vector3 wMax = win.InverseTransformPoint(corners[2]);
            float ww = wMax.x - wMin.x, wh = wMax.y - wMin.y;
            if (ww <= 0f || wh <= 0f) return parts;

            Image[] images = win.GetComponentsInChildren<Image>(false);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null || !img.enabled || img.transform.IsChildOf(tmp.transform)) continue;
                img.rectTransform.GetWorldCorners(corners);
                Vector3 a = win.InverseTransformPoint(corners[0]);
                Vector3 c = win.InverseTransformPoint(corners[2]);
                if (c.x - a.x < ww * 0.5f || c.y - a.y < wh * 0.5f) continue;
                FramePart p;
                p.Source = img;
                p.Left = a.x - wMin.x;
                p.Bottom = a.y - wMin.y;
                p.Right = wMax.x - c.x;
                p.Top = wMax.y - c.y;
                parts.Add(p);
            }
            return parts;
        }

        private static RectTransform NewPanel(string name, List<FramePart> frame, float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = root.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);

            if (frame.Count == 0)
            {
                Stretch(NewImage(rt, new Color(0.45f, 0.22f, 0.2f)), 0f, 0f, 0f, 0f);
                Stretch(NewImage(rt, new Color(0.13f, 0.11f, 0.18f)), 2f, 2f, 2f, 2f);
            }
            for (int i = 0; i < frame.Count; i++)
            {
                Image src = frame[i].Source;
                Image img = NewImage(rt, src.color);
                img.sprite = src.sprite;
                img.type = src.type;
                img.fillCenter = src.fillCenter;
                img.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier;
                img.material = src.material;
                Stretch(img, frame[i].Left, frame[i].Bottom, frame[i].Right, frame[i].Top);
            }
            return rt;
        }

        /// “▌ 标题”配小节条的底色，和 tooltip 里的小节标题一样。
        private static void Header(RectTransform panel, Metrics m, string key, string title, float width)
        {
            Sections.Style st = Sections.StyleOf(key);
            float mark = Mathf.Max(2f, Mathf.Round(m.Line * 0.3f));
            Place(NewImage(panel, Overlay.ToColor(Layout.HeaderBg, Palette.VeryDarkGray)), m.Pad, m.Pad, width - 2f * m.Pad, m.Line);
            Place(NewImage(panel, Overlay.ToColor(st.Accent, Palette.Gray)), m.Pad, m.Pad, mark, m.Line);
            TextMeshProUGUI t = Text(panel, m, title, Overlay.ToColor(st.Text, Palette.White));
            Place(t.rectTransform, m.Pad + mark + mark, m.Pad, t.rectTransform.sizeDelta.x, m.Line);
        }

        private static float HeaderWidth(Metrics m, string title)
        {
            return Mathf.Ceil(Size(root, m, title).x) + Mathf.Round(m.Line * 0.6f) + m.Line + 2f * m.Pad;
        }

        private static RectTransform LiquidPanel(LiquidInfo liquid, Metrics m, List<FramePart> frame)
        {
            string title = SectionedTooltipsMod.L("panel_liquid");
            string pct = Mathf.RoundToInt(liquid.Ratio * 100f) + "%";
            string amount = Rich(liquid.Text);
            float gaugeW = Mathf.Round(m.Line * 0.9f), gaugeH = Mathf.Round(m.Line * 3.5f);
            float textW = Mathf.Max(Size(root, m, pct).x, Size(root, m, amount).x);
            float w = Mathf.Ceil(Mathf.Max(m.Pad + gaugeW + m.Pad + textW + m.Pad, HeaderWidth(m, title)));
            float top = m.Pad + m.Line + m.Gap * 2f;
            float h = top + gaugeH + m.Pad;

            RectTransform p = NewPanel("liquid", frame, w, h);
            Header(p, m, Sections.Liquid, title, w);

            // 液位计：轨道、从下往上填充、四分之一刻度。
            Color fillColor = Overlay.ToColor(liquid.Color, Palette.Cyan);
            Place(NewImage(p, Overlay.ToColor(liquid.Background, Palette.VeryDarkGray)), m.Pad, top, gaugeW, gaugeH);
            float fill = Mathf.Round((gaugeH - 2f) * Mathf.Clamp01(liquid.Ratio));
            if (fill >= 1f) Place(NewImage(p, fillColor), m.Pad + 1f, top + gaugeH - 1f - fill, gaugeW - 2f, fill);
            Color tick = new Color(0f, 0f, 0f, 0.35f);
            for (int q = 1; q <= 3; q++)
                Place(NewImage(p, tick), m.Pad + 1f, top + Mathf.Round(gaugeH * q / 4f), Mathf.Round(gaugeW * 0.4f), 1f);

            float tx = m.Pad + gaugeW + m.Pad;
            TextMeshProUGUI tp = Text(p, m, pct, fillColor);
            Place(tp.rectTransform, tx, top, tp.rectTransform.sizeDelta.x, m.Line);
            if (amount.Length > 0)
            {
                TextMeshProUGUI ta = Text(p, m, amount, Color.white);
                Place(ta.rectTransform, tx, top + m.Line, ta.rectTransform.sizeDelta.x, m.Line);
            }
            return p;
        }

        private static RectTransform ContentsPanel(ContentsInfo contents, Metrics m, List<FramePart> frame)
        {
            List<ContentEntry> entries = contents.Entries;
            int maxCells = MaxColumns * Settings.MaxContentRows;
            int shown = entries.Count <= maxCells ? entries.Count : maxCells - 1;
            int more = 0;
            for (int i = shown; i < entries.Count; i++) more += entries[i].Count;
            int cells = shown + (more > 0 ? 1 : 0);
            int cols = Math.Min(MaxColumns, cells);
            int rows = (cells + cols - 1) / cols;

            string title = SectionedTooltipsMod.L("panel_contents") + "  " + contents.Total;
            float cell = Mathf.Round(m.Line * 1.8f), gap = m.Gap;
            float gridW = cols * cell + (cols - 1) * gap;
            float w = Mathf.Ceil(Mathf.Max(2f * m.Pad + gridW, HeaderWidth(m, title)));
            float top = m.Pad + m.Line + m.Gap * 2f;
            float h = top + rows * cell + (rows - 1) * gap + m.Pad;

            RectTransform p = NewPanel("contents", frame, w, h);
            Header(p, m, Sections.Container, title, w);

            Ticket.PrepareIcons(entries, shown);
            Color slot = new Color(0f, 0f, 0f, 0.35f);
            Color edge = new Color(1f, 1f, 1f, 0.07f);
            for (int i = 0; i < cells; i++)
            {
                float x = m.Pad + (i % cols) * (cell + gap);
                float y = top + (i / cols) * (cell + gap);
                Place(NewImage(p, edge), x, y, cell, cell);
                Place(NewImage(p, slot), x + 1f, y + 1f, cell - 2f, cell - 2f);

                if (i >= shown)
                {
                    TextMeshProUGUI t = Text(p, m, "+" + more, Color.white);
                    Vector2 s = t.rectTransform.sizeDelta;
                    Place(t.rectTransform, x + Mathf.Round((cell - s.x) / 2f), y + Mathf.Round((cell - m.Line) / 2f), s.x, m.Line);
                    continue;
                }

                Sprite icon = Icons.IconOf(entries[i].Item);
                if (icon != null)
                {
                    float iw, ih;
                    Sprite sprite = Icons.Fit(icon, cell - 4f, cell - 4f, Icons.PxOf(p), out iw, out ih);
                    if (sprite == null)
                    {
                        sprite = icon;
                        Overlay.Fit(icon, cell - 4f, cell - 4f, out iw, out ih);
                    }
                    Image img = NewImage(p, Color.white);
                    img.sprite = sprite;
                    Place(img, x + Mathf.Round((cell - iw) / 2f), y + Mathf.Round((cell - ih) / 2f), iw, ih);
                }
                if (entries[i].Count > 1)
                {
                    // 右下角的小数字，带 1 像素阴影，像背包里的堆叠数。
                    string n = entries[i].Count.ToString();
                    TextMeshProUGUI shadow = Text(p, m, n, new Color(0f, 0f, 0f, 0.9f), CountScale);
                    TextMeshProUGUI t = Text(p, m, n, Color.white, CountScale);
                    Vector2 s = t.rectTransform.sizeDelta;
                    float bx = x + cell - s.x - 2f, by = y + cell - s.y;
                    Place(shadow.rectTransform, bx + 1f, by + 1f, s.x, s.y);
                    Place(t.rectTransform, bx, by, s.x, s.y);
                }
            }
            return p;
        }

        // ---------------------------------------------------------------- 基础件

        private static string Rich(List<Seg> segs)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (s.Bar != null) continue;
                sb.Append(s.Color == Palette.None ? s.Text : RenderHandler.ApplyRichTextColorTag(s.Text, s.Color));
            }
            return sb.ToString();
        }

        private static Image NewImage(RectTransform parent, Color color)
        {
            return Ui.NewImage(parent, color);
        }

        /// 内容物网格里的堆叠数，相对 tooltip 字号。
        private const float CountScale = 0.65f;

        private static TextMeshProUGUI Text(RectTransform parent, Metrics m, string rich, Color color)
        {
            return Text(parent, m, rich, color, 1f);
        }

        private static TextMeshProUGUI Text(RectTransform parent, Metrics m, string rich, Color color, float scale)
        {
            return Ui.NewText(parent, m.Src, m.Font * scale, rich, color, 0f);
        }

        /// rich 在面板字体下的首选尺寸，用一个临时文字量。
        private static Vector2 Size(RectTransform parent, Metrics m, string rich)
        {
            TextMeshProUGUI t = Text(parent, m, rich, Color.clear);
            Vector2 size = t.rectTransform.sizeDelta;
            UnityEngine.Object.Destroy(t.gameObject);
            return size;
        }

        private static void Place(Graphic g, float x, float yTop, float w, float h)
        {
            Ui.Place(g, x, yTop, w, h);
        }

        private static void Place(RectTransform rt, float x, float yTop, float w, float h)
        {
            Ui.Place(rt, x, yTop, w, h);
        }

        private static void Stretch(Image img, float left, float bottom, float right, float top)
        {
            Ui.Stretch(img, left, bottom, right, top);
        }

        // ---------------------------------------------------------------- 摆放

        /// 在 tooltip 右边、顶对齐；屏幕边缘放不下时放左边。
        private static void Position(RectTransform parent, RectTransform win)
        {
            (box != null ? box : win).GetWorldCorners(corners);
            Vector3 bl = parent.InverseTransformPoint(corners[0]);
            Vector3 tr = parent.InverseTransformPoint(corners[2]);
            float sx = Mathf.Abs(win.localScale.x), sy = Mathf.Abs(win.localScale.y);
            float w = builtSize.x * sx, h = builtSize.y * sy;
            float gap = Mathf.Round(4f * sx);
            Rect screen = ScreenRect(parent, win);

            float x = tr.x + gap;
            if (x + w > screen.xMax && bl.x - gap - w >= screen.xMin) x = bl.x - gap - w;
            // 两边都没地方：压在 tooltip 边上，总比出屏好。
            if (x + w > screen.xMax) x = Mathf.Max(screen.xMin, screen.xMax - w);
            float y = tr.y;
            if (y - h < screen.yMin) y = screen.yMin + h;
            if (y > screen.yMax) y = screen.yMax;
            root.localPosition = new Vector3(Mathf.Round(x), Mathf.Round(y), 0f);
        }

        /// 可见区域，用父节点的坐标。tooltip 的父节点是个零尺寸的容器，所以从铺满屏幕的根 canvas 取。
        // 只在换了窗口时才重新找根 canvas（这个每帧都在跑）。
        private static RectTransform areaWin, area;

        internal static Rect ScreenRect(RectTransform parent, RectTransform win)
        {
            if (!ReferenceEquals(win, areaWin) || area == null)
            {
                Canvas canvas = win.GetComponentInParent<Canvas>();
                area = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
                areaWin = win;
            }
            if (area == null) return parent.rect;
            area.GetWorldCorners(corners);
            Vector3 a = parent.InverseTransformPoint(corners[0]);
            Vector3 b = parent.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }
}
