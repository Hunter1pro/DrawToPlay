using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The flow's accent beat: flash an item's cell in the bag. The item comes
    /// from the picked row, or dynamically from a blackboard key (the request that started
    /// the flow) — the key wins when both are set. A missing cell is a quiet no-op.</summary>
    [StateTreeCategory("Tasks/UI", "Flash an item's cell in the bag")]
    public sealed class FlashItemTask : StateTreeTaskAsset
    {
        [Tooltip("The item row — picked from the item registry.")]
        public StateTreeEntryRef<ItemDef> item = new StateTreeEntryRef<ItemDef>();

        [Tooltip("Optional: a blackboard key holding the item's name — the flow's request "
            + "value. Wins over the picked row when it resolves.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField itemKey = new StateTreeKeyField();

        [Tooltip("The bag's UI row — picked from the UI registry.")]
        public StateTreeEntryRef<UiDef> ui = new StateTreeEntryRef<UiDef>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;
            InventoryWidgetView widget = RedrawBagTask.FindBag(context, ui.entryName);
            if (widget == null)
                return StateTreeStatus.Success;

            string itemName = item.entryName;
            string key = itemKey;
            if (!string.IsNullOrEmpty(key)
                && context.blackboard.TryGetValue(key, out object held)
                && held is string dynamicName && !string.IsNullOrEmpty(dynamicName))
                itemName = dynamicName;

            widget.Flash(itemName);
            return StateTreeStatus.Success;
        }
    }
}
