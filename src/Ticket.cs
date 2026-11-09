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
        // 纸的颜色；UsePaper 每次构建时切换。深色纸上墨是浅的。
        private static Color Paper, PaperShade, PaperEdge, InkSoft, Ink, StampRed;
        private static bool darkPaper;
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.4f);

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
            // 印章那点暗红在深色纸上会看不出来：深色纸上用亮一点的。
            StampRed = darkPaper ? (Color)new Color32(226, 104, 86, 220) : (Color)new Color32(158, 60, 50, 200);
        }

        private static RectTransform root;
        private static RectTransform card;
        private static RectTransform box;
        private static PixelWindow boxWindow;
        private static LayoutResult builtFor;
        private static GameItemElement builtItem;
        /// 当前这张是完整的还是收起的；buildFull 是下一张要建成哪种。
        private static bool builtFull, buildFull;
        private static Vector2 cardSize;
        /// 屏幕高度，用卡片单位表示：比这更高的估价单也会把附加信息和其他节挪到独立一栏。
        private static float slipRoom = float.MaxValue;
        /// 屏幕宽度，同样用卡片单位：分栏后比这更宽就整张缩小（见 BuildCard 末尾）。
        private static float slipRoomW = float.MaxValue;

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
                slipRoomW = sx0 > 0f ? screen0.width / sx0 : float.MaxValue;
                RunStress(tmp, item, sy0 > 0f ? screen0.height / sy0 : float.MaxValue);
            }
#endif
            bool full = Expand.Full;
            bool current = !fresh && card != null && ReferenceEquals(builtFor, r) && ReferenceEquals(builtItem, item) && builtFull == full;
            if (!current && (Reuse(r, item, full) || BuildNow(item, hidden)))
            {
                if (!ReferenceEquals(builtFor, r) || !ReferenceEquals(builtItem, item) || builtFull != full)
                {
                    buildFull = full;
                    float sy = Mathf.Abs(root.localScale.y), sx = Mathf.Abs(root.localScale.x);
                    Rect screen = Panels.ScreenRect(parent, win);
                    slipRoom = sy > 0f ? screen.height / sy : float.MaxValue;
                    slipRoomW = sx > 0f ? screen.width / sx : float.MaxValue;
                    Build(tmp, r, item);
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

        // ---------------------------------------------------------------- Item Details 的分类标签

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

        // ---------------------------------------------------------------- 构建

        /// 一次构建要用的全部东西：度量、卡片，以及用来测量的临时文字。
        private sealed class Ctx
        {
            public Panels.Metrics M;
            public TMP_Text Src;
            public GameItem Item;
            public RectTransform Card;
            public TextMeshProUGUI Scratch;
            public float U;      // 像素风里的一个“像素”
            public float Pad;
            public float Gap;
            public float F;      // 正文字号
            public float PriceScale = 2.2f; // 大字价格的倍数，相对 F
            public int[] TagColors;         // 每个标签组一个下标，指向 TagColorNames
            public bool TagWash = true;     // 标签底下垫一层淡色，还是只给文字上色
            public float W;      // 卡片宽度
            public float LabelCol; // 各节的标签列（见 TicketGrid）
            /// 各节的制表位列几何（见 TicketGrid.TabLayoutOf）；一次构建里算一次，绘制与宽度共用。
            public readonly Dictionary<Block, Ticket.TabLayout> Tabs = new Dictionary<Block, Ticket.TabLayout>();
            /// 液体一节带条成分行的数字列（见 TicketGrid.NumColsOf）。
            public readonly Dictionary<Block, Ticket.NumCols> Nums = new Dictionary<Block, Ticket.NumCols>();
            public float Px = 1f;  // 每个卡片单位占多少屏幕像素，图标要对齐整像素（见 PixelFit）
            public List<Piece> Pieces;  // 分栏用的各块（见 Flow）
            public bool Fine;           // 每行另起一块，一节可以从中间断到下一栏（单子比窗口高时才这样重建）
            public float Lift = 1f;     // 字号抬高的倍数，卡片整张缩回这么多（见 ReadableFonts）
            public float Room = float.MaxValue;  // 窗口高度，用这张卡片的单位
            public float Inner { get { return W - 2f * Pad; } }
            /// 卡片上的实心矩形，合成一个 mesh，画在纸上面、所有文字和图标下面。
            public Quads Q;

            // 一次构建里每段文字会量几次（宽度、行、创建时）：所以每次构建只排一次，尺寸复用。
            private readonly Dictionary<float, Dictionary<string, Vector2>> sizes = new Dictionary<float, Dictionary<string, Vector2>>();

            /// rich 在 size 字号下的不换行尺寸。
            public Vector2 Size(string rich, float size)
            {
                Dictionary<string, Vector2> bySize;
                if (!sizes.TryGetValue(size, out bySize)) sizes[size] = bySize = new Dictionary<string, Vector2>();
                Vector2 v;
                if (!bySize.TryGetValue(rich, out v))
                {
                    long t0 = probe != null ? probe.ElapsedTicks : 0L;
                    Scratch.fontSize = size;
                    bySize[rich] = v = Scratch.GetPreferredValues(rich);
                    if (probe != null) { measureTicks += probe.ElapsedTicks - t0; measures++; }
                }
                return v;
            }

            public float Measure(string rich, float size)
            {
                return Mathf.Ceil(Size(rich, size).x);
            }
        }

        /// 卡片上的一段文字，有回收的文字就复用；不换行的按构建时量好的尺寸设。
        private static TextMeshProUGUI Text(Ctx c, float size, string rich, Color color, float wrap)
        {
            if (probe == null) return Ui.NewText(c.Card, c.Src, size, rich, color, wrap, wrap > 0f ? Vector2.zero : c.Size(rich, size), SpareText());
            Vector2 measured = wrap > 0f ? Vector2.zero : c.Size(rich, size);
            TextMeshProUGUI spare = SpareText();
            long t0 = probe.ElapsedTicks;
            TextMeshProUGUI t = Ui.NewText(c.Card, c.Src, size, rich, color, wrap, measured, spare);
            long dt = probe.ElapsedTicks - t0;
            texts++;
            if (spare == null) { newTexts++; newTextTicks += dt; }
            if (wrap > 0f) { wrapped++; wrapTicks += dt; }
            textTicks += dt;
            return t;
        }

        // ---------------------------------------------------------------- 构建耗时（仅调试日志）

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

        // ---------------------------------------------------------------- 复用的文字

        /// 丢弃的估价单上的文字，失活放在根节点下给下一张用。矩形合成一个 mesh 之后，创建 TextMeshPro 文字（字体、材质、注册到 canvas）还占估价单大半开销，而丢一张单子会销毁同样多的文字。
        private static readonly Stack<TextMeshProUGUI> spareTexts = new Stack<TextMeshProUGUI>();
        private static RectTransform spareHolder;
        private const int SpareTexts = 300;

        private static TextMeshProUGUI SpareText()
        {
            while (spareTexts.Count > 0)
            {
                TextMeshProUGUI t = spareTexts.Pop();
                if (t != null) return t; // 否则它们跟着旧根节点一起没了
            }
            return null;
        }

        /// 销毁一张估价单，留下它的文字复用。
        private static void Drop(RectTransform slip)
        {
            if (slip == null) return;
            if (root != null && (spareHolder == null || spareHolder.parent != root))
            {
                spareHolder = Ui.NewRect(root, "spare texts");
                spareHolder.gameObject.SetActive(false);
            }
            Spare(slip);
            UnityEngine.Object.Destroy(slip.gameObject);
        }

        /// 把 rt 和它各部分上的文字挪到复用池。
        private static void Spare(Transform rt)
        {
            for (int i = rt.childCount - 1; i >= 0 && spareHolder != null && spareTexts.Count < SpareTexts; i--)
            {
                Transform child = rt.GetChild(i);
                if (child.name == PartName) { Spare(child); continue; }
                TextMeshProUGUI t = child.GetComponent<TextMeshProUGUI>();
                if (t == null) continue;
                t.transform.SetParent(spareHolder, false);
                spareTexts.Push(t);
            }
        }

        /// 每次构建都用来量的文字，放在根节点下复用，不是每张单子都新建。
        private static TextMeshProUGUI scratch;

        private static TextMeshProUGUI Scratch(TMP_Text tmp, float size)
        {
            scratch = Ui.NewText(root, tmp, size, "", Color.clear, 0f, Vector2.zero, scratch != null && scratch.transform.parent == root ? scratch : null);
            return scratch;
        }

        /// 富文本标签里用的数字（如“<pos=12.5>”）：不管玩家什么区域设置，小数点都用点。
        private static string Num(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private const string PartName = "part";

        /// 开始卡片上的一块，可以整体移动：接下来画的东西放进它，有自己的矩形 mesh，移动前坐标是卡片的。
        private static RectTransform Part(Ctx c)
        {
            RectTransform part = Ui.NewRect(card, PartName);
            Ui.Move(part, 0f, 0f);
            c.Card = part;
            c.Q = Ui.NewQuads(part);
            return part;
        }

        /// 卡片上的一个实心矩形（在它唯一的矩形 mesh 里），摆放方式同 Ui.Place。
        private static void Solid(Ctx c, Color color, float x, float yTop, float w, float h)
        {
            c.Q.Add(x, yTop, w, h, color);
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
        private static void Build(TMP_Text tmp, LayoutResult r, GameItemElement item)
        {
            try
            {
                BuildFlow(tmp, r, item);
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

        /// 建单子：比窗口高时按行分块重建一次，这样一节太长也能从中间断到下一栏。平常一节一块，物体少一些。
        private static void BuildFlow(TMP_Text tmp, LayoutResult r, GameItemElement item)
        {
            fineParts = false;
            wantFine = false;
            BuildCard(tmp, r, item);
            if (!wantFine) return;
            Drop(card);
            card = null;
            fineParts = true;
            try { BuildCard(tmp, r, item); }
            finally { fineParts = false; }
        }

        private static bool fineParts, wantFine;
        /// 这一张平时收起的部分（见 Expand），以及实际收起了哪些（写进底部的提示）。
        private static bool foldPrices, foldContents, foldDesc, foldMods;
        private static readonly List<string> folded = new List<string>();

        private static void Folded(string key)
        {
            string name = SectionedTooltipsMod.L(key);
            if (!folded.Contains(name)) folded.Add(name);
        }
        /// 上一张单子因为比窗口宽缩到的比例（不含字号抬高的那部分），调试导出用。
        private static float builtFit = 1f;

        private static void BuildCard(TMP_Text tmp, LayoutResult r, GameItemElement item)
        {
            // 上一张卡片留着，只是隐藏，鼠标回到它的物品时直接用。
            if (card != null) card.gameObject.SetActive(false);
            builtFor = r;
            builtItem = item;
            builtFull = buildFull;
            foldPrices = !buildFull && Expand.Folds(Settings.CollapsePrices);
            foldContents = !buildFull && Expand.Folds(Settings.CollapseContents);
            foldDesc = !buildFull && Expand.Folds(Settings.CollapseDescription);
            foldMods = !buildFull && Expand.Folds(Settings.CollapseModInfo);
            folded.Clear();
            lastBuildFrame = Time.frameCount;
            waitingItem = null;
            StartProbe();

            Ctx c = new Ctx();
            // Measure 会抵掉根节点的缩放（文字在屏幕上和游戏一样大），所以大小设置要在这里乘进去。
            // 「字体大小优化」开着时：它会把 16 号以下的字放大（我们排版时不知道，就会重叠）。我们自己先放大 1.2 倍，
            // 再把所有字号抬到 16 以上、整张按同样比例缩回去，看起来一样大，它也就不再动我们的字（见 ReadableFonts）。
            float boost = 1f, lift = 1f;
            if (ReadableFonts.Active)
            {
                boost = ReadableFonts.Boost;
                Panels.Metrics plain = Panels.Measure(tmp, root, Settings.ScaleFactor * boost);
                lift = Mathf.Max(1f, ReadableFonts.Floor / (plain.Font * MinFontShare));
            }
            c.M = Panels.Measure(tmp, root, Settings.ScaleFactor * boost * lift);
            c.Lift = lift;
            c.Src = tmp;
            c.Item = item;
            c.Card = card = Ui.NewRect(root, "ticket");
            c.U = Mathf.Max(1f, Mathf.Round(c.M.Line / 12f));
            // canvas scaler 的系数是每个 canvas 单位多少屏幕像素，屏幕空间各种模式都适用。
            Canvas canvas = root.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas = canvas.rootCanvas;
                float cs = Mathf.Abs(canvas.transform.lossyScale.y), rs = Mathf.Abs(root.lossyScale.y);
                if (cs > 0f && rs > 0f) c.Px = rs / cs * canvas.scaleFactor / lift;
            }
            c.Pad = Mathf.Round(c.M.Line * 0.6f);
            c.Gap = Mathf.Max(2f, Mathf.Round(c.M.Line * 0.25f));
            c.F = c.M.Font;
            c.PriceScale = Settings.PriceFactor;
            c.TagColors = TagColorsNow();
            c.TagWash = Settings.On(Settings.TagBackground);
            c.Scratch = Scratch(tmp, c.F);

            Parts p = Parts.From(r);
            for (int i = 0; i < p.Extras.Count; i++) p.Chips.Add(ChipsOf(item, p.Extras[i]));
            UsePaper(p.NotOwned != null);

            // 先画纸，其余都盖在上面；高度算出来以后再定尺寸。
            Image shadow = Ui.NewImage(card, Shadow);
            Image edge = Ui.NewImage(card, PaperEdge);
            Image paper = Ui.NewImage(card, Paper);
            // 然后是矩形，在所有文字和图标下面。
            c.Q = Ui.NewQuads(card);
            Step("setup");

            c.W = Width(c, p, r);
            Step("width");

            Quads body = c.Q;
            float y = c.Pad;
            y = Title(c, p, item, y);
            Step("title");
            // 印章放在大字价格旁边，那里是空的；下面的行从最低的印章下面开始。
            float stampY = y;
            float clear = y;
            for (int i = 0; i < p.Stamps.Count; i++) clear = Mathf.Max(clear, stampY + StampDrop(c, p.Stamps[i], i) + c.Gap);
            y = Price(c, p, y, clear);
            Step("price");
            // 每一节各占一块，单子太高时整块挪到右边新的一栏。开放式的小节（其他模组的信息、游戏的“其他”行）按顺序放在第一个这种块原来的位置，
            // 它们太多时自己单独起一栏。
            List<Piece> pieces = new List<Piece>();
            c.Pieces = pieces;
            c.Fine = fineParts;
            int sideRows = 0;
            bool asideDone = false;
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (!OnSlip(bl.Key)) continue;
                if (foldMods && bl.Key == Sections.Mods)
                {
                    Folded("fold_mods");
                    continue;
                }
                if (Aside(bl.Key))
                {
                    if (asideDone) continue;
                    asideDone = true;
                    for (int j = i; j < r.Blocks.Count; j++)
                    {
                        Block side = r.Blocks[j];
                        if (!OnSlip(side.Key) || !Aside(side.Key)) continue;
                        if (foldMods && side.Key == Sections.Mods) { Folded("fold_mods"); continue; }
                        sideRows += side.Rows.Count;
                        Begin(c, pieces, y, true);
                        y = Section(c, side, y);
                        End(c, y);
                    }
                    continue;
                }
                Begin(c, pieces, y, false);
                y = Section(c, bl, y);
                End(c, y);
            }
            Step("sections");
            ContentsInfo inside = r.Contents != null && r.Contents.Entries.Count > 0 ? r.Contents : null;
            if (inside != null && foldContents)
            {
                // 收起的是物品格子；电池、水箱这类量条是读数，留着。
                Folded("fold_contents");
                inside = GaugesOnly(inside);
            }
            if (inside != null)
            {
                Begin(c, pieces, y, false);
                y = Contents(c, inside, y);
                End(c, y);
            }
            Step("contents");
            Begin(c, pieces, y, false);
            if (foldDesc && p.Desc.Count > 0) Folded("fold_desc");
            else y = Description(c, p, item, y);
            if (!foldMods) y = ModSource(c, item, y);
            if (folded.Count > 0) y = FoldHint(c, y);
            End(c, y);
            Step("description");
            // 页脚也单独一块，分栏后仍留在第一栏底部。
            float footTop = y;
            RectTransform foot = Part(c);
            y = Settings.On(Settings.ShowFooter) ? Footer(c, item, y) : y + c.Pad;
            float footH = y - footTop;
            Step("footer");

            float w = c.W, h = Mathf.Round(y);
            c.Card = card;
            c.Q = body;
            // 按行数算，不是量出来的：网格的行距会让六个短行量出来超过八行。
            int lines = Settings.ModsColumnLines;
            // 窗口大小换成这张卡片的单位（字号抬高时卡片单位变小）。
            c.Room = slipRoom * lift;
            float roomW = slipRoomW * lift;
            bool asideColumn = sideRows > 0 && ((lines > 0 && sideRows > lines) || h > c.Room);
            // 比窗口高：按行分块重新建一次（BuildFlow），这张半成品不要了。
            if (h > c.Room && !c.Fine)
            {
                wantFine = true;
                return;
            }
            if (asideColumn || h > c.Room)
            {
                float head = pieces.Count > 0 ? pieces[0].Top : footTop;
                int cols = Flow(c, pieces, head, footH, asideColumn, out h);
                Ui.Move(foot, 0f, h - footH - footTop);
                w = cols * c.W;
                // 栏与栏之间打一排孔，和上下边缘一样。
                for (int k = 1; k < cols; k++)
                    for (float py = c.Pad; py + 3f * c.U <= h - c.Pad; py += 5f * c.U) Solid(c, PaperEdge, k * c.W, py, c.U, 3f * c.U);
            }
            Dashes(c, c.Pad * 0.5f, c.Pad * 0.5f, w - c.Pad, 3f * c.U, 2f * c.U, c.U, PaperEdge);
            Dashes(c, c.Pad * 0.5f, h - c.Pad * 0.5f, w - c.Pad, 3f * c.U, 2f * c.U, c.U, PaperEdge);

            Ui.Place(shadow, 2f * c.U, 2f * c.U, w, h);
            Ui.Place(edge, 0f, 0f, w, h);
            Ui.Place(paper, c.U, c.U, w - 2f * c.U, h - 2f * c.U);
            for (int i = 0; i < card.childCount; i++)
            {
                Transform t = card.GetChild(i);
                Quads q = t.name == PartName && t.childCount > 0 ? t.GetChild(0).GetComponent<Quads>() : t.GetComponent<Quads>();
                if (q != null) Ui.Place(q, 0f, 0f, w, h);
            }
            Ui.Place(card, 0f, 0f, w, h);
            // 栏太多、比窗口还宽：整张按比例缩小，宁可字小一点也不能有一截跑到屏幕外。
            float fit = w > roomW ? roomW / w : 1f;
            card.localScale = new Vector3(fit / lift, fit / lift, 1f);
            builtFit = fit;
            cardSize = new Vector2(w * fit / lift, h * fit / lift);

            // 印章最后画，盖在纸和价格行上面。
            for (int i = 0; i < p.Stamps.Count; i++) Stamp(c, p.Stamps[i], stampY, i);
            // 改不透明度会加 Settings.Version，所以存着的估价单一定是当前这个值。
            float opacity = Settings.OpacityFactor;
            if (opacity < 1f)
            {
                CanvasGroup g = card.gameObject.AddComponent<CanvasGroup>();
                g.alpha = opacity;
                g.interactable = false;
                g.blocksRaycasts = false;
            }
            bool log = Settings.Debug != null && Settings.Debug.Value;
#if DEVDUMP
            if (Stress.Label != null)
            {
                Stress.AfterBuild(card);
                log = true;
            }
#endif
            if (log) LogSlip(item, card, w, h);
            Keep(r, item);
            if (probe == null) return;
            Step("finish");
            SectionedTooltipsMod.Log(string.Format("slip built in {0:F1} ms ({1} objects, {2} rectangles), frame {3}: {4}",
                Ms(probe.ElapsedTicks), card.childCount, c.Q.Count, Time.frameCount, steps.ToString().TrimEnd(',', ' ')));
            SectionedTooltipsMod.Log(string.Format("  texts {0} in {1:F1} ms ({2} new {3:F1} ms, {4} wrapped {5:F1} ms); measured {6} in {7:F1} ms",
                texts, Ms(textTicks), newTexts, Ms(newTextTicks), wrapped, Ms(wrapTicks), measures, Ms(measureTicks)));
            probe = null;
        }

        /// 把刚建好的卡片放到已存估价单的最前；丢掉最旧的、旧设置下建的，以及同一物品更早的：排版缓存每个物品只留一份，物品文字一变，旧估价单就再也显示不出来了。
        /// <summary>
        /// 调试用（记录分组日志开着时）：把这张单子的文字块和坐标写成一行日志，
        /// 游戏外可以用 tool\view.py 拼回预览、跑排版检查。不写任何文件，不需要文件权限。
        /// </summary>
        private static void LogSlip(GameItemElement item, RectTransform card, float w, float h)
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
                if (slipRoom < float.MaxValue && slipRoomW < float.MaxValue)
                    SectionedTooltipsMod.Log(string.Format(CultureInfo.InvariantCulture, "[room] {0} {1:F0} {2:F0} {3:F4} {4:F2}",
                        Clean(SlipName(item)), slipRoom, slipRoomW, card.localScale.x, builtFit));
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

        /// 估价单视角下的各节：名称、名称下的小行、印章、价格、描述。
        private sealed class Parts
        {
            public Row Name;
            /// 名称下面的行（厂商、标签、备注），按游戏给的顺序。
            public readonly List<Row> Extras = new List<Row>();
            /// 对应 Extras 每一行：是“[机器]”那行标签时给出分类标签，否则为 null。
            public readonly List<List<Chip>> Chips = new List<List<Chip>>();
            public readonly List<string> Stamps = new List<string>();
            /// 游戏的赃物标记（“[赃物（14%热度）]”）；不是赃物时为 null。
            public string Stolen;
            /// 游戏的“未拥有”标记，去掉方括号；自己物品时为 null。
            public string NotOwned;
            /// 游戏的标签行去掉“[标签]”后的文字（“破旧 -75%”）：画在名字下面，不单独占一节。
            public readonly List<string> Features = new List<string>();
            public Row Price;
            /// 价格后面括号里的内容（“进价 250+722”）。
            public List<Seg> NoteSegs = new List<Seg>();
            /// 价格注解的富文本，用当前纸的墨色。
            public string PriceNote { get { return NoteRich(NoteSegs); } }
            public readonly List<Row> Prices = new List<Row>();
            public readonly List<Row> Desc = new List<Row>();

            public static Parts From(LayoutResult r)
            {
                Parts p = new Parts();
                for (int i = 0; i < r.Blocks.Count; i++)
                {
                    Block bl = r.Blocks[i];
                    for (int j = 0; j < bl.Rows.Count; j++)
                    {
                        Row row = bl.Rows[j];
                        if (bl.Key == Sections.Title)
                        {
                            string plain = PlainOf(row).Trim();
                            if (p.Name == null)
                            {
                                string flag;
                                p.Name = SplitNotOwned(row, out flag);
                                if (flag != null) p.NotOwned = flag.Trim('(', ')', '（', '）', '[', ']', '【', '】', ' ');
                            }
                            else if (row.Ord >= 0 && (row.Ord == Helpers.OrdStolen || row.Ord == Helpers.OrdContraband))
                            {
                                int end = FlagEnd(plain);
                                if (row.Ord == Helpers.OrdStolen) p.Stolen = plain.Substring(0, end);
                                else p.Stamps.Add(plain.Substring(0, end));
                                // 游戏写赃物标记用的是 Add 而不是 AddLine，所以后面跟的内容和它同一行：通常是物品标签，没有标签时是下一行。两种都按没有标记的样子显示。
                                if (plain.Substring(end).Trim().Length > 0)
                                {
                                    Row rest = DropFront(row, end);
                                    if (rest != null) p.Extras.Add(rest);
                                }
                            }
                            else p.Extras.Add(row);
                        }
                        else if (bl.Key == Sections.Value)
                        {
                            if (p.Price == null && row.IsPair && LeadingNumber(PlainOf(row.Value)).Length > 0)
                            {
                                p.Price = row;
                                List<Seg> note, tail;
                                SplitPrice(row.Value, out note, out tail);
                                // “预估价值：1478，基础价值：1478”：没有括号，逗号后面又是另一个值。
                                string notePlain = PlainOf(note).Trim().TrimStart('，', ',').Trim();
                                if (tail.Count == 0 && notePlain.IndexOfAny(new char[] { '（', '(' }) < 0 && PairFromPlain(notePlain) != null)
                                {
                                    tail = note;
                                    note = new List<Seg>();
                                }
                                p.NoteSegs = note;
                                // “售价：50（进价 50） 标价：50”：括号后面的标价单独占一行。
                                Row extra = PairFromPlain(PlainOf(tail).Trim().TrimStart('，', ','));
                                // 和售价一样的标价等于没说。
                                if (extra != null && !SamePrice(p, extra)) p.Prices.Add(extra);
                            }
                            // 或者在下一行（“售价：650（进价 585+65）” / “标价：650”）。
                            else if (!(p.Prices.Count == 0 && j > 0 && ReferenceEquals(bl.Rows[j - 1], p.Price) && SamePrice(p, row))) p.Prices.Add(row);
                        }
                        else if (bl.Key == Sections.Desc) p.Desc.Add(row);
                        else if (bl.Key == Sections.Label)
                        {
                            string f = FeatureOf(row);
                            if (f.Length > 0) p.Features.Add(f);
                        }
                    }
                }
                return p;
            }
        }

        /// 游戏把“未拥有”标记直接写在名称前面，中间没有换行。返回去掉标记的名称行（是副本：原行属于缓存的排版）和标记文本；是自己的物品就原样返回，标记为 null。
        private static Row SplitNotOwned(Row row, out string flag)
        {
            flag = null;
            if (row.Segs == null) return row;
            string mark;
            try { mark = TextUnits.Plain(LocHelper.GetLocalizedMechanic("mech_tooltip_not_owned") ?? "").Trim(); }
            catch { return row; }
            if (mark.Length == 0 || !PlainOf(row.Segs).TrimStart().StartsWith(mark, StringComparison.Ordinal)) return row;
            Row name = DropFront(row, mark.Length);
            if (name == null) return row;
            flag = mark;
            return name;
        }

        /// row 是不是只是把大字价格又说了一遍（“售价：1680”下面的“标价：1680”）。
        private static bool SamePrice(Parts p, Row row)
        {
            return p.Price != null && row.IsPair && PlainOf(row.Value).Trim() == LeadingNumber(PlainOf(p.Price.Value));
        }

        /// 去掉游戏“[标签]”前缀后的标签行：“破旧 -75%”。
        private static string FeatureOf(Row row)
        {
            string plain = (row.IsPair ? PlainOf(row.Label) + " " + PlainOf(row.Value) : PlainOf(row)).Trim();
            string mark;
            try { mark = TextUnits.Plain(LocHelper.GetLocalizedMechanic("mech_label_tooltip_prefix") ?? "").Trim(); }
            catch { mark = ""; }
            if (mark.Length > 0 && plain.StartsWith(mark, StringComparison.Ordinal)) plain = plain.Substring(mark.Length).Trim();
            return plain;
        }

        /// 一行里的多个标签，“破旧 -75%  ·  …”，价格打折的部分用红色。
        private static string FeaturesRich(Parts p)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < p.Features.Count; i++)
            {
                if (i > 0) sb.Append(Tint("  ·  ", InkSoft));
                string f = p.Features[i];
                int space = f.LastIndexOf(' ');
                string tail = space > 0 ? f.Substring(space + 1) : "";
                if (tail.Length > 1 && (tail[0] == '-' || tail[0] == '−') && tail.EndsWith("%"))
                    sb.Append(Tint(f.Substring(0, space), InkSoft)).Append(' ').Append(Tint(tail, StampRed));
                else sb.Append(Tint(f, InkSoft));
            }
            return sb.ToString();
        }

        /// row 的副本（原行属于缓存的排版），去掉开头 count 个字符（不计前导空格）；没剩下内容时返回 null。从前面一段段丢，跨在切点上的那段保留后半部分。
        private static Row DropFront(Row row, int count)
        {
            if (row.Segs == null) return null;
            int left = count;
            List<Seg> rest = new List<Seg>();
            for (int i = 0; i < row.Segs.Count; i++)
            {
                Seg s = row.Segs[i];
                if (left <= 0 || s.Bar != null) { rest.Add(s); continue; }
                string t = TextUnits.Plain(s.Text);
                if (rest.Count == 0 && left == count) t = t.TrimStart();
                if (t.Length <= left) { left -= t.Length; continue; }
                rest.Add(s.With(t.Substring(left).TrimStart()));
                left = 0;
            }
            if (PlainOf(rest).Trim().Length == 0) return null;
            Row r = new Row();
            r.Segs = rest;
            r.Ord = row.Ord;
            return r;
        }

        private static float Width(Ctx c, Parts p, LayoutResult r)
        {
            float need = 0f;
            float small = c.F * 0.75f;
            if (Settings.On(Settings.ShowFooter)) need = Mathf.Max(need, c.Measure(TicketNo(builtItem), c.F * 0.65f) + 24f * c.U + 3f * c.Gap);
            for (int i = 0; i < p.Prices.Count; i++) if (ShowPriceRow(p, p.Prices[i])) need = Mathf.Max(need, PairWidth(c, p.Prices[i]));
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (!OnSlip(bl.Key)) continue;
                need = Mathf.Max(need, c.Measure(Sections.TitleOf(bl.Key), c.F * 0.85f) + 4f * c.M.Line);
            }
            c.LabelCol = LabelColumn(c, r);
            need = Mathf.Max(need, GridWidth(c, r));
            float min = Mathf.Round(c.M.Line * 11f), max = Mathf.Round(c.M.Line * 17f);
            float w = Mathf.Clamp(need + 2f * c.Pad, min, max);
            float widest = Mathf.Round(c.M.Line * NameMaxLines);

            // 长名字会把单子撑宽，比正文允许的更宽，而不是折成两行。偏移和 Title 一样，多一个像素，免得取整把最后一个字挤出去。
            // 名字下面的标签、特性行最多撑到正文的上限，再长就折行（标签本来就会换行排），不跟着名字一起放宽。
            if (p.Name != null)
            {
                float lead = (IconFor(builtItem) != null ? IconBox(c) + c.Gap * 1.5f : 0f) + c.U + 2f * c.Pad;
                float extra = 0f;
                for (int i = 0; i < p.Extras.Count; i++)
                    extra = Mathf.Max(extra, p.Chips[i] != null ? ChipsWidth(c, p.Chips[i]) : c.Measure(InkRich(p.Extras[i].Segs, InkSoft, false), c.F * 0.8f));
                if (p.Features.Count > 0) extra = Mathf.Max(extra, c.Measure(FeaturesRich(p), c.F * 0.8f));
                if (p.Stolen != null) extra = Mathf.Max(extra, c.Measure(StolenLabel(p.Stolen), small) + 6f * c.U);
                w = Mathf.Max(w, Mathf.Min(lead + c.Measure(InkRich(p.Name.Segs, Ink), c.F * 1.15f), widest));
                w = Mathf.Max(w, Mathf.Min(lead + extra, max));
            }

            // 价格行不能换行：大字价格、它的注解（“进价 137+18”，装了 Item Details 更长）和旁边的印章；以前长注解会跑到印章下面。
            if (p.Price != null)
            {
                float line = c.Measure(InkRich(p.Price.Label, InkSoft, false), small) + c.Gap
                    + c.Measure(BigPrice(p), PriceSize(c)) + c.Gap + c.Measure(p.PriceNote, small);
                if (p.Stamps.Count > 0) line += 2f * c.Gap + StampSpan(c, p.Stamps[0]);
                w = Mathf.Max(w, Mathf.Min(line + 2f * c.Pad, widest));
            }
            return Mathf.Round(Mathf.Max(w, TextWidth(c, p, r, w)));
        }

        /// 换行后的文字最多能有多高，相对单子宽度的比例，超过就把单子加宽。
        private const float TextTallness = 1.2f;
        /// 长文本时单子最多加宽到多少行（约 30 个汉字一行）。
        private const float TextMaxLines = 22f;

        /// 长文本（说明书描述、很长的信息行）用的宽度：常规宽度下会把单子挤成一条窄条，所以把单子加宽，直到这段文字的高度不超过宽度的 TextTallness 倍，最多到 TextMaxLines。
        private static float TextWidth(Ctx c, Parts p, LayoutResult r, float w)
        {
            // 文字的面积：每段按一行排好，乘上行高。
            float area = 0f;
            float descSize = c.F * 0.8f, rowSize = c.F * 0.9f;
            for (int i = 0; i < p.Desc.Count; i++)
                area += c.Measure("<i>" + InkRich(Trimmed(p.Desc[i].Segs), InkSoft) + "</i>", descSize) * c.M.Line * 0.8f;
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (!OnSlip(bl.Key)) continue;
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    Row row = bl.Rows[j];
                    if (row.IsPair || row.Heading || row.Segs == null) continue;
                    area += c.Measure(InkRich(row.Segs, Ink), rowSize) * c.M.Line * 0.9f;
                }
            }
            float max = Mathf.Round(c.M.Line * TextMaxLines);
            float step = Mathf.Round(c.M.Line);
            while (w < max && area / Mathf.Max(1f, w - 2f * c.Pad) > TextTallness * w) w += step;
            return Mathf.Min(w, max);
        }

        /// 第一个印章横向占多宽（尺寸和 Stamp 一致）。
        private static float StampSpan(Ctx c, string text)
        {
            float sw, sh;
            StampSize(c, text, out sw, out sh);
            return sw;
        }

        /// 印章从它的行往下伸多深：Stamp 摆放时的下移量，加上它的高度。
        private static float StampDrop(Ctx c, string text, int index)
        {
            float sw, sh;
            StampSize(c, text, out sw, out sh);
            return c.Gap + index * 2f * c.U + sh;
        }

        /// 印章的外框尺寸，用和 Stamp 一样的字体和边框估算（宁可多一点，不会少）。
        private static void StampSize(Ctx c, string text, out float sw, out float sh)
        {
            string main, sub;
            SplitFlag(text, out main, out sub);
            float w = c.Measure("<b>" + main + "</b>", c.F * StampMain);
            if (sub.Length > 0) w = Mathf.Max(w, c.Measure(sub, c.F * StampSub));
            sw = w + 2f * StampEdge * c.U;
            sh = c.M.Line * (sub.Length > 0 ? 1.6f : 1f) + 2f * StampEdge * c.U;
        }

        /// 名字最多把单子加宽到多少行；超过就还是折行。
        private const float NameMaxLines = 26f;

        private static float PairWidth(Ctx c, Row row)
        {
            float bar = BarOf(row.Value) != null ? BarWidth(c) + c.Gap : 0f;
            return c.Measure(InkRich(row.Label, InkSoft), c.F * 0.9f) + bar + c.Measure(InkRich(row.Value, Ink), c.F * 0.9f) + 3f * c.Gap;
        }

        // ---------------------------------------------------------------- 部件

        private static float IconBox(Ctx c)
        {
            return Mathf.Round(c.M.Line * 1.9f);
        }

        /// 图标在方框里的大小：屏幕上是它像素的整数倍，放不下时用二分之一、四分之一。像素画被别的比例缩小（32 像素的图标塞进 25）会让某些行重复、某些行丢掉。
        private static void PixelFit(Ctx c, Sprite sprite, float w, float h, out float outW, out float outH)
        {
            outW = outH = 0f;
            float sw = sprite.rect.width, sh = sprite.rect.height;
            if (sw <= 0f || sh <= 0f || w <= 0f || h <= 0f) return;
            // 图标每个像素占多少屏幕像素。
            float s = Mathf.Min(w * c.Px / sw, h * c.Px / sh);
            s = s >= 1f ? Mathf.Floor(s) : Mathf.Pow(2f, Mathf.Floor(Mathf.Log(s, 2f)));
            outW = sw * s / c.Px;
            outH = sh * s / c.Px;
        }

        /// 图标居中放在 box 见方、四边内缩 pad 的框里，对齐整屏幕像素。透明边裁掉后撑满框（见 Icons），裁不了时按整数倍画原图。
        private static void PlaceIcon(Ctx c, Sprite icon, float x, float y, float box, float pad)
        {
            float iw, ih;
            Sprite sprite = Icons.Fit(icon, box - 2f * pad, box - 2f * pad, c.Px, out iw, out ih);
            if (sprite == null)
            {
                sprite = icon;
                PixelFit(c, icon, box - 2f * pad, box - 2f * pad, out iw, out ih);
            }
            if (iw <= 0f) return;
            Image img = Ui.NewImage(c.Card, Color.white);
            img.sprite = sprite;
            Ui.Place(img, Snap(c, x + (box - iw) / 2f), Snap(c, y + (box - ih) / 2f), iw, ih);
        }

        private static float Snap(Ctx c, float v)
        {
            return Mathf.Round(v * c.Px) / c.Px;
        }

        private static readonly List<Sprite> iconBatch = new List<Sprite>();

        /// 网格里前 shown 格的图标一起读出来（见 Icons.Prepare）。
        internal static void PrepareIcons(List<ContentEntry> entries, int shown)
        {
            iconBatch.Clear();
            for (int i = 0; i < shown && i < entries.Count; i++) iconBatch.Add(Overlay.IconOf(entries[i].Item));
            Icons.Prepare(iconBatch);
            iconBatch.Clear();
        }

        /// 标题用的物品图标；没有图标或设置关掉时为 null。
        private static Sprite IconFor(GameItemElement item)
        {
            return Settings.On(Settings.Icon) ? Overlay.IconOf(item) : null;
        }

        private static float Title(Ctx c, Parts p, GameItemElement item, float y)
        {
            if (p.Name == null) return y;
            float boxSize = IconBox(c);
            float x = c.Pad;
            Sprite icon = IconFor(item);
            if (icon != null)
            {
                Solid(c, PaperShade, x, y, boxSize, boxSize);
                PlaceIcon(c, icon, x, y, boxSize, 2f * c.U);
                x += boxSize + c.Gap * 1.5f;
            }

            float wrap = c.W - c.Pad - x;
            float ty = y;
            TextMeshProUGUI name = Text(c, c.F * 1.15f, InkRich(p.Name.Segs, Ink), Color.white, wrap);
            Ui.Move(name.rectTransform, x, ty);
            ty += name.rectTransform.sizeDelta.y;
            if (p.NotOwned != null)
            {
                // 粉色纸已经说明了，这行只是平静地写出名字，用纸本身的墨。
                TextMeshProUGUI t = Text(c, c.F * 0.8f, "<cspace=0.2em>" + p.NotOwned + "</cspace>", InkSoft, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            for (int i = 0; i < p.Extras.Count; i++)
            {
                if (p.Chips[i] != null) { ty = DrawChips(c, p.Chips[i], x, ty, wrap); continue; }
                TextMeshProUGUI t = Text(c, c.F * 0.8f, InkRich(p.Extras[i].Segs, InkSoft, false), Color.white, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            if (p.Features.Count > 0)
            {
                TextMeshProUGUI t = Text(c, c.F * 0.8f, FeaturesRich(p), Color.white, wrap);
                Ui.Move(t.rectTransform, x, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            // 最后画，这样赃物和非赃物看起来完全一样，只多一个标签。
            if (p.Stolen != null) ty = StolenTag(c, p.Stolen, x, ty);
            return Mathf.Max(y + (icon != null ? boxSize : 0f), ty) + c.Gap * 1.5f;
        }

        /// 售价写大，价值一节其余的行画成带点线引导的小行。
        private static float Price(Ctx c, Parts p, float y, float clear)
        {
            float small = c.F * 0.75f;
            Row perCell = PerCellRow(p);
            if (p.Price != null)
            {
                // “售价”用小字放在数字前面、和注解一样贴着数字底部，不单独占一行。
                TextMeshProUGUI label = Text(c, small, InkRich(p.Price.Label, InkSoft, false), Color.white, 0f);
                Vector2 ls = label.rectTransform.sizeDelta;
                float bx = c.Pad + ls.x + c.Gap;
                TextMeshProUGUI big = Text(c, PriceSize(c), BigPrice(p), Ink, 0f);
                Ui.Move(big.rectTransform, bx, y);
                Vector2 bs = big.rectTransform.sizeDelta;
                Ui.Move(label.rectTransform, c.Pad, y + bs.y - ls.y - c.U);
                // 大字价格右边：进价，下面是性价比（常驻，不收起：它是估价单最要紧的数，收起再展开时下面的内容也不会跳）。两行一起贴着数字底部。
                // 性价比不撑宽单子：右边放不下（印章也在这一行）就在价格下面单独一行。
                string note = p.PriceNote;
                float bottom = y + bs.y - c.U;
                float room = c.W - c.Pad - (bx + bs.x + c.Gap);
                if (p.Stamps.Count > 0) room -= 2f * c.Gap + StampSpan(c, p.Stamps[0]);
                if (perCell != null && c.Measure(PerCellRich(perCell), small) <= room)
                {
                    TextMeshProUGUI pc = Text(c, small, PerCellRich(perCell), Color.white, 0f);
                    bottom -= pc.rectTransform.sizeDelta.y;
                    Ui.Move(pc.rectTransform, bx + bs.x + c.Gap, bottom);
                    perCell = null;
                }
                if (note.Length > 0)
                {
                    TextMeshProUGUI n = Text(c, small, note, Color.white, 0f);
                    Ui.Move(n.rectTransform, bx + bs.x + c.Gap, bottom - n.rectTransform.sizeDelta.y);
                }
                y += bs.y + c.Gap;
            }
            y = Mathf.Max(y, clear);
            if (perCell != null)
            {
                // 价格右边放不下的性价比：单独一行，照样常驻。
                List<string> own = new List<string>();
                own.Add(PerCellRich(perCell));
                y = PriceLine(c, own, y);
            }
            perCell = PerCellRow(p);
            // 大字价格下面的其他价格是明细：合成一行小字，“标价 100 · 性价比 11.1（100/9格）”。这一节里别的东西（比如“已上锁”这种标记）还是各占一行，按顺序。
            List<string> line = new List<string>();
            for (int i = 0; i < p.Prices.Count; i++)
            {
                Row row = p.Prices[i];
                if (!ShowPriceRow(p, row) || ReferenceEquals(row, perCell)) continue;
                if (row.IsPair && p.Price != null && BarOf(row.Value) == null)
                {
                    if (foldPrices)
                    {
                        Folded("fold_prices");
                        continue;
                    }
                    line.Add(InkRich(row.Label, InkSoft, false) + " " + InkRich(row.Value, Ink, false));
                    continue;
                }
                y = PriceLine(c, line, y);
                y = RowLine(c, row, y, c.Pad);
            }
            return PriceLine(c, line, y);
        }

        /// 价格明细里的“性价比 89.6（806/9格）”：画在进价下面。没有大字价格、或关了价格明细时为 null。
        private static Row PerCellRow(Parts p)
        {
            if (p.Price == null) return null;
            for (int i = 0; i < p.Prices.Count; i++)
            {
                Row row = p.Prices[i];
                if (ShowPriceRow(p, row) && row.IsPair && BarOf(row.Value) == null && TextRules.PerCellValue(PlainOf(row.Value))) return row;
            }
            return null;
        }

        private static string PerCellRich(Row row)
        {
            return InkRich(row.Label, InkSoft, false) + " " + InkRich(row.Value, Ink, false);
        }

        /// 把攒起来的价格明细写出去并清空。返回底部位置。
        private static float PriceLine(Ctx c, List<string> line, float y)
        {
            if (line.Count == 0) return y;
            float size = c.F * 0.8f;
            string dot = "<color=#" + ColorUtility.ToHtmlStringRGB(InkSoft) + ">  ·  </color>";
            // 一行放不下时在“·”处换行，每一项（“性价比 1051.6（16826/16格）”）不拆开；一项本身就比单子宽时才在项里折行。
            for (int i = 0; i < line.Count; )
            {
                string rich = line[i];
                int j = i + 1;
                for (; j < line.Count; j++)
                {
                    string more = rich + dot + line[j];
                    if (c.Measure(more, size) > c.Inner) break;
                    rich = more;
                }
                TextMeshProUGUI t = Text(c, size, rich, Color.white, c.Measure(rich, size) > c.Inner ? c.Inner : 0f);
                Ui.Move(t.rectTransform, c.Pad, y);
                y += t.rectTransform.sizeDelta.y + c.U;
                i = j;
            }
            line.Clear();
            return y;
        }

        /// 关掉价格明细时只留大字价格：其他的“标签 …… 数值”行不要，但同一节里的标记（“已上锁”）留着；没有大字价格可留时全都留。
        private static bool ShowPriceRow(Parts p, Row row)
        {
            return Settings.On(Settings.ShowPriceDetails) || p.Price == null || !row.IsPair;
        }

        /// 价格写大并加粗；多大由 PriceSize 设置决定。
        private static float PriceSize(Ctx c)
        {
            return c.F * c.PriceScale;
        }

        private static string BigPrice(Parts p)
        {
            return "<b>" + LeadingNumber(PlainOf(p.Price.Value)) + "</b>";
        }

        /// 这一节要不要画成估价单的一节。名称、价格、描述各有位置；装载那一节只有游戏的“双击打开”。
        private static bool OnSlip(string key)
        {
            return key != Sections.Title && key != Sections.Value && key != Sections.Desc && key != Sections.Container && key != Sections.Label;
        }

        /// 估价单上的一块：一节、内容物或描述，画在自己的子节点里，分栏时整块移动。Top/Bottom 是画的时候的位置。
        private sealed class Piece
        {
            public RectTransform Part;
            public float Top, Bottom;
            public bool Aside;
        }

        /// 只留量条（电池、液体容器）的内容物；没有量条时只剩标题“内容物 20”，装着多少东西还看得到。
        private static ContentsInfo GaugesOnly(ContentsInfo all)
        {
            ContentsInfo r = new ContentsInfo();
            r.Total = all.Total;
            for (int g = 0; g < all.Groups.Count; g++)
            {
                ContentGroup src = all.Groups[g];
                if (src.Gauges.Count == 0) continue;
                ContentGroup dst = new ContentGroup();
                dst.Label = src.Label;
                dst.Gauges.AddRange(src.Gauges);
                dst.Total = src.Total;
                r.Groups.Add(dst);
                r.Gauges.AddRange(src.Gauges);
            }
            return r;
        }

        /// 收起时单子底部的一行小字：键帽，后面是“查看价格明细、描述”。返回底部。
        private static float FoldHint(Ctx c, float y)
        {
            // 一道虚线把提示和上面的内容隔开，它和下面的条形码、单号是一组，不是一条数据。
            y += c.Gap;
            Dashes(c, c.Pad, y, c.Inner, 3f * c.U, 2f * c.U, c.U, PaperEdge);
            y += c.U + c.Gap;
            float size = c.F * 0.7f, px = 2f * c.U;
            TextMeshProUGUI key = Text(c, size, Tint(Expand.Cap, Ink), Color.white, 0f);
            Vector2 ks = key.rectTransform.sizeDelta;
            float kw = Mathf.Round(ks.x + 2f * px), kh = Mathf.Round(ks.y);
            Ui.Move(key.rectTransform, c.Pad + px, y);
            // 实心键帽：比纸深一档的底，底边再深一档，像一颗键。
            Solid(c, Track(), c.Pad, y, kw, kh);
            Solid(c, Color.Lerp(Paper, InkSoft, darkPaper ? 0.6f : 0.5f), c.Pad, y + kh, kw, 2f * c.U);
            kh += 2f * c.U;
            // 分隔符是英文逗号时补一个空格（csv 里的值会被去掉首尾空白）。
            string sep = SectionedTooltipsMod.L("fold_sep");
            if (sep.Length == 1 && sep[0] < 128) sep += " ";
            string text = string.Format(SectionedTooltipsMod.L("fold_view"), string.Join(sep, folded.ToArray()));
            float x = c.Pad + kw + c.Gap;
            TextMeshProUGUI t = Text(c, size, Tint(text, InkSoft), Color.white, c.W - c.Pad - x);
            Ui.Move(t.rectTransform, x, y);
            return y + Mathf.Max(kh, t.rectTransform.sizeDelta.y);
        }

        /// 当前这块画到 y 为止。
        private static void End(Ctx c, float y)
        {
            if (c.Pieces.Count > 0) c.Pieces[c.Pieces.Count - 1].Bottom = y;
        }

        /// 按行分块时，在这一行前面断开：前一块到此为止，后面画进新的一块。
        private static void Split(Ctx c, float y)
        {
            if (!c.Fine || c.Pieces == null || c.Pieces.Count == 0) return;
            Piece cur = c.Pieces[c.Pieces.Count - 1];
            if (y <= cur.Top) return;
            cur.Bottom = y;
            Begin(c, c.Pieces, y, cur.Aside);
        }

        private static Piece Begin(Ctx c, List<Piece> pieces, float y, bool aside)
        {
            Piece p = new Piece();
            p.Part = Part(c);
            p.Top = p.Bottom = y;
            p.Aside = aside;
            pieces.Add(p);
            return p;
        }

        /// <summary>
        /// 把各块排进几栏，每栏不超过窗口高度：第一栏接着价格往下排，后面的栏从顶上开始（第一个标签和名字顶部齐平），放不下就开新的一栏。
        /// asideColumn 时开放式的小节另起一栏。返回栏数，h 是分栏后的单子高度（页脚在第一栏底部）。
        /// </summary>
        private static int Flow(Ctx c, List<Piece> pieces, float head, float footH, bool asideColumn, out float h)
        {
            float top = c.Pad - c.Gap * 1.5f;
            int col = 0;
            float colY = head, first = head, rest = 0f;
            bool empty = true, inAside = false;
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                float ph = p.Bottom - p.Top;
                if (ph <= 0f) { Ui.Move(p.Part, col * c.W, colY - p.Top); continue; }
                bool start = asideColumn && p.Aside && !inAside;
                if (p.Aside) inAside = true;
                float room = col == 0 ? c.Room - footH : c.Room - c.Pad;
                // 一栏里至少放一块，单独一块比窗口还高也只能让它超出去。
                if (start || (!empty && colY + ph > room))
                {
                    col++;
                    colY = top;
                }
                Ui.Move(p.Part, col * c.W, colY - p.Top);
                colY += ph;
                empty = false;
                if (col == 0) first = colY; else rest = Mathf.Max(rest, colY);
            }
            h = Mathf.Round(Mathf.Max(first + footH, rest + c.Pad));
            return col + 1;
        }

        /// 可能很长、长到要单独占一栏的小节：其他模组的信息和“其他”。
        private static bool Aside(string key)
        {
            return key == Sections.Mods || key == Sections.Info;
        }


        /// 模组词条的名称或描述：游戏把它们写成浅蓝，属性写成浅青。
        private static bool Affix(Row row)
        {
            Seg s = row.IsPair || row.Heading ? null : LeadSeg(row);
            return s != null && s.Color == Palette.LightBlue;
        }

        /// 这一行第一个有字的 seg。
        private static Seg LeadSeg(Row row)
        {
            if (row.Segs == null) return null;
            for (int i = 0; i < row.Segs.Count; i++)
                if (row.Segs[i].Bar == null && TextUnits.Plain(row.Segs[i].Text).Trim().Length > 0) return row.Segs[i];
            return null;
        }

        private static float Indent(Ctx c)
        {
            return Mathf.Round(c.M.Line * 0.45f);
        }

        /// 小节标题用它自己的墨色，下面一条细线横过单子。（以前标题后面垫一块实色，比下面的内容还抢眼。）
        /// 分节标题：全都用正文的墨色，下面一道纸边色的细线。颜色只留给数据本身（盈亏、成分、市场事件）和印章。
        private static float SectionHeader(Ctx c, string title, float y)
        {
            y += c.Gap * 1.5f;
            TextMeshProUGUI t = Text(c, c.F * 0.8f, "<b>" + title + "</b>", Ink, 0f);
            float th = t.rectTransform.sizeDelta.y;
            Ui.Move(t.rectTransform, c.Pad, y);
            Solid(c, PaperEdge, c.Pad, y + th + c.U, c.Inner, c.U);
            return y + th + 2f * c.U + c.Gap * 0.75f;
        }

        /// 一行：“标签 ........ 数值”、小标题，或者折行的文字，有进度条就画上。
        private static float RowLine(Ctx c, Row row, float y, float left)
        {
            float size = c.F * 0.9f;
            float inner = c.W - c.Pad - left;
            if (row.Heading)
            {
                TextMeshProUGUI h = Text(c, c.F * 0.8f, InkRich(row.Label, InkSoft, false), Color.white, inner);
                Ui.Move(h.rectTransform, left, y);
                return y + h.rectTransform.sizeDelta.y;
            }
            if (!row.IsPair)
            {
                float x = left;
                BarMark bar = BarOf(row.Segs);
                // 进度条配一个短读数（“416 ml / 750 ml”、“10/28”）：进度条铺满整行，读数放在右端，和内容物的量条一样。
                string reading = bar != null ? InkRich(row.Segs, Ink) : null;
                if (bar != null && c.Measure(reading, size) <= inner * 0.5f)
                {
                    float lh = Mathf.Ceil(c.Size(" ", size).y);
                    TextMeshProUGUI r = Text(c, size, reading, Color.white, 0f);
                    Vector2 rs = r.rectTransform.sizeDelta;
                    Ui.Move(r.rectTransform, c.W - c.Pad - rs.x, y);
                    DrawBar(c, bar, left, y, c.W - c.Pad - rs.x - c.Gap - left, lh);
                    return y + Mathf.Max(lh, rs.y);
                }
                if (bar != null)
                {
                    float lh = Mathf.Ceil(c.Size(" ", size).y); // 这一行的行高
                    DrawBar(c, bar, x, y, BarWidth(c), lh);
                    x += BarWidth(c) + c.Gap;
                }
                TextMeshProUGUI t = Text(c, size, InkRich(row.Segs, Ink), Color.white, c.W - c.Pad - x);
                Ui.Move(t.rectTransform, x, y);
                return y + t.rectTransform.sizeDelta.y;
            }

            // 标签用棕色的墨，只有小节标题带颜色；数值保留自己的颜色。比单子还宽的标签或数值（标签上的“受日用品救济行动影响，……(-40%)”）折行而不是压出边；这时数值接在标签下面一行。
            BarMark vbar = BarOf(row.Value);
            float barW = vbar != null ? BarWidth(c) + c.Gap : 0f;
            string labelRich = InkRich(row.Label, InkSoft, false), valueRich = InkRich(row.Value, Ink);
            TextMeshProUGUI label = Text(c, size, labelRich, Color.white, c.Measure(labelRich, size) > inner ? inner : 0f);
            TextMeshProUGUI value = Text(c, size, valueRich, Color.white, c.Measure(valueRich, size) > inner - barW ? inner - barW : 0f);
            Vector2 ls = label.rectTransform.sizeDelta, vs = value.rectTransform.sizeDelta;
            float lineH = Mathf.Max(ls.y, vs.y);

            Ui.Move(label.rectTransform, left, y);
            float vy = y;
            if (ls.x + 3f * c.Gap + barW + vs.x > inner)
            {
                vy = y + ls.y; // 没地方了：数值右对齐放到下一行
                lineH = ls.y + vs.y;
            }
            float vx = Mathf.Max(left + barW, c.W - c.Pad - vs.x);
            Ui.Move(value.rectTransform, vx, vy);
            if (vbar != null) DrawBar(c, vbar, vx - barW, vy, BarWidth(c), vs.y);

            if (vy == y)
            {
                float from = left + ls.x + c.Gap, to = vx - barW - c.Gap;
                Leader(c, from, to, y + Mathf.Round((ls.y - c.U) / 2f));
            }
            return y + lineH;
        }

        /// 标签中线上的点线引导，右对齐，点正好停在数值前面。
        private static void Leader(Ctx c, float from, float to, float midY)
        {
            float step = 3f * c.U;
            int n = Mathf.FloorToInt((to - from + 2f * c.U) / step);
            if (n < 2) return;
            float x = to - n * step + 2f * c.U;
            for (int i = 0; i < n; i++) Solid(c, PaperEdge, x + i * step, midY, c.U, c.U);
        }

        private static float BarWidth(Ctx c)
        {
            return Mathf.Round(c.M.Line * 3f);
        }

        /// 游戏画的进度条，用它的颜色转成墨。
        private static void DrawBar(Ctx c, BarMark bar, float x, float yTop, float w, float lineH)
        {
            Meter(c, bar.Ratio, BarInk(bar), x, yTop, w, lineH);
        }


        /// 一格物品格，最多 Settings.MaxContentRows 行，多的折成“+n”一格。返回底部位置。
        private static float Grid(Ctx c, List<ContentEntry> entries, float left, float y)
        {
            float cell = Mathf.Round(c.M.Line * 1.5f), gap = c.U * 2f;
            int cols = Math.Max(1, Mathf.FloorToInt((c.W - c.Pad - left + gap) / (cell + gap)));
            int capacity = cols * Settings.MaxContentRows;
            int shown = entries.Count <= capacity ? entries.Count : capacity - 1;
            int more = 0;
            for (int i = shown; i < entries.Count; i++) more += entries[i].Count;
            int cells = shown + (more > 0 ? 1 : 0);
            float small = c.F * 0.65f;
            // 堆叠数在格子右下角，每个网格合成一段文字（再加一段纸色的阴影），而不是每格两段：满格时那要 28 段文字。每一行格子是一行文字，间距一格，每个数字用 <pos> 摆位置。
            float lineH = Mathf.Ceil(c.Size("0", small).y);
            float countsLeft = Mathf.Round(left), countsTop = Mathf.Round(y + cell - lineH);
            StringBuilder counts = new StringBuilder();
            counts.Append("<line-height=").Append(Num(cell + gap)).Append('>');
            int countRow = 0;
            bool anyCount = false;

            // 只有一件物品就只画图标：只围一个框会看着像按钮。
            bool bare = cells == 1;
            PrepareIcons(entries, shown);
            for (int i = 0; i < cells; i++)
            {
                float x = left + (i % cols) * (cell + gap);
                float cy = y + (i / cols) * (cell + gap);
                if (!bare)
                {
                    Solid(c, PaperEdge, x, cy, cell, cell);
                    Solid(c, PaperShade, x + c.U, cy + c.U, cell - 2f * c.U, cell - 2f * c.U);
                }
                if (i >= shown)
                {
                    TextMeshProUGUI m = Text(c, small, "+" + more, Ink, 0f);
                    Vector2 s = m.rectTransform.sizeDelta;
                    Ui.Move(m.rectTransform, x + Mathf.Round((cell - s.x) / 2f), cy + Mathf.Round((cell - s.y) / 2f));
                    continue;
                }
                Sprite icon = Overlay.IconOf(entries[i].Item);
                if (icon != null) PlaceIcon(c, icon, x, cy, cell, bare ? 0f : 2f * c.U);
                if (entries[i].Count > 1)
                {
                    string n = entries[i].Count.ToString(CultureInfo.InvariantCulture);
                    float bx = Mathf.Round(x + cell - Mathf.Ceil(c.Size(n, small).x) - c.U);
                    for (; countRow < i / cols; countRow++) counts.Append('\n');
                    counts.Append("<pos=").Append(Num(bx - countsLeft)).Append('>').Append(n);
                    anyCount = true;
                }
            }
            if (anyCount)
            {
                // 先画阴影，数字盖在上面；两段都在图标之上，图标这时都摆好了。
                string rich = counts.ToString();
                TextMeshProUGUI sh = Text(c, small, rich, Paper, 0f);
                TextMeshProUGUI tx = Text(c, small, rich, Ink, 0f);
                Ui.Move(sh.rectTransform, countsLeft + c.U, countsTop + c.U);
                Ui.Move(tx.rectTransform, countsLeft, countsTop);
            }
            int rows = (cells + cols - 1) / cols;
            return y + rows * cell + (rows - 1) * gap;
        }

        /// 物品描述。Item Details 会按游戏窗口的宽度断行（“……金属锭。金”/“属锭的质量……”）；估价单宽度不同，那些断点会留下参差的边，所以原文里连着的行重新接起来，按单子宽度重排。真正的换行保留。
        private static float Description(Ctx c, Parts p, GameItemElement item, float y)
        {
            if (p.Desc.Count == 0) return y;
            string source = DescriptionText(item);
            y += c.Gap * 2f;
            for (int i = 0; i < p.Desc.Count; i++)
            {
                StringBuilder sb = new StringBuilder(InkRich(Trimmed(p.Desc[i].Segs), InkSoft));
                string last = PlainOf(p.Desc[i]).Trim();
                while (i + 1 < p.Desc.Count)
                {
                    string next = PlainOf(p.Desc[i + 1]).Trim();
                    string joint = Joint(source, last, next);
                    if (joint == null) break;
                    sb.Append(joint).Append(InkRich(Trimmed(p.Desc[i + 1].Segs), InkSoft));
                    last = next;
                    i++;
                }
                TextMeshProUGUI t = Text(c, c.F * 0.8f, "<i>" + sb + "</i>", Color.white, c.Inner);
                Ui.Move(t.rectTransform, c.Pad, y);
                y += t.rectTransform.sizeDelta.y;
            }
            return y;
        }

        private static string DescriptionText(GameItemElement item)
        {
            try { return item == null ? "" : TextUnits.Plain((item.shortDescription ?? "") + "\n" + (item.flavorText ?? "")); }
            catch { return ""; }
        }

        /// source 里本来连着两行的两个片段之间用什么接：空的（中文）、一个空格，或者接不上时 null。
        private static string Joint(string source, string a, string b)
        {
            if (source.Length == 0 || a.Length == 0 || b.Length == 0) return null;
            if (source.IndexOf(a + b, StringComparison.Ordinal) >= 0) return "";
            if (source.IndexOf(a + " " + b, StringComparison.Ordinal) >= 0) return " ";
            // Item Details 只在断点处去掉 ASCII 空格，所以那里可能留着全角空格。
            if (source.IndexOf(a + "　" + b, StringComparison.Ordinal) >= 0) return "　";
            return null;
        }

        /// 去掉行首行尾空格的 segs（Item Details 会给自己的行加缩进）。
        private static List<Seg> Trimmed(List<Seg> segs)
        {
            if (segs == null || segs.Count == 0) return segs;
            List<Seg> r = new List<Seg>(segs);
            for (int i = 0; i < r.Count; i++)
            {
                if (r[i].Bar != null || string.IsNullOrEmpty(r[i].Text)) continue;
                bool visible;
                r[i] = r[i].With(TrimLead(r[i].Text, out visible));
                if (visible) break;
            }
            for (int i = r.Count - 1; i >= 0; i--)
            {
                if (r[i].Bar != null || string.IsNullOrEmpty(r[i].Text)) continue;
                r[i] = r[i].With(r[i].Text.TrimEnd());
                if (TextUnits.Plain(r[i].Text).Length > 0) break;
            }
            return r;
        }

        /// 去掉前导空格，包括前导标签后面的空格（“<color=…>  此机器”）；同时告诉后面还有没有文字。
        private static string TrimLead(string s, out bool visible)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                int close = ch == '<' ? s.IndexOf('>', i) : -1;
                if (close > i)
                {
                    sb.Append(s, i, close - i + 1);
                    i = close + 1;
                }
                else if (char.IsWhiteSpace(ch)) i++;
                else break;
            }
            visible = i < s.Length;
            sb.Append(s, i, s.Length - i);
            return sb.ToString();
        }

        /// 物品来自哪个模组，游戏自己的选项要求时才显示。游戏是在估价单取材的那段文字之后加这一行的，不补就漏掉了。
        private static float ModSource(Ctx c, GameItemElement item, float y)
        {
            if (!Settings.On(Settings.ShowModInfo)) return y;
            string name = null;
            try { if (EmporiumOption.ShowModSourceInTooltip) name = ModSourceHelper.GetItemModName(item); }
            catch { }
            name = string.IsNullOrEmpty(name) ? "" : TextUnits.Plain(name).Trim();
            if (name.Length == 0) return y;
            y += c.Gap;
            TextMeshProUGUI t = Text(c, c.F * 0.75f, name, InkSoft, c.Inner);
            Ui.Move(t.rectTransform, c.Pad, y);
            return y + t.rectTransform.sizeDelta.y;
        }

        /// 用物品身份生成的条形码，加上单号。
        private static float Footer(Ctx c, GameItemElement item, float y)
        {
            y += c.Gap * 1.5f;
            float h = Mathf.Round(c.M.Line * 0.55f);
            TextMeshProUGUI no = Text(c, c.F * 0.65f, TicketNo(item), InkSoft, 0f);
            // 条形码是装饰，不该是整张单子最黑的一块。
            Color bars = Color.Lerp(Paper, Ink, 0.7f);
            Vector2 s = no.rectTransform.sizeDelta;
            float x = c.Pad, end = c.Pad + Mathf.Min(c.Inner * 0.55f, 48f * c.U, c.Inner - s.x - 2f * c.Gap);
            int seed = 0;
            try { seed = (item.identifier ?? "").GetHashCode() ^ item.uniqueId; } catch { }
            System.Random rng = new System.Random(seed);
            while (x < end)
            {
                float w = rng.Next(1, 3) * c.U;
                Solid(c, bars, x, y, w, h);
                x += w + rng.Next(1, 3) * c.U;
            }
            Ui.Move(no.rectTransform, c.W - c.Pad - s.x, y + h - s.y);
            return y + h + c.Pad;
        }

        /// 把“[赃物（14%热度）]”这样的标记拆成“赃物”和“14%热度”（没有括号时后半为空）。游戏可能把物品标签写在同一行（“[赃物（14%热度）] [麻醉品] [医疗品]”）；标记在自己的右括号处结束，后面的标签丢掉。
        private static void SplitFlag(string text, out string main, out string sub)
        {
            main = text.Trim();
            main = main.Substring(0, FlagEnd(main)).Trim().Trim('[', ']', '【', '】').Trim();
            sub = "";
            int cut = main.IndexOfAny(new char[] { '（', '(' });
            if (cut > 0)
            {
                sub = main.Substring(cut + 1).TrimEnd('）', ')').Trim();
                main = main.Substring(0, cut).Trim();
            }
        }

        /// 行首的标记到哪结束：到它自己的右括号；没有自己括号的标记，到第一个方括号标签之前。
        private static int FlagEnd(string plain)
        {
            if (plain.Length > 0 && (plain[0] == '[' || plain[0] == '【'))
            {
                int close = plain.IndexOfAny(new char[] { ']', '】' });
                return close >= 0 ? close + 1 : plain.Length;
            }
            int open = plain.IndexOfAny(new char[] { '[', '【' });
            return open > 0 ? open : plain.Length;
        }

        // ---------------------------------------------------------------- 分类标签

        /// 分类按这些组上色，每组有自己的颜色设置（“TagColor” + 组名）。分组和 Item Details 一致：医疗/药品、酒、奢侈、食物、日用、武器、工具/机器/材料、违禁品、其余；水是后来从医疗里拆出来的，所以放在最后，老设置的位置不变。
        internal static readonly string[] TagGroups =
        {
            "Medical", "Alcohol", "Luxury", "Food", "Household", "Weapon", "Tool", "Contraband", "Other", "Water",
        };

        /// 每组的默认色，是 TagColorNames 的下标：就是 Item Details 对该组的颜色（水和医疗的一样）。
        internal static readonly int[] TagDefaults = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 0 };

        /// 设置里列出的组顺序：水紧跟在它拆出来的医疗后面。
        internal static readonly int[] TagOrder = { 0, 9, 1, 2, 3, 4, 5, 6, 7, 8 };

        /// 每组可选的颜色。
        internal static readonly string[] TagColorNames =
        {
            "蓝 Blue", "黑 Black", "粉 Pink", "绿 Green", "青 Cyan", "青绿 Teal", "紫 Purple", "红 Red", "灰 Grey",
            "橙 Orange", "黄 Yellow", "棕 Brown",
        };

        // TagColorNames 每一项的颜色和它在深色纸上的浅色版。前九个就是 Item Details 标签的填充和描边色。
        private static readonly Color32[][] TagPalette =
        {
            new[] { new Color32(21, 101, 192, 255), new Color32(115, 192, 255, 255) },
            new[] { new Color32(18, 18, 18, 255), new Color32(208, 208, 208, 255) },
            new[] { new Color32(196, 44, 122, 255), new Color32(255, 154, 213, 255) },
            new[] { new Color32(46, 125, 50, 255), new Color32(130, 217, 139, 255) },
            new[] { new Color32(0, 124, 145, 255), new Color32(92, 225, 242, 255) },
            new[] { new Color32(0, 105, 92, 255), new Color32(99, 216, 204, 255) },
            new[] { new Color32(85, 58, 133, 255), new Color32(195, 166, 255, 255) },
            new[] { new Color32(87, 26, 28, 255), new Color32(255, 148, 133, 255) },
            new[] { new Color32(85, 85, 85, 255), new Color32(192, 192, 192, 255) },
            new[] { new Color32(196, 90, 10, 255), new Color32(255, 183, 110, 255) },
            new[] { new Color32(150, 118, 0, 255), new Color32(250, 220, 100, 255) },
            new[] { new Color32(110, 72, 44, 255), new Color32(212, 178, 148, 255) },
        };

        /// 标签色的深浅两版，给面板颜色列表旁的色块用。
        internal static void TagSwatch(int index, out Color color, out Color light)
        {
            if (index < 0 || index >= TagPalette.Length) index = 0;
            color = TagPalette[index][0];
            light = TagPalette[index][1];
        }

        /// 每组选中的颜色，是 TagColorNames 的下标。
        private static int[] TagColorsNow()
        {
            int[] r = new int[TagGroups.Length];
            for (int i = 0; i < r.Length; i++)
            {
                ConfigEntry<string> e = Settings.TagColors != null ? Settings.TagColors[i] : null;
                int k = e != null ? Array.IndexOf(TagColorNames, e.Value) : -1;
                r[i] = k >= 0 ? k : TagDefaults[i];
            }
            return r;
        }

        /// 物品的一个分类，用游戏叫它的名字，以及它的颜色组。
        internal sealed class Chip
        {
            public string Name;
            public int Group;

            public Chip(string type, string name)
            {
                Name = name;
                Group = GroupOf(type);
            }
        }

        /// row 完全是它的“[机器] [工具]”这类标签时，给出物品的分类：每个名字都对得上物品的某个类型；其他行（厂商、备注、其他模组的）返回 null。
        private static List<Chip> ChipsOf(GameItem item, Row row)
        {
            if (item == null) return null;
            string plain = PlainOf(row).Trim();
            if (plain.Length == 0 || plain[0] != '[') return null;
            List<string> types;
            try { types = item.GetGameItemType(); }
            catch { return null; }
            if (types == null || types.Count == 0) return null;

            List<Chip> chips = new List<Chip>();
            int i = 0;
            while (i < plain.Length)
            {
                if (char.IsWhiteSpace(plain[i])) { i++; continue; }
                if (plain[i] != '[') return null;
                int end = plain.IndexOf(']', i);
                if (end < 0) return null;
                string name = plain.Substring(i + 1, end - i - 1);
                string type = TypeNamed(types, name);
                if (type == null) return null;
                chips.Add(new Chip(type, name));
                i = end + 1;
            }
            return chips.Count > 0 ? chips : null;
        }

        private static string TypeNamed(List<string> types, string name)
        {
            for (int i = 0; i < types.Count; i++)
            {
                string shown;
                try { shown = TypeHelper.GetTypeDisplayName(types[i]); }
                catch { continue; }
                // 按纯文本比较：tooltip 里的名字带富文本标签。
                if (shown != null && TextUnits.Plain(shown).Trim() == name) return types[i];
            }
            return null;
        }

        /// 游戏类型在 TagGroups 里的位置，分组和 Item Details 的标签一致。
        private static int GroupOf(string type)
        {
            switch ((type ?? "").ToUpperInvariant())
            {
                case "MEDICAL": case "MEDICAL_TOOL": case "ORGAN": case "NARCOTIC": case "POISON":
                    return 0;
                case "WATER": case "WATER_RATION": case "WATER_PURIFICATION_SUPPLY":
                    return 9;
                case "ALCOHOL": case "SUBSTANCE":
                    return 1;
                case "LUXURY_ITEM": case "ART":
                    return 2;
                case "FOOD": case "FRESH_FOOD": case "PROCESSED_FOOD": case "TREAT": case "BREVAGE":
                case "FARMING_SUPPLIES": case "SEED": case "MODIFIED_SEED":
                    return 3;
                case "HOUSEHOLD_GOOD":
                    return 4;
                case "WEAPON": case "MELEE_WEAPON": case "FIREARM": case "MODDABLE": case "GUN_MOD":
                case "AMMUNITION": case "MUNITION": case "ARMOR":
                    return 5;
                case "TOOL": case "MACHINE": case "MATERIAL": case "BASIC_MATERIAL": case "REFINED_MATERIAL":
                case "ENERGY_CELL": case "MODULE": case "NODE": case "HYDROPONIC_MODULE": case "ALARM_MODULE":
                case "PRINTER_MODULE": case "EVAPORATOR_MODULE": case "FILTER":
                    return 6;
                case "CONTRABAND": case "RESTRICTED_1": case "RESTRICTED_2": case "RESTRICTED_3":
                    return 7;
                default:
                    return 8;
            }
        }

        private static float ChipSize(Ctx c) { return c.F * 0.75f; }

        // 有底色时标签有内边距、彼此挨得近；没有时就是稍微分开的几个词。
        private static float ChipPadX(Ctx c) { return c.TagWash ? 3f * c.U : 0f; }
        private static float ChipPadY(Ctx c) { return c.TagWash ? 2f * c.U : c.U; }
        private static float ChipGap(Ctx c) { return c.TagWash ? 2f * c.U : 4f * c.U; }

        /// 标签的宽度：名字加两边内边距。
        private static float ChipWidth(Ctx c, Chip chip)
        {
            return c.Measure(chip.Name, ChipSize(c)) + 2f * ChipPadX(c);
        }

        /// 标签在当前纸上的样子：一层很淡的自身颜色，像贴在单子上的彩色标签，名字用那个颜色。Item Details 那种实心带亮边的标签在纸上看着像按钮。深色纸底色更深、名字用浅色版。
        private static void ChipInks(Ctx c, Chip chip, out Color wash, out Color text)
        {
            int k = c.TagColors != null && chip.Group < c.TagColors.Length ? c.TagColors[chip.Group] : TagDefaults[chip.Group];
            if (k < 0 || k >= TagPalette.Length) k = 0;
            Color color = TagPalette[k][0], light = TagPalette[k][1];
            if (darkPaper)
            {
                wash = Color.Lerp(Paper, color, 0.45f);
                text = light;
            }
            else
            {
                wash = Color.Lerp(Paper, color, 0.2f);
                text = color * 0.85f;
                text.a = 1f;
            }
        }

        /// 所有标签排一行的总宽度，单子要这么宽才不会折行。
        private static float ChipsWidth(Ctx c, List<Chip> chips)
        {
            float w = 0f;
            for (int i = 0; i < chips.Count; i++) w += ChipWidth(c, chips[i]) + (i > 0 ? ChipGap(c) : 0f);
            return w;
        }

        /// 分类画成小块的淡色标签（关掉标签底色时就是彩色文字），排在名字下面一行，放不下就折行。返回它们下面的 y。
        private static float DrawChips(Ctx c, List<Chip> chips, float x, float y, float wrap)
        {
            float px = ChipPadX(c), py = ChipPadY(c), gap = ChipGap(c);
            float cx = x, cy = y + 2f * c.U, rowH = 0f;
            for (int i = 0; i < chips.Count; i++)
            {
                Color wash, ink;
                ChipInks(c, chips[i], out wash, out ink);
                TextMeshProUGUI t = Text(c, ChipSize(c), chips[i].Name, ink, 0f);
                Vector2 ts = t.rectTransform.sizeDelta;
                float w = ts.x + 2f * px, h = ts.y + 2f * py;
                if (cx > x && cx + w > x + wrap)
                {
                    cx = x;
                    cy += rowH + c.U * 2f;
                    rowH = 0f;
                }
                if (c.TagWash) Solid(c, wash, cx, cy, w, h);
                Ui.Move(t.rectTransform, cx + px, cy + py);
                cx += w + gap;
                rowH = Mathf.Max(rowH, h);
            }
            return cy + rowH;
        }

        /// 赃物标签的文字：“赃物  14%热度”。
        private static string StolenLabel(string text)
        {
            string main, sub;
            SplitFlag(text, out main, out sub);
            return "<b>" + (sub.Length > 0 ? main + "<space=0.6em>" + sub : main) + "</b>";
        }

        /// 赃物在名字下面盖一个小红色实心标签，不用印章：它在标题自己的流里，不会和违禁品印章重叠，也保持小。返回它下面的 y。
        private static float StolenTag(Ctx c, string text, float x, float y)
        {
            float px = 3f * c.U, py = c.U, top = 2f * c.U;
            TextMeshProUGUI t = Text(c, c.F * 0.75f, StolenLabel(text), Paper, 0f);
            Vector2 ts = t.rectTransform.sizeDelta;
            Solid(c, StampRed, x, y + top, ts.x + 2f * px, ts.y + 2f * py);
            Ui.Move(t.rectTransform, x + px, y + top + py);
            return y + top + ts.y + 2f * py;
        }

        /// 价格右侧空白处盖一个橡皮章（“违禁品”）。正的，因为倾斜的像素字会糊；双线框加几处磨掉的缺口，看起来像盖上去的，而不是个按钮。
        private static RectTransform Stamp(Ctx c, string text, float y, int index)
        {
            string main, sub;
            SplitFlag(text, out main, out sub);
            RectTransform st = Ui.NewRect(c.Card, "stamp");
            TextMeshProUGUI t = Ui.NewText(st, c.Src, c.F * StampMain, "<b>" + main + "</b>", StampRed, 0f);
            Vector2 ms = t.rectTransform.sizeDelta;
            float w = ms.x, h = ms.y;
            TextMeshProUGUI s = null;
            if (sub.Length > 0)
            {
                s = Ui.NewText(st, c.Src, c.F * StampSub, sub, StampRed, 0f);
                w = Mathf.Max(w, s.rectTransform.sizeDelta.x);
                h += s.rectTransform.sizeDelta.y;
            }
            float edge = StampEdge * c.U, outer = 2f * c.U, inset = outer + c.U;
            float sw = w + 2f * edge, sh = h + 2f * edge;
            Frame(st, 0f, 0f, sw, sh, outer);
            Frame(st, inset, inset, sw - 2f * inset, sh - 2f * inset, c.U);
            Wear(c, st, text, sw, sh, outer);
            Ui.Move(t.rectTransform, Mathf.Round((sw - ms.x) / 2f), edge);
            if (s != null) Ui.Move(s.rectTransform, Mathf.Round((sw - s.rectTransform.sizeDelta.x) / 2f), edge + ms.y);

            float x = c.W - c.Pad - sw - index * Mathf.Round(sw * 0.35f);
            Ui.Place(st, x, y + c.Gap + index * 2f * c.U, sw, sh);
            // 轴心放中间，落下时以中心缩放。
            st.pivot = new Vector2(0.5f, 0.5f);
            st.anchoredPosition += new Vector2(sw / 2f, -sh / 2f);
            return st;
        }

        /// 印章主文字和副文字的字号，相对正文字号。
        private const float StampMain = 0.8f, StampSub = 0.6f;
        /// 单子上最小的字号相对正文的比例（印章的小字），抬字号时按它算。
        private const float MinFontShare = StampSub;
        /// 印章每边的边框和内边距，按像素：外线 2、间隔 1、内线 1、内边距 1。
        private const float StampEdge = 5f;

        private static void Frame(RectTransform parent, float x, float y, float w, float h, float thick)
        {
            Ui.Place(Ui.NewImage(parent, StampRed), x, y, w, thick);
            Ui.Place(Ui.NewImage(parent, StampRed), x, y + h - thick, w, thick);
            Ui.Place(Ui.NewImage(parent, StampRed), x, y + thick, thick, h - 2f * thick);
            Ui.Place(Ui.NewImage(parent, StampRed), x + w - thick, y + thick, thick, h - 2f * thick);
        }

        /// 外框线上几处没沾上墨的小缺口；同一个标记用同一组。
        private static void Wear(Ctx c, RectTransform st, string text, float sw, float sh, float thick)
        {
            System.Random rng = new System.Random(text.GetHashCode());
            for (int k = 0; k < 5; k++)
            {
                float len = rng.Next(1, 4) * c.U;
                // 只挖最外面那一行像素：缺口叠在一起也还有里面一行，框永远不会破。
                bool far = rng.Next(2) == 0;
                if (rng.Next(3) > 0)
                {
                    float x = Mathf.Round((float)rng.NextDouble() * (sw - len - 2f * thick) / c.U) * c.U + thick;
                    Ui.Place(Ui.NewImage(st, Paper), x, far ? sh - c.U : 0f, len, c.U);
                }
                else
                {
                    float yy = Mathf.Round((float)rng.NextDouble() * (sh - len - 2f * thick) / c.U) * c.U + thick;
                    Ui.Place(Ui.NewImage(st, Paper), far ? sw - c.U : 0f, yy, c.U, len);
                }
            }
        }

        private static void Dashes(Ctx c, float x, float y, float width, float dash, float gap, float thick, Color color)
        {
            for (float dx = 0f; dx + dash <= width; dx += dash + gap)
                Solid(c, color, x + dx, y, dash, thick);
        }

        // ---------------------------------------------------------------- 文字和颜色

        private static string TicketNumber(GameItemElement item)
        {
            int n = 0;
            try { n = item != null ? item.uniqueId : 0; } catch { }
            return (Math.Abs(n) % 10000).ToString("D4");
        }

        private static string TicketNo(GameItemElement item)
        {
            string fmt = SectionedTooltipsMod.L("ticket_no");
            try { return string.Format(fmt, TicketNumber(item)); }
            catch { return "No." + TicketNumber(item); }
        }

        /// 把游戏的调色板颜色转成纸上的墨：同样的色相压暗（深色纸上提亮、略淡一点）；灰色改用纸自己的墨。
        private static Color InkOf(Palette p, Color fallback)
        {
            if (p == Palette.None) return fallback;
            return InkOf(Overlay.ToColor(p, Palette.Gray), fallback);
        }

        /// 任意颜色调到纸上：保留色相，按纸色压暗或提亮；太灰的用 fallback。
        private static Color InkOf(Color c, Color fallback)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            if (s < 0.25f) return fallback;
            Color k = darkPaper
                ? Color.HSVToRGB(h, Mathf.Clamp01(s * 0.55f + 0.1f), 0.88f)
                : Color.HSVToRGB(h, Mathf.Clamp01(s * 0.8f + 0.2f), 0.45f);
            k.a = 1f;
            return k;
        }

        private static string InkRich(List<Seg> segs, Color baseInk)
        {
            return InkRich(segs, baseInk, true);
        }

        /// segs 转成墨色的富文本；keepColors 关掉时全部用 baseInk。
        private static string InkRich(List<Seg> segs, Color baseInk, bool keepColors)
        {
            if (segs == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (Drawn(s) || string.IsNullOrEmpty(s.Text)) continue;
                Color ink = keepColors ? InkOf(s.Color, baseInk) : baseInk;
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ink)).Append('>');
                if (s.Bold) sb.Append("<b>");
                sb.Append(s.Text);
                if (s.Bold) sb.Append("</b>");
                sb.Append("</color>");
            }
            return sb.ToString().Trim();
        }

        internal static string PlainOf(Row row)
        {
            return PlainOf(row.Segs ?? row.Label);
        }

        internal static string PlainOf(List<Seg> segs)
        {
            if (segs == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++) if (segs[i].Bar == null) sb.Append(segs[i].Text);
            return TextUnits.Plain(sb.ToString());
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

        private static string LeadingNumber(string s)
        {
            s = s.Trim();
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == ',' || (end == 0 && s[end] == '-'))) end++;
            return end > 0 && char.IsDigit(s[end - 1]) ? s.Substring(0, end) : "";
        }

        /// 把价格数值“50（进价 50） 标价：50”拆成数字后面到右括号之前的部分（note），以及之后的部分（tail），颜色保留。
        private static void SplitPrice(List<Seg> value, out List<Seg> note, out List<Seg> tail)
        {
            note = new List<Seg>();
            tail = new List<Seg>();
            bool numberCut = false, inTail = false;
            for (int i = 0; i < value.Count; i++)
            {
                Seg s = value[i];
                if (s.Bar != null) continue;
                if (inTail) { tail.Add(s); continue; }
                string t = s.Text;
                if (!numberCut && t.Trim().Length > 0)
                {
                    t = t.TrimStart();
                    t = t.Substring(LeadingNumber(t).Length);
                    numberCut = true;
                }
                int close = t.IndexOfAny(new char[] { '）', ')' });
                if (close >= 0)
                {
                    note.Add(s.With(t.Substring(0, close + 1)));
                    string rest = t.Substring(close + 1);
                    if (rest.Trim().Length > 0) tail.Add(s.With(rest));
                    inTail = true;
                    continue;
                }
                if (t.Length > 0) note.Add(s.With(t));
            }
        }

        /// “标价：50”转成标签/数值行，转不了返回 null。
        internal static Row PairFromPlain(string plain)
        {
            plain = plain.Trim();
            int colon = plain.IndexOfAny(new char[] { ':', '：' });
            if (colon <= 0 || colon >= plain.Length - 1) return null;
            Seg label = new Seg();
            label.Text = plain.Substring(0, colon).Trim();
            Seg value = new Seg();
            value.Text = plain.Substring(colon + 1).Trim();
            Row row = new Row();
            row.Label = new List<Seg> { label };
            row.Value = new List<Seg> { value };
            return row;
        }

        /// 价格后面括号里的注解，转成墨色、保留颜色、去掉括号。
        private static string NoteRich(List<Seg> note)
        {
            List<Seg> rest = new List<Seg>();
            for (int i = 0; i < note.Count; i++)
            {
                Seg s = note[i];
                if (rest.Count == 0)
                {
                    string t = s.Text.TrimStart();
                    if (t.Length == 0) continue;
                    s = s.With(t);
                }
                // “进价 350-140”会读成“350 到 140”：盈亏前面空一格，“350 -140”。
                else if (s.Text.Length > 1 && (s.Text[0] == '+' || s.Text[0] == '-') && char.IsDigit(s.Text[1])) s = s.With(" " + s.Text);
                rest.Add(s);
            }
            string rich = InkRich(rest, InkSoft);
            string plain = TextUnits.Plain(rich).Trim();
            if (plain.Length >= 2 && (plain[0] == '（' || plain[0] == '('))
            {
                int open = rich.IndexOfAny(new char[] { '（', '(' });
                int close = rich.LastIndexOfAny(new char[] { '）', ')' });
                if (open >= 0 && close > open) rich = rich.Remove(close, 1).Remove(open, 1);
            }
            return rich;
        }
    }
}
