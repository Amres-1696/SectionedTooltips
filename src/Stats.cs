using System.Collections.Generic;
using UnityEngine;

namespace SectionedTooltips
{
    /// 调试用：帧率变慢时每秒记一行（有没有 tooltip 都记）：帧时间花在哪（游戏和其他模组的 tooltip、我们的排版、canvas 重建），以及贵的部分跑了多少次。适合查只在某些物品上出现的卡顿。
    internal static class Stats
    {
        private const double SlowAverageMs = 12.0;
        private const double HeavyTooltipMs = 3.0;
        private const int MaxLines = 600;

        private static bool hooked;
        private static long canvasStart;
        private static int tooltipFrame = -1, lines;
        private static float since = -1f;
        private static int frames, tipFrames, calls, layouts, fingerprints, reads, readItems;
        private static double frameMs, frameMax, tipFrameMs, basicMs, oursMs, canvasMs, tipCanvasMs, fingerprintMs, readMs;
        private static readonly HashSet<int> items = new HashSet<int>();
        private static string lastName = "";
        private static int lastInside;

        internal static bool On { get { return Settings.Debug != null && Settings.Debug.Value; } }

        /// 一次 GetTooltipBasic 调用：游戏和其他模组的部分、我们的部分，以及是不是重新排过版。
        internal static void Tooltip(GameItem item, double basic, double ours, bool fresh)
        {
            Hook();
            tooltipFrame = Time.frameCount;
            calls++;
            if (fresh) layouts++;
            basicMs += basic;
            oursMs += ours;
            if (item != null)
            {
                items.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item));
                try { lastName = TextUnits.Plain(item.GetDisplayName(false) ?? ""); }
                catch { }
            }
            lastInside = Layout.LastResult != null && Layout.LastResult.Contents != null ? Layout.LastResult.Contents.Total : 0;
        }

        internal static void Fingerprint(double ms)
        {
            fingerprints++;
            fingerprintMs += ms;
        }

        internal static void Read(double ms, int count)
        {
            reads++;
            readItems += count;
            readMs += ms;
        }

        private static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Canvas.preWillRenderCanvases += () => canvasStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Canvas.willRenderCanvases += Frame;
        }

        /// 每帧一次，在 canvas 重建之后。
        private static void Frame()
        {
            if (!On) return;
            double canvas = (System.Diagnostics.Stopwatch.GetTimestamp() - canvasStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double ms = Time.unscaledDeltaTime * 1000.0;
            frames++;
            frameMs += ms;
            if (ms > frameMax) frameMax = ms;
            canvasMs += canvas;
            if (Time.frameCount == tooltipFrame)
            {
                tipFrames++;
                tipFrameMs += ms;
                tipCanvasMs += canvas;
            }
            float now = Time.unscaledTime;
            if (since < 0f) since = now;
            if (now - since < 1f) return;
            // 一个重型 tooltip（大容器）单独占满一秒时也记，快慢都记，方便对比设置。
            bool heavy = tipFrames == frames && items.Count == 1 && basicMs / tipFrames >= HeavyTooltipMs;
            if (lines < MaxLines && (frameMs / frames > SlowAverageMs || heavy))
            {
                lines++;
                string tip = tipFrames == 0 ? "no tooltip up" : string.Format(
                    "contents panel " + (Settings.On(Settings.ContentsPanel) ? "on" : "off") + ", tooltip up {0} frames (avg {1:F1} ms): per frame {2:F1} tooltip calls over {3} item(s), game+other mods {4:F1} ms, ours {5:F1} ms, canvas {6:F1} ms; " +
                    "{7} layouts, {8} fingerprints {9:F1} ms, {10} contents reads ({11} items) {12:F1} ms; '{13}' {14} inside",
                    tipFrames, tipFrameMs / tipFrames, (double)calls / tipFrames, items.Count, basicMs / tipFrames, oursMs / tipFrames, tipCanvasMs / tipFrames,
                    layouts, fingerprints, fingerprintMs, reads, readItems, readMs, lastName, lastInside);
                SectionedTooltipsMod.Log(string.Format((heavy ? "heavy" : "slow") + " second: {0} frames, avg {1:F1} ms (max {2:F1}), canvas {3:F1} ms; {4}",
                    frames, frameMs / frames, frameMax, canvasMs / frames, tip));
            }
            since = now;
            frames = tipFrames = calls = layouts = fingerprints = reads = readItems = 0;
            frameMs = frameMax = tipFrameMs = basicMs = oursMs = canvasMs = tipCanvasMs = fingerprintMs = readMs = 0.0;
            items.Clear();
        }
    }
}
