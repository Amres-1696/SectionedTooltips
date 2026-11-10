using System;
using System.Collections.Generic;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 分好节的行拆成“标签 / 数值”：哪几行并成一块，每行是一对、小标题、表格还是普通文字。两种风格共用。
    internal static partial class Layout
    {
        /// 这一对的值里已经有进度条（分数自动补的）。
        private static bool HasFractionBar(Row row)
        {
            if (row.Value == null) return false;
            for (int i = 0; i < row.Value.Count; i++) if (row.Value[i].Bar != null) return true;
            return false;
        }

        internal static List<Block> Group(List<Line> lines)
        {
            // 宽度设置是给经典风格用的；估价单自己决定换行，这里保持默认值。
            int maxLabel = Math.Max(8, (Settings.IsTicket ? 46 : Settings.MaxChars) * 3 / 5) * TextUnits.Cell;
            List<Block> blocks = new List<Block>();
            Block cur = null;
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                // 一行里塞了好几组“名字 +N%”：拆成每组一行，名字靠左、百分比右对齐。
                List<string[]> pairs = PercentRows(l);
                if (pairs != null)
                {
                    string pk = l.Key == Sections.Dev ? Sections.Info : l.Key;
                    if (cur == null || cur.Key != pk) { cur = new Block(); cur.Key = pk; blocks.Add(cur); }
                    for (int j = 0; j < pairs.Count; j++) cur.Rows.Add(PairRow(pairs[j], l.Ord));
                    continue;
                }
                // “精工扩舱 0 | 剩余升级次数：12 | 酒瓶区 6x4”：竖线隔开的几段各自成行，
                // 否则冒号会把整行切成一个假标签。
                if (l.Key != Sections.Module && l.Key != Sections.Desc && l.Plain.IndexOf('|') >= 0)
                {
                    List<List<Unit>> pipes = TextUnits.SplitPipes(TextUnits.Of(l.Segs));
                    if (pipes != null)
                    {
                        List<Row> split = new List<Row>(pipes.Count);
                        bool ok = true;
                        for (int k = 0; k < pipes.Count; k++)
                        {
                            Line part = new Line();
                            part.Segs = TextUnits.ToSegs(pipes[k], l.Segs);
                            part.Key = l.Key;
                            part.Ord = l.Ord;
                            part.Plain = TextUnits.Plain(pipes[k]);
                            Row pr = MakeRow(part, maxLabel, l.Key != Sections.Module);
                            // 没冒号的一段（“精工扩舱 0”）按“名字 数值”拆，不然整行会退回去被冒号切出假标签。
                            if (!pr.IsPair) pr = NameNumberRow(pipes[k], l.Segs) ?? pr;
                            if (!pr.IsPair) { ok = false; break; }
                            split.Add(pr);
                        }
                        if (ok && split.Count >= 2)
                        {
                            if (cur == null || cur.Key != l.Key) { cur = new Block(); cur.Key = l.Key; blocks.Add(cur); }
                            for (int k = 0; k < split.Count; k++) cur.Rows.Add(split[k]);
                            continue;
                        }
                    }
                }
                // 模组的状态和词条是句子，不是“标签: 数值”：词条描述里的冒号会被切成两半。
                Row row = MakeRow(l, maxLabel, l.Key != Sections.Module);
                string key = l.Key;
                if (key == Sections.Dev) key = Sections.Info;
                // 写在价格旁边的标记（“已上锁”之类）放在名字下面更好读。
                if (key == Sections.Value && !row.IsPair) key = Sections.Title;
                // “[标签] 耐久度 0/400”这种：标签行里带分数，当名字下面的文字画不出来进度条，
                // 挪到“状态”当普通的一对，和别的分数一样出条。
                if (key == Sections.Label && row.IsPair && HasFractionBar(row))
                {
                    if (cur == null || cur.Key != Sections.Condition) { cur = new Block(); cur.Key = Sections.Condition; blocks.Add(cur); }
                    cur.Rows.Add(row);
                    continue;
                }
                if (key == Sections.Title || key == Sections.Desc) row = Plain(l);
                row.Ord = l.Ord;

                if (cur == null || cur.Key != key)
                {
                    cur = new Block();
                    cur.Key = key;
                    blocks.Add(cur);
                }
                cur.Rows.Add(row);
                // 经典风格里没有标签的行前面会多一个圆点，只给估价单拆。
                if (row.IsPair && key != Sections.Value && Settings.IsTicket) SplitPercentList(cur, row);
            }
            MergeSplitSections(blocks);
            if (Settings.IsTicket) LiquidTables(blocks);
            return blocks;
        }

        /// 液体一节里游戏写的成分行（“水　7536.374 ml  99.00%”）是一对，数值里用两个空格隔开体积和百分比。
        /// 拆成“名称 | 体积 | 百分比”三格，和物品详情的成分表共用一套列，数字各自对齐。
        private static void LiquidTables(List<Block> blocks)
        {
            for (int b = 0; b < blocks.Count; b++)
            {
                Block bl = blocks[b];
                if (bl.Key != Sections.Liquid) continue;
                for (int i = 0; i < bl.Rows.Count; i++)
                {
                    Row row = bl.Rows[i];
                    if (!row.IsPair || row.Cells != null || row.Label.Count == 0) continue;
                    bool bar = false;
                    for (int k = 0; k < row.Value.Count; k++) if (row.Value[k].Bar != null) bar = true;
                    if (bar) continue;
                    List<List<Unit>> cells = TextUnits.SplitCells(TextUnits.Of(row.Value));
                    if (cells == null) continue;
                    bool numbers = true;
                    for (int k = 0; k < cells.Count; k++) if (!TextRules.StartsWithNumber(TextUnits.Plain(cells[k]).Trim())) numbers = false;
                    if (!numbers) continue;
                    row.Cells = new List<List<Seg>>(cells.Count + 1);
                    row.Cells.Add(row.Label);
                    for (int k = 0; k < cells.Count; k++) row.Cells.Add(TextUnits.ToSegs(cells[k], row.Value));
                    row.Segs = new List<Seg>(row.Label);
                    Seg space = new Seg();
                    space.Text = " ";
                    row.Segs.Add(space);
                    row.Segs.AddRange(row.Value);
                    row.Label = null;
                    row.Value = null;
                }
            }
        }

        /// 同一节被别的节隔开（酒瓶的“水”和“果酿”中间夹了附加信息）时并回第一段，
        /// 否则估价单上会出现两个同名标题。标题、描述不并：它们本来就按顺序分开。
        private static void MergeSplitSections(List<Block> blocks)
        {
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                Block later = blocks[i];
                if (later.Key == Sections.Title || later.Key == Sections.Desc) continue;
                for (int j = 0; j < i; j++)
                {
                    Block first = blocks[j];
                    if (first.Key != later.Key) continue;
                    first.Rows.AddRange(later.Rows);
                    blocks.RemoveAt(i);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- 标签和数值拆列

        /// 这一行要不要拆成多组“名字 +N%”；标题、描述、价格、标签行不拆。
        private static List<string[]> PercentRows(Line l)
        {
            string key = l.Key;
            if (key == Sections.Title || key == Sections.Desc || key == Sections.Value || key == Sections.Label) return null;
            return TextRules.PercentPairs(l.Plain);
        }

        /// <summary>
        /// “市场：非法武器走私货物 -5%、日用品救济行动 -3%”：几个事件挤在一个数值里会把单子撑得很宽。
        /// 每一项都是“名字 ±N%”时拆成一项一行，后面几行不写标签，接在数值列里，颜色照旧。
        /// </summary>
        private static void SplitPercentList(Block cur, Row row)
        {
            List<Seg> source = row.Value;
            List<Unit> u = TextUnits.Of(source);
            List<List<Unit>> items = new List<List<Unit>>();
            int from = 0;
            for (int i = 0; i <= u.Count; i++)
            {
                if (i < u.Count && (u[i].Tag || (u[i].Text != "、" && u[i].Text != "，" && u[i].Text != ","))) continue;
                List<Unit> item = TextUnits.TrimEnd(TextUnits.TrimStart(u.GetRange(from, i - from)));
                string label, value;
                if (!TextRules.TrailingPercent(TextUnits.Plain(item), out label, out value)) return;
                items.Add(item);
                from = i + 1;
            }
            if (items.Count < 2) return;
            row.Value = TextUnits.ToSegs(items[0], source);
            for (int k = 1; k < items.Count; k++)
            {
                Row more = new Row();
                more.Label = new List<Seg>();
                more.Value = TextUnits.ToSegs(items[k], source);
                more.Ord = row.Ord;
                cur.Rows.Add(more);
            }
        }

        /// “精工扩舱 0”“储水箱扩容 0/6”拆成一对，分数照样补进度条；拆不开返回 null。名字和数值各自保留原来的颜色。
        private static Row NameNumberRow(List<Unit> units, List<Seg> source)
        {
            List<Unit> u = TextUnits.TrimEnd(TextUnits.TrimStart(units));
            string name, number;
            if (!TextRules.NameNumber(TextUnits.Plain(u), out name, out number)) return null;
            Row r = SplitTail(u, number, source);
            if (r != null) AddFractionBar(r, number);
            return r;
        }

        /// 把一行在结尾那段 tail 前切成标签和数值；tail 是去掉首尾空白后文字的结尾。按单元切，两边都保留原来的颜色。切不开返回 null。
        private static Row SplitTail(List<Unit> units, string tail, List<Seg> source)
        {
            List<Unit> u = TextUnits.TrimEnd(TextUnits.TrimStart(units));
            int at = TextUnits.Plain(u).Length - tail.Length, cum = 0, split = -1;
            for (int i = 0; i < u.Count; i++)
            {
                if (cum == at && !u[i].Tag) { split = i; break; }
                if (!u[i].Tag) cum += u[i].Text.Length;
            }
            if (split <= 0) return null;
            Row r = new Row();
            r.Label = TextUnits.ToSegs(TextUnits.TrimEnd(u.GetRange(0, split)), source);
            r.Value = TextUnits.ToSegs(u.GetRange(split, u.Count - split), source);
            return r;
        }

        private static Row PairRow(string[] pair, int ord)
        {
            Row r = new Row();
            r.Wide = true;
            r.Ord = ord;
            r.Label = new List<Seg>();
            Seg label = new Seg();
            label.Text = pair[0];
            r.Label.Add(label);
            r.Value = new List<Seg>();
            Seg value = new Seg();
            value.Text = pair[1];
            r.Value.Add(value);
            return r;
        }

        private static Row Plain(Line l)
        {
            Row r = new Row();
            r.Segs = l.Segs;
            return r;
        }

        /// pairs 关掉用于句子行：这种行不做标签拆列，只处理项目符号和标题。
        private static Row MakeRow(Line l, int maxLabel, bool pairs)
        {
            List<Unit> u = TextUnits.Of(l.Segs);
            u = StripBullet(TextUnits.TrimStart(u));

            int split = -1, valueStart = -1, depth = 0;
            for (int k = 0; k < u.Count; k++)
            {
                if (u[k].Tag) continue;
                string t = u[k].Text;
                // 括号里的冒号是对数值的注解（“效率 +19% (基础: +16%)”），不是标签的结尾。
                if (t == "(" || t == "（" || t == "[" || t == "【") depth++;
                else if ((t == ")" || t == "）" || t == "]" || t == "】") && depth > 0) depth--;
                else if (depth == 0 && (t == ":" || t == "：")) { split = k; valueStart = k + 1; break; }
            }
            if (split < 0)
            {
                // “[上次扫描] 99.09% 优质水”：方括号里的内容就是标签。但 “[==    ] 1713 ml” 是进度条，不算。
                int open = -1, close = -1;
                bool wordy = false;
                for (int k = 0; k < u.Count; k++)
                {
                    if (u[k].Tag) continue;
                    if (open < 0)
                    {
                        if (u[k].Text != "[") break;
                        open = k;
                        continue;
                    }
                    if (u[k].Text == "]") { close = k; break; }
                    if (u[k].Wide || char.IsLetter(u[k].Text[0])) wordy = true;
                }
                if (open >= 0 && close > open && wordy && close + 1 < u.Count) { split = close + 1; valueStart = close + 1; }
            }
            bool hasBar = false;
            if (split < 0)
            {
                // “能量 [=====   ] (76/100kJ)”：进度条前面的部分就是它的标签。
                bool seenText = false;
                for (int k = 0; k < u.Count; k++)
                {
                    if (l.Segs[u[k].Seg].Bar != null)
                    {
                        hasBar = true;
                        if (seenText) { split = k; valueStart = k; }
                        break;
                    }
                    if (!u[k].Tag && !u[k].Space) seenText = true;
                }
            }
            List<List<Unit>> cells = null;
            if (split < 0 && pairs && Settings.IsTicket && !hasBar && l.Key != Sections.Value)
                cells = TextUnits.SplitCells(u);

            // 三列以上是表格；只有两格时仍优先认游戏的“标签    数值”。
            if (split < 0 && (cells == null || cells.Count == 2))
            {
                // 游戏用空格对齐的列：“标签    数值”。
                bool seenText = false;
                for (int k = 0; k + 1 < u.Count; k++)
                {
                    if (!u[k].Tag && !u[k].Space) seenText = true;
                    if (seenText && u[k].Space && u[k + 1].Space && !u[k].Tag && !u[k + 1].Tag) { split = k; valueStart = k; break; }
                }
            }

            // 没有冒号、但行尾是“+25%”这种：前面的短名字当标签（“种子产量 +25%”）。
            // 已经是多列的制表位行就不抢：那样会把列结构拆掉。
            if (split <= 0 && pairs && cells == null)
            {
                string tailLabel, tailValue;
                if (TextRules.TrailingPercent(TextUnits.Plain(u), out tailLabel, out tailValue))
                {
                    // 按单元切：DNA 盘、种子效果整行是游戏给的绿或红，拆开后两边都要带着。
                    Row pr = SplitTail(u, tailValue, l.Segs);
                    if (pr != null) return pr;
                }
            }

            if (split > 0)
            {
                List<Unit> label = TextUnits.TrimEnd(u.GetRange(0, split));
                List<Unit> value = TextUnits.TrimStart(u.GetRange(valueStart, u.Count - valueStart));
                string lp = TextUnits.Plain(label).Trim();
                string vp = TextUnits.Plain(value).Trim();
                // 冒号两边都是数字的是时间、比例（“12:30”），不拆；“繁殖力强1: 种子产量 +25%”（写入种子的特性，名字带等级）要拆。
                bool numeric = lp.Length > 0 && vp.Length > 0 && char.IsDigit(lp[lp.Length - 1]) && char.IsDigit(vp[0]);
                if (pairs && lp.Length > 0 && vp.Length > 0 && TextUnits.Width(label) <= maxLabel && !numeric)
                {
                    Row pair = new Row();
                    pair.Label = Recolor(TextUnits.ToSegs(label, l.Segs), Palette.LightGray);
                    pair.Value = Recolor(TextUnits.ToSegs(value, l.Segs), Palette.None);
                    AddFractionBar(pair, vp);
                    return pair;
                }
                if (lp.Length > 0 && vp.Length == 0 && valueStart == split + 1 && TextUnits.Width(label) <= maxLabel)
                {
                    Row heading = new Row();
                    heading.Heading = true;
                    heading.Label = TextUnits.ToSegs(label, l.Segs);
                    return heading;
                }
            }

            Row r = new Row();
            r.Segs = TextUnits.ToSegs(u, l.Segs);
            if (cells != null)
            {
                r.Cells = new List<List<Seg>>(cells.Count);
                for (int k = 0; k < cells.Count; k++) r.Cells.Add(TextUnits.ToSegs(cells[k], l.Segs));
            }
            return r;
        }

        private const int FractionBarCells = 8;

        /// “400/400”、“3 / 5”、“20.23/12 kg”：分数的数值在前面加一条进度条，按装满程度上色。数字还是那些，没多显示什么。
        private static void AddFractionBar(Row pair, string value)
        {
            if (Settings.Bars != null && !Settings.Bars.Value) return;
            if (Settings.FractionBars != null && !Settings.FractionBars.Value) return;
            for (int i = 0; i < pair.Value.Count; i++) if (pair.Value[i].Bar != null) return;

            float a, b;
            if (!TextRules.ReadFraction(value, LocTemplates.Known, LocTemplates.Prefix, LocTemplates.Mid, out a, out b)) return;
            float ratio = a / b;

            BarMark m = new BarMark();
            m.Ratio = Math.Max(0f, Math.Min(1f, ratio));
            m.Color = ratio > 1.0001f || ratio < 0.3f ? Palette.LightRed : ratio < 0.6f ? Palette.Yellow : Palette.LightGreen;
            m.Background = Palette.VeryDarkGray;

            Seg bar = new Seg();
            bar.Text = TextUnits.BarPlaceholder(FractionBarCells);
            bar.Bar = m;
            Seg gap = new Seg();
            gap.Text = TextUnits.Pad(TextUnits.Cell);
            pair.Value.Insert(0, gap);
            pair.Value.Insert(0, bar);
        }

        private static List<Unit> StripBullet(List<Unit> u)
        {
            int first = -1;
            for (int k = 0; k < u.Count; k++) if (!u[k].Tag) { first = k; break; }
            if (first < 0 || first + 1 >= u.Count) return u;
            string t = u[first].Text;
            if (!(t == "-" || t == "*" || t == "•" || t == "·" || t == ">")) return u;
            Unit after = u[first + 1];
            // “- 能量”和“-能量”是列表符号；“-5%”是数字。
            bool bullet = after.Space || (!after.Tag && after.Text.Length == 1 && (char.IsLetter(after.Text[0]) || after.Wide));
            if (!bullet) return u;
            List<Unit> r = new List<Unit>(u);
            r.RemoveRange(first, after.Space ? 2 : 1);
            return r;
        }

        private static List<Seg> Recolor(List<Seg> segs, Palette fallback)
        {
            if (fallback == Palette.None) return segs;
            List<Seg> r = new List<Seg>(segs.Count);
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (s.Color == Palette.None) { s = s.With(s.Text); s.Color = fallback; }
                r.Add(s);
            }
            return r;
        }
    }
}
