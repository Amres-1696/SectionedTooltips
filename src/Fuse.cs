using System;
using System.Collections.Generic;

namespace SectionedTooltips
{
    /// <summary>
    /// 连续失败到上限就把这项功能停掉：坏掉的逻辑不该每帧、每次悬停都再抛一遍。
    /// 只在本次会话有效，成功一次就清零；停用后不自动恢复，重启游戏才重来。
    /// </summary>
    internal static class Fuse
    {
        private const int Limit = 3;
        private static readonly Dictionary<string, int> failures = new Dictionary<string, int>();
        private static readonly HashSet<string> open = new HashSet<string>();

        /// <summary>这一项是否已经停用。</summary>
        internal static bool IsOpen(string key)
        {
            return open.Count > 0 && open.Contains(key);
        }

        internal static void Ok(string key)
        {
            if (failures.Count > 0) failures.Remove(key);
        }

        internal static void Fail(string key, Exception e)
        {
            int n;
            failures.TryGetValue(key, out n);
            failures[key] = ++n;
            if (n < Limit)
            {
                SectionedTooltipsMod.Log(string.Format("{0}第 {1} 次失败（连续 {2} 次停用）：{3}", key, n, Limit, e.Message));
                return;
            }
            if (!open.Add(key)) return;
            SectionedTooltipsMod.Error(string.Format("{0}连续失败 {1} 次，本次会话停用：{2}", key, n, e));
        }
    }
}
