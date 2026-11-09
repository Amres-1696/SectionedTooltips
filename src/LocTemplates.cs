using System;
using System.Globalization;

namespace SectionedTooltips
{
    /// <summary>
    /// 用哨兵数字反查游戏本地化的模板。工具提示里的“耐久度 311/400”是
    /// LocHelper.GetLocalizedMechanic("mech_durability_display", 当前, 上限) 拼出来的，
    /// 把前缀写死只对一种语言有效。这里传进哨兵数字，从结果里切出两个数字前后的字面量，
    /// 于是任何语言都能解析。切分和匹配在 <see cref="TextRules"/> 里，这里只负责探。
    /// </summary>
    internal static class LocTemplates
    {
        private const string DurabilityKey = "mech_durability_display";
        private const string StorageTabsKey = "mech_storage_bay_tabs";

        // 三位数哨兵：模板里不会有别的数字，也基本不会被千位分隔符拆开。
        private const int SentinelNow = 987;
        private const int SentinelMax = 654;

        private static string checkedLocale;
        private static bool known;
        private static string prefix = "", mid = "";
        private static string storageTabs = "";

        /// <summary>是否探到了模板。</summary>
        internal static bool Known { get { return known; } }

        /// <summary>模板里第一个数字前面、两个数字中间的字面量。</summary>
        internal static string Prefix { get { return prefix; } }
        internal static string Mid { get { return mid; } }

        /// <summary>储藏区“储物标签页 1/5”那一行的标签（去掉冒号）；没探到时为空。</summary>
        internal static string StorageTabs { get { return storageTabs; } }

        /// <summary>换语言后重探一次；同一个语言里只做一次。</summary>
        internal static void Check()
        {
            string locale;
            try { locale = LocHelper.GetCurrentLocaleCode(); }
            catch { return; }
            if (locale == checkedLocale) return;
            checkedLocale = locale;
            known = false;
            storageTabs = ProbeLabel(StorageTabsKey);
            string sample;
            try { sample = LocHelper.GetLocalizedMechanic(DurabilityKey, new object[] { SentinelNow, SentinelMax }); }
            catch { return; }
            string head, between, tail;
            TextRules.SplitTemplate(sample, SentinelNow.ToString(CultureInfo.InvariantCulture),
                SentinelMax.ToString(CultureInfo.InvariantCulture), out head, out between, out tail);
            if (head == null) return;
            prefix = head;
            mid = between;
            known = true;
        }

        /// 两个数字的模板里第一个数字前面的字，去掉空白和冒号；探不到返回空串。
        private static string ProbeLabel(string key)
        {
            try
            {
                string sample = LocHelper.GetLocalizedMechanic(key, new object[] { SentinelNow, SentinelMax });
                string head, between, tail;
                TextRules.SplitTemplate(sample, SentinelNow.ToString(CultureInfo.InvariantCulture),
                    SentinelMax.ToString(CultureInfo.InvariantCulture), out head, out between, out tail);
                return head == null ? "" : head.Trim().TrimEnd(':', '：').Trim();
            }
            catch { return ""; }
        }
    }
}
