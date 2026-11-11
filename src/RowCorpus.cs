#if DEVDUMP || HEADLESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// <summary>
    /// 并块拆列（Layout.Group）的回放语料。dev 构建全量导出时，把每件物品进 Group 之前的行和 Group 的结果写成文本；
    /// tests\test.bat 离线读回来重跑 Group，逐件比结果，改拆行规则不用开游戏。
    /// 一行一条，制表符分隔，文字里的制表符、换行和反斜杠转义：
    ///   #   v1  设置…                 文件头（见 Header）
    ///   === 物品 id
    ///   L   key  ord  helper  纯文字   进 Group 的一行
    ///   S   颜色  底色  下划线色  删除线色  样式  条  文字     这一行里的一段
    ///   B   key                        结果里的一块
    ///   R   类型  ord  各部分…          块里的一行（见 Describe）
    /// 只在 dev 构建和离线测试里编译。
    /// </summary>
    internal static class RowCorpus
    {
        internal sealed class Item
        {
            public string Id;
            public readonly List<Line> Lines = new List<Line>();
            public readonly List<string> Expected = new List<string>();
        }

        // ---------------------------------------------------------------- 写

        internal static void Header(StringBuilder sb, RowOptions o, bool cjk)
        {
            sb.Append("#\tv1")
                .Append("\tticket=").Append(o.Ticket ? 1 : 0)
                .Append("\tmaxChars=").Append(o.MaxChars.ToString(CultureInfo.InvariantCulture))
                .Append("\tfractionBars=").Append(o.FractionBars ? 1 : 0)
                .Append("\tcjk=").Append(cjk ? 1 : 0)
                .Append("\ttplKnown=").Append(o.TemplateKnown ? 1 : 0)
                .Append("\ttplPrefix=").Append(Escape(o.TemplatePrefix))
                .Append("\ttplMid=").Append(Escape(o.TemplateMid))
                .Append('\n');
        }

        internal static void Begin(StringBuilder sb, string id)
        {
            sb.Append("===\t").Append(Escape(id)).Append('\n');
        }

        internal static void Write(StringBuilder sb, List<Line> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                sb.Append("L\t").Append(Escape(l.Key)).Append('\t').Append(l.Ord.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(l.FromHelper ? 1 : 0).Append('\t').Append(Escape(l.Plain)).Append('\n');
                for (int k = 0; k < l.Segs.Count; k++)
                {
                    Seg s = l.Segs[k];
                    sb.Append("S\t").Append(s.Color).Append('\t').Append(s.Background).Append('\t').Append(s.UnderlineColor)
                        .Append('\t').Append(s.StrikeColor).Append('\t').Append(Flags(s)).Append('\t');
                    if (s.Bar == null) sb.Append('-');
                    else sb.Append(s.Bar.Ratio.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(s.Bar.Color)
                        .Append('|').Append(s.Bar.Background).Append('|').Append(s.Bar.HasRgb ? 1 : 0);
                    sb.Append('\t').Append(Escape(s.Text)).Append('\n');
                }
            }
        }

        /// Group 的结果：每块一行 B，块里每行一条 R。
        internal static void Describe(StringBuilder sb, List<Block> blocks)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                Block bl = blocks[i];
                sb.Append("B\t").Append(Escape(bl.Key)).Append('\n');
                for (int j = 0; j < bl.Rows.Count; j++) sb.Append(Describe(bl.Rows[j])).Append('\n');
            }
        }

        /// 一行的样子：类型（H 小标题、W 宽的一对、P 一对、C 表格、T 文字）、helper 序号，然后是它有的每个部分。
        internal static string Describe(Row row)
        {
            StringBuilder sb = new StringBuilder("R\t");
            sb.Append(row.Heading ? "H" : row.IsPair ? (row.Wide ? "W" : "P") : row.Cells != null ? "C" : "T");
            sb.Append('\t').Append(row.Ord.ToString(CultureInfo.InvariantCulture));
            if (row.Label != null) sb.Append("\tL=").Append(Describe(row.Label));
            if (row.Value != null) sb.Append("\tV=").Append(Describe(row.Value));
            if (row.Segs != null) sb.Append("\tS=").Append(Describe(row.Segs));
            if (row.Cells != null)
                for (int k = 0; k < row.Cells.Count; k++) sb.Append("\tC=").Append(Describe(row.Cells[k]));
            return sb.ToString();
        }

        /// 几段文字连起来；颜色、样式、进度条不是默认值的段前面带一个“‹…›”标记。
        private static string Describe(List<Seg> segs)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                string flags = Flags(s);
                bool plain = s.Color == Palette.None && s.Background == Palette.None && s.UnderlineColor == Palette.None
                    && s.StrikeColor == Palette.None && flags == "-" && s.Bar == null;
                if (!plain)
                {
                    sb.Append('‹').Append(s.Color);
                    if (s.Background != Palette.None) sb.Append(" 底").Append(s.Background);
                    if (s.UnderlineColor != Palette.None) sb.Append(" 线").Append(s.UnderlineColor);
                    if (s.StrikeColor != Palette.None) sb.Append(" 删").Append(s.StrikeColor);
                    if (flags != "-") sb.Append(' ').Append(flags);
                    if (s.Bar != null)
                        sb.Append(" 条").Append(s.Bar.Ratio.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ').Append(s.Bar.Color)
                            .Append('/').Append(s.Bar.Background);
                    sb.Append('›');
                }
                sb.Append(Escape(s.Text));
            }
            return sb.ToString();
        }

        private static string Flags(Seg s)
        {
            string f = (s.Bold ? "b" : "") + (s.Italic ? "i" : "") + (s.Underline ? "u" : "") + (s.Strike ? "s" : "") + (s.NewLine ? "n" : "");
            return f.Length > 0 ? f : "-";
        }

        // ---------------------------------------------------------------- 读

        internal static List<Item> Read(string text, out RowOptions options, out bool cjk)
        {
            options = new RowOptions();
            options.TemplatePrefix = "";
            options.TemplateMid = "";
            cjk = false;
            List<Item> items = new List<Item>();
            Item cur = null;
            Line line = null;
            string[] rows = text.Split('\n');
            for (int i = 0; i < rows.Length; i++)
            {
                string row = rows[i].TrimEnd('\r');
                if (row.Length == 0) continue;
                string[] f = row.Split('\t');
                if (f[0] == "#")
                {
                    for (int k = 2; k < f.Length; k++)
                    {
                        int eq = f[k].IndexOf('=');
                        if (eq < 0) continue;
                        string name = f[k].Substring(0, eq), value = f[k].Substring(eq + 1);
                        if (name == "ticket") options.Ticket = value == "1";
                        else if (name == "maxChars") options.MaxChars = int.Parse(value, CultureInfo.InvariantCulture);
                        else if (name == "fractionBars") options.FractionBars = value == "1";
                        else if (name == "cjk") cjk = value == "1";
                        else if (name == "tplKnown") options.TemplateKnown = value == "1";
                        else if (name == "tplPrefix") options.TemplatePrefix = Unescape(value);
                        else if (name == "tplMid") options.TemplateMid = Unescape(value);
                    }
                }
                else if (f[0] == "===")
                {
                    cur = new Item();
                    cur.Id = Unescape(f[1]);
                    items.Add(cur);
                    line = null;
                }
                else if (cur == null) continue;
                else if (f[0] == "L")
                {
                    line = new Line();
                    line.Key = Unescape(f[1]);
                    line.Ord = int.Parse(f[2], CultureInfo.InvariantCulture);
                    line.FromHelper = f[3] == "1";
                    line.Plain = Unescape(f[4]);
                    cur.Lines.Add(line);
                }
                else if (f[0] == "S" && line != null)
                {
                    Seg s = new Seg();
                    s.Color = Color(f[1]);
                    s.Background = Color(f[2]);
                    s.UnderlineColor = Color(f[3]);
                    s.StrikeColor = Color(f[4]);
                    s.Bold = f[5].IndexOf('b') >= 0;
                    s.Italic = f[5].IndexOf('i') >= 0;
                    s.Underline = f[5].IndexOf('u') >= 0;
                    s.Strike = f[5].IndexOf('s') >= 0;
                    s.NewLine = f[5].IndexOf('n') >= 0;
                    if (f[6] != "-")
                    {
                        string[] b = f[6].Split('|');
                        s.Bar = new BarMark();
                        s.Bar.Ratio = float.Parse(b[0], CultureInfo.InvariantCulture);
                        s.Bar.Color = Color(b[1]);
                        s.Bar.Background = Color(b[2]);
                        s.Bar.HasRgb = b[3] == "1";
                    }
                    s.Text = Unescape(f[7]);
                    line.Segs.Add(s);
                }
                else if (f[0] == "B" || f[0] == "R") cur.Expected.Add(row);
            }
            return items;
        }

        private static Palette Color(string name)
        {
            return (Palette)Enum.Parse(typeof(Palette), name);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n == 'r' ? '\r' : n);
            }
            return sb.ToString();
        }
    }
}
#endif
