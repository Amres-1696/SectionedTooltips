using System;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// 估价单的收起和展开：平时收起价格明细、内容物、描述等（哪些由设置决定），按住展开键时显示完整的单子。
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

        private static int Index
        {
            get
            {
                int i = Settings.ExpandKey != null ? Array.IndexOf(KeyNames, Settings.ExpandKey.Value) : 0;
                return i >= 0 && i < Codes.Length ? i : 0;
            }
        }

        /// 收起功能开着吗。
        internal static bool Enabled { get { return Settings.On(Settings.Collapse); } }

        /// 现在要显示完整的单子吗：收起功能关着，或者展开键按着。
        internal static bool Full
        {
            get
            {
                if (!Enabled) return true;
                KeyCode[] keys = Codes[Index];
                try
                {
                    for (int i = 0; i < keys.Length; i++) if (Input.GetKey(keys[i])) return true;
                }
                catch { }
                return false;
            }
        }

        internal static string Cap { get { return Caps[Index]; } }

        /// 这一部分平时收起吗（收起功能开着、这一项勾着）。
        internal static bool Folds(ConfigEntry<bool> part)
        {
            return Enabled && part != null && part.Value;
        }
    }
}
