using KSerialization;
using UnityEngine;

namespace BateriasFuturistas
{
    // Empties a battery's fluid through its output pipe while the player has draining enabled.
    [SerializationConfig(MemberSerialization.OptIn)]
    public class PipeDrain : KMonoBehaviour, ISim200ms
    {
        private const int RefreshUserMenuEvent = 493375141;

        public ConduitType conduitType;

        [Serialize]
        private bool draining;

        [MyCmpGet]
        private Storage storage;

        [MyCmpReq]
        private Building building;

        private static readonly EventSystem.IntraObjectHandler<PipeDrain> RefreshMenuDelegate =
            new EventSystem.IntraObjectHandler<PipeDrain>((component, data) => component.AddMenuButton());

        public bool Draining => draining;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe(RefreshUserMenuEvent, RefreshMenuDelegate);
        }

        public void SetDraining(bool value)
        {
            draining = value;
            Game.Instance.userMenu.Refresh(gameObject);
        }

        private void AddMenuButton()
        {
            string label = ModText.Get(draining ? "DRAIN_STOP" : "DRAIN_START");
            Game.Instance.userMenu.AddButton(
                gameObject,
                new KIconButtonMenu.ButtonInfo("action_empty_contents", label, () => SetDraining(!draining)));
        }

        public void Sim200ms(float dt)
        {
            if (!draining)
            {
                return;
            }
            ConduitFlow flow = conduitType == ConduitType.Gas ? Game.Instance.gasConduitFlow : Game.Instance.liquidConduitFlow;
            int cell = building.GetUtilityOutputCell();
            if (!flow.HasConduit(cell))
            {
                return;
            }
            foreach (GameObject item in storage.items)
            {
                PrimaryElement element = item != null ? item.GetComponent<PrimaryElement>() : null;
                if (element != null && element.Mass > 0f)
                {
                    float added = flow.AddElement(cell, element.ElementID, element.Mass, element.Temperature, byte.MaxValue, 0);
                    if (added > 0f)
                    {
                        storage.ConsumeIgnoringDisease(element.ElementID.CreateTag(), added);
                    }
                    return;
                }
            }
        }

        public static void Configure(GameObject go, ConduitType type)
        {
            RequireOutputs outputs = go.GetComponent<RequireOutputs>();
            if (outputs != null)
            {
                Object.DestroyImmediate(outputs);
            }
            go.AddOrGet<PipeDrain>().conduitType = type;
        }
    }
}
