using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SectionedTooltips
{
    /// 容器里的同一种物品：显示成一个带数量的格子。
    internal sealed class ContentEntry
    {
        public GameItem Item;
        public string Name = "";
        /// GameItem.name：和显示名不同时，其他模组按这个名字列物品。
        public string RawName = "";
        public int Count;
    }

    /// 机器的电池，或面板里的液体容器：显示为量条，不是格子。
    internal sealed class Gauge
    {
        public GameItem Item;
        public string Name = "";
        public string RawName = "";
        public float Ratio;
        /// 量文本，如 "55 / 100"、"6 / 6 L"。
        public string Amount = "";
        /// 玩家能看到的水质等级（如"基础水"）；未知或不是水时为空。
        public string Note = "";
        public Color Fill;
        /// 电池是电量，其余都是液体。
        public bool Power;
    }

    /// 一个隔间：整个容器，或机器的一个面板（"输入"、"芯片"、"输出"）。
    internal sealed class ContentGroup
    {
        /// 面板自己的标题；普通容器为空。
        public string Label = "";
        public readonly List<ContentEntry> Entries = new List<ContentEntry>();
        /// 这里的电池和液体容器，画成量条而不是格子。
        public readonly List<Gauge> Gauges = new List<Gauge>();
        public int Total;
    }

    /// 容器里装了什么，供内容物面板使用。只算玩家打开容器能看到的：上锁、密封、锁后物品都不算。
    internal sealed class ContentsInfo
    {
        public readonly List<ContentGroup> Groups = new List<ContentGroup>();
        /// 所有分组按种类合并后的列表（经典风格只画一个网格）。
        public readonly List<ContentEntry> Entries = new List<ContentEntry>();
        /// 所有分组的量条。
        public readonly List<Gauge> Gauges = new List<Gauge>();
        public int Total;

        // 不是玩家填的储物面板：模组槽和备注槽。电池面板只用来读量条。
        private static readonly string[] SkippedRoles = { "module", "note" };
        private const string PowerRole = "power";
        private const string PowerTag = "power_source_item";
        private const string EnergyTag = "power_source_item_energy";
        private const string MaxEnergyTag = "power_source_item_max_energy";
        /// 一个面板里液体容器超过这个数（一整架瓶子）就还是画格子。
        private const int MaxLiquidGauges = 3;
        private const int MaxBatteryGauges = 4;

        /// 一个隔间的物品，以及能不能画成量条（机器的面板可以）。
        private sealed class Part
        {
            public string Label = "";
            public List<GameItem> Items;
            public bool Power;
            public bool Gauged;
        }

        /// 物品的各个隔间和里面的物品，按游戏自己的窗口布局：机器一个面板一个隔间，其他容器整体算一个。
        private static List<Part> Compartments(GameItem item)
        {
            if (item == null) return null;
            try
            {
                if (LockHelper.IsCurrentlyLocked(item)) return null;
                PixelWindow window = item.contentWindow;
                if (window == null) return null;
                // 两套锁开锁前都会把窗口标成 isLocked：LOCK_TYPE（保险箱/枪柜，走 Lock）与
                // LOCKED_CONTAINER_TAG（物资箱/安全箱等，走 LockUpContainer）。不能只看
                // IsCurrentlyLocked：它要求物品已拥有（IS_OWNED_TAG），商店货架上还没买的
                // 箱子会被当成“没锁”，我们抢走内容物、又因 ITEM_BEHIND_LOCK_TAG 一件都画不
                // 出来，等于把 Item Details 的密封箱透视封死。
                if (window.isLocked) return null;

                List<Part> list = new List<Part>();
                PixelElement root = window.childElement;
                MachineryPanelRow row = root as MachineryPanelRow;
                // 面板行本身也算储物，所以在这里停止收集，逐个面板读。机器每个面板一个隔间；其他用面板布局的（加了扩容的储物区、一个槽一个面板的工具箱）整体算一个隔间。
                if (row != null)
                {
                    bool machine = item.IsGameItemType("MACHINE");
                    IReadOnlyList<MachineryPanel> panels = row.Panels;
                    if (panels == null) return null;
                    List<GameItem> all = new List<GameItem>();
                    bool powered = false;
                    for (int i = 0; i < panels.Count; i++)
                    {
                        MachineryPanel panel = panels[i];
                        if (panel == null || panel.isHidden || Array.IndexOf(SkippedRoles, panel.role ?? "") >= 0) continue;
                        bool power = panel.role == PowerRole;
                        if (power && !machine) continue;
                        List<GameItem> items = ItemsIn(panel.content, false);
                        if (power) powered = true;
                        if (items.Count == 0) continue;
                        if (machine) list.Add(new Part { Label = LabelOf(panel), Items = items, Power = power, Gauged = true });
                        else all.AddRange(items);
                    }
                    if (all.Count > 0) list.Add(new Part { Items = all });
                    // 有些机器的电池放在单独的槽里，不在 "power" 面板。
                    if (machine && !powered)
                    {
                        GameSlotInventory slot = MachineHelper.GetBatterySlot(item);
                        GameItem battery = slot != null && slot.childItems != null && slot.childItems.Count > 0 ? slot.childItems[0] : null;
                        if (battery != null) list.Insert(0, new Part { Items = new List<GameItem> { battery }, Power = true, Gauged = true });
                    }
                }
                else
                {
                    // 只取储物（可能是几个网格，比如金库）：不包括武器的弹匣槽。
                    List<GameItem> items = ItemsIn(root, true);
                    if (items.Count == 0)
                    {
                        GameGridInventory inv = window.child as GameGridInventory;
                        if (inv != null && inv.childItems != null)
                            for (int i = 0; i < inv.childItems.Count; i++) if (inv.childItems[i] != null) items.Add(inv.childItems[i]);
                    }
                    if (items.Count > 0) list.Add(new Part { Items = items });
                }
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }

        /// element 下面的物品；gridsOnly 时只有全都是网格才返回。
        private static List<GameItem> ItemsIn(PixelElement element, bool gridsOnly)
        {
            List<GameItem> items = new List<GameItem>();
            if (element == null) return items;
            List<GraphNodeStorage> storages = MachineryContainerUtils.Collect(element);
            if (storages == null) return items;
            for (int i = 0; i < storages.Count; i++)
            {
                if (gridsOnly && !(storages[i] is GameGridInventory)) { items.Clear(); return items; }
                GameInventory inv = storages[i] as GameInventory;
                if (inv == null || inv.childItems == null) continue;
                for (int j = 0; j < inv.childItems.Count; j++) if (inv.childItems[j] != null) items.Add(inv.childItems[j]);
            }
            return items;
        }

        /// 面板标题去掉末尾的冒号（"输入：" 变成 "输入"）。
        private static string LabelOf(MachineryPanel panel)
        {
            try
            {
                string s = panel.label != null ? panel.label.GetText() : null;
                return string.IsNullOrEmpty(s) ? "" : TextUnits.Plain(s).Trim().TrimEnd(':', '：').Trim();
            }
            catch { return ""; }
        }

        private static bool Hidden(GameItem child)
        {
            try { return child == null || child.IsTag(LockHelper.ITEM_BEHIND_LOCK_TAG); }
            catch { return true; }
        }

        private static GameItem signedItem;
        private static int signature;
        private static float signedAt = -100f;
        /// 同一个物品的指纹缓存多久（秒）。
        private const float SignatureEvery = 0.25f;

        /// 内容物指纹（哪些物品、各多少个），没有内容物时为 0。每帧都会问，但整仓遍历太贵，所以同一个物品每 0.25 秒才重算一次。
        internal static int Signature(GameItem item)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (ReferenceEquals(item, signedItem) && now - signedAt < SignatureEvery) return signature;
            signedItem = item;
            signedAt = now;
            long t0 = Stats.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            signature = SignatureNow(item);
            if (t0 != 0L) Stats.Fingerprint((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            return signature;
        }

        private static int SignatureNow(GameItem item)
        {
            List<Part> parts = Compartments(item);
            if (parts == null) return 0;
            unchecked
            {
                int h = 17;
                for (int p = 0; p < parts.Count; p++)
                {
                    List<GameItem> items = parts[p].Items;
                    bool gauged = parts[p].Gauged;
                    h = h * 31 + items.Count;
                    for (int i = 0; i < items.Count; i++)
                    {
                        h = (h * 31 + items[i].GetHashCode()) * 31 + Units(items[i]);
                        // 电池在放电、瓶子在灌水只改变量条，不改变文本。
                        if (gauged) h = h * 31 + Level(items[i]);
                    }
                }
                return h;
            }
        }

        private static int Level(GameItem child)
        {
            try
            {
                if (child.IsTag(PowerTag)) return child.GetTagReadonly(EnergyTag).GetInt();
                if (child.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return WaterHelper.GetCurrentCapacityUL(child);
            }
            catch { }
            return 0;
        }

        internal static ContentsInfo Collect(GameItem item)
        {
            long t0 = Stats.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            ContentsInfo r = CollectNow(item);
            if (t0 != 0L) Stats.Read((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, r != null ? r.Total : 0);
            return r;
        }

        private static ContentsInfo CollectNow(GameItem item)
        {
            List<Part> parts = Compartments(item);
            if (parts == null) return null;
            ContentsInfo r = new ContentsInfo();
            Dictionary<string, ContentEntry> merged = new Dictionary<string, ContentEntry>();
            for (int p = 0; p < parts.Count; p++)
            {
                ContentGroup g = new ContentGroup();
                g.Label = parts[p].Label;
                Dictionary<string, ContentEntry> byKind = new Dictionary<string, ContentEntry>();
                List<GameItem> items = parts[p].Items;
                bool liquidGauges = parts[p].Gauged && Liquids(items) <= MaxLiquidGauges;
                // 比如充电器里的电池：机器任何面板里只要有，而且数量不多。
                bool batteryGauges = parts[p].Gauged && Batteries(items) <= MaxBatteryGauges;
                for (int i = 0; i < items.Count; i++)
                {
                    GameItem child = items[i];
                    if (Hidden(child)) continue;
                    string name = "", raw = "", id = "";
                    try { name = child.GetDisplayName(false) ?? ""; raw = child.name ?? ""; id = child.identifier ?? ""; }
                    catch { }
                    string kind = id + "|" + name;
                    int units = Units(child);
                    Gauge gauge = parts[p].Power ? PowerGauge(child) : null;
                    if (gauge == null && liquidGauges) gauge = LiquidGauge(child);
                    if (gauge == null && batteryGauges) gauge = PowerGauge(child);
                    if (gauge != null)
                    {
                        gauge.Item = child;
                        gauge.Name = name;
                        gauge.RawName = raw;
                        g.Gauges.Add(gauge);
                        r.Gauges.Add(gauge);
                    }
                    else Add(g.Entries, byKind, kind, child, name, raw, units);
                    // 画成量条的物品仍留在合并列表里：经典风格的网格和文本去重都要用。
                    Add(r.Entries, merged, kind, child, name, raw, units);
                    g.Total += units;
                    r.Total += units;
                }
                if (g.Total > 0) r.Groups.Add(g);
            }
            return r.Total > 0 ? r : null;
        }

        /// 这个物品代表多少个：一叠 20 个螺丝是 1 个物体装 20 个。
        private static int Units(GameItem child)
        {
            try { return Math.Max(1, child.unitCount); }
            catch { return 1; }
        }

        private static int Liquids(List<GameItem> items)
        {
            return Tagged(items, WaterHelper.LIQUID_CONTAINER_TAG);
        }

        private static int Batteries(List<GameItem> items)
        {
            return Tagged(items, PowerTag);
        }

        private static int Tagged(List<GameItem> items, string tag)
        {
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                try { if (items[i] != null && items[i].IsTag(tag)) n++; }
                catch { }
            }
            return n;
        }

        // 电池用游戏自己的琥珀色；液体用自身颜色，太浅就压暗。
        private static readonly Color PowerFill = new Color32(214, 146, 28, 255);
        private static readonly Color LiquidFallback = new Color32(64, 140, 200, 255);

        private static Gauge PowerGauge(GameItem child)
        {
            try
            {
                if (!child.IsTag(PowerTag)) return null;
                int now = child.GetTagReadonly(EnergyTag).GetInt(), max = child.GetTagReadonly(MaxEnergyTag).GetInt();
                if (max <= 0) return null;
                Gauge g = new Gauge();
                g.Ratio = Mathf.Clamp01((float)now / max);
                g.Amount = now.ToString(CultureInfo.InvariantCulture) + " / " + max.ToString(CultureInfo.InvariantCulture);
                g.Fill = PowerFill;
                g.Power = true;
                return g;
            }
            catch { return null; }
        }

        private static Gauge LiquidGauge(GameItem child)
        {
            try
            {
                if (!child.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return null;
                int now = WaterHelper.GetCurrentCapacityML(child), max = WaterHelper.GetTotalCapacityML(child);
                if (max <= 0) return null;
                Gauge g = new Gauge();
                g.Ratio = Mathf.Clamp01((float)now / max);
                bool liters = max >= 1000;
                g.Amount = Volume(now, liters) + " / " + Volume(max, liters) + (liters ? " L" : " ml");
                // 游戏显示的水质：测过或贴过标签才有等级，之前为空。
                string grade = now > 0 ? WaterFeatureHelper.GetCurrentWaterDisplay(child) : "";
                g.Note = string.IsNullOrEmpty(grade) ? "" : TextUnits.Plain(grade).Trim();
                g.Fill = LiquidColor(child);
                return g;
            }
            catch { return null; }
        }

        private static string Volume(int ml, bool liters)
        {
            return liters ? (ml / 1000f).ToString("0.##", CultureInfo.InvariantCulture) : ml.ToString(CultureInfo.InvariantCulture);
        }

        private static Color LiquidColor(GameItem child)
        {
            Color c;
            string hex = null;
            try { hex = WaterHelper.GetLiquidContainerColor(child); }
            catch { }
            if (string.IsNullOrEmpty(hex) || !ColorUtility.TryParseHtmlString(hex[0] == '#' ? hex : "#" + hex, out c)) return LiquidFallback;
            c.a = 1f;
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return lum > 0.65f ? Color.Lerp(c, Color.black, (lum - 0.65f) * 1.2f + 0.15f) : c;
        }

        private static void Add(List<ContentEntry> list, Dictionary<string, ContentEntry> byKind, string kind, GameItem child, string name, string raw, int units)
        {
            ContentEntry e;
            if (!byKind.TryGetValue(kind, out e))
            {
                e = new ContentEntry();
                e.Item = child;
                e.Name = name;
                e.RawName = raw;
                byKind[kind] = e;
                list.Add(e);
            }
            e.Count += units;
        }
    }
}
