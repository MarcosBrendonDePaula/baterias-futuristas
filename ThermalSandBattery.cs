using System.Collections.Generic;
using KSerialization;
using TUNING;
using UnityEngine;

namespace BateriasFuturistas
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public class ThermalSandBattery : KMonoBehaviour, ISim200ms, IActivationRangeTarget
    {
        public static readonly HashedString RangePortId = "ThermalRangeLogicPort";

        [Serialize]
        private int rangeMinCelsius = 150;

        [Serialize]
        private int rangeMaxCelsius = 500;

        [MyCmpGet]
        private LogicPorts logicPorts;

        public float ActivateValue
        {
            get => rangeMaxCelsius;
            set => rangeMaxCelsius = (int)value;
        }

        public float DeactivateValue
        {
            get => rangeMinCelsius;
            set => rangeMinCelsius = (int)value;
        }

        public float MinValue => 0f;

        public float MaxValue => 500f;

        public bool UseWholeNumbers => true;

        public string ActivateTooltip => ModText.Get("RANGE_MAX_TIP");

        public string DeactivateTooltip => ModText.Get("RANGE_MIN_TIP");

        public string ActivationRangeTitleText => ModText.Get("RANGE_TITLE");

        public string ActivateSliderLabelText => ModText.Get("RANGE_MAX");

        public string DeactivateSliderLabelText => ModText.Get("RANGE_MIN");

        public bool TemperatureInRange
        {
            get
            {
                float celsius = Temperature - 273.15f;
                return celsius >= rangeMinCelsius && celsius <= rangeMaxCelsius;
            }
        }

        public const float MaxTemperature = 773.15f;
        public const float MinSteamTemperature = 423.15f;
        public const float HeaterKdtuPerSecond = 1000f;
        public const float WaterKgPerSecond = 4f;
        public const float SandHeatCapacityKdtuPerKg = 0.83f;
        public const float WaterHeatCapacityKdtuPerKg = 4.179f;
        public const float MaxSteamStoredKg = 20f;

        private const float RoomTemperature = 293.15f;
        private const float SteamMargin = 10f;

        [MyCmpReq]
        private Operational operational;

        [MyCmpGet]
        private KBatchedAnimController animation;

        [MyCmpReq]
        private Building building;

        private Storage sandStorage;
        private Storage fluidStorage;
        private MeterController glow;

        public float SandMass => sandStorage.MassStored();

        public float Temperature
        {
            get
            {
                float mass = 0f;
                float heat = 0f;
                foreach (PrimaryElement sand in Sand())
                {
                    mass += sand.Mass;
                    heat += sand.Mass * sand.Temperature;
                }
                return mass > 0f ? heat / mass : RoomTemperature;
            }
        }

        public float SteamStored => fluidStorage.GetMassAvailable(SimHashes.Steam.CreateTag());

        public float WaterStored => fluidStorage.GetMassAvailable(SimHashes.Water.CreateTag());

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Storage[] storages = GetComponents<Storage>();
            sandStorage = storages[0];
            fluidStorage = storages[1];
            glow = new MeterController(animation, "meter_target", "meter", Meter.Offset.Infront, Grid.SceneLayer.NoLayer);
        }

        public void Sim200ms(float dt)
        {
            bool canHeat = operational.IsOperational && SandMass > 0f && Temperature < MaxTemperature;
            operational.SetActive(canHeat);
            if (canHeat)
            {
                ApplyHeater(dt);
            }
            MakeSteam(dt);
            DispenseSteam();
            logicPorts.SendSignal(RangePortId, TemperatureInRange ? 1 : 0);
            glow.SetPositionPercent(Mathf.InverseLerp(RoomTemperature, MaxTemperature, Temperature));
        }

        public void ApplyHeater(float seconds)
        {
            if (SandMass > 0f)
            {
                float rise = HeaterKdtuPerSecond * seconds / (SandMass * SandHeatCapacityKdtuPerKg);
                SetTemperature(Mathf.Min(MaxTemperature, Temperature + rise));
            }
        }

        public void SetTemperature(float kelvin)
        {
            foreach (PrimaryElement sand in Sand())
            {
                sand.Temperature = kelvin;
            }
        }

        private IEnumerable<PrimaryElement> Sand()
        {
            foreach (GameObject item in sandStorage.items)
            {
                if (item != null)
                {
                    yield return item.GetComponent<PrimaryElement>();
                }
            }
        }

        private void MakeSteam(float dt)
        {
            float temperature = Temperature;
            float room = MaxSteamStoredKg - SteamStored;
            if (temperature < MinSteamTemperature || SandMass <= 0f || room <= 0f)
            {
                return;
            }
            float water = Mathf.Min(WaterKgPerSecond * dt, WaterStored, room);
            if (water <= 0f)
            {
                return;
            }
            float inlet = WaterTemperature();
            float outlet = Mathf.Max(MinSteamTemperature, temperature - SteamMargin);
            float heat = water * WaterHeatCapacityKdtuPerKg * (outlet - inlet);
            SetTemperature(temperature - heat / (SandMass * SandHeatCapacityKdtuPerKg));
            fluidStorage.ConsumeIgnoringDisease(SimHashes.Water.CreateTag(), water);
            fluidStorage.AddGasChunk(SimHashes.Steam, water, outlet, byte.MaxValue, 0, false);
        }

        // ConduitDispenser would disable the whole building while no pipe is attached.
        private void DispenseSteam()
        {
            int cell = building.GetUtilityOutputCell();
            ConduitFlow flow = Game.Instance.gasConduitFlow;
            if (!flow.HasConduit(cell))
            {
                return;
            }
            foreach (GameObject item in fluidStorage.items)
            {
                PrimaryElement element = item != null ? item.GetComponent<PrimaryElement>() : null;
                if (element != null && element.ElementID == SimHashes.Steam && element.Mass > 0f)
                {
                    float added = flow.AddElement(cell, SimHashes.Steam, element.Mass, element.Temperature, byte.MaxValue, 0);
                    if (added > 0f)
                    {
                        fluidStorage.ConsumeIgnoringDisease(SimHashes.Steam.CreateTag(), added);
                    }
                    return;
                }
            }
        }

        private float WaterTemperature()
        {
            foreach (GameObject item in fluidStorage.items)
            {
                PrimaryElement element = item != null ? item.GetComponent<PrimaryElement>() : null;
                if (element != null && element.ElementID == SimHashes.Water)
                {
                    return element.Temperature;
                }
            }
            return RoomTemperature;
        }
    }

    public class ThermalSandBatteryConfig : IBuildingConfig
    {
        public const string ID = "ThermalSandBattery";

        public const int HalfWidth = 2;
        private const int Height = 8;
        private const float MaxSandKg = 30000f;
        private const float RefillSandKg = 26000f;
        private const float HeaterWatts = 2000f;
        private const float WaterBufferKg = 20f;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                ID,
                2 * HalfWidth + 1,
                Height,
                "thermal_sand_kanim",
                100,
                120f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                MATERIALS.ALL_METALS,
                1600f,
                BuildLocationRule.OnFloor,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER2,
                NOISE_POLLUTION.NOISY.TIER1);
            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = HeaterWatts;
            def.PowerInputOffset = new CellOffset(0, 0);
            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(-HalfWidth, 0);
            def.OutputConduitType = ConduitType.Gas;
            def.UtilityOutputOffset = new CellOffset(HalfWidth, Height - 1);
            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(HalfWidth, 0));
            def.LogicOutputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.OutputPort(
                    ThermalSandBattery.RangePortId,
                    new CellOffset(-1, 0),
                    ModText.Get("SENSOR_PORT"),
                    ModText.Get("SENSOR_ACTIVE"),
                    ModText.Get("SENSOR_INACTIVE"),
                    true),
            };
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.Power.ID;
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.PowerBuilding);
            go.AddOrGet<LogicOperationalController>();
            Prioritizable.AddRef(go);

            Storage sand = go.AddComponent<Storage>();
            sand.capacityKg = MaxSandKg;
            sand.showInUI = true;
            sand.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);

            Storage fluids = go.AddComponent<Storage>();
            fluids.capacityKg = WaterBufferKg + ThermalSandBattery.MaxSteamStoredKg;
            fluids.showInUI = true;
            fluids.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);

            ManualDeliveryKG delivery = go.AddOrGet<ManualDeliveryKG>();
            delivery.SetStorage(sand);
            delivery.RequestedItemTag = SimHashes.Sand.CreateTag();
            delivery.capacity = MaxSandKg;
            delivery.refillMass = RefillSandKg;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.MachineFetch.IdHash;
            delivery.operationalRequirement = Operational.State.None;

            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.consumptionRate = ThermalSandBattery.WaterKgPerSecond * 2f;
            consumer.capacityKG = WaterBufferKg;
            consumer.capacityTag = SimHashes.Water.CreateTag();
            consumer.forceAlwaysSatisfied = true;
            consumer.alwaysConsume = true;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;
            consumer.storage = fluids;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            // The steam pipe is optional: the sand can heat up before an outlet exists.
            RequireOutputs outputs = go.GetComponent<RequireOutputs>();
            if (outputs != null)
            {
                Object.DestroyImmediate(outputs);
            }
            go.AddOrGet<ThermalSandBattery>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }
}
