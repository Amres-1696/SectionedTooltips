using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 估价单的各节排在一张网格上：左列是标签，宽度取单子上最长的标签；右边是数值列，所有数值、进度条、升级、模组标签和量条都从同一个 x 开始。全局只有一种进度条：比纸深一档的平底槽，按比例填充。
    internal static partial class Ticket
    {
        // ---------------------------------------------------------------- 网格

        private static float RowSize(Ctx c) { return c.F * 0.9f; }
        private static float NoteSize(Ctx c) { return c.F * 0.75f; }
        private static float LineH(Ctx c) { return Mathf.Ceil(c.Size(" ", RowSize(c)).y); }
        private static float RowGap(Ctx c) { return c.U; }
        private static float LabelX(Ctx c) { return c.Pad + Indent(c); }
        private static float ValueX(Ctx c) { return c.LabelCol > 0f ? LabelX(c) + c.LabelCol + 2f * c.Gap : LabelX(c); }

        /// 标签超过这么多字符就在列内折行，不再加宽列。
        private const float LabelChars = 8f;

        private static float LabelCap(Ctx c) { return RowSize(c) * LabelChars; }

        /// 一对的标签放进标签列。太长放不下的把末尾括号让出去（“去除的污染物（昨夜）”）：括号里的内容挪到数值后面（note），标签不折行。strip 为真时（见 StripsBrackets）所有标签都这么处理，不管长短。
        private static string PairLabel(Ctx c, Row row, bool strip, out string note)
        {
            note = "";
            string rich = InkRich(row.Label, InkSoft, false);
            if (!strip && c.Measure(rich, RowSize(c)) <= LabelCap(c)) return rich;
            string plain = PlainOf(row.Label).Trim();
            int open = TrailingGroup(plain);
            if (open <= 0) return rich;
            note = plain.Substring(open + 1, plain.Length - open - 2).Trim();
            return Tint(plain.Substring(0, open).Trim(), InkSoft);
        }

        /// 这一节里有标签太长放不进列、要让出括号吗。有的话这一节所有标签都让，这样种子的基因读起来一致：“固有”都跟在数值后面，而不是有的在里面有的不是。
        private static bool StripsBrackets(Ctx c, Block bl)
        {
            for (int i = 0; i < bl.Rows.Count; i++)
            {
                Row row = bl.Rows[i];
                if (!row.IsPair || UpgradeOf(row) != null || EffectOf(row) != null) continue;
                if (c.Measure(InkRich(row.Label, InkSoft, false), RowSize(c)) > LabelCap(c) && TrailingGroup(PlainOf(row.Label).Trim()) > 0) return true;
            }
            return false;
        }

        private static string Tint(string text, Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        }

        private static string Small(string text, Color color)
        {
            return "<size=85%>" + Tint(text, color) + "</size>";
        }

        /// 标签列里的一个标签，按列宽折行。返回高度。
        private static float GridLabel(Ctx c, string rich, float y)
        {
            if (string.IsNullOrEmpty(rich)) return 0f;
            float size = RowSize(c);
            TextMeshProUGUI t = Text(c, size, rich, Color.white, c.Measure(rich, size) > c.LabelCol ? Mathf.Max(c.LabelCol, size) : 0f);
            Ui.Move(t.rectTransform, LabelX(c), y);
            return t.rectTransform.sizeDelta.y;
        }

        /// 从 x 开始的文字，在单子右边缘折行。返回高度。
        private static float GridText(Ctx c, string rich, float size, float x, float y)
        {
            return GridText(c, rich, size, x, y, c.W - c.Pad);
        }

        /// 从 x 开始的文字，在 right 处折行。返回高度。
        private static float GridText(Ctx c, string rich, float size, float x, float y, float right)
        {
            if (string.IsNullOrEmpty(rich)) return 0f;
            float room = right - x;
            // 让一个像素：单子宽度取整了，正好量到刚好放得下的文字会把最后一个字挤到下一行。
            TextMeshProUGUI t = Text(c, size, rich, Color.white, c.Measure(rich, size) > room + 1f ? room : 0f);
            Ui.Move(t.rectTransform, x, y);
            return t.rectTransform.sizeDelta.y;
        }

        /// 数值：用墨色；箭头替换成什么用淡色调（“8 →” 6/天）；末尾括号里的内容用小字淡色跟在后面。多种颜色的数值保留原色，括号也一样。
        private static string ValueRich(List<Seg> segs)
        {
            Palette color = Palette.None;
            bool one = true, any = false;
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (Drawn(s) || TextUnits.Plain(s.Text).Trim().Length == 0) continue;
                if (any && s.Color != color) one = false;
                color = s.Color;
                any = true;
            }
            if (!one) return ValueInks(segs);
            string plain = PlainOf(segs).Trim(), main = plain, note = "";
            int open = TrailingGroup(plain);
            if (open > 0)
            {
                main = plain.Substring(0, open).Trim();
                note = plain.Substring(open + 1, plain.Length - open - 2).Trim();
            }
            Color ink = ValueInk(color);
            StringBuilder sb = new StringBuilder();
            int arrow = main.IndexOf('→');
            if (arrow >= 0) sb.Append(Tint(main.Substring(0, arrow + 1).Trim(), InkSoft)).Append(' ').Append(Tint(main.Substring(arrow + 1).Trim(), ink));
            else sb.Append(Tint(main, ink));
            if (note.Length > 0) sb.Append("  ").Append(Small(note, InkSoft));
            return sb.ToString();
        }

        /// 数值的墨色。游戏几乎给每个数值都上了色，颜色反而没信息量：数值统一用墨色，只留红、橙、黄（少、低、快坏了）让它们显出来。
        private static Color ValueInk(Palette p)
        {
            if (p == Palette.None) return Ink;
            float h, s, v;
            Color.RGBToHSV(Overlay.ToColor(p, Palette.Gray), out h, out s, out v);
            return s >= 0.25f && (h < 0.17f || h > 0.94f) ? InkOf(p, Ink) : Ink;
        }

        /// 由多种颜色组成的数值，各部分按自己的 ValueInk。
        private static string ValueInks(List<Seg> segs)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (Drawn(s) || string.IsNullOrEmpty(s.Text)) continue;
                string text = s.Bold ? "<b>" + s.Text + "</b>" : s.Text;
                sb.Append(Tint(text, ValueInk(s.Color)));
            }
            return sb.ToString().Trim();
        }

        /// s 末尾那组括号从哪里开始（“效率（电子元件（普通））” 得 2）；不是以括号结尾时返回 -1。
        private static int TrailingGroup(string s)
        {
            if (s.Length < 2) return -1;
            char last = s[s.Length - 1];
            if (last != '）' && last != ')') return -1;
            int depth = 0;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                char ch = s[i];
                if (ch == '）' || ch == ')') depth++;
                else if (ch == '（' || ch == '(')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        // ---------------------------------------------------------------- 进度条

        private static float BarW(Ctx c) { return Mathf.Round(c.M.Line * 3.5f); }
        private static float BarH(Ctx c, float lineH) { return Mathf.Max(4f * c.U, Mathf.Round(lineH * 0.4f)); }

        /// 进度条或没点亮的格子的空白部分：比纸深一档。
        private static Color Track()
        {
            return Color.Lerp(Paper, InkSoft, darkPaper ? 0.35f : 0.22f);
        }

        /// 一条平底槽，填到 ratio，在 lineH 高的行里垂直居中。
        private static void Meter(Ctx c, float ratio, Color color, float x, float yTop, float w, float lineH)
        {
            float h = BarH(c, lineH);
            float y = yTop + Mathf.Round((lineH - h) / 2f);
            Solid(c, Track(), x, y, w, h);
            float fill = Mathf.Round(w * Mathf.Clamp01(ratio));
            if (fill >= 1f) Solid(c, color, x, y, fill, h);
        }

        /// 这一行的进度条：游戏的（BarOf），或者 Item Details 用文字画的“█████”（自身颜色，空白部分是 40 透明度）。后者还原成进度条并从 segs 里去掉；当文字写出来会被染成数值的墨色，淡的那一半也不再像进度条。
        private static BarMark SplitBar(ref List<Seg> segs)
        {
            BarMark game = BarOf(segs);
            if (game != null || segs == null || !Settings.On(Settings.Bars)) return game;
            int full = 0, empty = 0;
            Palette color = Palette.None;
            bool faint = false;
            List<Seg> rest = new List<Seg>();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                string t = s.Text ?? "";
                if (t.IndexOf(Block) < 0) { rest.Add(s); continue; }
                StringBuilder kept = new StringBuilder();
                for (int k = 0; k < t.Length; k++)
                {
                    char ch = t[k];
                    if (ch == '<')
                    {
                        int end = t.IndexOf('>', k);
                        if (end > k && t.Substring(k + 1, end - k - 1).StartsWith("alpha=#", StringComparison.OrdinalIgnoreCase))
                        {
                            faint = !t.Substring(k + 8, end - k - 8).Equals("FF", StringComparison.OrdinalIgnoreCase);
                            k = end;
                            continue;
                        }
                    }
                    if (ch == Block)
                    {
                        if (faint) empty++;
                        else full++;
                        if (color == Palette.None) color = s.Color;
                        continue;
                    }
                    kept.Append(ch);
                }
                if (kept.Length > 0) rest.Add(s.With(kept.ToString()));
            }
            if (full + empty == 0) return null;
            segs = rest;
            BarMark bar = new BarMark();
            bar.Ratio = (float)full / (full + empty);
            bar.Color = color;
            return bar;
        }

        private const char Block = '█';

        // ---------------------------------------------------------------- 分节

        /// 把状态行（“状态：生长中”和它下面的进度条）挪到最前面的块：机器在做什么，是最先要看的。是副本，块本身属于缓存的排版。
        private static Block StatusFirst(Block bl)
        {
            string word = SectionedTooltipsMod.L("row_status");
            for (int i = 1; i < bl.Rows.Count; i++)
            {
                Row row = bl.Rows[i];
                if (!row.IsPair || PlainOf(row.Label).Trim().TrimEnd(':', '：') != word) continue;
                int n = Reading(bl, i) != null ? 2 : 1;
                Block moved = new Block();
                moved.Key = bl.Key;
                moved.Rows.AddRange(bl.Rows.GetRange(i, n));
                for (int k = 0; k < bl.Rows.Count; k++) if (k < i || k >= i + n) moved.Rows.Add(bl.Rows[k]);
                return moved;
            }
            return bl;
        }

        private static float Section(Ctx c, Block bl, float y)
        {
            bl = StatusFirst(bl);
            y = SectionHeader(c, Sections.TitleOf(bl.Key), InkOf(Sections.StyleOf(bl.Key).Accent, Ink), y);
            float left = LabelX(c);
            bool module = bl.Key == Sections.Module, stats = false, affixes = false;
            bool labelled = Labelled(bl), strip = StripsBrackets(c, bl);
            // 小标题（“升级：”、“模组效果：”）给它下面没有自己标签的行当标签。
            string pending = null;
            for (int i = 0; i < bl.Rows.Count; i++)
            {
                Row row = bl.Rows[i];
                // 模组的词条和属性分开画（游戏给两者上的蓝几乎一样）：第一个词条前画虚线，每个名称前留点空，描述字号小一档。
                if (module && Affix(row))
                {
                    bool name = LeadSeg(row).Bold;
                    if (!affixes && stats)
                    {
                        y += c.Gap;
                        Dashes(c, left, y, c.W - c.Pad - left, 3f * c.U, 2f * c.U, c.U, PaperEdge);
                        y += c.U + c.Gap;
                    }
                    else if (affixes && name) y += c.Gap * 0.5f;
                    affixes = true;
                    float size = name ? RowSize(c) : c.F * 0.8f;
                    y += GridText(c, InkRich(row.Segs, name ? Ink : InkSoft, name), size, left, y) + RowGap(c);
                    continue;
                }
                if (row.Heading)
                {
                    if (pending != null) y = GridLine(c, pending, "", y);
                    pending = InkRich(row.Label, InkSoft, false);
                    continue;
                }
                stats = true;
                Upgrade u = UpgradeOf(row);
                if (u != null)
                {
                    // 名字放在标签列，升级项顺着这一列读（“储藏 ▬▬▬▬ 4/4 废金属”）；上面的小标题单独一行。
                    if (!string.IsNullOrEmpty(pending)) y = GridLine(c, pending, "", y);
                    pending = null;
                    y = UpgradeRow(c, u, Tint(u.Name, InkSoft), y);
                    continue;
                }
                // 只在小标题下面（“模组效果：”）：孤零零一个“+20%”只是个数字。
                if (pending != null && EffectOf(row) != null)
                {
                    y = Effects(c, bl, ref i, pending, y);
                    pending = "";
                    continue;
                }
                if (row.IsPair)
                {
                    // 有自己标签的行上面的小标题单独占一行。
                    if (!string.IsNullOrEmpty(pending)) y = GridLine(c, pending, "", y);
                    pending = null;
                    Row reading = Reading(bl, i);
                    y = GridPair(c, row, reading, strip, y);
                    if (reading != null) i++;
                    continue;
                }
                List<Row> parts = PipeParts(row);
                if (parts != null)
                {
                    for (int k = 0; k < parts.Count; k++) y = GridPair(c, parts[k], null, false, y);
                    continue;
                }
                bool negative;
                if (bl.Key == Sections.Weapon && AffixSign(row, out negative))
                {
                    // 枪械词条：正号垫浅绿、负号垫浅红，一眼分得出哪条是好的。
                    y = WeaponAffix(c, row, negative, labelled ? ValueX(c) : left, y);
                    if (pending != null) pending = "";
                    continue;
                }
                y = GridRow(c, row, pending ?? "", labelled ? ValueX(c) : left, y);
                if (pending != null) pending = "";
            }
            if (!string.IsNullOrEmpty(pending)) y = GridLine(c, pending, "", y);
            return y;
        }

        /// 这一节里有没有标签；纯文字的节占满整宽。
        private static bool Labelled(Block bl)
        {
            for (int i = 0; i < bl.Rows.Count; i++) if (bl.Rows[i].IsPair || bl.Rows[i].Heading) return true;
            return false;
        }

        /// 已经拼好的一行标签和数值。返回底部。
        private static float GridLine(Ctx c, string label, string value, float y)
        {
            float h = Mathf.Max(LineH(c), GridLabel(c, label, y));
            h = Mathf.Max(h, GridText(c, value, RowSize(c), ValueX(c), y));
            return y + h + RowGap(c);
        }

        /// “标签  数值”，有进度条就从数值列开头画。reading（下面那条只有读数的行）把它的进度条和读数放到这一行，在数值前面。
        private static float GridPair(Ctx c, Row row, Row reading, bool strip, float y)
        {
            float lh = LineH(c);
            string note;
            string label = PairLabel(c, row, strip, out note);
            Color hue;
            float lw = c.Measure(label, RowSize(c));
            if (TraitHue(row, out hue) && lw <= c.LabelCol + 1f)
            {
                // 游戏用绿或红上色的标签（种子的好基因和坏基因）垫一层同色底，和模组效果一样：哪些基因好哪些坏，顺着标签列就能看出来。
                Color wash, ink;
                Wash(hue, out wash, out ink);
                float px = 3f * c.U;
                Solid(c, wash, LabelX(c) - px, y, lw + 2f * px, lh);
                label = Tint(TextUnits.Plain(label), ink);
            }
            float h = Mathf.Max(lh, GridLabel(c, label, y));
            float x = ValueX(c);
            List<Seg> value = row.Value;
            BarMark bar = reading != null ? BarOf(reading.Segs) : SplitBar(ref value);
            if (bar != null)
            {
                Meter(c, bar.Ratio, InkOf(bar.Color, Ink), x, y, BarW(c), lh);
                x += BarW(c) + c.Gap;
            }
            float right = c.W - c.Pad;
            if (note.Length > 0)
            {
                // 标签括号里的内容（“固有”）画在单子右边缘：每行都在同一个位置，顺着单子读下来像一列，而不是跟着长短不一的数值乱跑。
                TextMeshProUGUI t = Text(c, RowSize(c), Small(note, InkSoft), Color.white, 0f);
                float tw = t.rectTransform.sizeDelta.x;
                Ui.Move(t.rectTransform, right - tw, y);
                right -= tw + 2f * c.Gap;
            }
            h = Mathf.Max(h, GridText(c, PairValue(value, reading), RowSize(c), x, y, right));
            return y + h + RowGap(c);
        }

        private static readonly Color32 GoodHue = new Color32(76, 140, 52, 255);
        private static readonly Color32 BadHue = new Color32(178, 64, 52, 255);

        /// 游戏是否给这一行的标签上绿（好）或红（坏）色，并给出底色用的色相。
        private static bool TraitHue(Row row, out Color hue)
        {
            hue = Color.clear;
            Seg lead = null;
            for (int i = 0; row.Label != null && i < row.Label.Count && lead == null; i++)
                if (TextUnits.Plain(row.Label[i].Text).Trim().Length > 0) lead = row.Label[i];
            if (lead == null || lead.Color == Palette.None) return false;
            float h, s, v;
            Color.RGBToHSV(Overlay.ToColor(lead.Color, Palette.Gray), out h, out s, out v);
            if (s < 0.25f) return false;
            if (h > 0.17f && h < 0.45f) hue = GoodHue;
            else if (h < 0.05f || h > 0.94f) hue = BadHue;
            else return false;
            return true;
        }

        private static string PairValue(List<Seg> value, Row reading)
        {
            string text = ValueRich(value);
            if (reading != null) text = Tint(PlainOf(reading.Segs).Trim(), Ink) + "  " + text;
            return text;
        }

        private static float GridPairWidth(Ctx c, Block bl, int i, bool strip)
        {
            Row row = bl.Rows[i], reading = Reading(bl, i);
            string note;
            PairLabel(c, row, strip, out note);
            List<Seg> value = row.Value;
            BarMark bar = reading != null ? BarOf(reading.Segs) : SplitBar(ref value);
            float w = (bar != null ? BarW(c) + c.Gap : 0f) + c.Measure(PairValue(value, reading), RowSize(c));
            if (note.Length > 0) w += 2f * c.Gap + c.Measure(Small(note, InkSoft), RowSize(c));
            return w;
        }

        /// 第 i 对下面那行只是进度条加读数时（“状态：生长中”下面是“▬ 10/28”），返回它：两件东西是一件事，合成一行。否则返回 null。
        private static Row Reading(Block bl, int i)
        {
            Row row = bl.Rows[i];
            if (!row.IsPair || BarOf(row.Value) != null || i + 1 >= bl.Rows.Count) return null;
            Row next = bl.Rows[i + 1];
            if (next.IsPair || next.Heading || BarOf(next.Segs) == null) return null;
            string t = PlainOf(next.Segs).Trim();
            if (t.Length == 0) return null;
            for (int k = 0; k < t.Length; k++) if (!char.IsDigit(t[k]) && " ./%".IndexOf(t[k]) < 0) return null;
            return next;
        }

        /// 没有自己标签的行：从 x 开始的文字，或进度条加读数。
        private static float GridRow(Ctx c, Row row, string label, float x, float y)
        {
            float lh = LineH(c);
            float h = Mathf.Max(lh, GridLabel(c, label, y));
            List<Seg> segs = row.Segs;
            BarMark bar = SplitBar(ref segs);
            if (bar != null)
            {
                Meter(c, bar.Ratio, InkOf(bar.Color, Ink), x, y, BarW(c), lh);
                x += BarW(c) + c.Gap;
            }
            h = Mathf.Max(h, GridText(c, ValueInks(segs), RowSize(c), x, y));
            return y + h + RowGap(c);
        }

        private static float GridRowWidth(Ctx c, Row row)
        {
            List<Seg> segs = row.Segs;
            BarMark bar = SplitBar(ref segs);
            return (bar != null ? BarW(c) + c.Gap : 0f) + c.Measure(ValueInks(segs), RowSize(c));
        }

        /// <summary>武器一节里的词条行（“+12 后坐力控制”“-6 人机工效”）：正号返回 false，负号返回 true，不是词条返回 false。</summary>
        private static bool AffixSign(Row row, out bool negative)
        {
            negative = false;
            if (row.IsPair || row.Heading || row.Segs == null) return false;
            string s = PlainOf(row.Segs).Trim();
            if (s.Length < 2) return false;
            if (s[0] == '+') return s[1] == '.' || char.IsDigit(s[1]);
            if (s[0] != '-' && s[0] != '−') return false;
            negative = true;
            return s[1] == '.' || char.IsDigit(s[1]);
        }

        /// <summary>一条武器词条，垫一层浅绿（正）或浅红（负）的底，文字沿用数值的墨色。返回底部。</summary>
        private static float WeaponAffix(Ctx c, Row row, bool negative, float x, float y)
        {
            float lh = LineH(c), size = RowSize(c);
            List<Seg> segs = row.Segs;
            BarMark bar = SplitBar(ref segs);
            if (bar != null)
            {
                Meter(c, bar.Ratio, InkOf(bar.Color, Ink), x, y, BarW(c), lh);
                x += BarW(c) + c.Gap;
            }
            float px = 3f * c.U;
            string rich = ValueInks(segs);
            float w = Mathf.Min(c.Measure(rich, size), c.W - c.Pad - x - 2f * px);
            Color wash, ink;
            Wash(negative ? BadHue : GoodHue, out wash, out ink);
            Solid(c, wash, x - px, y, w + 2f * px, lh);
            float h = Mathf.Max(lh, GridText(c, rich, size, x, y));
            return y + h + RowGap(c);
        }

        /// “精工扩舱 0/12 | 养殖区 12x8 | 剩余升级次数：12”拆成标签/数值行；拆不开返回 null。
        private static List<Row> PipeParts(Row row)
        {
            string plain = PlainOf(row.Segs).Trim();
            if (plain.IndexOf('|') < 0 || BarOf(row.Segs) != null) return null;
            string[] bits = plain.Split('|');
            if (bits.Length < 2) return null;
            List<Row> list = new List<Row>();
            for (int i = 0; i < bits.Length; i++)
            {
                string bit = bits[i].Trim();
                if (bit.Length == 0) continue;
                Row r = PairFromPlain(bit);
                if (r == null)
                {
                    int space = bit.LastIndexOf(' ');
                    if (space <= 0) return null;
                    r = PairFromPlain(bit.Substring(0, space) + "：" + bit.Substring(space + 1));
                }
                if (r == null) return null;
                list.Add(r);
            }
            return list.Count >= 2 ? list : null;
        }

        // ---------------------------------------------------------------- 升级

        /// 游戏的升级行之一，“储藏（废金属）：[bar] 0/7”：名称、要什么材料、等级。
        private sealed class Upgrade
        {
            public string Name = "";
            public string Need = "";
            public int Level;
            public int Max;
        }

        /// 等级高于这个数就画进度条，不画格子。
        private const int MaxPips = 12;

        private static Upgrade UpgradeOf(Row row)
        {
            if (!row.IsPair || BarOf(row.Value) == null) return null;
            string v = PlainOf(row.Value).Trim();
            int slash = v.IndexOf('/');
            int level, max;
            if (slash <= 0
                || !int.TryParse(v.Substring(0, slash).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out level)
                || !int.TryParse(v.Substring(slash + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out max)
                || max <= 0 || level < 0 || level > max) return null;
            string label = PlainOf(row.Label).Trim();
            int open = TrailingGroup(label);
            Upgrade u = new Upgrade();
            u.Level = level;
            u.Max = max;
            if (open > 0)
            {
                u.Name = label.Substring(0, open).Trim();
                u.Need = label.Substring(open + 1, label.Length - open - 2).Trim();
            }
            // 没有材料说明时，只有数量小到能用格子表示（“储物标签页 1/5”）才算：“10/28”天的生长是进度条。
            else if (max <= MaxPips) u.Name = label;
            return u.Name.Length > 0 ? u : null;
        }

        private static string LevelText(Upgrade u)
        {
            return u.Level.ToString(CultureInfo.InvariantCulture) + "/" + u.Max.ToString(CultureInfo.InvariantCulture);
        }

        /// 升级行在等级后面写的东西：要什么材料（“废金属”）。名称是这一行的标签。
        private static string UpgradeNote(Upgrade u)
        {
            return u.Need;
        }

        /// 格子占一条进度条的宽度，这样后面的内容和进度条后面的内容对齐。
        private static float PipsWidth(Ctx c, Upgrade u)
        {
            return BarW(c);
        }

        /// “储藏  ▬ ▬ ▬ ▬ 4/4  废金属”：进度条按等级切成几块，然后等级，然后材料。
        private static float UpgradeRow(Ctx c, Upgrade u, string label, float y)
        {
            float lh = LineH(c);
            float h = Mathf.Max(lh, GridLabel(c, label, y));
            float x = ValueX(c), w = BarW(c), gap = c.U;
            float piece = Mathf.Floor((w - (u.Max - 1) * gap) / u.Max);
            if (u.Max > MaxPips || piece < 2f * c.U) Meter(c, (float)u.Level / u.Max, Ink, x, y, w, lh);
            else
            {
                float ph = BarH(c, lh), py = y + Mathf.Round((lh - ph) / 2f);
                // 格子铺满整宽；取整剩下的给间距。
                float step = u.Max > 1 ? (w - piece) / (u.Max - 1) : 0f;
                for (int i = 0; i < u.Max; i++) Solid(c, i < u.Level ? Ink : Track(), Mathf.Round(x + i * step), py, piece, ph);
            }
            x += PipsWidth(c, u) + c.Gap;
            string text = Tint(LevelText(u), Ink);
            string note = UpgradeNote(u);
            if (note.Length > 0) text += "  " + Small(note, InkSoft);
            h = Mathf.Max(h, GridText(c, text, RowSize(c), x, y));
            return y + h + RowGap(c);
        }

        private static float UpgradeWidth(Ctx c, Upgrade u)
        {
            return PipsWidth(c, u) + c.Gap + c.Measure(LevelText(u), RowSize(c)) + 2f * c.Gap + c.Measure(UpgradeNote(u), NoteSize(c));
        }

        // ---------------------------------------------------------------- 模组效果

        /// 一条模组效果行，“-质量: +6% (系数 0.5) (+94%后可产出纯水)”：彩色标签和后面的内容。
        private sealed class Effect
        {
            public string Name = "";
            public string Percent = "";
            public bool Negative;
            /// 它做什么（“-2能量/天”）。
            public string Main = "";
            /// 补充说明。
            public readonly List<string> Notes = new List<string>();
        }

        private static Effect EffectOf(Row row)
        {
            if (!row.IsPair || BarOf(row.Value) != null) return null;
            string v = PlainOf(row.Value).Trim();
            if (v.Length < 3 || (v[0] != '+' && v[0] != '-' && v[0] != '−')) return null;
            int i = 1;
            while (i < v.Length && (char.IsDigit(v[i]) || v[i] == '.')) i++;
            if (i == 1 || i >= v.Length || v[i] != '%') return null;
            Effect e = new Effect();
            e.Name = PlainOf(row.Label).Trim().TrimStart('-', '−').Trim();
            if (e.Name.Length == 0) return null;
            e.Percent = v.Substring(0, i + 1);
            e.Negative = v[0] != '+';
            string rest = v.Substring(i + 1).Trim();
            List<string> groups = Groups(rest);
            if (groups == null) e.Main = rest;
            else
                for (int k = 0; k < groups.Count; k++)
                {
                    if (k == 0) e.Main = groups[k];
                    else e.Notes.Add(groups[k]);
                }
            return e;
        }

        /// “(a) (b)” 拆成 a、b；括号外面还有别的东西时返回 null。
        private static List<string> Groups(string s)
        {
            List<string> list = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                if (s[i] != '(' && s[i] != '（') return null;
                int depth = 0, start = i;
                for (; i < s.Length; i++)
                {
                    if (s[i] == '(' || s[i] == '（') depth++;
                    else if ((s[i] == ')' || s[i] == '）') && --depth == 0) break;
                }
                if (i >= s.Length) return null;
                list.Add(s.Substring(start + 1, i - start - 1).Trim());
                i++;
            }
            return list;
        }

        // 游戏给模组效果用的颜色，按游戏各语言里的效果名判断。
        private static readonly Color32 EfficiencyHue = new Color32(76, 140, 52, 255);
        private static readonly Color32 PerformanceHue = new Color32(128, 88, 176, 255);
        private static readonly Color32 QualityHue = new Color32(48, 112, 184, 255);

        private static Color EffectHue(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("效率") || n.Contains("effic")) return EfficiencyHue;
            if (n.Contains("性能") || n.Contains("perform")) return PerformanceHue;
            if (n.Contains("质量") || n.Contains("quality")) return QualityHue;
            return InkSoft;
        }

        /// 彩色标签在当前纸上的底色和文字色，和分类标签一样。
        private static void Wash(Color hue, out Color wash, out Color text)
        {
            if (darkPaper)
            {
                wash = Color.Lerp(Paper, hue, 0.45f);
                text = Color.Lerp(hue, Color.white, 0.6f);
            }
            else
            {
                wash = Color.Lerp(Paper, hue, 0.22f);
                text = hue * 0.75f;
            }
            text.a = 1f;
        }

        private static float ChipFont(Ctx c) { return c.F * 0.8f; }

        private static string ChipText(Effect e)
        {
            return e.Name + " " + (e.Negative ? Tint(e.Percent, StampRed) : e.Percent);
        }

        private static float EffectChipWidth(Ctx c, Effect e)
        {
            return c.Measure(ChipText(e), ChipFont(c)) + 6f * c.U;
        }

        /// 标签后面写什么：效果用墨色，说明用小字淡色。
        private static string EffectText(Effect e)
        {
            string text = e.Main.Length > 0 ? Tint(e.Main, Ink) : "";
            if (e.Notes.Count > 0) text += (text.Length > 0 ? "  " : "") + Small(string.Join(" · ", e.Notes.ToArray()), InkSoft);
            return text;
        }

        /// 标签画在它的底色上，在 lineH 高的行里垂直居中。
        private static void EffectChip(Ctx c, Effect e, float x, float y, float lineH)
        {
            Color wash, ink;
            Wash(EffectHue(e.Name), out wash, out ink);
            TextMeshProUGUI chip = Text(c, ChipFont(c), ChipText(e), ink, 0f);
            Vector2 cs = chip.rectTransform.sizeDelta;
            float px = 3f * c.U, py = c.U, chipH = cs.y + 2f * py;
            float cy = y + Mathf.Round((lineH - chipH) / 2f);
            Solid(c, wash, x, cy, cs.x + 2f * px, chipH);
            Ui.Move(chip.rectTransform, x + px, cy + py);
        }

        /// 从第 i 行开始的一串效果行（i 停在最后一行），放在数值列里，用它上面那行小标题当标签。自己能说清的标签（“性能 -22%”）排在同一行；后面还有内容的，一个一行，内容对齐到最宽标签之后。
        private static float Effects(Ctx c, Block bl, ref int i, string label, float y)
        {
            List<Effect> run = new List<Effect>();
            int j = i;
            for (; j < bl.Rows.Count; j++)
            {
                Effect e = EffectOf(bl.Rows[j]);
                if (e == null) break;
                run.Add(e);
            }
            i = j - 1;
            float lh = LineH(c), left = ValueX(c), right = c.W - c.Pad;
            float h = Mathf.Max(lh, GridLabel(c, label, y));
            bool bare = true;
            float col = 0f;
            for (int k = 0; k < run.Count; k++)
            {
                if (run[k].Main.Length > 0 || run[k].Notes.Count > 0) bare = false;
                col = Mathf.Max(col, EffectChipWidth(c, run[k]));
            }
            if (bare)
            {
                float x = left, top = y;
                for (int k = 0; k < run.Count; k++)
                {
                    float w = EffectChipWidth(c, run[k]);
                    if (x > left && x + w > right)
                    {
                        x = left;
                        y += lh + RowGap(c);
                    }
                    EffectChip(c, run[k], x, y, lh);
                    x += w + c.Gap;
                }
                return Mathf.Max(top + h, y + lh) + RowGap(c);
            }
            for (int k = 0; k < run.Count; k++)
            {
                EffectChip(c, run[k], left, y, lh);
                float th = GridText(c, EffectText(run[k]), RowSize(c), left + col + c.Gap, y);
                y += Mathf.Max(k == 0 ? h : lh, th) + RowGap(c);
            }
            return y;
        }

        // ---------------------------------------------------------------- 内容物

        /// 一个分组的标签：面板的标题；没有标题的电池或水箱，就写它是什么。
        private static string GroupLabel(ContentGroup g)
        {
            if (g.Label.Length > 0) return g.Label;
            if (g.Gauges.Count > 0) return SectionedTooltipsMod.L(g.Gauges[0].Power ? "gauge_power" : "gauge_liquid");
            // 没有标题、只装一种东西的槽：写它的名字，而不是画个旁边什么都没写的格子。
            if (g.Entries.Count == 1) return g.Entries[0].Name;
            return "";
        }

        /// 内容物要不要上网格：有任何一个分组带标签就要。普通箱子的一个网格占满整宽。
        private static bool GridContents(ContentsInfo contents)
        {
            for (int g = 0; g < contents.Groups.Count; g++) if (GroupLabel(contents.Groups[g]).Length > 0) return true;
            return false;
        }

        /// 一个带标签、里面只有一个格子的槽。
        private static bool OneCell(ContentGroup g)
        {
            return g.Gauges.Count == 0 && g.Entries.Count == 1 && GroupLabel(g).Length > 0;
        }

        private static string GaugeNote(Gauge g)
        {
            return g.Note.Length > 0 ? g.Note : g.Name;
        }

        private static float GaugeWidth(Ctx c, Gauge g)
        {
            return BarW(c) + c.Gap + c.Measure(g.Amount, RowSize(c)) + 2f * c.Gap + c.Measure(GaugeNote(g), NoteSize(c));
        }

        /// “电池  ▬▬▬▬ 66 / 100  能量电池”：量条在数值列，然后是量，然后是等级或名字。
        private static float GaugeRow(Ctx c, Gauge g, string label, float y)
        {
            float lh = LineH(c);
            float h = Mathf.Max(lh, GridLabel(c, label, y));
            float x = ValueX(c);
            Meter(c, g.Ratio, g.Fill, x, y, BarW(c), lh);
            x += BarW(c) + c.Gap;
            string text = Tint(g.Amount, Ink);
            string note = GaugeNote(g);
            if (note.Length > 0) text += "  " + Small(note, InkSoft);
            h = Mathf.Max(h, GridText(c, text, RowSize(c), x, y));
            return y + h + RowGap(c);
        }

        /// 一个标题下的内容物，机器的每个面板是网格的一行：先是它的量条，再是格子。
        private static float Contents(Ctx c, ContentsInfo contents, float y)
        {
            string title = SectionedTooltipsMod.L("panel_contents") + " " + contents.Total;
            y = SectionHeader(c, title, InkOf(Sections.StyleOf(Sections.Container).Accent, Ink), y);
            bool grid = GridContents(contents);
            float body = grid ? ValueX(c) : LabelX(c);
            float cell = Mathf.Round(c.M.Line * 1.5f);
            float mid = Mathf.Max(0f, Mathf.Round((cell - LineH(c)) / 2f));
            for (int g = 0; g < contents.Groups.Count; g++)
            {
                ContentGroup group = contents.Groups[g];
                if (g > 0) y += c.Gap * 0.5f;
                if (grid && OneCell(group))
                {
                    // 每个只装一样东西的槽（“种子输入”、“作物输出”）放得下就排在同一行。
                    GridLabel(c, Tint(GroupLabel(group), InkSoft), y + mid);
                    float bottom = Grid(c, group.Entries, ValueX(c), y);
                    float x = ValueX(c) + cell;
                    while (g + 1 < contents.Groups.Count && OneCell(contents.Groups[g + 1]))
                    {
                        ContentGroup next = contents.Groups[g + 1];
                        string rich = Tint(GroupLabel(next), InkSoft);
                        float lx = x + 3f * c.Gap, cx = lx + c.Measure(rich, RowSize(c)) + 2f * c.Gap;
                        if (cx + cell > c.W - c.Pad) break;
                        TextMeshProUGUI t = Text(c, RowSize(c), rich, Color.white, 0f);
                        Ui.Move(t.rectTransform, lx, y + mid);
                        bottom = Mathf.Max(bottom, Grid(c, next.Entries, cx, y));
                        x = cx + cell;
                        g++;
                    }
                    y = bottom + RowGap(c);
                    continue;
                }
                string label = grid ? Tint(GroupLabel(group), InkSoft) : "";
                for (int k = 0; k < group.Gauges.Count; k++)
                {
                    y = GaugeRow(c, group.Gauges[k], label, y);
                    label = "";
                }
                if (group.Entries.Count > 0)
                {
                    float top = y;
                    // 标签放在第一行格子的旁边，垂直居中。
                    if (label.Length > 0) GridLabel(c, label, y + Mathf.Max(0f, Mathf.Round((cell - LineH(c)) / 2f)));
                    y = Grid(c, group.Entries, body, y) + RowGap(c);
                    y = Mathf.Max(y, top + cell);
                }
            }
            return y + c.Gap;
        }

        // ---------------------------------------------------------------- 宽度

        /// 标签列：和单子上最长的标签一样宽，最多 LabelChars 个字符。
        private static float LabelColumn(Ctx c, LayoutResult r)
        {
            float size = RowSize(c), w = 0f;
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (!OnSlip(bl.Key)) continue;
                bool strip = StripsBrackets(c, bl);
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    Row row = bl.Rows[j];
                    if (row.Heading) w = Mathf.Max(w, c.Measure(InkRich(row.Label, InkSoft, false), size));
                    else if (row.IsPair)
                    {
                        Upgrade u = UpgradeOf(row);
                        string note;
                        if (u != null) w = Mathf.Max(w, c.Measure(u.Name, size));
                        else if (EffectOf(row) == null) w = Mathf.Max(w, c.Measure(PairLabel(c, row, strip, out note), size));
                    }
                    else
                    {
                        List<Row> parts = PipeParts(row);
                        string note;
                        if (parts != null) for (int k = 0; k < parts.Count; k++) w = Mathf.Max(w, c.Measure(PairLabel(c, parts[k], false, out note), size));
                    }
                }
            }
            if (r.Contents != null)
                for (int g = 0; g < r.Contents.Groups.Count; g++) w = Mathf.Max(w, c.Measure(GroupLabel(r.Contents.Groups[g]), size));
            return Mathf.Ceil(Mathf.Min(w, LabelCap(c)));
        }

        /// 网格需要的单子宽度：标签列，加上最宽的一行数值（进度条、格子、标签、量条）。
        private static float GridWidth(Ctx c, LayoutResult r)
        {
            float value = 0f, size = RowSize(c);
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (!OnSlip(bl.Key)) continue;
                bool labelled = Labelled(bl), module = bl.Key == Sections.Module, strip = StripsBrackets(c, bl);
                // 效果的文字从这一串里最宽的标签之后开始，不是从自己那个标签后面。
                float chip = 0f, said = 0f;
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    Row row = bl.Rows[j];
                    if (row.Heading) continue;
                    if (!row.IsPair)
                    {
                        // 数值列里没有自己标签的行（“金属锭×1：9/10、再+175%升档”）。
                        if (!labelled || (module && Affix(row)) || (j > 0 && Reading(bl, j - 1) == row)) continue;
                        List<Row> parts = PipeParts(row);
                        if (parts != null) for (int k = 0; k < parts.Count; k++) value = Mathf.Max(value, c.Measure(ValueRich(parts[k].Value), size));
                        else value = Mathf.Max(value, GridRowWidth(c, row));
                        continue;
                    }
                    Upgrade u = UpgradeOf(row);
                    Effect e = u == null ? EffectOf(row) : null;
                    if (u != null) value = Mathf.Max(value, UpgradeWidth(c, u));
                    else if (e != null)
                    {
                        chip = Mathf.Max(chip, EffectChipWidth(c, e));
                        string text = EffectText(e);
                        if (text.Length > 0) said = Mathf.Max(said, c.Measure(text, size));
                    }
                    else value = Mathf.Max(value, GridPairWidth(c, bl, j, strip));
                }
                if (chip > 0f) value = Mathf.Max(value, said > 0f ? chip + c.Gap + said : chip);
            }
            if (r.Contents != null)
                for (int i = 0; i < r.Contents.Gauges.Count; i++) value = Mathf.Max(value, GaugeWidth(c, r.Contents.Gauges[i]));
            return ValueX(c) - c.Pad + value;
        }
    }
}
