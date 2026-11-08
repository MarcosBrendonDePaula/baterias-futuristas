using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace BateriasFuturistas
{
    public class GravityBatteryBaseConfig : BaseBatteryConfig
    {
        public const string ID = "GravityBatteryBase";
        public const int HeightTiles = 2;

        private const int Width = 3;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = CreateBuildingDef(
                ID,
                Width,
                HeightTiles,
                30,
                "gravity_base_kanim",
                60f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                MATERIALS.ALL_METALS,
                800f,
                0f,
                0f,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER2,
                NOISE_POLLUTION.NOISY.TIER1);
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
            GravityStorage.Configure(go);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            GravityBatteryBase battery = go.AddOrGet<GravityBatteryBase>();
            battery.capacity = GravityBattery.SteelCableMassKg * GravityBattery.JoulesPerKgPerTile * GravityBatteryBase.MaxCableTiles;
            battery.joulesLostPerSecond = 0f;
            base.DoPostConfigureComplete(go);
        }
    }

    public static class GravityStorage
    {
        public static void Configure(GameObject go)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.PowerBuilding);
            Storage storage = go.AddOrGet<Storage>();
            storage.capacityKg = GravityBattery.SteelCableMassKg;
            storage.showInUI = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);
            storage.allowItemRemoval = true;
            storage.storageFilters = STORAGEFILTERS.NOT_EDIBLE_SOLIDS;
            storage.fetchCategory = Storage.FetchCategory.GeneralStorage;
            storage.showCapacityStatusItem = true;
            go.AddOrGet<DropAllWorkable>();
            Prioritizable.AddRef(go);
        }
    }

    public abstract class GravityStructureConfig : IBuildingConfig
    {
        protected BuildingDef CreateStructure(string id, int height, string anim)
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id,
                3,
                height,
                anim,
                30,
                30f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER2,
                MATERIALS.ALL_METALS,
                800f,
                BuildLocationRule.Anywhere,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER1,
                NOISE_POLLUTION.NONE);
            def.Floodable = false;
            def.Entombable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.Power.ID;
            return def;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
        }
    }

    public class GravityBatteryRailConfig : GravityStructureConfig
    {
        public const string ID = "GravityBatteryRail";

        public override BuildingDef CreateBuildingDef() => CreateStructure(ID, 1, "gravity_rail_kanim");
    }

    public class GravityBatteryMotorConfig : GravityStructureConfig
    {
        public const string ID = "GravityBatteryMotor";

        public override BuildingDef CreateBuildingDef() => CreateStructure(ID, 2, "gravity_motor_kanim");

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LoopingSounds>();
            go.AddOrGet<GravityMotor>();
        }
    }
}
