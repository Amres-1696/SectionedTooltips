using System;
using System.Collections.Generic;
using System.Text;

namespace SectionedTooltips
{
    /// 一行里一个可见字符，或一整段富文本标签，带着它来自哪个 Seg。
    internal struct Unit
    {
        public int Seg;
        public string Text;
        public int Width;
        public bool Tag;
        public bool Space;
        public bool Wide;
    }

    /// tooltip 字体的显示宽度计算，单位是半角格的十二分之一（见 Cell），而游戏自己的 RichTextBuilder 把每个字符都算作一格。富文本标签不占宽度。
    /// 在 0.46D 中文环境下量的：ASCII 一格，CJK/全角和制表符/方块字符两格，普通空格只有约 5/12 格。其他语言的 tooltip 字体是等宽的 Mx437，除 CJK 外都是一格。
    internal static class TextUnits
    {
        internal const char ZeroWidthSpace = '​';
        internal const char IdeographicSpace = '　';
        internal const int Cell = 12;

        /// 一个 CJK 字符占几格：游戏的像素字体里是 2。
        internal const int Cjk = 2;

        /// ' ' 的宽度，也是背景层为无色字符画的一个空格的宽度。
        internal static int SpaceW = Cell;

        /// 制表符和方块字符的宽度，包括背景层的 '█'。
        internal static int BoxW = Cell;

        internal static bool CjkLocale;

        internal static void Configure()
        {
            CjkLocale = false;
            try { CjkLocale = UISettings.current != null && UISettings.current.GetCharWidthScale() > 1f; }
            catch { }
            SpaceW = CjkLocale ? 5 : Cell;
            BoxW = CjkLocale ? 2 * Cell : Cell;
        }

        internal static bool IsWide(char c)
        {
            return (c >= 0x1100 && c <= 0x115F)
                || (c >= 0x2E80 && c <= 0x303E)
                || (c >= 0x3041 && c <= 0x33FF)
                || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0xA000 && c <= 0xA4CF)
                || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFE30 && c <= 0xFE4F)
                || (c >= 0xFF00 && c <= 0xFF60)
                || (c >= 0xFFE0 && c <= 0xFFE6);
        }

        internal static int CharWidth(char c)
        {
            if (c == ZeroWidthSpace || c == '‌' || c == '‍' || c == '﻿') return 0;
            if (char.IsControl(c) || char.IsLowSurrogate(c)) return 0;
            if (c == ' ') return SpaceW;
            if (c == IdeographicSpace || char.IsHighSurrogate(c)) return 2 * Cell;
            if (c >= 0x2500 && c <= 0x259F) return BoxW;
            return IsWide(c) ? Cjk * Cell : Cell;
        }

        /// 宽度约 w 的不可见填充。故意不带标签：背景层会把包括标签在内的每个字符都变成一个空格，标签会把背景层推到文字前面。
        internal static string Pad(int w)
        {
            if (w <= 0) return "";
            int ideo = CjkLocale ? w / (2 * Cell) : 0;
            int rest = w - ideo * 2 * Cell;
            int spaces = (rest + SpaceW / 2) / SpaceW;
            return new string(IdeographicSpace, ideo) + new string(' ', spaces);
        }

        /// 背景层为 text 画的字符数（每个非控制字符一个，标签也算）。
        internal static int RawChars(string text)
        {
            int n = 0;
            if (text == null) return 0;
            for (int i = 0; i < text.Length; i++) if (!char.IsControl(text[i])) n++;
            return n;
        }

        internal static List<Unit> Of(List<Seg> segs)
        {
            List<Unit> units = new List<Unit>();
            for (int i = 0; i < segs.Count; i++)
            {
                int before = units.Count;
                Append(units, segs[i].Text, i);
                if (segs[i].Bar == null) continue;
                // 进度条占位符是一整块：不裁剪、不拆开、不换行。
                for (int k = before; k < units.Count; k++)
                {
                    Unit x = units[k];
                    x.Space = false;
                    x.Wide = false;
                    units[k] = x;
                }
            }
            return units;
        }

        /// 代表一条约 cells 格宽的进度条的不可见文本。
        internal static string BarPlaceholder(int cells)
        {
            return CjkLocale ? new string(IdeographicSpace, (cells + 1) / 2) : new string(' ', cells);
        }

        /// TextMeshPro 为 text 创建的字符数：除了标签都算。
        internal static int TmpChars(string text)
        {
            List<Unit> units = new List<Unit>();
            Append(units, text, 0);
            int n = 0;
            for (int i = 0; i < units.Count; i++)
                if (!units[i].Tag && !char.IsLowSurrogate(units[i].Text[0])) n++;
            return n;
        }

        internal static void Append(List<Unit> units, string text, int seg)
        {
            if (string.IsNullOrEmpty(text)) return;
            int j = 0;
            while (j < text.Length)
            {
                char c = text[j];
                Unit u;
                u.Seg = seg;
                if (c == '<')
                {
                    int close = text.IndexOf('>', j + 1);
                    if (close > j)
                    {
                        u.Text = text.Substring(j, close - j + 1);
                        u.Tag = true;
                        u.Space = false;
                        u.Wide = false;
                        u.Width = u.Text.StartsWith("<sprite") ? 2 * Cell : 0;
                        units.Add(u);
                        j = close + 1;
                        continue;
                    }
                }
                u.Text = c.ToString();
                u.Tag = false;
                u.Space = c == ' ' || c == IdeographicSpace || c == '\t';
                u.Width = CharWidth(c);
                u.Wide = u.Width >= 2 * Cell;
                units.Add(u);
                j++;
            }
        }

        internal static int Width(List<Unit> units)
        {
            int w = 0;
            for (int i = 0; i < units.Count; i++) w += units[i].Width;
            return w;
        }

        internal static int Width(string text)
        {
            List<Unit> units = new List<Unit>();
            Append(units, text, 0);
            return Width(units);
        }

        internal static string Plain(List<Unit> units)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < units.Count; i++) if (!units[i].Tag) sb.Append(units[i].Text);
            return sb.ToString();
        }

        internal static string Plain(string text)
        {
            List<Unit> units = new List<Unit>();
            Append(units, text, 0);
            return Plain(units);
        }

        internal static List<Unit> TrimStart(List<Unit> units)
        {
            List<Unit> r = new List<Unit>(units.Count);
            bool seen = false;
            for (int i = 0; i < units.Count; i++)
            {
                if (!seen && units[i].Space) continue;
                if (!units[i].Tag) seen = true;
                r.Add(units[i]);
            }
            return r;
        }

        internal static List<Unit> TrimEnd(List<Unit> units)
        {
            List<Unit> r = new List<Unit>(units);
            for (int i = r.Count - 1; i >= 0; i--)
            {
                if (r[i].Tag) continue;
                if (!r[i].Space) break;
                r.RemoveAt(i);
            }
            return r;
        }

        /// 贪心换行：在空格后或宽字符之间断，不行就词中断。
        internal static List<List<Unit>> Wrap(List<Unit> units, int firstWidth, int contWidth)
        {
            List<List<Unit>> lines = new List<List<Unit>>();
            List<Unit> cur = new List<Unit>();
            int w = 0;
            int limit = firstWidth < 1 ? 1 : firstWidth;
            int lastBreak = -1; // 断行时 cur[0..lastBreak] 可以留在这一行
            int lastSpace = -1; // 最近一个空格的位置，优先在这里断

            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u.Tag) { cur.Add(u); w += u.Width; continue; }

                if (w + u.Width > limit && w > 0)
                {
                    if (u.Space)
                    {
                        lines.Add(TrimEnd(cur));
                        cur = new List<Unit>();
                        w = 0;
                        limit = contWidth < 1 ? 1 : contWidth;
                        lastBreak = -1;
                        lastSpace = -1;
                        continue;
                    }
                    List<Unit> carry = new List<Unit>();
                    // 优先在空格处断：中文哪里都能断，但“紫外线灯”这种名字被拆开很难看。
                    // 只在空格离断点不远时才回退，免得把一大段都推到下一行。
                    int cut = -1;
                    if (lastSpace >= 0 && lastSpace < cur.Count - 1 && cur.Count - lastSpace - 1 <= MaxCarry) cut = lastSpace;
                    else if (!u.Wide && lastBreak >= 0 && lastBreak < cur.Count - 1) cut = lastBreak;
                    if (cut >= 0)
                    {
                        carry = cur.GetRange(cut + 1, cur.Count - cut - 1);
                        cur.RemoveRange(cut + 1, cur.Count - cut - 1);
                    }
                    // 收尾标点不能放在行首：把它前面那个字符一起带下来。
                    Unit next = u;
                    for (int k = 0; k < carry.Count; k++) if (!carry[k].Tag) { next = carry[k]; break; }
                    if (NoLineStart(next.Text))
                    {
                        int prev = -1, visible = 0;
                        for (int k = cur.Count - 1; k >= 0; k--)
                        {
                            if (cur[k].Tag) continue;
                            visible++;
                            if (prev < 0) prev = k;
                        }
                        if (prev >= 0 && visible > 1 && !cur[prev].Space)
                        {
                            carry.InsertRange(0, cur.GetRange(prev, cur.Count - prev));
                            cur.RemoveRange(prev, cur.Count - prev);
                        }
                    }
                    lines.Add(TrimEnd(cur));
                    cur = TrimStart(carry);
                    w = Width(cur);
                    limit = contWidth < 1 ? 1 : contWidth;
                    lastBreak = -1;
                    lastSpace = -1;
                }

                cur.Add(u);
                w += u.Width;
                if (u.Space) lastSpace = cur.Count - 1;
                if (u.Space || u.Wide) lastBreak = cur.Count - 1;
            }
            if (cur.Count > 0 || lines.Count == 0) lines.Add(TrimEnd(cur));
            return lines;
        }

        /// 回退到空格断行时，最多把这么多个字符带到下一行。
        private const int MaxCarry = 12;

        private const string NoStartChars = "。，、；：！？）》」』】〕…—～％,.;:!?)]}%";

        private static bool NoLineStart(string text)
        {
            return text != null && text.Length == 1 && NoStartChars.IndexOf(text[0]) >= 0;
        }

        /// <summary>
        /// 按制表位把一行切成几格：全角空格、两个以上连续空格，或游戏的 &lt;pos=…&gt; 标签
        /// （标签只当标记，不进正文）。少于两格时返回 null，表示这是一行普通文字。
        /// </summary>
        internal static List<List<Unit>> SplitCells(List<Unit> units)
        {
            List<List<Unit>> cells = new List<List<Unit>>();
            int from = 0, i = 0;
            bool any = false;
            while (i < units.Count)
            {
                if (units[i].Tag && units[i].Text.StartsWith("<pos", StringComparison.Ordinal))
                {
                    if (i > from) cells.Add(TrimEnd(units.GetRange(from, i - from)));
                    from = i + 1;
                    i++;
                    any = true;
                    continue;
                }
                if (!units[i].Space || units[i].Tag) { i++; continue; }
                int j = i;
                bool wide = false;
                while (j < units.Count && units[j].Space && !units[j].Tag)
                {
                    if (units[j].Text.Length == 1 && units[j].Text[0] == IdeographicSpace) wide = true;
                    j++;
                }
                if (!wide && j - i < 2) { i = j; continue; } // 单个半角空格只是词间距
                if (i > from) cells.Add(TrimEnd(units.GetRange(from, i - from)));
                from = j;
                i = j;
                any = true;
            }
            if (!any) return null;
            if (from < units.Count) cells.Add(TrimEnd(units.GetRange(from, units.Count - from)));
            for (int k = cells.Count - 1; k >= 0; k--) if (cells[k].Count == 0) cells.RemoveAt(k);
            return cells.Count >= 2 ? cells : null;
        }

        /// 把 units 还原成带样式的 segs，来自同一 seg 的相邻部分合并。
        internal static List<Seg> ToSegs(List<Unit> units, List<Seg> source)
        {
            List<Seg> r = new List<Seg>();
            StringBuilder sb = new StringBuilder();
            int seg = -1;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Seg != seg)
                {
                    if (seg >= 0 && sb.Length > 0) r.Add(source[seg].With(sb.ToString()));
                    sb.Length = 0;
                    seg = units[i].Seg;
                }
                sb.Append(units[i].Text);
            }
            if (seg >= 0 && sb.Length > 0) r.Add(source[seg].With(sb.ToString()));
            return r;
        }
    }
}
