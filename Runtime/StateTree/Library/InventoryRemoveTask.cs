using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Removes items from the Player scope's inventory — and FAILS when there are not enough,
    /// which is the branchable answer a spend/consume flow wants ("no potion left" is a
    /// transition, not an exception). Removal is all-or-nothing: a partial spend would leave
    /// the tree believing something it did not pay for.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Inventory Remove", fileName = "InventoryRemove")]
    [StateTreeCategory("Tasks/Items", "Remove items from the Player-scope inventory; Fails when short")]
    public sealed class InventoryRemoveTask : StateTreeTaskAsset
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

            int held = StateTreeInventoryUtil.Count(player.Context, itemId);
            if (held < count)
                return StateTreeStatus.Failure;

            StateTreeInventoryUtil.SetCount(player.Context, itemId, held - count);
            return StateTreeStatus.Success;
        }
    }
}
