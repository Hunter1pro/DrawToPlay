using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True when the item named by a blackboard key is of one <see cref="ItemKind"/> — the
    /// notebook's item-click BRANCH ("weapon equips, consumable shows a tooltip") as a plain
    /// transition condition: the click transition routes the id onto the blackboard, and two
    /// transitions out of the item-flow state ask this with different kinds. The same test is
    /// a value node in graphs for the Blueprint-flavored version of the same branch.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Item Kind", fileName = "ItemKind")]
    [StateTreeCategory("Conditions/Items", "The item under a blackboard key is of one kind")]
    public sealed class ItemKindCondition : StateTreeConditionAsset
    {
        /// <summary>Blackboard key holding the item id — the key the click transition routed
        /// ("clickedItem" in the inventory flow).</summary>
        public string itemIdKey = "clickedItem";

        public ItemRegistryAsset registry;

        public ItemKind kind = ItemKind.Weapon;

        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool matches = false;
            if (context != null && registry != null && !string.IsNullOrEmpty(itemIdKey)
                && context.blackboard.TryGetValue(itemIdKey, out object held)
                && held is string itemId
                && registry.TryGet(itemId, out ItemDefAsset def))
            {
                matches = def.kind == kind;
            }
            return invert ? !matches : matches;
        }
    }
}
