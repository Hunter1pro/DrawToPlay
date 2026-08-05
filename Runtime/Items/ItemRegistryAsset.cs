using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The item DATA REGISTRY of the notebook's Inventory flow (brief §3.5): the list of what
    /// can exist, id → definition. Still the §3.7 DATA row — <see cref="TryGet"/> is a lookup,
    /// not a rule. The economics this asset carries: the NEXT feature that needs a catalog
    /// (quests, recipes, shops) is another registry asset plus wiring, zero new C#.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Item Registry", fileName = "ItemRegistry")]
    public sealed class ItemRegistryAsset : ScriptableObject
    {
        public List<ItemDefAsset> items = new List<ItemDefAsset>();

        /// <summary>Ordinal id lookup; false for null/empty/unknown. Linear on purpose — a
        /// registry big enough to need an index is big enough to deserve the decision being
        /// made then, on real numbers.</summary>
        public bool TryGet(string id, out ItemDefAsset def)
        {
            def = null;
            if (string.IsNullOrEmpty(id))
                return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && string.Equals(items[i].id, id, StringComparison.Ordinal))
                {
                    def = items[i];
                    return true;
                }
            }
            return false;
        }
    }
}
