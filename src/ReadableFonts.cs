using System;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// 和《字体大小优化 | Readable Fonts》（创意工坊 3832924001）的兼容。不引用它的 DLL，只按它在场景里留下的对象判断它开着没有。
    /// 它每秒把场景里所有 TextMeshPro 文字扫一遍：中文界面下 16 号以下的字改成 ×1.2（至少 10、最多 16），16 号及以上不动；
    /// 游戏自己的 tooltip 文字它会保护起来，我们的估价单不在保护范围里。估价单排版时量的是原字号，被它放大后就会重叠。
    /// 所以它开着时估价单自己放大 Boost 倍，并把字号整体抬到 Floor 以上、卡片按同样比例缩回去（见 Ticket.BuildCard）。
    /// </summary>
    internal static class ReadableFonts
    {
        /// 它给阅读文字的放大比例（0.1.8 的 ReadingPercent = 120）。
        internal const float Boost = 1.2f;
        /// 它只改 16 号以下的字；抬到比这略大，取整也不会掉回去。
        internal const float Floor = 16.5f;
        /// 它启用时建的常驻对象。
        private const string ControllerName = "ReadableFonts.Controller";

        private static float checkedAt = -10f;
        private static bool active;

        internal static bool Active
        {
            get
            {
#if DEVDUMP
                if (Stress.EmulateReadableFonts) return true;
#endif
                if (Time.unscaledTime - checkedAt > 2f)
                {
                    checkedAt = Time.unscaledTime;
                    bool was = active;
                    active = false;
                    try { active = GameObject.Find(ControllerName) != null && Chinese(); }
                    catch { }
                    // 存着的估价单是按另一种字号建的：换语言或开关它之后都要重建。
                    if (active != was) Settings.Version++;
                }
                return active;
            }
        }

        /// 它只在中文界面生效（“zh”或“zh-…”）。
        private static bool Chinese()
        {
            string code = LocHelper.GetCurrentLocaleCode() ?? "";
            return string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
