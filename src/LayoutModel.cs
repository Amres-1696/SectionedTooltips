using System.Collections.Generic;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 一段样式一致的 tooltip 文本，从 RichTextBuilder 内部的 TextSlice 复制出来。
    internal sealed class Seg
    {
        public string Text = "";
        public Palette Color = Palette.None;
        public bool Bold, Italic, Underline, Strike, NewLine;
        public Palette Background = Palette.None;
        public Palette UnderlineColor = Palette.None;
        public Palette StrikeColor = Palette.None;

        /// 这一段是游戏用 AddProgressBar 画的进度条时设置。
        public BarMark Bar;

        public Seg With(string text)
        {
            Seg s = (Seg)MemberwiseClone();
            s.Text = text;
            return s;
        }
    }

    /// slice 区间 [Start, End) 是一条进度条（AddProgressBar 画的）。
    internal sealed class BarMark
    {
        public int Start;
        public int End;
        public float Ratio;
        public RenderHandler.ColorPalette Color;
        public RenderHandler.ColorPalette Background;
        /// 其他模组按 RGB 给的条色（AquaSense 的成分条）；有它时不看 Color。
        public bool HasRgb;
        public UnityEngine.Color32 Rgb;
    }

    /// 并块、拆列时要看的设置和语言模板。游戏里从 Settings 和 LocTemplates 取（见 Layout.Group），离线回放时直接填。
    internal struct RowOptions
    {
        /// 估价单风格（否则是经典风格）。
        public bool Ticket;
        /// 经典风格每行最多多少个半角字符。
        public int MaxChars;
        /// 分数后面补一条进度条（图形进度条和分数进度条两个设置都开着）。
        public bool FractionBars;
        /// 探到了游戏“耐久度 a/b”的模板；Prefix 和 Mid 是第一个数字前面、两个数字中间的字面量。
        public bool TemplateKnown;
        public string TemplatePrefix, TemplateMid;
    }

    internal sealed class Line
    {
        public List<Seg> Segs = new List<Seg>();
        public string Key;
        public bool FromHelper;
        /// 写这一行的 helper 序号（见 Helpers）；-1 表示 GetTooltipBasic 自己写的。
        public int Ord = -1;
        public string Plain = "";
    }

    internal sealed class Row
    {
        public List<Seg> Label; // 普通行没有 Label
        public List<Seg> Value;
        public List<Seg> Segs;  // 整行文本，只有普通行有
        public List<List<Seg>> Cells; // 按制表位分开的几列（见 TextUnits.SplitCells）；普通行没有
        public bool Heading;    // 只有“标签：”、后面没内容；Label 存去掉冒号的文字
        public int Ord = -1;    // 同 Line.Ord
        public bool IsPair { get { return Label != null && !Heading; } }
        /// 整行的纯文字（一对和小标题取标签），不含进度条。
        public string Plain { get { return TextUnits.Plain(Segs ?? Label); } }
        /// 名字不占标签列、自己撑满一行（“名字 …… +6.96%”那种列表）。
        public bool Wide;
    }

    internal sealed class Block
    {
        public string Key;
        public readonly List<Row> Rows = new List<Row>();
    }

    /// 在输出行 Line 的第 [Char, Char + Count) 个字符上画一条进度条。
    internal sealed class OverlayBar
    {
        public int Line;
        public int Char;
        public int Count;
        public float Ratio;
        public Palette Color;
        public Palette Background;
    }

    /// 覆盖层要画在排好版的 tooltip 上；行号从这个 tooltip 的第一行算起。
    internal sealed class LayoutResult
    {
        public readonly List<OverlayBar> Bars = new List<OverlayBar>();
        /// 留给物品图标的两行中的第一行；没有时为 -1。
        public int IconLine = -1;
        /// 这两行开头为图标留出的空白字符数。
        public int IconChars;
        /// 排版用的分节结果；估价单风格自己画。
        public List<Block> Blocks;
        /// 侧边面板的数据；没有面板时为 null。
        public ContentsInfo Contents;
        public LiquidInfo Liquid;
    }

    /// 侧边面板的液位计：液量条的填充和跟在后面的文字。
    internal sealed class LiquidInfo
    {
        public float Ratio;
        public Palette Color;
        public Palette Background;
        public List<Seg> Text = new List<Seg>();
    }
}
