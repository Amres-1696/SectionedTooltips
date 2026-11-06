using System;
using System.Collections.Generic;

namespace SectionedTooltips
{
    // Item Details 区块协议里的两种类型（见它的 sdk，v1）。元组在 C# 5 里写不出语法糖，
    // 但类型本身可以直接写全名——这样编译期就有确定的类型，不用反射，也就不会触发模组扫描器。
    using Runs = IReadOnlyList<ValueTuple<string, string, RenderHandler.ColorPalette, float, int, RenderHandler.ColorPalette, string>>;
    using Fields = IReadOnlyDictionary<string, object>;
    using ReadDelegate = Func<RichTextBuilder, Action<string,
        IReadOnlyList<ValueTuple<string, string, RenderHandler.ColorPalette, float, int, RenderHandler.ColorPalette, string>>,
        IReadOnlyDictionary<string, object>>, bool>;

    /// <summary>
    /// 读 Item Details 的区块协议。它把一次 tooltip 的输出划分成若干区块（water、storage、seed …），
    /// 我们只取“区块 id + 这个区块里每一行的文字”，把它的行归到我们对应的小节，不再靠文本形状去猜。
    /// </summary>
    internal static class ItemDetailsSections
    {
        private const string WatchSlot = "item_details.tooltip_sections.v1.watch";
        private const string ReadSlot = "item_details.tooltip_sections.v1.read";

        private static readonly Action watch = delegate { };

        internal static void Register()
        {
            try
            {
                AppDomain domain = AppDomain.CurrentDomain;
                domain.SetData(WatchSlot, Delegate.Combine(domain.GetData(WatchSlot) as Delegate, watch));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("Item Details 区块监听注册失败：" + e.Message);
            }
        }

        internal static void Unregister()
        {
            try
            {
                AppDomain domain = AppDomain.CurrentDomain;
                domain.SetData(WatchSlot, Delegate.Remove(domain.GetData(WatchSlot) as Delegate, watch));
            }
            catch { }
        }

        /// <summary>
        /// 读这次构建的区块：返回“行文字 → 区块 id”。没装 Item Details、它没启用区块协议，
        /// 或者这行不在任何区块里时返回 null。
        /// </summary>
        internal static Dictionary<string, string> Read(RichTextBuilder builder)
        {
            if (builder == null) return null;
            ReadDelegate read = AppDomain.CurrentDomain.GetData(ReadSlot) as ReadDelegate;
            if (read == null) return null;
            Dictionary<string, string> result = new Dictionary<string, string>();
            try
            {
                bool any = read(builder, delegate(string id, Runs runs, Fields fields)
                {
                    Collect(result, id, runs);
                });
                return any && result.Count > 0 ? result : null;
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("读 Item Details 区块失败，按文本处理：" + e.Message);
                return null;
            }
        }

        private static void Collect(Dictionary<string, string> result, string id, Runs runs)
        {
            if (string.IsNullOrEmpty(id) || runs == null) return;
            System.Text.StringBuilder line = new System.Text.StringBuilder();
            for (int i = 0; i < runs.Count; i++)
            {
                string kind = runs[i].Item1;
                if (kind == "line" || kind == "break")
                {
                    Put(result, id, line);
                    continue;
                }
                string text = runs[i].Item2;
                if (!string.IsNullOrEmpty(text)) line.Append(text);
            }
            Put(result, id, line);
        }

        private static void Put(Dictionary<string, string> result, string id, System.Text.StringBuilder line)
        {
            string plain = TextUnits.Plain(line.ToString()).Trim();
            line.Length = 0;
            if (plain.Length == 0) return;
            // 同一行文字出现在多个区块里时先到先得；我们的行本来也不会重复。
            if (!result.ContainsKey(plain)) result[plain] = id;
        }
    }
}
