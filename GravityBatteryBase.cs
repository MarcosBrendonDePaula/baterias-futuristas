using KSerialization;
using UnityEngine;

namespace BateriasFuturistas
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GravityBatteryBase : GravityBattery
    {
        public const int SteelCableRails = 100;
        public const int IronCableRails = 50;
        public const int DefaultCableRails = 20;
        public const float MaxCableTiles = 100f + GravityBatteryBaseConfig.HeightTiles;

        private const int MotorHeightTiles = 2;
        private const float ReferenceHeightTiles = 6f;
        private const float CableAnimationTiles = 105f;
        private const float WeightRestCenterTiles = 1.53f;
        private const float MotorClearanceTiles = 2.3f;
        private const float CableAttachAboveWeightTiles = 0.68f;
        private const float CableTopBelowRoofTiles = 1.27f;
        private const int MeasureEveryTicks = 5;
        private const float MotorRunningJoules = 0.5f;

        private MeterController weight;
        private MeterController cable;
        private int measuredHeight;
        private int measuredMotorCell = Grid.InvalidCell;
        private int ticksUntilMeasure;
        private string lastAssemblyLog;
        private GravityMotor motor;

        public bool IsComplete => measuredHeight > 0;

        public int MeasuredHeight => measuredHeight;

        public float WeightCenterTiles => weight.gameObject.transform.GetPosition().y - transform.GetPosition().y;

        public float ExpectedWeightCenterTiles
        {
            get
            {
                float top = Mathf.Max(WeightRestCenterTiles, measuredHeight - MotorClearanceTiles);
                return Mathf.Lerp(WeightRestCenterTiles, top, Mathf.Clamp01(JoulesAvailable / MassCapacity));
            }
        }

        protected override int HeightTiles => measuredHeight;

        // A taller tower keeps roughly the same fill time as the 6-tile reference.
        public override float ChargeWatts => MotorWatts * Mathf.Max(1f, measuredHeight / ReferenceHeightTiles);

        // An unfinished tower keeps its charge so it can be upgraded without losing energy.
        protected override float PreservedCharge => IsComplete ? 0f : JoulesAvailable;

        protected override int MotorCell =>
            measuredMotorCell == Grid.InvalidCell ? Grid.PosToCell(this) : measuredMotorCell;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            KBatchedAnimController controller = GetComponent<KBatchedAnimController>();
            weight = CreateVisual(controller, "weight");
            cable = CreateVisual(controller, "cable");
            cable.interpolateFunction = MeterController.StandardLerp;
            Measure();
        }

        private static MeterController CreateVisual(KBatchedAnimController controller, string animation)
        {
            var visual = new MeterController(controller, "meter_target", animation, Meter.Offset.Infront, Grid.SceneLayer.NoLayer);
            visual.meterController.visibilityType = KAnimControllerBase.VisibilityType.Always;
            // The tracker pins the visual to the building and only refreshes when the building moves.
            visual.gameObject.GetComponent<KBatchedAnimTracker>().enabled = false;
            return visual;
        }

        public override void EnergySim200ms(float dt)
        {
            if (--ticksUntilMeasure <= 0)
            {
                ticksUntilMeasure = MeasureEveryTicks;
                Measure();
            }
            base.EnergySim200ms(dt);
        }

        protected override void OnEnergyUpdated(float massCapacity)
        {
            UpdateMotor();
            if (!IsComplete)
            {
                PlaceVisuals(WeightRestCenterTiles, 0f);
                return;
            }
            float roof = measuredHeight - CableTopBelowRoofTiles;
            float top = Mathf.Max(WeightRestCenterTiles, measuredHeight - MotorClearanceTiles);
            float center = Mathf.Lerp(WeightRestCenterTiles, top, Mathf.Clamp01(JoulesAvailable / massCapacity));
            PlaceVisuals(center, Mathf.Max(0f, roof - (center + CableAttachAboveWeightTiles)));
        }

        private void UpdateMotor()
        {
            GravityMotor current = null;
            if (IsComplete)
            {
                GameObject occupant = Grid.Objects[measuredMotorCell, (int)ObjectLayer.Building];
                current = occupant != null ? occupant.GetComponent<GravityMotor>() : null;
            }
            if (motor != null && motor != current)
            {
                motor.SetState(GravityMotorState.Idle);
            }
            motor = current;
            if (motor != null)
            {
                motor.SetState(CurrentMotorState());
            }
        }

        private GravityMotorState CurrentMotorState()
        {
            if (LastStoredDelta > MotorRunningJoules)
            {
                return GravityMotorState.Charging;
            }
            return LastStoredDelta < -MotorRunningJoules ? GravityMotorState.Discharging : GravityMotorState.Idle;
        }

        private void PlaceVisuals(float weightCenter, float cableLength)
        {
            MoveVisual(weight, weightCenter);
            MoveVisual(cable, weightCenter + CableAttachAboveWeightTiles + cableLength);
            cable.SetPositionPercent(Mathf.Clamp01(cableLength / CableAnimationTiles));
        }

        private void MoveVisual(MeterController visual, float tilesAboveBase)
        {
            Vector3 origin = transform.GetPosition();
            float depth = visual.gameObject.transform.GetPosition().z;
            visual.gameObject.transform.SetPosition(new Vector3(origin.x, origin.y + tilesAboveBase, depth));
        }

        private void Measure()
        {
            int limit = RailLimitFor(primaryElement.ElementID);
            int cell = Grid.OffsetCell(Grid.PosToCell(this), 0, GravityBatteryBaseConfig.HeightTiles);
            for (int rails = 0; rails <= limit; rails++)
            {
                Tag piece = PieceAt(cell);
                if (piece == GravityBatteryMotorConfig.ID)
                {
                    measuredHeight = GravityBatteryBaseConfig.HeightTiles + rails + MotorHeightTiles;
                    measuredMotorCell = Grid.OffsetCell(cell, 0, MotorHeightTiles - 1);
                    LogAssembly(rails, limit, piece);
                    return;
                }
                if (piece != GravityBatteryRailConfig.ID)
                {
                    measuredHeight = 0;
                    measuredMotorCell = Grid.InvalidCell;
                    LogAssembly(rails, limit, piece);
                    return;
                }
                cell = Grid.CellAbove(cell);
            }
            measuredHeight = 0;
            measuredMotorCell = Grid.InvalidCell;
            LogAssembly(limit + 1, limit, Tag.Invalid);
        }

        private void LogAssembly(int rails, int limit, Tag stoppedAt)
        {
            string state = $"rails={rails} limit={limit} stoppedAt={stoppedAt} height={measuredHeight} motorCell={measuredMotorCell}";
            if (state != lastAssemblyLog)
            {
                lastAssemblyLog = state;
                Debug.Log($"[GravityBatteryBase] cell={Grid.PosToCell(this)} material={primaryElement.ElementID} " +
                    $"mass={storage.MassStored()} joules={JoulesAvailable} {state}");
            }
        }

        private static Tag PieceAt(int cell)
        {
            if (!Grid.IsValidCell(cell))
            {
                return Tag.Invalid;
            }
            GameObject occupant = Grid.Objects[cell, (int)ObjectLayer.Building];
            return occupant != null ? occupant.GetComponent<KPrefabID>().PrefabTag : Tag.Invalid;
        }

        private static int RailLimitFor(SimHashes material)
        {
            switch (material)
            {
                case SimHashes.Steel:
                    return SteelCableRails;
                case SimHashes.Iron:
                    return IronCableRails;
                default:
                    return DefaultCableRails;
            }
        }
    }
}
