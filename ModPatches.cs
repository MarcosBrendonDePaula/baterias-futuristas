using HarmonyLib;
using STRINGS;

namespace BateriasFuturistas
{
    internal static class ModPatches
    {
        private const string PowerCategory = "Power";
        private const string TechId = "AdvancedPowerRegulation";

        private static readonly string[] BuildablePieces =
        {
            GravityBatteryBaseConfig.ID,
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
