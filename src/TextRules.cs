using System;
using System.Collections.Generic;

namespace SectionedTooltips
{
    /// <summary>
    /// 不碰 Unity 的文本规则：数值拆分数、分数模板、末尾括号、清单标题。
    /// 单独放一个文件是为了能在 tests\test.bat 里离线跑：这个文件和 TextUnits.cs
    /// 只引用 System，不引用任何游戏程序集。
    /// </summary>
    internal static class TextRules
    {
        // ---------------------------------------------------------------- 分数

        /// <summary>“a/b”，两个数字后面可以带单位；别的都不算。</summary>
        internal static bool ParseFraction(string s, out float a, out float b)
        {
            a = b = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            int slash = s.IndexOf('/');
            if (slash <= 0 || s.IndexOf('/', slash + 1) >= 0) return false;
            return ParseNumber(s.Substring(0, slash), out a) && ParseNumber(s.Substring(slash + 1), out b);
        }

        /// <summary>
        /// value 里能画条的那段分数：整段是分数就用整段；“耐久度 311/400（已耗尽，不再过滤）”只取“311/400”。
        /// 前面的短词不含数字，末尾只去掉一组括号；取不到返回 null。
        /// </summary>
        internal static string FractionPart(string value)
        {
            string s = (value ?? "").Trim();
            float a, b;
            if (ParseFraction(s, out a, out b)) return s;

            // 末尾的括号注解不属于分数。
            if (s.Length > 1 && (s[s.Length - 1] == '）' || s[s.Length - 1] == ')'))
            {
                int open = s.LastIndexOfAny(new char[] { '（', '(' });
                if (open > 0) s = TrimEnd(s.Substring(0, open));
                if (ParseFraction(s, out a, out b)) return s;
            }

            // “12 / 12 L  幽灵水”“6/6升，98.47% 基础水”：分数后面隔着两个空格、全角空格或逗号跟着说明。
            int cut = NoteCut(s, s.IndexOf('/'));
            if (cut > 0)
            {
                string head = TrimEnd(s.Substring(0, cut));
                if (ParseFraction(head, out a, out b)) return head;
            }

            // “耐久度 311/400”：空格前是不含数字的短词，分数在后面。
            int space = s.IndexOfAny(new char[] { ' ', TextUnits.IdeographicSpace });
            if (space <= 0 || space > 6) return null;
            for (int i = 0; i < space; i++) if (char.IsDigit(s[i])) return null;
            string tail = s.Substring(space + 1).Trim();
            return ParseFraction(tail, out a, out b) ? tail : null;
        }

        /// 斜杠后面第一个说明分隔符的位置：两个半角空格、全角空格、全角逗号，或“逗号+空格”
        /// （单独的半角逗号可能是千位分隔符）。没有返回 -1。
        private static int NoteCut(string s, int slash)
        {
            if (slash < 0) return -1;
            for (int i = slash + 1; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == TextUnits.IdeographicSpace || ch == '，') return i;
                if ((ch == ' ' || ch == ',') && i + 1 < s.Length && s[i + 1] == ' ') return i;
            }
            return -1;
        }

        /// <summary>
        /// 从数值文本里读出分数。给了模板（从本地化反查来的“前缀 + 数字 + 中间 + 数字”）就按模板读，
        /// 任何语言都行；否则退回通用规则。
        /// </summary>
        internal static bool ReadFraction(string value, bool templateKnown, string prefix, string mid, out float a, out float b)
        {
            a = b = 0f;
            string s = (value ?? "").Trim();
            if (templateKnown && MatchTemplate(s, prefix, mid, out a, out b)) return b > 0f;
            string fraction = FractionPart(value);
            return fraction != null && ParseFraction(fraction, out a, out b) && b > 0f;
        }

        /// <summary>按“prefix + 数字 + mid + 数字”读；两个数字后面可以跟注解。</summary>
        internal static bool MatchTemplate(string text, string prefix, string mid, out float a, out float b)
        {
            a = b = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            prefix = prefix ?? "";
            mid = mid ?? "";
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int start = prefix.Length;
            int at = text.IndexOf(mid, start, StringComparison.Ordinal);
            if (at < 0) return false;
            if (!Number(text.Substring(start, at - start), out a)) return false;
            int rest = at + mid.Length;
            return Number(text.Length > rest ? text.Substring(rest) : "", out b);
        }

        /// <summary>
        /// 从探针结果里切出两个哨兵前后的字面量：prefix + first + mid + second + suffix。
        /// 两个哨兵都要在，且按先后顺序；否则三个输出都是 null。
        /// </summary>
        internal static void SplitTemplate(string sample, string first, string second, out string prefix, out string mid, out string suffix)
        {
            prefix = mid = suffix = null;
            if (string.IsNullOrEmpty(sample) || string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second)) return;
            int i = sample.IndexOf(first, StringComparison.Ordinal);
            if (i < 0) return;
            int j = sample.IndexOf(second, i + first.Length, StringComparison.Ordinal);
            if (j < 0) return;
            prefix = sample.Substring(0, i);
            mid = sample.Substring(i + first.Length, j - i - first.Length);
            suffix = sample.Substring(j + second.Length);
        }

        /// <summary>数字后面可以带短单位（“ml”“kg”“kJ”“格”），更长的就不当单位了。</summary>
        internal static bool ParseNumber(string s, out float v)
        {
            v = 0f;
            s = (s ?? "").Trim();
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == ',' || (end == 0 && s[end] == '-'))) end++;
            if (end == 0) return false;
            string unit = s.Substring(end).Trim();
            if (unit.Length > 3) return false;
            for (int i = 0; i < unit.Length; i++) if (char.IsDigit(unit[i]) || unit[i] == '(' || unit[i] == '（') return false;
            return float.TryParse(s.Substring(0, end).Replace(",", ""), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        /// <summary>取开头那段数字（后面的注解不管）；没有数字返回 false。</summary>
        internal static bool Number(string s, out float v)
        {
            v = 0f;
            s = TrimStart(s ?? "");
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == ',' || (end == 0 && s[end] == '-'))) end++;
            if (end == 0) return false;
            return float.TryParse(s.Substring(0, end).Replace(",", ""), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        // ---------------------------------------------------------------- 百分比列表

        /// <summary>
        /// 一行里排了好几组“名字 +N%”时拆成组：“海德拉科技微型净水器 +6.96% 紫外线灯 +1.34%” → 两组。
        /// 整行必须正好由这些组成（前面有名字、后面没有多余文字），否则返回 null。
        /// </summary>
        internal static List<string[]> PercentPairs(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            List<int> starts = new List<int>(), ends = new List<int>();
            for (int i = 0; i < plain.Length; i++)
            {
                char ch = plain[i];
                if (ch != '+' && ch != '-' && ch != '\u2212') continue;
                int j = i + 1, digits = 0;
                while (j < plain.Length && (char.IsDigit(plain[j]) || plain[j] == '.'))
                {
                    if (char.IsDigit(plain[j])) digits++;
                    j++;
                }
                if (digits == 0 || j >= plain.Length || plain[j] != '%') continue;
                starts.Add(i);
                ends.Add(j + 1);
                i = j;
            }
            if (starts.Count < 2) return null;
            List<string[]> pairs = new List<string[]>(starts.Count);
            int from = 0;
            for (int k = 0; k < starts.Count; k++)
            {
                string name = plain.Substring(from, starts[k] - from).Trim();
                if (k == 0) name = StripBullet(name);
                if (!PlausibleName(name)) return null;
                pairs.Add(new string[] { name, plain.Substring(starts[k], ends[k] - starts[k]) });
                from = ends[k];
            }
            if (plain.Substring(from).Trim().Length > 0) return null;
            return pairs;
        }

        /// 名字不能太长、也不能带句子标点，否则这行是在切一句话，不是在切列表。
        private static bool PlausibleName(string name)
        {
            if (name.Length == 0) return false;
            int cells = 0;
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if ("。，、；：！？,;:!?".IndexOf(ch) >= 0) return false;
                cells += TextUnits.IsWide(ch) ? 8 : 3;
            }
            return cells <= 8 * NameMax;
        }

        /// 去掉行首的项目符号（“-多年生” → “多年生”）。
        private static string StripBullet(string s)
        {
            if (s.Length < 2) return s;
            char ch = s[0];
            if (ch != '-' && ch != '*' && ch != '•' && ch != '·' && ch != '>') return s;
            return s.Substring(1).TrimStart();
        }

        /// 列表里的名字最多这么多个汉字；按宽度算，一个英文字母算 3/8 个（最多 32 个字母，“Illegal weapons smuggling”放得下）。
        private const int NameMax = 12;

        /// <summary>
        /// 行尾是一个带符号的百分比时，它前面的短名字当标签：“种子产量 +25%” → “种子产量”、“+25%”。
        /// 没有冒号的行，靠这个变成正常的标签/数值行。
        /// </summary>
        internal static bool TrailingPercent(string plain, out string label, out string value)
        {
            label = value = null;
            if (string.IsNullOrEmpty(plain)) return false;
            int space = -1;
            for (int i = plain.Length - 1; i >= 0; i--)
            {
                if (plain[i] == ' ' || plain[i] == TextUnits.IdeographicSpace) { space = i; break; }
            }
            if (space <= 0) return false;
            string head = plain.Substring(0, space).Trim();
            string tail = plain.Substring(space + 1).Trim();
            if (!PlausibleName(head) || !SignedPercent(tail)) return false;
            label = head;
            value = tail;
            return true;
        }

        /// “+25%”“-2.5%”这种带符号的百分比。
        private static bool SignedPercent(string s)
        {
            if (s.Length < 3) return false;
            if (s[0] != '+' && s[0] != '-' && s[0] != '\u2212') return false;
            int i = 1, digits = 0;
            while (i < s.Length - 1 && (char.IsDigit(s[i]) || s[i] == '.'))
            {
                if (char.IsDigit(s[i])) digits++;
                i++;
            }
            return digits > 0 && i == s.Length - 1 && s[i] == '%';
        }

        /// <summary>
        /// 没有冒号的“名字 数值”：“精工扩舱 0”“水量 0/12 升”“酒瓶区 6x4”。在一个前面是空格的数字处拆（可带正负号）：
        /// 数字后面最多跟一个短单位，“水量 0/12 升”拆成“水量”“0/12 升”，单位留在数值里；
        /// 后面还有别的字就往后找下一个数字，“Tier 2 filter 3/5”拆成“Tier 2 filter”“3/5”。名字不能以数字开头。
        /// </summary>
        internal static bool NameNumber(string plain, out string label, out string value)
        {
            label = value = null;
            string s = (plain ?? "").Trim();
            if (s.Length == 0 || char.IsDigit(s[0])) return false;
            for (int d = 1; d < s.Length; d++)
            {
                if (!char.IsDigit(s[d]) || char.IsDigit(s[d - 1])) continue;
                int start = d;
                if (s[start - 1] == '+' || s[start - 1] == '-' || s[start - 1] == '−') start--;
                if (start < 2 || (s[start - 1] != ' ' && s[start - 1] != TextUnits.IdeographicSpace)) continue;
                string rest = s.Substring(start);
                if (!NumberWithUnit(rest)) continue;
                string head = s.Substring(0, start).Trim();
                if (!PlausibleName(head)) return false;
                label = head;
                value = rest;
                return true;
            }
            return false;
        }

        /// 一段数（可带 / x × . , 和正负号），后面最多一个不含数字的短单位：“0/12 升”“6x4”“-5”。
        private static bool NumberWithUnit(string s)
        {
            int end = 0;
            if (end < s.Length && (s[end] == '+' || s[end] == '-' || s[end] == '−')) end++;
            int digits = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || ".,/x×".IndexOf(s[end]) >= 0))
            {
                if (char.IsDigit(s[end])) digits++;
                end++;
            }
            if (digits == 0) return false;
            string unit = s.Substring(end).Trim();
            if (unit.Length > 3) return false;
            for (int i = 0; i < unit.Length; i++) if (char.IsDigit(unit[i])) return false;
            return true;
        }

        // ---------------------------------------------------------------- 标签

        /// <summary>末尾那组括号从哪里开始（“效率（电子元件（普通））” 得 2）；不是以括号结尾时返回 -1。</summary>
        internal static int TrailingGroup(string s)
        {
            if (s == null || s.Length < 2) return -1;
            char last = s[s.Length - 1];
            if (last != '）' && last != ')') return -1;
            int depth = 0;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                char ch = s[i];
                if (ch == '）' || ch == ')') depth++;
                else if (ch == '（' || ch == '(')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        // ---------------------------------------------------------------- 清单

        /// <summary>
        /// 这一行是不是“说明：清单”的形式：冒号前是没有数字和括号的短说明（八个字符以内），后面有内容、也没有括号注解。
        /// 只要说明短、后面又点了条目的名字，就不必知道它用哪种语言写。
        /// </summary>
        internal static bool CaptionList(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int colon = text.IndexOfAny(CaptionColons);
            if (colon < 2 || colon > 8) return false;
            for (int i = 0; i < colon; i++)
            {
                char ch = text[i];
                if (char.IsDigit(ch) || ch == '（' || ch == '(') return false;
            }
            bool any = false;
            for (int i = colon + 1; i < text.Length; i++)
            {
                char ch = text[i];
                // 括号里通常是注解（“……（含铁锭×2）”），不是清单。
                if (ch == '（' || ch == '(' || ch == '）' || ch == ')') return false;
                if (!char.IsWhiteSpace(ch)) any = true;
            }
            return any;
        }

        private static readonly char[] CaptionColons = { '：', ':' };

        // 无参的 TrimEnd/TrimStart 是 .NET Standard 2.1 才加的，桌面 .NET Framework 跑测试时没有，所以自己写。
        private static string TrimEnd(string s)
        {
            int end = s.Length;
            while (end > 0 && char.IsWhiteSpace(s[end - 1])) end--;
            return end == s.Length ? s : s.Substring(0, end);
        }

        private static string TrimStart(string s)
        {
            int start = 0;
            while (start < s.Length && char.IsWhiteSpace(s[start])) start++;
            return start == 0 ? s : s.Substring(start);
        }
    }
}
