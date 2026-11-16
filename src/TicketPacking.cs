using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SectionedTooltips
{
    /// 装箱单：容器里的物品不画进估价单，而是另一张小票用订书钉订在估价单旁边，列出名字和数量。
    /// 估价单本身不会因为东西多而变高；电池、水箱这类量条是读数，仍留在估价单里。
    internal static partial class Ticket
    {
        /// 一张建好的装箱单。它是卡片的子物体，跟卡片一起缓存（Built.Pack 和这里的 pack 指向同一个对象）。尺寸都是卡片单位。
        private sealed class PackSlip
        {
            public RectTransform Root, Staple;
            /// 宽、高、顶边离卡片顶的距离、卡片的宽。
            public float W, H, Top, CardW;
            /// 压住卡片边框的那一截。
            public float Overlap;
            /// 伸出卡片外的宽度。
            public float Out { get { return W - Overlap; } }
        }

        /// 当前估价单的装箱单；没有时为 null。
        private static PackSlip pack;

        private sealed class PackLine
        {
            public string Label;
            public ContentEntry Entry;
        }


        /// 小票的纸：比估价单的纸更白一点，看得出是另一张。
        private static Color SlipPaper()
        {
            return Color.Lerp(Paper, Color.white, darkPaper ? 0.06f : 0.35f);
        }

        /// 建装箱单，挂在卡片 card 下面；cardW 是卡片的宽，top 是价格下面正文开始的高度（都是卡片单位）。没有物品时不建。
        private static void Packing(Ctx c, ContentsInfo contents, float cardW, float top)
        {
            pack = null;
            if (contents == null) return;
            // 只列格子里的物品：机器的电池、液体容器画成量条留在单子里，不再进装箱单（只有它们时不建装箱单）。
            int pieces = 0;
            for (int g = 0; g < contents.Groups.Count; g++)
                for (int i = 0; i < contents.Groups[g].Entries.Count; i++) pieces += contents.Groups[g].Entries[i].Count;
            if (pieces == 0) return;
            float size = RowSize(c), small = NoteSize(c), lh = LineH(c);
            float pad = Mathf.Round(c.Pad * 0.8f), gap = c.Gap;

            // 要列的行：机器的面板标题（“输入”“输出”）单独一行，下面是它的物品。只有一个分组时不写标题。
            int titled = 0;
            for (int g = 0; g < contents.Groups.Count; g++) if (contents.Groups[g].Entries.Count > 0 && contents.Groups[g].Label.Length > 0) titled++;
            bool labels = titled > 0 && contents.Groups.Count > 1;
            List<PackLine> lines = new List<PackLine>();
            for (int g = 0; g < contents.Groups.Count; g++)
            {
                ContentGroup group = contents.Groups[g];
                if (group.Entries.Count == 0) continue;
                if (labels && group.Label.Length > 0) lines.Add(new PackLine { Label = group.Label });
                for (int i = 0; i < group.Entries.Count; i++) lines.Add(new PackLine { Entry = group.Entries[i] });
            }
            // 行数上限：设置的档数，以及窗口放得下多少行（标题、虚线、“…另有”各算一行）。
            int max = Settings.MaxContentRows * PackKindsPerStep, moreKinds = 0;
            float fit = c.Room - top - 2f * pad - 3f * (lh + c.U);
            if (fit < float.MaxValue / 2f) max = Mathf.Max(3, Mathf.Min(max, Mathf.FloorToInt(fit / (lh + c.U))));
            if (lines.Count > max)
            {
                for (int i = max - 1; i < lines.Count; i++) if (lines[i].Entry != null) moreKinds++;
                lines.RemoveRange(max - 1, lines.Count - (max - 1));
                // 最后一行别停在一个空的面板标题上。
                if (lines.Count > 0 && lines[lines.Count - 1].Entry == null) lines.RemoveAt(lines.Count - 1);
            }

            string title = "<b>" + Tint(string.Format(SectionedTooltipsMod.L("packing_title"), pieces), Ink) + "</b>";
            // 储藏区扩建后有几页：只列当前页，标题右边写“第 2/5 页”。
            string page = contents.Pages > 1 ? Tint(string.Format(SectionedTooltipsMod.L("packing_page"), contents.Page, contents.Pages), InkSoft) : "";
            string more = moreKinds > 0 ? Tint(string.Format(SectionedTooltipsMod.L("packing_more"), moreKinds), InkSoft) : "";

            // 宽度：最长的一行，夹在 7 到 13 个字宽之间；放不下的名字截短加“…”。
            float minW = Mathf.Round(c.M.Line * 7f), maxW = Mathf.Round(c.M.Line * 13f);
            float need = c.Measure(title, size) + (page.Length > 0 ? 2f * gap + c.Measure(page, small) : 0f);
            if (more.Length > 0) need = Mathf.Max(need, c.Measure(more, small));
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Entry == null) { need = Mathf.Max(need, c.Measure(Tint(lines[i].Label, InkSoft), small)); continue; }
                need = Mathf.Max(need, lh + gap + c.Measure(NameRich(lines[i].Entry.Name), size) + 2f * gap + c.Measure(CountRich(lines[i].Entry), size));
            }
            float w = Mathf.Clamp(Mathf.Ceil(need) + 2f * pad, minW, maxW);
            float inner = w - 2f * pad;

            RectTransform savedCard = c.Card;
            Quads savedQ = c.Q;
            RectTransform a = Ui.NewRect(card, PartName);
            c.Card = a;
            Image shadow = Ui.NewImage(a, Shadow);
            Image edge = Ui.NewImage(a, PaperEdge);
            Image paper = Ui.NewImage(a, SlipPaper());
            c.Q = Ui.NewQuads(a);
            try
            {
                float y = pad;
                TextMeshProUGUI t = Text(c, size, title, Color.white, 0f);
                Ui.Move(t.rectTransform, pad, y);
                if (page.Length > 0)
                {
                    TextMeshProUGUI pg = Text(c, small, page, Color.white, 0f);
                    Vector2 ps = pg.rectTransform.sizeDelta;
                    Ui.Move(pg.rectTransform, w - pad - ps.x, y + Mathf.Max(0f, t.rectTransform.sizeDelta.y - ps.y));
                }
                y += t.rectTransform.sizeDelta.y + c.U;
                Dashes(c, pad, y, inner, 3f * c.U, 2f * c.U, c.U, PaperEdge);
                y += c.U + gap;

                PrepareIconsOf(lines);
                for (int i = 0; i < lines.Count; i++)
                {
                    PackLine line = lines[i];
                    if (line.Entry == null)
                    {
                        if (i > 0) y += c.U;
                        TextMeshProUGUI l = Text(c, small, Tint(line.Label, InkSoft), Color.white, 0f);
                        Ui.Move(l.rectTransform, pad, y);
                        y += l.rectTransform.sizeDelta.y;
                        continue;
                    }
                    ContentEntry e = line.Entry;
                    Sprite icon = Icons.IconOf(e.Item);
                    if (icon != null) PlaceIcon(c, icon, pad, y, lh, 0f);
                    string count = CountRich(e);
                    float cw = count.Length > 0 ? c.Measure(count, size) : 0f;
                    float nx = pad + lh + gap;
                    string name = Fitted(c, e.Name, size, w - pad - nx - (cw > 0f ? cw + 2f * gap : 0f));
                    TextMeshProUGUI n = Text(c, size, NameRich(name), Color.white, 0f);
                    Ui.Move(n.rectTransform, nx, y);
                    float h = Mathf.Max(lh, n.rectTransform.sizeDelta.y);
                    if (count.Length > 0)
                    {
                        TextMeshProUGUI k = Text(c, size, count, Color.white, 0f);
                        Ui.Move(k.rectTransform, w - pad - k.rectTransform.sizeDelta.x, y);
                    }
                    y += h + c.U;
                }
                if (more.Length > 0)
                {
                    Dashes(c, pad, y + c.U, inner, 3f * c.U, 2f * c.U, c.U, PaperEdge);
                    y += c.U + gap;
                    TextMeshProUGUI m = Text(c, small, more, Color.white, 0f);
                    Ui.Move(m.rectTransform, pad, y);
                    y += m.rectTransform.sizeDelta.y;
                }
                float h2 = Mathf.Round(y + pad);

                Ui.Place(shadow, 2f * c.U, 2f * c.U, w, h2);
                Ui.Place(edge, 0f, 0f, w, h2);
                Ui.Place(paper, c.U, c.U, w - 2f * c.U, h2 - 2f * c.U);
                Ui.Place(c.Q, 0f, 0f, w, h2);
                Ui.Place(a, 0f, 0f, w, h2);

                // 订书钉：一道深灰的钉脚横跨两张纸的接缝，上面一道亮边。贴哪一边由 Position 决定。
                RectTransform staple = Ui.NewRect(a, "staple");
                float sw = Mathf.Round(c.M.Line * 0.7f), st = Mathf.Max(c.U, Mathf.Round(c.U * 1.5f));
                Image leg = Ui.NewImage(staple, new Color32(96, 96, 100, 255));
                Image shine = Ui.NewImage(staple, new Color32(196, 196, 200, 255));
                Ui.Place(leg, 0f, 0f, sw, st + c.U);
                Ui.Place(shine, 0f, 0f, sw, c.U);
                Ui.Place(staple, 0f, 0f, sw, st + c.U);

                // 只压住估价单的边框，不盖到上面的字；名字和价格那块留着，从正文开始的高度挂下来。
                PackSlip slip = new PackSlip();
                slip.Root = a;
                slip.Staple = staple;
                slip.W = w;
                slip.H = h2;
                slip.Top = Mathf.Round(top);
                slip.CardW = cardW;
                slip.Overlap = 2f * c.U;
                pack = slip;
                PlacePacking(true);
            }
            finally
            {
                c.Card = savedCard;
                c.Q = savedQ;
            }
        }

        /// 每档设置列几种物品（“内容物最多显示”2 档就是 10 种）。
        private const int PackKindsPerStep = 5;

        /// 装箱单贴到卡片右边（right）或左边，订书钉跟着跨在接缝上。
        private static void PlacePacking(bool right)
        {
            if (pack == null) return;
            Ui.Move(pack.Root, right ? pack.CardW - pack.Overlap : pack.Overlap - pack.W, pack.Top);
            if (pack.Staple != null)
            {
                float sw = pack.Staple.sizeDelta.x;
                float seam = right ? 0f : pack.W;
                Ui.Move(pack.Staple, Mathf.Round(seam - sw / 2f), Mathf.Round(pack.Staple.sizeDelta.y * 2f));
            }
        }

        private static string NameRich(string name)
        {
            return Tint(name, Ink);
        }

        private static string CountRich(ContentEntry e)
        {
            return e.Count > 1 ? Tint("×" + e.Count.ToString(CultureInfo.InvariantCulture), InkSoft) : "";
        }

        /// 名字放不下 room 宽时截短，末尾加“…”。
        private static string Fitted(Ctx c, string name, float size, float room)
        {
            if (room <= 0f || c.Measure(NameRich(name), size) <= room) return name;
            for (int n = name.Length - 1; n > 0; n--)
            {
                string s = name.Substring(0, n).TrimEnd() + "…";
                if (c.Measure(NameRich(s), size) <= room) return s;
            }
            return "…";
        }

        private static void PrepareIconsOf(List<PackLine> lines)
        {
            List<ContentEntry> entries = new List<ContentEntry>();
            for (int i = 0; i < lines.Count; i++) if (lines[i].Entry != null) entries.Add(lines[i].Entry);
            PrepareIcons(entries, entries.Count);
        }
    }
}
