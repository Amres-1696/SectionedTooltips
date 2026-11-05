using System.Collections.Generic;
using Palette = RenderHandler.ColorPalette;

namespace SectionedTooltips
{
    /// 各节的键，以及每一节标题栏的样子。
    internal static class Sections
    {
        // 不是真正的小节：标题块没有标题栏，描述是折行的段落。
        internal const string Title = "title";
        internal const string Desc = "desc";

        internal const string Value = "value";
        internal const string Mods = "mods";
        internal const string Dev = "dev";
        internal const string Appraisal = "appraisal";
        internal const string Power = "power";
        internal const string Equipment = "equipment";
        internal const string Machine = "machine";
        internal const string Condition = "condition";
        internal const string Liquid = "liquid";
        internal const string Material = "material";
        internal const string Weapon = "weapon";
        internal const string Security = "security";
        internal const string Lottery = "lottery";
        internal const string Plant = "plant";
        internal const string Husbandry = "husbandry";
        internal const string Food = "food";
        internal const string Bio = "bio";
        internal const string Brew = "brew";
        internal const string Module = "module";
        internal const string Label = "label";
        internal const string Container = "container";
        internal const string Info = "info";

        internal struct Style
        {
            public Palette Accent;
            public Palette Text;
        }

        private static readonly Dictionary<string, Style> styles = new Dictionary<string, Style>
        {
            { Value, S(Palette.Yellow, Palette.LightYellow) },
            { Appraisal, S(Palette.Turquoise, Palette.LightTurquoise) },
            { Power, S(Palette.Orange, Palette.LightOrange) },
            { Equipment, S(Palette.Blue, Palette.LightBlue) },
            { Machine, S(Palette.Blue, Palette.LightBlue) },
            { Condition, S(Palette.Orange, Palette.LightOrange) },
            { Liquid, S(Palette.Cyan, Palette.LightCyan) },
            { Material, S(Palette.Gray, Palette.VeryLightGray) },
            { Weapon, S(Palette.Red, Palette.LightRed) },
            { Security, S(Palette.Purple, Palette.LightPurple) },
            { Lottery, S(Palette.Yellow, Palette.LightYellow) },
            { Plant, S(Palette.Green, Palette.LightGreen) },
            { Husbandry, S(Palette.Green, Palette.LightGreen) },
            { Food, S(Palette.Green, Palette.LightGreen) },
            { Bio, S(Palette.Red, Palette.LightRed) },
            { Brew, S(Palette.Purple, Palette.LightPurple) },
            { Module, S(Palette.Turquoise, Palette.LightTurquoise) },
            { Label, S(Palette.Gray, Palette.VeryLightGray) },
            { Container, S(Palette.Cyan, Palette.LightCyan) },
            { Mods, S(Palette.Purple, Palette.LightPurple) },
            { Info, S(Palette.Gray, Palette.VeryLightGray) },
        };

        internal static Style StyleOf(string key)
        {
            Style s;
            return styles.TryGetValue(key, out s) ? s : styles[Info];
        }

        internal static string TitleOf(string key)
        {
            return SectionedTooltipsMod.L("sec_" + key);
        }

        private static Style S(Palette accent, Palette text)
        {
            Style s;
            s.Accent = accent;
            s.Text = text;
            return s;
        }
    }
}
