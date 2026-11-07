#if DEVDUMP
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// dev 构建专用：把游戏里**全部**物品的估价单排版结果导出成文本，不用逐个悬停。
    ///
    /// 触发：模组数据目录里有 dumpall.flag（读档后第一次悬停物品时检查一次）。
    /// 产物：模组数据目录的 alldump.txt，view.py 直接读。
    ///
    /// 要拿到原生 tooltip 必须反射调用 private 的 GameItemElement.GetTooltipBasic，
    /// 所以这个文件只在 build.bat 传 dev 时编译（清单里同时声明 reflection-external）。
    /// </summary>
    internal static class DumpAll
    {
        private const string ModId = "amres.sectioned_tooltips";
        private const string Flag = "dumpall.flag";
        private const string Output = "alldump.txt";

        /// 正在导出：这时候 Layout 只记录、不画卡片。
        internal static bool Running;

        private static bool checkedOnce, started;
        private static readonly StringBuilder Text = new StringBuilder(1 << 20);
        private static readonly List<string> Failed = new List<string>();

        /// 读档后第一次排版时调用一次：有 flag 就开始导出。
        internal static void Check()
        {
            if (checkedOnce || started) return;
            checkedOnce = true;
            try
            {
                if (!ModHelper.ModDataFileExists(ModId, Flag))
                {
                    string folder = null;
                    try { folder = ModHelper.GetModDataFolder(ModId); } catch { }
                    SectionedTooltipsMod.Log("全量导出：没找到 " + Flag + "，模组数据目录是 " + (folder ?? "?"));
                    return;
                }
                started = true;
                Run();
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("全量导出失败：" + e.Message);
            }
        }

        private static void Run()
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            int count = 0;
            Running = true;
            try
            {
                System.Reflection.MethodInfo get = AccessTools.Method(typeof(GameItemElement), "GetTooltipBasic", Type.EmptyTypes);
                if (get == null) throw new Exception("找不到 GameItemElement.GetTooltipBasic");

                List<string> ids = DirectoryMaster.GetIdentifierList<GameItem>();
                SectionedTooltipsMod.Log(string.Format("全量导出开始：{0} 个物品", ids != null ? ids.Count : 0));
                if (ids == null) return;

                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    GameItem item = null;
                    try
                    {
                        item = DirectoryMaster.Create<GameItem>(id);
                        if (item == null) { Failed.Add(id + "：创建失败"); continue; }
                        get.Invoke(item, null);   // 我们的 Postfix 会走 Layout，把结果交给 Take
                        count++;
                    }
                    catch (Exception e)
                    {
                        Failed.Add(id + "：" + e.GetType().Name + " " + e.Message);
                    }
                    finally
                    {
                        if (item != null)
                        {
                            try { item.Destroy(); } catch { }
                        }
                    }
                }
            }
            finally
            {
                Running = false;
            }

            try
            {
                Text.Append("### 导出完成：").Append(count).Append(" 件，用时 ")
                    .Append(Math.Round(watch.Elapsed.TotalSeconds, 1)).Append(" 秒\n");
                for (int i = 0; i < Failed.Count && i < 40; i++) Text.Append("### 失败：").Append(Failed[i]).Append('\n');
                ModHelper.WriteModDataFileText(ModId, Output, Text.ToString());
                SectionedTooltipsMod.Log(string.Format("全量导出完成：{0} 件，{1} 件失败，用时 {2:F1} 秒",
                    count, Failed.Count, watch.Elapsed.TotalSeconds));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("全量导出写文件失败：" + e.Message);
            }
        }

        /// 由 Layout.Apply 在算完分节后调用：把这张单子记下来，不画卡片。
        internal static void Take(GameItem item, List<Block> blocks)
        {
            try
            {
                Text.Append("=== ").Append(Name(item)).Append('\n');
                for (int i = 0; i < blocks.Count; i++)
                {
                    Block bl = blocks[i];
                    for (int j = 0; j < bl.Rows.Count; j++)
                    {
                        Row row = bl.Rows[j];
                        Text.Append('\t').Append(bl.Key).Append('\t');
                        if (row.Heading) Text.Append("(标题) ").Append(Flat(Ticket.PlainOf(row.Label)));
                        else if (row.IsPair)
                        {
                            Text.Append(Flat(Ticket.PlainOf(row.Label))).Append(" = ")
                                .Append(Flat(Ticket.PlainOf(row.Value)));
                            if (row.Wide) Text.Append("  [整行]");
                        }
                        else Text.Append(Flat(Ticket.PlainOf(row.Segs)));
                        Text.Append('\n');
                    }
                }
            }
            catch (Exception e)
            {
                Text.Append("### 记录失败：").Append(e.Message).Append('\n');
            }
        }

        private static string Name(GameItem item)
        {
            try { return item != null ? item.GetDisplayName(false) : "?"; }
            catch { return "?"; }
        }

        private static string Flat(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
#endif
