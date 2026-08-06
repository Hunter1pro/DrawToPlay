using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Pushes one item's detail text into a screen — what a TOOLTIP state runs before its
    /// <see cref="ShowScreenTask"/>. The <see cref="itemId"/> field is a plain bindable
    /// string, and that is the whole trick: link it with ⚑ to the key the click transition
    /// routed ("clickedItem") and the tooltip shows whatever was clicked — the M7k entry-time
    /// binding closing the loop from a row click to the next screen's content, no code
    /// between.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/UI/Bind Item Detail", fileName = "BindItemDetail")]
    [StateTreeCategory("Tasks/UI", "Push one item's detail text into a screen (bind itemId to the routed click)")]
    public sealed class BindItemDetailTask : StateTreeTaskAsset
    {
        public string screenId = "";

        public ItemRegistryAsset registry;

        /// <summary>The item to describe — bind it (⚑) to the blackboard key the click
        /// transition routes into this state.</summary>
        public string itemId = "";

        /// <summary>Text in front of the item's description — "EQUIPPED: ", "Sold: " — so the
        /// same atom labels what HAPPENED, not just what the item is.</summary>
        public string prefix = "";

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || registry == null || string.IsNullOrEmpty(itemId))
                return StateTreeStatus.Failure;

            UIService service = StateTreeContextHost.FindService<UIService>(context.owner);
            UIScreenBehaviour screen = service != null ? service.Find(screenId) : null;
            if (screen == null)
                return StateTreeStatus.Failure;

            if (!registry.TryGet(itemId, out ItemDefAsset def))
            {
                screen.SetDetail(prefix + itemId + " (unknown item)");
                return StateTreeStatus.Failure;
            }

            screen.SetDetail(prefix + def.displayName + " — " + def.kind);
            return StateTreeStatus.Success;
        }
    }
}
