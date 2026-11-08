using System;
using System.Collections.Generic;
using UnityEngine;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    // 槽位里的类型（AquaSense 1.12.0 的 CompositionApi）：每行是（名称、体积、百分比、占比 0~1、条色 0xRRGGBB）。
    using Rows = IReadOnlyList<ValueTuple<string, string, string, float, int>>;
    using ReadRows = Func<GameItem, IReadOnlyList<ValueTuple<string, string, string, float, int>>>;

    /// <summary>
    /// 和《更好的水商操作体验》（AquaSense）的对接。它把水容器的成分行放在 AppDomain 槽位里，我们只读，不引用它的 DLL。
    /// 它的成分进度条是盖在原版文字上的 UI 矩形，文字里只留了一个 &lt;link&gt; 占位；估价单是另画的，看不到那层矩形。
    /// 所以把带占位的成分行换成“名称：条 体积 百分比”，条用它给的占比和颜色由我们画。
    /// 它切到纯文本列表时没有占位，那几行照原样排。
    /// </summary>
    internal static class AquaSense
    {
        private const string DataSlot = "aquasense.composition.v1";
        private const string Marker = "AquaSenseCompositionBar:";
        // 条的占位：不能是空格（拆标签时行首空格会被去掉），也得是游戏字体里有的字，量宽度时才不报缺字。
        private const string BarText = "—";

        /// <summary>读这件物品的成分行；没装它、不是装了水的容器、或它关了成分显示时返回 null。</summary>
        private static Rows Read(GameItem item)
        {
            ReadRows read = AppDomain.CurrentDomain.GetData(DataSlot) as ReadRows;
            if (read == null) return null;
            try { return read(item); }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("读 AquaSense 成分失败，按文本处理：" + e.Message);
                return null;
            }
        }

        /// <summary>把它带占位的成分行换成我们画条的行。只管估价单：经典风格还是原版窗口，它自己的条照样画得出来。</summary>
        internal static void Apply(List<Line> lines, GameItem item)
        {
            if (item == null || !Settings.IsTicket) return;
            bool any = false;
            for (int i = 0; i < lines.Count && !any; i++) any = HasMarker(lines[i]);
            if (!any) return;
            Rows rows = Read(item);
            if (rows == null || rows.Count == 0) return;

            // 它按槽位返回的同一份列表、同样的顺序逐行写，截短也只截行尾；所以第几行占位就是第几项，不按名字找（截短的名字会配错）。
            // 每行后面那条零宽的进度条走的是 AddProgressBarOld，我们不当它是进度条，它的文字是空的，留着不影响排版。
            int next = 0;
            for (int i = 0; i < lines.Count && next < rows.Count; i++)
                if (HasMarker(lines[i])) Rewrite(lines[i], rows[next++]);
        }

        /// 占位在一次 Add 写进去的整段里，不会跨段，逐段找就行，不用拼整行。
        private static bool HasMarker(Line l)
        {
            for (int i = 0; i < l.Segs.Count; i++)
            {
                string t = l.Segs[i].Text;
                if (t != null && t.IndexOf(Marker, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static void Rewrite(Line l, ValueTuple<string, string, string, float, int> row)
        {
            Seg style = null;
            for (int i = 0; i < l.Segs.Count && style == null; i++)
                if (l.Segs[i].Bar == null && TextUnits.Plain(l.Segs[i].Text).Trim().Length > 0) style = l.Segs[i];
            if (style == null) style = new Seg();

            string name = (row.Item1 ?? "").Trim(), amount = (row.Item2 ?? "").Trim(), percent = (row.Item3 ?? "").Trim();
            string value = amount.Length > 0 && percent.Length > 0 ? amount + "  " + percent : amount + percent;
            List<Seg> segs = new List<Seg>();
            segs.Add(Plain(style, name));
            segs.Add(Plain(style, "："));
            if (Settings.On(Settings.Bars))
            {
                Seg bar = Plain(style, BarText);
                bar.Bar = new BarMark();
                bar.Bar.Ratio = Mathf.Clamp01(row.Item4);
                bar.Bar.HasRgb = true;
                int rgb = row.Item5;
                bar.Bar.Rgb = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                segs.Add(bar);
            }
            segs.Add(Plain(style, value));
            l.Segs = segs;
            l.Plain = name + "：" + value;
            if (l.Key == Sections.Mods || l.Key == Sections.Info) l.Key = Sections.Liquid;
        }

        private static Seg Plain(Seg style, string text)
        {
            Seg s = style.With(text);
            s.Bar = null;
            s.NewLine = false;
            s.Background = Palette.None;
            return s;
        }
    }
}
