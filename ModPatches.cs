using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BateriasFuturistas
{
    internal static class ModPatches
    {
        private const string PowerCategory = "Power";
        private const string TechId = "AdvancedPowerRegulation";
        private const string ThermalTechId = "TemperatureModulation";
        private const string UtilitiesCategory = "Utilities";

        private static readonly string[] BuildablePieces =
        {
            GravityBatteryBaseConfig.ID,
            GravityBatteryHydraulicConfig.ID,
            GravityBatteryRailConfig.ID,
            GravityBatteryMotorConfig.ID,
            FlywheelBatteryConfig.ID,
            CompressedAirBatteryConfig.ID,
            FlowBatteryConfig.ID,
            MoltenSaltBatteryConfig.ID,
        };

        [HarmonyPatch(typeof(GeneratedBuildings), nameof(GeneratedBuildings.LoadGeneratedBuildings))]
        private static class RegisterBuilding
        {
            private static void Prefix()
            {
                ModText.Register();
                ModUtil.AddBuildingToPlanScreen(UtilitiesCategory, HeatCompressorConfig.ID);
                ModUtil.AddBuildingToPlanScreen(UtilitiesCategory, ExpansionValveConfig.ID);
                foreach (string piece in AllPieces)
                {
                    ModUtil.AddBuildingToPlanScreen(PowerCategory, piece);
                }
            }
        }

        private static readonly string[] AllPieces = BuildablePieces.Concat(new[] { ThermalSandBatteryConfig.ID }).ToArray();

        private static bool NeedsSupport(BuildingDef def)
        {
            return def.PrefabID == GravityBatteryRailConfig.ID || def.PrefabID == GravityBatteryMotorConfig.ID;
        }

        private static bool IsSupported(int cell)
        {
            int below = Grid.CellBelow(cell);
            if (!Grid.IsValidCell(below))
            {
                return false;
            }
            GameObject occupant = Grid.Objects[below, (int)ObjectLayer.Building];
            Building support = occupant != null ? occupant.GetComponent<Building>() : null;
            if (support == null || Grid.CellColumn(Grid.PosToCell(occupant)) != Grid.CellColumn(cell))
            {
                return false;
            }
            string id = support.Def.PrefabID;
            return id == GravityBatteryBaseConfig.ID
                || id == GravityBatteryHydraulicConfig.ID
                || id == GravityBatteryRailConfig.ID;
        }

        private static void RequireSupport(BuildingDef def, int cell, ref string reason, ref bool result)
        {
            if (result && NeedsSupport(def) && !IsSupported(cell))
            {
                result = false;
                reason = ModText.Get("UNSUPPORTED");
            }
        }

        [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.IsValidBuildLocation),
            new[] { typeof(GameObject), typeof(int), typeof(Orientation), typeof(bool), typeof(string) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
        private static class ValidateBuild
        {
            private static void Postfix(BuildingDef __instance, int cell, ref string fail_reason, ref bool __result)
            {
                RequireSupport(__instance, cell, ref fail_reason, ref __result);
            }
        }

        [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.IsValidPlaceLocation),
            new[] { typeof(GameObject), typeof(int), typeof(Orientation), typeof(bool), typeof(string), typeof(bool) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
        private static class ValidatePlacement
        {
            private static void Postfix(BuildingDef __instance, int cell, ref string fail_reason, ref bool __result)
            {
                RequireSupport(__instance, cell, ref fail_reason, ref __result);
            }
        }

        [HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
        private static class RegisterResearch
        {
            private static void Postfix()
            {
                Db.Get().Techs.Get(TechId).unlockedItemIDs.AddRange(BuildablePieces);
                Db.Get().Techs.Get(ThermalTechId).unlockedItemIDs.Add(ThermalSandBatteryConfig.ID);
                Db.Get().Techs.Get(ThermalTechId).unlockedItemIDs.Add(HeatCompressorConfig.ID);
                Db.Get().Techs.Get(ThermalTechId).unlockedItemIDs.Add(ExpansionValveConfig.ID);
            }
        }
    }
}
