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
    /// 纸和墨：可选的纸色，游戏的颜色怎么换成纸上的墨色，带颜色的文字怎么写。
    internal static partial class Ticket
    {
        // 纸的颜色；UsePaper 每次构建时切换。深色纸上墨是浅的。
        private static Color Paper, PaperShade, PaperEdge, InkSoft, Ink, FlagRed;
        private static bool darkPaper;

        /// 两个纸色设置的可选项，原样显示在设置菜单里。
        internal static readonly string[] PaperNames =
        {
            "米黄 Cream", "粉 Pink", "蓝 Blue", "绿 Green", "黄 Yellow",
            "炭黑 Charcoal", "藏青 Navy", "墨绿 Forest", "深棕 Walnut",
        };

        private static readonly Color32 BrownInk = new Color32(59, 42, 26, 255);

        // PaperNames 每一项的纸色、暗调、边、淡墨和墨。
        private static readonly Color32[][] Papers =
        {
            new[] { new Color32(233, 221, 191, 255), new Color32(222, 207, 170, 255), new Color32(185, 167, 124, 255), new Color32(122, 100, 64, 255), BrownInk },
            new[] { new Color32(242, 206, 201, 255), new Color32(230, 186, 181, 255), new Color32(198, 134, 130, 255), new Color32(140, 62, 60, 255), BrownInk },
            new[] { new Color32(204, 220, 232, 255), new Color32(184, 203, 219, 255), new Color32(122, 150, 176, 255), new Color32(52, 82, 116, 255), BrownInk },
            new[] { new Color32(208, 228, 200, 255), new Color32(188, 213, 179, 255), new Color32(124, 160, 112, 255), new Color32(54, 96, 50, 255), BrownInk },
            new[] { new Color32(239, 226, 166, 255), new Color32(227, 211, 143, 255), new Color32(196, 173, 94, 255), new Color32(138, 109, 31, 255), BrownInk },
            new[] { new Color32(40, 37, 42, 255), new Color32(52, 48, 55, 255), new Color32(92, 85, 94, 255), new Color32(164, 154, 142, 255), new Color32(232, 224, 208, 255) },
            new[] { new Color32(30, 40, 58, 255), new Color32(40, 52, 73, 255), new Color32(80, 98, 126, 255), new Color32(142, 162, 188, 255), new Color32(222, 230, 240, 255) },
            new[] { new Color32(31, 47, 39, 255), new Color32(41, 61, 51, 255), new Color32(80, 108, 92, 255), new Color32(146, 176, 156, 255), new Color32(224, 236, 226, 255) },
            new[] { new Color32(56, 42, 32, 255), new Color32(70, 54, 42, 255), new Color32(114, 92, 72, 255), new Color32(186, 162, 130, 255), new Color32(240, 228, 206, 255) },
        };

        /// 设置里选的纸。默认自己物品用米黄，未拥有的用复写的粉色，不读内容就知道不是自己的（原来默认黄色，游戏里和米黄太接近）。
        private static void UsePaper(bool notOwned)
        {
            ConfigEntry<string> e = notOwned ? Settings.NotOwnedPaper : Settings.Paper;
            int i = e != null ? Array.IndexOf(PaperNames, e.Value) : -1;
            UsePaper(i >= 0 ? i : notOwned ? 1 : 0);
        }

        private static void UsePaper(int i)
        {
            if (i < 0 || i >= Papers.Length) i = 0;
            Paper = Papers[i][0];
            PaperShade = Papers[i][1];
            PaperEdge = Papers[i][2];
            InkSoft = Papers[i][3];
            Ink = Papers[i][4];
            darkPaper = Paper.grayscale < 0.4f;
            inks.Clear();
            // 赃物、违禁品标签和红边的那点暗红在深色纸上会看不出来：深色纸上用亮一点的。
            FlagRed = darkPaper ? (Color)new Color32(226, 104, 86, 220) : (Color)new Color32(158, 60, 50, 200);
        }

        /// 文字的墨色。设置里开着“文字颜色”（默认）时保留游戏和其他模组给的颜色；关掉时只留红、橙、黄这些提醒色，其余统一用 fallback（纸自己的墨）。
        /// 进度条、分类标签这些不是文字的颜色不受这个设置影响，直接用 InkOf。
        private static Color TextInk(Palette p, Color fallback)
        {
            if (p == Palette.None) return fallback;
            if (Settings.KeepColors == null || Settings.KeepColors.Value) return InkOf(p, fallback);
            float h, s, v;
            Color.RGBToHSV(Overlay.ToColor(p, Palette.Gray), out h, out s, out v);
            return s >= 0.25f && (h < 0.17f || h > 0.94f) ? InkOf(p, fallback) : fallback;
        }

        /// 把游戏的调色板颜色转成纸上的墨（见下面的 InkOf(Color, Color)）；灰色改用纸自己的墨。
        private static Color InkOf(Palette p, Color fallback)
        {
            if (p == Palette.None) return fallback;
            return InkOf(Overlay.ToColor(p, Palette.Gray), fallback);
        }

        /// 任意颜色调到纸上，保留色相；太灰的用 fallback。
        /// 浅纸：颜色要鲜，亮度从亮往暗找，到和纸的对比度够了为止（一律压到同一个暗度时，橙、黄在米黄纸上都成了发闷的棕色）。
        /// 深纸：直接用亮的、略淡的颜色，和深纸上的白字一样亮。不能也按“刚好够”去找：那一档在深纸上是勉强能读的暗色，原来白色的数值会一下子变灰。
        private static Color InkOf(Color c, Color fallback)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            if (s < 0.25f) return fallback;
            // Color32 没有自己的 GetHashCode，默认的按字段异或会把红色通道抵消掉：只差红色的两个颜色会共用一个墨色。自己按 r、g、b 打包。
            Color32 c32 = c;
            int key = c32.r | (c32.g << 8) | (c32.b << 16);
            Color k;
            if (inks.TryGetValue(key, out k)) return k;
            if (darkPaper) k = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.55f + 0.1f), 0.92f);
            else
            {
                float sat = Mathf.Clamp01(Mathf.Max(s, 0.75f));
                k = Color.HSVToRGB(h, sat, 0.2f);
                for (int i = 0; i <= 40; i++)
                {
                    Color t = Color.HSVToRGB(h, sat, 0.95f - i * 0.0187f);
                    if (Contrast(t, Paper) >= InkContrast) { k = t; break; }
                }
            }
            k.a = 1f;
            inks[key] = k;
            return k;
        }

        /// 浅纸上彩色墨和纸的对比度：够读，又不至于压成黑色。
        private const float InkContrast = 4f;
        private static readonly Dictionary<int, Color> inks = new Dictionary<int, Color>();

        private static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static float Luminance(Color c)
        {
            return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
        }

        private static float Linear(float x)
        {
            return x <= 0.04045f ? x / 12.92f : Mathf.Pow((x + 0.055f) / 1.055f, 2.4f);
        }

        /// segs 转成墨色的富文本：保留游戏和其他模组（如 Item Details）给的颜色，调到纸上看得清的深浅；没颜色、太灰的用 baseInk。
        private static string InkRich(List<Seg> segs, Color baseInk)
        {
            if (segs == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (Drawn(s) || string.IsNullOrEmpty(s.Text)) continue;
                Color ink = TextInk(s.Color, baseInk);
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ink)).Append('>');
                if (s.Bold) sb.Append("<b>");
                sb.Append(s.Text);
                if (s.Bold) sb.Append("</b>");
                sb.Append("</color>");
            }
            return sb.ToString().Trim();
        }

        /// 估价单自己画的进度条。关掉图形进度条时，游戏自己的文字进度条（“[====    ]”）留在文字里，和经典风格一样。
        private static bool Drawn(Seg s)
        {
            return s.Bar != null && Settings.On(Settings.Bars);
        }

        private static BarMark BarOf(List<Seg> segs)
        {
            if (segs == null) return null;
            for (int i = 0; i < segs.Count; i++) if (Drawn(segs[i])) return segs[i].Bar;
            return null;
        }
    }
}
