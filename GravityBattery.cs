using KSerialization;
using UnityEngine;

namespace BateriasFuturistas
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GravityBattery : BatterySmart, IUserControlledCapacity
    {
        public const float JoulesPerKgPerTile = 50f / 6f;
        public const float SteelCableMassKg = 16000f;
        public const float IronCableMassKg = 8000f;
        public const float DefaultCableMassKg = 4000f;
        public const float MotorWatts = 480f;

        private const float ChargeLossFraction = 0.05f;
        private const float DischargeEfficiency = 0.97f;
        private const float MaxHeatTemperature = 1000f;
        private const float MinimumCapacity = 1f;

        [MyCmpReq]
        protected Storage storage;

        [MyCmpReq]
        protected PrimaryElement primaryElement;

        [MyCmpReq]
        protected Building building;

        [Serialize]
        private float lastJoules;

        [Serialize]
        private float userMaxCapacity = float.PositiveInfinity;

        private FilteredStorage filteredStorage;

        protected float LastStoredDelta { get; private set; }

        protected virtual int HeightTiles => building.Def.HeightInCells;

        protected virtual int MotorCell => Grid.OffsetCell(Grid.PosToCell(this), 0, building.Def.HeightInCells - 1);

        private float MassCapacity =>
            Mathf.Max(MinimumCapacity, storage.MassStored() * JoulesPerKgPerTile * HeightTiles);

        public float UserMaxCapacity
        {
            get => Mathf.Min(userMaxCapacity, storage.capacityKg);
            set
            {
                userMaxCapacity = value;
                filteredStorage.FilterChanged();
            }
        }

        public float AmountStored => storage.MassStored();

        public float MinCapacity => 0f;

        public float MaxCapacity => storage.capacityKg;

        public bool WholeValues => false;

        public LocString CapacityUnits => GameUtil.GetCurrentMassUnit();

        public bool ControlEnabled() => true;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            filteredStorage = new FilteredStorage(this, null, this, false, Db.Get().ChoreTypes.PowerFetch);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            storage.capacityKg = MaxMassFor(primaryElement.ElementID);
            filteredStorage.FilterChanged();
        }

        protected override void OnCleanUp()
        {
            filteredStorage.CleanUp();
            base.OnCleanUp();
        }

        public override void EnergySim200ms(float dt)
        {
            float massCapacity = MassCapacity;
            capacity = massCapacity;
            base.EnergySim200ms(dt);

            LastStoredDelta = JoulesAvailable - lastJoules;
            float loss = LossFor(LastStoredDelta);
            ConsumeEnergy(loss, false);
            EmitHeat(loss);
            RemoveChargeAboveCapacity(massCapacity);

            lastJoules = JoulesAvailable;
            OnEnergyUpdated(massCapacity);
            // CircuitManager ignores chargeWattage on batteries; spare capacity is what limits charging.
            capacity = Mathf.Min(massCapacity, JoulesAvailable + MotorWatts * dt);
        }

        protected virtual void OnEnergyUpdated(float massCapacity)
        {
        }

        private static float MaxMassFor(SimHashes material)
        {
            switch (material)
            {
                case SimHashes.Steel:
                    return SteelCableMassKg;
                case SimHashes.Iron:
                    return IronCableMassKg;
                default:
                    return DefaultCableMassKg;
            }
        }

        private float LossFor(float storedDelta)
        {
            if (storedDelta > 0f)
            {
                return storedDelta * ChargeLossFraction;
            }
            float delivered = -storedDelta;
            return Mathf.Min(JoulesAvailable, delivered * (1f / DischargeEfficiency - 1f));
        }

        private void EmitHeat(float lossJoules)
        {
            if (lossJoules > 0f)
            {
                SimMessages.ModifyEnergy(
                    MotorCell,
                    lossJoules / 1000f,
                    MaxHeatTemperature,
                    SimMessages.EnergySourceID.StructureTemperature);
            }
        }

        private void RemoveChargeAboveCapacity(float massCapacity)
        {
            if (JoulesAvailable > massCapacity)
            {
                ConsumeEnergy(JoulesAvailable - massCapacity, false);
            }
        }
    }
}
