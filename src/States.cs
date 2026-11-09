#if DEVDUMP
using System;
using System.Collections.Generic;

namespace SectionedTooltips
{
    /// <summary>
    /// dev 构建专用：造出游戏里很难凑齐状态的真物品（写入过特性的种子、各种 DNA 盘……），让游戏自己生成 tooltip。
    /// 不是假文字：用的是游戏公开的 PlantGeneticsHelper（SetTraitRank、ApplyTrait、AddHeat、WriteDisk），
    /// 所以行的写法、颜色、本地化都和玩家看到的一样。状态清单来自反编译 PlantGeneticsHelper.CreateSeedGeneticsTooltip / CreateDiskTooltip。
    ///
    /// 用在两处：全量导出（dumpall.flag）把样本也导出来；有 samples.flag 时，悬停任意物品就把每个样本建成估价单写进日志（[slip]，名字“压力:样本·…”）。
    /// </summary>
    internal static class States
    {
        private const string Seed = "fillerweed_seed";
        private const string Disk = "plant_dna_disk";

        internal sealed class Sample
        {
            public string Label;
            public GameItem Item;
            /// 种子样本：生成 tooltip 时基因扫描仪开没开（开着才显示固有/无法编辑/效果那种写法）；其他样本为 null，不动它。
            public bool? Scanner;
        }

        /// 生成这个样本的 tooltip（走 GetTooltipBasic，我们的排版跟着跑）；种子按样本的要求临时开关基因扫描仪。
        internal static void Invoke(System.Reflection.MethodInfo get, Sample s)
        {
            PlayerStore store = null;
            bool keep = false;
            if (s.Scanner.HasValue)
            {
                store = PlayerStore.Instance;
                if (store != null)
                {
                    keep = store.isGeneScannerScanning;
                    store.isGeneScannerScanning = s.Scanner.Value;
                }
            }
            try { get.Invoke(s.Item, null); }
            finally { if (store != null) store.isGeneScannerScanning = keep; }
        }

        /// 正在给样本生成 tooltip：这时候别的 dev 功能（压力测试）不要插手。
        internal static bool Building;

        /// 造好全部样本；用完要 Destroy。造某一个失败只跳过它，并记日志。
        internal static List<Sample> Make()
        {
            List<Sample> list = new List<Sample>();
            List<PlantTrait> all = new List<PlantTrait>();
            try { all.AddRange(PlantTraitRegistry.All); }
            catch (Exception e) { SectionedTooltipsMod.Log("样本：读不到特性表：" + e.Message); }

            AddSeed(list, all, "种子·原样", null);
            AddSeed(list, all, "种子·写入好坏特性", delegate(GameItem s, Picks p)
            {
                Apply(s, p.NewGood, 1);
                Apply(s, p.NewBad, 1);
            });
            AddSeed(list, all, "种子·提升天生基因", delegate(GameItem s, Picks p)
            {
                if (p.Innate != null) PlantGeneticsHelper.SetTraitRank(s, p.Innate.id, p.InnateRank + 2);
            });
            AddSeed(list, all, "种子·移除天生基因", delegate(GameItem s, Picks p)
            {
                if (p.Innate2 != null) PlantGeneticsHelper.SetTraitRank(s, p.Innate2.id, 0);
            });
            AddSeed(list, all, "种子·无效果特性", delegate(GameItem s, Picks p)
            {
                if (p.Useless != null) PlantGeneticsHelper.SetTraitRank(s, p.Useless.id, 1);
            });
            AddSeed(list, all, "种子·有热度", delegate(GameItem s, Picks p)
            {
                if (p.NewGood != null) PlantGeneticsHelper.AddHeat(s, p.NewGood);
            });
            AddSeed(list, all, "种子·综合改良", delegate(GameItem s, Picks p)
            {
                Apply(s, p.NewGood, 1);
                Apply(s, p.NewBad, 1);
                if (p.Innate != null) PlantGeneticsHelper.SetTraitRank(s, p.Innate.id, p.InnateRank + 1);
                if (p.Innate2 != null) PlantGeneticsHelper.SetTraitRank(s, p.Innate2.id, 0);
                if (p.Useless != null) PlantGeneticsHelper.SetTraitRank(s, p.Useless.id, 1);
                if (p.NewGood != null) PlantGeneticsHelper.AddHeat(s, p.NewGood);
            });
            AddSeed(list, all, "种子·全部特性", delegate(GameItem s, Picks p)
            {
                for (int i = 0; i < all.Count; i++) PlantGeneticsHelper.SetTraitRank(s, all[i].id, Math.Max(1, Math.Min(3, all[i].maxRank)));
            });
            AddSeed(list, all, "种子·无特性", delegate(GameItem s, Picks p)
            {
                List<ValueTuple<PlantTrait, int>> now = PlantGeneticsHelper.GetTraits(s);
                for (int i = 0; i < now.Count; i++) PlantGeneticsHelper.SetTraitRank(s, now[i].Item1.id, 0);
            });

            AddDisk(list, "DNA盘·空白", null, 0);
            for (int i = 0; i < all.Count; i++) AddDisk(list, "DNA盘·" + all[i].id, all[i], Math.Max(1, Math.Min(2, all[i].maxRank)));
            return list;
        }

        internal static void Destroy(List<Sample> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                try { if (list[i].Item != null) list[i].Item.Destroy(); } catch { }
            }
            list.Clear();
        }

        /// 种子样本要用到的几个特性：新写入的好、坏特性，可以提升的和可以移除的天生特性，对这种作物无效的特性。
        private sealed class Picks
        {
            public PlantTrait NewGood, NewBad, Innate, Innate2, Useless;
            public int InnateRank;
        }

        /// 每个种子状态造两份：扫描仪开着、关着各一份。
        private static void AddSeed(List<Sample> list, List<PlantTrait> all, string label, Action<GameItem, Picks> edit)
        {
            AddSeed(list, all, label + "·已分析", edit, true);
            AddSeed(list, all, label + "·未分析", edit, false);
        }

        private static void AddSeed(List<Sample> list, List<PlantTrait> all, string label, Action<GameItem, Picks> edit, bool scanner)
        {
            GameItem seed = null;
            try
            {
                seed = DirectoryMaster.Create<GameItem>(Seed);
                if (seed == null) { SectionedTooltipsMod.Log("样本：造不出种子 " + Seed); return; }
                if (edit != null) edit(seed, Pick(seed, all));
                try { PlantGeneticsHelper.RefreshModifiedType(seed); } catch { }
                Sample s = new Sample();
                s.Label = label;
                s.Item = seed;
                s.Scanner = scanner;
                list.Add(s);
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("样本：" + label + " 失败：" + e);
                try { if (seed != null) seed.Destroy(); } catch { }
            }
        }

        private static Picks Pick(GameItem seed, List<PlantTrait> all)
        {
            Picks p = new Picks();
            List<ValueTuple<PlantTrait, int>> innate = PlantGeneticsHelper.GetTraits(seed);
            HashSet<string> has = new HashSet<string>();
            for (int i = 0; i < innate.Count; i++)
            {
                PlantTrait t = innate[i].Item1;
                has.Add(t.id);
                if (t.locked) continue;
                if (p.Innate == null) { p.Innate = t; p.InnateRank = innate[i].Item2; }
                else if (p.Innate2 == null) p.Innate2 = t;
            }
            for (int i = 0; i < all.Count; i++)
            {
                PlantTrait t = all[i];
                if (has.Contains(t.id) || t.locked) continue;
                string why;
                bool usable = PlantGeneticsHelper.IsTraitUsable(seed, t, out why);
                if (!usable) { if (p.Useless == null) p.Useless = t; continue; }
                if (!PlantGeneticsHelper.CanApplyTrait(seed, t, out why)) continue;
                if (t.IsNegative) { if (p.NewBad == null) p.NewBad = t; }
                else if (p.NewGood == null) p.NewGood = t;
            }
            SectionedTooltipsMod.Log(string.Format("样本：种子特性 好={0} 坏={1} 天生={2}({3}) 天生2={4} 无效={5}",
                Id(p.NewGood), Id(p.NewBad), Id(p.Innate), p.InnateRank, Id(p.Innate2), Id(p.Useless)));
            return p;
        }

        /// 走游戏写入特性的正路（会带上热度等副作用）；不让写就直接设等级。
        private static void Apply(GameItem seed, PlantTrait t, int rank)
        {
            if (t == null) return;
            try { PlantGeneticsHelper.ApplyTrait(seed, t, rank); }
            catch { PlantGeneticsHelper.SetTraitRank(seed, t.id, rank); }
        }

        private static void AddDisk(List<Sample> list, string label, PlantTrait trait, int rank)
        {
            GameItem disk = null;
            try
            {
                disk = DirectoryMaster.Create<GameItem>(Disk);
                if (disk == null) { SectionedTooltipsMod.Log("样本：造不出 DNA 盘 " + Disk); return; }
                if (trait != null) PlantGeneticsHelper.WriteDisk(disk, trait, rank, 3);
                Sample s = new Sample();
                s.Label = label;
                s.Item = disk;
                list.Add(s);
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("样本：" + label + " 失败：" + e.Message);
                try { if (disk != null) disk.Destroy(); } catch { }
            }
        }

        private static string Id(PlantTrait t) { return t != null ? t.id : "-"; }
    }
}
#endif
