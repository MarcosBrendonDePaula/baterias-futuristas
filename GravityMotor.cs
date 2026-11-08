namespace BateriasFuturistas
{
    public class GravityMotor : KMonoBehaviour
    {
        private const string LoopSoundName = "ManualPressureDoor_gear_LP";

        [MyCmpGet]
        private KBatchedAnimController animation;

        [MyCmpGet]
        private LoopingSounds sounds;

        private bool running;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            animation.Play("on");
        }

        public void SetRunning(bool value)
        {
            if (running == value)
            {
                return;
            }
            running = value;
            string sound = GlobalAssets.GetSound(LoopSoundName);
            if (running)
            {
                animation.Play("working_loop", KAnim.PlayMode.Loop);
                sounds.StartSound(sound);
            }
            else
            {
                animation.Play("on");
                sounds.StopSound(sound);
            }
        }
    }
}
