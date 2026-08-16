using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Take a slot's item off from a tree — the bag's take-off request served, or
    /// an authored "disarm before the cutscene" step. The slot comes from the picked row,
    /// or dynamically from a blackboard key holding a slot id; the key wins. Emptying an
    /// already empty slot is a done no-op, not an error.</summary>
    [StateTreeCategory("Tasks/Items", "Take off whatever a slot holds")]
    public sealed class UnequipItemTask : StateTreeTaskAsset
    {
        [Tooltip("The slot row — picked from the slot registry.")]
        public StateTreeEntryRef<EquipmentSlotDef> slot = new StateTreeEntryRef<EquipmentSlotDef>();

        [Tooltip("Optional: a blackboard key holding a slot ID — the bag's request value. "
            + "Wins over the picked row when it resolves.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField slotKey = new StateTreeKeyField();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;
            InventoryService inventory =
                StateTreeContextHost.FindService<InventoryService>(context.owner);
            if (inventory == null)
                return StateTreeStatus.Failure;

            string slotRef = ItemTaskName.Resolve(context, slotKey, slot.entryId);
            if (string.IsNullOrEmpty(slotRef))
                return StateTreeStatus.Failure;
            // Typed request values name ROWS (§4d); the domain speaks ids — a name
            // resolves through the slot catalog, and an id passes through untouched.
            EquipmentSlotRegistry slots = inventory.Slots();
            var named = slots != null ? slots.FindByName(slotRef) as EquipmentSlotDef : null;
            inventory.Unequip(named != null ? named.id : slotRef);
            return StateTreeStatus.Success;
        }
    }
}
