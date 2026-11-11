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
    /// 估价单的上半张：从排版结果里取出名称、价格、印章这些（Parts），画标题、大字价格、价格明细和印章。
    internal static partial class Ticket
    {
        /// 估价单视角下的各节：名称、名称下的小行、印章、价格、描述。
        private sealed class Parts
        {
            public Row Name;
            /// 名称下面的行（厂商、标签、备注），按游戏给的顺序。
            public readonly List<Row> Extras = new List<Row>();
            /// 对应 Extras 每一行：是“[机器]”那行标签时给出分类标签，否则为 null。
            public readonly List<List<Chip>> Chips = new List<List<Chip>>();
            public readonly List<string> Stamps = new List<string>();
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
            public string PriceNote { get { return NoteRich(NoteSegs); } }
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
                                else p.Stamps.Add(plain.Substring(0, end));
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
                    sb.Append(Tint(f.Substring(0, space), InkSoft)).Append(' ').Append(Tint(tail, StampRed));
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

        /// 第一个印章横向占多宽（尺寸和 Stamp 一致）。
        private static float StampSpan(Ctx c, string text)
        {
            float sw, sh;
            StampSize(c, text, out sw, out sh);
            return sw;
        }

        /// 印章从它的行往下伸多深：Stamp 摆放时的下移量，加上它的高度。
        private static float StampDrop(Ctx c, string text, int index)
        {
            float sw, sh;
            StampSize(c, text, out sw, out sh);
            return c.Gap + index * 2f * c.U + sh;
        }

        /// 印章的外框尺寸，用和 Stamp 一样的字体和边框估算（宁可多一点，不会少）。
        private static void StampSize(Ctx c, string text, out float sw, out float sh)
        {
            string main, sub;
            SplitFlag(text, out main, out sub);
            float w = c.Measure("<b>" + main + "</b>", c.F * StampMain);
            if (sub.Length > 0) w = Mathf.Max(w, c.Measure(sub, c.F * StampSub));
            sw = w + 2f * StampEdge * c.U;
            sh = c.M.Line * (sub.Length > 0 ? 1.6f : 1f) + 2f * StampEdge * c.U;
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
                TextMeshProUGUI t = Text(c, c.F * 0.8f, InkRich(p.Extras[i].Segs, InkSoft, false), Color.white, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            if (p.Features.Count > 0)
            {
                TextMeshProUGUI t = Text(c, c.F * 0.8f, FeaturesRich(p), Color.white, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            // 最后画，这样赃物和非赃物看起来完全一样，只多一个标签。
            if (p.Stolen != null) ty = StolenTag(c, p.Stolen, x, ty);
            return Mathf.Max(y + (icon != null ? boxSize : 0f), ty) + c.Gap * 1.5f;
        }

        /// 售价写大，价值一节其余的行画成带点线引导的小行。
        private static float Price(Ctx c, Parts p, float y, float clear)
        {
            float small = c.F * 0.75f;
            Row perCell = PerCellRow(p);
            if (p.Price != null)
            {
                // “售价”用小字放在数字前面、和注解一样贴着数字底部，不单独占一行。
                TextMeshProUGUI label = Text(c, small, InkRich(p.Price.Label, InkSoft, false), Color.white, 0f);
                Vector2 ls = label.rectTransform.sizeDelta;
                float bx = c.Pad + ls.x + c.Gap;
                TextMeshProUGUI big = Text(c, PriceSize(c), BigPrice(p), Ink, 0f);
                Ui.Move(big.rectTransform, bx, y);
                Vector2 bs = big.rectTransform.sizeDelta;
                Ui.Move(label.rectTransform, c.Pad, y + bs.y - ls.y - c.U);
                // 大字价格右边：进价，下面是性价比（常驻，不收起：它是估价单最要紧的数，收起再展开时下面的内容也不会跳）。两行一起贴着数字底部。
                // 性价比不撑宽单子：右边放不下（印章也在这一行）就在价格下面单独一行。
                string note = p.PriceNote;
                float bottom = y + bs.y - c.U;
                float room = c.W - c.Pad - (bx + bs.x + c.Gap);
                if (p.Stamps.Count > 0) room -= 2f * c.Gap + StampSpan(c, p.Stamps[0]);
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
            y = Mathf.Max(y, clear);
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
                    line.Add(InkRich(row.Label, InkSoft, false) + " " + InkRich(row.Value, Ink, false));
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
            return InkRich(row.Label, InkSoft, false) + " " + InkRich(row.Value, Ink, false);
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

        /// 赃物标签的文字：“赃物  14%热度”。
        private static string StolenLabel(string text)
        {
            string main, sub;
            SplitFlag(text, out main, out sub);
            return "<b>" + (sub.Length > 0 ? main + "<space=0.6em>" + sub : main) + "</b>";
        }

        /// 赃物在名字下面盖一个小红色实心标签，不用印章：它在标题自己的流里，不会和违禁品印章重叠，也保持小。返回它下面的 y。
        private static float StolenTag(Ctx c, string text, float x, float y)
        {
            float px = 3f * c.U, py = c.U, top = 2f * c.U;
            TextMeshProUGUI t = Text(c, c.F * 0.75f, StolenLabel(text), Paper, 0f);
            Vector2 ts = t.rectTransform.sizeDelta;
            Solid(c, StampRed, x, y + top, ts.x + 2f * px, ts.y + 2f * py);
            Ui.Move(t.rectTransform, x + px, y + top + py);
            return y + top + ts.y + 2f * py;
        }

        /// 价格右侧空白处盖一个橡皮章（“违禁品”）。正的，因为倾斜的像素字会糊；双线框加几处磨掉的缺口，看起来像盖上去的，而不是个按钮。
        private static RectTransform Stamp(Ctx c, string text, float y, int index)
        {
            string main, sub;
            SplitFlag(text, out main, out sub);
            RectTransform st = Ui.NewRect(c.Card, "stamp");
            TextMeshProUGUI t = Ui.NewText(st, c.Src, c.F * StampMain, "<b>" + main + "</b>", StampRed, 0f);
            Vector2 ms = t.rectTransform.sizeDelta;
            float w = ms.x, h = ms.y;
            TextMeshProUGUI s = null;
            if (sub.Length > 0)
            {
                s = Ui.NewText(st, c.Src, c.F * StampSub, sub, StampRed, 0f);
                w = Mathf.Max(w, s.rectTransform.sizeDelta.x);
                h += s.rectTransform.sizeDelta.y;
            }
            float edge = StampEdge * c.U, outer = 2f * c.U, inset = outer + c.U;
            float sw = w + 2f * edge, sh = h + 2f * edge;
            Frame(st, 0f, 0f, sw, sh, outer);
            Frame(st, inset, inset, sw - 2f * inset, sh - 2f * inset, c.U);
            Wear(c, st, text, sw, sh, outer);
            Ui.Move(t.rectTransform, Mathf.Round((sw - ms.x) / 2f), edge);
            if (s != null) Ui.Move(s.rectTransform, Mathf.Round((sw - s.rectTransform.sizeDelta.x) / 2f), edge + ms.y);

            float x = c.W - c.Pad - sw - index * Mathf.Round(sw * 0.35f);
            Ui.Place(st, x, y + c.Gap + index * 2f * c.U, sw, sh);
            // 轴心放中间，落下时以中心缩放。
            st.pivot = new Vector2(0.5f, 0.5f);
            st.anchoredPosition += new Vector2(sw / 2f, -sh / 2f);
            return st;
        }

        /// 印章主文字和副文字的字号，相对正文字号。
        private const float StampMain = 0.8f, StampSub = 0.6f;
        /// 单子上最小的字号相对正文的比例（印章的小字），抬字号时按它算。
        private const float MinFontShare = StampSub;
        /// 印章每边的边框和内边距，按像素：外线 2、间隔 1、内线 1、内边距 1。
        private const float StampEdge = 5f;

        private static void Frame(RectTransform parent, float x, float y, float w, float h, float thick)
        {
            Ui.Place(Ui.NewImage(parent, StampRed), x, y, w, thick);
            Ui.Place(Ui.NewImage(parent, StampRed), x, y + h - thick, w, thick);
            Ui.Place(Ui.NewImage(parent, StampRed), x, y + thick, thick, h - 2f * thick);
            Ui.Place(Ui.NewImage(parent, StampRed), x + w - thick, y + thick, thick, h - 2f * thick);
        }

        /// 外框线上几处没沾上墨的小缺口；同一个标记用同一组。
        private static void Wear(Ctx c, RectTransform st, string text, float sw, float sh, float thick)
        {
            System.Random rng = new System.Random(text.GetHashCode());
            for (int k = 0; k < 5; k++)
            {
                float len = rng.Next(1, 4) * c.U;
                // 只挖最外面那一行像素：缺口叠在一起也还有里面一行，框永远不会破。
                bool far = rng.Next(2) == 0;
                if (rng.Next(3) > 0)
                {
                    float x = Mathf.Round((float)rng.NextDouble() * (sw - len - 2f * thick) / c.U) * c.U + thick;
                    Ui.Place(Ui.NewImage(st, Paper), x, far ? sh - c.U : 0f, len, c.U);
                }
                else
                {
                    float yy = Mathf.Round((float)rng.NextDouble() * (sh - len - 2f * thick) / c.U) * c.U + thick;
                    Ui.Place(Ui.NewImage(st, Paper), far ? sw - c.U : 0f, yy, c.U, len);
                }
            }
        }

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

        /// 价格后面括号里的注解，转成墨色、保留颜色、去掉括号。
        private static string NoteRich(List<Seg> note)
        {
            List<Seg> rest = new List<Seg>();
            for (int i = 0; i < note.Count; i++)
            {
                Seg s = note[i];
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
