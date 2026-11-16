using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 设置面板里示例估价单显示的内容：面板控件当前的值，还没应用。
    internal sealed class SampleLook
    {
        public int Paper;
        public bool NotOwned;
        public bool Icon = true, Prices = true, Description = true, Footer = true, Contents = true;
        public float PriceScale = 2.2f;
        public float Opacity = 1f;
        /// 每个标签组一个下标，指向 Ticket.TagColorNames；默认时为空。
        public int[] TagColors;
        public bool TagWash = true;
    }

    /// 设置面板里同一台假想的机器，画成经典风格：游戏的深色 tooltip，文字分组放在“▌ 小节”条下面，带树形连线，数值靠右，和 Layout.Render 写出来的一样。
    internal static class ClassicSample
    {
        private static readonly Color Back = new Color32(41, 34, 52, 255);
        private static readonly Color Frame = new Color32(155, 74, 61, 255);

        internal static Vector2 Draw(RectTransform parent, TMP_Text src, float font, float x, float yTop, SampleLook look, bool tree, int maxChars)
        {
            RectTransform card = Ui.NewRect(parent, "classic");
            float line = Mathf.Round(Mathf.Max(8f, font * 1.3f));
            float pad = Mathf.Round(line * 0.5f), frame = Mathf.Max(2f, Mathf.Round(line / 8f));
            Image edge = Ui.NewImage(card, Frame);
            Image back = Ui.NewImage(card, Back);
            Color text = Overlay.ToColor(Palette.LightGray, Palette.LightGray);
            Color dim = Overlay.ToColor(Palette.DarkGray, Palette.DarkGray);

            List<Row> value = new List<Row>();
            value.Add(Ticket.Pair(SectionedTooltipsMod.L("sample_price"), "1478", Palette.Turquoise));
            value.Add(Ticket.Pair(SectionedTooltipsMod.L("sample_buy"), "225", Palette.Turquoise));
            List<Row> machine = new List<Row>();
            machine.Add(Ticket.Pair(SectionedTooltipsMod.L("sample_output"), SectionedTooltipsMod.L("sample_output_value"), Palette.Turquoise));
            machine.Add(Ticket.Pair(SectionedTooltipsMod.L("sample_power"), SectionedTooltipsMod.L("sample_power_value"), Palette.Green));

            // 内容宽度：最宽的那对加上树形符号和间距，不超过宽度设置。
            TextMeshProUGUI probe = Ui.NewText(card, src, font, "", Color.clear, 0f);
            float glyph = Measure(probe, "├ "), widest = Measure(probe, "<b>" + SectionedTooltipsMod.L("sample_name") + "</b>");
            List<Row> all = new List<Row>(value);
            all.AddRange(machine);
            for (int i = 0; i < all.Count; i++)
                widest = Mathf.Max(widest, glyph + Measure(probe, all[i].Label[0].Text) + font + Measure(probe, all[i].Value[0].Text));
            Object.Destroy(probe.gameObject);
            float c = Mathf.Max(widest, Mathf.Min(maxChars * font * 0.5f, widest * 1.25f));

            float y = pad + frame, left = pad + frame;
            float box = 2f * line;
            float tx = left;
            if (look.Icon)
            {
                Ui.Place(Ui.NewImage(card, new Color(1f, 1f, 1f, 0.08f)), left, y, box - 2f, box - 2f);
                tx += box + font * 0.5f;
            }
            TextMeshProUGUI n = Ui.NewText(card, src, font, "<b>" + SectionedTooltipsMod.L("sample_name") + "</b>", text, 0f);
            Ui.Move(n.rectTransform, tx, y);
            TextMeshProUGUI tags = Ui.NewText(card, src, font, SectionedTooltipsMod.L("sample_tags"), Overlay.ToColor(Palette.Cyan, Palette.Cyan), 0f);
            Ui.Move(tags.rectTransform, tx, y + line);
            y += 2f * line;

            y = Section(card, src, font, line, left, c, y, Sections.Value, value, tree, text, dim);
            y = Section(card, src, font, line, left, c, y, Sections.Machine, machine, tree, text, dim);

            if (look.Description)
            {
                Ui.Place(Ui.NewImage(card, dim), left, y + Mathf.Round(line / 2f), c, 1f);
                y += line;
                TextMeshProUGUI d = Ui.NewText(card, src, font, "<i>" + SectionedTooltipsMod.L("sample_desc") + "</i>", Overlay.ToColor(Palette.Gray, Palette.Gray), c);
                Ui.Move(d.rectTransform, left, y);
                y += d.rectTransform.sizeDelta.y;
            }

            float w = Mathf.Round(c + 2f * (pad + frame)), h = Mathf.Round(y + pad + frame);
            Ui.Place(edge, 0f, 0f, w, h);
            Ui.Place(back, frame, frame, w - 2f * frame, h - 2f * frame);
            Ui.Place(card, x, yTop, w, h);
            return new Vector2(w, h);
        }

        private static float Section(RectTransform card, TMP_Text src, float font, float line, float left, float c, float y, string key, List<Row> rows, bool tree, Color text, Color dim)
        {
            Sections.Style st = Sections.StyleOf(key);
            Ui.Place(Ui.NewImage(card, Overlay.ToColor(Layout.HeaderBg, Layout.HeaderBg)), left, y + 1f, c, line - 2f);
            TextMeshProUGUI head = Ui.NewText(card, src, font,
                "<color=#" + ColorUtility.ToHtmlStringRGB(Overlay.ToColor(st.Accent, st.Accent)) + ">▌</color><b> " + Sections.TitleOf(key) + "</b>",
                Overlay.ToColor(st.Text, st.Text), 0f);
            Ui.Move(head.rectTransform, left, y);
            y += line;
            for (int i = 0; i < rows.Count; i++)
            {
                string g = !tree ? "" : i == rows.Count - 1 ? "└ " : "├ ";
                TextMeshProUGUI l = Ui.NewText(card, src, font,
                    "<color=#" + ColorUtility.ToHtmlStringRGB(dim) + ">" + g + "</color>" + rows[i].Label[0].Text, text, 0f);
                Ui.Move(l.rectTransform, left, y);
                TextMeshProUGUI v = Ui.NewText(card, src, font, rows[i].Value[0].Text, Overlay.ToColor(rows[i].Value[0].Color, Palette.LightGray), 0f);
                Ui.Move(v.rectTransform, left + c - v.rectTransform.sizeDelta.x, y);
                y += line;
            }
            return y;
        }

        private static float Measure(TextMeshProUGUI probe, string rich)
        {
            return probe.GetPreferredValues(rich).x;
        }
    }

    internal static partial class Ticket
    {
        /// 一台假想机器的估价单，用和真实估价单一样的部件和颜色画在 parent 的 (x, yTop)，正文字号 font。给设置面板的预览用。返回尺寸。
        internal static Vector2 Sample(RectTransform parent, TMP_Text src, float font, float x, float yTop, SampleLook look)
        {
            Ctx c = new Ctx();
            c.M = new Panels.Metrics();
            c.M.Src = src;
            c.M.Font = font;
            c.M.Line = Mathf.Round(Mathf.Max(8f, font * 1.3f));
            c.Src = src;
            c.Card = Ui.NewRect(parent, "sample");
            if (look.Opacity < 1f) c.Card.gameObject.AddComponent<CanvasGroup>().alpha = look.Opacity;
            c.U = Mathf.Max(1f, Mathf.Round(c.M.Line / 12f));
            c.Pad = Mathf.Round(c.M.Line * 0.6f);
            c.Gap = Mathf.Max(2f, Mathf.Round(c.M.Line * 0.25f));
            c.F = font;
            c.PriceScale = look.PriceScale;
            c.TagColors = look.TagColors;
            c.TagWash = look.TagWash;
            c.Scratch = Ui.NewText(c.Card, src, c.F, "", Color.clear, 0f);
            UsePaper(look.Paper);

            Image shadow = Ui.NewImage(c.Card, Shadow);
            Image edge = Ui.NewImage(c.Card, PaperEdge);
            Image paper = Ui.NewImage(c.Card, Paper);
            c.Q = Ui.NewQuads(c.Card);

            // 一张单子能显示的东西全放上去：多种颜色的标签、赃物标签、违禁品标签和红边、价格、一节内容、电池量条、描述和页脚。
            string name = SectionedTooltipsMod.L("sample_name");
            string price = "1478";
            string stolen = SectionedTooltipsMod.L("sample_stolen");
            string contraband = SectionedTooltipsMod.L("sample_contraband");
            List<Chip> kinds = new List<Chip>();
            kinds.Add(SampleChip("MACHINE", "sample_tags"));
            kinds.Add(SampleChip("HOUSEHOLD_GOOD", "sample_tag_household"));
            kinds.Add(SampleChip("CONTRABAND", "sample_tag_contraband"));
            Parts p = new Parts();
            p.Price = Pair(SectionedTooltipsMod.L("sample_price"), price, Palette.None);
            if (look.Prices)
            {
                p.Prices.Add(Pair(SectionedTooltipsMod.L("sample_base"), "1520", Palette.None));
                p.Prices.Add(Pair(SectionedTooltipsMod.L("sample_buy"), "225", Palette.Turquoise));
            }
            Block machine = new Block();
            machine.Key = Sections.Machine;
            machine.Rows.Add(Pair(SectionedTooltipsMod.L("sample_output"), SectionedTooltipsMod.L("sample_output_value"), Palette.Turquoise));
            machine.Rows.Add(Pair(SectionedTooltipsMod.L("sample_power"), SectionedTooltipsMod.L("sample_power_value"), Palette.Green));
            LayoutResult r = new LayoutResult();
            r.Blocks = new List<Block> { machine };
            // 槽里的一块电池，和内容物显示的一样。
            if (look.Contents)
            {
                Gauge battery = new Gauge();
                battery.Name = SectionedTooltipsMod.L("sample_battery");
                battery.Ratio = 0.66f;
                battery.Amount = "66 / 100";
                battery.Fill = new Color32(214, 146, 28, 255);
                battery.Power = true;
                ContentGroup group = new ContentGroup();
                group.Gauges.Add(battery);
                group.Total = 1;
                r.Contents = new ContentsInfo();
                r.Contents.Groups.Add(group);
                r.Contents.Gauges.Add(battery);
                r.Contents.Total = 1;
            }

            // 宽度按 Width 对真实单子的算法：最宽且不能折的行，范围 11 到 17 行。
            float iconW = look.Icon ? IconBox(c) + c.Gap * 1.5f : 0f;
            float need = c.Measure("<b>" + name + "</b>", c.F * 1.15f) + iconW;
            need = Mathf.Max(need, ChipsWidth(c, kinds) + iconW);
            List<string> flags = new List<string> { stolen, contraband };
            need = Mathf.Max(need, FlagsWidth(c, flags) + iconW);
            need = Mathf.Max(need, c.Measure(SectionedTooltipsMod.L("sample_price"), c.F * 0.75f) + c.Gap
                + c.Measure("<b>" + price + "</b>", PriceSize(c)));
            need = Mathf.Max(need, c.Measure(Sections.TitleOf(Sections.Machine), c.F * 0.85f) + 4f * c.M.Line);
            c.LabelCol = LabelColumn(c, r);
            need = Mathf.Max(need, GridWidth(c, r));
            // 和 Width 一样多一个像素，免得取整把最后一个字挤出去。
            c.W = Mathf.Round(Mathf.Clamp(need + c.U + 2f * c.Pad, c.M.Line * 11f, c.M.Line * 17f));

            float y = c.Pad * 0.5f;
            Dashes(c, c.Pad * 0.5f, y, c.W - c.Pad, 3f * c.U, 2f * c.U, c.U, PaperEdge);
            y = c.Pad;

            // 标题：图标框、名字、未拥有说明、分类标签和赃物标签。
            float tx = c.Pad, box = IconBox(c);
            if (look.Icon)
            {
                Solid(c, PaperShade, c.Pad, y, box, box);
                // 用淡墨画一台小机器，代替物品的 sprite。
                float u = Mathf.Max(1f, Mathf.Round(box / 10f));
                Solid(c, InkSoft, c.Pad + 2f * u, y + 2f * u, box - 4f * u, box - 4f * u);
                Solid(c, PaperShade, c.Pad + 3f * u, y + 3f * u, box - 6f * u, 2f * u);
                Solid(c, Paper, c.Pad + 3f * u, y + box - 4f * u, 2f * u, u);
                tx += box + c.Gap * 1.5f;
            }
            float wrap = c.W - c.Pad - tx, ty = y;
            TextMeshProUGUI n = Text(c, c.F * 1.15f, "<b>" + name + "</b>", Ink, wrap);
            Ui.Move(n.rectTransform, tx, ty);
            ty += n.rectTransform.sizeDelta.y;
            if (look.NotOwned)
            {
                TextMeshProUGUI t = Text(c, c.F * 0.8f, "<cspace=0.2em>" + SectionedTooltipsMod.L("sample_not_owned") + "</cspace>", InkSoft, wrap);
                Ui.Move(t.rectTransform, tx, ty);
                ty += t.rectTransform.sizeDelta.y;
            }
            ty = DrawChips(c, kinds, tx, ty, wrap);
            ty = FlagTags(c, flags, tx, ty, wrap);
            y = Mathf.Max(y + (look.Icon ? box : 0f), ty) + c.Gap * 1.5f;

            // 大字价格、价格明细和各节，和真实单子用同一套代码。
            y = Price(c, p, y);
            y = Section(c, machine, y);
            if (r.Contents != null) y = Contents(c, r.Contents, y);

            if (look.Description)
            {
                y += c.Gap * 2f;
                TextMeshProUGUI d = Text(c, c.F * 0.8f, "<i>" + SectionedTooltipsMod.L("sample_desc") + "</i>", InkSoft, c.Inner);
                Ui.Move(d.rectTransform, c.Pad, y);
                y += d.rectTransform.sizeDelta.y;
            }
            y = look.Footer ? Footer(c, null, y) : y + c.Pad;
            Dashes(c, c.Pad * 0.5f, y - c.Pad * 0.5f, c.W - c.Pad, 3f * c.U, 2f * c.U, c.U, PaperEdge);

            float h = Mathf.Round(y);
            ContrabandEdge(c, h);
            Ui.Place(shadow, 2f * c.U, 2f * c.U, c.W, h);
            Ui.Place(edge, 0f, 0f, c.W, h);
            Ui.Place(paper, c.U, c.U, c.W - 2f * c.U, h - 2f * c.U);
            Ui.Place(c.Q, 0f, 0f, c.W, h);
            Ui.Place(c.Card, x, yTop, c.W, h);
            Object.Destroy(c.Scratch.gameObject);
            return new Vector2(c.W, h);
        }

        /// 按游戏叫的名字取一个分类标签；游戏说不上来就用我们自己的文本。
        private static Chip SampleChip(string type, string fallbackKey)
        {
            string name = null;
            try { name = TypeHelper.GetTypeDisplayName(type); }
            catch { }
            name = string.IsNullOrEmpty(name) ? "" : TextUnits.Plain(name).Trim();
            if (name.Length == 0) name = SectionedTooltipsMod.L(fallbackKey).Trim('[', ']', ' ');
            return new Chip(type, name);
        }

        /// 一种纸的颜色和它的边色，给面板纸色列表旁的色块用。
        internal static void PaperSwatch(int index, out Color paper, out Color edge)
        {
            if (index < 0 || index >= Papers.Length) index = 0;
            paper = Papers[index][0];
            edge = Papers[index][2];
        }

        /// 给设置示例用的“标签 …… 数值”行。
        internal static Row Pair(string label, string value, Palette color)
        {
            Row r = new Row();
            r.Label = new List<Seg> { new Seg { Text = label } };
            r.Value = new List<Seg> { new Seg { Text = value, Color = color } };
            return r;
        }
    }
}
