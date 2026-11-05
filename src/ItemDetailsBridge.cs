using System;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// 和 Item Details 的对接。它把几项协议放在 AppDomain 的固定槽位里，我们只往里放委托，不引用它的 DLL：
    /// tooltip_layout 逐件说明谁在画内容物，tooltip_anchor 让它知道估价单在哪。
    /// 于是分类标签和内容物浮窗由它自己贴过来，不用我们每帧手动搬。
    /// 槽位名和位标志取自 item-details 的 sdk/ItemDetailsClient.cs（v1）。
    /// </summary>
    internal static class ItemDetailsBridge
    {
        private const string LayoutSlot = "item_details.tooltip_layout.v1";
        private const string AnchorSlot = "item_details.tooltip_anchor.v1";

        // TooltipLayoutOptions 的三位
        private const int NativeProgressBars = 1;
        private const int FlowingText = 2;
        private const int DrawsContents = 4;

        private static Func<GameItem, int?> layout;
        private static Func<RectTransform> anchor;

        internal static void Register()
        {
            if (layout == null)
            {
                layout = Layout;
                anchor = Anchor;
            }
            Add(LayoutSlot, layout);
            Add(AnchorSlot, anchor);
            SectionedTooltipsMod.Log("Item Details：已声明排版和 tooltip 锚点");
        }

        internal static void Unregister()
        {
            Remove(LayoutSlot, layout);
            Remove(AnchorSlot, anchor);
        }

        private static void Add(string slot, Delegate callback)
        {
            try
            {
                AppDomain domain = AppDomain.CurrentDomain;
                domain.SetData(slot, Delegate.Combine(domain.GetData(slot) as Delegate, callback));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("Item Details 槽位 " + slot + " 注册失败：" + e.Message);
            }
        }

        private static void Remove(string slot, Delegate callback)
        {
            try
            {
                AppDomain domain = AppDomain.CurrentDomain;
                domain.SetData(slot, Delegate.Remove(domain.GetData(slot) as Delegate, callback));
            }
            catch { }
        }

        /// <summary>这一件物品的排版声明；null 表示不表态，让它按默认处理。</summary>
        private static int? Layout(GameItem item)
        {
            if (item == null) return null;
            if (!Settings.On(Settings.Enabled)) return null;
            // 已经熔断时不画任何东西，别把内容物也从它那里要过来。
            if (Fuse.IsOpen(Fuse.LayoutKey) || Fuse.IsOpen(Fuse.TicketKey)) return null;
            int options = NativeProgressBars | FlowingText;
            if (DrawsContentsNow(item)) options |= DrawsContents;
            return options;
        }

        /// <summary>这一件的内容物确实由我们画；上锁、密封的箱子留给 Item Details。</summary>
        private static bool DrawsContentsNow(GameItem item)
        {
            if (!Settings.On(Settings.ContentsPanel)) return false;
            try { return ContentsInfo.Signature(item) != 0; }
            catch { return false; }
        }

        /// <summary>估价单的根；没显示时返回 null，它就退回原版窗口。</summary>
        private static RectTransform Anchor()
        {
            try { return Ticket.Card; }
            catch { return null; }
        }
    }
}
