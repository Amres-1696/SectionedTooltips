using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 估价单上小节以外的部件：小节标题、物品图标、内容物格子、描述、页脚。
    internal static partial class Ticket
    {
        private static float IconBox(Ctx c)
        {
            return Mathf.Round(c.M.Line * 1.9f);
        }

        /// 图标在方框里的大小：屏幕上是它像素的整数倍，放不下时用二分之一、四分之一。像素画被别的比例缩小（32 像素的图标塞进 25）会让某些行重复、某些行丢掉。
        private static void PixelFit(Ctx c, Sprite sprite, float w, float h, out float outW, out float outH)
        {
            outW = outH = 0f;
            float sw = sprite.rect.width, sh = sprite.rect.height;
            if (sw <= 0f || sh <= 0f || w <= 0f || h <= 0f) return;
            // 图标每个像素占多少屏幕像素。
            float s = Mathf.Min(w * c.Px / sw, h * c.Px / sh);
            s = s >= 1f ? Mathf.Floor(s) : Mathf.Pow(2f, Mathf.Floor(Mathf.Log(s, 2f)));
            outW = sw * s / c.Px;
            outH = sh * s / c.Px;
        }

        /// 图标居中放在 box 见方、四边内缩 pad 的框里，对齐整屏幕像素。透明边裁掉后撑满框（见 Icons），裁不了时按整数倍画原图。
        private static void PlaceIcon(Ctx c, Sprite icon, float x, float y, float box, float pad)
        {
            float iw, ih;
            Sprite sprite = Icons.Fit(icon, box - 2f * pad, box - 2f * pad, c.Px, out iw, out ih);
            if (sprite == null)
            {
                sprite = icon;
                PixelFit(c, icon, box - 2f * pad, box - 2f * pad, out iw, out ih);
            }
            if (iw <= 0f) return;
            Image img = Ui.NewImage(c.Card, Color.white);
            img.sprite = sprite;
            Ui.Place(img, Snap(c, x + (box - iw) / 2f), Snap(c, y + (box - ih) / 2f), iw, ih);
        }

        private static readonly List<Sprite> iconBatch = new List<Sprite>();

        /// 网格里前 shown 格的图标一起读出来（见 Icons.Prepare）。
        internal static void PrepareIcons(List<ContentEntry> entries, int shown)
        {
            iconBatch.Clear();
            for (int i = 0; i < shown && i < entries.Count; i++) iconBatch.Add(Icons.IconOf(entries[i].Item));
            Icons.Prepare(iconBatch);
            iconBatch.Clear();
        }

        /// 标题用的物品图标；没有图标或设置关掉时为 null。
        private static Sprite IconFor(GameItemElement item)
        {
            return Settings.On(Settings.Icon) ? Icons.IconOf(item) : null;
        }

        private static float Indent(Ctx c)
        {
            return Mathf.Round(c.M.Line * 0.45f);
        }

        /// 小节标题用它自己的墨色，下面一条细线横过单子。（以前标题后面垫一块实色，比下面的内容还抢眼。）
        /// 分节标题：全都用正文的墨色，下面一道纸边色的细线。颜色只留给数据本身（盈亏、成分、市场事件）和赃物、违禁品标签。
        private static float SectionHeader(Ctx c, string title, float y)
        {
            y += c.Gap * 1.5f;
            TextMeshProUGUI t = Text(c, c.F * 0.8f, "<b>" + title + "</b>", Ink, 0f);
            float th = t.rectTransform.sizeDelta.y;
            Ui.Move(t.rectTransform, c.Pad, y);
            Solid(c, PaperEdge, c.Pad, y + th + c.U, c.Inner, c.U);
            return y + th + 2f * c.U + c.Gap * 0.75f;
        }

        /// 一行：“标签 ........ 数值”、小标题，或者折行的文字，有进度条就画上。
        private static float RowLine(Ctx c, Row row, float y, float left)
        {
            float size = c.F * 0.9f;
            float inner = c.W - c.Pad - left;
            if (row.Heading)
            {
                TextMeshProUGUI h = Text(c, c.F * 0.8f, InkRich(row.Label, InkSoft), Color.white, inner);
                Ui.Move(h.rectTransform, left, y);
                return y + h.rectTransform.sizeDelta.y;
            }
            if (!row.IsPair)
            {
                float x = left;
                BarMark bar = BarOf(row.Segs);
                // 进度条配一个短读数（“416 ml / 750 ml”、“10/28”）：进度条铺满整行，读数放在右端，和内容物的量条一样。
                string reading = bar != null ? InkRich(row.Segs, Ink) : null;
                if (bar != null && c.Measure(reading, size) <= inner * 0.5f)
                {
                    float lh = Mathf.Ceil(c.Size(" ", size).y);
                    TextMeshProUGUI r = Text(c, size, reading, Color.white, 0f);
                    Vector2 rs = r.rectTransform.sizeDelta;
                    Ui.Move(r.rectTransform, c.W - c.Pad - rs.x, y);
                    DrawBar(c, bar, left, y, c.W - c.Pad - rs.x - c.Gap - left, lh);
                    return y + Mathf.Max(lh, rs.y);
                }
                if (bar != null)
                {
                    float lh = Mathf.Ceil(c.Size(" ", size).y); // 这一行的行高
                    DrawBar(c, bar, x, y, BarWidth(c), lh);
                    x += BarWidth(c) + c.Gap;
                }
                TextMeshProUGUI t = Text(c, size, InkRich(row.Segs, Ink), Color.white, c.W - c.Pad - x);
                Ui.Move(t.rectTransform, x, y);
                return y + t.rectTransform.sizeDelta.y;
            }

            // 标签用棕色的墨，只有小节标题带颜色；数值保留自己的颜色。比单子还宽的标签或数值（标签上的“受日用品救济行动影响，……(-40%)”）折行而不是压出边；这时数值接在标签下面一行。
            BarMark vbar = BarOf(row.Value);
            float barW = vbar != null ? BarWidth(c) + c.Gap : 0f;
            string labelRich = InkRich(row.Label, InkSoft), valueRich = InkRich(row.Value, Ink);
            TextMeshProUGUI label = Text(c, size, labelRich, Color.white, c.Measure(labelRich, size) > inner ? inner : 0f);
            TextMeshProUGUI value = Text(c, size, valueRich, Color.white, c.Measure(valueRich, size) > inner - barW ? inner - barW : 0f);
            Vector2 ls = label.rectTransform.sizeDelta, vs = value.rectTransform.sizeDelta;
            float lineH = Mathf.Max(ls.y, vs.y);

            Ui.Move(label.rectTransform, left, y);
            float vy = y;
            if (ls.x + 3f * c.Gap + barW + vs.x > inner)
            {
                vy = y + ls.y; // 没地方了：数值右对齐放到下一行
                lineH = ls.y + vs.y;
            }
            float vx = Mathf.Max(left + barW, c.W - c.Pad - vs.x);
            Ui.Move(value.rectTransform, vx, vy);
            if (vbar != null) DrawBar(c, vbar, vx - barW, vy, BarWidth(c), vs.y);

            if (vy == y)
            {
                float from = left + ls.x + c.Gap, to = vx - barW - c.Gap;
                Leader(c, from, to, y + Mathf.Round((ls.y - c.U) / 2f));
            }
            return y + lineH;
        }

        /// 标签中线上的点线引导，右对齐，点正好停在数值前面。
        private static void Leader(Ctx c, float from, float to, float midY)
        {
            float step = 3f * c.U;
            int n = Mathf.FloorToInt((to - from + 2f * c.U) / step);
            if (n < 2) return;
            float x = to - n * step + 2f * c.U;
            for (int i = 0; i < n; i++) Solid(c, PaperEdge, x + i * step, midY, c.U, c.U);
        }

        private static float BarWidth(Ctx c)
        {
            return Mathf.Round(c.M.Line * 3f);
        }

        /// 游戏画的进度条，用它的颜色转成墨。
        private static void DrawBar(Ctx c, BarMark bar, float x, float yTop, float w, float lineH)
        {
            Meter(c, bar.Ratio, BarInk(bar), x, yTop, w, lineH);
        }

        /// 一格物品格，最多 Settings.MaxContentRows 行，多的折成“+n”一格。返回底部位置。
        private static float Grid(Ctx c, List<ContentEntry> entries, float left, float y)
        {
            float cell = Mathf.Round(c.M.Line * 1.5f), gap = c.U * 2f;
            int cols = Math.Max(1, Mathf.FloorToInt((c.W - c.Pad - left + gap) / (cell + gap)));
            int capacity = cols * Settings.MaxContentRows;
            int shown = entries.Count <= capacity ? entries.Count : capacity - 1;
            int more = 0;
            for (int i = shown; i < entries.Count; i++) more += entries[i].Count;
            int cells = shown + (more > 0 ? 1 : 0);
            float small = c.F * 0.65f;
            // 堆叠数在格子右下角，每个网格合成一段文字（再加一段纸色的阴影），而不是每格两段：满格时那要 28 段文字。每一行格子是一行文字，间距一格，每个数字用 <pos> 摆位置。
            float lineH = Mathf.Ceil(c.Size("0", small).y);
            float countsLeft = Mathf.Round(left), countsTop = Mathf.Round(y + cell - lineH);
            StringBuilder counts = new StringBuilder();
            counts.Append("<line-height=").Append(Num(cell + gap)).Append('>');
            int countRow = 0;
            bool anyCount = false;

            // 只有一件物品就只画图标：只围一个框会看着像按钮。
            bool bare = cells == 1;
            PrepareIcons(entries, shown);
            for (int i = 0; i < cells; i++)
            {
                float x = left + (i % cols) * (cell + gap);
                float cy = y + (i / cols) * (cell + gap);
                if (!bare)
                {
                    Solid(c, PaperEdge, x, cy, cell, cell);
                    Solid(c, PaperShade, x + c.U, cy + c.U, cell - 2f * c.U, cell - 2f * c.U);
                }
                if (i >= shown)
                {
                    TextMeshProUGUI m = Text(c, small, "+" + more, Ink, 0f);
                    Vector2 s = m.rectTransform.sizeDelta;
                    Ui.Move(m.rectTransform, x + Mathf.Round((cell - s.x) / 2f), cy + Mathf.Round((cell - s.y) / 2f));
                    continue;
                }
                Sprite icon = Icons.IconOf(entries[i].Item);
                if (icon != null) PlaceIcon(c, icon, x, cy, cell, bare ? 0f : 2f * c.U);
                if (entries[i].Count > 1)
                {
                    string n = entries[i].Count.ToString(CultureInfo.InvariantCulture);
                    float bx = Mathf.Round(x + cell - Mathf.Ceil(c.Size(n, small).x) - c.U);
                    for (; countRow < i / cols; countRow++) counts.Append('\n');
                    counts.Append("<pos=").Append(Num(bx - countsLeft)).Append('>').Append(n);
                    anyCount = true;
                }
            }
            if (anyCount)
            {
                // 先画阴影，数字盖在上面；两段都在图标之上，图标这时都摆好了。
                string rich = counts.ToString();
                TextMeshProUGUI sh = Text(c, small, rich, Paper, 0f);
                TextMeshProUGUI tx = Text(c, small, rich, Ink, 0f);
                Ui.Move(sh.rectTransform, countsLeft + c.U, countsTop + c.U);
                Ui.Move(tx.rectTransform, countsLeft, countsTop);
            }
            int rows = (cells + cols - 1) / cols;
            return y + rows * cell + (rows - 1) * gap;
        }

        /// 物品描述。Item Details 会按游戏窗口的宽度断行（“……金属锭。金”/“属锭的质量……”）；估价单宽度不同，那些断点会留下参差的边，所以原文里连着的行重新接起来，按单子宽度重排。真正的换行保留。
        private static float Description(Ctx c, Parts p, GameItemElement item, float y)
        {
            if (p.Desc.Count == 0) return y;
            string source = DescriptionText(item);
            y += c.Gap * 2f;
            for (int i = 0; i < p.Desc.Count; i++)
            {
                StringBuilder sb = new StringBuilder(InkRich(Trimmed(p.Desc[i].Segs), InkSoft));
                string last = p.Desc[i].Plain.Trim();
                while (i + 1 < p.Desc.Count)
                {
                    string next = p.Desc[i + 1].Plain.Trim();
                    string joint = Joint(source, last, next);
                    if (joint == null) break;
                    sb.Append(joint).Append(InkRich(Trimmed(p.Desc[i + 1].Segs), InkSoft));
                    last = next;
                    i++;
                }
                TextMeshProUGUI t = Text(c, c.F * 0.8f, "<i>" + sb + "</i>", Color.white, c.Inner);
                Ui.Move(t.rectTransform, c.Pad, y);
                y += t.rectTransform.sizeDelta.y;
            }
            return y;
        }

        private static string DescriptionText(GameItemElement item)
        {
            try { return item == null ? "" : TextUnits.Plain((item.shortDescription ?? "") + "\n" + (item.flavorText ?? "")); }
            catch { return ""; }
        }

        /// source 里本来连着两行的两个片段之间用什么接：空的（中文）、一个空格，或者接不上时 null。
        private static string Joint(string source, string a, string b)
        {
            if (source.Length == 0 || a.Length == 0 || b.Length == 0) return null;
            if (source.IndexOf(a + b, StringComparison.Ordinal) >= 0) return "";
            if (source.IndexOf(a + " " + b, StringComparison.Ordinal) >= 0) return " ";
            // Item Details 只在断点处去掉 ASCII 空格，所以那里可能留着全角空格。
            if (source.IndexOf(a + "　" + b, StringComparison.Ordinal) >= 0) return "　";
            return null;
        }

        /// 去掉行首行尾空格的 segs（Item Details 会给自己的行加缩进）。
        private static List<Seg> Trimmed(List<Seg> segs)
        {
            if (segs == null || segs.Count == 0) return segs;
            List<Seg> r = new List<Seg>(segs);
            for (int i = 0; i < r.Count; i++)
            {
                if (r[i].Bar != null || string.IsNullOrEmpty(r[i].Text)) continue;
                bool visible;
                r[i] = r[i].With(TrimLead(r[i].Text, out visible));
                if (visible) break;
            }
            for (int i = r.Count - 1; i >= 0; i--)
            {
                if (r[i].Bar != null || string.IsNullOrEmpty(r[i].Text)) continue;
                r[i] = r[i].With(r[i].Text.TrimEnd());
                if (TextUnits.Plain(r[i].Text).Length > 0) break;
            }
            return r;
        }

        /// 去掉前导空格，包括前导标签后面的空格（“<color=…>  此机器”）；同时告诉后面还有没有文字。
        private static string TrimLead(string s, out bool visible)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                int close = ch == '<' ? s.IndexOf('>', i) : -1;
                if (close > i)
                {
                    sb.Append(s, i, close - i + 1);
                    i = close + 1;
                }
                else if (char.IsWhiteSpace(ch)) i++;
                else break;
            }
            visible = i < s.Length;
            sb.Append(s, i, s.Length - i);
            return sb.ToString();
        }

        /// 物品来自哪个模组，游戏自己的选项要求时才显示。游戏是在估价单取材的那段文字之后加这一行的，不补就漏掉了。
        private static float ModSource(Ctx c, GameItemElement item, float y)
        {
            if (!Settings.On(Settings.ShowModInfo)) return y;
            string name = null;
            try { if (EmporiumOption.ShowModSourceInTooltip) name = ModSourceHelper.GetItemModName(item); }
            catch { }
            name = string.IsNullOrEmpty(name) ? "" : TextUnits.Plain(name).Trim();
            if (name.Length == 0) return y;
            y += c.Gap;
            TextMeshProUGUI t = Text(c, c.F * 0.75f, name, InkSoft, c.Inner);
            Ui.Move(t.rectTransform, c.Pad, y);
            return y + t.rectTransform.sizeDelta.y;
        }

        /// 用物品身份生成的条形码，加上单号。
        private static float Footer(Ctx c, GameItemElement item, float y)
        {
            y += c.Gap * 1.5f;
            float h = Mathf.Round(c.M.Line * 0.55f);
            TextMeshProUGUI no = Text(c, c.F * 0.65f, TicketNo(item), InkSoft, 0f);
            // 条形码是装饰，不该是整张单子最黑的一块。
            Color bars = Color.Lerp(Paper, Ink, 0.7f);
            Vector2 s = no.rectTransform.sizeDelta;
            float x = c.Pad, end = c.Pad + Mathf.Min(c.Inner * 0.55f, 48f * c.U, c.Inner - s.x - 2f * c.Gap);
            int seed = 0;
            try { seed = (item.identifier ?? "").GetHashCode() ^ item.uniqueId; } catch { }
            System.Random rng = new System.Random(seed);
            while (x < end)
            {
                float w = rng.Next(1, 3) * c.U;
                Solid(c, bars, x, y, w, h);
                x += w + rng.Next(1, 3) * c.U;
            }
            Ui.Move(no.rectTransform, c.W - c.Pad - s.x, y + h - s.y);
            return y + h + c.Pad;
        }

        private static string TicketNumber(GameItemElement item)
        {
            int n = 0;
            try { n = item != null ? item.uniqueId : 0; } catch { }
            return (Math.Abs(n) % 10000).ToString("D4");
        }

        private static string TicketNo(GameItemElement item)
        {
            string fmt = SectionedTooltipsMod.L("ticket_no");
            try { return string.Format(fmt, TicketNumber(item)); }
            catch { return "No." + TicketNumber(item); }
        }
    }
}
