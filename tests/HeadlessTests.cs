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
        ItemDetailsShapes();
        PercentLists();
        HintLines();
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

        // 中文里夹着空格分隔的条目时，优先在空格处断，别把“紫外线灯”拆开。
        lines = TextUnits.Wrap(Units("海德拉科技微型净水器 +6.96% 紫外线灯 +1.34% 滤水器 +1.03%"), 370, 370);
        Check(TextUnits.Plain(lines[0]) == "海德拉科技微型净水器 +6.96%", "在空格处断行，实际第一行：" + TextUnits.Plain(lines[0]));
        Check(TextUnits.Plain(lines[1]).StartsWith("紫外线灯", StringComparison.Ordinal), "名字没有被拆开");

        // 纯中文没有空格时仍然到处都能断。
        lines = TextUnits.Wrap(Units("中文中文中文中文中文"), 4 * TextUnits.Cell, 4 * TextUnits.Cell);
        Check(lines.Count == 5, "纯中文仍按宽度断，实际 " + lines.Count);

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


    /// 物品详情（Item Details 0.7.7）实际写出的几种文本，按它的 zh.csv 抄的。
    private static void ItemDetailsShapes()
    {
        float a, b;
        // hidden.durability “耐久度：{0}/{1}”：冒号拆列后数值就是纯分数
        Check(TextRules.ReadFraction("311/400", true, "耐久度 ", "/", out a, out b) && b == 400f, "耐久度：311/400");

        // inner.durability “{0}：耐久度 {1}/{2}”：按模板读
        Check(TextRules.ReadFraction("耐久度 311/400", true, "耐久度 ", "/", out a, out b) && a == 311f, "滤水器：耐久度 311/400");

        // inner.filters “{0} ×{1}：最低耐久度 {2}/{3}”：前缀不是模板，退回通用规则
        Check(TextRules.ReadFraction("最低耐久度 311/400", true, "耐久度 ", "/", out a, out b) && a == 311f, "最低耐久度 311/400 退回通用规则");

        // hidden.durability_faked “真实耐久度：{0}/{1}（显示 {2}/{3}）”
        Check(TextRules.ReadFraction("311/400（显示 400/400）", true, "耐久度 ", "/", out a, out b) && b == 400f, "真实耐久度带括号注解");

        // storage.contains “内含：A×2、B、C”
        Check(TextRules.CaptionList("内含：铁锭×2、铜锭×1、塑料"), "内含清单");
        Check(TextRules.CaptionList("Contains: iron x2, copper"), "英文内含清单");

        // wine.composition “成分：糖度 {0}‰、污染物 {1}‰”
        Check(TextRules.CaptionList("成分：糖度 12‰、污染物 3‰"), "成分清单");

        // 带括号注解的耐久度行不是内容物清单，不能删
        Check(!TextRules.CaptionList("真实耐久度：311/400（显示 400/400）"), "带括号的耐久度行不算清单");
    }


    /// 一行里好几组“名字 +N%”时应当拆成多行，名字靠左、百分比右对齐。
    private static void PercentLists()
    {
        List<string[]> pairs = TextRules.PercentPairs("海德拉科技微型净水器 +6.96% 紫外线灯 +1.34% 滤水器 +1.03%");
        Check(pairs != null && pairs.Count == 3, "拆出三组，实际 " + (pairs == null ? 0 : pairs.Count));
        Check(pairs != null && pairs[0][0] == "海德拉科技微型净水器" && pairs[0][1] == "+6.96%", "第一组是名字加百分比");
        Check(pairs != null && pairs[2][0] == "滤水器" && pairs[2][1] == "+1.03%", "最后一组也对");

        pairs = TextRules.PercentPairs("净水器 +0.77% 净水药片 +0.23%");
        Check(pairs != null && pairs.Count == 2 && pairs[1][0] == "净水药片", "两组也能拆");

        Check(TextRules.PercentPairs("破旧 -75%") == null, "只有一组不拆");
        Check(TextRules.PercentPairs("每次使用 +5% 速度 +2% 的加成") == null, "后面还有别的字不拆");
        Check(TextRules.PercentPairs("+5% +2%") == null, "没有名字不拆");
        Check(TextRules.PercentPairs("没有百分比") == null, "没有百分比不拆");
    }


    /// “按Ctrl展开杂质　1.00%”：提示和数数据要分开。
    private static void HintLines()
    {
        string hint, label, value;
        bool ok = TextRules.HintPair("按Ctrl展开杂质　1.00%", out hint, out label, out value);
        Check(ok && hint == "按Ctrl展开" && label == "杂质" && value == "1.00%", "中文提示行：" + hint + "/" + label + "/" + value);

        ok = TextRules.HintPair("Ctrl to expand impurities  1.00%", out hint, out label, out value);
        Check(ok && hint == "Ctrl to expand" && label == "impurities", "英文提示行：" + hint + "/" + label);

        ok = TextRules.HintPair("水　7.54L　99.00%", out hint, out label, out value);
        Check(!ok, "普通数据行不算提示");

        ok = TextRules.HintPair("按Ctrl展开　1.00%", out hint, out label, out value);
        Check(!ok, "提示后面没有名字不算");
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
