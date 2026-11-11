using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 把 GetTooltipBasic 生成好的 builder 重新排一遍：每行归到某一节，“标签: 数值”拆成两列，再写回 builder。tooltip 打开期间每帧都跑，所以按物品和内容缓存最近几张。
    internal static partial class Layout
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
            bool stress = false;
#if DEVDUMP
            // 压力测试要这件物品分节前的行，不能走缓存。
            stress = Stress.Wanted(f.Item);
#endif
            if (hit != null && hit.Hash == hash && hit.Version == Settings.Version && !stress)
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
#if DEVDUMP
            if (DumpAll.Running) DumpAll.Input(lines);
#endif
            // 其他模组写的行先修成我们认识的样子（归到对应小节、换成我们画的条），再做下面的筛选。
            Compat.Normalize(f.Item, b, lines);
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

#if DEVDUMP
            if (stress) Stress.Prepare(f.Item, lines, contents);
            if (DumpAll.Running) DumpAll.GroupInput(lines);
#endif
            List<Block> blocks = Group(lines);
#if DEVDUMP
            if (DumpAll.Running)
            {
                // 经典风格把最终文字也记下来；估价单的卡片要 UI，靠 [slip] 日志看。
                LayoutResult classic = null;
                RichTextBuilder classicText = null;
                if (!Settings.IsTicket)
                {
                    classic = new LayoutResult();
                    classicText = Render(blocks, Settings.On(Settings.Icon) ? IconCellsFor(Icons.IconOf(f.Item)) : 0, classic);
                }
                DumpAll.Take(f.Item, blocks, classicText, classic);
                LastItem = f.Item;
                return;
            }
#endif
            int iconCells = 0;
            if (Settings.On(Settings.Icon)) iconCells = IconCellsFor(Icons.IconOf(f.Item));
            LayoutResult result = new LayoutResult();
            result.Contents = contents;
            result.Liquid = liquid;
            result.Blocks = blocks;
            RichTextBuilder output = Render(blocks, iconCells, result);
#if DEVDUMP
            if (timer != null && !Settings.IsTicket) DumpAll.LogClassic(f.Item, output, result);
#endif

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

        /// 按当前的设置和语言并块、拆列。
        internal static List<Block> Group(List<Line> lines)
        {
            return Group(lines, RowOptionsNow());
        }

        internal static RowOptions RowOptionsNow()
        {
            RowOptions o;
            o.Ticket = Settings.IsTicket;
            o.MaxChars = Settings.MaxChars;
            o.FractionBars = Settings.On(Settings.Bars) && Settings.On(Settings.FractionBars);
            o.TemplateKnown = LocTemplates.Known;
            o.TemplatePrefix = LocTemplates.Prefix;
            o.TemplateMid = LocTemplates.Mid;
            return o;
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
