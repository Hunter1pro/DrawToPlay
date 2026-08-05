using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Adds items to the Player scope's inventory — one §3.7 atom over
    /// <see cref="StateTreeInventoryUtil"/>'s encoding. The <see cref="itemId"/> is bindable,
    /// so a pickup flow can route WHAT was picked up into it the same way the UI routes
    /// clicks.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Inventory Add", fileName = "InventoryAdd")]
    [StateTreeCategory("Tasks/Items", "Add items to the Player-scope inventory")]
    public sealed class InventoryAddTask : StateTreeTaskAsset
    {
        public string itemId = "";

        public int count = 1;

        public string scopeId = "";

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(itemId) || count <= 0)
                return StateTreeStatus.Failure;

            StateTreeContextHost player = StateTreeContextHost.Resolve(context.owner,
                StateTreeContextKind.Player, scopeId);
            if (player == null)
                return StateTreeStatus.Failure;

            StateTreeInventoryUtil.SetCount(player.Context, itemId,
                StateTreeInventoryUtil.Count(player.Context, itemId) + count);
            return StateTreeStatus.Success;
        }
    }
}
