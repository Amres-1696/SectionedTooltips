using System;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// 估价单的收起和展开：平时收起价格明细、内容物、描述等（哪些由设置决定），按住展开键时显示完整的单子；
    /// 也可以设成按一下展开、再按一下收起。另有一个翻页键，切换装箱单显示储藏区的哪一页。
    /// 按键是一份固定的列表，设置里用下拉框选（游戏的模组设置没有“按一个键”的控件）。
    /// </summary>
    internal static class Expand
    {
        /// 设置里的选项，按顺序和 Codes、Caps 一一对应。改顺序会让已保存的值对不上，只能往后加。
        internal static readonly string[] KeyNames =
        {
            "Shift", "Ctrl", "Alt", "Tab", "空格 Space",
            "Q", "E", "R", "F", "X", "Z", "C", "V",
            "鼠标侧键 4 Mouse 4", "鼠标侧键 5 Mouse 5",
        };

        private static readonly KeyCode[][] Codes =
        {
            new[] { KeyCode.LeftShift, KeyCode.RightShift },
            new[] { KeyCode.LeftControl, KeyCode.RightControl },
            new[] { KeyCode.LeftAlt, KeyCode.RightAlt },
            new[] { KeyCode.Tab },
            new[] { KeyCode.Space },
            new[] { KeyCode.Q }, new[] { KeyCode.E }, new[] { KeyCode.R }, new[] { KeyCode.F },
            new[] { KeyCode.X }, new[] { KeyCode.Z }, new[] { KeyCode.C }, new[] { KeyCode.V },
            new[] { KeyCode.Mouse3 }, new[] { KeyCode.Mouse4 },
        };

        /// 估价单底部提示里画在键帽里的字。
        private static readonly string[] Caps =
        {
            "Shift", "Ctrl", "Alt", "Tab", "Space",
            "Q", "E", "R", "F", "X", "Z", "C", "V",
            "M4", "M5",
        };

        /// 展开键的两种用法，原样显示在设置菜单里。
        internal static readonly string[] Modes = { "按住 Hold", "按一下切换 Toggle" };

        private static int Index { get { return KeyIndex(Settings.ExpandKey, 0); } }

        /// 翻页键默认是 Tab（游戏自己没有用它）。
        internal const int DefaultPageKey = 3;

        private static int PageIndex { get { return KeyIndex(Settings.PageKey, DefaultPageKey); } }

        private static int KeyIndex(ConfigEntry<string> e, int def)
        {
            int i = e != null ? Array.IndexOf(KeyNames, e.Value) : def;
            return i >= 0 && i < Codes.Length ? i : def;
        }

        private static bool Toggles { get { return Settings.ExpandMode != null && Settings.ExpandMode.Value == Modes[1]; } }

        /// 切换模式下现在是不是展开着。
        private static bool toggled;
        private static int polledFrame = -1;

        /// 每帧读一次按键（游戏每帧问鼠标下的物品有没有 tooltip 时调用，见 HasTooltipPatch）：
        /// 切换模式下按一下展开键换一次状态；按翻页键让装箱单看下一页。只在鼠标停在物品上时读。
        internal static void Poll(GameItem item)
        {
            int frame = Time.frameCount;
            if (frame == polledFrame) return;
            polledFrame = frame;
            try
            {
                if (!Settings.On(Settings.Enabled)) return;
                int key = Index, page = PageIndex;
                if (Toggles && Down(Codes[key])) toggled = !toggled;
                ContentsInfo.Hover(item, page != key && Down(Codes[page]));
            }
            catch { }
        }

        private static bool Down(KeyCode[] keys)
        {
            for (int i = 0; i < keys.Length; i++) if (Input.GetKeyDown(keys[i])) return true;
            return false;
        }

        /// 翻页键画在页码旁边的字；翻页键和展开键相同（翻页不起作用）时为 null。
        internal static string PageCap { get { int p = PageIndex; return p != Index ? Caps[p] : null; } }

        /// 换了展开方式：切换模式下留下的展开状态不带过去。
        internal static void ResetToggle() { toggled = false; }

        /// 收起功能开着吗。
        internal static bool Enabled { get { return Settings.On(Settings.Collapse); } }

        /// 现在要显示完整的单子吗：收起功能关着，或者展开键按着（切换模式下是切到了展开）。
        internal static bool Full { get { return !Enabled || KeyHeld; } }

        /// 展开键现在按着吗；切换模式下是现在是不是切到了展开。
        internal static bool KeyHeld
        {
            get
            {
                if (Toggles) return toggled;
                KeyCode[] keys = Codes[Index];
                try
                {
                    for (int i = 0; i < keys.Length; i++) if (Input.GetKey(keys[i])) return true;
                }
                catch { }
                return false;
            }
        }

        /// 平时不弹物品详情、按住展开键才显示（设置里开了才这样），而现在键没按着。
        internal static bool Concealed
        {
            get { return Settings.ShowOnKey != null && Settings.ShowOnKey.Value && Settings.On(Settings.Enabled) && !KeyHeld; }
        }

        internal static string Cap { get { return Caps[Index]; } }

        /// 这一部分平时收起吗（收起功能开着、这一项勾着）。
        internal static bool Folds(ConfigEntry<bool> part)
        {
            return Enabled && part != null && part.Value;
        }
    }
}
