using System;

namespace SectionedTooltips
{
    /// ModuleEffectHelper 在静态构造函数里一次性生成所有模组效果的名字和描述。如果在游戏应用玩家语言之前就有东西碰到它（别的模组加载时就够了），这个会话里模组卡就一直是英文。这里在语言对不上时按当前语言重写：第一次 tooltip 时做一次，换语言后再做。
    internal static class Relocalize
    {
        // 效果 id、名字键、描述键和描述的参数，照游戏静态构造函数里的写法。
        private static readonly object[][] Effects =
        {
            new object[] { "MODULE_EFFECT_PREMIUM", "mech_module_effect_premium_name", "mech_module_effect_premium_desc", new object[] { 20 } },
            new object[] { "MODULE_EFFECT_INFERIOR", "mech_module_effect_inferior_name", "mech_module_effect_inferior_desc", new object[] { 19 } },
            new object[] { "MODULE_EFFECT_RECEIVER", "mech_module_effect_receiver_name", "mech_module_effect_receiver_desc", new object[] { 10 } },
            new object[] { "MODULE_EFFECT_TOP_MOUNT", "mech_module_effect_top_mount_name", "mech_module_effect_top_mount_desc", new object[] { 20 } },
            new object[] { "MODULE_EFFECT_SIDE_MOUNT", "mech_module_effect_side_mount_name", "mech_module_effect_side_mount_desc", new object[] { 20 } },
            new object[] { "MODULE_EFFECT_OVERVOLTED", "mech_module_effect_overvolted_name", "mech_module_effect_overvolted_desc", new object[] { 200, 10 } },
            new object[] { "MODULE_EFFECT_DEGRADING", "mech_module_effect_degrading_name", "mech_module_effect_degrading_desc", new object[] { 1 } },
            new object[] { "MODULE_EFFECT_LEARNING_ALGORITHM", "mech_module_effect_learning_algorithm_name", "mech_module_effect_learning_algorithm_desc", new object[] { 1 } },
            new object[] { "MODULE_EFFECT_NEGATIVE_FEEDBACK", "mech_module_effect_negative_feedback_name", "mech_module_effect_negative_feedback_desc", new object[] { 25, 25 } },
            new object[] { "NODE_EFFECT_SUPPORT", "mech_module_effect_support_node_name", "mech_module_effect_support_node_desc", new object[] { 20 } },
        };

        /// 上次检查效果时用的语言。
        private static string checkedFor;

        internal static void Check()
        {
            string locale;
            try { locale = LocHelper.GetCurrentLocaleCode(); }
            catch { return; }
            // 游戏还没选语言时为空：没东西可比。
            if (string.IsNullOrEmpty(locale) || locale == checkedFor) return;
            checkedFor = locale;
            try
            {
                int fixedCount = 0;
                for (int i = 0; i < Effects.Length; i++)
                {
                    object[] fx = Effects[i];
                    ModuleEffectHelper.ModuleEffect e;
                    if (!ModuleEffectHelper.moduleEffects.TryGetValue((string)fx[0], out e) || e == null) continue;
                    string name = LocHelper.GetLocalizedMechanic((string)fx[1], new object[0]);
                    if (Missing(name) || name == e.displayName) continue;
                    e.displayName = name;
                    string desc = LocHelper.GetLocalizedMechanic((string)fx[2], (object[])fx[3]);
                    if (!Missing(desc)) e.description = desc;
                    fixedCount++;
                }
                if (fixedCount > 0) SectionedTooltipsMod.Log(string.Format("module effects re-localised to {0} ({1} of {2})", locale, fixedCount, Effects.Length));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Error("re-localising module effects failed: " + e.Message);
            }
        }

        /// 空，或者本地化系统的“找不到翻译”文本：保留游戏原来的。
        private static bool Missing(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string table, key;
            try { return LocHelper.TryParseMissingTranslation(text, out table, out key); }
            catch { return false; }
        }
    }
}
