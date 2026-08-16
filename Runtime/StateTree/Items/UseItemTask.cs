using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Drink the potion from a tree — the consumable verb as a task: spend one of
    /// the picked item and apply its use effect to the player. Success when it landed;
    /// Failure when the item is not usable or not carried (a tree can branch on an empty
    /// bag). Publishes a 'result' output — an <see cref="ItemUseResult"/> contract payload
    /// (§4d) a transition can route to one key for whoever cares what was just drunk.</summary>
    [StateTreeCategory("Tasks/Items", "Use one of a picked consumable item")]
    [TaskOutputContract("result", typeof(ItemUseResult),
        "What the use came to — the item's definition, its name, and whether it landed.")]
    public sealed class UseItemTask : StateTreeTaskAsset, IStateTreeOutputSource
    {
        [Tooltip("The item row — picked from the item registry.")]
        public StateTreeEntryRef<ItemDef> item = new StateTreeEntryRef<ItemDef>();

        [Tooltip("Optional: a blackboard key holding the item's name — the bag's request "
            + "value (the LoadLevel levelNameKey shape). Wins over the picked row when it "
            + "resolves.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField itemKey = new StateTreeKeyField();

        [NonSerialized] private ItemUseResult m_Result;

        public override void OnEnter(StateTreeContext context)
        {
            m_Result = null;
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;
            InventoryService inventory =
                StateTreeContextHost.FindService<InventoryService>(context.owner);
            if (inventory == null)
                return StateTreeStatus.Failure;
            string itemName = ItemTaskName.Resolve(context, itemKey, item.entryName);
            bool used = inventory.Use(itemName);
            m_Result = new ItemUseResult
            {
                item = inventory.Row(itemName),
                itemName = itemName ?? "",
                used = used
            };
            return used ? StateTreeStatus.Success : StateTreeStatus.Failure;
        }

        /// <summary>The contract payload rides the output channel whole; the item's name
        /// stays in the string slot as the degraded view for scalar readers.</summary>
        public bool TryCollectOutputs(List<TaskOutputValue> into)
        {
            if (m_Result == null || into == null)
                return false;
            into.Add(new TaskOutputValue
            {
                name = "result",
                kind = GraphTaskParameterKind.String,
                stringValue = m_Result.itemName,
                objectValue = m_Result
            });
            return true;
        }
    }
}
