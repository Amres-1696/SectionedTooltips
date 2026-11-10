using System;
using System.Collections.Generic;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 经典风格的输出：把分好的块写回一个新的 builder（标题栏、树形前缀、对齐的两列）。
    internal static partial class Layout
    {
        private const string GlyphMid = "├ ";
        private const string GlyphLast = "└ ";
        private const string GlyphCont = "│ ";
        private const string HeaderMark = "▌";
        /// 经典风格小节标题栏的底色（侧边面板的标题也一样）。
        internal const Palette HeaderBg = Palette.VeryDarkGray;
        /// 经典风格最窄的内容宽度，按半角字符算。
        private const int MinChars = 28;

        /// 排版需要的最宽一行，单位是十二分之一格，最后夹到设置范围内。
        private static int ContentWidth(List<Block> blocks, int titleIndent)
        {
            int min = MinChars * TextUnits.Cell;
            int max = Settings.MaxChars * TextUnits.Cell;
            if (min > max) min = max;
            int glyph = TextUnits.Width(GlyphMid);
            int need = min;
            for (int i = 0; i < blocks.Count; i++)
            {
                Block bl = blocks[i];
                if (bl.Key == Sections.Desc) continue;
                if (bl.Key != Sections.Title)
                    need = Math.Max(need, TextUnits.Width(HeaderMark) + TextUnits.Width(" " + Sections.TitleOf(bl.Key)) + TextUnits.Cell);
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    Row r = bl.Rows[j];
                    int indent = bl.Key == Sections.Title ? titleIndent : glyph;
                    if (r.IsPair)
                    {
                        int w = indent + Width(r.Label) + 2 * TextUnits.Cell + Width(r.Value);
                        if (w <= max) need = Math.Max(need, w);
                    }
                    else need = Math.Max(need, Math.Min(indent + Width(r.Heading ? r.Label : r.Segs), max));
                }
            }
            return Math.Min(need, max);
        }

        private static int Width(List<Seg> segs)
        {
            return TextUnits.Width(TextUnits.Of(segs));
        }

        /// 给图标留几个字符宽：两行文字大约 5 格高，方形图标占 5 格再加 1 格间距；宽的（注射器、台灯）按比例多留，最多 10 格。
        private static int IconCellsFor(UnityEngine.Sprite sprite)
        {
            if (sprite == null) return 0;
            float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
            int cells = (int)Math.Ceiling(4.5 * Math.Max(1f, aspect));
            return Math.Min(9, Math.Max(5, cells)) + 1;
        }

        private static RichTextBuilder Render(List<Block> blocks, int iconCells, LayoutResult result)
        {
            bool icon = iconCells > 0;
            int iconW = iconCells * TextUnits.Cell;
            int c = ContentWidth(blocks, iconW);
            bool tree = Settings.On(Settings.Tree);
            Emitter e = new Emitter(new RichTextBuilder(Unlimited), result);

            for (int i = 0; i < blocks.Count; i++)
            {
                Block bl = blocks[i];
                if (bl.Key == Sections.Title)
                {
                    bool withIcon = icon && result.IconLine < 0;
                    string indent = withIcon ? TextUnits.Pad(iconW) : "";
                    int first = e.LineNo;
                    for (int j = 0; j < bl.Rows.Count; j++) e.Wrapped(bl.Rows[j].Segs, indent, indent, c);
                    if (withIcon)
                    {
                        while (e.LineNo - first < 2)
                        {
                            e.Put(indent, Palette.None, false, Palette.None);
                            e.Flush();
                        }
                        result.IconLine = first;
                        result.IconChars = TextUnits.TmpChars(indent);
                    }
                    continue;
                }
                if (bl.Key == Sections.Desc)
                {
                    if (e.Any)
                    {
                        e.Put(new string('─', Math.Max(1, c / TextUnits.BoxW)), Palette.DarkGray, false, Palette.None);
                        e.Flush();
                    }
                    for (int j = 0; j < bl.Rows.Count; j++) e.Wrapped(bl.Rows[j].Segs, "", "", c);
                    continue;
                }

                Header(e, bl.Key, c);

                int glyphW = TextUnits.Width(GlyphMid);
                string blank = TextUnits.Pad(glyphW);
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    bool last = j == bl.Rows.Count - 1;
                    string glyph = !tree ? blank : last ? GlyphLast : GlyphMid;
                    string cont = !tree || last ? blank : GlyphCont;
                    Row r = bl.Rows[j];
                    if (r.Heading)
                    {
                        e.Put(glyph, Palette.DarkGray, false, Palette.None);
                        e.PutAll(r.Label);
                        e.Flush();
                        continue;
                    }
                    if (!r.IsPair)
                    {
                        e.Wrapped(r.Segs, glyph, cont, c);
                        continue;
                    }

                    int wl = Width(r.Label), wv = Width(r.Value);
                    if (glyphW + wl + TextUnits.Cell + wv <= c)
                    {
                        e.Put(glyph, Palette.DarkGray, false, Palette.None);
                        e.PutAll(r.Label);
                        e.Put(TextUnits.Pad(c - glyphW - wl - wv), Palette.None, false, Palette.None);
                        e.PutAll(r.Value);
                        e.Flush();
                    }
                    else
                    {
                        // 一行放不下：标签单独一行，数值右对齐（或换行）放在下面。
                        e.Wrapped(r.Label, glyph, cont, c);
                        if (wv <= c - glyphW)
                        {
                            e.Put(cont, Palette.DarkGray, false, Palette.None);
                            e.Put(TextUnits.Pad(c - glyphW - wv), Palette.None, false, Palette.None);
                            e.PutAll(r.Value);
                            e.Flush();
                        }
                        else
                        {
                            string indent = cont + TextUnits.Pad(2 * TextUnits.Cell);
                            e.Wrapped(r.Value, indent, indent, c);
                        }
                    }
                }
            }
            return e.Finish();
        }

        /// 一行“▌ 标题”配彩色底条。背景层每个字符画一个 '█'，所以用零宽空格（背景层当空格画，文字里没有宽度）把底条补到内容宽度。
        private static void Header(Emitter e, string key, int c)
        {
            Palette bg = HeaderBg;
            Sections.Style st = Sections.StyleOf(key);
            string title = " " + Sections.TitleOf(key);
            e.Put(HeaderMark, st.Accent, false, bg);
            e.Put(title, st.Text, true, bg);
            int drawn = (TextUnits.RawChars(HeaderMark) + TextUnits.RawChars(title)) * TextUnits.BoxW;
            int extra = (c - drawn) / TextUnits.BoxW;
            if (extra > 0) e.Put(new string(TextUnits.ZeroWidthSpace, extra), Palette.None, false, bg);
            e.Flush();
        }

        /// 一行一行写进新的 builder，先把一行攒好再输出。
        private sealed class Emitter
        {
            private readonly RichTextBuilder o;
            private readonly LayoutResult result;
            private readonly bool drawBars;
            private readonly List<Seg> line = new List<Seg>();
            public bool Any;

            /// 正在拼的行号（等于已经输出的行数）。
            public int LineNo;

            public Emitter(RichTextBuilder output, LayoutResult result)
            {
                o = output;
                this.result = result;
                drawBars = Settings.On(Settings.Bars);
            }

            public void Put(string text, Palette color, bool bold, Palette bg)
            {
                if (string.IsNullOrEmpty(text)) return;
                Seg s = new Seg();
                s.Text = text;
                s.Color = color;
                s.Bold = bold;
                s.Background = bg;
                line.Add(s);
            }

            public void PutAll(List<Seg> segs)
            {
                for (int i = 0; i < segs.Count; i++) line.Add(segs[i]);
            }

            /// 把 segs 按内容宽度换行，每行加上前缀，写进 builder。
            public void Wrapped(List<Seg> segs, string first, string cont, int width)
            {
                int pf = TextUnits.Width(first), pc = TextUnits.Width(cont);
                List<List<Unit>> parts = TextUnits.Wrap(TextUnits.Of(segs), width - pf, width - pc);
                for (int i = 0; i < parts.Count; i++)
                {
                    Put(i == 0 ? first : cont, Palette.DarkGray, false, Palette.None);
                    PutAll(TextUnits.ToSegs(parts[i], segs));
                    Flush();
                }
            }

            public void Flush()
            {
                Compensate();
                if (Any) o.NextLine();
                Any = true;
                int chars = 0;
                for (int i = 0; i < line.Count; i++)
                {
                    Seg s = line[i];
                    int n = TextUnits.TmpChars(s.Text);
                    if (s.Bar != null && drawBars && n > 0)
                    {
                        OverlayBar bar = new OverlayBar();
                        bar.Line = LineNo;
                        bar.Char = chars;
                        bar.Count = n;
                        bar.Ratio = s.Bar.Ratio;
                        bar.Color = s.Bar.Color;
                        bar.Background = s.Bar.Background;
                        result.Bars.Add(bar);
                    }
                    chars += n;
                    o.Add(s.Text, s.Color, "", s.Bold, s.Italic, s.Underline, s.Strike, s.Background, s.UnderlineColor, s.StrikeColor);
                }
                line.Clear();
                LineNo++;
            }

            /// 背景层是整行文字，每个字符换成 '█'（彩色段）或 ' '（其余），所以字符宽度和这两个不一致时会错位。在每个彩色段前面用零宽空格把背景层补齐：它在背景层画成空格，文字里看不到。
            private void Compensate()
            {
                int fg = 0, bg = 0;
                for (int i = 0; i < line.Count; i++)
                {
                    Seg s = line[i];
                    bool coloured = s.Background != Palette.None;
                    if (coloured && fg - bg >= TextUnits.SpaceW)
                    {
                        int k = (fg - bg + TextUnits.SpaceW / 2) / TextUnits.SpaceW;
                        Seg z = new Seg();
                        z.Text = new string(TextUnits.ZeroWidthSpace, k);
                        line.Insert(i, z);
                        i++;
                        bg += k * TextUnits.SpaceW;
                    }
                    fg += TextUnits.Width(s.Text);
                    bg += TextUnits.RawChars(s.Text) * (coloured ? TextUnits.BoxW : TextUnits.SpaceW);
                }
            }

            public RichTextBuilder Finish()
            {
                if (line.Count > 0) Flush();
                o.AddLine("");
                return o;
            }
        }
    }
}
