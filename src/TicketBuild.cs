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
    /// 建一张估价单：这一次构建的上下文（Ctx）、复用的文字、把各部分拼成卡片（BuildCard）、单子的宽度、太高时分栏。
    internal static partial class Ticket
    {
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.4f);

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
            /// 各节的定型结果（见 TicketPlan.PlanOf）；一次构建里每节只定一次，画和量宽共用。
            public readonly Dictionary<Block, Ticket.SectionPlan> Plans = new Dictionary<Block, Ticket.SectionPlan>();
            /// 液体一节带条成分行的数字列（见 TicketGrid.NumColsOf）。
            public readonly Dictionary<Block, Ticket.NumCols> Nums = new Dictionary<Block, Ticket.NumCols>();
            public float Px = 1f;  // 每个卡片单位占多少屏幕像素，图标要对齐整像素（见 PixelFit）
            public List<Piece> Pieces;  // 分栏用的各块（见 Flow）
            public bool Fine;           // 每行另起一块，一节可以从中间断到下一栏（单子比窗口高时才这样重建）
            public float Lift = 1f;     // 字号抬高的倍数，卡片整张缩回这么多（见 Compat.SmallTextResized）
            public float Room = float.MaxValue;  // 窗口高度，用这张卡片的单位
            /// 窗口的高和宽，用根节点的单位（字号抬高之前的；Room 是换算到这张卡片之后的高）。
            public float Screen = float.MaxValue, ScreenW = float.MaxValue;
            /// 这一张平时收起的部分（见 Expand），以及实际收起了哪些（写进底部的提示）。
            public bool FoldPrices, FoldContents, FoldDesc, FoldMods, FoldFactors;
            public readonly List<string> Folded = new List<string>();
            /// 因为比窗口宽，整张缩到的比例（不含字号抬高的那部分）。
            public float Fit = 1f;
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

        /// 建单子：比窗口高时按行分块重建一次，这样一节太长也能从中间断到下一栏。平常一节一块，物体少一些。
        /// full：建完整的还是收起的；room、roomW：窗口的高和宽（卡片单位）。
        private static void BuildFlow(TMP_Text tmp, LayoutResult r, GameItemElement item, bool full, float room, float roomW)
        {
            if (BuildCard(tmp, r, item, full, room, roomW, false)) return;
            Drop(card);
            card = null;
            BuildCard(tmp, r, item, full, room, roomW, true);
        }

        /// 这张单子实际要画的排版。收起价格影响因素时，附加信息里那几行（“市场  日用品救济行动 -40%”、“黑市溢价  +70%”）先拿掉；
        /// 返回的是副本，原来的排版属于缓存。没有可拿掉的就原样返回。
        private static LayoutResult Visible(Ctx c, LayoutResult r)
        {
            if (!c.FoldFactors || r.Blocks == null) return r;
            List<Block> blocks = new List<Block>(r.Blocks.Count);
            bool removed = false;
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (bl.Key != Sections.Mods) { blocks.Add(bl); continue; }
                Block kept = new Block();
                kept.Key = bl.Key;
                for (int j = 0; j < bl.Rows.Count; j++)
                {
                    if (PriceFactor(bl.Rows[j])) removed = true;
                    else kept.Rows.Add(bl.Rows[j]);
                }
                if (kept.Rows.Count > 0) blocks.Add(kept);
            }
            if (!removed) return r;
            Folded(c, "fold_factors");
            LayoutResult view = new LayoutResult();
            view.Blocks = blocks;
            view.Contents = r.Contents;
            view.Liquid = r.Liquid;
            view.IconLine = r.IconLine;
            view.IconChars = r.IconChars;
            return view;
        }

        /// 价格影响因素：一对，数值以带正负号的百分比结尾（其他模组写在附加信息里的市场事件、黑市溢价）。
        private static bool PriceFactor(Row row)
        {
            if (!row.IsPair || row.Value == null) return false;
            string v = TextUnits.Plain(row.Value).Trim();
            int i = v.Length - 1;
            if (i < 2 || v[i] != '%') return false;
            int digits = 0;
            for (i--; i >= 0 && (char.IsDigit(v[i]) || v[i] == '.'); i--) digits++;
            return digits > 0 && i >= 0 && (v[i] == '+' || v[i] == '-' || v[i] == '−');
        }

        /// 记下这张单子收起了哪一部分，写进底部的提示。
        private static void Folded(Ctx c, string key)
        {
            string name = SectionedTooltipsMod.L(key);
            if (!c.Folded.Contains(name)) c.Folded.Add(name);
        }

        /// 建一张单子。fine：每行另起一块（见 BuildFlow）。单子比窗口高、还没按行分块时返回 false：这张半成品不要了，调用方按行分块重建。
        private static bool BuildCard(TMP_Text tmp, LayoutResult source, GameItemElement item, bool full, float room, float roomW, bool fine)
        {
            // 上一张卡片留着，只是隐藏，鼠标回到它的物品时直接用。
            if (card != null) card.gameObject.SetActive(false);
            builtFor = source;
            builtItem = item;
            builtFull = full;
            lastBuildFrame = Time.frameCount;
            waitingItem = null;
            StartProbe();

            Ctx c = new Ctx();
            c.Screen = room;
            c.ScreenW = roomW;
            c.FoldPrices = !full && Expand.Folds(Settings.CollapsePrices);
            c.FoldContents = !full && Expand.Folds(Settings.CollapseContents);
            c.FoldDesc = !full && Expand.Folds(Settings.CollapseDescription);
            c.FoldMods = !full && Expand.Folds(Settings.CollapseModInfo);
            c.FoldFactors = !full && !c.FoldMods && Expand.Folds(Settings.CollapsePriceFactors);
            LayoutResult r = Visible(c, source);
            // Measure 会抵掉根节点的缩放（文字在屏幕上和游戏一样大），所以大小设置要在这里乘进去。
            // 有模组会在建好后放大小字号时（见 Compat.SmallTextResized）：我们排版时不知道，就会重叠。所以自己先放大同样倍数，
            // 再把所有字号抬到它不再动的字号以上、整张按同样比例缩回去，看起来一样大，它也就不再动我们的字。
            float boost, floor, lift = 1f;
            if (Compat.SmallTextResized(out boost, out floor))
            {
                Panels.Metrics plain = Panels.Measure(tmp, root, Settings.ScaleFactor * boost);
                lift = Mathf.Max(1f, floor / (plain.Font * MinFontShare));
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

            c.W = Width(c, p, r, item);
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
            c.Fine = fine;
            int sideRows = 0;
            bool asideDone = false;
            for (int i = 0; i < r.Blocks.Count; i++)
            {
                Block bl = r.Blocks[i];
                if (!OnSlip(bl.Key)) continue;
                if (c.FoldMods && bl.Key == Sections.Mods)
                {
                    Folded(c, "fold_mods");
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
                        if (c.FoldMods && side.Key == Sections.Mods) { Folded(c, "fold_mods"); continue; }
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
            if (inside != null && c.FoldContents)
            {
                // 收起的是物品格子；电池、水箱这类量条是读数，留着。
                Folded(c, "fold_contents");
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
            if (c.FoldDesc && p.Desc.Count > 0) Folded(c, "fold_desc");
            else y = Description(c, p, item, y);
            if (!c.FoldMods) y = ModSource(c, item, y);
            if (c.Folded.Count > 0) y = FoldHint(c, y);
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
            c.Room = room * lift;
            roomW *= lift;
            bool asideColumn = sideRows > 0 && ((lines > 0 && sideRows > lines) || h > c.Room);
            // 比窗口高：按行分块重新建一次（BuildFlow），这张半成品不要了。
            if (h > c.Room && !c.Fine) return false;
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
            c.Fit = fit;
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
            // dev 构建的压力用例和样本：不管开没开调试日志都写。
            if (Dev.Label != null)
            {
                Dev.AfterBuild(card);
                log = true;
            }
            if (log) LogSlip(c, item, card, w, h);
            Keep(source, item);
            if (probe == null) return true;
            Step("finish");
            SectionedTooltipsMod.Log(string.Format("slip built in {0:F1} ms ({1} objects, {2} rectangles), frame {3}: {4}",
                Ms(probe.ElapsedTicks), card.childCount, c.Q.Count, Time.frameCount, steps.ToString().TrimEnd(',', ' ')));
            SectionedTooltipsMod.Log(string.Format("  texts {0} in {1:F1} ms ({2} new {3:F1} ms, {4} wrapped {5:F1} ms); measured {6} in {7:F1} ms",
                texts, Ms(textTicks), newTexts, Ms(newTextTicks), wrapped, Ms(wrapTicks), measures, Ms(measureTicks)));
            probe = null;
            return true;
        }

        private static float Width(Ctx c, Parts p, LayoutResult r, GameItemElement item)
        {
            float need = 0f;
            float small = c.F * 0.75f;
            if (Settings.On(Settings.ShowFooter)) need = Mathf.Max(need, c.Measure(TicketNo(item), c.F * 0.65f) + 24f * c.U + 3f * c.Gap);
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
                float lead = (IconFor(item) != null ? IconBox(c) + c.Gap * 1.5f : 0f) + c.U + 2f * c.Pad;
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

        /// 名字最多把单子加宽到多少行；超过就还是折行。
        private const float NameMaxLines = 26f;

        private static float PairWidth(Ctx c, Row row)
        {
            float bar = BarOf(row.Value) != null ? BarWidth(c) + c.Gap : 0f;
            return c.Measure(InkRich(row.Label, InkSoft), c.F * 0.9f) + bar + c.Measure(InkRich(row.Value, Ink), c.F * 0.9f) + 3f * c.Gap;
        }

        private static float Snap(Ctx c, float v)
        {
            return Mathf.Round(v * c.Px) / c.Px;
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
            string text = string.Format(SectionedTooltipsMod.L("fold_view"), string.Join(sep, c.Folded.ToArray()));
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

        private static void Dashes(Ctx c, float x, float y, float width, float dash, float gap, float thick, Color color)
        {
            for (float dx = 0f; dx + dash <= width; dx += dash + gap)
                Solid(c, color, x + dx, y, dash, thick);
        }
    }
}
