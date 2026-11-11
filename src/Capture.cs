using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SectionedTooltips
{
    /// 一段 slice 区间 [Start, End)，由编号为 Ord 的 helper 写入。
    internal struct Mark
    {
        public int Ord;
        public int Start;
        public int End;
    }

    /// 一次 GetTooltipBasic 调用期间的状态，helper 运行时往里记。
    internal sealed class Frame
    {
        public GameItem Item;
        public RichTextBuilder Builder;
        public IList Slices;
        public int Depth;
        public int CurOrd;
        public int CurStart;
        public readonly List<Mark> Marks = new List<Mark>();
        public readonly List<BarMark> Bars = new List<BarMark>();

        public void Reset(GameItem item)
        {
            Item = item;
            Builder = null;
            Slices = null;
            Depth = 0;
            CurOrd = -1;
            CurStart = 0;
            Marks.Clear();
            Bars.Clear();
        }
    }

    internal static class Capture
    {
        // GetTooltipBasic 可能嵌套（helper 描述容器里的物品），所以用栈。
        private static readonly List<Frame> stack = new List<Frame>();
        private static readonly List<Frame> pool = new List<Frame>();

        internal static void Push(GameItem item)
        {
            Frame f;
            if (pool.Count > 0) { f = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); }
            else f = new Frame();
            f.Reset(item);
            stack.Add(f);
        }

        internal static void Pop()
        {
            if (stack.Count == 0) return;
            Frame f = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            f.Reset(null);
            pool.Add(f);
        }

        internal static Frame Top { get { return stack.Count > 0 ? stack[stack.Count - 1] : null; } }
        internal static int Depth { get { return stack.Count; } }

        internal static void Enter(MethodBase helper, RichTextBuilder b)
        {
            Frame f = Top;
            if (f == null || b == null) return;
            if (f.Depth++ > 0) return; // helper 调用了另一个已 patch 的 helper：区间归外层。
            if (!ReferenceEquals(b, f.Builder))
            {
                f.Builder = b;
                f.Slices = SliceAccess.Slices(b);
            }
            f.CurOrd = Helpers.Ordinal(helper);
            f.CurStart = f.Slices != null ? f.Slices.Count : 0;
        }

        /// 进度条开始前 builder 的 slice 数；不是进度条返回 -1。
        internal static int BarStart(RichTextBuilder b)
        {
            Frame f = Top;
            if (f == null || b == null) return -1;
            if (f.Builder == null)
            {
                f.Builder = b;
                f.Slices = SliceAccess.Slices(b);
            }
            if (!ReferenceEquals(b, f.Builder) || f.Slices == null) return -1;
            return f.Slices.Count;
        }

        internal static void BarEnd(RichTextBuilder b, int start, float ratio, RenderHandler.ColorPalette color, RenderHandler.ColorPalette background)
        {
            Frame f = Top;
            if (f == null || start < 0 || !ReferenceEquals(b, f.Builder) || f.Slices == null) return;
            BarMark m = new BarMark();
            m.Start = start;
            m.End = f.Slices.Count;
            m.Ratio = float.IsNaN(ratio) ? 0f : Math.Max(0f, Math.Min(1f, ratio));
            m.Color = color;
            m.Background = background;
            f.Bars.Add(m);
        }

        internal static void Exit(RichTextBuilder b)
        {
            Frame f = Top;
            if (f == null || f.Depth == 0) return;
            if (--f.Depth > 0) return;
            if (!ReferenceEquals(b, f.Builder) || f.Slices == null || f.CurOrd < 0) return;
            Mark m;
            m.Ord = f.CurOrd;
            m.Start = f.CurStart;
            m.End = f.Slices.Count;
            f.Marks.Add(m);
        }
    }
}
