using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Drink the potion from a tree — the consumable verb as a task: spend one of
    /// the picked item and apply its use effect to the player. Success when it landed;
    /// Failure when the item is not usable or not carried (a tree can branch on an empty
    /// bag).</summary>
    [StateTreeCategory("Tasks/Items", "Use one of a picked consumable item")]
    public sealed class UseItemTask : StateTreeTaskAsset
    {
        [Tooltip("The item row — picked from the item registry.")]
        public StateTreeEntryRef<ItemDef> item = new StateTreeEntryRef<ItemDef>();

        [Tooltip("Optional: a blackboard key holding the item's name — the bag's request "
            + "value (the LoadLevel levelNameKey shape). Wins over the picked row when it "
            + "resolves.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField itemKey = new StateTreeKeyField();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;
            InventoryService inventory =
                StateTreeContextHost.FindService<InventoryService>(context.owner);
            if (inventory == null)
                return StateTreeStatus.Failure;
            return inventory.Use(ItemTaskName.Resolve(context, itemKey, item.entryName))
                ? StateTreeStatus.Success
                : StateTreeStatus.Failure;
        }
    }
}
