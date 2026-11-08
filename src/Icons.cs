using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SectionedTooltips
{
    /// <summary>
    /// 物品图标的副本：裁掉透明边，再按要画的大小放大。
    /// 游戏的图标 sprite 四周常有一圈透明，大小也不一（矿石比助溶剂的画布大），直接塞进方框时有的只占一半。
    /// 像素画只能按整数倍放大，否则有的像素行变宽、有的被吞掉：能放整数倍时用点采样直接放大；
    /// 放不下整数倍时，先最近邻放大到不小于目标的整数倍，再让双线性缩小那一点零头，边缘只软一个像素。
    /// </summary>
    internal static class Icons
    {
        /// 一个图标裁好后的像素（从下往上逐行），透明处的颜色已经从旁边的像素借过来，双线性插值时边缘不会发黑。
        private sealed class Trim
        {
            public int W, H;
            public Color32[] Pixels;
        }

        /// 已经画到 RenderTexture 上、还没读回来的图标。
        private sealed class Pending
        {
            public Sprite Sprite;
            public RenderTexture Rt;
            public int W, H;
        }

        private static readonly Dictionary<int, Trim> trims = new Dictionary<int, Trim>();
        /// 放大后的副本；做失败的记成 null，不每帧再试。
        private static readonly Dictionary<long, Sprite> scaled = new Dictionary<long, Sprite>();
        private static readonly List<Pending> pending = new List<Pending>();
        private static readonly List<Sprite> one = new List<Sprite>(1);
        /// 放大后的边最多多少像素。副本一直留着（还存着的估价单可能在用），所以限住每张的大小：几百种物品全看一遍也只有几十 MB。
        private const int MaxSide = 256;
        /// 整数倍比实际可放的小不到这么多时，就用整数倍（每个像素一样大，最清楚）。
        private const float IntegerShare = 0.9f;

        /// <summary>
        /// 把图标放进 w×h（卡片单位）的框里：px 是每单位多少屏幕像素。返回要画的 sprite 和它的大小；做不了时返回 null，调用方照旧画原图。
        /// </summary>
        internal static Sprite Fit(Sprite sprite, float w, float h, float px, out float outW, out float outH)
        {
            outW = outH = 0f;
            if (sprite == null || w <= 0f || h <= 0f || px <= 0f) return null;
            Trim t = TrimOf(sprite);
            if (t == null) return null;

            // 图标每个像素占多少屏幕像素。
            float s = Mathf.Min(w * px / t.W, h * px / t.H);
            float k = Mathf.Floor(s);
            bool point = k >= 1f && k / s >= IntegerShare;
            int n;
            if (point)
            {
                // 整数倍：原尺寸的副本用点采样放大。位置不一定落在整屏幕像素上（卡片单位取整），点采样也不会混色。
                s = k;
                n = 1;
            }
            else
            {
                n = Mathf.CeilToInt(s - 0.001f);
                int cap = Mathf.Max(1, MaxSide / Mathf.Max(t.W, t.H));
                // 放大的副本封顶了，就按副本原大画，不再让双线性往上拉（会糊）。
                if (n > cap) { n = cap; s = cap; }
                if (n < 1) n = 1;
            }
            Sprite result = Scaled(sprite, t, n, point);
            if (result == null) return null;
            outW = t.W * s / px;
            outH = t.H * s / px;
            return result;
        }

        /// <summary>
        /// 先把这些图标一起读出来：每个图标读回像素都要等显卡画完，一格格读就是等很多次；
        /// 先全部画到 RenderTexture 上再一起读，只等第一次。内容物网格在画图标之前调用。
        /// </summary>
        internal static void Prepare(IList<Sprite> sprites)
        {
            try
            {
                for (int i = 0; i < sprites.Count; i++)
                {
                    Sprite s = sprites[i];
                    if (s == null || trims.ContainsKey(s.GetInstanceID())) continue;
                    trims[s.GetInstanceID()] = null; // 同一批里重复的只读一次；读失败也就留着 null
                    try { Begin(s); }
                    catch (Exception e) { SectionedTooltipsMod.Log("读图标 " + s.name + " 失败，照原图画：" + e.Message); }
                }
                for (int i = 0; i < pending.Count; i++)
                {
                    try { Finish(pending[i]); }
                    catch (Exception e) { SectionedTooltipsMod.Log("读图标 " + pending[i].Sprite.name + " 失败，照原图画：" + e.Message); }
                }
            }
            finally
            {
                for (int i = 0; i < pending.Count; i++) RenderTexture.ReleaseTemporary(pending[i].Rt);
                pending.Clear();
            }
        }

        /// t 下面每单位占多少屏幕像素（经典风格的图标用；估价单自己算好了 Ctx.Px）。
        internal static float PxOf(Transform t)
        {
            Canvas cv = t != null ? t.GetComponentInParent<Canvas>() : null;
            if (cv == null) return 1f;
            Canvas top = cv.rootCanvas;
            float cs = top.transform.lossyScale.x, ts = t.lossyScale.x;
            return cs > 0f && ts > 0f ? ts / cs * top.scaleFactor : top.scaleFactor;
        }

        private static Trim TrimOf(Sprite sprite)
        {
            Trim t;
            if (trims.TryGetValue(sprite.GetInstanceID(), out t)) return t;
            one.Clear();
            one.Add(sprite);
            Prepare(one);
            one.Clear();
            trims.TryGetValue(sprite.GetInstanceID(), out t);
            return t;
        }

        /// 一比一画到 RenderTexture 上：图标贴图一般不可读。采样点正好落在纹素中心，点采样或双线性都不会混色。
        private static void Begin(Sprite sprite)
        {
            Texture2D src = sprite.texture;
            if (src == null) return;
            // 紧密打包或旋转过的图集里没有正放的矩形区域。
            if (sprite.packed && (sprite.packingMode == SpritePackingMode.Tight || sprite.packingRotation != SpritePackingRotation.None)) return;
            Rect r = sprite.textureRect;
            int x0 = Mathf.RoundToInt(r.x), y0 = Mathf.RoundToInt(r.y);
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            if (w <= 0 || h <= 0) return;

            Pending p = new Pending();
            p.Sprite = sprite;
            p.W = w;
            p.H = h;
            p.Rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            pending.Add(p);
            Graphics.Blit(src, p.Rt, new Vector2((float)w / src.width, (float)h / src.height), new Vector2((float)x0 / src.width, (float)y0 / src.height));
        }

        private static void Finish(Pending p)
        {
            int w = p.W, h = p.H;
            RenderTexture prev = RenderTexture.active;
            Texture2D tmp = null;
            Color32[] all;
            try
            {
                RenderTexture.active = p.Rt;
                tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                all = tmp.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                if (tmp != null) Object.Destroy(tmp);
            }

            // 不透明部分的外框。
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (all[y * w + x].a < 8) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            if (maxX < 0) return;

            Trim t = new Trim();
            t.W = maxX - minX + 1;
            t.H = maxY - minY + 1;
            t.Pixels = new Color32[t.W * t.H];
            for (int y = 0; y < t.H; y++)
                Array.Copy(all, (minY + y) * w + minX, t.Pixels, y * t.W, t.W);
            Bleed(t);
            trims[p.Sprite.GetInstanceID()] = t;
            if (Settings.Debug != null && Settings.Debug.Value)
                SectionedTooltipsMod.Log(string.Format("[icon] {0} {1}x{2} -> {3}x{4}", p.Sprite.name, w, h, t.W, t.H));
        }

        /// 透明像素借相邻不透明像素的颜色（透明度不变）。
        private static void Bleed(Trim t)
        {
            Color32[] p = t.Pixels;
            Color32[] src = (Color32[])p.Clone();
            for (int y = 0; y < t.H; y++)
                for (int x = 0; x < t.W; x++)
                {
                    int i = y * t.W + x;
                    if (src[i].a >= 8) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= t.W || ny >= t.H) continue;
                            Color32 q = src[ny * t.W + nx];
                            if (q.a < 8) continue;
                            r += q.r; g += q.g; b += q.b; n++;
                        }
                    if (n > 0) p[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), src[i].a);
                }
        }

        private static Sprite Scaled(Sprite sprite, Trim t, int n, bool point)
        {
            long key = ((long)sprite.GetInstanceID() << 12) | ((long)n << 1) | (point ? 1L : 0L);
            Sprite s;
            if (scaled.TryGetValue(key, out s)) return s;
            s = null;
            try { s = Make(sprite, t, n, point); }
            catch (Exception e) { SectionedTooltipsMod.Log("放大图标 " + sprite.name + " 失败，照原图画：" + e.Message); }
            scaled[key] = s;
            return s;
        }

        private static Sprite Make(Sprite sprite, Trim t, int n, bool point)
        {
            int w = t.W * n, h = t.H * n;
            Color32[] big = t.Pixels;
            if (n > 1)
            {
                big = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    int row = (y / n) * t.W;
                    for (int x = 0; x < w; x++) big[y * w + x] = t.Pixels[row + x / n];
                }
            }
            // 只有比原图还小时才要 mipmap（缩得多也不会跳着取像素）；放大的副本缩小不到一半，带了反而会挑小一级、整张发糊。
            bool mips = !point && n == 1;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            tex.name = "SectionedTooltips icon " + sprite.name;
            tex.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(big);
            tex.Apply(mips, true);
            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = tex.name;
            return s;
        }
    }
}
