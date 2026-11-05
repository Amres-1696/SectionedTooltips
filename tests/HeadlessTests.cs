using System;
using System.Collections.Generic;
using SectionedTooltips;

/// <summary>
/// 无头自检：只编 TextRules.cs 和 TextUnits.cs（这两个文件不引用游戏程序集），
/// 用桌面 .NET Framework 直接跑，不需要启动游戏。
/// 用 tests\test.bat 编译并运行；返回码即失败的用例数。
/// </summary>
internal static class HeadlessTests
{
    private static int failed;
    private static int passed;

    public static int Main()
    {
        try { Run(); }
        catch (Exception e)
        {
            Console.WriteLine("CRASH " + e.GetType().Name + ": " + e.Message);
            return 99;
        }
        Console.WriteLine("{0} passed, {1} failed", passed, failed);
        return failed;
    }

    private static void Run()
    {
        Widths();
        Wrapping();
        Fractions();
        Templates();
        Captions();
    }

    // ---------------------------------------------------------------- 用例

    private static void Widths()
    {
        Check(TextUnits.CharWidth('A') == TextUnits.Cell, "ASCII 算一格");
        Check(TextUnits.CharWidth('中') == 2 * TextUnits.Cell, "汉字算两格");
        Check(TextUnits.CharWidth('　') == 2 * TextUnits.Cell, "全角空格算两格");
        Check(TextUnits.CharWidth(' ') == TextUnits.SpaceW, "半角空格用 SpaceW");
        Check(TextUnits.CharWidth('\u200b') == 0, "零宽空格没有宽度");
        Check(TextUnits.Width("中A") == 3 * TextUnits.Cell, "中英混排宽度");
        Check(TextUnits.Pad(TextUnits.Cell).Length == 1, "一格填充是一个空格");
        Check(TextUnits.Plain("<color=#fff>字</color>") == "字", "剥掉富文本标签");
        Check(TextUnits.RawChars("<b>x</b>") == 8, "背景层按原始字符数画");
    }

    private static void Wrapping()
    {
        List<List<Unit>> lines = TextUnits.Wrap(Units("中文中文中文"), 4 * TextUnits.Cell, 4 * TextUnits.Cell);
        Check(lines.Count == 3, "六个汉字按四格折成三行，实际 " + lines.Count);
        Check(TextUnits.Plain(lines[0]) == "中文", "第一行是两个汉字");

        lines = TextUnits.Wrap(Units("aaa bbb ccc"), 4 * TextUnits.Cell, 4 * TextUnits.Cell);
        Check(lines.Count >= 2, "英文按空格折行，实际 " + lines.Count);
        Check(TextUnits.Plain(lines[0]).Trim().Length > 0, "第一行不为空");

        // 收尾标点不能落在行首：。会被带到下一行，和它前面那个字一起。
        lines = TextUnits.Wrap(Units("中文。中文"), 4 * TextUnits.Cell, 4 * TextUnits.Cell);
        bool noneStartsWithStop = true;
        for (int i = 0; i < lines.Count; i++) if (TextUnits.Plain(lines[i]).StartsWith("。", StringComparison.Ordinal)) noneStartsWithStop = false;
        Check(noneStartsWithStop, "句号不落在行首");
    }

    private static void Fractions()
    {
        float a, b;
        Check(TextRules.ParseFraction("400/400", out a, out b) && a == 400f && b == 400f, "解析 400/400");
        Check(TextRules.ParseFraction("20.23/12 kg", out a, out b) && a == 20.23f && b == 12f, "带小数和单位");
        Check(!TextRules.ParseFraction("耐久度 311/400", out a, out b), "整段不是纯分数时不认");
        Check(!TextRules.ParseFraction("1/2/3", out a, out b), "两个斜杠不算");

        Check(TextRules.FractionPart("311/400") == "311/400", "整段是分数");
        Check(TextRules.FractionPart("耐久度 311/400（已耗尽，不再过滤）") == "311/400", "带前缀和括号");
        Check(TextRules.FractionPart("2498 ml / 3000 ml") == "2498 ml / 3000 ml", "带单位");
        Check(TextRules.FractionPart("没有数字") == null, "没有分数返回 null");

        // 模板没探到时退回通用规则。
        Check(TextRules.ReadFraction("耐久度 311/400", false, null, null, out a, out b) && a == 311f && b == 400f, "退回通用规则");
        Check(!TextRules.ReadFraction("没有数字", false, null, null, out a, out b), "没有分数时不画条");
        Check(!TextRules.ReadFraction("3/0", false, null, null, out a, out b), "上限为 0 时不画条");

        // 有模板时按模板读，语言无关。
        Check(TextRules.ReadFraction("耐久度 311/400", true, "耐久度 ", "/", out a, out b) && a == 311f && b == 400f, "按模板读");
        Check(TextRules.ReadFraction("耐久度 0/400（已耗尽）", true, "耐久度 ", "/", out a, out b) && b == 400f, "模板读法忽略末尾注解");
        Check(TextRules.ReadFraction("其它行 311/400", true, "耐久度 ", "/", out a, out b), "模板对不上时退回通用规则");
    }

    private static void Templates()
    {
        string prefix, mid, suffix;
        TextRules.SplitTemplate("耐久度 987/654 点", "987", "654", out prefix, out mid, out suffix);
        Check(prefix == "耐久度 " && mid == "/" && suffix == " 点", "切出前缀、中间和后缀");

        TextRules.SplitTemplate("耐久度 987/654", "987", "654", out prefix, out mid, out suffix);
        Check(suffix != null && suffix.Length == 0, "没有后缀时是空串");

        TextRules.SplitTemplate("没有数字", "987", "654", out prefix, out mid, out suffix);
        Check(prefix == null, "哨兵不在时切不出来");

        TextRules.SplitTemplate("只有 654 一个 987", "987", "654", out prefix, out mid, out suffix);
        Check(prefix == null, "顺序反了也切不出来");

        float a, b;
        Check(TextRules.MatchTemplate("耐久度 311/400", "耐久度 ", "/", out a, out b) && a == 311f, "按模板取到第一个数");
        Check(!TextRules.MatchTemplate("耐久度", "耐久度 ", "/", out a, out b), "没有斜杠读不出来");
    }

    private static void Captions()
    {
        Check(TextRules.TrailingGroup("去除的污染物（昨夜）") == 6, "末尾括号的起点");
        Check(TextRules.TrailingGroup("效率（电子元件（普通））") == 2, "嵌套括号取最外层");
        Check(TextRules.TrailingGroup("纯度") == -1, "没有括号返回 -1");
        Check(TextRules.TrailingGroup("）") == -1, "单字符返回 -1");

        Check(TextRules.CaptionList("内含：铁锭、铜锭"), "认出“说明：清单”");
        Check(TextRules.CaptionList("Contents: iron, copper"), "英文的说明也认");
        Check(!TextRules.CaptionList("价格：500（铁锭×2）"), "带括号注解的不算清单");
        Check(!TextRules.CaptionList("没有冒号"), "没有冒号不算");
        Check(!TextRules.CaptionList("这是一个特别长的说明句子：内容"), "说明太长不算");
        Check(!TextRules.CaptionList("数量 3：铁锭"), "说明里有数字不算");
    }

    // ---------------------------------------------------------------- 工具

    /// Seg 带着游戏的调色板类型，离线跑不了，所以直接拼 Unit。
    private static List<Unit> Units(string text)
    {
        List<Unit> list = new List<Unit>();
        TextUnits.Append(list, text, 0);
        return list;
    }

    private static void Check(bool ok, string name)
    {
        if (ok) { passed++; return; }
        failed++;
        Console.WriteLine("FAIL  " + name);
    }
}
