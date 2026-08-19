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
    [ServiceActionContract(UseAction, "value = item name")]
    [ServiceActionContract(WearAction, "value = item name")]
    [ServiceActionContract(TakeoffAction, "value = slot name")]
    public sealed class InventoryService : StateTreeService
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
            // A DRAW IS NOT A USE: a bag on screen during a level swap draws empty rather
            // than complaining — the carrier accessor is for verbs, which need one.
            StateTreeContextHost player = m_Carrier != null ? m_Carrier
                : StateTreeContextHost.Resolve(scope.gameObject, StateTreeContextKind.Player);
            if (player == null || player.Context == null)
            {
                widget.Redraw(null, null);
                return;
            }
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
            widget.Redraw(Stacks(player.Context), m_Lines);
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

        // ---- the carried counts: the API the example service had, kept verbatim --------

        public ItemDef Row(string itemName)
        {
            ItemRegistry items = registry;
            return items != null && !string.IsNullOrEmpty(itemName)
                ? items.FindByName(itemName) as ItemDef
                : null;
        }

        /// <summary>Put some in the bag; returns the new total.</summary>
        public int Add(StateTreeContext scope, string itemName, int count = 1)
        {
            if (scope == null || string.IsNullOrEmpty(itemName) || count <= 0)
                return Count(scope, itemName);
            return Write(scope, itemName, StateTreeInventoryUtil.Count(scope, itemName) + count);
        }

        /// <summary>The same, on whoever is carrying this bag — the form a caller wants when it
        /// has no business knowing which scope the counts live on.</summary>
        public int Add(string itemName, int count = 1)
        {
            StateTreeContextHost carrier = Carrier();
            return carrier != null ? Add(carrier.Context, itemName, count) : 0;
        }

        /// <summary>All-or-nothing: false leaves the bag untouched.</summary>
        public bool Remove(StateTreeContext scope, string itemName, int count = 1)
        {
            if (scope == null || string.IsNullOrEmpty(itemName) || count <= 0)
                return false;
            int held = StateTreeInventoryUtil.Count(scope, itemName);
            if (held < count)
                return false;
            Write(scope, itemName, held - count);
            return true;
        }

        /// <summary>The same, on whoever is carrying this bag.</summary>
        public bool Remove(string itemName, int count = 1)
        {
            StateTreeContextHost carrier = Carrier();
            return carrier != null && Remove(carrier.Context, itemName, count);
        }

        /// <summary>
        /// State what is carried, rather than change it by a delta — what a RESTORE means, and
        /// the only reason a public setter exists at all. Announced and redrawn like every
        /// other write, because a loaded bag is a changed bag.
        /// </summary>
        public int SetCount(StateTreeContext scope, string itemName, int count)
        {
            if (scope == null || string.IsNullOrEmpty(itemName))
                return 0;
            return Write(scope, itemName, Mathf.Max(0, count));
        }

        /// <summary>What the carrier holds.</summary>
        public int Count(string itemName)
        {
            // A READ NEEDS NO CARRIER TO COMPLAIN: asking an empty bag what it holds is a fair
            // question with the answer zero.
            StateTreeContextHost carrier = m_Carrier != null ? m_Carrier
                : StateTreeContextHost.Resolve(scope.gameObject, StateTreeContextKind.Player);
            return carrier != null ? Count(carrier.Context, itemName) : 0;
        }

        /// <summary>
        /// THE ONE WRITE (M32). Every change to a bag goes through here: the encoding is set in
        /// exactly one place, and the event that redraws every screen and every listener fires
        /// from the same line. Four callers writing a static could not say that, which is why
        /// picking something up did not always redraw the bag that was open.
        /// </summary>
        private int Write(StateTreeContext scope, string itemName, int total)
        {
            StateTreeInventoryUtil.SetCount(scope, itemName, total);
            changed?.Invoke();
            return Mathf.Max(0, total);
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
            if (!Remove(player.Context, itemName))
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
        /// old grant reverts in the same act. False when it has no slot or is not carried.</summary>
        public bool Equip(string itemName)
        {
            ItemDef row = Row(itemName);
            if (row == null || string.IsNullOrEmpty(row.slot.entryId))
                return false;
            StateTreeContextHost player = Carrier();
            if (player == null || !Has(player.Context, itemName))
                return false;

            Unequip(row.slot.entryId);

            var worn = new WornItem { slotId = row.slot.entryId, itemName = itemName };
            AttributeComponent attributes = m_CarrierAttributes;
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
                        + "' is not a resolvable Modifier row — skipped.");
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
                // Reverting a grant on a body that is already gone is a no-op, not an error:
                // the modifiers died with it. See ForgetWornOnPlayerChange.
                AttributeComponent attributes = m_Carrier != null ? m_CarrierAttributes : null;
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

        private StateTreeContextHost m_Carrier;
        private AbilityHost m_CarrierAbilities;
        private AttributeComponent m_CarrierAttributes;
        private bool m_ToldThemAboutTheCarrier;

        /// <summary>
        /// WHO IS CARRYING THIS BAG — and what carrying it implies, resolved WITH it.
        ///
        /// An inventory without a carrier is not a degraded inventory: it is a level in
        /// transition or a wiring fault, and returning false to every caller told them neither.
        /// So the answer is given once, loudly, and re-armed the moment a carrier appears —
        /// a level swap must not fill the console, and a missing rig must not be silent.
        ///
        /// The ability host and the attributes come with it, because they are the same object's
        /// parts and looking them up per use was a GetComponent inside a verb.
        /// </summary>
        private StateTreeContextHost Carrier([System.Runtime.CompilerServices.CallerMemberName]
            string doing = "")
        {
            // THE CARRIER IS LOOKED UP, not injected: a player is a spawned citizen, so the
            // host that scopes it appears and is replaced during a session. Asking the spine is
            // the same question the injector used to ask on a heartbeat, asked when it matters.
            StateTreeContextHost player = StateTreeContextHost.Resolve(scope.gameObject,
                StateTreeContextKind.Player);
            if (player == null || player.Context == null)
            {
                if (!m_ToldThemAboutTheCarrier)
                {
                    m_ToldThemAboutTheCarrier = true;
                    Debug.LogError("[Inventory] '" + doing + "' was asked with no carrier — no "
                        + "Player-scope host is mounted, so there is nobody to carry anything. "
                        + "Between levels this is momentary; otherwise the player scope is "
                        + "missing.");
                }
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
            }
            return m_Carrier;
        }

        /// <summary>The blackboard the counts live on — the carrier's, or null.</summary>
        public StateTreeContext Bag => Carrier()?.Context;

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
