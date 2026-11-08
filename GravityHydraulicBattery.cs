using KSerialization;
using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace BateriasFuturistas
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GravityHydraulicBattery : GravityBatteryBase
    {
        public const float SteelTankMassKg = 10000f;
        public const float IronTankMassKg = 5000f;
        public const float DefaultTankMassKg = 3000f;

        [MyCmpReq]
        private ConduitConsumer consumer;

        protected override bool UsesDeliveries => false;

        protected override void ApplyUserCapacity()
        {
            consumer.capacityKG = UserMaxCapacity;
        }

        protected override float MaxMassFor(SimHashes material)
        {
            switch (material)
            {
                case SimHashes.Steel:
                    return SteelTankMassKg;
                case SimHashes.Iron:
                    return IronTankMassKg;
                default:
                    return DefaultTankMassKg;
            }
        }
    }

    public class GravityBatteryHydraulicConfig : BaseBatteryConfig
    {
        public const string ID = "GravityBatteryHydraulicBase";

        private const float IntakeKgPerSecond = 10f;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = CreateBuildingDef(
                ID,
                3,
                GravityBatteryBaseConfig.HeightTiles,
                30,
                "gravity_hydraulic_base_kanim",
                60f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                MATERIALS.ALL_METALS,
                800f,
                0f,
                0f,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER2,
                NOISE_POLLUTION.NOISY.TIER1);
            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(-1, 0);
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = new CellOffset(1, 0);
            def.LogicOutputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.OutputPort(
                    BatterySmart.PORT_ID,
                    new CellOffset(0, 0),
                    STRINGS.BUILDINGS.PREFABS.BATTERYSMART.LOGIC_PORT,
                    STRINGS.BUILDINGS.PREFABS.BATTERYSMART.LOGIC_PORT_ACTIVE,
                    STRINGS.BUILDINGS.PREFABS.BATTERYSMART.LOGIC_PORT_INACTIVE,
                    true),
            };
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            base.ConfigureBuildingTemplate(go, prefabTag);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.PowerBuilding);

            Storage storage = go.AddOrGet<Storage>();
            storage.capacityKg = GravityHydraulicBattery.SteelTankMassKg;
            storage.showInUI = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);
            go.AddOrGet<DropAllWorkable>();

            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.consumptionRate = IntakeKgPerSecond;
            consumer.capacityKG = GravityHydraulicBattery.DefaultTankMassKg;
            consumer.ignoreMinMassCheck = true;
            consumer.forceAlwaysSatisfied = true;
            consumer.alwaysConsume = true;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Store;
            consumer.storage = storage;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            GravityHydraulicBattery battery = go.AddOrGet<GravityHydraulicBattery>();
            battery.capacity = GravityHydraulicBattery.SteelTankMassKg * GravityBattery.JoulesPerKgPerTile * GravityBatteryBase.MaxCableTiles;
            battery.joulesLostPerSecond = 0f;
            PipeDrain.Configure(go, ConduitType.Liquid);
            base.DoPostConfigureComplete(go);
        }
    }
}
