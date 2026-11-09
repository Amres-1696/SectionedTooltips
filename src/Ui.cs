using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SectionedTooltips
{
    /// 侧边面板和估价单共用的小 uGUI 构件。所有东西都从父节点左上角定位：x 向右，yTop 向下。
    internal static class Ui
    {
        internal static RectTransform NewRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            TopLeft(rt);
            return rt;
        }

        internal static Image NewImage(RectTransform parent, Color color)
        {
            GameObject go = new GameObject("img", typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = color;
            TopLeft(img.rectTransform);
            return img;
        }

        /// tooltip 字体的一段文字，按内容定尺寸；wrap 大于 0 时按它折行。
        internal static TextMeshProUGUI NewText(RectTransform parent, TMP_Text src, float size, string rich, Color color, float wrap)
        {
            return NewText(parent, src, size, rich, color, wrap, Vector2.zero);
        }

        /// 同上；measured 不为零时是同样字体字号下已经量好的首选尺寸，这样不折行的文字不用再排一遍只为量尺寸。
        internal static TextMeshProUGUI NewText(RectTransform parent, TMP_Text src, float size, string rich, Color color, float wrap, Vector2 measured)
        {
            return NewText(parent, src, size, rich, color, wrap, measured, null);
        }

        /// 同上，给了 reuse（已经不用了的文字）就在它上面改造，不新建。
        internal static TextMeshProUGUI NewText(RectTransform parent, TMP_Text src, float size, string rich, Color color, float wrap, Vector2 measured, TextMeshProUGUI reuse)
        {
            TextMeshProUGUI t;
            if (reuse != null)
            {
                t = reuse;
                t.gameObject.layer = parent.gameObject.layer;
                t.transform.SetParent(parent, false);
            }
            else
            {
                GameObject go = new GameObject("text", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                t = go.GetComponent<TextMeshProUGUI>();
            }
            // 只有不一样时才设：设字体或材质会让 TextMeshPro 重建它的材质。
            if (t.font != src.font) t.font = src.font;
            if (t.fontSharedMaterial != src.fontSharedMaterial) t.fontSharedMaterial = src.fontSharedMaterial;
            t.fontSize = size;
            t.richText = true;
            t.enableWordWrapping = wrap > 0f;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.raycastTarget = false;
            t.margin = Vector4.zero;
            t.color = color;
            t.text = rich;
            TopLeft(t.rectTransform);
            if (wrap <= 0f)
            {
                Vector2 pref = measured != Vector2.zero ? measured : t.GetPreferredValues(rich);
                t.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(pref.x), Mathf.Ceil(pref.y));
                return t;
            }
            // 折行的文字：按真实宽度排一次再读回行数。GetPreferredValues 对折行的中文富文本会少数行，导致下一行压到上一行上。ignoreActiveState：TMP 对未激活的对象跳过排版，而估价单常在两次悬停之间隐藏着构建。
            t.rectTransform.sizeDelta = new Vector2(wrap, 10000f);
            t.ForceMeshUpdate(true, true);
            TMP_TextInfo ti = t.textInfo;
            // 末行只剩一两个字（“架。”“/天”）时把宽度收两个字，让上一行匀几个字下来；收了反而多出一行或还是孤字就不收。
            if (ti != null && ti.lineCount >= 2 && ti.lineInfo[ti.lineCount - 1].visibleCharacterCount <= 2 && wrap - 2f * size > wrap * 0.6f)
            {
                int lines = ti.lineCount;
                t.rectTransform.sizeDelta = new Vector2(wrap - 2f * size, 10000f);
                t.ForceMeshUpdate(true, true);
                ti = t.textInfo;
                if (ti.lineCount == lines && ti.lineInfo[ti.lineCount - 1].visibleCharacterCount > 2) wrap -= 2f * size;
                else
                {
                    t.rectTransform.sizeDelta = new Vector2(wrap, 10000f);
                    t.ForceMeshUpdate(true, true);
                    ti = t.textInfo;
                }
            }
            float h = t.preferredHeight;
            if (ti != null && ti.lineCount > 0) h = Mathf.Max(h, ti.lineInfo[0].ascender - ti.lineInfo[ti.lineCount - 1].descender);
            t.rectTransform.sizeDelta = new Vector2(wrap, Mathf.Ceil(h));
            return t;
        }

        internal static Quads NewQuads(RectTransform parent)
        {
            GameObject go = new GameObject("quads", typeof(RectTransform), typeof(Quads));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Quads q = go.GetComponent<Quads>();
            q.raycastTarget = false;
            TopLeft(q.rectTransform);
            q.rectTransform.anchoredPosition = Vector2.zero;
            return q;
        }

        internal static void Place(Graphic g, float x, float yTop, float w, float h)
        {
            Place(g.rectTransform, x, yTop, w, h);
        }

        internal static void Place(RectTransform rt, float x, float yTop, float w, float h)
        {
            TopLeft(rt);
            rt.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(yTop));
            rt.sizeDelta = new Vector2(w, h);
        }

        internal static void Move(RectTransform rt, float x, float yTop)
        {
            rt.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(yTop));
        }

        internal static void Stretch(Image img, float left, float bottom, float right, float top)
        {
            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void TopLeft(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }
    }

    /// 实心矩形合成一个 mesh，按添加顺序画，摆放方式同 Ui.Place，从左上角算。以前估价单的点线、虚线边、条形码、格子和进度条框每个都是一个 GameObject（满容器时 150 个以上），创建它们占了构建的大半时间。
    internal sealed class Quads : MaskableGraphic
    {
        private readonly List<Rect> rects = new List<Rect>();
        private readonly List<Color32> colors = new List<Color32>();

        internal int Count { get { return rects.Count; } }

        /// 调试导出用：第 i 个矩形和它的颜色。
        internal Rect RectAt(int i) { return rects[i]; }
        internal Color32 ColorAt(int i) { return colors[i]; }

        internal void Add(float x, float yTop, float w, float h, Color color)
        {
            if (w <= 0f || h <= 0f) return;
            rects.Add(new Rect(Mathf.Round(x), Mathf.Round(yTop), w, h));
            colors.Add(color);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < rects.Count; i++)
            {
                Rect r = rects[i];
                Color32 c = colors[i];
                int k = vh.currentVertCount;
                // 局部坐标原点在左上轴心，y 向上。
                vh.AddVert(new Vector3(r.x, -r.y - r.height), c, Vector2.zero);
                vh.AddVert(new Vector3(r.x, -r.y), c, Vector2.zero);
                vh.AddVert(new Vector3(r.x + r.width, -r.y), c, Vector2.zero);
                vh.AddVert(new Vector3(r.x + r.width, -r.y - r.height), c, Vector2.zero);
                vh.AddTriangle(k, k + 1, k + 2);
                vh.AddTriangle(k + 2, k + 3, k);
            }
        }
    }
}
