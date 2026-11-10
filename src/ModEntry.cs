using System;
using System.Diagnostics;
using HarmonyLib;

namespace SectionedTooltips
{
    /// 模组加载器的入口。加载器对这个程序集跑 PatchAll()，装上 TooltipBasicPatch；各系统那 90 多个 tooltip helper 在第一次构建 tooltip 时按表手工 patch（见 EnsureHelpers）。
    public sealed class SectionedTooltipsMod : IMod
    {
        public const string DefaultId = "amres.sectioned_tooltips";

        internal static string Id = DefaultId;
        private static ModLog log;

        public void Init(ModManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.ID)) Id = manifest.ID;
            log = new ModLog(manifest);

            Settings.Define(ModConfig.For(Id));

            ready = SliceAccess.Init();
            if (!ready) Error("RichTextBuilder internals not found; tooltips stay vanilla.");
            // 向其他模组声明我们（没装它们也没关系，只是没人读）。
            if (ready) Compat.Register();
        }

        private static bool ready;
        private static bool helpersPatched;

        /// 第一次构建 tooltip 时才 patch helper，不在 Init 里。patch 会让运行时初始化这些 helper 类，其中一些（WaterFeatureHelper）把本地化文本缓存在静态字段里：在 Init 时做，游戏还没应用玩家语言，会缓存成系统语言。
        internal static void EnsureHelpers()
        {
            if (helpersPatched || !ready) return;
            helpersPatched = true;
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                int patched = Helpers.Apply(new Harmony(Id + ".helpers"));
                Log(string.Format("patched {0}/{1} tooltip helpers in {2} ms", patched, Helpers.Count, sw.ElapsedMilliseconds));
            }
            catch (Exception e)
            {
                Error("patching the tooltip helpers failed: " + e);
            }
        }

        public void OnEnable()
        {
        }

        public void OnDisable()
        {
            Compat.Unregister();
            // 之后加载器会立刻卸载我们的 patch，所以现在不隐藏的话，估价单不会消失，淡出的原版窗口也回不来。
            try { Overlay.Hide(); }
            catch (Exception e) { Error("hiding on disable failed: " + e.Message); }
        }

        internal static string L(string key)
        {
            try { return ModHelper.GetLocalized(Id, key, new object[0]); }
            catch { return key; }
        }

        internal static void Log(string s) { try { if (log != null) log.Log(s); } catch { } }
        internal static void Error(string s) { try { if (log != null) log.Error(s); } catch { } }
    }

    public enum TooltipStyle
    {
        Ticket,
        Classic,
    }

    internal static class Settings
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<TooltipStyle> Style;
        internal static ConfigEntry<int> MaxWidth;
        internal static ConfigEntry<bool> Tree;
        internal static ConfigEntry<bool> Icon;
        internal static ConfigEntry<bool> Bars;
        internal static ConfigEntry<bool> FractionBars;
        internal static ConfigEntry<bool> LiquidPanel;
        internal static ConfigEntry<bool> ContentsPanel;
        internal static ConfigEntry<string> Paper;
        internal static ConfigEntry<string> NotOwnedPaper;
        internal static ConfigEntry<int> Scale;
        internal static ConfigEntry<int> PriceSize;
        internal static ConfigEntry<int> ModsColumn;
        internal static ConfigEntry<int> Opacity;
        internal static ConfigEntry<int> ContentRows;
        internal static ConfigEntry<bool> ShowDescription;
        internal static ConfigEntry<bool> ShowPriceDetails;
        internal static ConfigEntry<bool> ShowModInfo;
        internal static ConfigEntry<bool> ShowFooter;
        internal static ConfigEntry<bool> TagBackground;
        internal static ConfigEntry<bool> Collapse;
        internal static ConfigEntry<string> ExpandKey;
        internal static ConfigEntry<bool> CollapsePrices;
        internal static ConfigEntry<bool> CollapseContents;
        internal static ConfigEntry<bool> CollapseDescription;
        internal static ConfigEntry<bool> CollapseModInfo;
        /// 每个 Ticket.TagGroups 条目一个颜色设置。
        internal static ConfigEntry<string>[] TagColors;
        internal static ConfigEntry<bool> Debug;

        /// 设置一改就加一，让缓存的排版重建。
        internal static int Version;

        internal static bool IsTicket { get { return Style == null || Style.Value == TooltipStyle.Ticket; } }

        /// 开关的值；设置还没加载时当作开。
        internal static bool On(ConfigEntry<bool> e) { return e == null || e.Value; }

        /// 估价单相对原版 tooltip 的缩放（TicketScale 是百分数）。
        internal static float ScaleFactor { get { return IntOf(Scale, 100, 50, 150) / 100f; } }

        /// 大字价格相对正文的倍数（PriceSize 是百分数）。
        internal static float PriceFactor { get { return IntOf(PriceSize, 220, 100, 220) / 100f; } }

        /// 估价单的不透明度，0.4 到 1（TicketOpacity 是百分数）。
        internal static float OpacityFactor { get { return IntOf(Opacity, 100, 40, 100) / 100f; } }

        internal static int MaxContentRows { get { return IntOf(ContentRows, 2, 1, 6); } }

        /// 附加信息和其他两节超过这么多行就单独占一栏；0 表示不按行数分栏。
        internal static int ModsColumnLines { get { return IntOf(ModsColumn, 8, 0, 30); } }

        /// 经典风格最宽的内容宽度，按半角字符算。
        internal static int MaxChars { get { return IntOf(MaxWidth, 46, 30, 80); } }

        /// 数字设置在 [min, max] 内的值；没设置或读不出来时用 def。
        private static int IntOf(ConfigEntry<int> e, int def, int min, int max)
        {
            return Math.Max(min, Math.Min(max, e != null ? e.Value : def));
        }

        /// 估价单那一节的名称：设置面板把预览放在这个标题下。
        internal const string SlipSection = "Slip / 估价单";

        internal static void Define(ModConfig cfg)
        {
            // Init 早于加载器读取本模组的 Localization csv，所以节名在这里没法本地化；标签和提示是回调，可以。
            const string general = "General / 常规";
            const string slip = SlipSection;
            const string content = "Content / 显示内容";
            const string fold = "Collapse / 收起与展开";
            const string tags = "Tags / 分类标签";
            const string classic = "Classic style / 经典风格";
            const string debug = "Diagnostics / 诊断";

            // 按设置窗口里的顺序定义：常规、估价单外观、显示内容、收起与展开、分类标签、经典风格、诊断。
            // 存档只按键名存，挪分组不影响已经保存的值。
            Enabled = cfg.Define("Enabled", true, general, Text("cfg_enabled"), Text("cfg_enabled_tip"), false);
            Style = cfg.DefineEnum("Style", TooltipStyle.Ticket, general, Text("cfg_style"), Text("cfg_style_tip"), false);
            // 估价单本身的大小和样子。
            Scale = Number(cfg, "TicketScale", 100, slip, "cfg_scale", 50, 150);
            PriceSize = Number(cfg, "PriceSize", 220, slip, "cfg_price_size", 100, 220);
            Opacity = Number(cfg, "TicketOpacity", 100, slip, "cfg_opacity", 40, 100);
            Paper = cfg.Define("PaperColor", Ticket.PaperNames[0], slip, Text("cfg_paper"), Ticket.PaperNames, Text("cfg_paper_tip"), false);
            NotOwnedPaper = cfg.Define("NotOwnedPaperColor", Ticket.PaperNames[1], slip, Text("cfg_paper_not_owned"), Ticket.PaperNames, Text("cfg_paper_not_owned_tip"), false);
            ModsColumn = Number(cfg, "ModsColumnLines", 8, slip, "cfg_mods_column", 0, 30);
            // 显示哪些内容（关掉的两种风格都不显示，按展开键也不出来）。
            Icon = cfg.Define("ItemIcon", true, content, Text("cfg_icon"), Text("cfg_icon_tip"), false);
            Bars = cfg.Define("GraphicBars", true, content, Text("cfg_bars"), Text("cfg_bars_tip"), false);
            FractionBars = cfg.Define("FractionBars", true, content, Text("cfg_fraction_bars"), Text("cfg_fraction_bars_tip"), false);
            ContentsPanel = cfg.Define("ContentsPanel", true, content, Text("cfg_contents_panel"), Text("cfg_contents_panel_tip"), false);
            ContentRows = Number(cfg, "ContentRows", 2, content, "cfg_content_rows", 1, 6);
            ShowPriceDetails = cfg.Define("ShowPriceDetails", true, content, Text("cfg_show_prices"), Text("cfg_show_prices_tip"), false);
            ShowDescription = cfg.Define("ShowDescription", true, content, Text("cfg_show_desc"), Text("cfg_show_desc_tip"), false);
            ShowModInfo = cfg.Define("ShowModInfo", true, content, Text("cfg_show_mods"), Text("cfg_show_mods_tip"), false);
            ShowFooter = cfg.Define("ShowFooter", true, content, Text("cfg_show_footer"), Text("cfg_show_footer_tip"), false);
            // 估价单平时收起哪些部分，按住哪个键展开。
            Collapse = cfg.Define("Collapse", true, fold, Text("cfg_collapse"), Text("cfg_collapse_tip"), false);
            ExpandKey = cfg.Define("ExpandKey", Expand.KeyNames[0], fold, Text("cfg_expand_key"), Expand.KeyNames, Text("cfg_expand_key_tip"), false);
            CollapsePrices = cfg.Define("CollapsePrices", true, fold, Text("cfg_collapse_prices"), Text("cfg_collapse_prices_tip"), false);
            CollapseContents = cfg.Define("CollapseContents", true, fold, Text("cfg_collapse_contents"), Text("cfg_collapse_contents_tip"), false);
            CollapseDescription = cfg.Define("CollapseDescription", true, fold, Text("cfg_collapse_desc"), Text("cfg_collapse_desc_tip"), false);
            CollapseModInfo = cfg.Define("CollapseModInfo", false, fold, Text("cfg_collapse_mods"), Text("cfg_collapse_mods_tip"), false);
            // 估价单的分类标签：底色的开关，以及每组一个颜色。
            TagBackground = cfg.Define("TagBackground", true, tags, Text("cfg_tag_bg"), Text("cfg_tag_bg_tip"), false);
            TagColors = new ConfigEntry<string>[Ticket.TagGroups.Length];
            for (int n = 0; n < TagColors.Length; n++)
            {
                int i = Ticket.TagOrder[n];
                string group = Ticket.TagGroups[i];
                TagColors[i] = cfg.Define("TagColor" + group, Ticket.TagColorNames[Ticket.TagDefaults[i]], tags, Text("cfg_tag_" + group.ToLowerInvariant()),
                    Ticket.TagColorNames, Text("cfg_tag_color_tip"), false);
            }
            // 只对经典风格：它在原版窗口里排版。
            MaxWidth = Number(cfg, "MaxWidth", 46, classic, "cfg_max_width", 30, 80);
            Tree = cfg.Define("TreeLines", true, classic, Text("cfg_tree"), Text("cfg_tree_tip"), false);
            LiquidPanel = cfg.Define("LiquidPanel", true, classic, Text("cfg_liquid_panel"), Text("cfg_liquid_panel_tip"), false);
            Debug = cfg.Define("DebugLog", false, debug, Text("cfg_debug"), Text("cfg_debug_tip"), false);
            cfg.Save();

            Icon.OnChanged += delegate { Version++; };
            Bars.OnChanged += delegate { Version++; };
            FractionBars.OnChanged += delegate { Version++; };
            LiquidPanel.OnChanged += delegate { Version++; };
            ContentsPanel.OnChanged += delegate { Version++; };
            Paper.OnChanged += delegate { Version++; };
            NotOwnedPaper.OnChanged += delegate { Version++; };
            Scale.OnChanged += delegate { Version++; };
            PriceSize.OnChanged += delegate { Version++; };
            ModsColumn.OnChanged += delegate { Version++; };
            Opacity.OnChanged += delegate { Version++; };
            ContentRows.OnChanged += delegate { Version++; };
            ShowDescription.OnChanged += delegate { Version++; };
            ShowPriceDetails.OnChanged += delegate { Version++; };
            ShowModInfo.OnChanged += delegate { Version++; };
            ShowFooter.OnChanged += delegate { Version++; };
            TagBackground.OnChanged += delegate { Version++; };
            Collapse.OnChanged += delegate { Version++; };
            ExpandKey.OnChanged += delegate { Version++; };
            CollapsePrices.OnChanged += delegate { Version++; };
            CollapseContents.OnChanged += delegate { Version++; };
            CollapseDescription.OnChanged += delegate { Version++; };
            CollapseModInfo.OnChanged += delegate { Version++; };
            for (int i = 0; i < TagColors.Length; i++) TagColors[i].OnChanged += delegate { Version++; };

            Enabled.OnChanged += delegate { Version++; };
            Style.OnChanged += delegate { Version++; };
            MaxWidth.OnChanged += delegate { Version++; };
            Tree.OnChanged += delegate { Version++; };
            // 调试输出是排版时写的，所以改这个要让缓存的排版重做一遍。
            Debug.OnChanged += delegate { Version++; };
        }

        /// 滑块上的数字。游戏的滑块不显示数值，由 SettingsPanel 写在右边。1.3.4-1.3.7 用列表选出的值存的是同样的数字、同样的键，所以能沿用。
        private static ConfigEntry<int> Number(ModConfig cfg, string key, int def, string section, string text, int min, int max)
        {
            return cfg.Define(key, def, section, Text(text), min, max, Text(text + "_tip"), false);
        }

        private static Func<string> Text(string key)
        {
            return delegate { return SectionedTooltipsMod.L(key); };
        }
    }
}
