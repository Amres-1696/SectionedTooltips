using System.Collections.Generic;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
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
