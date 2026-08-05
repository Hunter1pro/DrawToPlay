using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>What flavor of thing an item is — the fact the item-click flow branches on
    /// (brief §3.5: weapon equips, consumable shows a tooltip). A DATA fact: the branching
    /// itself lives in trees and graphs, never here.</summary>
    public enum ItemKind
    {
        Weapon,
        Consumable,
        Trinket
    }

    /// <summary>
    /// One item definition — the §3.7 DATA row: a ScriptableObject with zero logic. The
    /// <see cref="id"/> is the contract everything else speaks (inventory blackboard keys,
    /// clicked-item routing, registry lookup); <see cref="displayName"/> is what a screen
    /// shows; <see cref="kind"/> is what logic branches on. Nothing here knows about
    /// inventories, screens, or trees.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Item Definition", fileName = "ItemDef")]
    public sealed class ItemDefAsset : ScriptableObject
    {
        /// <summary>Stable text id — ordinal, the way every name contract in this toolset is
        /// matched. Renaming it is a deliberate breaking change.</summary>
        public string id = "";

        public string displayName = "";

        public ItemKind kind = ItemKind.Trinket;
    }
}
