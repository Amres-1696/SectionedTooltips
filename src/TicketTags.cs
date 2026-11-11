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
    /// 名字下面的分类标签：分组、每组的颜色、画成小标签。
    internal static partial class Ticket
    {
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
            string plain = row.Plain.Trim();
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
    }
}
