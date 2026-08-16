using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Wear a carried item from a tree — a reward step equipping what a dialog
    /// just gave. The slot comes from the item's own row; an occupied slot swaps.</summary>
    [StateTreeCategory("Tasks/Items", "Equip a picked item into its declared slot")]
    public sealed class EquipItemTask : StateTreeTaskAsset
    {
        [Tooltip("The item row — picked from the item registry. It must declare a slot.")]
        public StateTreeEntryRef<ItemDef> item = new StateTreeEntryRef<ItemDef>();

        [Tooltip("Optional: a blackboard key holding the item's name — the bag's request "
            + "value. Wins over the picked row when it resolves.")]
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
            return inventory.Equip(ItemTaskName.Resolve(context, itemKey, item.entryName))
                ? StateTreeStatus.Success
                : StateTreeStatus.Failure;
        }
    }
}
