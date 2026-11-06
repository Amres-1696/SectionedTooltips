using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SectionedTooltips
{
    /// <summary>
    /// 读 Item Details 的区块协议。它把一次 tooltip 的输出划分成若干个区块（water、storage、seed、
    /// value …），我们只取“区块 id + 这个区块里每一行的文字”，用来把它的行归到我们对应的小节，
    /// 不再靠文本形状去猜。
    ///
    /// 不引用它的 DLL：槽位里放的是 delegate，参数类型里带着元组，编译期写不出来，
    /// 所以用一个泛型收集器在运行时闭合，再用 DynamicInvoke 调过去。协议见它的 sdk（v1）。
    /// </summary>
    internal static class ItemDetailsSections
    {
        private const string WatchSlot = "item_details.tooltip_sections.v1.watch";
        private const string ReadSlot = "item_details.tooltip_sections.v1.read";

        private static readonly Action watch = delegate { };
        private static bool reported;

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
        /// 读这次构建的区块：返回“行文字 → 区块 id”。没装 Item Details、它没启用区块协议、
        /// 或者这行不在任何区块里时返回 null。
        /// </summary>
        internal static Dictionary<string, string> Read(RichTextBuilder builder)
        {
            if (builder == null) return null;
            Delegate read = AppDomain.CurrentDomain.GetData(ReadSlot) as Delegate;
            if (read == null) return null;
            try
            {
                MethodInfo invoke = read.GetType().GetMethod("Invoke");
                if (invoke == null) return null;
                ParameterInfo[] ps = invoke.GetParameters();
                if (ps.Length != 2) return null;
                Type callback = ps[1].ParameterType;
                Type[] args = callback.GetGenericArguments();
                if (args.Length != 3) return null;

                Type collectorType = typeof(Collector<,>).MakeGenericType(args[1], args[2]);
                object collector = Activator.CreateInstance(collectorType);
                Delegate sink = Delegate.CreateDelegate(callback, collector, collectorType.GetMethod("Add"));
                object[] call = new object[] { builder, sink };
                if (!(bool)invoke.Invoke(read, call)) return null;
                return ((ICollector)collector).Result;
            }
            catch (Exception e)
            {
                if (!reported)
                {
                    reported = true;
                    SectionedTooltipsMod.Log("读 Item Details 区块失败，按文本处理：" + e.Message);
                }
                return null;
            }
        }

        private interface ICollector
        {
            Dictionary<string, string> Result { get; }
        }

        /// <summary>
        /// 运行时闭合的回调：参数类型是它的私有元组，只能当作 IEnumerable 逐项读 Item1（种类）和
        /// Item2（文字）。
        /// </summary>
        internal sealed class Collector<TRuns, TFields> : ICollector
        {
            private readonly Dictionary<string, string> result = new Dictionary<string, string>();
            private FieldInfo kindField, textField;

            Dictionary<string, string> ICollector.Result { get { return result; } }

            internal void Add(string id, TRuns runs, TFields fields)
            {
                if (id == null || id.Length == 0) return;
                IEnumerable list = runs as IEnumerable;
                if (list == null) return;
                StringBuilder line = new StringBuilder();
                foreach (object run in list)
                {
                    if (run == null) continue;
                    if (kindField == null)
                    {
                        Type t = run.GetType();
                        kindField = t.GetField("Item1");
                        textField = t.GetField("Item2");
                        if (kindField == null || textField == null) return;
                    }
                    string kind = kindField.GetValue(run) as string;
                    if (kind == "line" || kind == "break")
                    {
                        Put(id, line);
                        continue;
                    }
                    string text = textField.GetValue(run) as string;
                    if (!string.IsNullOrEmpty(text)) line.Append(text);
                }
                Put(id, line);
            }

            private void Put(string id, StringBuilder line)
            {
                string plain = TextUnits.Plain(line.ToString()).Trim();
                line.Length = 0;
                if (plain.Length == 0) return;
                // 同一行文字出现在多个区块里时，先到先得；我们的行本来也不会重复。
                if (!result.ContainsKey(plain)) result[plain] = id;
            }
        }
    }
}
