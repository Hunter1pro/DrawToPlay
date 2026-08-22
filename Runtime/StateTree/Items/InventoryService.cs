using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE INVENTORY AS A SUBSYSTEM (M25) — and, since M39, A BAG THAT OWNS WHAT IT HOLDS.
    ///
    /// The counts are a dictionary HERE and the worn slots a list HERE; the save is a snapshot
    /// of both. M7g had put the counts on the player scope's blackboard ("per-player for free,
    /// readable from any tree, picked up by the save") and none of the three came true: one
    /// player, nothing read the keys but this class, and the board died with the body at every
    /// level swap so the save grew a retry loop to put the counts back. HT's
    /// <c>ItemStatProvider</c> is the shape: the service holds the data, the file holds the
    /// snapshot, the body is where worn modifiers are granted — and re-granted when the body
    /// is replaced, which is the one thing a level swap actually changes.
    ///
    /// USE (a consumable spends one and applies its picked effect row to the carrier) and WEAR
    /// (equipment occupies a slot row and holds its Modifier effects as revertible attribute
    /// grants until unequipped or swapped) are the item rows' declarations, unchanged.
    ///
    /// NO EVENTS (M39.2b), NO WATCHING (M40.3): the body binds itself at birth (<see cref="Bind"/>). HT's rule: a domain holds what it spawned and calls it, a method
    /// calls the services it needs and returns a result, nothing subscribes. So every write
    /// here ends the same visible way — redraw the screen this bag showed, tell the quest
    /// line the count, knock on the save — in <see cref="Changed"/>, one method, readable top
    /// to bottom. A screen that subscribed to "changed" would be the same four lines hidden
    /// in a second place.
    /// </summary>
    [ServiceActionContract(AddAction, "value = item name — one is put in the bag")]
    [ServiceActionContract(RemoveAction, "value = item name — one is taken, or nothing is")]
    [ServiceActionContract(OpenAction, "the bag panel opens — a gift wants to be seen")]
    public sealed class InventoryService : StateTreeService, IBag
    {
        /// <summary>
        /// Built by its scope's installer (M33), with the host it belongs to and the def it
        /// serves. Everything else it needs it asks for HERE, where a missing collaborator is a
        /// loud failure at install time rather than a null three frames into play.
        /// </summary>
        public InventoryService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
            if (definition == null)
                Debug.LogError("[Inventory] built with no ServiceDef — it serves nothing.");
            else if (registry == null)
                Debug.LogError("[Inventory] the ServiceDef's registry is not an ItemRegistry.",
                    definition);
        }

        // THE DECLARED VERBS (M39): what a flow wires — a gift, a take, and "show me". Use,
        // wear and take off are the bag's own buttons, and its screen calls them in C#; they
        // were rows once, marked internal, which is a def admitting nobody else should call them.
        public const string AddAction = "add";
        public const string RemoveAction = "remove";
        public const string OpenAction = "open";

        /// <summary>The screen this bag showed (its def's spawn), held from the moment it was
        /// shown. Null when the def spawns none — a bag with no screen is still a bag.</summary>
        private InventoryWidgetView m_Screen;

        /// <summary>The file, knocked on after every write — asked for at the first knock and
        /// remembered, because a session with no save is a legal session and [InjectService]
        /// is loud about a collaborator that is not there.</summary>
        private IAutosave m_Autosave;

        /// <summary>The first tick has shown the spawns: take the screen and draw it.</summary>
        protected override void OnStarted()
        {
            m_Screen = Spawned<InventoryWidgetView>();
            Redraw();
        }

        /// <summary>
        /// WHAT A CHANGE MEANS, in one place: the screen is redrawn, the quest line hears the
        /// count of whatever moved, the save is knocked on. Every write and every wear ends
        /// here, so the bag's effects on the rest of the session can be read in one method.
        /// </summary>
        private void Changed(string itemName)
        {
            Redraw();
            ReportToObjectives(itemName);
            m_Autosave ??= StateTreeContextHost.FindService<IAutosave>(scope.gameObject);
            m_Autosave?.MarkDirty();
        }

        private void Redraw()
        {
            m_Screen?.Redraw(Stacks(), SlotLines());
        }

        /// <summary>The quest line is LEVEL-scoped and the bag is not, so it is asked for
        /// through the body carrying the bag — which is in the level — at the moment of the
        /// write, not remembered.</summary>
        private void ReportToObjectives(string itemName)
        {
            StateTreeContextHost body = Body();
            ObjectiveService objectives = body != null
                ? StateTreeContextHost.FindService<ObjectiveService>(body.gameObject)
                : null;
            ObjectiveDef current = objectives != null ? objectives.current : null;
            if (current == null || current.kind != ObjectiveKind.Pickup
                || !string.Equals(current.target.entryName, itemName, StringComparison.Ordinal))
                return;
            objectives.ReportPickupCount(itemName, Count(itemName));
        }

        private readonly List<ItemStack> m_Stacks = new List<ItemStack>();

        /// <summary>The slot half of the read model, reused like the stacks — a redraw runs on
        /// every change and a new list per change is garbage nobody asked for.</summary>
        private readonly List<BagSlotLine> m_Lines = new List<BagSlotLine>();

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

        /// <summary>THE DOMAIN HOOK (§4g): what a request's action means.</summary>
        protected override void OnRequest(ServiceRequest request, string value)
        {
            switch (request.action)
            {
                case AddAction:
                    Add(value, 1);
                    break;
                case RemoveAction:
                    Remove(value, 1);
                    break;
                case OpenAction:
                    Open();
                    break;
            }
        }

        /// <summary>Open the screen this bag showed — the keeper's "show me".</summary>
        public void Open()
        {
            m_Screen?.Open();
        }

        /// <summary>The equipment half of what a screen draws: one line per declared slot,
        /// with what is worn in it. Reused like <see cref="Stacks"/>.</summary>
        public IReadOnlyList<BagSlotLine> SlotLines()
        {
            m_Lines.Clear();
            EquipmentSlotRegistry slots = Slots();
            for (int i = 0; slots != null && i < slots.entries.Count; i++)
            {
                EquipmentSlotDef slot = slots.entries[i];
                if (slot == null)
                    continue;
                string wornName = EquippedIn(slot.id);
                ItemDef worn = string.IsNullOrEmpty(wornName) ? null : Row(wornName);
                m_Lines.Add(new BagSlotLine(
                    slot.id,
                    slot.name,
                    string.IsNullOrEmpty(slot.displayName) ? slot.name : slot.displayName,
                    wornName,
                    worn == null ? "" : (string.IsNullOrEmpty(worn.displayName)
                        ? worn.name : worn.displayName)));
            }
            return m_Lines;
        }

        // ---- the carried counts ---------------------------------------------------------

        public ItemDef Row(string itemName)
        {
            ItemRegistry items = registry;
            return items != null && !string.IsNullOrEmpty(itemName)
                ? items.FindByName(itemName) as ItemDef
                : null;
        }

        /// <summary>Put some in the bag; returns the new total. The screen flashes the cell —
        /// the domain says what just went up, rather than a screen guessing from a diff.</summary>
        public int Add(string itemName, int count = 1)
        {
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                return Count(itemName);
            int total = Write(itemName, Count(itemName) + count);
            m_Screen?.Flash(itemName);
            return total;
        }

        /// <summary>All-or-nothing: false leaves the bag untouched.</summary>
        public bool Remove(string itemName, int count = 1)
        {
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                return false;
            int held = Count(itemName);
            if (held < count)
                return false;
            Write(itemName, held - count);
            return true;
        }

        /// <summary>State what is carried rather than change it by a delta — what a restore
        /// means. Announced and redrawn like every other write.</summary>
        public int SetCount(string itemName, int count)
        {
            if (string.IsNullOrEmpty(itemName))
                return 0;
            return Write(itemName, Mathf.Max(0, count));
        }

        public int Count(string itemName)
        {
            return !string.IsNullOrEmpty(itemName) && m_Counts.TryGetValue(itemName, out int held)
                ? held
                : 0;
        }

        public bool Has(string itemName, int count = 1)
        {
            return Count(itemName) >= count;
        }

        /// <summary>
        /// THE ONE WRITE (M32). Every change to a bag goes through here, and what a change
        /// means (<see cref="Changed"/>) follows from the same line. Zero-or-less REMOVES the
        /// entry, so "none left" and "never had one" are the same absent state.
        /// </summary>
        private int Write(string itemName, int total)
        {
            if (total <= 0)
                m_Counts.Remove(itemName);
            else
                m_Counts[itemName] = total;
            Changed(itemName);
            return Mathf.Max(0, total);
        }

        private readonly Dictionary<string, int> m_Counts =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Everything carried, as definition + count, in registry order — the order a
        /// grid draws, stable across adds.</summary>
        public IReadOnlyList<ItemStack> Stacks()
        {
            m_Stacks.Clear();
            ItemRegistry items = registry;
            if (items == null)
                return m_Stacks;
            foreach (ItemDef entry in items.entries)
            {
                if (entry == null)
                    continue;
                int count = Count(entry.name);
                if (count > 0)
                    m_Stacks.Add(new ItemStack(entry, count));
            }
            return m_Stacks;
        }

        /// <summary>The slot catalog the item registry depends on, or null — the domain
        /// closure is this service's knowledge, so nobody else walks dependsOn for it.</summary>
        public EquipmentSlotRegistry Slots()
        {
            if (registry == null)
                return null;
            var reachable = new List<StateTreeRegistryAsset>();
            registry.CollectWithDependencies(reachable);
            for (int i = 0; i < reachable.Count; i++)
            {
                if (reachable[i] is EquipmentSlotRegistry slots)
                    return slots;
            }
            return null;
        }

        // ---- USE: a consumable spends one for its picked effect ------------------------

        /// <summary>Spend one and apply the row's use effect to the carrier. The answer is
        /// returned to whoever asked AND announced under <see cref="ItemUseResult.Key"/>, so a
        /// screen shows it directly and a graph can still react to it. Not used when the item
        /// is not usable, not carried, or nobody is there to apply it to.</summary>
        public ItemUseResult Use(string itemName)
        {
            ItemDef row = Row(itemName);
            var result = new ItemUseResult { item = row, itemName = itemName ?? "", used = Spend(row) };
            Announce(ItemUseResult.Key, result);
            return result;
        }

        private bool Spend(ItemDef row)
        {
            if (row == null || string.IsNullOrEmpty(row.useEffect.entryName))
                return false;
            string itemName = row.name;
            StateTreeContextHost player = Carrier();
            if (player == null)
                return false;
            EffectDef effect = FindInClosure(row.useEffect.entryName) as EffectDef;
            if (effect == null)
            {
                Debug.LogError("[Inventory] '" + itemName + "' uses effect '"
                    + row.useEffect.entryName + "', which resolves to no row.");
                return false;
            }
            if (m_CarrierAbilities == null)
            {
                // A CARRIER THAT CANNOT BE AFFECTED is a rig fault, not an empty pocket: the
                // item would be spent on nothing, so it is not spent at all.
                Debug.LogError("[Inventory] '" + player.name + "' carries the bag but has no "
                    + "AbilityHost, so '" + itemName + "' has nothing to apply its effect to.");
                return false;
            }
            if (!Remove(itemName))
                return false;
            m_CarrierAbilities.ApplyEffect(effect);
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
        /// old grant reverts in the same act. False when it has no slot or is not carried. The
        /// grant lands on the body carrying the bag, now or when one appears.</summary>
        public bool Equip(string itemName)
        {
            ItemDef row = Row(itemName);
            if (row == null || string.IsNullOrEmpty(row.slot.entryId) || !Has(itemName))
                return false;

            Unequip(row.slot.entryId);
            var worn = new WornItem { slotId = row.slot.entryId, itemName = itemName };
            m_Worn.Add(worn);
            Grant(worn, Body());
            Changed(itemName);
            return true;
        }

        /// <summary>Apply a worn row's Modifier effects to a body and remember the handles, so
        /// the same act reverts them. Nothing granted when there is no body yet — the grant
        /// happens when one arrives (see <see cref="Body"/>).</summary>
        private void Grant(WornItem worn, StateTreeContextHost body)
        {
            worn.handles.Clear();
            AttributeComponent attributes = body != null ? m_CarrierAttributes : null;
            if (attributes == null)
                return;
            ItemDef row = Row(worn.itemName);
            for (int i = 0; row != null && i < row.wornEffects.Count; i++)
            {
                if (row.wornEffects[i] == null
                    || string.IsNullOrEmpty(row.wornEffects[i].entryName))
                    continue;
                var effect = FindInClosure(row.wornEffects[i].entryName) as EffectDef;
                if (effect == null || effect.operation != EffectOperation.Modifier
                    || string.IsNullOrEmpty(effect.attribute.entryName))
                {
                    Debug.LogWarning("[Inventory] worn effect '"
                        + row.wornEffects[i].entryName + "' on '" + worn.itemName
                        + "' is not a resolvable Modifier row — skipped.");
                    continue;
                }
                attributes.Ensure(effect.attribute.entryName, 0f);
                AttributeComponent.ModifierHandle handle = attributes.AddModifier(
                    effect.attribute.entryName, effect.magnitude, effect.multiplier);
                if (handle != null)
                    worn.handles.Add(handle);
            }
        }

        /// <summary>Take a slot's item off, reverting everything it granted.</summary>
        public void Unequip(string slotId)
        {
            for (int i = m_Worn.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(m_Worn[i].slotId, slotId, StringComparison.Ordinal))
                    continue;
                // Reverting a grant on a body that is already gone is a no-op, not an error:
                // the modifiers died with it.
                AttributeComponent attributes = m_CarrierAttributes;
                for (int j = 0; j < m_Worn[i].handles.Count && attributes != null; j++)
                    attributes.RemoveModifier(m_Worn[i].handles[j]);
                string wornName = m_Worn[i].itemName;
                m_Worn.RemoveAt(i);
                Changed(wornName);
            }
        }

        // ---- what a reload needs -------------------------------------------------------

        /// <summary>The whole bag as a snapshot: what is carried and what is worn. What the
        /// file holds; nothing in it needs a player to be put back.</summary>
        [Serializable]
        public sealed class SaveState
        {
            public bool hasState;
            public List<string> itemNames = new List<string>();
            public List<int> itemCounts = new List<int>();
            public List<string> wornItems = new List<string>();
        }

        public SaveState CaptureState()
        {
            var state = new SaveState { hasState = true };
            ItemRegistry items = registry;
            // Registry order, like Stacks(): a file that diffs cleanly between two saves.
            for (int i = 0; items != null && i < items.entries.Count; i++)
            {
                ItemDef entry = items.entries[i];
                int count = entry != null ? Count(entry.name) : 0;
                if (count <= 0)
                    continue;
                state.itemNames.Add(entry.name);
                state.itemCounts.Add(count);
            }
            for (int i = 0; i < m_Worn.Count; i++)
                state.wornItems.Add(m_Worn[i].itemName);
            return state;
        }

        /// <summary>Become the snapshot: the counts are STATED (a restore says what is carried,
        /// it does not add to it), then the loadout is re-worn through the same Equip path —
        /// grants land on the body if there is one, or when one arrives.</summary>
        public void RestoreState(SaveState state)
        {
            if (state == null || !state.hasState)
                return;
            for (int i = m_Worn.Count - 1; i >= 0; i--)
                Unequip(m_Worn[i].slotId);
            m_Counts.Clear();
            for (int i = 0; i < state.itemNames.Count; i++)
            {
                int count = i < state.itemCounts.Count ? state.itemCounts[i] : 0;
                if (count > 0)
                    m_Counts[state.itemNames[i]] = count;
            }
            for (int i = 0; i < state.wornItems.Count; i++)
                Equip(state.wornItems[i]);
            // A restore is not a change worth writing back — but it is worth drawing.
            Redraw();
        }

        // ---- resolution ----------------------------------------------------------------

        private StateTreeContextHost m_Carrier;
        private AbilityHost m_CarrierAbilities;
        private AttributeComponent m_CarrierAttributes;
        private bool m_ToldThemAboutTheCarrier;

        /// <summary>
        /// THE BODY BINDS ITSELF (M40.3, meta-rule 3) — HT's <c>bind_player</c>. A player is a
        /// spawned citizen: it is born with the level and dies with it, and the bag outlives
        /// both. So the body, at its start, tells the bag "I am carrying you", and the bag
        /// grants it whatever is worn right then. Nothing here watches for a body to be
        /// replaced; the next body binds when it is born, and the old one's modifiers died with
        /// its components. Between bodies the bag is a full bag with nobody holding it.
        /// </summary>
        public void Bind(StateTreeContextHost body)
        {
            if (body == null || ReferenceEquals(body, m_Carrier))
                return;
            m_Carrier = body;
            m_CarrierAbilities = body.GetComponent<AbilityHost>();
            m_CarrierAttributes = body.GetComponent<AttributeComponent>();
            m_ToldThemAboutTheCarrier = false;
            for (int i = 0; i < m_Worn.Count; i++)
                Grant(m_Worn[i], body);
            if (m_Worn.Count > 0)
                Redraw();
        }

        /// <summary>The body is going (its scope is being disposed): forget it without
        /// reverting — its components go with it.</summary>
        public void Unbind(StateTreeContextHost body)
        {
            if (body == null || !ReferenceEquals(body, m_Carrier))
                return;
            m_Carrier = null;
            m_CarrierAbilities = null;
            m_CarrierAttributes = null;
            for (int i = 0; i < m_Worn.Count; i++)
                m_Worn[i].handles.Clear();
        }

        /// <summary>The bound body, or null — a destroyed one reads as null too.</summary>
        private StateTreeContextHost Body()
        {
            return m_Carrier != null ? m_Carrier : null;
        }

        /// <summary>The body, for a verb that NEEDS one (a use applies an effect to someone):
        /// said once, loudly, when there is nobody, re-armed when a body binds.</summary>
        private StateTreeContextHost Carrier([System.Runtime.CompilerServices.CallerMemberName]
            string doing = "")
        {
            StateTreeContextHost body = Body();
            if (body == null && !m_ToldThemAboutTheCarrier)
            {
                m_ToldThemAboutTheCarrier = true;
                Debug.LogError("[Inventory] '" + doing + "' was asked with no body bound — no "
                    + "player has bound this bag (OutpostPlayerBody.Start does). Between "
                    + "levels this is momentary; otherwise the player prefab lacks it.");
            }
            return body;
        }

        /// <summary>
        /// A ROW FROM WHAT THIS DEF DECLARES — an effect the item picks, a slot it fills.
        ///
        /// It used to walk the whole dependsOn closure on every use, allocating the closure
        /// each time: a swallowed potion did a graph traversal to find a row that had not moved
        /// since the level loaded. The closure is gathered once per registry and the answers
        /// are remembered, because neither can change without a domain reload — and a service
        /// that re-derives constants inside a verb is the habit this milestone is about.
        /// </summary>
        private StateTreeRegistryEntry FindInClosure(string entryName)
        {
            ItemRegistry items = registry;
            if (string.IsNullOrEmpty(entryName) || items == null)
                return null;

            if (!ReferenceEquals(items, m_ClosureOf))
            {
                m_ClosureOf = items;
                m_Closure.Clear();
                m_Known.Clear();
                items.CollectWithDependencies(m_Closure);
            }

            if (m_Known.TryGetValue(entryName, out StateTreeRegistryEntry remembered))
                return remembered;

            StateTreeRegistryEntry found = null;
            for (int i = 0; i < m_Closure.Count && found == null; i++)
            {
                StateTreeRegistryEntry entry = m_Closure[i].FindByName(entryName);
                if (entry != null && m_Closure[i] != items)
                    found = entry;
            }
            // A MISS IS REMEMBERED TOO: a misspelt effect name asked twice a second by a held
            // button should cost one walk, not one per press.
            m_Known[entryName] = found;
            return found;
        }

        private ItemRegistry m_ClosureOf;

        private readonly List<StateTreeRegistryAsset> m_Closure =
            new List<StateTreeRegistryAsset>();

        private readonly Dictionary<string, StateTreeRegistryEntry> m_Known =
            new Dictionary<string, StateTreeRegistryEntry>(StringComparer.Ordinal);
    }
}
