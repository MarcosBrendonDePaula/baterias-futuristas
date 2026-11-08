namespace BateriasFuturistas
{
    public enum GravityMotorState
    {
        Idle,
        Charging,
        Discharging,
    }

    public class GravityMotor : KMonoBehaviour
    {
        private const string LoopSoundName = "ManualPressureDoor_gear_LP";

        [MyCmpGet]
        private KBatchedAnimController animation;

        [MyCmpGet]
        private LoopingSounds sounds;

        private GravityMotorState state = GravityMotorState.Idle;

        public GravityMotorState State => state;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            animation.Play("on");
        }

        public void SetState(GravityMotorState value)
        {
            if (state == value)
            {
                return;
            }
            bool wasRunning = state != GravityMotorState.Idle;
            state = value;
            string sound = GlobalAssets.GetSound(LoopSoundName);
            switch (state)
            {
                case GravityMotorState.Charging:
                    animation.Play("working_loop", KAnim.PlayMode.Loop);
                    break;
                case GravityMotorState.Discharging:
                    animation.Play("discharge_loop", KAnim.PlayMode.Loop);
                    break;
                default:
                    animation.Play("on");
                    sounds.StopSound(sound);
                    return;
            }
            if (!wasRunning)
            {
                sounds.StartSound(sound);
            }
        }
    }
}
