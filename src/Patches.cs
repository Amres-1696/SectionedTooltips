using System;
using HarmonyLib;
using UnityEngine;

namespace SectionedTooltips
{
    // 模组挂在游戏上的全部固定补丁（tooltip helper 的那一批是运行时按表打的，见 Helpers）。一帧里的顺序：
    //   HasTooltip        设置成按住展开键才显示时，键没按着就告诉游戏“这件物品没有 tooltip”
    //   GetTooltipBasic   前缀开始记 helper 写了哪些段，finalizer 里重新排版（Layout.Apply）
    //   AddProgressBar    记下游戏画的进度条
    //   GetTooltip(Advanced)  记下这份文字对应哪次排版（Overlay.Note）
    //   ShowTooltip       游戏把文字显示出来时，画估价单或经典风格的图形（Overlay.OnShow）
    //   HideTooltip       收起
    //   ModConfigWindow.Open  给设置面板加色块和示例单

    [HarmonyPatch(typeof(GameItemElement), "GetTooltipBasic")]
    internal static class TooltipBasicPatch
    {
        private static GameItemElement checkedItem;

        /// 调试用：从我们的 prefix（最先）到 finalizer（所有 postfix 之后），即游戏和其他模组的耗时。
        internal static readonly System.Diagnostics.Stopwatch BasicTimer = new System.Diagnostics.Stopwatch();
        internal static double BasicMs;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(GameItemElement __instance)
        {
            if (Capture.Depth == 0 && Settings.Debug != null && Settings.Debug.Value)
            {
                BasicTimer.Reset();
                BasicTimer.Start();
            }
            SectionedTooltipsMod.EnsureHelpers();
            // 在 helper 运行前修好语言，这样模组 helper 写出的就是对的；每个新物品只做一次。
            if (!ReferenceEquals(__instance, checkedItem))
            {
                checkedItem = __instance;
                Relocalize.Check();
            }
            Capture.Push(__instance);
        }

        // 在 finalizer 里排版：它在所有模组的 postfix 之后运行，不管加载顺序，其他模组 postfix 加的文本都能一起排版。
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception, GameItemElement __instance, RichTextBuilder __result)
        {
            if (__exception == null) Arrange(__instance, __result);
            Capture.Pop();
            return __exception;
        }

        private static void Arrange(GameItemElement __instance, RichTextBuilder __result)
        {
            if (Dev.Dumping && __result != null)
            {
                // dev 构建的全量导出：每件物品自己调 GetTooltipBasic，这里只算分节、写文件
                try { Layout.Apply(Capture.Top, __result); }
                catch (Exception e) { Fuse.Fail(Fuse.LayoutKey, e); }
                return;
            }
            if (__result == null || Capture.Depth != 1) return;
            if (Settings.Enabled != null && !Settings.Enabled.Value) return;
            if (Fuse.IsOpen(Fuse.LayoutKey)) return;
            bool timing = BasicTimer.IsRunning;
            if (timing)
            {
                BasicTimer.Stop();
                BasicMs = BasicTimer.Elapsed.TotalMilliseconds;
            }
            Dev.BeforeLayout();
            try
            {
                Layout.Apply(Capture.Top, __result);
                if (timing) Layout.LogSlowFrame(__instance, BasicMs);
                Fuse.Ok(Fuse.LayoutKey);
            }
            catch (Exception e)
            {
                // 连续失败到上限就不再排版，工具提示退回原版。
                Fuse.Fail(Fuse.LayoutKey, e);
            }
        }
    }

    [HarmonyPatch(typeof(RichTextBuilder), "AddProgressBar")]
    internal static class ProgressBarPatch
    {
        private static void Prefix(RichTextBuilder __instance, out int __state)
        {
            __state = Capture.BarStart(__instance);
        }

        private static void Postfix(RichTextBuilder __instance, float ratio, RenderHandler.ColorPalette color, RenderHandler.ColorPalette backgroundColor, int __state)
        {
            Capture.BarEnd(__instance, __state, ratio, color, backgroundColor);
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "GetTooltip")]
    internal static class GetTooltipPatch
    {
        private static void Postfix(GameItemElement __instance, RichText __result)
        {
            Overlay.Note(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "GetTooltipAdvanced")]
    internal static class GetTooltipAdvancedPatch
    {
        private static void Postfix(GameItemElement __instance, RichText __result)
        {
            Overlay.Note(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(RichTextElement), "Validate")]
    internal static class ValidatePatch
    {
        private static void Postfix(RichTextElement __instance)
        {
            Overlay.AlignLayers(__instance);
        }
    }

    [HarmonyPatch(typeof(OverlayHandler), "ShowTooltip", new Type[] { typeof(Vector2), typeof(Vector2), typeof(RichText), typeof(int) })]
    internal static class ShowTooltipPatch
    {
        private static void Postfix(OverlayHandler __instance, RichText richText, Vector2 __0)
        {
            Overlay.Cursor = __0;
            Overlay.OnShow(__instance, richText);
        }
    }

    /// 游戏每帧先问鼠标下的物品有没有 tooltip（它自己用这个关掉带 IS_TOOLTIP_DISABLED_TAG 的物品），没有就走它自己的隐藏。
    /// 所以“按住展开键才显示”只要在这里答没有：游戏不显示，别的模组挂在 tooltip 上的东西也跟着不出现，不用我们去藏。
    [HarmonyPatch(typeof(GameItemElement), "HasTooltip")]
    internal static class HasTooltipPatch
    {
        private static void Postfix(GameItemElement __instance, ref bool __result)
        {
            if (__result) Expand.Poll(__instance);
            if (__result && Expand.Concealed) __result = false;
        }
    }

    [HarmonyPatch(typeof(OverlayHandler), "HideTooltip")]
    internal static class HideTooltipPatch
    {
        private static void Postfix()
        {
            try { Overlay.Hide(); }
            catch { }
        }
    }

    /// 游戏给所有模组用同一套方式建设置面板：滑块不显示数值，纸色和标签色的列表只有名字。等它建好我们的面板，SettingsPanel 再补上缺的部分。面板结构不是预期的样子就原样不动。
    [HarmonyPatch(typeof(ModConfigWindow), "Open")]
    internal static class SettingsPanelPatch
    {
        private static void Postfix(LoadedMod mod, CustomUIWindow __result)
        {
            if (__result == null || mod == null || mod.Manifest == null || mod.Manifest.ID != SectionedTooltipsMod.Id) return;
            try { SettingsPanel.Decorate(__result); }
            catch (Exception e) { SectionedTooltipsMod.Error("could not add to the settings panel, it stays as the game made it: " + e.Message); }
        }
    }
}
