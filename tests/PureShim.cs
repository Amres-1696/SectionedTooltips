#pragma warning disable 0649
// 离线自检用的替身：游戏和 Unity 的几个类型，桌面 .NET Framework 装载不了真的。
// 只提供被测文件（TextRules、TextUnits、LayoutModel、LayoutRows、Sections、RowCorpus）用到的成员，
// 只在 tests\test.bat 的编译里出现，不进 mod 产物。

/// 游戏的调色板。名字和数值照抄游戏 0.46D 的 RenderHandler.ColorPalette（数值是 RGB）。
internal static class RenderHandler
{
    internal enum ColorPalette
    {
        None = -1, White = 12895408, VeryLightGray = 11058895, LightGray = 8624059, Gray = 8950922, DarkGray = 5198736,
        VeryDarkGray = 4275576, Black = 2297671, LightPurple = 11567781, Purple = 10312861, DarkPurple = 8407419,
        VeryLightRed = 12822958, LightRed = 12615037, Red = 12472652, DarkRed = 9454675, VeryDarkRed = 6502234,
        LightOrange = 12092762, Orange = 11362567, DarkOrange = 8932401, LightYellow = 9672266, Yellow = 8877056,
        DarkYellow = 6511905, VeryLightGreen = 10533268, LightGreen = 8035658, Green = 5538048, DarkGreen = 4875297,
        VeryDarkGreen = 4212545, LightTurquoise = 4366987, Turquoise = 35154, DarkTurquoise = 1206625,
        VeryLightCyan = 8699076, LightCyan = 4367018, Cyan = 35216, DarkCyan = 1206656, VeryDarkCyan = 2378096,
        LightBlue = 4365501, Blue = 31927, DarkBlue = 1204883, EGreen = 8698449, ERed = 10177085,
    }
}

/// TextUnits.Configure 读这个判断是不是中文排版。Scale 大于 1 时当作中文界面；默认当作没有界面设置。
internal sealed class UISettings
{
    internal static float Scale = 1f;
    private static readonly UISettings one = new UISettings();

    internal static UISettings current { get { return Scale > 1f ? one : null; } }

    internal float GetCharWidthScale() { return Scale; }
}

namespace UnityEngine
{
    /// BarMark.Rgb 的类型；离线不看它的值。
    internal struct Color32
    {
        public byte r, g, b, a;
    }
}

namespace SectionedTooltips
{
    /// LayoutResult 带着它；离线不用。
    internal sealed class ContentsInfo
    {
    }

    /// Sections.TitleOf 要的文案入口；离线直接返回 key。
    internal static class SectionedTooltipsMod
    {
        internal static string L(string key) { return key; }
    }
}
