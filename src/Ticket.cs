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
    /// “当铺估价单”风格：把 tooltip 画成一张纸单。它顶替游戏的 tooltip 窗口（窗口留在原地淡出，游戏还在给它定位），内容用文本排版分好的节，颜色转成深色墨。
    internal static partial class Ticket
    {
        private static RectTransform root;
        private static RectTransform card;
        private static RectTransform box;
        private static PixelWindow boxWindow;
        private static LayoutResult builtFor;
        private static GameItemElement builtItem;
        /// 当前这张是完整的还是收起的。
        private static bool builtFull;
        private static Vector2 cardSize;

        /// 已经建好、藏在根节点下的估价单，回到同一个物品时只需再显示一次。
        private sealed class Built
        {
            public LayoutResult For;
            public GameItemElement Item;
            public int Version;
            public RectTransform Card;
            public Vector2 Size;
            /// 完整的单子（展开键按着，或者没开收起）。
            public bool Full;
        }

        private const int KeptSlips = 8;
        /// 最近显示过的在前；当前这张也在里面。
        private static readonly List<Built> slips = new List<Built>();
        private static int lastBuildFrame = -10;
        /// 推迟到下一帧再建的物品（见 BuildNow）。
        private static GameItemElement waitingItem;
        private static CanvasGroup faded;
        private static RectTransform fadedWin;
        private static readonly Vector3[] corners = new Vector3[4];

        /// 当前估价单的根；交给 Item Details 当 tooltip 锚点用，没显示时返回 null。
        internal static RectTransform Card
        {
            get
            {
                return root != null && root.gameObject.activeSelf && card != null && card.gameObject.activeSelf
                    ? card : null;
            }
        }

        internal static void Show(PixelWindow window, TMP_Text tmp, LayoutResult r, GameItemElement item)
        {
            RectTransform win = window != null ? window.rectTransform : null;
            RectTransform parent = win != null ? win.parent as RectTransform : null;
            if (parent == null || tmp == null || r == null || r.Blocks == null)
            {
                Hide();
                return;
            }
            // 连续建失败到上限就不再画，显示游戏自己的 tooltip。
            if (Fuse.IsOpen(Fuse.TicketKey))
            {
                Hide();
                return;
            }
            // 这张排版对应的估价单建失败了，就显示游戏自己的 tooltip，免得每帧都失败。
            if (ReferenceEquals(r, failedFor) && ReferenceEquals(item, failedItem))
            {
                Hide();
                return;
            }
            if (box == null || !ReferenceEquals(boxWindow, window))
            {
                box = Panels.FindBox(win, tmp);
                boxWindow = window;
            }
            bool fresh = EnsureRoot(parent, win);
            bool hidden = fresh || !root.gameObject.activeSelf;
            // 构建前先激活：TextMeshPro 只给激活状态的文字排版和测量。
            root.gameObject.SetActive(true);
#if DEVDUMP
            Stress.PrepareSamples(item);
            if (Stress.Pending(item))
            {
                float sy0 = Mathf.Abs(root.localScale.y), sx0 = Mathf.Abs(root.localScale.x);
                Rect screen0 = Panels.ScreenRect(parent, win);
                RunStress(tmp, item, sy0 > 0f ? screen0.height / sy0 : float.MaxValue, sx0 > 0f ? screen0.width / sx0 : float.MaxValue);
            }
#endif
            bool full = Expand.Full;
            bool current = !fresh && card != null && ReferenceEquals(builtFor, r) && ReferenceEquals(builtItem, item) && builtFull == full;
            if (!current && (Reuse(r, item, full) || BuildNow(item, hidden)))
            {
                if (!ReferenceEquals(builtFor, r) || !ReferenceEquals(builtItem, item) || builtFull != full)
                {
                    // 窗口的高和宽换成卡片单位：比窗口高的单子分栏，分栏后比窗口宽的整张缩小（见 BuildCard）。
                    float sy = Mathf.Abs(root.localScale.y), sx = Mathf.Abs(root.localScale.x);
                    Rect screen = Panels.ScreenRect(parent, win);
                    Build(tmp, r, item, full, sy > 0f ? screen.height / sy : float.MaxValue, sx > 0f ? screen.width / sx : float.MaxValue);
                }
            }
            Position(parent, win);
            Raise();
            Fade(win);
#if DEVDUMP
            if (card != null) Shots.Seen(card, SlipName(item));
#endif
        }

        internal static void Hide()
        {
            if (root != null && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }
            if (faded != null) faded.alpha = 1f;
            faded = null;
        }

        /// 把游戏自己的 tooltip 窗口淡出（它照常排版、跟着鼠标）。
        private static void Fade(RectTransform win)
        {
            // 显示期间每帧都要：只有窗口换了才重新取它的 CanvasGroup。
            CanvasGroup cg = faded != null && ReferenceEquals(fadedWin, win) ? faded : win.GetComponent<CanvasGroup>();
            if (cg == null) cg = win.gameObject.AddComponent<CanvasGroup>();
            if (cg.alpha != 0f) cg.alpha = 0f;
            faded = cg;
            fadedWin = win;
        }

        private static bool EnsureRoot(RectTransform parent, RectTransform win)
        {
            bool fresh = false;
            if (root == null || root.parent != parent)
            {
                if (root != null) UnityEngine.Object.Destroy(root.gameObject);
                GameObject go = new GameObject("SectionedTooltipsTicket", typeof(RectTransform));
                go.layer = win.gameObject.layer;
                root = (RectTransform)go.transform;
                root.SetParent(parent, false);
                root.anchorMin = new Vector2(0.5f, 0.5f);
                root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0f, 1f);
                root.sizeDelta = Vector2.zero;
                card = null;
                slips.Clear(); // 它们的卡片跟着旧根节点一起没了
                spareTexts.Clear(); // 复用的文字也一样
                fresh = true;
            }
            // 玩家的缩放设置叠在游戏自己的 UI 缩放上。
            Vector3 scale = win.localScale * Settings.ScaleFactor;
            // 估价单按这个大小构建（Build 用 ScaleFactor 量）；大小改了会加 Settings.Version。
            root.localScale = scale;
            root.localRotation = Quaternion.identity;
            return fresh;
        }

        private static void Position(RectTransform parent, RectTransform win)
        {
            (box != null ? box : win).GetWorldCorners(corners);
            Vector3 bl = parent.InverseTransformPoint(corners[0]), tr = parent.InverseTransformPoint(corners[2]);
            float sx = Mathf.Abs(root.localScale.x), sy = Mathf.Abs(root.localScale.y);
            float w = cardSize.x * sx, h = cardSize.y * sy;
            Rect screen = Panels.ScreenRect(parent, win);
            // 钉在游戏窗口离鼠标最近的那个角，这样比窗口小（或大）的估价单也贴着鼠标，而不是贴着窗口另一边。
            float x = bl.x, y = tr.y;
            if (Overlay.Cursor.HasValue)
            {
                Vector2 c = Overlay.Cursor.Value;
                Vector3 m = parent.InverseTransformPoint(new Vector3(c.x, c.y, 0f));
                if (m.x > (bl.x + tr.x) / 2f) x = tr.x - w;
                if (m.y < (bl.y + tr.y) / 2f) y = bl.y + h;
            }
            // 估价单上方留给 Item Details 的分类标签（它自己贴在锚点上方，这里只留高度）。
            float above = 0f;
            RectTransform badge = Badge();
            if (badge != null)
            {
                float ps = Mathf.Abs(parent.lossyScale.y), ws = Mathf.Abs(win.lossyScale.y);
                if (ps > 0f && ws > 0f) above = (badge.rect.height + BadgeGap) * ws / ps;
            }
            if (x + w > screen.xMax) x = screen.xMax - w;
            if (x < screen.xMin) x = screen.xMin;
            if (y - h < screen.yMin) y = screen.yMin + h;
            if (y + above > screen.yMax) y = screen.yMax - above;
            root.localPosition = new Vector3(Mathf.Round(x), Mathf.Round(y), 0f);
        }

        // Item Details 会把“[机器]”那行换成一排彩色标签，画在锚点上方。我们把估价单的根注册成了
        // tooltip_anchor，位置由它自己摆；这里只按名字找到它，量个高度给上方留出空间。
        private const string BadgeName = "ItemDetailsTypeBadge";
        private const float BadgeGap = 4f; // Item Details 在窗口上方留的间距，窗口单位

        private static RectTransform Badge()
        {
            if (root == null || root.parent == null) return null;
            Transform t = root.parent.Find(BadgeName);
            return t != null && t.gameObject.activeInHierarchy ? t as RectTransform : null;
        }

        /// 把估价单提到最上层。Item Details 的标签和内容物浮窗自己贴锚点，这里不再动它们。
        private static void Raise()
        {
            if (root != null) root.SetAsLastSibling();
        }

        /// 如果这个排版和物品有存好的估价单，就显示它。
        private static bool Reuse(LayoutResult r, GameItemElement item, bool full)
        {
            for (int i = 0; i < slips.Count; i++)
            {
                Built s = slips[i];
                if (!ReferenceEquals(s.For, r) || !ReferenceEquals(s.Item, item) || s.Version != Settings.Version || s.Card == null || s.Full != full) continue;
                slips.RemoveAt(i);
                slips.Insert(0, s);
                if (card != null && card != s.Card) card.gameObject.SetActive(false);
                card = s.Card;
                cardSize = s.Size;
                builtFor = s.For;
                builtItem = s.Item;
                builtFull = s.Full;
                card.gameObject.SetActive(true);
                return true;
            }
            return false;
        }

        /// 这一帧要不要建这张估价单。鼠标扫货架时几乎每帧换一个物品，每次都建会掉帧；构建完的下一帧才第一次见到的物品先等一帧（上一张留着），这样扫过时最多隔帧建，只是路过的物品直接跳过。
        private static bool BuildNow(GameItemElement item, bool hidden)
        {
            if (hidden || card == null || ReferenceEquals(item, waitingItem)) return true;
            waitingItem = item;
            return Time.frameCount - lastBuildFrame > 1;
        }

        /// 上次建失败的排版和物品；见 Show。
        private static LayoutResult failedFor;
        private static GameItemElement failedItem;

        /// 构建估价单，失败就把画到一半的丢掉：半张卡片绝不能当成这个物品的成品显示（Show 会一直显示它，而且永远不会销毁）。
        private static void Build(TMP_Text tmp, LayoutResult r, GameItemElement item, bool full, float room, float roomW)
        {
            try
            {
                BuildFlow(tmp, r, item, full, room, roomW);
                Fuse.Ok(Fuse.TicketKey);
            }
            catch (Exception e)
            {
                Drop(card);
                card = null;
                builtFor = null;
                builtItem = null;
                failedFor = r;
                failedItem = item;
                Fuse.Fail(Fuse.TicketKey, e);
                throw;
            }
        }

        /// 把刚建好的卡片放到已存估价单的最前；丢掉最旧的、旧设置下建的，以及同一物品更早的：排版缓存每个物品只留一份，物品文字一变，旧估价单就再也显示不出来了。
        private static void Keep(LayoutResult r, GameItemElement item)
        {
#if DEVDUMP
            // 压力测试的单子建完就丢，不能挤掉存着的（尤其是同一件物品自己的那张）。
            if (Stress.Label != null) return;
#endif
            for (int i = slips.Count - 1; i >= 0; i--)
            {
                Built s = slips[i];
                if (s.Card == card) slips.RemoveAt(i);
                else if (s.Card == null || s.Version != Settings.Version || slips.Count >= KeptSlips
                    || (ReferenceEquals(s.Item, item) && (s.Full == builtFull || !ReferenceEquals(s.For, r))))
                {
                    Drop(s.Card);
                    slips.RemoveAt(i);
                }
            }
            Built b = new Built();
            b.For = r;
            b.Item = item;
            b.Version = Settings.Version;
            b.Card = card;
            b.Size = cardSize;
            b.Full = builtFull;
            slips.Insert(0, b);
        }
    }
}
