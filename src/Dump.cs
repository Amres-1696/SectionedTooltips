using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// 开发用：把排好版的一张估价单落成 JSON，方便在游戏外查看、diff、跑自动检查。
    /// 只有 Modding\tool\dump.flag 存在时才工作，产物写到 Modding\tool\dump\。不发版。
    ///
    /// 记录三样东西：分节和每行的文字（排版决策）、卡片尺寸、每个文字块的最终坐标（用来查重叠和出界）。
    /// </summary>
    internal static class Dump
    {
        private static bool checkedOnce, enabled;
        private static string folder;

        internal static bool On
        {
            get { Check(); return enabled; }
        }

        private static void Check()
        {
            if (checkedOnce) return;
            checkedOnce = true;
            try
            {
                string tool = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..\\Modding\\tool"));
                if (!System.IO.File.Exists(System.IO.Path.Combine(tool, "dump.flag"))) return;
                folder = System.IO.Path.Combine(tool, "dump");
                System.IO.Directory.CreateDirectory(folder);
                enabled = true;
                SectionedTooltipsMod.Log("估价单导出已开启：" + folder);
            }
            catch (Exception e)
            {
                enabled = false;
                SectionedTooltipsMod.Log("估价单导出开启失败：" + e.Message);
            }
        }

        internal static void Write(GameItem item, RectTransform card, float w, float h, List<Block> blocks)
        {
            try
            {
                string name = DisplayName(item);
                if (name.Length == 0 || card == null) return;

                StringBuilder sb = new StringBuilder(8192);
                sb.Append("{\n");
                sb.Append("  \"item\": ").Append(Str(name)).Append(",\n");
                sb.Append("  \"id\": ").Append(Str(Identifier(item))).Append(",\n");
                sb.Append("  \"w\": ").Append(F(w)).Append(",\n");
                sb.Append("  \"h\": ").Append(F(h)).Append(",\n");
                sb.Append("  \"sections\": [");
                bool first = true;
                for (int i = 0; i < blocks.Count; i++)
                {
                    Block bl = blocks[i];
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("\n    {\"key\": ").Append(Str(bl.Key)).Append(", \"rows\": [");
                    for (int j = 0; j < bl.Rows.Count; j++)
                    {
                        Row row = bl.Rows[j];
                        if (j > 0) sb.Append(", ");
                        sb.Append("\n      {\"heading\": ").Append(row.Heading ? "true" : "false");
                        if (row.Wide) sb.Append(", \"wide\": true");
                        if (row.IsPair)
                        {
                            sb.Append(", \"label\": ").Append(Str(Ticket.PlainOf(row.Label).Trim()));
                            sb.Append(", \"value\": ").Append(Str(Ticket.PlainOf(row.Value).Trim()));
                            Seg bar = BarSeg(row.Value);
                            if (bar != null) sb.Append(", \"bar\": ").Append(F(bar.Bar.Ratio));
                        }
                        else sb.Append(", \"text\": ").Append(Str(Ticket.PlainOf(row.Segs).Trim()));
                        sb.Append('}');
                    }
                    sb.Append("]}");
                }
                sb.Append("\n  ],\n");

                sb.Append("  \"texts\": [");
                TextMeshProUGUI[] texts = card.GetComponentsInChildren<TextMeshProUGUI>(true);
                Vector3[] corners = new Vector3[4];
                card.GetWorldCorners(corners);
                float ox = Mathf.Min(corners[0].x, corners[1].x);
                float oy = Mathf.Max(corners[1].y, corners[2].y);
                bool firstText = true;
                for (int i = 0; i < texts.Length; i++)
                {
                    TextMeshProUGUI t = texts[i];
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    RectTransform rt = t.rectTransform;
                    rt.GetWorldCorners(corners);
                    float x = Mathf.Min(corners[0].x, corners[1].x) - ox;
                    float top = oy - Mathf.Max(corners[1].y, corners[2].y);
                    float tw = Mathf.Abs(corners[2].x - corners[0].x);
                    float th = Mathf.Abs(corners[1].y - corners[0].y);
                    if (tw <= 0f || th <= 0f) continue;
                    if (!firstText) sb.Append(',');
                    firstText = false;
                    sb.Append("\n    {\"t\": ").Append(Str(t.text));
                    sb.Append(", \"x\": ").Append(F(x)).Append(", \"y\": ").Append(F(top));
                    sb.Append(", \"w\": ").Append(F(tw)).Append(", \"h\": ").Append(F(th));
                    sb.Append(", \"s\": ").Append(F(t.fontSize)).Append('}');
                }
                sb.Append("\n  ]\n}\n");

                string file = System.IO.Path.Combine(folder, Safe(name) + ".json");
                System.IO.File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("估价单导出失败：" + e.Message);
            }
        }

        private static Seg BarSeg(List<Seg> segs)
        {
            if (segs == null) return null;
            for (int i = 0; i < segs.Count; i++) if (segs[i].Bar != null) return segs[i];
            return null;
        }

        private static string DisplayName(GameItem item)
        {
            try { return (item != null ? item.GetDisplayName(false) : "") ?? ""; }
            catch { return ""; }
        }

        private static string Identifier(GameItem item)
        {
            try { return item != null ? item.identifier : ""; }
            catch { return ""; }
        }

        private static string Safe(string name)
        {
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (ch < 32 || "\\/:*?\"<>|".IndexOf(ch) >= 0) ch = '_';
                sb.Append(ch);
            }
            string s = sb.ToString().Trim();
            return s.Length > 0 ? s : "item";
        }

        private static string F(float v)
        {
            return Math.Round(v, 1).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Str(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\r') continue;
                else if (ch < 32) sb.Append(' ');
                else sb.Append(ch);
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
