using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE INVENTORY AS A SUBSYSTEM (M25, the boring third — promoted from the example
    /// service it started as): a ServiceDef-declared capability over the encoding the spine
    /// already has. Contents stay where M19 put them — <c>item:&lt;name&gt;</c> counts on
    /// the PLAYER scope's blackboard (<see cref="StateTreeInventoryUtil"/>) — so the graph
    /// atoms (InventoryAdd, InventoryCount) and this service are the same inventory.
    ///
    /// What M25 adds is what the ITEM ROWS now declare: USE (a consumable spends one and
    /// applies its picked effect row to the user) and WEAR (equipment occupies a slot row
    /// and holds its Modifier effects as revertible attribute grants — the ModifierHandle
    /// contract — until unequipped or swapped). One item per slot; equipping into an
    /// occupied slot swaps. All of it rows plus existing verbs — no new framework anywhere,
    /// which was the milestone's bet.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Inventory Service")]
    public sealed class InventoryService : StateTreeServiceBehaviour
    {
        [Tooltip("The declaration this service runs: scope and the item registry (whose "
            + "dependsOn names the effect and slot registries its rows pick from).")]
        public ServiceDef definition;

        /// <summary>Raised after any carried-count change, so views redraw instead of poll.</summary>
        public event Action changed;

        /// <summary>Raised after equip/unequip/swap.</summary>
        public event Action equipmentChanged;

        private readonly List<ItemStack> m_Stacks = new List<ItemStack>();

        private sealed class WornItem
        {
            public string slotId;
            public string itemName;
            public readonly List<AttributeComponent.ModifierHandle> handles =
                new List<AttributeComponent.ModifierHandle>();
        }

        private readonly List<WornItem> m_Worn = new List<WornItem>();

        public ItemRegistry registry =>
            definition != null ? definition.registry as ItemRegistry : null;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (definition == null)
                Debug.LogError("[Inventory] no ServiceDef assigned.", this);
            else if (registry == null)
                Debug.LogError("[Inventory] the ServiceDef's registry is not an ItemRegistry.",
                    this);
        }

        // ---- the carried counts: the API the example service had, kept verbatim --------

        public ItemDef Row(string itemName)
        {
            ItemRegistry items = registry;
            return items != null && !string.IsNullOrEmpty(itemName)
                ? items.FindByName(itemName) as ItemDef
                : null;
        }

        public int Add(StateTreeContext scope, string itemName, int count = 1)
        {
            if (scope == null || string.IsNullOrEmpty(itemName) || count <= 0)
                return Count(scope, itemName);
            int total = StateTreeInventoryUtil.Count(scope, itemName) + count;
            StateTreeInventoryUtil.SetCount(scope, itemName, total);
            changed?.Invoke();
            return total;
        }

        /// <summary>All-or-nothing: false leaves the bag untouched.</summary>
        public bool Remove(StateTreeContext scope, string itemName, int count = 1)
        {
            if (scope == null || string.IsNullOrEmpty(itemName) || count <= 0)
                return false;
            int held = StateTreeInventoryUtil.Count(scope, itemName);
            if (held < count)
                return false;
            StateTreeInventoryUtil.SetCount(scope, itemName, held - count);
            changed?.Invoke();
            return true;
        }

        public int Count(StateTreeContext scope, string itemName)
        {
            return scope == null ? 0 : StateTreeInventoryUtil.Count(scope, itemName);
        }

        public bool Has(StateTreeContext scope, string itemName, int count = 1)
        {
            return Count(scope, itemName) >= count;
        }

        /// <summary>Everything carried, as definition + count, in registry order.</summary>
        public IReadOnlyList<ItemStack> Stacks(StateTreeContext scope)
        {
            m_Stacks.Clear();
            ItemRegistry items = registry;
            if (items == null || scope == null)
                return m_Stacks;
            foreach (ItemDef entry in items.entries)
            {
                if (entry == null)
                    continue;
                int count = StateTreeInventoryUtil.Count(scope, entry.name);
                if (count > 0)
                    m_Stacks.Add(new ItemStack(entry, count));
            }
            return m_Stacks;
        }

        /// <summary>Announce a change made through the graph atoms rather than this
        /// service — they write the same keys.</summary>
        public void NotifyChanged()
        {
            changed?.Invoke();
        }

        // ---- USE: a consumable spends one for its picked effect ------------------------

        /// <summary>Spend one and apply the row's use effect to the player. False when the
        /// item is not usable, not carried, or nobody is there to apply it to.</summary>
        public bool Use(string itemName)
        {
            ItemDef row = Row(itemName);
            if (row == null || string.IsNullOrEmpty(row.useEffect.entryName))
                return false;
            StateTreeContextHost player = PlayerHost();
            if (player == null || player.Context == null)
                return false;
            EffectDef effect = FindInClosure(row.useEffect.entryName) as EffectDef;
            if (effect == null)
            {
                Debug.LogError("[Inventory] '" + itemName + "' uses effect '"
                    + row.useEffect.entryName + "', which resolves to no row.", this);
                return false;
            }
            var host = player.GetComponent<AbilityHost>();
            if (host == null)
                return false;
            if (!Remove(player.Context, itemName))
                return false;
            host.ApplyEffect(effect);
            return true;
        }

        // ---- WEAR: one item per slot, modifiers held while worn ------------------------

        /// <summary>The worn item's name in a slot, or empty.</summary>
        public string EquippedIn(string slotId)
        {
            for (int i = 0; i < m_Worn.Count; i++)
            {
                if (string.Equals(m_Worn[i].slotId, slotId, StringComparison.Ordinal))
                    return m_Worn[i].itemName;
            }
            return "";
        }

        /// <summary>Whether this item is currently worn (in its declared slot).</summary>
        public bool IsEquipped(string itemName)
        {
            for (int i = 0; i < m_Worn.Count; i++)
            {
                if (string.Equals(m_Worn[i].itemName, itemName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Wear a carried item in its declared slot. An occupied slot SWAPS — the
        /// old grant reverts in the same act. False when it has no slot or is not carried.</summary>
        public bool Equip(string itemName)
        {
            ItemDef row = Row(itemName);
            if (row == null || string.IsNullOrEmpty(row.slot.entryId))
                return false;
            StateTreeContextHost player = PlayerHost();
            if (player == null || player.Context == null
                || !Has(player.Context, itemName))
                return false;

            Unequip(row.slot.entryId);

            var worn = new WornItem { slotId = row.slot.entryId, itemName = itemName };
            var attributes = player.GetComponent<AttributeComponent>();
            for (int i = 0; attributes != null && i < row.wornEffects.Count; i++)
            {
                if (row.wornEffects[i] == null
                    || string.IsNullOrEmpty(row.wornEffects[i].entryName))
                    continue;
                var effect = FindInClosure(row.wornEffects[i].entryName) as EffectDef;
                if (effect == null || effect.operation != EffectOperation.Modifier
                    || string.IsNullOrEmpty(effect.attribute.entryName))
                {
                    Debug.LogWarning("[Inventory] worn effect '"
                        + row.wornEffects[i].entryName + "' on '" + itemName
                        + "' is not a resolvable Modifier row — skipped.", this);
                    continue;
                }
                attributes.Ensure(effect.attribute.entryName, 0f);
                AttributeComponent.ModifierHandle handle = attributes.AddModifier(
                    effect.attribute.entryName, effect.magnitude, effect.multiplier);
                if (handle != null)
                    worn.handles.Add(handle);
            }
            m_Worn.Add(worn);
            equipmentChanged?.Invoke();
            return true;
        }

        /// <summary>Forget the worn list WITHOUT reverting — for the moment the player
        /// OBJECT is replaced (a level swap): the old body's modifiers died with its
        /// components, and reverting into the new body would take away what was never
        /// granted to it. Re-equip afterwards via <see cref="RestoreState"/>.</summary>
        public void ForgetWornOnPlayerChange()
        {
            if (m_Worn.Count == 0)
                return;
            m_Worn.Clear();
            equipmentChanged?.Invoke();
        }

        /// <summary>Take a slot's item off, reverting everything it granted.</summary>
        public void Unequip(string slotId)
        {
            for (int i = m_Worn.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(m_Worn[i].slotId, slotId, StringComparison.Ordinal))
                    continue;
                StateTreeContextHost player = PlayerHost();
                var attributes = player != null
                    ? player.GetComponent<AttributeComponent>() : null;
                for (int j = 0; j < m_Worn[i].handles.Count && attributes != null; j++)
                    attributes.RemoveModifier(m_Worn[i].handles[j]);
                m_Worn.RemoveAt(i);
                equipmentChanged?.Invoke();
            }
        }

        // ---- what a reload needs -------------------------------------------------------

        [Serializable]
        public sealed class SaveState
        {
            public bool hasState;
            public List<string> slotIds = new List<string>();
            public List<string> itemNames = new List<string>();
        }

        public SaveState CaptureState()
        {
            var state = new SaveState { hasState = true };
            for (int i = 0; i < m_Worn.Count; i++)
            {
                state.slotIds.Add(m_Worn[i].slotId);
                state.itemNames.Add(m_Worn[i].itemName);
            }
            return state;
        }

        /// <summary>Re-wear a saved loadout — call AFTER the carried items are back (an
        /// equip checks the bag). Grants re-apply through the same Equip path.</summary>
        public void RestoreState(SaveState state)
        {
            if (state == null || !state.hasState)
                return;
            for (int i = 0; i < state.itemNames.Count; i++)
                Equip(state.itemNames[i]);
        }

        // ---- resolution ----------------------------------------------------------------

        private StateTreeContextHost PlayerHost()
        {
            return StateTreeContextHost.Resolve(gameObject, StateTreeContextKind.Player);
        }

        private StateTreeRegistryEntry FindInClosure(string entryName)
        {
            if (string.IsNullOrEmpty(entryName) || registry == null)
                return null;
            var reachable = new List<StateTreeRegistryAsset>();
            registry.CollectWithDependencies(reachable);
            for (int i = 0; i < reachable.Count; i++)
            {
                StateTreeRegistryEntry entry = reachable[i].FindByName(entryName);
                if (entry != null && reachable[i] != registry)
                    return entry;
            }
            return null;
        }
    }
}
