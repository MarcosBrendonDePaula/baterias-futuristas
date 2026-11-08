using System.Collections.Generic;
using KSerialization;
using TUNING;
using UnityEngine;

namespace BateriasFuturistas
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public class MassBattery : BatterySmart
    {
        private const float MaxHeatTemperature = 1000f;
        private const float MinimumCapacity = 1f;

        public float joulesPerKg;
        public float fixedCapacity;
        public float chargeLoss;
        public float dischargeLoss;
        public float idleLossPerSecond;
        public bool coolsOnDischarge;

        [MyCmpGet]
        private Storage storage;

        [Serialize]
        private float lastJoules;

        public float StoredMass => storage != null ? storage.MassStored() : 0f;

        public float FullCapacity =>
            fixedCapacity > 0f ? fixedCapacity : Mathf.Max(MinimumCapacity, StoredMass * joulesPerKg);

        public override void EnergySim200ms(float dt)
        {
            capacity = FullCapacity;
            joulesLostPerSecond = 0f;
            base.EnergySim200ms(dt);

            float delta = JoulesAvailable - lastJoules;
            float loss = delta > 0f
                ? delta * chargeLoss
                : Mathf.Min(JoulesAvailable, -delta * (1f / (1f - dischargeLoss) - 1f));
            float idle = Mathf.Min(JoulesAvailable, JoulesAvailable * idleLossPerSecond * dt);
            ConsumeEnergy(loss + idle, false);
            ApplyHeat(delta, loss + idle);
            if (JoulesAvailable > capacity)
            {
                ConsumeEnergy(JoulesAvailable - capacity, false);
            }
            lastJoules = JoulesAvailable;
        }

        private void ApplyHeat(float delta, float lostJoules)
        {
            if (lostJoules <= 0f)
            {
                return;
            }
            float kdtu = lostJoules / 1000f;
            SimMessages.ModifyEnergy(
                Grid.PosToCell(this),
                coolsOnDischarge && delta < 0f ? -kdtu : kdtu,
                MaxHeatTemperature,
                SimMessages.EnergySourceID.StructureTemperature);
        }
    }

    public abstract class FutureBatteryConfig : BaseBatteryConfig
    {
        protected abstract string Id { get; }

        protected abstract int Width { get; }

        protected abstract int Height { get; }

        protected abstract string Anim { get; }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = CreateBuildingDef(
                Id,
                Width,
                Height,
                60,
                Anim,
                120f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                MATERIALS.ALL_METALS,
                1600f,
                0f,
                0f,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER2,
                NOISE_POLLUTION.NOISY.TIER2);
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
            ConfigureDef(def);
            return def;
        }

        protected virtual void ConfigureDef(BuildingDef def)
        {
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            base.ConfigureBuildingTemplate(go, prefabTag);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.PowerBuilding);
        }

        protected static Storage AddStorage(GameObject go, float capacityKg)
        {
            Storage storage = go.AddOrGet<Storage>();
            storage.capacityKg = capacityKg;
            storage.showInUI = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);
            go.AddOrGet<DropAllWorkable>();
            return storage;
        }

        protected static void AddPipeIntake(GameObject go, ConduitType type, Storage storage, float kgPerSecond)
        {
            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = type;
            consumer.consumptionRate = kgPerSecond;
            consumer.capacityKG = storage.capacityKg;
            consumer.ignoreMinMassCheck = true;
            consumer.forceAlwaysSatisfied = true;
            consumer.alwaysConsume = true;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Store;
            consumer.storage = storage;
        }

        protected MassBattery AddBattery(GameObject go, float joulesPerKg, float fixedCapacity, float maxJoules)
        {
            MassBattery battery = go.AddOrGet<MassBattery>();
            battery.joulesPerKg = joulesPerKg;
            battery.fixedCapacity = fixedCapacity;
            battery.capacity = maxJoules;
            battery.joulesLostPerSecond = 0f;
            return battery;
        }
    }

    public class FlywheelBatteryConfig : FutureBatteryConfig
    {
        public const string ID = "FlywheelBattery";
        public const float Capacity = 300000f;
        public const float IdleLossPerSecond = 0.002f;

        protected override string Id => ID;
        protected override int Width => 4;
        protected override int Height => 5;
        protected override string Anim => "flywheel_kanim";

        public override void DoPostConfigureComplete(GameObject go)
        {
            MassBattery battery = AddBattery(go, 0f, Capacity, Capacity);
            battery.chargeLoss = 0.02f;
            battery.dischargeLoss = 0.02f;
            battery.idleLossPerSecond = IdleLossPerSecond;
            base.DoPostConfigureComplete(go);
        }
    }

    public class CompressedAirBatteryConfig : FutureBatteryConfig
    {
        public const string ID = "CompressedAirBattery";
        public const float MaxKg = 1200f;
        public const float JoulesPerKg = 1000f;
        public const float Loss = 0.10f;

        protected override string Id => ID;
        protected override int Width => 4;
        protected override int Height => 7;
        protected override string Anim => "compressed_air_kanim";

        protected override void ConfigureDef(BuildingDef def)
        {
            def.InputConduitType = ConduitType.Gas;
            def.UtilityInputOffset = new CellOffset(-1, 0);
            def.OutputConduitType = ConduitType.Gas;
            def.UtilityOutputOffset = new CellOffset(2, 0);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            base.ConfigureBuildingTemplate(go, prefabTag);
            AddPipeIntake(go, ConduitType.Gas, AddStorage(go, MaxKg), 1f);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            MassBattery battery = AddBattery(go, JoulesPerKg, 0f, MaxKg * JoulesPerKg);
            battery.chargeLoss = Loss;
            battery.dischargeLoss = Loss;
            battery.coolsOnDischarge = true;
            PipeDrain.Configure(go, ConduitType.Gas);
            base.DoPostConfigureComplete(go);
        }
    }

    public class FlowBatteryConfig : FutureBatteryConfig
    {
        public const string ID = "FlowBattery";
        public const float MaxKg = 18000f;
        public const float JoulesPerKg = 200f;
        public const float Loss = 0.12f;

        protected override string Id => ID;
        protected override int Width => 6;
        protected override int Height => 8;
        protected override string Anim => "flow_battery_kanim";

        protected override void ConfigureDef(BuildingDef def)
        {
            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(-2, 0);
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = new CellOffset(3, 0);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            base.ConfigureBuildingTemplate(go, prefabTag);
            AddPipeIntake(go, ConduitType.Liquid, AddStorage(go, MaxKg), 10f);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            MassBattery battery = AddBattery(go, JoulesPerKg, 0f, MaxKg * JoulesPerKg);
            battery.chargeLoss = Loss;
            battery.dischargeLoss = Loss;
            PipeDrain.Configure(go, ConduitType.Liquid);
            base.DoPostConfigureComplete(go);
        }
    }

    public class MoltenSaltBatteryConfig : FutureBatteryConfig
    {
        public const string ID = "MoltenSaltBattery";
        public const float MaxKg = 60000f;
        public const float JoulesPerKg = 250f;
        public const float Loss = 0.05f;
        public const float IdleLossPerSecond = 0.00002f;

        protected override string Id => ID;
        protected override int Width => 6;
        protected override int Height => 10;
        protected override string Anim => "molten_salt_kanim";

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            base.ConfigureBuildingTemplate(go, prefabTag);
            Storage storage = AddStorage(go, MaxKg);
            ManualDeliveryKG delivery = go.AddOrGet<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = SimHashes.Salt.CreateTag();
            delivery.capacity = MaxKg;
            delivery.refillMass = MaxKg * 0.875f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.PowerFetch.IdHash;
            delivery.operationalRequirement = Operational.State.None;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            MassBattery battery = AddBattery(go, JoulesPerKg, 0f, MaxKg * JoulesPerKg);
            battery.chargeLoss = Loss;
            battery.dischargeLoss = Loss;
            battery.idleLossPerSecond = IdleLossPerSecond;
            base.DoPostConfigureComplete(go);
        }
    }
}
