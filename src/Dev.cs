using System.Collections.Generic;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// dev 构建的工具（全量导出 DumpAll、压力测试和样本 Stress、截图 Shots）都从这里接进正式流程。
    /// 正式流程只调这里的钩子，不写 #if；普通构建里这些钩子是空的，那几个工具的文件整个不编译。
    /// 要用到 Ticket 私有成员的钩子是 Ticket 的 partial 方法（DevBeforeShow、DevShown），实现在 Stress.cs。
    /// </summary>
    internal static class Dev
    {
#if DEVDUMP
        /// 正在全量导出：Layout 只记录、不画卡片。
        internal static bool Dumping { get { return DumpAll.Running; } }

        /// 正在建的压力用例或样本的名字；没在跑时为 null。
        internal static string Label { get { return Stress.Label; } }

        /// 当前用例要当作开着「字体大小优化」。
        internal static bool BigFonts { get { return Stress.EmulateReadableFonts; } }

        /// 这件物品要不要跑压力测试；要的话 Layout.Apply 不走缓存。
        internal static bool WantsStress(GameItem item) { return Stress.Wanted(item); }

        /// 每次排版前：有 flag 而这种风格还没导出过，就开始全量导出。
        internal static void BeforeLayout()
        {
            try { DumpAll.Check(); } catch { }
        }

        /// 拆完行、其他模组的行还没归一化之前。
        internal static void LinesSplit(List<Line> lines)
        {
            if (DumpAll.Running) DumpAll.Input(lines);
        }

        /// 并块之前：行已经筛好。
        internal static void BeforeGroup(GameItem item, List<Line> lines, ContentsInfo contents, bool stress)
        {
            if (stress) Stress.Prepare(item, lines, contents);
            if (DumpAll.Running) DumpAll.GroupInput(lines);
        }

        /// 全量导出时：这件物品的分节结果（经典风格连同最终文字）。
        internal static void Dumped(GameItem item, List<Block> blocks, RichTextBuilder classicText, LayoutResult classic)
        {
            DumpAll.Take(item, blocks, classicText, classic);
        }

        /// 经典风格排完一件物品。
        internal static void ClassicDone(GameItem item, RichTextBuilder output, LayoutResult result)
        {
            DumpAll.LogClassic(item, output, result);
        }

        /// 一张单子建完、写日志之前。
        internal static void AfterBuild(RectTransform card) { Stress.AfterBuild(card); }
#else
        internal static bool Dumping { get { return false; } }
        internal static string Label { get { return null; } }
        internal static bool BigFonts { get { return false; } }
        internal static bool WantsStress(GameItem item) { return false; }
        internal static void BeforeLayout() { }
        internal static void LinesSplit(List<Line> lines) { }
        internal static void BeforeGroup(GameItem item, List<Line> lines, ContentsInfo contents, bool stress) { }
        internal static void Dumped(GameItem item, List<Block> blocks, RichTextBuilder classicText, LayoutResult classic) { }
        internal static void ClassicDone(GameItem item, RichTextBuilder output, LayoutResult result) { }
        internal static void AfterBuild(RectTransform card) { }
#endif
    }
}
