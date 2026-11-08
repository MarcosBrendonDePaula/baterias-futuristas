using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace BateriasFuturistas
{
    // Condenses piped gas into liquid; the heat taken from the gas leaves at the compressor.
    public class HeatCompressor : KMonoBehaviour, ISim200ms
    {
        public const float KgPerSecond = 1f;
        public const float Watts = 1200f;
        public const float BelowBoilingMargin = 2f;

        private const float MaxHeatTemperature = 1500f;

        [MyCmpReq]
        private Operational operational;

        [MyCmpReq]
        private Building building;

        public void Sim200ms(float dt)
        {
            operational.SetActive(operational.IsOperational && Compress(dt));
        }

        private bool Compress(float dt)
        {
            if (!operational.IsOperational)
            {
                return false;
            }
            int inlet = building.GetUtilityInputCell();
            ConduitFlow gas = Game.Instance.gasConduitFlow;
            ConduitFlow.ConduitContents contents = gas.GetContents(inlet);
            Element element = ElementLoader.FindElementByHash(contents.element);
            Element liquid = element != null && element.IsGas ? element.lowTempTransition : null;
            if (contents.mass <= 0f || liquid == null || !liquid.IsLiquid)
            {
                return false;
            }
            float outletTemperature = Mathf.Min(contents.temperature, element.lowTemp - BelowBoilingMargin);
            float amount = Mathf.Min(KgPerSecond * dt, contents.mass);
            float moved = Game.Instance.liquidConduitFlow.AddElement(
                building.GetUtilityOutputCell(), liquid.id, amount, outletTemperature, byte.MaxValue, 0);
            if (moved <= 0f)
            {
                return false;
            }
            gas.RemoveElement(inlet, moved);
            float removedKdtu = moved * element.specificHeatCapacity * (contents.temperature - outletTemperature);
            float electricKdtu = Watts * dt / 1000f;
            SimMessages.ModifyEnergy(
                Grid.PosToCell(this),
                removedKdtu + electricKdtu,
                MaxHeatTemperature,
                SimMessages.EnergySourceID.StructureTemperature);
            return true;
        }
    }

    // Lets a small flow of liquid through and releases it as very cold gas.
    public class ExpansionValve : KMonoBehaviour, ISim200ms
    {
        public const float KgPerSecond = 0.2f;
        public const float AboveBoilingMargin = 2f;

        [MyCmpReq]
        private Operational operational;

        [MyCmpReq]
        private Building building;

        public void Sim200ms(float dt)
        {
            operational.SetActive(Expand(dt));
        }

        private bool Expand(float dt)
        {
            int inlet = building.GetUtilityInputCell();
            ConduitFlow liquid = Game.Instance.liquidConduitFlow;
            ConduitFlow.ConduitContents contents = liquid.GetContents(inlet);
            Element element = ElementLoader.FindElementByHash(contents.element);
            Element gas = element != null && element.IsLiquid ? element.highTempTransition : null;
            if (contents.mass <= 0f || gas == null || !gas.IsGas)
            {
                return false;
            }
            float amount = Mathf.Min(KgPerSecond * dt, contents.mass);
            float moved = Game.Instance.gasConduitFlow.AddElement(
                building.GetUtilityOutputCell(), gas.id, amount, element.highTemp + AboveBoilingMargin, byte.MaxValue, 0);
            if (moved <= 0f)
            {
                return false;
            }
            liquid.RemoveElement(inlet, moved);
            return true;
        }
    }

    public class HeatCompressorConfig : IBuildingConfig
    {
        public const string ID = "HeatCompressor";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                ID, 2, 2, "heat_compressor_kanim", 60, 60f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER3, MATERIALS.ALL_METALS, 1600f,
                BuildLocationRule.OnFloor, TUNING.BUILDINGS.DECOR.PENALTY.TIER1, NOISE_POLLUTION.NOISY.TIER3);
            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = HeatCompressor.Watts;
            def.PowerInputOffset = new CellOffset(0, 0);
            def.InputConduitType = ConduitType.Gas;
            def.UtilityInputOffset = new CellOffset(0, 1);
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = new CellOffset(1, 1);
            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(1, 0));
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.GasConduits.ID;
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            go.AddOrGet<LogicOperationalController>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            HeatPumpPieces.RemoveOutputRequirement(go);
            go.AddOrGet<HeatCompressor>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }

    public class ExpansionValveConfig : IBuildingConfig
    {
        public const string ID = "ExpansionValve";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                ID, 1, 2, "expansion_valve_kanim", 30, 30f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER2, MATERIALS.ALL_METALS, 1600f,
                BuildLocationRule.Anywhere, TUNING.BUILDINGS.DECOR.PENALTY.TIER1, NOISE_POLLUTION.NONE);
            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(0, 1);
            def.OutputConduitType = ConduitType.Gas;
            def.UtilityOutputOffset = new CellOffset(0, 0);
            def.Floodable = false;
            def.Entombable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.GasConduits.ID;
            return def;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            HeatPumpPieces.RemoveOutputRequirement(go);
            go.AddOrGet<ExpansionValve>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }

    internal static class HeatPumpPieces
    {
        public static void RemoveOutputRequirement(GameObject go)
        {
            RequireOutputs outputs = go.GetComponent<RequireOutputs>();
            if (outputs != null)
            {
                Object.DestroyImmediate(outputs);
            }
            // These pieces read the pipe themselves and have no Storage for the automatic consumer.
            ConduitConsumer consumer = go.GetComponent<ConduitConsumer>();
            if (consumer != null)
            {
                Object.DestroyImmediate(consumer);
            }
        }
    }
}
