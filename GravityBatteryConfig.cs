using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace BateriasFuturistas
{
    public class GravityBatteryConfig : BaseBatteryConfig
    {
        public const string ID = "GravityBattery";

        private const int Width = 3;
        private const int Height = 6;
        private const int Hitpoints = 30;
        private const float ConstructionTime = 60f;
        private const float MeltingPoint = 800f;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = CreateBuildingDef(
                ID,
                Width,
                Height,
                Hitpoints,
                "gravity_battery_kanim",
                ConstructionTime,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                MATERIALS.ALL_METALS,
                MeltingPoint,
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
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.PowerBuilding);

            Storage storage = go.AddOrGet<Storage>();
            storage.capacityKg = GravityBattery.SteelCableMassKg;
            storage.showInUI = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);
            storage.allowItemRemoval = true;
            storage.storageFilters = STORAGEFILTERS.NOT_EDIBLE_SOLIDS;
            storage.fetchCategory = Storage.FetchCategory.GeneralStorage;
            storage.showCapacityStatusItem = true;
            Prioritizable.AddRef(go);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            GravityBattery battery = go.AddOrGet<GravityBattery>();
            battery.capacity = GravityBattery.SteelCableMassKg * GravityBattery.JoulesPerKgPerTile * Height;
            battery.joulesLostPerSecond = 0f;
            base.DoPostConfigureComplete(go);
        }
    }
}
