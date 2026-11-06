using System;
using System.Collections.Generic;
using System.Text;

namespace SectionedTooltips
{
    // Item Details 区块协议里的两种类型（见它的 sdk，v1）。元组在 C# 5 里写不出语法糖，
    // 但类型本身可以直接写全名——这样编译期就有确定的类型，不用反射，也就不会触发模组扫描器。
    using Runs = IReadOnlyList<ValueTuple<string, string, RenderHandler.ColorPalette, float, int, RenderHandler.ColorPalette, string>>;
    using Fields = IReadOnlyDictionary<string, object>;
    using RowsList = IReadOnlyList<ValueTuple<string, string, string, string>>;
    using RowDataList = IReadOnlyList<ValueTuple<int, string, object>>;
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

        /// <summary>区块里的一行成分：名称、用量、百分比（Item Details 的 rows 字段）。</summary>
        internal sealed class ComponentRow
        {
            internal string Name = "";
            internal string Amount = "";
            internal string Percent = "";
        }

        /// <summary>这次构建读到的东西：每行属于哪个区块、可选归属组，以及区块给出的成分行。</summary>
        internal sealed class Result
        {
            internal readonly Dictionary<string, string> LineToSection = new Dictionary<string, string>();
            internal readonly Dictionary<string, string> LineToGroup = new Dictionary<string, string>();
            internal readonly List<ComponentRow> Components = new List<ComponentRow>();
        }

        /// <summary>
        /// 读这次构建的区块。没装 Item Details、它没启用区块协议，或者什么都读不到时返回 null。
        /// </summary>
        internal static Result Read(RichTextBuilder builder)
        {
            if (builder == null) return null;
            ReadDelegate read = AppDomain.CurrentDomain.GetData(ReadSlot) as ReadDelegate;
            if (read == null) return null;
            Result result = new Result();
            try
            {
                bool debug = Settings.Debug != null && Settings.Debug.Value;
                bool any = read(builder, delegate(string id, Runs runs, Fields fields)
                {
                    Collect(result, id, runs, fields);
                    CollectRows(result, fields);
                    if (debug) Dump(id, runs, fields);
                });
                return any && (result.LineToSection.Count > 0 || result.Components.Count > 0) ? result : null;
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("读 Item Details 区块失败，按文本处理：" + e.Message);
                return null;
            }
        }

        /// <summary>
        /// rows 字段：IReadOnlyList<(string Kind, string Name, string Amount, string Percent)>。
        /// 这是它算好的成分行（名称/用量/百分比），不用再从文本里反推。
        /// </summary>
        private static void CollectRows(Result result, Fields fields)
        {
            if (fields == null) return;
            object value;
            if (!fields.TryGetValue("rows", out value)) return;
            RowsList rows = value as RowsList;
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                // component 是成分行，fold-hint 是“按Ctrl展开杂质”那一行，都属于这张表。
                string name = rows[i].Item2;
                if (string.IsNullOrEmpty(name)) continue;
                ComponentRow row = new ComponentRow();
                row.Name = name.Trim();
                row.Amount = (rows[i].Item3 ?? "").Trim();
                row.Percent = (rows[i].Item4 ?? "").Trim();
                if (row.Name.Length > 0) result.Components.Add(row);
            }
        }

        /// 调试用：把这个区块的每一行、以及它带的字段名写进日志。
        private static void Dump(string id, Runs runs, Fields fields)
        {
            if (runs != null) DumpRuns(id, runs);
            if (fields == null) return;
            StringBuilder keys = new StringBuilder();
            foreach (KeyValuePair<string, object> kv in fields)
            {
                if (kv.Key == null) continue;
                keys.Append(kv.Key);
                System.Collections.ICollection set = kv.Value as System.Collections.ICollection;
                if (set != null) keys.Append('(').Append(set.Count).Append(')');
                keys.Append(' ');
            }
            if (keys.Length > 0) SectionedTooltipsMod.Log("区块字段 " + id + "：" + keys.ToString().Trim());

            // rowData：(行号, 数据键, 值)。用来判断这行的结构化数据里有没有文本。
            object raw;
            if (fields.TryGetValue("rowData", out raw))
            {
                RowDataList data = raw as RowDataList;
                if (data != null)
                {
                    StringBuilder text = new StringBuilder();
                    for (int i = 0; i < data.Count && i < 6; i++)
                    {
                        text.Append('[').Append(data[i].Item1).Append(' ').Append(data[i].Item2).Append(" = ");
                        object v = data[i].Item3;
                        string shown = v == null ? "null" : v.ToString();
                        if (shown != null && shown.Length > 40) shown = shown.Substring(0, 40) + "…";
                        text.Append(shown).Append("] ");
                    }
                    SectionedTooltipsMod.Log("  rowData " + id + "：" + text.ToString().Trim());
                }
            }
            if (runs != null) SectionedTooltipsMod.Log("  runs " + id + "：" + runs.Count);
        }

        private static void DumpRuns(string id, Runs runs)
        {
            if (runs == null) return;
            StringBuilder line = new StringBuilder();
            StringBuilder all = new StringBuilder();
            for (int i = 0; i < runs.Count; i++)
            {
                string kind = runs[i].Item1, text = runs[i].Item2;
                if (!string.IsNullOrEmpty(text)) line.Append(text.Replace('\n', ' ').Replace('\r', ' '));
                if (kind == "line" || kind == "break")
                {
                    if (line.Length > 0) all.Append(" | ").Append(line.ToString());
                    line.Length = 0;
                }
            }
            if (line.Length > 0) all.Append(" | ").Append(line.ToString());
            string text2 = TextUnits.Plain(all.ToString());
            if (text2.Length > 200) text2 = text2.Substring(0, 200) + "…";
            SectionedTooltipsMod.Log("区块 " + id + "：" + text2.Trim());
        }

        private static void Collect(Result result, string id, Runs runs, Fields fields)
        {
            if (string.IsNullOrEmpty(id) || runs == null) return;
            string group = GroupOf(fields);
            System.Text.StringBuilder line = new System.Text.StringBuilder();
            for (int i = 0; i < runs.Count; i++)
            {
                string kind = runs[i].Item1, text = runs[i].Item2;
                // bar / inline-bar 也是整行：文本 + 条 + 后缀，收完就断行。
                if (kind == "bar" || kind == "inline-bar")
                {
                    if (!string.IsNullOrEmpty(text)) line.Append(text);
                    string tail = runs[i].Item7;
                    if (!string.IsNullOrEmpty(tail)) line.Append(tail);
                    Put(result, id, line, group);
                    continue;
                }
                // kind == "line"：整个 run 就是一行，文本在它自己身上，要先收再断行。
                if (string.IsNullOrEmpty(text))
                {
                    if (kind == "line" || kind == "break") Put(result, id, line, group);
                    continue;
                }
                // 一个 run 里可能塞了整块多行文字（水质成分表就是），按换行再切一次。
                for (int k = 0; k < text.Length; k++)
                {
                    char ch = text[k];
                    if (ch == '\n' || ch == '\r') { Put(result, id, line, group); continue; }
                    line.Append(ch);
                }
                if (kind == "line") Put(result, id, line, group);
            }
            Put(result, id, line, group);
        }

        /// <summary>可选字段；旧版不提供、或类型不符时保持原有归类。</summary>
        private static string GroupOf(Fields fields)
        {
            if (fields == null) return "";
            object value;
            if (!fields.TryGetValue("group", out value)) return "";
            return value as string ?? "";
        }

        private static void Put(Result result, string id, System.Text.StringBuilder line, string group)
        {
            string plain = TextUnits.Plain(line.ToString()).Trim();
            line.Length = 0;
            if (plain.Length == 0) return;
            // 同一行文字出现在多个区块里时先到先得；我们的行本来也不会重复。
            if (!result.LineToSection.ContainsKey(plain))
            {
                result.LineToSection[plain] = id;
                if (!string.IsNullOrEmpty(group)) result.LineToGroup[plain] = group;
            }
        }
    }
}
