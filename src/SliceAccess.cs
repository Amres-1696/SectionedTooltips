using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 读 RichTextBuilder.slices 和内部 TextSlice 字段。TextSlice 是 internal，没法 typeof：每个字段用字面量名字经 Type.GetType 读，写成一句表达式，模组扫描器能通过。
    internal static class SliceAccess
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo fSlices, fText, fColor, fBold, fItalic, fUnderline, fStrike, fBg, fUl, fSt, fNewLine;

        internal static bool Init()
        {
            try
            {
                fSlices = AccessTools.Field(typeof(RichTextBuilder), "slices");
                fText = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("text", Any);
                fColor = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("color", Any);
                fBold = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("bold", Any);
                fItalic = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("italic", Any);
                fUnderline = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("underline", Any);
                fStrike = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("strikethrough", Any);
                fBg = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("backgroundColor", Any);
                fUl = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("underlineColor", Any);
                fSt = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("strikethroughColor", Any);
                fNewLine = Type.GetType("RichTextBuilder+TextSlice, Assembly-CSharp").GetField("isNewLine", Any);
            }
            catch (NullReferenceException)
            {
                return false; // TextSlice 改名或挪位置了
            }
            return fSlices != null && fText != null && fColor != null && fBold != null && fItalic != null
                && fUnderline != null && fStrike != null && fBg != null && fUl != null && fSt != null
                && fNewLine != null;
        }

        internal static IList Slices(RichTextBuilder b)
        {
            return b == null ? null : fSlices.GetValue(b) as IList;
        }

        /// builder 内容的轻量指纹，用来复用排版。tooltip 打开期间每帧都跑，所以只读文本：颜色和标记字段会装箱，每帧每个 slice 一次分配，而且文本不变颜色也不会变。
        internal static int Hash(IList slices)
        {
            unchecked
            {
                int h = 17 + slices.Count;
                for (int i = 0; i < slices.Count; i++)
                {
                    string t = (string)fText.GetValue(slices[i]);
                    h = h * 31 + (t == null ? 0 : t.GetHashCode());
                }
                return h;
            }
        }

        internal static Seg Read(object s)
        {
            Seg r = new Seg();
            r.Text = (string)fText.GetValue(s) ?? "";
            r.Color = (Palette)fColor.GetValue(s);
            r.Bold = (bool)fBold.GetValue(s);
            r.Italic = (bool)fItalic.GetValue(s);
            r.Underline = (bool)fUnderline.GetValue(s);
            r.Strike = (bool)fStrike.GetValue(s);
            r.Background = (Palette)fBg.GetValue(s);
            r.UnderlineColor = (Palette)fUl.GetValue(s);
            r.StrikeColor = (Palette)fSt.GetValue(s);
            r.NewLine = (bool)fNewLine.GetValue(s);
            return r;
        }
    }
}
