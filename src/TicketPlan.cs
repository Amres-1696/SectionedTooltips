using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 估价单的一节先“定型”再画：每一行（或连着的几行）是什么，只在 PlanOf 判一次，画的时候按类型分支。
    /// 这里不量也不画，只读行的文字、颜色和前后关系。
    internal static partial class Ticket
    {
        private enum ItemKind
        {
            /// 单独占一行的小标题；Row 为 null 时是一行空白（上一个小标题已经被别的行借去当标签）。
            HeadingLine,
            /// 模组的词条名称或描述。
            ModuleAffix,
            /// 升级行：名称、格子或进度条、等级、材料。
            Upgrade,
            /// 连着的几条模组效果，画成彩色标签。
            Effects,
            /// 名字靠左、数值靠右的一行。
            Wide,
            /// 连着的几行养殖基因，画成表。
            Genes,
            /// “标签  数值”；Reading 是并到这一行的进度条读数行。
            Pair,
            /// 竖线隔开的几段，各自成一对。
            Pipes,
            /// 按制表位分列的一行。
            Cells,
            /// 武器词条，按正负垫底色。
            WeaponAffix,
            /// 没有自己标签的文字，或进度条加读数。
            Text,
        }

        private sealed class PlanItem
        {
            public ItemKind Kind;
            public Row Row;
            /// 画之前可以从这里断到下一栏（单子太高时）。
            public bool Break;
            /// 借来放进标签列的小标题；没有时为 null。
            public Row Label;
            /// 前面画一道虚线（词条和属性之间、两张成分表之间）。
            public bool Rule;
            /// 前面留半个空隙（下一个词条名称）。
            public bool Gap;
            /// ModuleAffix：是名称（粗体）还是描述。
            public bool Name;
            /// WeaponAffix：负面词条。
            public bool Negative;
            /// Text：从数值列起画，而不是标签列。
            public bool AtValue;
            public Row Reading;
            public Upgrade Upgrade;
            public List<Effect> Effects;
            public List<Row> Rows;
            public List<Gene> Genes;
            public List<Row> Parts;
        }

        private sealed class SectionPlan
        {
            /// 画的顺序（状态行已经挪到最前，见 StatusFirst）。
            public Block Block;
            public readonly List<PlanItem> Items = new List<PlanItem>();
            /// 这一节里有没有标签；纯文字的节占满整宽。
            public bool Labelled;
            /// 最后一项之后还有一个断点。
            public bool TrailingBreak;
        }

        /// 小标题（“升级：”、“模组效果：”）给它下面没有自己标签的行当标签；下面的行有自己的标签时，它单独占一行。
        private static SectionPlan PlanOf(Block source)
        {
            SectionPlan plan = new SectionPlan();
            Block bl = StatusFirst(source);
            plan.Block = bl;
            plan.Labelled = Labelled(bl);
            bool module = bl.Key == Sections.Module, weapon = bl.Key == Sections.Weapon, liquid = bl.Key == Sections.Liquid;
            bool tabbed = false;
            for (int i = 0; i < bl.Rows.Count; i++) if (bl.Rows[i].Cells != null && bl.Rows[i].Cells.Count >= 2) tabbed = true;
            bool stats = false, affixes = false, ruled = false;
            // 等着用的小标题。open 而 pending 为 null：已经被借去当过标签，或者本来就没有字。
            Row pending = null;
            bool open = false;
            bool headed = false;   // 正处在某个小标题管着的行里
            bool brk = false;
            for (int i = 0; i < bl.Rows.Count; i++)
            {
                Row row = bl.Rows[i];
                // 单子太高时每行另起一块，可以断到下一栏；小标题和它管着的行不拆开。
                if (i > 0 && !open) brk = true;
                // 模组的词条和属性分开画（游戏给两者上的蓝几乎一样）：第一个词条前画虚线，每个名称前留点空，描述字号小一档。
                if (module && Affix(row))
                {
                    PlanItem affix = Add(plan, ItemKind.ModuleAffix, row, ref brk);
                    affix.Name = LeadSeg(row).Bold;
                    if (!affixes && stats) affix.Rule = true;
                    else if (affixes && affix.Name) affix.Gap = true;
                    affixes = true;
                    continue;
                }
                if (row.Heading)
                {
                    if (open) Add(plan, ItemKind.HeadingLine, pending, ref brk);
                    pending = Inked(row.Label) ? row : null;
                    open = true;
                    headed = true;
                    continue;
                }
                stats = true;
                Upgrade u = UpgradeOf(row);
                if (u != null)
                {
                    // 名字放在标签列，升级项顺着这一列读（“储藏 ▬▬▬▬ 4/4 废金属”）；上面的小标题单独一行。
                    if (pending != null) Add(plan, ItemKind.HeadingLine, pending, ref brk);
                    pending = null;
                    open = false;
                    Add(plan, ItemKind.Upgrade, row, ref brk).Upgrade = u;
                    continue;
                }
                // 只在小标题下面（“模组效果：”）：孤零零一个“+20%”只是个数字。
                if (open && module && EffectOf(row) != null)
                {
                    PlanItem effects = Add(plan, ItemKind.Effects, row, ref brk);
                    effects.Label = pending;
                    effects.Effects = new List<Effect>();
                    int j = i;
                    for (; j < bl.Rows.Count; j++)
                    {
                        Effect e = EffectOf(bl.Rows[j]);
                        if (e == null) break;
                        effects.Effects.Add(e);
                    }
                    i = j - 1;
                    pending = null;
                    continue;
                }
                if (row.IsPair)
                {
                    headed = false;
                    // 有自己标签的行上面的小标题单独占一行。
                    if (pending != null) Add(plan, ItemKind.HeadingLine, pending, ref brk);
                    pending = null;
                    open = false;
                    if (row.Wide) { Add(plan, ItemKind.Wide, row, ref brk); continue; }
                    if (GeneOf(bl, row) != null)
                    {
                        PlanItem genes = Add(plan, ItemKind.Genes, row, ref brk);
                        genes.Rows = new List<Row>();
                        genes.Genes = new List<Gene>();
                        int j = i;
                        for (; j < bl.Rows.Count; j++)
                        {
                            Gene g = GeneOf(bl, bl.Rows[j]);
                            if (g == null) break;
                            genes.Rows.Add(bl.Rows[j]);
                            genes.Genes.Add(g);
                        }
                        i = j - 1;
                        continue;
                    }
                    Row reading = Reading(bl, i);
                    Add(plan, ItemKind.Pair, row, ref brk).Reading = reading;
                    if (reading != null) i++;
                    continue;
                }
                List<Row> parts = PipeParts(row);
                if (parts != null)
                {
                    Add(plan, ItemKind.Pipes, row, ref brk).Parts = parts;
                    continue;
                }
                if (row.Cells != null && tabbed)
                {
                    // 制表位行自己按列落位，不参与标签列。
                    if (pending != null) Add(plan, ItemKind.HeadingLine, pending, ref brk);
                    pending = null;
                    open = false;
                    PlanItem cells = Add(plan, ItemKind.Cells, row, ref brk);
                    // 液体里带条的成分表下面紧接着物品详情的成分表：中间一道虚线，读得出是两张表。
                    if (liquid && i > 0 && !ruled)
                    {
                        ruled = true;
                        cells.Rule = true;
                    }
                    continue;
                }
                bool negative;
                if (weapon && AffixSign(row, out negative))
                {
                    // 枪械词条：正号垫浅绿、负号垫浅红，一眼分得出哪条是好的。
                    PlanItem affix = Add(plan, ItemKind.WeaponAffix, row, ref brk);
                    affix.Negative = negative;
                    affix.Label = pending;
                    pending = null;
                    continue;
                }
                // 带进度条的行跟着上面那条条的起点走（和数值列对齐）；其余的从标签列起，
                // 免得“（另 3 项低于显示阈值）”这种说明缩到数值列去。
                // 小标题（“模组效果：”）管着的那几行例外：标签占着标签列，行也从标签列起就会叠在一起，
                // 整组都从数值列起。
                // 进度条总是从数值列起（这一节只有一条没标签的条时也是，比如空量筒的“▬ 0 ml / 100 ml”），和别的节的条对齐；宽度也是按数值列算的。
                PlanItem text = Add(plan, ItemKind.Text, row, ref brk);
                text.AtValue = BarOf(row.Segs) != null || (plan.Labelled && headed);
                text.Label = pending;
                pending = null;
            }
            if (pending != null) Add(plan, ItemKind.HeadingLine, pending, ref brk);
            plan.TrailingBreak = brk;
            return plan;
        }

        private static PlanItem Add(SectionPlan plan, ItemKind kind, Row row, ref bool brk)
        {
            PlanItem item = new PlanItem();
            item.Kind = kind;
            item.Row = row;
            item.Break = brk;
            brk = false;
            plan.Items.Add(item);
            return item;
        }

        /// 这几段写出来有没有字（见 InkRich：画成条的段和空段不写）。
        private static bool Inked(List<Seg> segs)
        {
            if (segs == null) return false;
            for (int i = 0; i < segs.Count; i++) if (!Drawn(segs[i]) && !string.IsNullOrEmpty(segs[i].Text)) return true;
            return false;
        }

        // ---------------------------------------------------------------- 行的读法：这一行是什么

        /// 模组词条的名称或描述：游戏把它们写成浅蓝，属性写成浅青。
        private static bool Affix(Row row)
        {
            Seg s = row.IsPair || row.Heading ? null : LeadSeg(row);
            return s != null && s.Color == Palette.LightBlue;
        }

        /// 这一行第一个有字的 seg。
        private static Seg LeadSeg(Row row)
        {
            if (row.Segs == null) return null;
            for (int i = 0; i < row.Segs.Count; i++)
                if (row.Segs[i].Bar == null && TextUnits.Plain(row.Segs[i].Text).Trim().Length > 0) return row.Segs[i];
            return null;
        }

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

        /// 这一节里有没有标签；纯文字的节占满整宽。
        private static bool Labelled(Block bl)
        {
            for (int i = 0; i < bl.Rows.Count; i++) if (bl.Rows[i].IsPair || bl.Rows[i].Heading) return true;
            return false;
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
    }
}
