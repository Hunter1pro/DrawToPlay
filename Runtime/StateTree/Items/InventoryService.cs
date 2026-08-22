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
    /// </summary>
    [ServiceActionContract(UseAction, "value = item name", typeof(ItemUseResult))]
    [ServiceActionContract(WearAction, "value = item name")]
    [ServiceActionContract(TakeoffAction, "value = slot name")]
    [ServiceActionContract(AddAction, "value = item name — one is put in the bag")]
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

        // The action vocabulary as SYMBOLS — the attribute above, the switch below, and
        // the builder all reference these, so declaration and dispatch cannot drift.
        public const string UseAction = "use";
        public const string WearAction = "wear";
        public const string TakeoffAction = "takeoff";

        /// <summary>The one PUBLIC verb (M38.1): put an item in the bag — what a keeper's gift, a
        /// quest reward or a cheat asks for by key. The three above are the bag's own buttons.</summary>
        public const string AddAction = "add";

        /// <summary>Raised after any carried-count change, so views redraw instead of poll.</summary>
        public event Action changed;

        /// <summary>Raised after equip/unequip/swap.</summary>
        public event Action equipmentChanged;

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

        /// <summary>
        /// THE DOMAIN HOOK (§4g): what a request's action MEANS. Every one of the bag's
        /// handlers is single-frame — verb, beats, consume — so no state tree serves
        /// them; the rows say the beats, this switch says the verbs, and every served
        /// request ends with the skin redrawn to the truth.
        /// </summary>
        protected override void OnRequest(ServiceRequest request, string value)
        {
            switch (request.action)
            {
                case UseAction:
                {
                    ItemDef row = Row(value);
                    bool used = Use(value);
                    // The announcement (§4d): the whole story as one contract object —
                    // whoever cares reads the class, and the class is where it grows.
                    Announce(ItemUseResult.Key, new ItemUseResult
                    {
                        item = row, itemName = value ?? "", used = used
                    });
                    break;
                }
                case WearAction:
                    Equip(value);
                    break;
                case AddAction:
                    Add(value, 1);
                    break;

                case TakeoffAction:
                {
                    // Typed request values name ROWS; the domain speaks ids — resolved
                    // here at the boundary, an id passing through untouched.
                    EquipmentSlotRegistry slots = Slots();
                    var named = slots != null
                        ? slots.FindByName(value) as EquipmentSlotDef
                        : null;
                    Unequip(named != null ? named.id : value);
                    break;
                }
            }
            RedrawSpawnedBags();
        }

        /// <summary>Build the read model and hand it to a bag skin — the one place domain
        /// and skin meet, shared by the def-served path and the RedrawBagTask atom.</summary>
        public void RedrawInto(InventoryWidgetView widget)
        {
            if (widget == null)
                return;
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
            widget.Redraw(Stacks(), m_Lines);
        }

        /// <summary>Every bag this service is showing, redrawn to the present.</summary>
        private void RedrawSpawnedBags()
        {
            for (int i = m_Bags.Count - 1; i >= 0; i--)
            {
                if (m_Bags[i] == null)
                {
                    m_Bags.RemoveAt(i);   // a destroyed skin forgets itself
                    continue;
                }
                RedrawInto(m_Bags[i]);
            }
        }

        /// <summary>
        /// THE SKIN IS HELD, NOT HUNTED (M32). This used to re-find its own screen on every
        /// change — ShownView by name, then GetComponentInChildren for the widget — which is a
        /// search per redraw and the wrong answer the day two bags are open.
        ///
        /// The UI service says when it shows something and when it hides it. That is the moment
        /// to take a reference and the moment to drop one, so the bag the service redraws is
        /// exactly the bag it was given.
        /// </summary>
        private void OnUiShown(UiDef row, GameObject view)
        {
            if (row == null || view == null || !Spawns(row.name))
                return;
            var widget = view.GetComponentInChildren<InventoryWidgetView>(true);
            if (widget == null || m_Bags.Contains(widget))
                return;
            m_Bags.Add(widget);
            RedrawInto(widget);
        }

        private void OnUiHidden(UiDef row)
        {
            if (row == null || !Spawns(row.name))
                return;
            for (int i = m_Bags.Count - 1; i >= 0; i--)
            {
                if (m_Bags[i] == null || m_Bags[i].gameObject == null
                    || !m_Bags[i].gameObject.activeInHierarchy)
                    m_Bags.RemoveAt(i);
            }
        }

        /// <summary>Is this one of the screens this def says it owns?</summary>
        private bool Spawns(string uiRowName)
        {
            ServiceDef def = definition;
            for (int i = 0; def != null && i < def.spawns.Count; i++)
            {
                if (def.spawns[i] != null && def.spawns[i].entryName == uiRowName)
                    return true;
            }
            return false;
        }

        private readonly List<InventoryWidgetView> m_Bags = new List<InventoryWidgetView>();

        /// <summary>
        /// THE FIRST TICK, when the world is assembled (M33): the def is validated, the flow
        /// tree is running and the declared screens are up — so the bag takes the one already
        /// on screen here rather than waiting to notice it.
        /// </summary>
        protected override void OnStarted()
        {
            // IT REDRAWS ITSELF: every mutation raises these — its own verbs, a pickup, a save
            // restore — so nobody outside needs to ask it to refresh.
            changed += RedrawSpawnedBags;
            equipmentChanged += RedrawSpawnedBags;

            UiService ui = Ui;
            if (ui == null)
                return;
            ui.shown += OnUiShown;
            ui.hidden += OnUiHidden;
            m_Screens = ui;

            ServiceDef def = definition;
            for (int i = 0; def != null && i < def.spawns.Count; i++)
            {
                var spawn = def.spawns[i];
                if (spawn == null || string.IsNullOrEmpty(spawn.entryName))
                    continue;
                OnUiShown(ui.Find(spawn.entryName), ui.ShownView(spawn.entryName));
            }
        }

        /// <summary>Everything the bag holds open, released with its scope.</summary>
        public override void Dispose()
        {
            changed -= RedrawSpawnedBags;
            equipmentChanged -= RedrawSpawnedBags;
            if (m_Screens != null)
            {
                m_Screens.shown -= OnUiShown;
                m_Screens.hidden -= OnUiHidden;
                m_Screens = null;
            }
            m_Bags.Clear();
            base.Dispose();
        }

        private UiService m_Screens;

        // ---- the carried counts ---------------------------------------------------------

        public ItemDef Row(string itemName)
        {
            ItemRegistry items = registry;
            return items != null && !string.IsNullOrEmpty(itemName)
                ? items.FindByName(itemName) as ItemDef
                : null;
        }

        /// <summary>Put some in the bag; returns the new total.</summary>
        public int Add(string itemName, int count = 1)
        {
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                return Count(itemName);
            return Write(itemName, Count(itemName) + count);
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
        /// THE ONE WRITE (M32). Every change to a bag goes through here, and the event that
        /// redraws every screen and every listener fires from the same line. Zero-or-less
        /// REMOVES the entry, so "none left" and "never had one" are the same absent state.
        /// </summary>
        private int Write(string itemName, int total)
        {
            if (total <= 0)
                m_Counts.Remove(itemName);
            else
                m_Counts[itemName] = total;
            changed?.Invoke();
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

        /// <summary>Spend one and apply the row's use effect to the player. False when the
        /// item is not usable, not carried, or nobody is there to apply it to.</summary>
        public bool Use(string itemName)
        {
            ItemDef row = Row(itemName);
            if (row == null || string.IsNullOrEmpty(row.useEffect.entryName))
                return false;
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
            equipmentChanged?.Invoke();
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
                m_Worn.RemoveAt(i);
                equipmentChanged?.Invoke();
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
            changed?.Invoke();
            for (int i = 0; i < state.wornItems.Count; i++)
                Equip(state.wornItems[i]);
        }

        // ---- resolution ----------------------------------------------------------------

        private StateTreeContextHost m_Carrier;
        private AbilityHost m_CarrierAbilities;
        private AttributeComponent m_CarrierAttributes;
        private bool m_ToldThemAboutTheCarrier;

        /// <summary>
        /// THE BODY CARRYING THIS BAG, looked up rather than injected: a player is a spawned
        /// citizen, so the host that scopes it appears and is replaced during a session. The
        /// moment it changes is the moment the worn modifiers move — the old body's died with
        /// its components (nothing to revert), the new one has never been granted them — so
        /// the change is noticed here, on every tick and every verb, and the loadout is
        /// re-granted in place. Null, quietly, when there is nobody: a bag between levels is
        /// a full bag with no one holding it.
        /// </summary>
        private StateTreeContextHost Body()
        {
            StateTreeContextHost player = StateTreeContextHost.Resolve(scope.gameObject,
                StateTreeContextKind.Player);
            if (player == null || player.Context == null)
            {
                m_Carrier = null;
                m_CarrierAbilities = null;
                m_CarrierAttributes = null;
                return null;
            }
            if (!ReferenceEquals(player, m_Carrier))
            {
                m_Carrier = player;
                m_CarrierAbilities = player.GetComponent<AbilityHost>();
                m_CarrierAttributes = player.GetComponent<AttributeComponent>();
                m_ToldThemAboutTheCarrier = false;
                for (int i = 0; i < m_Worn.Count; i++)
                    Grant(m_Worn[i], player);
                if (m_Worn.Count > 0)
                    equipmentChanged?.Invoke();
            }
            return m_Carrier;
        }

        /// <summary>The body, for a verb that NEEDS one (a use applies an effect to someone):
        /// said once, loudly, when there is nobody, re-armed when a body appears.</summary>
        private StateTreeContextHost Carrier([System.Runtime.CompilerServices.CallerMemberName]
            string doing = "")
        {
            StateTreeContextHost body = Body();
            if (body == null && !m_ToldThemAboutTheCarrier)
            {
                m_ToldThemAboutTheCarrier = true;
                Debug.LogError("[Inventory] '" + doing + "' was asked with no carrier — no "
                    + "Player-scope host is mounted, so there is nobody to apply it to. "
                    + "Between levels this is momentary; otherwise the player scope is missing.");
            }
            return body;
        }

        /// <summary>The swap is noticed without waiting for a verb.</summary>
        protected override void OnTick(float deltaTime)
        {
            Body();
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
