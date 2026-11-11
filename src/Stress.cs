#if DEVDUMP
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// <summary>
    /// dev 构建专用：压力测试。游戏里很难凑出的极端单子（几十条升级、超长名字、比屏幕还高、大字体……）用假数据现场造出来。
    ///
    /// 触发：模组数据目录里有 stress.flag。之后悬停物品，每种物品（按显示名）第一次悬停时，
    /// 用这件物品真实的 tooltip 加上每个用例的假数据，各走一遍完整的排版和建单，写进日志的 [slip]/[quad]，
    /// 名字是“压力:用例 · 物品名”。Modding\tool 的 view.js 在游戏外画出来、跑检查。
    /// 每局游戏最多跑 MaxItems 种物品；要重跑就重开游戏。
    /// </summary>
    internal static class Stress
    {
        private const string ModId = "amres.sectioned_tooltips";
        private const string Flag = "stress.flag";
        private const int MaxItems = 12;

        /// 一个用例：怎么改这件物品的行和内容物，单子在多高的窗口里排，建完要不要模拟大字体模组。
        private sealed class Case
        {
            public string Name;
            public Action<List<Line>, ContentsInfo> Edit;
            public float Room = 1f;
            public bool BigFonts;
            /// 只有物品装着东西时才跑。
            public bool NeedsContents;
        }

        /// 排好、等着建单的一个用例。
        private sealed class Ready
        {
            public Case Case;
            public LayoutResult Result;
            /// 样本（States）建的是样本自己的单子；压力用例为 null，用悬停的物品。
            public GameItemElement Item;
        }

        private static readonly HashSet<string> done = new HashSet<string>();
        private static readonly List<Ready> ready = new List<Ready>();
        private static GameItem readyFor;
        private static bool flag;
        private static float checkedAt = -10f;

        /// 正在建的用例名；LogSlip 拿它当名字前缀。没在跑时为 null。
        internal static string Label;
        /// 当前用例建完后要不要把文字放大（模拟“字体大小优化”）。
        private static bool bigFonts;

        /// 大字体用例：当作开着「字体大小优化」，估价单走兼容的那套（ReadableFonts.Active）。
        internal static bool EmulateReadableFonts { get { return bigFonts && Label != null; } }

        // ---------------------------------------------------------------- 排版阶段（Layout.Apply 里调用）

        /// 这件物品要不要跑压力测试；要的话 Layout.Apply 不走缓存。
        internal static bool Wanted(GameItem item)
        {
            // 全量导出时的物品不是玩家悬停的，不算。
            if (item == null || DumpAll.Running || States.Building || ReferenceEquals(item, readyFor)) return false;
            if (Time.unscaledTime - checkedAt > 1f)
            {
                checkedAt = Time.unscaledTime;
                try { flag = ModHelper.ModDataFileExists(ModId, Flag); }
                catch { flag = false; }
            }
            if (!flag || done.Count >= MaxItems) return false;
            return !done.Contains(NameOf(item));
        }

        /// 用这件物品分好节之前的行，给每个用例排一份结果，等估价单那边建。
        internal static void Prepare(GameItem item, List<Line> lines, ContentsInfo contents)
        {
            string name = NameOf(item);
            done.Add(name);
            ready.Clear();
            readyFor = item;
            List<Case> cases = Cases();
            for (int i = 0; i < cases.Count; i++)
            {
                Case k = cases[i];
                bool has = contents != null && contents.Entries.Count > 0;
                if (k.NeedsContents && !has) continue;
                try
                {
                    List<Line> copy = Copy(lines);
                    ContentsInfo inside = Copy(contents);
                    if (k.Edit != null) k.Edit(copy, inside);
                    Ready r = new Ready();
                    r.Case = k;
                    r.Result = new LayoutResult();
                    r.Result.Blocks = Layout.Group(copy);
                    r.Result.Contents = inside;
                    ready.Add(r);
                }
                catch (Exception e)
                {
                    SectionedTooltipsMod.Log("压力测试：用例 " + k.Name + " 排版失败：" + e);
                }
            }
            SectionedTooltipsMod.Log(string.Format("压力测试：{0} 排好 {1} 个用例", name, ready.Count));
        }

        // ---------------------------------------------------------------- 样本（samples.flag）

        private const string SamplesFlag = "samples.flag";
        /// 全量建单：样本之外，游戏里每一种物品也各建一张。改画法前后可以逐张比坐标；第一次悬停要卡几秒。
        private const string SlipsFlag = "slips.flag";
        private static bool samplesDone;
        private static List<States.Sample> samples;
        /// 全量建单时新建的物品，建完单子一起销毁。
        private static readonly List<GameItem> created = new List<GameItem>();

        /// 有 samples.flag 时，第一次悬停物品就把 States 的每个样本排好版，等下面和压力用例一样逐个建单、写日志。每局只跑一次。
        /// 有 slips.flag 时再加上游戏里的每一种物品（见 PrepareAll）。
        internal static void PrepareSamples(GameItem hovered)
        {
            if (samplesDone || hovered == null || ready.Count > 0) return;
            bool wanted, everything;
            try { everything = ModHelper.ModDataFileExists(ModId, SlipsFlag); }
            catch { everything = false; }
            try { wanted = everything || ModHelper.ModDataFileExists(ModId, SamplesFlag); }
            catch { wanted = false; }
            if (!wanted) return;
            samplesDone = true;
            GameItem keepItem = Layout.LastItem;
            LayoutResult keepResult = Layout.LastResult;
            States.Building = true;
            try
            {
                System.Reflection.MethodInfo get = HarmonyLib.AccessTools.Method(typeof(GameItemElement), "GetTooltipBasic", Type.EmptyTypes);
                samples = States.Make();
                for (int i = 0; i < samples.Count; i++)
                {
                    States.Sample s = samples[i];
                    try
                    {
                        States.Invoke(get, s);
                        if (!ReferenceEquals(Layout.LastItem, s.Item) || Layout.LastResult == null)
                        {
                            SectionedTooltipsMod.Log("样本：" + s.Label + " 没有排版结果");
                            continue;
                        }
                        Ready r = new Ready();
                        r.Case = Make("样本·" + s.Label, null);
                        r.Result = Layout.LastResult;
                        r.Item = s.Item as GameItemElement;
                        ready.Add(r);
                    }
                    catch (Exception e)
                    {
                        SectionedTooltipsMod.Log("样本：" + s.Label + " 排版失败：" + e);
                    }
                }
                int sampleCount = ready.Count;
                if (everything) PrepareAll(get);
                readyFor = hovered;
                SectionedTooltipsMod.Log(string.Format("样本：排好 {0} 个，全量 {1} 件", sampleCount, ready.Count - sampleCount));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("样本失败：" + e);
            }
            finally
            {
                States.Building = false;
                Layout.LastItem = keepItem;
                Layout.LastResult = keepResult;
            }
        }

        /// 每一种物品新建一件、排好版，排进待建的队列，名字是“全量·id”。
        private static void PrepareAll(System.Reflection.MethodInfo get)
        {
            List<string> ids = DirectoryMaster.GetIdentifierList<GameItem>();
            if (ids == null) return;
            int failed = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                GameItem item = null;
                bool kept = false;
                try
                {
                    item = States.Create(ids[i]);
                    GameItemElement element = item as GameItemElement;
                    if (element == null) { failed++; continue; }
                    get.Invoke(item, null);
                    if (!ReferenceEquals(Layout.LastItem, item) || Layout.LastResult == null) { failed++; continue; }
                    Ready r = new Ready();
                    r.Case = Make("全量·" + ids[i], null);
                    r.Result = Layout.LastResult;
                    r.Item = element;
                    ready.Add(r);
                    created.Add(item);
                    kept = true;
                }
                catch { failed++; }
                finally
                {
                    if (!kept && item != null)
                    {
                        try { item.Destroy(); } catch { }
                    }
                }
            }
            if (failed > 0) SectionedTooltipsMod.Log(string.Format("全量建单：{0} 件没有排版结果，跳过", failed));
        }

        // ---------------------------------------------------------------- 建单阶段（Ticket.Show 里调用）

        /// 有排好的用例等着这件物品时返回 true。
        internal static bool Pending(GameItem item)
        {
            return ready.Count > 0 && ReferenceEquals(item, readyFor);
        }

        /// 依次建每个用例的单子：room 是窗口高度（卡片单位），build 建一张并写日志，drop 把它丢掉。
        internal static void Run(float room, Action<LayoutResult, float, GameItemElement> build)
        {
            int ok = 0;
            try
            {
                for (int i = 0; i < ready.Count; i++)
                {
                    Ready r = ready[i];
                    Label = r.Case.Name;
                    bigFonts = r.Case.BigFonts;
                    try
                    {
                        build(r.Result, room * r.Case.Room, r.Item);
                        ok++;
                    }
                    catch (Exception e)
                    {
                        SectionedTooltipsMod.Log("压力测试：用例 " + r.Case.Name + " 建单失败：" + e);
                    }
                }
            }
            finally
            {
                Label = null;
                bigFonts = false;
                ready.Clear();
                States.Destroy(samples);
                for (int i = 0; i < created.Count; i++)
                {
                    try { created[i].Destroy(); } catch { }
                }
                created.Clear();
            }
            SectionedTooltipsMod.Log(string.Format("压力测试：建好 {0} 张单子，窗口高 {1:F0}", ok, room));
        }

        /// 建完、写日志之前调用：大字体用例照「字体大小优化」0.1.8 的规则改字号（16 号以下的字 ×1.2、至少 10、最多 16），文字框跟着撑开。
        internal static void AfterBuild(RectTransform card)
        {
            if (!bigFonts || card == null) return;
            TextMeshProUGUI[] texts = card.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TextMeshProUGUI t = texts[i];
                if (t == null || t.fontSize >= 16f) continue;
                t.fontSize = Mathf.Min(16f, Mathf.Max(t.fontSize * 1.2f, 10f));
                Vector2 pref = t.GetPreferredValues(t.text, t.rectTransform.sizeDelta.x > 0f && t.enableWordWrapping ? t.rectTransform.sizeDelta.x : 0f, 0f);
                t.rectTransform.sizeDelta = new Vector2(Mathf.Max(t.rectTransform.sizeDelta.x, pref.x), pref.y);
            }
        }

        // ---------------------------------------------------------------- 用例

        private static List<Case> Cases()
        {
            List<Case> list = new List<Case>();
            list.Add(Make("原样", null));
            Case half = Make("半屏", null);
            half.Room = 0.5f;
            list.Add(half);
            Case third = Make("三分之一屏·每行×3", delegate(List<Line> l, ContentsInfo c) { Repeat(l, 3); });
            third.Room = 0.34f;
            list.Add(third);
            list.Add(Make("每行×5", delegate(List<Line> l, ContentsInfo c) { Repeat(l, 5); }));
            list.Add(Make("升级30条", delegate(List<Line> l, ContentsInfo c) { Upgrades(l, 30); }));
            list.Add(Make("附加信息40行", delegate(List<Line> l, ContentsInfo c) { ModLines(l, 40); }));
            list.Add(Make("超长名字", delegate(List<Line> l, ContentsInfo c) { LongName(l); }));
            list.Add(Make("超长标签", delegate(List<Line> l, ContentsInfo c) { LongTags(l); }));
            list.Add(Make("长事件", delegate(List<Line> l, ContentsInfo c) { Events(l, 5); }));
            list.Add(Make("英文长行", delegate(List<Line> l, ContentsInfo c) { English(l); }));
            Case many = Make("内容物×10", delegate(List<Line> l, ContentsInfo c) { MoreContents(c, 10); });
            many.NeedsContents = true;
            list.Add(many);
            Case big = Make("大字体（字体大小优化）", null);
            big.BigFonts = true;
            list.Add(big);
            Case bigTall = Make("大字体（字体大小优化）·每行×3", delegate(List<Line> l, ContentsInfo c) { Repeat(l, 3); });
            bigTall.BigFonts = true;
            list.Add(bigTall);
            list.Add(Make("全部叠加", delegate(List<Line> l, ContentsInfo c)
            {
                Repeat(l, 2);
                Upgrades(l, 20);
                ModLines(l, 20);
                LongName(l);
                LongTags(l);
                Events(l, 3);
                English(l);
                MoreContents(c, 5);
            }));
            return list;
        }

        private static Case Make(string name, Action<List<Line>, ContentsInfo> edit)
        {
            Case k = new Case();
            k.Name = name;
            k.Edit = edit;
            return k;
        }

        /// 名字、描述、价格以外的每一行重复 n 遍（重复的那几份标上序号，免得被当成重复行合并）。
        private static void Repeat(List<Line> lines, int n)
        {
            List<Line> result = new List<Line>(lines.Count * n);
            for (int i = 0; i < lines.Count; i++)
            {
                Line l = lines[i];
                result.Add(l);
                if (l.Key == Sections.Title || l.Key == Sections.Desc || l.Key == Sections.Value) continue;
                if (l.Plain.Trim().Length == 0) continue;
                for (int k = 2; k <= n; k++)
                {
                    Line c = CopyLine(l);
                    // 第一段加个序号：分节、拆标签照旧，只是不和原行一模一样。
                    for (int s = 0; s < c.Segs.Count; s++)
                    {
                        if (c.Segs[s].Bar != null || TextUnits.Plain(c.Segs[s].Text).Trim().Length == 0) continue;
                        c.Segs[s] = c.Segs[s].With(Numbered(c.Segs[s].Text, k));
                        break;
                    }
                    c.Plain = TextUnits.Plain(Join(c.Segs));
                    result.Add(c);
                }
            }
            lines.Clear();
            lines.AddRange(result);
        }

        /// 在开头（前导空白之后）加“#k ”：标签跟着变，不会被当成重复行。
        private static string Numbered(string text, int k)
        {
            int lead = 0;
            while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
            return text.Substring(0, lead) + "#" + k + " " + text.Substring(lead);
        }

        /// 像“机器升级”类模组那样，机器一节多出 n 条“强化项：a/b”。
        private static void Upgrades(List<Line> lines, int n)
        {
            int at = LastIndexOf(lines, Sections.Machine);
            List<Line> add = new List<Line>();
            for (int i = 1; i <= n; i++)
                add.Add(NewLine(Sections.Machine, "强化项" + i + "：" + (i % 10) + "/9", Palette.None));
            lines.InsertRange(at >= 0 ? at + 1 : Before(lines), add);
        }

        /// 其他模组加的 n 行：有分数的、有长句的。
        private static void ModLines(List<Line> lines, int n)
        {
            int at = Before(lines);
            List<Line> add = new List<Line>();
            for (int i = 1; i <= n; i++)
            {
                string text = i % 3 == 0
                    ? "说明" + i + "，这是一行比较长的附加信息，用来测试折行、分栏和单子宽度会不会被撑开。"
                    : "附加项" + i + "：" + i + "/" + n;
                add.Add(NewLine(Sections.Mods, text, Palette.None));
            }
            lines.InsertRange(at, add);
        }

        private static void LongName(List<Line> lines)
        {
            int at = lines.FindIndex(l => l.Key == Sections.Title);
            if (at < 0) return;
            Line name = lines[at];
            for (int s = 0; s < name.Segs.Count; s++)
            {
                if (TextUnits.Plain(name.Segs[s].Text).Trim().Length == 0) continue;
                name.Segs[s] = name.Segs[s].With(name.Segs[s].Text + "（一个非常非常长的物品名字，用来测试估价单宽度上限和标题折行）");
                break;
            }
            name.Plain = TextUnits.Plain(Join(name.Segs));
        }

        private static void LongTags(List<Line> lines)
        {
            int at = lines.FindIndex(l => l.Key == Sections.Title);
            Line tags = NewLine(Sections.Title, "[机器][容器][工具][电子元件][化学品][食品][武器][违禁品][水][医疗用品][药品][植物][养殖][饮品][材料][杂项][收藏品][科技]", Palette.Cyan);
            lines.Insert(at >= 0 ? at + 1 : 0, tags);
        }

        /// 过夜事件那种长句子，一句一行。
        private static void Events(List<Line> lines, int n)
        {
            string[] samples =
            {
                "日用品过剩：市场上日用品供大于求，今天收购日用品的价格下降，出售价格也随之降低，持续两天。",
                "救济行动：政府发放救济物资，居民对日用品的需求减少，相关商品的成交量明显下滑，持续三天。",
                "非法武器走私：港口查获大批走私武器，武器类商品的价格上涨，警察巡逻的频率也提高了。",
                "电子元件短缺：工厂停工导致电子元件紧缺，收购价上涨三成，维修类订单增加。",
                "暴雨预警：未来两天有强降雨，客流减少，雨具和净水用品的需求上升。"
            };
            int at = Before(lines);
            List<Line> add = new List<Line>();
            for (int i = 0; i < n; i++) add.Add(NewLine(Sections.Info, samples[i % samples.Length], Palette.None));
            lines.InsertRange(at, add);
        }

        /// 英文模组的行：竖线隔开的一串、逗号隔开的百分比、名字里带数字、很长的标签。
        private static void English(List<Line> lines)
        {
            string[] samples =
            {
                "Precision expansion 0 | Upgrades remaining: 4 | Wine storage 6x4",
                "Illegal weapons smuggling -35%, Daily goods relief -40%, Weapon demand +12%",
                "Water tank expansion 0/6",
                "Tier 2 filter 3/5",
                "An extremely long english label that keeps going on: 42",
                "Note: this line is a plain sentence written by another mod, long enough to wrap across the slip."
            };
            int at = Before(lines);
            List<Line> add = new List<Line>();
            for (int i = 0; i < samples.Length; i++) add.Add(NewLine(Sections.Mods, samples[i], Palette.None));
            lines.InsertRange(at, add);
        }

        /// 内容物每种复制 n 份（堆叠数也变一变）。
        private static void MoreContents(ContentsInfo c, int n)
        {
            if (c == null || c.Entries.Count == 0) return;
            List<ContentEntry> all = new List<ContentEntry>(c.Entries);
            c.Entries.Clear();
            for (int k = 0; k < n; k++)
                for (int i = 0; i < all.Count; i++)
                {
                    ContentEntry e = new ContentEntry();
                    e.Item = all[i].Item;
                    e.Name = all[i].Name + " #" + (k + 1);
                    e.RawName = all[i].RawName;
                    e.Count = all[i].Count + k * 7;
                    c.Entries.Add(e);
                }
            c.Total = c.Entries.Count;
            for (int g = 0; g < c.Groups.Count; g++)
            {
                ContentGroup group = c.Groups[g];
                List<ContentEntry> items = new List<ContentEntry>(group.Entries);
                group.Entries.Clear();
                for (int k = 0; k < n; k++) group.Entries.AddRange(items);
                group.Total = group.Entries.Count;
            }
        }

        // ---------------------------------------------------------------- 小工具

        /// 名字、价格和描述之后、页尾之前插入的位置：最后一个不是描述的行后面。
        private static int Before(List<Line> lines)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
                if (lines[i].Key != Sections.Desc) return i + 1;
            return lines.Count;
        }

        private static int LastIndexOf(List<Line> lines, string key)
        {
            for (int i = lines.Count - 1; i >= 0; i--) if (lines[i].Key == key) return i;
            return -1;
        }

        private static Line NewLine(string key, string text, Palette color)
        {
            Line l = new Line();
            Seg s = new Seg();
            s.Text = text;
            s.Color = color;
            l.Segs.Add(s);
            l.Key = key;
            l.Plain = text;
            return l;
        }

        private static List<Line> Copy(List<Line> lines)
        {
            List<Line> copy = new List<Line>(lines.Count);
            for (int i = 0; i < lines.Count; i++) copy.Add(CopyLine(lines[i]));
            return copy;
        }

        private static Line CopyLine(Line l)
        {
            Line c = new Line();
            c.Key = l.Key;
            c.FromHelper = l.FromHelper;
            c.Ord = l.Ord;
            c.Plain = l.Plain;
            for (int i = 0; i < l.Segs.Count; i++) c.Segs.Add(l.Segs[i].With(l.Segs[i].Text));
            return c;
        }

        private static ContentsInfo Copy(ContentsInfo c)
        {
            if (c == null) return null;
            ContentsInfo r = new ContentsInfo();
            r.Entries.AddRange(c.Entries);
            r.Gauges.AddRange(c.Gauges);
            r.Total = c.Total;
            for (int g = 0; g < c.Groups.Count; g++)
            {
                ContentGroup src = c.Groups[g], dst = new ContentGroup();
                dst.Label = src.Label;
                dst.Entries.AddRange(src.Entries);
                dst.Gauges.AddRange(src.Gauges);
                dst.Total = src.Total;
                r.Groups.Add(dst);
            }
            return r;
        }

        private static string Join(List<Seg> segs)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < segs.Count; i++) sb.Append(segs[i].Text);
            return sb.ToString();
        }

        private static string NameOf(GameItem item)
        {
            try { return TextUnits.Plain(item.GetDisplayName(false) ?? "?"); }
            catch { return "?"; }
        }
    }

    internal static partial class Ticket
    {
        static partial void DevBeforeShow(TMP_Text tmp, GameItemElement item, RectTransform parent, RectTransform win)
        {
            Stress.PrepareSamples(item);
            if (!Stress.Pending(item)) return;
            float sy0 = Mathf.Abs(root.localScale.y), sx0 = Mathf.Abs(root.localScale.x);
            Rect screen0 = Panels.ScreenRect(parent, win);
            RunStress(tmp, item, sy0 > 0f ? screen0.height / sy0 : float.MaxValue, sx0 > 0f ? screen0.width / sx0 : float.MaxValue);
        }

        static partial void DevShown(GameItemElement item)
        {
            if (card != null) Shots.Seen(card, SlipName(item));
        }

        /// 压力测试：用当前的 tooltip 窗口把排好的用例逐个建成单子、写日志、丢掉，然后照常显示这件物品。
        private static void RunStress(TMP_Text tmp, GameItemElement item, float room, float roomW)
        {
            RectTransform keepCard = card;
            Vector2 keepSize = cardSize;
            Stress.Run(room, delegate(LayoutResult r, float caseRoom, GameItemElement own)
            {
                try
                {
                    // 压力测试的单子都按完整的建，收起与否不影响分栏和宽度的检查。
                    BuildFlow(tmp, r, own ?? item, true, caseRoom, roomW);
                }
                finally
                {
                    for (int i = slips.Count - 1; i >= 0; i--) if (slips[i].Card == card) slips.RemoveAt(i);
                    if (card != keepCard) Drop(card);
                    card = keepCard;
                }
            });
            card = keepCard;
            cardSize = keepSize;
            // 让下面照常重建（或复用）这件物品自己的单子。
            builtFor = null;
            builtItem = null;
            waitingItem = null;
        }
    }
}
#endif
