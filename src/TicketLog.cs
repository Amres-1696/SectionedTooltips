using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 调试用：构建各阶段的耗时，以及把建好的单子写进日志（坐标、文字、矩形），给游戏外的查看器用。
    internal static partial class Ticket
    {
        // 一次构建的时间花在哪：各步骤、创建和测量的文字数，以及之后 Unity 的 canvas 重建（文字的 mesh 在那里生成，不在构建里）。调试关掉时为 null。
        private static System.Diagnostics.Stopwatch probe;
        private static readonly StringBuilder steps = new StringBuilder();
        private static long stepMark, textTicks, newTextTicks, wrapTicks, measureTicks;
        private static int texts, newTexts, wrapped, measures;
        private static int probeFrame = -10;
        private static bool canvasHooked;
        private static long canvasStart;

        private static void Step(string name)
        {
            if (probe == null) return;
            long now = probe.ElapsedTicks;
            steps.Append(name).Append(' ').Append(Ms(now - stepMark).ToString("F1", CultureInfo.InvariantCulture)).Append(", ");
            stepMark = now;
        }

        private static double Ms(long ticks)
        {
            return ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private static void StartProbe()
        {
            probe = Settings.Debug != null && Settings.Debug.Value ? System.Diagnostics.Stopwatch.StartNew() : null;
            if (probe == null) return;
            steps.Length = 0;
            stepMark = textTicks = newTextTicks = wrapTicks = measureTicks = 0L;
            texts = newTexts = wrapped = measures = 0;
            probeFrame = Time.frameCount;
            if (canvasHooked) return;
            // 比 UI 自己的处理注册得晚，所以 canvas 重建完之后才轮到我们。
            canvasHooked = true;
            Canvas.preWillRenderCanvases += () => canvasStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Canvas.willRenderCanvases += CanvasDone;
        }

        private static void CanvasDone()
        {
            int f = Time.frameCount;
            if (f == probeFrame)
                SectionedTooltipsMod.Log(string.Format("  canvas rebuild after it {0:F1} ms, frame {1}", Ms(System.Diagnostics.Stopwatch.GetTimestamp() - canvasStart), f));
            else if (f == probeFrame + 1)
                SectionedTooltipsMod.Log(string.Format("  frame {0} took {1:F1} ms in all", probeFrame, Time.unscaledDeltaTime * 1000f));
        }

        /// <summary>
        /// 调试用（记录分组日志开着时）：把这张单子的文字块和坐标写成一行日志，
        /// 游戏外可以用 tool\view.py 拼回预览、跑排版检查。不写任何文件，不需要文件权限。
        /// </summary>
        private static void LogSlip(Ctx c, GameItemElement item, RectTransform card, float w, float h)
        {
            try
            {
                if (card == null) return;
                // 尺寸用构建时算好的 w/h：card.rect 在 anchoredPosition/sizeDelta 刚写完时可能还是旧值
                // （多列卡片就会报小一半，导出后看什么都"出界"）。
                card.ForceUpdateRectTransforms();
                Rect cr = card.rect;
                float cw = Mathf.Round(Mathf.Max(w, cr.width)), ch = Mathf.Round(Mathf.Max(h, cr.height));
                StringBuilder sb = new StringBuilder(2048);
                sb.Append("[slip] ").Append(Clean(SlipName(item)))
                  .Append(' ').Append(cw).Append('x').Append(ch);
                TextMeshProUGUI[] texts = card.GetComponentsInChildren<TextMeshProUGUI>(true);
                Vector3[] corners = new Vector3[4];
                for (int i = 0; i < texts.Length; i++)
                {
                    TextMeshProUGUI t = texts[i];
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    t.rectTransform.GetWorldCorners(corners);
                    Vector3 lo = card.InverseTransformPoint(corners[0]);
                    Vector3 hi = card.InverseTransformPoint(corners[2]);
                    float x = Mathf.Min(lo.x, hi.x) - cr.xMin;
                    float top = cr.yMax - Mathf.Max(lo.y, hi.y);
                    float tw = Mathf.Abs(hi.x - lo.x);
                    float th = Mathf.Abs(hi.y - lo.y);
                    if (tw <= 0.5f || th <= 0.5f) continue;
                    sb.Append(" | ").Append(Clean(t.text))
                      .Append('@').Append(Mathf.Round(x)).Append(',').Append(Mathf.Round(top))
                      .Append(',').Append(Mathf.Round(tw)).Append(',').Append(Mathf.Round(th))
                      .Append(',').Append(Mathf.Round(t.fontSize))
                      .Append(',').Append(Hex(t.color));
                }
                SectionedTooltipsMod.Log(sb.ToString());

                // 矩形（底板、进度条、色块、虚线）另起一行：和纸一样是画出来的，不是文字。
                // 每一块（分栏用的 Part）都有自己的矩形 mesh，全部导出，换算到卡片坐标。
                Quads[] all = card.GetComponentsInChildren<Quads>(true);
                if (all.Length > 0)
                {
                    StringBuilder qb = new StringBuilder(4096);
                    qb.Append("[quad] ").Append(Clean(SlipName(item)));
                    for (int k = 0; k < all.Length; k++)
                    {
                        Quads quads = all[k];
                        float dx = 0f, dy = 0f;
                        for (Transform t = quads.transform; t != null && t != card; t = t.parent)
                        {
                            RectTransform rt = t as RectTransform;
                            if (rt == null) continue;
                            dx += rt.anchoredPosition.x;
                            dy -= rt.anchoredPosition.y;
                        }
                        for (int i = 0; i < quads.Count; i++)
                        {
                            Rect r = quads.RectAt(i);
                            qb.Append(" | ").Append(Mathf.Round(r.x + dx)).Append(',').Append(Mathf.Round(r.y + dy))
                              .Append(',').Append(Mathf.Round(r.width)).Append(',').Append(Mathf.Round(r.height))
                              .Append(',').Append(Hex(quads.ColorAt(i)));
                        }
                    }
                    SectionedTooltipsMod.Log(qb.ToString());
                }
                if (c.Screen < float.MaxValue && c.ScreenW < float.MaxValue)
                    SectionedTooltipsMod.Log(string.Format(CultureInfo.InvariantCulture, "[room] {0} {1:F0} {2:F0} {3:F4} {4:F2}",
                        Clean(SlipName(item)), c.Screen, c.ScreenW, card.localScale.x, c.Fit));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("导出这张单子失败：" + e.Message);
            }
        }

        /// 日志和截图里这张单子的名字：同一件物品收起的、展开的各记各的，比较时不会互相覆盖。
        private static string SlipName(GameItemElement item)
        {
            string name = DumpName(item);
#if DEVDUMP
            if (Stress.Label != null) return name;
#endif
            return builtFull ? (Expand.Enabled ? name + " 展开" : name) : name + " 收起";
        }

        private static string DumpName(GameItemElement item)
        {
            string name;
            try { name = item != null ? item.GetDisplayName(false) : "?"; }
            catch { name = "?"; }
#if DEVDUMP
            if (Stress.Label != null) return "压力:" + Stress.Label + " · " + name;
#endif
            return name;
        }

        private static string Hex(Color c)
        {
            Color32 v = c;
            return ((v.r << 16) | (v.g << 8) | v.b).ToString("X6");
        }

        /// 日志里用 | 和 @ 分隔，正文里的这两个字符换成全角，免得解析错位。
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', '｜').Replace('@', '＠').Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
