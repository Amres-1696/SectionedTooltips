#if DEVDUMP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// dev 构建专用：把游戏里**全部**物品的估价单排版结果导出成文本，不用逐个悬停。
    ///
    /// 触发：模组数据目录里有 dumpall.flag（读档后第一次悬停物品时检查；换了风格再悬停会按新风格再导一次）。
    /// 产物：模组数据目录的 alldump.txt（最近一次）和 alldump-ticket.txt / alldump-classic.txt，tool\view.js --all 读。
    /// 每件物品记三样：&lt; 排版前的原始行、分节后的行、&gt; 经典风格的最终文字（只有经典风格才有）。
    /// 改代码前后各导一次，view.js --all --diff 就能逐行比。
    ///
    /// 要拿到原生 tooltip 必须反射调用 private 的 GameItemElement.GetTooltipBasic，
    /// 所以这个文件只在 build.bat 传 dev 时编译（清单里同时声明 reflection-external）。
    /// </summary>
    internal static class DumpAll
    {
        private const string ModId = "amres.sectioned_tooltips";
        private const string Flag = "dumpall.flag";
        private const string Output = "alldump.txt";

        /// 正在导出：这时候 Layout 只记录、不画卡片。
        internal static bool Running;

        /// 已经按这种风格导出过（或看过没有 flag）；换风格后重新检查。
        private static bool? doneTicket;
        private static readonly StringBuilder Text = new StringBuilder(1 << 20);
        private static readonly List<string> Failed = new List<string>();
        private static readonly List<string> input = new List<string>();
        private static string currentId;

        /// 每次排版时调用：这种风格还没导出过、又有 flag，就开始导出。
        internal static void Check()
        {
            bool ticket = Settings.IsTicket;
            if (doneTicket == ticket) return;
            doneTicket = ticket;
            try
            {
                if (!ModHelper.ModDataFileExists(ModId, Flag))
                {
                    string folder = null;
                    try { folder = ModHelper.GetModDataFolder(ModId); } catch { }
                    SectionedTooltipsMod.Log("全量导出：没找到 " + Flag + "，模组数据目录是 " + (folder ?? "?"));
                    return;
                }
                Run(ticket);
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("全量导出失败：" + e.Message);
            }
        }

        private static void Run(bool ticket)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            int count = 0;
            Text.Length = 0;
            Failed.Clear();
            Header(ticket);
            Running = true;
            try
            {
                System.Reflection.MethodInfo get = AccessTools.Method(typeof(GameItemElement), "GetTooltipBasic", Type.EmptyTypes);
                if (get == null) throw new Exception("找不到 GameItemElement.GetTooltipBasic");

                List<string> ids = DirectoryMaster.GetIdentifierList<GameItem>();
                SectionedTooltipsMod.Log(string.Format("全量导出开始：{0} 个物品，{1}", ids != null ? ids.Count : 0, ticket ? "估价单" : "经典"));
                if (ids == null) return;

                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    GameItem item = null;
                    try
                    {
                        item = DirectoryMaster.Create<GameItem>(id);
                        if (item == null) { Failed.Add(id + "：创建失败"); continue; }
                        input.Clear();
                        currentId = id;
                        get.Invoke(item, null);   // 我们的 finalizer 会走 Layout，把结果交给 Take
                        count++;
                    }
                    catch (Exception e)
                    {
                        Failed.Add(id + "：" + e.GetType().Name + " " + e.Message);
                    }
                    finally
                    {
                        if (item != null)
                        {
                            try { item.Destroy(); } catch { }
                        }
                    }
                }
            }
            finally
            {
                Running = false;
            }

            try
            {
                Text.Append("### 导出完成：").Append(count).Append(" 件，用时 ")
                    .Append(Math.Round(watch.Elapsed.TotalSeconds, 1)).Append(" 秒\n");
                for (int i = 0; i < Failed.Count && i < 40; i++) Text.Append("### 失败：").Append(Failed[i]).Append('\n');
                string all = Text.ToString();
                ModHelper.WriteModDataFileText(ModId, Output, all);
                ModHelper.WriteModDataFileText(ModId, ticket ? "alldump-ticket.txt" : "alldump-classic.txt", all);
                SectionedTooltipsMod.Log(string.Format("全量导出完成：{0} 件，{1} 件失败，用时 {2:F1} 秒",
                    count, Failed.Count, watch.Elapsed.TotalSeconds));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("全量导出写文件失败：" + e.Message);
            }
        }

        /// 文件头：影响排版的设置，以及哪些模组改了 tooltip。对比时先看这几行一样不一样。
        private static void Header(bool ticket)
        {
            Text.Append("### 风格：").Append(ticket ? "估价单" : "经典").Append('\n');
            Text.Append("### 设置：");
            Text.Append("图标=").Append(On(Settings.Icon)).Append(" 图形条=").Append(On(Settings.Bars))
                .Append(" 分数条=").Append(On(Settings.FractionBars)).Append(" 内容物面板=").Append(On(Settings.ContentsPanel))
                .Append(" 液体面板=").Append(On(Settings.LiquidPanel)).Append(" 描述=").Append(On(Settings.ShowDescription))
                .Append(" 价格明细=").Append(On(Settings.ShowPriceDetails)).Append(" 附加信息=").Append(On(Settings.ShowModInfo))
                .Append(" 树线=").Append(On(Settings.Tree)).Append(" 最宽=").Append(Settings.MaxChars)
                .Append(" 字体大小优化=").Append(ReadableFonts.Active ? 1 : 0)
                .Append(" 语言=").Append(LocHelper.GetCurrentLocaleCode() ?? "?").Append('\n');
            Text.Append("### 改 tooltip 的模组：").Append(string.Join("、", PatchOwners().ToArray())).Append('\n');
        }

        private static int On(ConfigEntry<bool> e) { return Settings.On(e) ? 1 : 0; }

        /// 给 GetTooltipBasic、各个 tooltip helper 以及名字里带 Tooltip 的方法打补丁的 Harmony id。
        private static List<string> PatchOwners()
        {
            SortedDictionary<string, bool> owners = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            try
            {
                foreach (MethodBase m in Harmony.GetAllPatchedMethods())
                {
                    if (m == null) continue;
                    if (m.Name.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) < 0 && Helpers.Ordinal(m) < 0) continue;
                    Patches p = Harmony.GetPatchInfo(m);
                    if (p == null) continue;
                    foreach (string o in p.Owners) owners[o] = true;
                }
            }
            catch (Exception e) { owners["（读不到：" + e.Message + "）"] = true; }
            return new List<string>(owners.Keys);
        }

        /// 由 Layout.Apply 在拆完行、还没动它们时调用：排版前的原始行。
        internal static void Input(List<Line> lines)
        {
            for (int i = 0; i < lines.Count; i++) input.Add(lines[i].Key + "\t" + Flat(lines[i].Plain));
        }

        /// 由 Layout.Apply 在算完分节后调用：把这张单子记下来，不画卡片。经典风格时 classic 是它最终的文字。
        internal static void Take(GameItem item, List<Block> blocks, RichTextBuilder classic, LayoutResult classicResult)
        {
            try
            {
                // 名字后面跟物品 id：有些物品同名，对比时按 id 配对。
                Text.Append("=== ").Append(Flat(Name(item))).Append('\t').Append(currentId ?? "?").Append('\n');
                for (int i = 0; i < input.Count; i++) Text.Append("\t<\t").Append(input[i]).Append('\n');
                input.Clear();
                for (int i = 0; i < blocks.Count; i++)
                {
                    Block bl = blocks[i];
                    for (int j = 0; j < bl.Rows.Count; j++)
                    {
                        Row row = bl.Rows[j];
                        Text.Append('\t').Append(bl.Key).Append('\t');
                        if (row.Heading) Text.Append("(标题) ").Append(Flat(Ticket.PlainOf(row.Label)));
                        else if (row.IsPair)
                        {
                            Text.Append(Flat(Ticket.PlainOf(row.Label))).Append(" = ")
                                .Append(Flat(Ticket.PlainOf(row.Value)));
                            if (row.Wide) Text.Append("  [整行]");
                        }
                        else Text.Append(Flat(Ticket.PlainOf(row.Segs)));
                        Text.Append(Marks(row));
                        Text.Append('\n');
                    }
                }
                if (classic != null)
                {
                    List<string> lines = PlainLines(classic);
                    for (int i = 0; i < lines.Count; i++) Text.Append("\t>\t").Append(Flat(lines[i])).Append('\n');
                    if (classicResult != null)
                        for (int i = 0; i < classicResult.Bars.Count; i++) Text.Append("\t>\t").Append(BarText(classicResult.Bars[i])).Append('\n');
                }
            }
            catch (Exception e)
            {
                Text.Append("### 记录失败：").Append(e.Message).Append('\n');
            }
        }

        /// 这一行文字里看不出来、但重构时容易弄丢的东西：进度条、分列、用到的颜色。
        private static string Marks(Row row)
        {
            StringBuilder sb = new StringBuilder();
            BarMark bar = null;
            SortedDictionary<string, bool> colors = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            Scan(row.Label, ref bar, colors);
            Scan(row.Value, ref bar, colors);
            Scan(row.Segs, ref bar, colors);
            if (bar != null) sb.Append("  [条 ").Append(Mathf.RoundToInt(bar.Ratio * 100f)).Append("%]");
            if (row.Cells != null && row.Cells.Count > 1) sb.Append("  [").Append(row.Cells.Count).Append(" 列]");
            if (colors.Count > 0) sb.Append("  {").Append(string.Join(",", new List<string>(colors.Keys).ToArray())).Append('}');
            return sb.ToString();
        }

        private static void Scan(List<Seg> segs, ref BarMark bar, SortedDictionary<string, bool> colors)
        {
            if (segs == null) return;
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (s.Bar != null && bar == null) bar = s.Bar;
                if (s.Color != RenderHandler.ColorPalette.None && TextUnits.Plain(s.Text).Trim().Length > 0) colors[s.Color.ToString()] = true;
            }
        }

        private static string BarText(OverlayBar b)
        {
            return string.Format(CultureInfo.InvariantCulture, "[条] 第 {0} 行 {1}+{2} {3}% {4}/{5}",
                b.Line, b.Char, b.Count, Mathf.RoundToInt(b.Ratio * 100f), b.Color, b.Background);
        }

        /// builder 里的文字按行拆开，去掉标签和零宽空格，行尾空白去掉（行首的缩进留着，对齐就看它）。
        internal static List<string> PlainLines(RichTextBuilder b)
        {
            List<string> lines = new List<string>();
            IList slices = SliceAccess.Slices(b);
            if (slices == null) return lines;
            StringBuilder cur = new StringBuilder();
            for (int i = 0; i < slices.Count; i++)
            {
                Seg s = SliceAccess.Read(slices[i]);
                if (s.NewLine)
                {
                    lines.Add(Trimmed(cur));
                    cur.Length = 0;
                    continue;
                }
                cur.Append(s.Text);
            }
            if (cur.Length > 0) lines.Add(Trimmed(cur));
            return lines;
        }

        private static string Trimmed(StringBuilder sb)
        {
            return TextUnits.Plain(sb.ToString()).Replace(TextUnits.ZeroWidthSpace.ToString(), "").TrimEnd();
        }

        /// 记录分组日志开着、又是经典风格时，每次重新排版写一行 [classic]（同一件物品文字没变就不重复写），view.js --diff 拿它比。
        private static readonly Dictionary<string, string> logged = new Dictionary<string, string>();

        internal static void LogClassic(GameItem item, RichTextBuilder output, LayoutResult result)
        {
            try
            {
                string name = Name(item).Replace('|', '｜').Replace('\n', ' ');
                StringBuilder sb = new StringBuilder(1024);
                sb.Append("[classic] ").Append(name);
                List<string> lines = PlainLines(output);
                for (int i = 0; i < lines.Count; i++) sb.Append(" | ").Append(lines[i].Replace('|', '｜'));
                if (result != null)
                    for (int i = 0; i < result.Bars.Count; i++) sb.Append(" | ").Append(BarText(result.Bars[i]));
                string text = sb.ToString();
                string last;
                if (logged.TryGetValue(name, out last) && last == text) return;
                if (logged.Count > 2000) logged.Clear();
                logged[name] = text;
                SectionedTooltipsMod.Log(text);
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("导出经典风格文字失败：" + e.Message);
            }
        }

        private static string Name(GameItem item)
        {
            try { return item != null ? item.GetDisplayName(false) : "?"; }
            catch { return "?"; }
        }

        private static string Flat(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
#endif
