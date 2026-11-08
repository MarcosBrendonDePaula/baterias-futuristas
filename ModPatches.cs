using HarmonyLib;
using STRINGS;
using UnityEngine;

namespace BateriasFuturistas
{
    internal static class ModPatches
    {
        private const string PowerCategory = "Power";
        private const string TechId = "AdvancedPowerRegulation";
        private const string UnsupportedReason = "Precisa ficar em cima de uma base ou de um trilho, alinhado com eles";

        private static readonly string[] BuildablePieces =
        {
            GravityBatteryBaseConfig.ID,
            GravityBatteryHydraulicConfig.ID,
            GravityBatteryRailConfig.ID,
            GravityBatteryMotorConfig.ID,
        };

        private static void AddStrings(string id, string name, string description, string effect)
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + id.ToUpperInvariant();
            Strings.Add(prefix + ".NAME", UI.FormatAsLink(name, id));
            Strings.Add(prefix + ".DESC", description);
            Strings.Add(prefix + ".EFFECT", effect);
        }

        [HarmonyPatch(typeof(GeneratedBuildings), nameof(GeneratedBuildings.LoadGeneratedBuildings))]
        private static class RegisterBuilding
        {
            private static void Prefix()
            {
                AddStrings(GravityBatteryConfig.ID, "Bateria Gravitacional Compacta",
                    "Um peso erguido guarda energia indefinidamente, sem vazamento.",
                    "Versão antiga em torre fixa.");
                AddStrings(GravityBatteryBaseConfig.ID, "Base da Bateria Gravitacional",
                    "A base guarda o peso e faz a ligação elétrica da torre.",
                    "Precisa de trilhos empilhados e de um motor no topo. A energia guardada cresce com a massa e com a altura da torre.\n\nNão perde carga parada, mas gera calor no motor ao carregar e descarregar.");
                AddStrings(GravityBatteryHydraulicConfig.ID, "Base Hidráulica Gravitacional",
                    "Um tanque selado de líquido serve de peso para a torre.",
                    "Enche pelo cano de líquido. Precisa de trilhos empilhados e de um motor no topo. A energia guardada cresce com a massa e com a altura da torre.\n\nNão perde carga parada, mas gera calor no motor ao carregar e descarregar.");
                AddStrings(GravityBatteryRailConfig.ID, "Trilho Gravitacional",
                    "Cada trilho aumenta a altura da queda do peso.",
                    "Empilhe sobre a base ou sobre outro trilho. O limite de altura depende do metal da base.");
                AddStrings(GravityBatteryMotorConfig.ID, "Motor Gravitacional",
                    "O motor ergue o peso e fecha o topo da torre.",
                    "Coloque em cima do último trilho para completar a bateria.");
                foreach (string piece in BuildablePieces)
                {
                    ModUtil.AddBuildingToPlanScreen(PowerCategory, piece);
                }
            }
        }

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
                reason = UnsupportedReason;
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
            }
        }
    }
}
