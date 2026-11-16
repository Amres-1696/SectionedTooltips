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
    /// 估价单的上半张：从排版结果里取出名称、价格、赃物和违禁品标记这些（Parts），画标题和标签、大字价格、价格明细。
    internal static partial class Ticket
    {
        /// 估价单视角下的各节：名称、名称下的小行、赃物和违禁品标记、价格、描述。
        private sealed class Parts
        {
            public Row Name;
            /// 名称下面的行（厂商、标签、备注），按游戏给的顺序。
            public readonly List<Row> Extras = new List<Row>();
            /// 对应 Extras 每一行：是“[机器]”那行标签时给出分类标签，否则为 null。
            public readonly List<List<Chip>> Chips = new List<List<Chip>>();
            /// 违禁品标记（“中级违禁品”），和赃物一样画成名字下面的红色标签。
            public readonly List<string> Contraband = new List<string>();
            /// 游戏的赃物标记（“[赃物（14%热度）]”）；不是赃物时为 null。
            public string Stolen;
            /// 游戏的“未拥有”标记，去掉方括号；自己物品时为 null。
            public string NotOwned;
            /// 游戏的标签行去掉“[标签]”后的文字（“破旧 -75%”）：画在名字下面，不单独占一节。
            public readonly List<string> Features = new List<string>();
            public Row Price;
            /// 价格后面括号里的内容（“进价 250+722”）。
            public List<Seg> NoteSegs = new List<Seg>();
            /// 价格注解的富文本，用当前纸的墨色。
            public string PriceNote { get { return NoteRich(NoteSegs, Price != null ? LabelColor(Price.Label) : Palette.None); } }
            public readonly List<Row> Prices = new List<Row>();
            public readonly List<Row> Desc = new List<Row>();

            public static Parts From(LayoutResult r)
            {
                Parts p = new Parts();
                for (int i = 0; i < r.Blocks.Count; i++)
                {
                    Block bl = r.Blocks[i];
                    for (int j = 0; j < bl.Rows.Count; j++)
                    {
                        Row row = bl.Rows[j];
                        if (bl.Key == Sections.Title)
                        {
                            string plain = row.Plain.Trim();
                            if (p.Name == null)
                            {
                                string flag;
                                p.Name = SplitNotOwned(row, out flag);
                                if (flag != null) p.NotOwned = flag.Trim('(', ')', '（', '）', '[', ']', '【', '】', ' ');
                            }
                            else if (row.Ord >= 0 && (row.Ord == Helpers.OrdStolen || row.Ord == Helpers.OrdContraband))
                            {
                                int end = FlagEnd(plain);
                                if (row.Ord == Helpers.OrdStolen) p.Stolen = plain.Substring(0, end);
                                else p.Contraband.Add(plain.Substring(0, end));
                                // 游戏写赃物标记用的是 Add 而不是 AddLine，所以后面跟的内容和它同一行：通常是物品标签，没有标签时是下一行。两种都按没有标记的样子显示。
                                if (plain.Substring(end).Trim().Length > 0)
                                {
                                    Row rest = DropFront(row, end);
                                    if (rest != null) p.Extras.Add(rest);
                                }
                            }
                            else p.Extras.Add(row);
                        }
                        else if (bl.Key == Sections.Value)
                        {
                            if (p.Price == null && row.IsPair && LeadingNumber(TextUnits.Plain(row.Value)).Length > 0)
                            {
                                p.Price = row;
                                List<Seg> note, tail;
                                SplitPrice(row.Value, out note, out tail);
                                // “预估价值：1478，基础价值：1478”：没有括号，逗号后面又是另一个值。
                                string notePlain = TextUnits.Plain(note).Trim().TrimStart('，', ',').Trim();
                                if (tail.Count == 0 && notePlain.IndexOfAny(new char[] { '（', '(' }) < 0 && PairFromPlain(notePlain) != null)
                                {
                                    tail = note;
                                    note = new List<Seg>();
                                }
                                p.NoteSegs = note;
                                // “售价：50（进价 50） 标价：50”：括号后面的标价单独占一行。
                                Row extra = PairFromPlain(TextUnits.Plain(tail).Trim().TrimStart('，', ','));
                                // 和售价一样的标价等于没说。
                                if (extra != null && !SamePrice(p, extra)) p.Prices.Add(extra);
                            }
                            // 或者在下一行（“售价：650（进价 585+65）” / “标价：650”）。
                            else if (!(p.Prices.Count == 0 && j > 0 && ReferenceEquals(bl.Rows[j - 1], p.Price) && SamePrice(p, row))) p.Prices.Add(row);
                        }
                        else if (bl.Key == Sections.Desc) p.Desc.Add(row);
                        else if (bl.Key == Sections.Label)
                        {
                            string f = FeatureOf(row);
                            if (f.Length > 0) p.Features.Add(f);
                        }
                    }
                }
                return p;
            }
        }

        /// 游戏把“未拥有”标记直接写在名称前面，中间没有换行。返回去掉标记的名称行（是副本：原行属于缓存的排版）和标记文本；是自己的物品就原样返回，标记为 null。
        private static Row SplitNotOwned(Row row, out string flag)
        {
            flag = null;
            if (row.Segs == null) return row;
            string mark;
            try { mark = TextUnits.Plain(LocHelper.GetLocalizedMechanic("mech_tooltip_not_owned") ?? "").Trim(); }
            catch { return row; }
            if (mark.Length == 0 || !TextUnits.Plain(row.Segs).TrimStart().StartsWith(mark, StringComparison.Ordinal)) return row;
            Row name = DropFront(row, mark.Length);
            if (name == null) return row;
            flag = mark;
            return name;
        }

        /// row 是不是只是把大字价格又说了一遍（“售价：1680”下面的“标价：1680”）。
        private static bool SamePrice(Parts p, Row row)
        {
            return p.Price != null && row.IsPair && TextUnits.Plain(row.Value).Trim() == LeadingNumber(TextUnits.Plain(p.Price.Value));
        }

        /// 去掉游戏“[标签]”前缀后的标签行：“破旧 -75%”。
        private static string FeatureOf(Row row)
        {
            string plain = (row.IsPair ? TextUnits.Plain(row.Label) + " " + TextUnits.Plain(row.Value) : row.Plain).Trim();
            string mark;
            try { mark = TextUnits.Plain(LocHelper.GetLocalizedMechanic("mech_label_tooltip_prefix") ?? "").Trim(); }
            catch { mark = ""; }
            if (mark.Length > 0 && plain.StartsWith(mark, StringComparison.Ordinal)) plain = plain.Substring(mark.Length).Trim();
            return plain;
        }

        /// 一行里的多个标签，“破旧 -75%  ·  …”，价格打折的部分用红色。
        private static string FeaturesRich(Parts p)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < p.Features.Count; i++)
            {
                if (i > 0) sb.Append(Tint("  ·  ", InkSoft));
                string f = p.Features[i];
                int space = f.LastIndexOf(' ');
                string tail = space > 0 ? f.Substring(space + 1) : "";
                if (tail.Length > 1 && (tail[0] == '-' || tail[0] == '−') && tail.EndsWith("%"))
                    sb.Append(Tint(f.Substring(0, space), InkSoft)).Append(' ').Append(Tint(tail, FlagRed));
                else sb.Append(Tint(f, InkSoft));
            }
            return sb.ToString();
        }

        /// row 的副本（原行属于缓存的排版），去掉开头 count 个字符（不计前导空格）；没剩下内容时返回 null。从前面一段段丢，跨在切点上的那段保留后半部分。
        private static Row DropFront(Row row, int count)
        {
            if (row.Segs == null) return null;
            int left = count;
            List<Seg> rest = new List<Seg>();
            for (int i = 0; i < row.Segs.Count; i++)
            {
                Seg s = row.Segs[i];
                if (left <= 0 || s.Bar != null) { rest.Add(s); continue; }
                string t = TextUnits.Plain(s.Text);
                if (rest.Count == 0 && left == count) t = t.TrimStart();
                if (t.Length <= left) { left -= t.Length; continue; }
                rest.Add(s.With(t.Substring(left).TrimStart()));
                left = 0;
            }
            if (TextUnits.Plain(rest).Trim().Length == 0) return null;
            Row r = new Row();
            r.Segs = rest;
            r.Ord = row.Ord;
            return r;
        }

        private static float Title(Ctx c, Parts p, GameItemElement item, float y)
        {
            if (p.Name == null) return y;
            float boxSize = IconBox(c);
            float x = c.Pad;
            Sprite icon = IconFor(item);
            if (icon != null)
            {
                Solid(c, PaperShade, x, y, boxSize, boxSize);
                PlaceIcon(c, icon, x, y, boxSize, 2f * c.U);
                x += boxSize + c.Gap * 1.5f;
            }

            float wrap = c.W - c.Pad - x;
            float ty = y;
            TextMeshProUGUI name = Text(c, c.F * 1.15f, InkRich(p.Name.Segs, Ink), Color.white, wrap);
            Ui.Move(name.rectTransform, x, ty);
            ty += name.rectTransform.sizeDelta.y;
            if (p.NotOwned != null)
            {
                // 粉色纸已经说明了，这行只是平静地写出名字，用纸本身的墨。
                TextMeshProUGUI t = Text(c, c.F * 0.8f, "<cspace=0.2em>" + p.NotOwned + "</cspace>", InkSoft, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            for (int i = 0; i < p.Extras.Count; i++)
            {
                if (p.Chips[i] != null) { ty = DrawChips(c, p.Chips[i], x, ty, wrap); continue; }
                TextMeshProUGUI t = Text(c, c.F * 0.8f, InkRich(p.Extras[i].Segs, InkSoft), Color.white, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            if (p.Features.Count > 0)
            {
                TextMeshProUGUI t = Text(c, c.F * 0.8f, FeaturesRich(p), Color.white, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            // 最后画，这样赃物、违禁品和普通物品看起来完全一样，只多一排红色标签。
            ty = FlagTags(c, Flags(p), x, ty, wrap);
            return Mathf.Max(y + (icon != null ? boxSize : 0f), ty) + c.Gap * 1.5f;
        }

        /// 售价写大，价值一节其余的行画成带点线引导的小行。
        private static float Price(Ctx c, Parts p, float y)
        {
            float small = c.F * 0.75f;
            Row perCell = PerCellRow(p);
            if (p.Price != null)
            {
                // “售价”用小字放在数字前面、和注解一样贴着数字底部，不单独占一行。
                TextMeshProUGUI label = Text(c, small, InkRich(p.Price.Label, InkSoft), Color.white, 0f);
                Vector2 ls = label.rectTransform.sizeDelta;
                float bx = c.Pad + ls.x + c.Gap;
                TextMeshProUGUI big = Text(c, PriceSize(c), BigPrice(p), Ink, 0f);
                Ui.Move(big.rectTransform, bx, y);
                Vector2 bs = big.rectTransform.sizeDelta;
                Ui.Move(label.rectTransform, c.Pad, y + bs.y - ls.y - c.U);
                // 大字价格右边：进价，下面是性价比（常驻，不收起：它是估价单最要紧的数，收起再展开时下面的内容也不会跳）。两行一起贴着数字底部。
                // 性价比不撑宽单子：右边放不下就在价格下面单独一行。
                string note = p.PriceNote;
                float bottom = y + bs.y - c.U;
                float room = c.W - c.Pad - (bx + bs.x + c.Gap);
                if (perCell != null && c.Measure(PerCellRich(perCell), small) <= room)
                {
                    TextMeshProUGUI pc = Text(c, small, PerCellRich(perCell), Color.white, 0f);
                    bottom -= pc.rectTransform.sizeDelta.y;
                    Ui.Move(pc.rectTransform, bx + bs.x + c.Gap, bottom);
                    perCell = null;
                }
                if (note.Length > 0)
                {
                    TextMeshProUGUI n = Text(c, small, note, Color.white, 0f);
                    Ui.Move(n.rectTransform, bx + bs.x + c.Gap, bottom - n.rectTransform.sizeDelta.y);
                }
                y += bs.y + c.Gap;
            }
            if (perCell != null)
            {
                // 价格右边放不下的性价比：单独一行，照样常驻。
                List<string> own = new List<string>();
                own.Add(PerCellRich(perCell));
                y = PriceLine(c, own, y);
            }
            perCell = PerCellRow(p);
            // 大字价格下面的其他价格是明细：合成一行小字，“标价 100 · 性价比 11.1（100/9格）”。这一节里别的东西（比如“已上锁”这种标记）还是各占一行，按顺序。
            List<string> line = new List<string>();
            for (int i = 0; i < p.Prices.Count; i++)
            {
                Row row = p.Prices[i];
                if (!ShowPriceRow(p, row) || ReferenceEquals(row, perCell)) continue;
                if (row.IsPair && p.Price != null && BarOf(row.Value) == null)
                {
                    if (c.FoldPrices)
                    {
                        Folded(c, "fold_prices");
                        continue;
                    }
                    line.Add(InkRich(row.Label, InkSoft) + " " + InkRich(row.Value, Ink));
                    continue;
                }
                y = PriceLine(c, line, y);
                y = RowLine(c, row, y, c.Pad);
            }
            return PriceLine(c, line, y);
        }

        /// 价格明细里的“性价比 89.6（806/9格）”：画在进价下面。没有大字价格、或关了价格明细时为 null。
        private static Row PerCellRow(Parts p)
        {
            if (p.Price == null) return null;
            for (int i = 0; i < p.Prices.Count; i++)
            {
                Row row = p.Prices[i];
                if (ShowPriceRow(p, row) && row.IsPair && BarOf(row.Value) == null && TextRules.PerCellValue(TextUnits.Plain(row.Value))) return row;
            }
            return null;
        }

        private static string PerCellRich(Row row)
        {
            return InkRich(row.Label, InkSoft) + " " + InkRich(row.Value, Ink);
        }

        /// 把攒起来的价格明细写出去并清空。返回底部位置。
        private static float PriceLine(Ctx c, List<string> line, float y)
        {
            if (line.Count == 0) return y;
            float size = c.F * 0.8f;
            string dot = "<color=#" + ColorUtility.ToHtmlStringRGB(InkSoft) + ">  ·  </color>";
            // 一行放不下时在“·”处换行，每一项（“性价比 1051.6（16826/16格）”）不拆开；一项本身就比单子宽时才在项里折行。
            for (int i = 0; i < line.Count; )
            {
                string rich = line[i];
                int j = i + 1;
                for (; j < line.Count; j++)
                {
                    string more = rich + dot + line[j];
                    if (c.Measure(more, size) > c.Inner) break;
                    rich = more;
                }
                TextMeshProUGUI t = Text(c, size, rich, Color.white, c.Measure(rich, size) > c.Inner ? c.Inner : 0f);
                Ui.Move(t.rectTransform, c.Pad, y);
                y += t.rectTransform.sizeDelta.y + c.U;
                i = j;
            }
            line.Clear();
            return y;
        }

        /// 关掉价格明细时只留大字价格：其他的“标签 …… 数值”行不要，但同一节里的标记（“已上锁”）留着；没有大字价格可留时全都留。
        private static bool ShowPriceRow(Parts p, Row row)
        {
            return Settings.On(Settings.ShowPriceDetails) || p.Price == null || !row.IsPair;
        }

        /// 价格写大并加粗；多大由 PriceSize 设置决定。
        private static float PriceSize(Ctx c)
        {
            return c.F * c.PriceScale;
        }

        private static string BigPrice(Parts p)
        {
            return "<b>" + LeadingNumber(TextUnits.Plain(p.Price.Value)) + "</b>";
        }

        /// 把“[赃物（14%热度）]”这样的标记拆成“赃物”和“14%热度”（没有括号时后半为空）。游戏可能把物品标签写在同一行（“[赃物（14%热度）] [麻醉品] [医疗品]”）；标记在自己的右括号处结束，后面的标签丢掉。
        private static void SplitFlag(string text, out string main, out string sub)
        {
            main = text.Trim();
            main = main.Substring(0, FlagEnd(main)).Trim().Trim('[', ']', '【', '】').Trim();
            sub = "";
            int cut = main.IndexOfAny(new char[] { '（', '(' });
            if (cut > 0)
            {
                sub = main.Substring(cut + 1).TrimEnd('）', ')').Trim();
                main = main.Substring(0, cut).Trim();
            }
        }

        /// 行首的标记到哪结束：到它自己的右括号；没有自己括号的标记，到第一个方括号标签之前。
        private static int FlagEnd(string plain)
        {
            if (plain.Length > 0 && (plain[0] == '[' || plain[0] == '【'))
            {
                int close = plain.IndexOfAny(new char[] { ']', '】' });
                return close >= 0 ? close + 1 : plain.Length;
            }
            int open = plain.IndexOfAny(new char[] { '[', '【' });
            return open > 0 ? open : plain.Length;
        }

        /// 赃物、违禁品标签的文字：“赃物  14%热度”、“中级违禁品”。
        private static string FlagLabel(string text)
        {
            string main, sub;
            SplitFlag(text, out main, out sub);
            return "<b>" + (sub.Length > 0 ? main + "<space=0.6em>" + sub : main) + "</b>";
        }

        /// 名字下面的红色实心标签：赃物在前，违禁品在后。
        private static List<string> Flags(Parts p)
        {
            List<string> r = new List<string>();
            if (p.Stolen != null) r.Add(p.Stolen);
            r.AddRange(p.Contraband);
            return r;
        }

        private const float FlagSize = 0.75f;

        /// 一个标签的宽度：文字加两边内边距。
        private static float FlagWidth(Ctx c, string text)
        {
            return c.Measure(FlagLabel(text), c.F * FlagSize) + 6f * c.U;
        }

        /// 所有标签排一行的宽度。
        private static float FlagsWidth(Ctx c, List<string> flags)
        {
            float w = 0f;
            for (int i = 0; i < flags.Count; i++) w += FlagWidth(c, flags[i]) + (i > 0 ? 2f * c.U : 0f);
            return w;
        }

        /// 赃物、违禁品在名字下面各盖一个小红色实心标签，排成一行，放不下就折行。不用印章：印章的方框看着像按钮。返回它们下面的 y。
        private static float FlagTags(Ctx c, List<string> flags, float x, float y, float wrap)
        {
            if (flags.Count == 0) return y;
            float px = 3f * c.U, py = c.U, top = 2f * c.U, gap = 2f * c.U;
            float cx = x, cy = y + top, rowH = 0f;
            for (int i = 0; i < flags.Count; i++)
            {
                TextMeshProUGUI t = Text(c, c.F * FlagSize, FlagLabel(flags[i]), Paper, 0f);
                Vector2 ts = t.rectTransform.sizeDelta;
                float w = ts.x + 2f * px, h = ts.y + 2f * py;
                if (cx > x && wrap > 0f && cx + w > x + wrap)
                {
                    cx = x;
                    cy += rowH + gap;
                    rowH = 0f;
                }
                Solid(c, FlagRed, cx, cy, w, h);
                Ui.Move(t.rectTransform, cx + px, cy + py);
                cx += w + gap;
                rowH = Mathf.Max(rowH, h);
            }
            return cy + rowH;
        }

        /// 违禁品的单子左边一道整高的红边：一排货扫过去，余光就能认出来，单子里的内容不变。
        private static void ContrabandEdge(Ctx c, float h)
        {
            Color red = FlagRed;
            red.a = 1f;
            Solid(c, red, c.U, c.U, Mathf.Max(3f * c.U, Mathf.Round(c.Pad * 0.4f)), h - 2f * c.U);
        }

        /// 单子上最小的字号相对正文的比例，抬字号时按它算。实际最小的是页脚单号和格子堆叠数（0.65），这里留一点余量。
        private const float MinFontShare = 0.6f;

        private static string LeadingNumber(string s)
        {
            s = s.Trim();
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == ',' || (end == 0 && s[end] == '-'))) end++;
            return end > 0 && char.IsDigit(s[end - 1]) ? s.Substring(0, end) : "";
        }

        /// 把价格数值“50（进价 50） 标价：50”拆成数字后面到右括号之前的部分（note），以及之后的部分（tail），颜色保留。
        private static void SplitPrice(List<Seg> value, out List<Seg> note, out List<Seg> tail)
        {
            note = new List<Seg>();
            tail = new List<Seg>();
            bool numberCut = false, inTail = false;
            for (int i = 0; i < value.Count; i++)
            {
                Seg s = value[i];
                if (s.Bar != null) continue;
                if (inTail) { tail.Add(s); continue; }
                string t = s.Text;
                if (!numberCut && t.Trim().Length > 0)
                {
                    t = t.TrimStart();
                    t = t.Substring(LeadingNumber(t).Length);
                    numberCut = true;
                }
                int close = t.IndexOfAny(new char[] { '）', ')' });
                if (close >= 0)
                {
                    note.Add(s.With(t.Substring(0, close + 1)));
                    string rest = t.Substring(close + 1);
                    if (rest.Trim().Length > 0) tail.Add(s.With(rest));
                    inTail = true;
                    continue;
                }
                if (t.Length > 0) note.Add(s.With(t));
            }
        }

        /// “标价：50”转成标签/数值行，转不了返回 null。
        internal static Row PairFromPlain(string plain)
        {
            plain = plain.Trim();
            int colon = plain.IndexOfAny(new char[] { ':', '：' });
            if (colon <= 0 || colon >= plain.Length - 1) return null;
            Seg label = new Seg();
            label.Text = plain.Substring(0, colon).Trim();
            Seg value = new Seg();
            value.Text = plain.Substring(colon + 1).Trim();
            Row row = new Row();
            row.Label = new List<Seg> { label };
            row.Value = new List<Seg> { value };
            return row;
        }

        /// 有字、没有数字的一段（“进价”），不是“74”“+783”。
        private static bool Wordy(string t)
        {
            bool letter = false;
            for (int i = 0; i < t.Length; i++)
            {
                if (char.IsDigit(t[i])) return false;
                if (char.IsLetter(t[i])) letter = true;
            }
            return letter;
        }

        /// 标签里第一个带颜色的段的颜色；都没颜色返回 None。
        private static Palette LabelColor(List<Seg> label)
        {
            if (label == null) return Palette.None;
            for (int i = 0; i < label.Count; i++) if (label[i].Color != Palette.None) return label[i].Color;
            return Palette.None;
        }

        /// 价格后面括号里的注解，转成墨色、保留颜色、去掉括号。注解里的字（“进价”）和“售价”用同一个颜色，不然一个有色一个没色；数字照游戏给的颜色。
        private static string NoteRich(List<Seg> note, Palette labelColor)
        {
            List<Seg> rest = new List<Seg>();
            for (int i = 0; i < note.Count; i++)
            {
                Seg s = note[i];
                if (labelColor != Palette.None && Wordy(s.Text))
                {
                    s = s.With(s.Text);
                    s.Color = labelColor;
                }
                if (rest.Count == 0)
                {
                    string t = s.Text.TrimStart();
                    if (t.Length == 0) continue;
                    s = s.With(t);
                }
                // “进价 350-140”会读成“350 到 140”：盈亏前面空一格，“350 -140”。
                else if (s.Text.Length > 1 && (s.Text[0] == '+' || s.Text[0] == '-') && char.IsDigit(s.Text[1])) s = s.With(" " + s.Text);
                rest.Add(s);
            }
            string rich = InkRich(rest, InkSoft);
            string plain = TextUnits.Plain(rich).Trim();

            if (plain.Length >= 2 && (plain[0] == '（' || plain[0] == '('))
            {
                int open = rich.IndexOfAny(new char[] { '（', '(' });
                int close = rich.LastIndexOfAny(new char[] { '）', ')' });
                if (open >= 0 && close > open) rich = rich.Remove(close, 1).Remove(open, 1);
            }
            return rich;
        }
    }
}
