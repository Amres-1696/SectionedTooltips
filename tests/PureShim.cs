#pragma warning disable 0649
// 离线自检用的最小替身：真正的 Seg / BarMark 带着游戏的调色板类型（RenderHandler.ColorPalette），
// 桌面 .NET Framework 装载不了。这里只提供 TextUnits 用到的那几个成员，
// 只在 tests\test.bat 的编译里出现，不进 mod 产物。
internal sealed class Seg
{
    public string Text = "";
    public BarMark Bar;

    public Seg With(string text)
    {
        Seg s = (Seg)MemberwiseClone();
        s.Text = text;
        return s;
    }
}

internal sealed class BarMark
{
}

/// TextUnits.Configure 读这个判断是不是中文排版；离线跑时当作没有界面设置。
internal sealed class UISettings
{
    internal static UISettings current { get { return null; } }

    internal float GetCharWidthScale() { return 1f; }
}
