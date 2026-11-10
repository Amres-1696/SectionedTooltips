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
            string plain = TextUnits.Plain(row.Label).Trim();
            int open = TextRules.TrailingGroup(plain);
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
                if (c.Measure(InkRich(row.Label, InkSoft, false), RowSize(c)) > LabelCap(c) && TextRules.TrailingGroup(TextUnits.Plain(row.Label).Trim()) > 0) return true;
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
            float wrap = c.LabelCol > 0f && c.Measure(rich, size) > c.LabelCol ? Mathf.Max(c.LabelCol, size) : 0f;
            TextMeshProUGUI t = Text(c, size, rich, Color.white, wrap);
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
            string plain = TextUnits.Plain(segs).Trim(), main = plain, note = "";
            int open = TextRules.TrailingGroup(plain);
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

        // ---------------------------------------------------------------- 进度条

        private static float BarW(Ctx c) { return Mathf.Round(c.M.Line * 3.5f); }

        /// 条的颜色：其他模组按 RGB 给的（AquaSense 的成分条）和游戏调色板一样调到纸上。
        private static Color BarInk(BarMark bar)
        {
            return bar.HasRgb ? InkOf((Color)bar.Rgb, Ink) : InkOf(bar.Color, Ink);
        }
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
            // 不是 0 就至少留一道细条（1% 的污染物四舍五入后会变成空槽，看着像没数据）。
            if (ratio > 0f) fill = Mathf.Max(fill, Mathf.Max(1f, c.U));
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
                if (!row.IsPair || TextUnits.Plain(row.Label).Trim().TrimEnd(':', '：') != word) continue;
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
            y = SectionHeader(c, Sections.TitleOf(bl.Key), y);
            float left = LabelX(c);
            bool module = bl.Key == Sections.Module, stats = false, affixes = false;
            bool labelled = Labelled(bl), strip = StripsBrackets(c, bl);
            TabLayout tab = TabLayoutOf(c, bl);
            bool liquid = bl.Key == Sections.Liquid, ruled = false;
            NumCols nums = liquid ? NumColsOf(c, bl) : null;
            // 小标题（“升级：”、“模组效果：”）给它下面没有自己标签的行当标签。
            string pending = null;
            bool headed = false;   // 正处在某个小标题管着的行里
            for (int i = 0; i < bl.Rows.Count; i++)
            {
                Row row = bl.Rows[i];
                // 单子太高时每行另起一块，可以断到下一栏；小标题和它管着的行不拆开。
                if (i > 0 && pending == null) Split(c, y);
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
                    if (module && Settings.Debug != null && Settings.Debug.Value)
                        SectionedTooltipsMod.Log(string.Format("模块节：标签列 {0:F0}px，值列 {1:F0}px，小标题「{2}」", c.LabelCol, ValueX(c), TextUnits.Plain(row.Label).Trim()));
                    if (pending != null) y = GridLine(c, pending, y);
                    pending = InkRich(row.Label, InkSoft, false);
                    headed = true;
                    continue;
                }
                stats = true;
                Upgrade u = UpgradeOf(row);
                if (u != null)
                {
                    // 名字放在标签列，升级项顺着这一列读（“储藏 ▬▬▬▬ 4/4 废金属”）；上面的小标题单独一行。
                    if (!string.IsNullOrEmpty(pending)) y = GridLine(c, pending, y);
                    pending = null;
                    y = UpgradeRow(c, u, Tint(u.Name, InkSoft), y);
                    continue;
                }
                // 只在小标题下面（“模组效果：”）：孤零零一个“+20%”只是个数字。
                if (pending != null && module && EffectOf(row) != null)
                {
                    y = Effects(c, bl, ref i, pending, y);
                    pending = "";
                    continue;
                }
                if (row.IsPair)
                {
                    headed = false;
                    // 有自己标签的行上面的小标题单独占一行。
                    if (!string.IsNullOrEmpty(pending)) y = GridLine(c, pending, y);
                    pending = null;
                    if (row.Wide) { y = ModifierRow(c, row, y); continue; }
                    if (GeneOf(bl, row) != null) { y = Genes(c, bl, ref i, y); continue; }
                    Row reading = Reading(bl, i);
                    y = GridPair(c, row, reading, strip, y, NumColsOf(c, bl));
                    if (reading != null) i++;
                    continue;
                }
                List<Row> parts = PipeParts(row);
                if (parts != null)
                {
                    for (int k = 0; k < parts.Count; k++) y = GridPair(c, parts[k], null, false, y, null);
                    continue;
                }
                if (row.Cells != null && tab != null)
                {
                    // 制表位行自己按列落位，不参与标签列。
                    if (!string.IsNullOrEmpty(pending)) y = GridLine(c, pending, y);
                    pending = null;
                    // 液体里带条的成分表下面紧接着物品详情的成分表：中间一道虚线，读得出是两张表。
                    if (liquid && i > 0 && !ruled)
                    {
                        ruled = true;
                        y += c.Gap * 0.5f;
                        Dashes(c, left, y, c.W - c.Pad - left, 3f * c.U, 2f * c.U, c.U, PaperEdge);
                        y += c.U + c.Gap * 0.5f;
                    }
                    y = GridCells(c, row, tab, left, y, nums, liquid);
                    continue;
                }
                bool negative;
                if (bl.Key == Sections.Weapon && AffixSign(row, out negative))
                {
                    // 枪械词条：正号垫浅绿、负号垫浅红，一眼分得出哪条是好的。
                    // 上面有小标题时，有标签列就写在这一行的标签列里，没有就单独占一行。
                    float top = y;
                    bool head = !string.IsNullOrEmpty(pending);
                    if (head && !labelled) top = y = GridLine(c, pending, y);
                    y = WeaponAffix(c, row, negative, labelled ? ValueX(c) : left, y);
                    if (head && labelled) y = Mathf.Max(y, top + GridLabel(c, pending, top) + RowGap(c));
                    if (pending != null) pending = "";
                    continue;
                }
                // 带进度条的行跟着上面那条条的起点走（和数值列对齐）；其余的从标签列起，
                // 免得“（另 3 项低于显示阈值）”这种说明缩到数值列去。
                // 小标题（“模组效果：”）管着的那几行例外：标签占着标签列，行也从标签列起就会叠在一起，
                // 整组都从数值列起。
                // 进度条总是从数值列起（这一节只有一条没标签的条时也是，比如空量筒的“▬ 0 ml / 100 ml”），和别的节的条对齐；宽度也是按数值列算的。
                float rowX = BarOf(row.Segs) != null || (labelled && headed) ? ValueX(c) : left;
                y = GridRow(c, row, pending ?? "", rowX, y);
                if (pending != null) pending = "";
            }
            if (!string.IsNullOrEmpty(pending)) y = GridLine(c, pending, y);
            return y;
        }

        /// 这一节里有没有标签；纯文字的节占满整宽。
        private static bool Labelled(Block bl)
        {
            for (int i = 0; i < bl.Rows.Count; i++) if (bl.Rows[i].IsPair || bl.Rows[i].Heading) return true;
            return false;
        }

        /// 单独占一行的小标题（下面的行有自己的标签）。数值列空着，所以不挤在标签列里，
        /// 一直写到单子右边缘：“基因（复杂度0，热度+12）”不会被折成两行。返回底部。
        private static float GridLine(Ctx c, string label, float y)
        {
            float h = Mathf.Max(LineH(c), GridText(c, label, RowSize(c), LabelX(c), y));
            return y + h + RowGap(c);
        }

        /// 这个小标题会单独占一行：后面是有自己标签的行、另一个小标题，或者它在最后。
        /// 这样的小标题不用撑宽标签列。模组效果例外：Section 把它画在标签列里，后面的效果行（“质量 +6%”拆出的行也算）排在数值列。
        private static bool AloneHeading(Block bl, int i)
        {
            if (i + 1 >= bl.Rows.Count) return true;
            Row next = bl.Rows[i + 1];
            if (bl.Key == Sections.Module && !next.Heading && UpgradeOf(next) == null && EffectOf(next) != null) return false;
            return next.Heading || next.IsPair;
        }

        /// 名字靠左，百分比右对齐到单子右边；名字太长就在数值左边折行。
        private static float ModifierRow(Ctx c, Row row, float y)
        {
            float size = RowSize(c), lh = LineH(c);
            float right = c.W - c.Pad;
            TextMeshProUGUI value = Text(c, size, InkRich(row.Value, Ink), Color.white, 0f);
            Vector2 vs = value.rectTransform.sizeDelta;
            Ui.Move(value.rectTransform, right - vs.x, y);
            float room = right - vs.x - c.Gap - LabelX(c);
            float h = Mathf.Max(lh, GridText(c, InkRich(row.Label, InkSoft, false), size, LabelX(c), y,
                LabelX(c) + Mathf.Max(room, size)));
            return y + Mathf.Max(h, vs.y) + RowGap(c);
        }

        /// “标签  数值”，有进度条就从数值列开头画。reading（下面那条只有读数的行）把它的进度条和读数放到这一行，在数值前面。
        private static float GridPair(Ctx c, Row row, Row reading, bool strip, float y, NumCols nums)
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
                Meter(c, bar.Ratio, BarInk(bar), x, y, BarW(c), lh);
                x += BarW(c) + c.Gap;
                if (reading == null) value = Unparen(value);
                List<List<Seg>> cells = nums != null && reading == null ? NumCells(value) : null;
                if (cells != null) return NumRow(c, nums, cells, x, y, h);
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

        /// 液体一节带条的成分行（“水 ▬▬ 7536.374 ml  99.00%”）条后面那几个数：每列多宽，整组多宽。
        internal sealed class NumCols
        {
            internal float[] W;
            internal float Gap;
            internal float Width;
        }

        /// 条后面的数值按两个以上空格切成几格，每格都以数字开头时返回这些格，否则 null。
        private static List<List<Seg>> NumCells(List<Seg> value)
        {
            // 条本身（游戏的或其他模组按占位画的）也是数值里的一段，不算文字。
            List<Seg> text = new List<Seg>(value.Count);
            for (int i = 0; i < value.Count; i++) if (value[i].Bar == null) text.Add(value[i]);
            value = text;
            List<List<Unit>> cells = TextUnits.SplitCells(TextUnits.Of(value));
            if (cells == null) return null;
            List<List<Seg>> r = new List<List<Seg>>(cells.Count);
            for (int k = 0; k < cells.Count; k++)
            {
                if (!TextRules.StartsWithNumber(TextUnits.Plain(cells[k]).Trim())) return null;
                r.Add(TextUnits.ToSegs(cells[k], value));
            }
            return r;
        }

        /// 液体一节里至少两行带条的成分行时，它们条后面的数字共用的列；不是这种节返回 null。一次构建算一次。
        private static NumCols NumColsOf(Ctx c, Block bl)
        {
            NumCols cached;
            if (c.Nums.TryGetValue(bl, out cached)) return cached;
            NumCols nums = null;
            if (bl.Key == Sections.Liquid)
            {
                float size = RowSize(c);
                float[] w = new float[0];
                int rows = 0;
                for (int i = 0; i < bl.Rows.Count; i++)
                {
                    Row row = bl.Rows[i];
                    if (!row.IsPair || row.Wide || Reading(bl, i) != null) continue;
                    List<Seg> value = row.Value;
                    if (SplitBar(ref value) == null) continue;
                    List<List<Seg>> cells = NumCells(Unparen(value));
                    if (cells == null) continue;
                    rows++;
                    if (cells.Count > w.Length) Array.Resize(ref w, cells.Count);
                    for (int k = 0; k < cells.Count; k++)
                    {
                        int col = TabLayout.Column(k, cells.Count, w.Length);
                        w[col] = Mathf.Max(w[col], c.Measure(ValueRich(cells[k]), size));
                    }
                }
                // 物品详情那张成分表的数字也对齐到这几列，列宽要放得下它们。
                for (int i = 0; rows >= 2 && i < bl.Rows.Count; i++)
                {
                    List<List<Seg>> cells = bl.Rows[i].Cells;
                    if (cells == null || cells.Count < 2) continue;
                    bool numbers = true;
                    for (int k = 1; k < cells.Count; k++) if (!TextRules.StartsWithNumber(TextUnits.Plain(cells[k]).Trim())) numbers = false;
                    if (!numbers) continue;
                    for (int k = 1; k < cells.Count; k++)
                    {
                        int col = TabLayout.Column(k - 1, cells.Count - 1, w.Length);
                        w[col] = Mathf.Max(w[col], c.Measure(CellRich(cells[k], false), size));
                    }
                }
                if (rows >= 2)
                {
                    nums = new NumCols();
                    nums.W = w;
                    nums.Gap = Mathf.Round(c.Gap * TabGapFactor);
                    for (int k = 0; k < w.Length; k++) nums.Width += w[k] + (k > 0 ? nums.Gap : 0f);
                }
            }
            c.Nums[bl] = nums;
            return nums;
        }

        /// 液体表里的一格：名称用标签的淡墨，数字用墨色，都不加粗（和其他节的“标签 数值”一样）。
        private static string CellRich(List<Seg> cell, bool name)
        {
            string rich = name ? InkRich(cell, InkSoft, false) : ValueInks(cell);
            return rich.Replace("<b>", "").Replace("</b>", "");
        }

        /// 物品详情成分表的一行（“水 7.54L 99.00%”）能不能把数字对齐到上面带条那张表的数字列：后面几格都是数字，名称放得进条的左边。
        private static bool LiquidCellsFit(Ctx c, Row row, float x)
        {
            int n = row.Cells.Count;
            if (n < 2) return false;
            for (int k = 1; k < n; k++) if (!TextRules.StartsWithNumber(TextUnits.Plain(row.Cells[k]).Trim())) return false;
            return x + c.Measure(CellRich(row.Cells[0], true), RowSize(c)) + c.Gap <= ValueX(c) + BarW(c) + c.Gap;
        }

        /// 名称在标签列，数字各自靠右落在上面带条那张表的数字列里。返回底部。
        private static float LiquidCells(Ctx c, Row row, NumCols nums, float x, float y)
        {
            float h = Mathf.Max(LineH(c), GridText(c, CellRich(row.Cells[0], true), RowSize(c), x, y));
            List<List<Seg>> numbers = new List<List<Seg>>(row.Cells.Count - 1);
            for (int k = 1; k < row.Cells.Count; k++)
            {
                List<Seg> one = new List<Seg>(row.Cells[k].Count);
                for (int j = 0; j < row.Cells[k].Count; j++)
                {
                    Seg sg = row.Cells[k][j].With(row.Cells[k][j].Text);
                    sg.Bold = false;
                    one.Add(sg);
                }
                numbers.Add(one);
            }
            return NumRow(c, nums, numbers, ValueX(c) + BarW(c) + c.Gap, y, h);
        }

        /// 条后面的几格数字各自靠右落在共用的列里。h 是标签已经占的高度。返回底部。
        private static float NumRow(Ctx c, NumCols nums, List<List<Seg>> cells, float x, float y, float h)
        {
            for (int k = 0; k < cells.Count; k++)
            {
                int col = TabLayout.Column(k, cells.Count, nums.W.Length);
                float end = x;
                for (int j = 0; j <= col; j++) end += nums.W[j] + (j > 0 ? nums.Gap : 0f);
                TextMeshProUGUI t = Text(c, RowSize(c), ValueRich(cells[k]), Color.white, 0f);
                Ui.Move(t.rectTransform, end - t.rectTransform.sizeDelta.x, y);
                h = Mathf.Max(h, t.rectTransform.sizeDelta.y);
            }
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
            if (reading != null) text = Tint(TextUnits.Plain(reading.Segs).Trim(), Ink) + "  " + text;
            return text;
        }

        private static float GridPairWidth(Ctx c, Block bl, int i, bool strip)
        {
            Row row = bl.Rows[i], reading = Reading(bl, i);
            string note;
            PairLabel(c, row, strip, out note);
            List<Seg> value = row.Value;
            BarMark bar = reading != null ? BarOf(reading.Segs) : SplitBar(ref value);
            if (bar != null && reading == null) value = Unparen(value);
            NumCols nums = bar != null && reading == null ? NumColsOf(c, bl) : null;
            if (nums != null && NumCells(value) != null) return BarW(c) + c.Gap + nums.Width;
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
            string t = TextUnits.Plain(next.Segs).Trim();
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
            string rich = ValueInks(segs);
            if (bar != null)
            {
                Meter(c, bar.Ratio, BarInk(bar), x, y, BarW(c), lh);
                x += BarW(c) + c.Gap;
            }
            else rich = Remark(segs);
            h = Mathf.Max(h, GridText(c, rich, RowSize(c), x, y));
            return y + h + RowGap(c);
        }

        /// 没有标签的整行说明（“1 瓶陈化中，每天陈化 1 天”）：不加粗，默认颜色的字用比数值淡一档的墨，有自己颜色的照旧。
        private static string Remark(List<Seg> segs)
        {
            Color mid = Color.Lerp(Ink, InkSoft, 0.5f);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (Drawn(s) || string.IsNullOrEmpty(s.Text)) continue;
                Color ink = ValueInk(s.Color);
                sb.Append(Tint(s.Text, ink == Ink ? mid : ink));
            }
            return sb.ToString().Trim();
        }

        private static float GridRowWidth(Ctx c, Row row)
        {
            List<Seg> segs = row.Segs;
            BarMark bar = SplitBar(ref segs);
            return (bar != null ? BarW(c) + c.Gap : 0f) + c.Measure(ValueInks(segs), RowSize(c));
        }

        /// 有进度条时，“(115/112)”外面那对括号只是原版用来和条隔开的，去掉。
        private static List<Seg> Unparen(List<Seg> segs)
        {
            string plain = TextUnits.Plain(segs).Trim();
            float a, b;
            if (plain.Length < 5 || plain[0] != '(' || plain[plain.Length - 1] != ')'
                || !TextRules.ParseFraction(plain.Substring(1, plain.Length - 2), out a, out b)) return segs;
            List<Seg> r = new List<Seg>(segs);
            int first = -1, last = -1;
            for (int i = 0; i < r.Count; i++)
            {
                if (Drawn(r[i]) || string.IsNullOrEmpty(r[i].Text)) continue;
                if (first < 0 && r[i].Text.IndexOf('(') >= 0) first = i;
                if (r[i].Text.IndexOf(')') >= 0) last = i;
            }
            if (first < 0 || last < 0) return segs;
            string t = r[first].Text;
            int k = t.IndexOf('(');
            r[first] = r[first].With(t.Remove(k, 1));
            t = r[last].Text;
            k = t.LastIndexOf(')');
            r[last] = r[last].With(t.Remove(k, 1));
            return r;
        }

        // ---------------------------------------------------------------- 养殖的基因

        /// <summary>
        /// 养殖的一行属性：“免疫力: 28.46 (27.63 / 29.29)”。括号里是两个等位基因（开着基因扫描仪才有），
        /// 游戏拿它们和物种的基准值比：好 5% 以上标绿、差 5% 以上标红，其余是中性色。
        /// </summary>
        private sealed class Gene
        {
            public string Value = "", A = "", B = "";
            /// 1 好，-1 差，0 中性。
            public int ValueTone, ToneA, ToneB;
        }

        private static Gene GeneOf(Block bl, Row row)
        {
            if (bl.Key != Sections.Husbandry || !row.IsPair || row.Value == null || BarOf(row.Value) != null) return null;
            // 去掉空白段后，末尾应当是“(”、A、“/”、B、“)”五段，各自带着游戏给的颜色。
            List<Seg> parts = new List<Seg>();
            for (int i = 0; i < row.Value.Count; i++)
                if (TextUnits.Plain(row.Value[i].Text).Trim().Length > 0) parts.Add(row.Value[i]);
            int n = parts.Count;
            if (n < 5) return null;
            string close = Trimmed(parts[n - 1]), b = Trimmed(parts[n - 2]), slash = Trimmed(parts[n - 3]), a = Trimmed(parts[n - 4]), open = Trimmed(parts[n - 5]);
            float f;
            if (close != ")" || slash != "/" || !open.EndsWith("(", StringComparison.Ordinal)
                || !TextRules.ParseNumber(a, out f) || !TextRules.ParseNumber(b, out f)) return null;
            Gene g = new Gene();
            StringBuilder value = new StringBuilder();
            for (int i = 0; i < n - 5; i++) value.Append(parts[i].Text);
            string rest = open.Substring(0, open.Length - 1).Trim();
            if (rest.Length > 0) value.Append(' ').Append(rest);
            g.Value = TextUnits.Plain(value.ToString()).Trim();
            if (g.Value.Length == 0) return null;
            g.ValueTone = Tone(parts[0].Color);
            g.A = a;
            g.B = b;
            g.ToneA = Tone(parts[n - 4].Color);
            g.ToneB = Tone(parts[n - 2].Color);
            return g;
        }

        private static string Trimmed(Seg s) { return TextUnits.Plain(s.Text).Trim(); }

        /// 游戏的颜色是好、坏还是中性：绿是好，红是坏，其余（青色、无色）中性。
        private static int Tone(Palette p)
        {
            if (p == Palette.None) return 0;
            Color c = Overlay.ToColor(p, Palette.Gray);
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            if (s < 0.25f) return 0;
            if (h > 0.17f && h < 0.45f) return 1;
            if (h < 0.05f || h > 0.92f) return -1;
            return 0;
        }

        /// 基因表的列宽：表现值、A、B 三列各取这一节里最宽的（含表头），数字右对齐。
        private sealed class GeneCols
        {
            public float Value, A, B, Gap, Pad;
            public float Width { get { return Value + Gap + Pad + A + Pad + Gap + Pad + B + Pad; } }
            public float RightValue(float x) { return x + Value; }
            public float RightA(float x) { return RightValue(x) + Gap + Pad + A; }
            public float RightB(float x) { return RightA(x) + Pad + Gap + Pad + B; }
        }

        private static GeneCols GeneColsOf(Ctx c, Block bl)
        {
            float size = RowSize(c), small = NoteSize(c);
            GeneCols g = new GeneCols();
            g.Gap = Mathf.Round(c.Gap * 1.5f);
            g.Pad = 3f * c.U;
            bool alleles = false;
            for (int i = 0; i < bl.Rows.Count; i++)
            {
                Gene gene = GeneOf(bl, bl.Rows[i]);
                if (gene == null) continue;
                alleles = true;
                g.Value = Mathf.Max(g.Value, c.Measure(gene.Value, size));
                g.A = Mathf.Max(g.A, c.Measure(gene.A, size));
                g.B = Mathf.Max(g.B, c.Measure(gene.B, size));
            }
            if (alleles)
            {
                g.Value = Mathf.Max(g.Value, c.Measure(SectionedTooltipsMod.L("gene_expressed"), small));
                g.A = Mathf.Max(g.A, c.Measure(SectionedTooltipsMod.L("gene_a"), small));
                g.B = Mathf.Max(g.B, c.Measure(SectionedTooltipsMod.L("gene_b"), small));
            }
            return g;
        }

        /// 从第 i 行开始的一串基因行（i 停在最后一行），上面一行小字表头。返回底部。
        private static float Genes(Ctx c, Block bl, ref int i, float y)
        {
            GeneCols cols = GeneColsOf(c, bl);
            float lh = LineH(c), size = RowSize(c), small = NoteSize(c), x = ValueX(c);
            // 表头：标签列写“基因”，三列各自右对齐。
            float hh = 0f;
            hh = Mathf.Max(hh, GridLabel(c, Small(SectionedTooltipsMod.L("gene_header"), InkSoft), y));
            hh = Mathf.Max(hh, RightText(c, SectionedTooltipsMod.L("gene_expressed"), small, InkSoft, cols.RightValue(x), y));
            hh = Mathf.Max(hh, RightText(c, SectionedTooltipsMod.L("gene_a"), small, InkSoft, cols.RightA(x), y));
            hh = Mathf.Max(hh, RightText(c, SectionedTooltipsMod.L("gene_b"), small, InkSoft, cols.RightB(x), y));
            y += hh + RowGap(c);

            for (; i < bl.Rows.Count; i++)
            {
                Row row = bl.Rows[i];
                Gene g = GeneOf(bl, row);
                if (g == null) break;
                float h = Mathf.Max(lh, GridLabel(c, InkRich(row.Label, InkSoft, false), y));
                h = Mathf.Max(h, RightText(c, g.Value, size, ToneInk(g.ValueTone, Ink), cols.RightValue(x), y));
                h = Mathf.Max(h, Allele(c, g.A, g.ToneA, cols, cols.RightA(x), y, lh));
                h = Mathf.Max(h, Allele(c, g.B, g.ToneB, cols, cols.RightB(x), y, lh));
                y += h + RowGap(c);
            }
            i--;
            return y;
        }

        /// 一个等位基因：好的垫浅绿、差的垫浅红（和种子基因一样），中性的用淡墨。返回高度。
        private static float Allele(Ctx c, string text, int tone, GeneCols cols, float right, float y, float lh)
        {
            if (tone == 0) return RightText(c, text, RowSize(c), InkSoft, right, y);
            Color wash, ink;
            Wash(tone > 0 ? GoodHue : BadHue, out wash, out ink);
            float w = c.Measure(text, RowSize(c));
            Solid(c, wash, right - w - cols.Pad, y, w + 2f * cols.Pad, lh);
            return RightText(c, text, RowSize(c), ink, right, y);
        }

        private static Color ToneInk(int tone, Color fallback)
        {
            if (tone == 0) return fallback;
            Color wash, ink;
            Wash(tone > 0 ? GoodHue : BadHue, out wash, out ink);
            return ink;
        }

        /// 右边缘对齐到 right 的一段文字。返回高度。
        private static float RightText(Ctx c, string text, float size, Color color, float right, float y)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            TextMeshProUGUI t = Text(c, size, Tint(text, color), Color.white, 0f);
            Ui.Move(t.rectTransform, right - t.rectTransform.sizeDelta.x, y);
            return t.rectTransform.sizeDelta.y;
        }

        // ---------------------------------------------------------------- 制表位

        /// <summary>
        /// 一节里制表位行的列几何：每列宽度、列间距、整表宽度，以及每列的起始 x。
        /// 绘制和“这一节要占多宽”都读同一份，不会各算一套。
        /// </summary>
        private sealed class TabLayout
        {
            internal readonly float Size;
            private readonly float[] start;
            private readonly float[] columns;
            private readonly bool[] right;
            private readonly float width;

            internal TabLayout(float[] columns, bool[] right, float gap, float size)
            {
                Size = size;
                this.columns = columns;
                this.right = right;
                start = new float[columns.Length];
                float x = 0f;
                for (int k = 0; k < columns.Length; k++)
                {
                    if (k > 0) x += gap;
                    start[k] = x;
                    x += columns[k];
                }
                width = x;
            }

            internal float Width { get { return width; } }
            internal static int Column(int cell, int cells, int columns)
            {
                return cell == cells - 1 ? columns - 1 : cell;
            }

            internal float CellStart(int cell, int cells) { return start[Column(cell, cells, start.Length)]; }

            /// 这一格靠右对齐吗（整列都是数字）；靠右时文字的右边缘在 CellEnd。
            internal bool Right(int cell, int cells) { return right[Column(cell, cells, start.Length)]; }
            internal float CellEnd(int cell, int cells)
            {
                int k = Column(cell, cells, start.Length);
                return start[k] + columns[k];
            }
        }

        /// 两列之间的间距：全角空格在游戏的 tooltip 里本来就比半角格宽。
        private const float TabGapFactor = 1.5f;

        /// <summary>本节制表位行的列几何；本节没有多列行时返回 null。一次构建里只算一次。</summary>
        private static TabLayout TabLayoutOf(Ctx c, Block bl)
        {
            TabLayout cached;
            if (c.Tabs.TryGetValue(bl, out cached)) return cached;

            int count = 0;
            for (int i = 0; i < bl.Rows.Count; i++)
            {
                List<List<Seg>> cells = bl.Rows[i].Cells;
                if (cells != null && cells.Count > count) count = cells.Count;
            }
            if (count < 2)
            {
                c.Tabs[bl] = null;
                return null;
            }

            float size = RowSize(c);
            float[] w = new float[count];
            // 第一列以外，整列都以数字开头（“7.54L”“99.00%”）就靠右对齐，小数点和单位上下对得齐。
            bool[] right = new bool[count];
            for (int k = 1; k < count; k++) right[k] = true;
            for (int i = 0; i < bl.Rows.Count; i++)
            {
                List<List<Seg>> cells = bl.Rows[i].Cells;
                if (cells == null) continue;
                for (int k = 0; k < cells.Count; k++)
                {
                    float cw = c.Measure(ValueInks(cells[k]), size);
                    int column = TabLayout.Column(k, cells.Count, count);
                    if (cw > w[column]) w[column] = cw;
                    if (!TextRules.StartsWithNumber(TextUnits.Plain(cells[k]).Trim())) right[column] = false;
                }
            }
            TabLayout tab = new TabLayout(w, right, Mathf.Round(c.Gap * TabGapFactor), size);
            c.Tabs[bl] = tab;
            return tab;
        }

        /// <summary>
        /// 一行按制表位画成几列。格数与列数相同时逐列左对齐；格数少于列数时，第一格贴第 0 列、
        /// 末格贴最后一列——“按Ctrl展开杂质　2.98%”这种少一列的提示行，百分比因此仍落在百分比列。
        /// </summary>
        private static float GridCells(Ctx c, Row row, TabLayout tab, float x, float y, NumCols nums, bool liquid)
        {
            float h = LineH(c);
            int n = row.Cells.Count;
            if (nums != null && LiquidCellsFit(c, row, x)) return LiquidCells(c, row, nums, x, y);
            if (tab.Width > c.W - c.Pad - x)
            {
                // 卡片有宽度上限。按单元格重组文字，让旧式 <pos> 也能在卡片内自然折行。
                StringBuilder line = new StringBuilder();
                for (int k = 0; k < n; k++)
                {
                    if (k > 0) line.Append(' ');
                    line.Append(ValueInks(row.Cells[k]));
                }
                h = Mathf.Max(h, GridText(c, line.ToString(), tab.Size, x, y));
                return y + h + RowGap(c);
            }
            for (int k = 0; k < n; k++)
            {
                string rich = liquid ? CellRich(row.Cells[k], k == 0) : ValueInks(row.Cells[k]);
                TextMeshProUGUI t = Text(c, tab.Size, rich, Color.white, 0f);
                float at = tab.Right(k, n) ? tab.CellEnd(k, n) - t.rectTransform.sizeDelta.x : tab.CellStart(k, n);
                Ui.Move(t.rectTransform, x + at, y);
                h = Mathf.Max(h, t.rectTransform.sizeDelta.y);
            }
            return y + h + RowGap(c);
        }


        /// <summary>武器一节里的词条行（“+12 后坐力控制”“-6 人机工效”）：正号返回 false，负号返回 true，不是词条返回 false。</summary>
        private static bool AffixSign(Row row, out bool negative)
        {
            negative = false;
            if (row.IsPair || row.Heading || row.Segs == null) return false;
            string s = TextUnits.Plain(row.Segs).Trim();
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
                Meter(c, bar.Ratio, BarInk(bar), x, y, BarW(c), lh);
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
            string plain = TextUnits.Plain(row.Segs).Trim();
            if (plain.IndexOf('|') < 0 || BarOf(row.Segs) != null) return null;
            string[] bits = plain.Split('|');
            if (bits.Length < 2) return null;
            List<Row> list = new List<Row>();
            for (int i = 0; i < bits.Length; i++)
            {
                string bit = bits[i].Trim();
                if (bit.Length == 0) continue;
                Row r = PairFromPlain(bit);
                string name, number;
                if (r == null && TextRules.NameNumber(bit, out name, out number)) r = PairFromPlain(name + "：" + number);
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
            string v = TextUnits.Plain(row.Value).Trim();
            int slash = v.IndexOf('/');
            int level, max;
            if (slash <= 0
                || !int.TryParse(v.Substring(0, slash).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out level)
                || !int.TryParse(v.Substring(slash + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out max)
                || max <= 0 || level < 0 || level > max) return null;
            string label = TextUnits.Plain(row.Label).Trim();
            int open = TextRules.TrailingGroup(label);
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
            if (TabsAsNumbers(c, u))
            {
                // 页码本身就说了有几页，后面不再写“5/5”。
                TabNumbers(c, u, x, y, w, lh);
                return y + Mathf.Max(h, lh) + RowGap(c);
            }
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

        /// 储藏区的“储物标签页 1/5”（已有几页、最多几页）。
        private static bool StorageTabs(Upgrade u)
        {
            string name = LocTemplates.StorageTabs;
            return name.Length > 0 && u.Need.Length == 0 && u.Name.Trim().TrimEnd(':', '：').Trim() == name;
        }

        /// 储藏区当前打开的是第几页（从 0 起）；没打开过、读不到时返回 -1。
        private static int OpenTab(GameItem item)
        {
            try
            {
                PixelWindow w = item != null ? item.contentWindow : null;
                MachineryPanelRow row = w != null ? w.childElement as MachineryPanelRow : null;
                MachineryPanel panel = row != null ? row.GetPanel("storage") : null;
                MachineryTabs tabs = panel != null ? panel.content as MachineryTabs : null;
                return tabs != null && tabs.count > 1 ? tabs.selected : -1;
            }
            catch { return -1; }
        }

        /// 储物标签页写成页码，格子够宽才这样（字放不下时还是格子加“1/5”）。
        private static bool TabsAsNumbers(Ctx c, Upgrade u)
        {
            if (!StorageTabs(u)) return false;
            float piece = Mathf.Floor((BarW(c) - (u.Max - 1) * c.U) / u.Max);
            return piece >= c.Measure("5", RowSize(c));
        }

        /// 储物标签页只写页码“1 2 3 4 5”：已有的页深色，还没有的浅色，当前打开的那页下面画一道线。
        private static void TabNumbers(Ctx c, Upgrade u, float x, float y, float w, float lh)
        {
            float gap = c.U;
            float piece = Mathf.Floor((w - (u.Max - 1) * gap) / u.Max);
            float step = u.Max > 1 ? (w - piece) / (u.Max - 1) : 0f;
            int open = OpenTab(c.Item);
            Color faint = Color.Lerp(Paper, InkSoft, darkPaper ? 0.65f : 0.62f);
            for (int i = 0; i < u.Max; i++)
            {
                bool have = i < u.Level;
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                TextMeshProUGUI t = Text(c, RowSize(c), Tint(n, have ? Ink : faint), Color.white, 0f);
                Vector2 ts = t.rectTransform.sizeDelta;
                float tx = Mathf.Round(x + i * step + (piece - ts.x) / 2f), ty = Mathf.Round(y + (lh - ts.y) / 2f);
                Ui.Move(t.rectTransform, tx, ty);
                if (i == open) Solid(c, Ink, tx, ty + ts.y - c.U, ts.x, 2f * c.U);
            }
        }

        private static float UpgradeWidth(Ctx c, Upgrade u)
        {
            if (TabsAsNumbers(c, u)) return PipsWidth(c, u);
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
            string v = TextUnits.Plain(row.Value).Trim();
            if (v.Length < 3 || (v[0] != '+' && v[0] != '-' && v[0] != '−')) return null;
            int i = 1;
            while (i < v.Length && (char.IsDigit(v[i]) || v[i] == '.')) i++;
            if (i == 1 || i >= v.Length || v[i] != '%') return null;
            Effect e = new Effect();
            e.Name = TextUnits.Plain(row.Label).Trim().TrimStart('-', '−').Trim();
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
            y = SectionHeader(c, title, y);
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
                if (!OnSlip(r.Blocks[i].Key)) continue;
                // 和 Section 画的顺序一致，小标题后面跟的是哪一行才算得准。
                Block bl = StatusFirst(r.Blocks[i]);
                bool strip = StripsBrackets(c, bl), module = bl.Key == Sections.Module;
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    Row row = bl.Rows[j];
                    if (row.Heading)
                    {
                        if (!AloneHeading(bl, j)) w = Mathf.Max(w, c.Measure(InkRich(row.Label, InkSoft, false), size));
                    }
                    else if (row.IsPair)
                    {
                        if (row.Wide) continue;
                        Upgrade u = UpgradeOf(row);
                        string note;
                        if (u != null) w = Mathf.Max(w, c.Measure(u.Name, size));
                        else if (GeneOf(bl, row) != null) w = Mathf.Max(w, c.Measure(InkRich(row.Label, InkSoft, false), size));
                        else w = Mathf.Max(w, c.Measure(PairLabel(c, row, strip, out note), size));
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
            float value = 0f, size = RowSize(c), tabs = 0f;
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (!OnSlip(bl.Key)) continue;
                bool labelled = Labelled(bl), module = bl.Key == Sections.Module, strip = StripsBrackets(c, bl);
                // 效果的文字从这一串里最宽的标签之后开始，不是从自己那个标签后面。
                float chip = 0f, said = 0f;
                GeneCols genes = null;
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    Row row = bl.Rows[j];
                    if (row.Heading) continue;
                    if (row.Wide)
                    {
                        value = Mathf.Max(value, c.Measure(InkRich(row.Label, InkSoft, false), size)
                            + c.Gap + c.Measure(InkRich(row.Value, Ink), size));
                        continue;
                    }
                    if (!row.IsPair)
                    {
                        // 数值列里没有自己标签的行（“金属锭×1：9/10、再+175%升档”）。
                        if (row.Cells != null) continue; // 制表位行按整表宽度算，见下面的 tab
                        if ((module && Affix(row)) || (j > 0 && Reading(bl, j - 1) == row)) continue;
                        // 带进度条的读数行（“▬ 2000/2000mL 铁锈水”）是整行画出来的，宽度必须计入，
                        // 否则单子不够宽，中文会在字中间断成两行。长句照旧折行，不撑宽单子。
                        if (!labelled && BarOf(row.Segs) == null) continue;
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
                    else if (GeneOf(bl, row) != null)
                    {
                        if (genes == null) genes = GeneColsOf(c, bl);
                        value = Mathf.Max(value, genes.Width);
                    }
                    else value = Mathf.Max(value, GridPairWidth(c, bl, j, strip));
                }
                if (chip > 0f) value = Mathf.Max(value, said > 0f ? chip + c.Gap + said : chip);
                TabLayout tab = TabLayoutOf(c, bl);
                if (tab != null && tab.Width > tabs) tabs = tab.Width;
            }
            if (r.Contents != null)
                for (int i = 0; i < r.Contents.Gauges.Count; i++) value = Mathf.Max(value, GaugeWidth(c, r.Contents.Gauges[i]));
            // 制表位行从标签列起画，所以它要的宽度和数值列那套分开算。
            return Mathf.Max(ValueX(c) - c.Pad + value, LabelX(c) - c.Pad + tabs);
        }
    }
}
