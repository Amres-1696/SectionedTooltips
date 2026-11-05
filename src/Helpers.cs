using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SectionedTooltips
{
    /// GetTooltipBasic 调用的各系统 tooltip helper，按调用顺序排列（游戏 0.46D）。每个都用 prefix/postfix 包起来，记下它写了哪些 slice，排版时据此判断一行属于哪一节。
    internal static class Helpers
    {
        private static readonly Type[] Sig = new Type[] { typeof(RichTextBuilder), typeof(GameItem) };

        private static readonly List<string> names = new List<string>();
        private static readonly List<string> keys = new List<string>();
        private static readonly Dictionary<MethodBase, int> ordinals = new Dictionary<MethodBase, int>();

        // Layout 的空档规则要用到的 helper 序号；Apply 之前是 -1。
        internal static int OrdStolen = -1;
        internal static int OrdContraband = -1;
        internal static int OrdBarterLast = -1;
        internal static int OrdModsLate = -1;
        internal static int OrdContainer = -1;

        internal static int Count { get { return names.Count; } }
        internal static string Name(int ord) { return ord >= 0 && ord < names.Count ? names[ord] : "?"; }
        internal static string Key(int ord) { return ord >= 0 && ord < keys.Count ? keys[ord] : Sections.Info; }

        internal static int Ordinal(MethodBase m)
        {
            int ord;
            return m != null && ordinals.TryGetValue(m, out ord) ? ord : -1;
        }

        private static Harmony harmony;
        private static HarmonyMethod prefix;
        private static HarmonyMethod postfix;
        private static int patched;

        internal static int Apply(Harmony h)
        {
            harmony = h;
            prefix = new HarmonyMethod(AccessTools.Method(typeof(Helpers), "Prefix"));
            postfix = new HarmonyMethod(AccessTools.Method(typeof(Helpers), "Postfix"));

            R("GeneralHelper:CreateGeneralItemTooltip", AccessTools.Method(typeof(GeneralHelper), "CreateGeneralItemTooltip", Sig), Sections.Title);
            OrdStolen = R("StolenHelper:CreateStolenItemTooltip", AccessTools.Method(typeof(StolenHelper), "CreateStolenItemTooltip", Sig), Sections.Title);
            R("ItemPinHelper:CreatePinnedTooltip", AccessTools.Method(typeof(ItemPinHelper), "CreatePinnedTooltip", Sig), Sections.Title);
            R("CommonTooltipHelper:CreateItemTypeTooltip", AccessTools.Method(typeof(CommonTooltipHelper), "CreateItemTypeTooltip", Sig), Sections.Title);
            R("ToggleAppearanceHelper:CreateToggleAppearanceTooltip", AccessTools.Method(typeof(ToggleAppearanceHelper), "CreateToggleAppearanceTooltip", Sig), Sections.Title);
            OrdContraband = R("ContrabandHelper:CreateContrabandItemTooltip", AccessTools.Method(typeof(ContrabandHelper), "CreateContrabandItemTooltip", Sig), Sections.Title);
            R("BarterHelper:CreateDiscountedBarteringItemTooltip", AccessTools.Method(typeof(BarterHelper), "CreateDiscountedBarteringItemTooltip", Sig), Sections.Value);
            OrdBarterLast = R("BarterHelper:CreateTaggedItemAsPaymentTooltip", AccessTools.Method(typeof(BarterHelper), "CreateTaggedItemAsPaymentTooltip", Sig), Sections.Value);
            R("ModInternal:CreateTooltipEarly", AccessTools.Method(typeof(ModInternal), "CreateTooltipEarly", Sig), Sections.Mods);
            R("ModInternal:CreateDevTooltipEarly", AccessTools.Method(typeof(ModInternal), "CreateDevTooltipEarly", Sig), Sections.Dev);
            R("AppraisalHelper:CreateAppraisalTooltip", AccessTools.Method(typeof(AppraisalHelper), "CreateAppraisalTooltip", Sig), Sections.Appraisal);
            R("AppraisalRankHelper:CreateCertificateTooltip", AccessTools.Method(typeof(AppraisalRankHelper), "CreateCertificateTooltip", Sig), Sections.Appraisal);
            R("PowerHelper:CreatePowerSourceItemTooltip", AccessTools.Method(typeof(PowerHelper), "CreatePowerSourceItemTooltip", Sig), Sections.Power);
            R("EquipmentHelper:CreateEquipmentTooltip", AccessTools.Method(typeof(EquipmentHelper), "CreateEquipmentTooltip", Sig), Sections.Equipment);
            R("InspectableHelper:CreateInspectableTooltip", AccessTools.Method(typeof(InspectableHelper), "CreateInspectableTooltip", Sig), Sections.Appraisal);
            R("InsCigaretteHelper:CreateDebugTooltip", AccessTools.Method(typeof(InsCigaretteHelper), "CreateDebugTooltip", Sig), Sections.Dev);
            R("InsStampHelper:CreateDebugTooltip", AccessTools.Method(typeof(InsStampHelper), "CreateDebugTooltip", Sig), Sections.Dev);
            R("InsInjectorHelper:CreateDebugTooltip", AccessTools.Method(typeof(InsInjectorHelper), "CreateDebugTooltip", Sig), Sections.Dev);
            R("RibwichHelper:CreateDebugTooltip", AccessTools.Method(typeof(RibwichHelper), "CreateDebugTooltip", Sig), Sections.Dev);
            R("AppHandHelper:CreateDebugTooltip", AccessTools.Method(typeof(AppHandHelper), "CreateDebugTooltip", Sig), Sections.Dev);
            R("TechnicianBackpackHelper:CreateComplexeEquipmentTooltip", AccessTools.Method(typeof(TechnicianBackpackHelper), "CreateComplexeEquipmentTooltip", Sig), Sections.Equipment);
            R("TechnicianBackpackHelper:CreateComplexeModuleTooltip", AccessTools.Method(typeof(TechnicianBackpackHelper), "CreateComplexeModuleTooltip", Sig), Sections.Equipment);
            R("TechnicianBackpackHelper:CreateComplexeSystemTooltip", AccessTools.Method(typeof(TechnicianBackpackHelper), "CreateComplexeSystemTooltip", Sig), Sections.Equipment);
            R("UseCountHelper:CreateUseCountItemTooltip", AccessTools.Method(typeof(UseCountHelper), "CreateUseCountItemTooltip", Sig), Sections.Condition);
            R("ToolboxHelper:CreateToolboxTooltip", AccessTools.Method(typeof(ToolboxHelper), "CreateToolboxTooltip", Sig), Sections.Condition);
            R("DebrisHelper:CreateTooltip", AccessTools.Method(typeof(DebrisHelper), "CreateTooltip", Sig), Sections.Info);
            R("ReactorHelper:CreateTooltip", AccessTools.Method(typeof(ReactorHelper), "CreateTooltip", Sig), Sections.Power);
            R("StorageExpansionHelper:CreateTooltip", AccessTools.Method(typeof(StorageExpansionHelper), "CreateTooltip", Sig), Sections.Machine);
            R("MachineStorageCoordinator:CreateTooltip", AccessTools.Method(typeof(MachineStorageCoordinator), "CreateTooltip", Sig), Sections.Machine);
            R("HopperHelper:CreateTooltip", AccessTools.Method(typeof(HopperHelper), "CreateTooltip", Sig), Sections.Machine);
            R("OrganHelper:CreateOrganItemTooltip", AccessTools.Method(typeof(OrganHelper), "CreateOrganItemTooltip", Sig), Sections.Bio);
            R("MachineDesequencer:CreateMachineDesequencerTooltip", AccessTools.Method(typeof(MachineDesequencer), "CreateMachineDesequencerTooltip", Sig), Sections.Machine);
            R("MachineBlender:CreateTooltip", AccessTools.Method(typeof(MachineBlender), "CreateTooltip", Sig), Sections.Machine);
            R("MachineHeatingPlate:CreateTooltip", AccessTools.Method(typeof(MachineHeatingPlate), "CreateTooltip", Sig), Sections.Machine);
            R("MachineChemFinisher:CreateTooltip", AccessTools.Method(typeof(MachineChemFinisher), "CreateTooltip", Sig), Sections.Machine);
            R("MachineAlarm:CreateAlarmTooltip", AccessTools.Method(typeof(MachineAlarm), "CreateAlarmTooltip", Sig), Sections.Machine);
            R("MachineProjector:CreateProjectorTooltip", AccessTools.Method(typeof(MachineProjector), "CreateProjectorTooltip", Sig), Sections.Machine);
            R("MachineDealMaker:CreateTooltip", AccessTools.Method(typeof(MachineDealMaker), "CreateTooltip", Sig), Sections.Machine);
            R("MachineMoistureFarm:CreateTooltip", AccessTools.Method(typeof(MachineMoistureFarm), "CreateTooltip", Sig), Sections.Machine);
            R("MachinePurifier:CreateTooltip", AccessTools.Method(typeof(MachinePurifier), "CreateTooltip", Sig), Sections.Machine);
            R("MachineBottlePrinter:CreateTooltip", AccessTools.Method(typeof(MachineBottlePrinter), "CreateTooltip", Sig), Sections.Machine);
            R("VendingMachineHelper:CreateTooltip", AccessTools.Method(typeof(VendingMachineHelper), "CreateTooltip", Sig), Sections.Machine);
            R("VendingFountainHelper:CreateTooltip", AccessTools.Method(typeof(VendingFountainHelper), "CreateTooltip", Sig), Sections.Machine);
            R("ScavHelper:CreateTooltip", AccessTools.Method(typeof(ScavHelper), "CreateTooltip", Sig), Sections.Machine);
            R("HusbandryHelper:CreateVacuumRobotTooltip", AccessTools.Method(typeof(HusbandryHelper), "CreateVacuumRobotTooltip", Sig), Sections.Machine);
            R("MachineryHelper:CreateMachineryTooltip", AccessTools.Method(typeof(MachineryHelper), "CreateMachineryTooltip", Sig), Sections.Machine);
            R("MachineProgressHelper:CreateMachineTooltip", AccessTools.Method(typeof(MachineProgressHelper), "CreateMachineTooltip", Sig), Sections.Machine);
            R("MachineHydroponic:CreateMachineTooltip", AccessTools.Method(typeof(MachineHydroponic), "CreateMachineTooltip", Sig), Sections.Machine);
            R("MachineDnaManipulator:CreateMachineTooltip", AccessTools.Method(typeof(MachineDnaManipulator), "CreateMachineTooltip", Sig), Sections.Machine);
            R("MachineTurboBoosterAdv:CreateMachineTooltip", AccessTools.Method(typeof(MachineTurboBoosterAdv), "CreateMachineTooltip", Sig), Sections.Machine);
            R("CurrentConverterHelper:CreateTooltip", AccessTools.Method(typeof(CurrentConverterHelper), "CreateTooltip", Sig), Sections.Power);
            R("HypertechBatteryHelper:CreateTooltip", AccessTools.Method(typeof(HypertechBatteryHelper), "CreateTooltip", Sig), Sections.Power);
            R("MindReprogrammerHelper:CreateTooltip", AccessTools.Method(typeof(MindReprogrammerHelper), "CreateTooltip", Sig), Sections.Machine);
            R("PropulsionHelper:CreatePropulsionSystemTooltip", AccessTools.Method(typeof(PropulsionHelper), "CreatePropulsionSystemTooltip", Sig), Sections.Machine);
            R("MachineryHelper:CreateTrashPileTooltip", AccessTools.Method(typeof(MachineryHelper), "CreateTrashPileTooltip", Sig), Sections.Info);
            R("CanteenHelper:CreateCanteenTooltip", AccessTools.Method(typeof(CanteenHelper), "CreateCanteenTooltip", Sig), Sections.Liquid);
            R("WaterFeatureHelper:CreateWaterFeatureTooltip", AccessTools.Method(typeof(WaterFeatureHelper), "CreateWaterFeatureTooltip", Sig), Sections.Liquid);
            R("WaterHelper:CreateWaterContainerTooltip", AccessTools.Method(typeof(WaterHelper), "CreateWaterContainerTooltip", Sig), Sections.Liquid);
            R("IngotPurityHelper:CreateIngotTooltip", AccessTools.Method(typeof(IngotPurityHelper), "CreateIngotTooltip", Sig), Sections.Material);
            R("DurabilityHelper:CreateIngotTooltip", AccessTools.Method(typeof(DurabilityHelper), "CreateIngotTooltip", Sig), Sections.Condition);
            R("GunHelper:CreateTooltip", AccessTools.Method(typeof(GunHelper), "CreateTooltip", Sig), Sections.Weapon);
            R("GunModHelper:CreateTooltip", AccessTools.Method(typeof(GunModHelper), "CreateTooltip", Sig), Sections.Weapon);
            R("LockHelper:CreateLockerContainerTooltip", AccessTools.Method(typeof(LockHelper), "CreateLockerContainerTooltip", Sig), Sections.Security);
            R("LotteryHelper:CreateLotteryTicketTooltip", AccessTools.Method(typeof(LotteryHelper), "CreateLotteryTicketTooltip", Sig), Sections.Lottery);
            R("SecuredContainerHelper:CreateKeyItemTip", AccessTools.Method(typeof(SecuredContainerHelper), "CreateKeyItemTip", Sig), Sections.Security);
            R("SecuredContainerHelper:CreateSecuredContainerToolTip", AccessTools.Method(typeof(SecuredContainerHelper), "CreateSecuredContainerToolTip", Sig), Sections.Security);
            R("HydroponicHelper:CreateSeedItemTooltip", AccessTools.Method(typeof(HydroponicHelper), "CreateSeedItemTooltip", Sig), Sections.Plant);
            R("PlantGeneticsHelper:CreateSeedGeneticsTooltip", AccessTools.Method(typeof(PlantGeneticsHelper), "CreateSeedGeneticsTooltip", Sig), Sections.Plant);
            R("PlantGeneticsHelper:CreateDiskTooltip", AccessTools.Method(typeof(PlantGeneticsHelper), "CreateDiskTooltip", Sig), Sections.Plant);
            R("MachineProgressHelper:CreateSourceItemDefaultTooltip", AccessTools.Method(typeof(MachineProgressHelper), "CreateSourceItemDefaultTooltip", Sig), Sections.Machine);
            R("LabelerHelper:CreateLabeledItemTooltip", AccessTools.Method(typeof(LabelerHelper), "CreateLabeledItemTooltip", Sig), Sections.Label);
            R("ModuleHelper:CreateModuleTooltip", AccessTools.Method(typeof(ModuleHelper), "CreateModuleTooltip", Sig), Sections.Module);
            R("HusbandryHelper:CreateItemTooltip", AccessTools.Method(typeof(HusbandryHelper), "CreateItemTooltip", Sig), Sections.Husbandry);
            R("HusbandryHelper:CreateButcherTallyTooltip", AccessTools.Method(typeof(HusbandryHelper), "CreateButcherTallyTooltip", Sig), Sections.Husbandry);
            R("LotteryHelper:CreateGamblersCoinTooltip", AccessTools.Method(typeof(LotteryHelper), "CreateGamblersCoinTooltip", Sig), Sections.Lottery);
            R("AnimalMarkHelper:CreateTooltip", AccessTools.Method(typeof(AnimalMarkHelper), "CreateTooltip", Sig), Sections.Husbandry);
            R("IngesterHelper:CreateIngesterTooltip", AccessTools.Method(typeof(IngesterHelper), "CreateIngesterTooltip", Sig), Sections.Food);
            R("FoodItemHelper:CreateFoodItemTooltip", AccessTools.Method(typeof(FoodItemHelper), "CreateFoodItemTooltip", Sig), Sections.Food);
            R("AnimalCage:CreateTooltip", AccessTools.Method(typeof(AnimalCage), "CreateTooltip", Sig), Sections.Husbandry);
            R("MachineFridge:CreateTooltip", AccessTools.Method(typeof(MachineFridge), "CreateTooltip", Sig), Sections.Machine);
            R("MachineFeedDispenser:CreateTooltip", AccessTools.Method(typeof(MachineFeedDispenser), "CreateTooltip", Sig), Sections.Husbandry);
            R("MachineAutoButcher:CreateTooltip", AccessTools.Method(typeof(MachineAutoButcher), "CreateTooltip", Sig), Sections.Husbandry);
            R("AquacultureHelper:CreateTooltip", AccessTools.Method(typeof(AquacultureHelper), "CreateTooltip", Sig), Sections.Husbandry);
            R("MachineRAS:CreateTooltip", AccessTools.Method(typeof(MachineRAS), "CreateTooltip", Sig), Sections.Husbandry);
            R("GutterflowPelletHelper:CreateTooltip", AccessTools.Method(typeof(GutterflowPelletHelper), "CreateTooltip", Sig), Sections.Husbandry);
            R("IndustrialFishFeedHelper:CreateTooltip", AccessTools.Method(typeof(IndustrialFishFeedHelper), "CreateTooltip", Sig), Sections.Husbandry);
            R("AugScanner:CreateTooltip", AccessTools.Method(typeof(AugScanner), "CreateTooltip", Sig), Sections.Bio);
            R("WineHelper:CreateTooltip", AccessTools.Method(typeof(WineHelper), "CreateTooltip", Sig), Sections.Brew);
            R("BlackLampHelper:CreateBlackLampTooltip", AccessTools.Method(typeof(BlackLampHelper), "CreateBlackLampTooltip", Sig), Sections.Appraisal);
            OrdModsLate = R("ModInternal:CreateTooltipLate", AccessTools.Method(typeof(ModInternal), "CreateTooltipLate", Sig), Sections.Mods);
            R("ModInternal:CreateDevTooltipLate", AccessTools.Method(typeof(ModInternal), "CreateDevTooltipLate", Sig), Sections.Dev);
            OrdContainer = R("ContainerHelper:CreateContainerTooltip", AccessTools.Method(typeof(ContainerHelper), "CreateContainerTooltip", Sig), Sections.Container);
            R("ScratcherHelper:CreateScratcherTooltip", AccessTools.Method(typeof(ScratcherHelper), "CreateScratcherTooltip", Sig), Sections.Lottery);
            R("LotteryHelper:CreateLotteryRedeemTooltip", AccessTools.Method(typeof(LotteryHelper), "CreateLotteryRedeemTooltip", Sig), Sections.Lottery);

            return patched;
        }

        /// 按调用顺序登记下一个 helper；方法找不到也照样占着序号。
        private static int R(string name, MethodInfo method, string key)
        {
            int ord = names.Count;
            names.Add(name);
            keys.Add(key);
            if (method == null)
            {
                SectionedTooltipsMod.Log("  helper not found (game changed?): " + name);
                return ord;
            }
            try
            {
                ordinals[method] = ord;
                // 不用 Harmony.Patch：各 Harmony 版本参数不同（HarmonyX 多一个）。
                harmony.CreateProcessor(method).AddPrefix(prefix).AddPostfix(postfix).Patch();
                patched++;
            }
            catch (Exception e)
            {
                SectionedTooltipsMod.Log("  could not patch " + name + ": " + e.Message);
            }
            return ord;
        }

        private static void Prefix(MethodBase __originalMethod, RichTextBuilder __0)
        {
            Capture.Enter(__originalMethod, __0);
        }

        private static void Postfix(RichTextBuilder __0)
        {
            Capture.Exit(__0);
        }
    }
}
