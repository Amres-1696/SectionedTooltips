using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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

    /// 把 GetTooltipBasic 生成好的 builder 重新排一遍：每行归到某一节，“标签: 数值”拆成两列，再写回 builder。tooltip 打开期间每帧都跑，所以按物品和内容缓存最近几张。
    internal static class Layout
    {
        private const int Unlimited = 1000; // RichTextBuilder.maxCharWidth：每行已经由我们排好，不再自动换行

        private sealed class Cached
        {
            public GameItem Item;
            public int Hash;
            public int Version;
            public RichTextBuilder Output;
            public LayoutResult Result;
        }

        private const int CacheSize = 32;
        /// 最近用过的在前。
        private static readonly List<Cached> cache = new List<Cached>();
        private static GameItem loggedItem;
        private static bool sectionsLogged;

        /// 最近一次排版的物品和结果（也就是这一帧的 tooltip）。
        internal static GameItem LastItem;
        internal static LayoutResult LastResult;

        internal static void Apply(Frame f, RichTextBuilder b)
        {
            LastItem = null;
            LastResult = null;
            IList slices = SliceAccess.Slices(b);
            if (slices == null || slices.Count == 0) return;

            System.Diagnostics.Stopwatch timer = Settings.Debug != null && Settings.Debug.Value ? System.Diagnostics.Stopwatch.StartNew() : null;
            frameLayoutMs = frameSignatureMs = frameContentsMs = 0.0;
            frameFresh = false;
            bool contentsPanel = Settings.On(Settings.ContentsPanel);
            int hash = SliceAccess.Hash(slices);
            // tooltip 文本不变，内容物面板也可能变（比如“双击打开”）。
            if (contentsPanel) hash = unchecked(hash * 397) ^ ContentsInfo.Signature(f.Item);
            if (timer != null) frameSignatureMs = timer.Elapsed.TotalMilliseconds;
            Cached hit = Find(f.Item);
            if (hit != null && hit.Hash == hash && hit.Version == Settings.Version)
            {
                Swap(b, hit.Output);
                LastItem = f.Item;
                LastResult = hit.Result;
                if (timer != null) frameLayoutMs = timer.Elapsed.TotalMilliseconds;
                return;
            }
            frameFresh = true;
            TextUnits.Configure();
            LocTemplates.Check();

            List<Seg> segs = new List<Seg>(slices.Count);
            for (int i = 0; i < slices.Count; i++) segs.Add(SliceAccess.Read(slices[i]));

            // 只有 helper 确实写进了这个 builder，marks 才描述它。
            bool ours = f.Builder == null || ReferenceEquals(f.Builder, b);
            List<Mark> marks = ours ? f.Marks : new List<Mark>();
            if (ours) MarkBars(segs, f.Bars);

            int[] ordOf;
            string[] keyOf = AssignKeys(segs.Count, marks, out ordOf);
            List<Line> lines = SplitLines(segs, keyOf, ordOf);
            // Item Details 的区块：把它的行归到对应的小节，不再靠文本猜（没装它时为 null）。
            RekeySections(lines, ItemDetailsSections.Read(b));
            SplitCustomName(lines, f.Item);
            MarkDescription(lines, f.Item);
            // 玩家关掉的部分，两种风格都适用。
            bool desc = Settings.On(Settings.ShowDescription), mods = Settings.On(Settings.ShowModInfo);
            if (!desc || !mods) lines.RemoveAll(l => (!desc && l.Key == Sections.Desc) || (!mods && l.Key == Sections.Mods));

            double beforeContents = timer != null ? timer.Elapsed.TotalMilliseconds : 0.0;
            ContentsInfo contents = contentsPanel ? ContentsInfo.Collect(f.Item) : null;
            if (timer != null) frameContentsMs = timer.Elapsed.TotalMilliseconds - beforeContents;
            if (contents != null) DropContentLists(lines, contents);
            // 估价单把液量条留在液体一节里；只有经典风格把它挪到侧边面板。
            bool liquidPanel = !Settings.IsTicket && Settings.On(Settings.LiquidPanel)
                && Settings.On(Settings.Bars);
            LiquidInfo liquid = liquidPanel ? ExtractLiquid(lines) : null;

            List<Block> blocks = Group(lines);
            int iconCells = 0;
            if (Settings.On(Settings.Icon)) iconCells = IconCellsFor(Overlay.IconOf(f.Item));
            LayoutResult result = new LayoutResult();
            result.Contents = contents;
            result.Liquid = liquid;
            result.Blocks = blocks;
            RichTextBuilder output = Render(blocks, iconCells, result);

            if (timer != null) frameLayoutMs = timer.Elapsed.TotalMilliseconds;
            if (timer != null && !ReferenceEquals(f.Item, loggedItem))
            {
                loggedItem = f.Item;
                SectionedTooltipsMod.Log(string.Format("layout in {0:F1} ms ({1} slices)", timer.Elapsed.TotalMilliseconds, slices.Count));
                Dump(f, lines);
                if (contents != null)
                    for (int i = 0; i < contents.Gauges.Count; i++)
                    {
                        Gauge g = contents.Gauges[i];
                        SectionedTooltipsMod.Log(string.Format("  gauge '{0}' {1:P0} {2} '{3}' #{4}",
                            g.Name, g.Ratio, g.Amount, g.Note, UnityEngine.ColorUtility.ToHtmlStringRGB(g.Fill)));
                    }
            }

            if (hit == null)
            {
                if (cache.Count >= CacheSize) cache.RemoveAt(cache.Count - 1);
                hit = new Cached();
                hit.Item = f.Item;
                cache.Insert(0, hit);
            }
            hit.Output = output;
            hit.Result = result;
            hit.Hash = hash;
            hit.Version = Settings.Version;
            Swap(b, output);
            LastItem = f.Item;
            LastResult = result;
        }

        // 调试用：这一帧排版各段的耗时，给 LogSlowFrame 用。
        private static double frameLayoutMs, frameSignatureMs, frameContentsMs;
        private static bool frameFresh;
        private static int slowLogged;
        private const double SlowFrameMs = 3.0;
        private const int SlowFrameLogs = 400;

        /// 调试用：每次排版太久就记一行，说明游戏和其他模组花了多少、我们花了多少、其中内容物多少。有上限，免得刷满日志。
        internal static void LogSlowFrame(GameItem item, double basicMs)
        {
            Stats.Tooltip(item, basicMs, frameLayoutMs, frameFresh);
            if (basicMs + frameLayoutMs < SlowFrameMs || slowLogged >= SlowFrameLogs) return;
            slowLogged++;
            string name = "";
            try { name = item != null ? item.GetDisplayName(false) : ""; }
            catch { }
            int count = LastResult != null && LastResult.Contents != null ? LastResult.Contents.Total : 0;
            SectionedTooltipsMod.Log(string.Format(
                "slow tooltip frame {0}: '{1}' game+other mods {2:F1} ms, ours {3:F1} ms ({4}; contents fingerprint {5:F1} ms, contents {6:F1} ms, {7} inside)",
                UnityEngine.Time.frameCount, TextUnits.Plain(name ?? ""), basicMs, frameLayoutMs, frameFresh ? "laid out" : "cached",
                frameSignatureMs, frameContentsMs, count));
        }

        /// 这个物品缓存的排版，移到最前；没有则返回 null。
        private static Cached Find(GameItem item)
        {
            for (int i = 0; i < cache.Count; i++)
            {
                Cached c = cache[i];
                if (!ReferenceEquals(c.Item, item)) continue;
                if (i > 0)
                {
                    cache.RemoveAt(i);
                    cache.Insert(0, c);
                }
                return c;
            }
            return null;
        }

        /// 内容物面板已经显示了容器里的东西，所以其他模组加的内容物文字列表要删掉（如“内含 A×2、B、C”）：附加信息、其他两节里点到至少两个条目的行。别处只删写明了是什么、又点到其中一个条目的行（Item Details 的“内含：”、机器面板的标题“输入：A、B”）。
        private static void DropContentLists(List<Line> lines, ContentsInfo contents)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                Line l = lines[i];
                if (l.Key == Sections.Title || l.Key == Sections.Desc) continue;
                string plain = l.Plain.Trim();
                bool drop = false;
                // 至少两个名字：只点到一个更像是在说那件物品（“可用电池：3”）。
                if (l.Key == Sections.Mods || l.Key == Sections.Info) drop = contents.Entries.Count >= 2 && Names(plain, contents.Entries, 2);
                // “某种说明：A、B”这样的清单（Item Details 的“内含：”）：说明文字用什么语言都认。
                if (!drop && TextRules.CaptionList(plain)) drop = Names(plain, contents.Entries, 1);
                for (int g = 0; g < contents.Groups.Count && !drop; g++)
                {
                    string label = contents.Groups[g].Label;
                    if (label.Length == 0 || !plain.StartsWith(label, StringComparison.Ordinal)) continue;
                    string rest = plain.Substring(label.Length).TrimStart();
                    // Item Details 已经在自己的行里概括过的东西（工具、电池），面板标题下点到其中一个就够。
                    if (rest.Length > 0 && (rest[0] == '：' || rest[0] == ':')) drop = Names(rest, contents.Groups[g].Entries, 1);
                }
                // 量条已经说过了：Item Details 的“桶装水：6/6升”、“能量电池：电量 55/100”。
                for (int k = 0; k < contents.Gauges.Count && !drop; k++)
                    drop = l.Key != Sections.Value && (Captioned(plain, contents.Gauges[k].Name) || Captioned(plain, contents.Gauges[k].RawName));
                if (drop) lines.RemoveAt(i);
            }
        }

        /// text 是否是“name：…”这种形式。
        private static bool Captioned(string text, string name)
        {
            name = name.Trim();
            if (name.Length < 2 || !text.StartsWith(name, StringComparison.Ordinal)) return false;
            string rest = text.Substring(name.Length).TrimStart();
            return rest.Length > 1 && (rest[0] == '：' || rest[0] == ':');
        }

        /// text 是否点到至少 need 个条目（条目更少时要全部）。
        private static bool Names(string text, List<ContentEntry> entries, int need)
        {
            int kinds = 0, hits = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                string a = entries[i].Name.Trim(), b = entries[i].RawName.Trim();
                if (a.Length < 2 && b.Length < 2) continue;
                kinds++;
                if ((a.Length >= 2 && text.IndexOf(a, StringComparison.Ordinal) >= 0) || (b.Length >= 2 && text.IndexOf(b, StringComparison.Ordinal) >= 0)) hits++;
            }
            return kinds > 0 && hits >= Math.Min(need, kinds);
        }

        /// 把液体一节的液量条那一行挪进侧边面板的液位计。
        private static LiquidInfo ExtractLiquid(List<Line> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                if (l.Key != Sections.Liquid) continue;
                int bar = -1;
                for (int j = 0; j < l.Segs.Count; j++) if (l.Segs[j].Bar != null) { bar = j; break; }
                if (bar < 0) continue;

                LiquidInfo info = new LiquidInfo();
                info.Ratio = l.Segs[bar].Bar.Ratio;
                info.Color = l.Segs[bar].Bar.Color;
                info.Background = l.Segs[bar].Bar.Background;
                List<Seg> rest = new List<Seg>(l.Segs);
                rest.RemoveAt(bar);
                info.Text = TextUnits.ToSegs(TextUnits.TrimEnd(TextUnits.TrimStart(TextUnits.Of(rest))), rest);
                lines.RemoveAt(i);
                return info;
            }
            return null;
        }

        /// 标出每个 AddProgressBar 写的 seg；开了图形进度条时把它们清空，交给覆盖层画。
        private static void MarkBars(List<Seg> segs, List<BarMark> bars)
        {
            bool drawn = Settings.On(Settings.Bars);
            for (int i = 0; i < bars.Count; i++)
            {
                BarMark m = bars[i];
                for (int j = Math.Max(0, m.Start); j < Math.Min(m.End, segs.Count); j++)
                {
                    Seg s = segs[j];
                    if (s.NewLine || string.IsNullOrEmpty(s.Text)) continue;
                    s.Bar = m;
                    if (!drawn) continue;
                    int cells = Math.Max(6, Math.Min(20, TextUnits.Plain(s.Text).Length));
                    s.Text = TextUnits.BarPlaceholder(cells);
                    s.Background = Palette.None;
                }
            }
        }

        private static void Swap(RichTextBuilder b, RichTextBuilder output)
        {
            b.Clear();
            b.Add(output);
            if (b.maxCharWidth < Unlimited) b.maxCharWidth = Unlimited;
        }

        // ---------------------------------------------------------------- 分节

        private static string[] AssignKeys(int n, List<Mark> marks, out int[] ordOf)
        {
            string[] keys = new string[n];
            ordOf = new int[n];
            for (int j = 0; j < n; j++) ordOf[j] = -1;
            int pos = 0;
            int prevOrd = -1;
            for (int i = 0; i < marks.Count; i++)
            {
                Mark m = marks[i];
                int start = Math.Min(Math.Max(m.Start, pos), n);
                int end = Math.Min(Math.Max(m.End, start), n);
                if (start > pos) Fill(keys, pos, start, GapKey(prevOrd, m.Ord));
                Fill(keys, start, end, Helpers.Key(m.Ord));
                for (int j = start; j < end; j++) ordOf[j] = m.Ord;
                pos = Math.Max(pos, end);
                prevOrd = m.Ord;
            }
            if (pos < n) Fill(keys, pos, n, GapKey(prevOrd, int.MaxValue));
            return keys;
        }

        private static void Fill(string[] keys, int from, int to, string key)
        {
            for (int i = from; i < to; i++) keys[i] = key;
        }

        /// GetTooltipBasic 自己写的文本属于哪一节，位置在 helper prev 和 next 之间。
        private static string GapKey(int prev, int next)
        {
            // 跑过但没留 mark 的 helper（被 JIT 内联，比如 ModInternal 那两行钩子）。只在交易 helper 之后判断：不值钱的物品会跳过它们，之后那段是它自己的“上锁”行，不是模组的。
            int last = Math.Min(next, Helpers.Count);
            if (prev >= Helpers.OrdBarterLast)
                for (int o = prev + 1; o < last; o++) if (Helpers.Key(o) == Sections.Mods) return Sections.Mods;

            if (prev < Helpers.OrdContraband) return Sections.Title;    // 名字、厂商、标记
            if (prev < Helpers.OrdBarterLast) return Sections.Value;    // 价格、上锁标记
            if (Helpers.OrdModsLate >= 0 && prev >= Helpers.OrdModsLate && next <= Helpers.OrdContainer) return Sections.Desc;
            return Sections.Info;
        }

        private static List<Line> SplitLines(List<Seg> segs, string[] keyOf, int[] ordOf)
        {
            List<Line> lines = new List<Line>();
            Line cur = new Line();
            for (int i = 0; i < segs.Count; i++)
            {
                if (segs[i].NewLine)
                {
                    lines.Add(cur);
                    cur = new Line();
                    continue;
                }
                if (cur.Key == null)
                {
                    cur.Key = keyOf[i] ?? Sections.Info;
                    cur.Ord = ordOf[i];
                    cur.FromHelper = cur.Ord >= 0;
                }
                cur.Segs.Add(segs[i]);
            }
            lines.Add(cur);

            List<Line> kept = new List<Line>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                StringBuilder sb = new StringBuilder();
                for (int j = 0; j < l.Segs.Count; j++) sb.Append(l.Segs[j].Text);
                l.Plain = TextUnits.Plain(sb.ToString()).Trim();
                if (l.Plain.Length == 0) continue; // 原版的空行；现在靠小节标题分隔
                kept.Add(l);
            }
            return kept;
        }

        /// 用蓝色贴标机改过名的物品，游戏会把“自定义名 (原名)”写在一行里，名字长了就把估价单撑开。自定义名留在标题，原名换到下一行，像物品标签一样画在名字下面。
        private static void SplitCustomName(List<Line> lines, GameItem item)
        {
            string custom, original;
            try
            {
                if (item == null || !GeneralHelper.HasCustomName(item)) return;
                custom = GeneralHelper.GetCustomName(item);
                original = item.GetDisplayName(false); // 也就是 GetTooltipBasic 写在括号里的内容
            }
            catch { return; }
            if (string.IsNullOrEmpty(custom) || string.IsNullOrEmpty(original)) return;
            string tail = " (" + original + ")";
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                if (l.Key != Sections.Title) continue;
                if (!l.Plain.EndsWith(custom + tail, StringComparison.Ordinal)) return; // 名字是标题的第一行
                StringBuilder sb = new StringBuilder();
                for (int j = 0; j < l.Segs.Count; j++) sb.Append(l.Segs[j].Text);
                string raw = sb.ToString();
                int cut = raw.LastIndexOf(tail, StringComparison.Ordinal);
                if (cut < 0 || raw.Substring(cut + tail.Length).Trim().Length > 0) return;

                // 保留切点之前的片段，切点所在的那段截短。
                List<Seg> kept = new List<Seg>();
                Seg style = null;
                int pos = 0;
                for (int j = 0; j < l.Segs.Count && pos < cut; j++)
                {
                    Seg s = l.Segs[j];
                    style = s;
                    kept.Add(pos + s.Text.Length <= cut ? s : s.With(s.Text.Substring(0, cut - pos)));
                    pos += s.Text.Length;
                }
                if (style == null) return;
                l.Segs = kept;
                l.Plain = TextUnits.Plain(raw.Substring(0, cut)).Trim();

                Line under = new Line();
                under.Key = l.Key;
                under.Ord = l.Ord;
                under.FromHelper = l.FromHelper;
                under.Segs.Add(style.With(original));
                under.Plain = TextUnits.Plain(original).Trim();
                lines.Insert(i + 1, under);
                return;
            }
        }

        /// ModInternal 被内联时，物品自己的描述可能落进“附加信息”的空档里；按文本匹配找回来。
        private static void MarkDescription(List<Line> lines, GameItem item)
        {
            if (item == null) return;
            string desc;
            try { desc = TextUnits.Plain((item.shortDescription ?? "") + "\n" + (item.flavorText ?? "")); }
            catch { return; }
            if (desc.Trim().Length == 0) return;
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                // 绝不碰名字行：很多描述里会提到物品名（“……消音器正好装上”），以前会把名字行拖进描述里。太短的文本容易误匹配，除非它是描述换行后的最后一段（“的。”）。
                int min = i > 0 && lines[i - 1].Key == Sections.Desc ? 1 : 4;
                if (l.FromHelper || l.Key == Sections.Title || l.Plain.Length < min) continue;
                if (desc.IndexOf(l.Plain, StringComparison.Ordinal) >= 0) l.Key = Sections.Desc;
            }
        }

        /// 用 Item Details 的区块把它的行改判到我们的小节。
        private static void RekeySections(List<Line> lines, Dictionary<string, string> sections)
        {
            if (sections == null || sections.Count == 0) return;
            if (!sectionsLogged)
            {
                // 第一次读到区块时记一条，方便确认协议真的通了。
                sectionsLogged = true;
                StringBuilder ids = new StringBuilder();
                foreach (string id in sections.Values) if (ids.ToString().IndexOf(id) < 0) ids.Append(id).Append(' ');
                SectionedTooltipsMod.Log("Item Details 区块：" + ids.ToString().Trim());
            }
            // 水质成分表不在 water 区块里（它由物品详情直接写进 builder），但总是紧跟在
            // 量条那行后面，所以从 water 那一行开始，把后面连续的“制表位 + 百分比”行一起归过去。
            bool afterWater = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string id;
                bool known = sections.TryGetValue(lines[i].Plain, out id);
                string key = known ? SectionOf(id) : null;
                if (key != null) lines[i].Key = key;
                if (!known)
                {
                    if (afterWater && WaterRow(lines[i])) { lines[i].Key = Sections.Liquid; continue; }
                    afterWater = false;
                    continue;
                }
                afterWater = id == "water";
            }
        }

        /// 成分行、展开提示行：以百分比结尾，并且带制表位（“水　9.1L　91.04%”）。
        private static bool WaterRow(Line line)
        {
            if (line.Key != Sections.Mods) return false;
            string plain = line.Plain;
            if (plain.Length < 3 || plain[plain.Length - 1] != '%') return false;
            return plain.IndexOf(TextUnits.IdeographicSpace) >= 0;
        }

        /// 区块 id 对应到我们哪一节；null 表示保持原样。
        private static string SectionOf(string id)
        {
            switch (id)
            {
                case "water":
                    return Sections.Liquid;
                case "seed":
                    return Sections.Plant;
                case "machine-interior":
                    return Sections.Machine;
                case "storage":
                case "sealed-box":
                    // 内容物由我们画的时候，这两块的行整段不要了（装载一节不画）；
                    // 面板关着时保持原样，免得把内容物信息丢掉。
                    return Settings.IsTicket && Settings.On(Settings.ContentsPanel) ? Sections.Container : null;
                default:
                    return null;
            }
        }

        private static List<Block> Group(List<Line> lines)
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
                // 模组的状态和词条是句子，不是“标签: 数值”：词条描述里的冒号会被切成两半。
                Row row = MakeRow(l, maxLabel, l.Key != Sections.Module);
                string key = l.Key;
                if (key == Sections.Dev) key = Sections.Info;
                // 写在价格旁边的标记（“已上锁”之类）放在名字下面更好读。
                if (key == Sections.Value && !row.IsPair) key = Sections.Title;
                if (key == Sections.Title || key == Sections.Desc) row = Plain(l);
                row.Ord = l.Ord;

                if (cur == null || cur.Key != key)
                {
                    cur = new Block();
                    cur.Key = key;
                    blocks.Add(cur);
                }
                cur.Rows.Add(row);
            }
            return blocks;
        }

        // ---------------------------------------------------------------- 标签和数值拆列

        /// 这一行要不要拆成多组“名字 +N%”；标题、描述、价格、标签行不拆。
        private static List<string[]> PercentRows(Line l)
        {
            string key = l.Key;
            if (key == Sections.Title || key == Sections.Desc || key == Sections.Value || key == Sections.Label) return null;
            return TextRules.PercentPairs(l.Plain);
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
            if (split <= 0 && pairs)
            {
                string tailLabel, tailValue;
                if (TextRules.TrailingPercent(l.Plain, out tailLabel, out tailValue))
                {
                    Row pr = new Row();
                    pr.Label = new List<Seg>();
                    Seg name = new Seg();
                    name.Text = tailLabel;
                    pr.Label.Add(name);
                    pr.Value = new List<Seg>();
                    Seg num = new Seg();
                    num.Text = tailValue;
                    pr.Value.Add(num);
                    return pr;
                }
            }

            if (split > 0)
            {
                List<Unit> label = TextUnits.TrimEnd(u.GetRange(0, split));
                List<Unit> value = TextUnits.TrimStart(u.GetRange(valueStart, u.Count - valueStart));
                string lp = TextUnits.Plain(label).Trim();
                string vp = TextUnits.Plain(value).Trim();
                if (pairs && lp.Length > 0 && vp.Length > 0 && TextUnits.Width(label) <= maxLabel && !char.IsDigit(lp[lp.Length - 1]))
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

        // ---------------------------------------------------------------- 输出

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

        // ---------------------------------------------------------------- 诊断

        private static void Dump(Frame f, List<Line> lines)
        {
            StringBuilder sb = new StringBuilder();
            string name = "?";
            try { name = f.Item != null ? f.Item.GetDisplayName(false) : "?"; } catch { }
            sb.AppendLine("tooltip of '" + name + "': " + f.Marks.Count + " helper ranges");
            for (int i = 0; i < f.Marks.Count; i++)
            {
                Mark m = f.Marks[i];
                if (m.End > m.Start) sb.AppendLine(string.Format("  #{0} {1} [{2},{3})", m.Ord, Helpers.Name(m.Ord), m.Start, m.End));
            }
            for (int i = 0; i < lines.Count; i++)
                sb.AppendLine(string.Format("  [{0}{1}] {2}", lines[i].Key, lines[i].FromHelper ? "" : "*", lines[i].Plain));
            SectionedTooltipsMod.Log(sb.ToString());
        }
    }
}
