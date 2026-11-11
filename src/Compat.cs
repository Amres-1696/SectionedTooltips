using System;
using System.Collections.Generic;

namespace SectionedTooltips
{
    /// <summary>
    /// 和其他模组的兼容都从这里进出。核心流程（ModEntry、Layout、Ticket）只调这里的钩子，不出现别的模组的名字；
    /// 每个模组具体怎么对接写在它自己的文件里（ItemDetailsBridge、ItemDetailsSections、AquaSense……）。
    ///
    /// 每个兼容件有自己的熔断：连续出错只停用它自己，那个模组的内容退回普通文字，估价单照常排。
    /// 加新的兼容：在对应的钩子里加一行 Run(键, …)，不改核心流程。
    /// </summary>
    internal static class Compat
    {
        private const string ItemDetails = "兼容·Item Details";
        private const string AquaSenseKey = "兼容·AquaSense";

        /// 模组启用时：向其他模组声明我们的存在（它们读不读都行）。
        internal static void Register()
        {
            Start(ItemDetails, ItemDetailsBridge.Register);
            Start(ItemDetails, ItemDetailsSections.Register);
        }

        /// 模组停用时：撤回 Register 里的声明。
        internal static void Unregister()
        {
            Start(ItemDetails, ItemDetailsSections.Unregister);
            Start(ItemDetails, ItemDetailsBridge.Unregister);
        }

        /// 拆完行、还没删任何行之前：把其他模组写的行修成我们认识的样子（归到它们声明的小节、换成我们画的条）。
        /// 每次重新排版跑一次（命中缓存时不跑）。
        internal static void Normalize(GameItem item, RichTextBuilder builder, List<Line> lines)
        {
            if (Usable(ItemDetails))
            {
                try { ItemDetailsSections.Apply(lines, builder); Fuse.Ok(ItemDetails); }
                catch (Exception e) { Fuse.Fail(ItemDetails, e); }
            }
            if (Usable(AquaSenseKey))
            {
                try { AquaSense.Apply(lines, item); Fuse.Ok(AquaSenseKey); }
                catch (Exception e) { Fuse.Fail(AquaSenseKey, e); }
            }
        }

        private const string ReadableFontsKey = "兼容·字体大小优化";

        /// 建单时：有模组会在估价单建好后改小字号时返回 true。boost 是它放大的倍数（我们先自己放大，排版时就按放大后的量）；
        /// floor 是它不再动的字号，所有字抬到这以上它就不碰（整张再按比例缩回去，看起来一样大）。
        internal static bool SmallTextResized(out float boost, out float floor)
        {
            boost = 1f;
            floor = 0f;
            if (!Usable(ReadableFontsKey)) return false;
            try
            {
                bool on = ReadableFonts.Active;
                if (on)
                {
                    boost = ReadableFonts.Boost;
                    floor = ReadableFonts.Floor;
                }
                Fuse.Ok(ReadableFontsKey);
                return on;
            }
            catch (Exception e)
            {
                Fuse.Fail(ReadableFontsKey, e);
                return false;
            }
        }

        private static bool Usable(string key) { return !Fuse.IsOpen(key); }

        /// 注册、撤回这类一次性的动作：出错只记日志，不算熔断。
        private static void Start(string key, Action action)
        {
            try { action(); }
            catch (Exception e) { SectionedTooltipsMod.Log(key + " 注册/撤回失败：" + e.Message); }
        }
    }
}
