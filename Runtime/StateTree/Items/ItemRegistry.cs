using System;
using System.Collections.Generic;
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
    /// One item — the M13 shape of the §3.7 DATA row: a plain serializable class in the
    /// registry's list, not an asset per item. The base carries the identity (id = what
    /// typed references store, name = the runtime string inventory keys and click routing
    /// speak, group = dashboard/picker organization); this class carries only what an item
    /// IS. Nothing here knows about inventories, screens, or trees.
    /// </summary>
    [Serializable]
    public sealed class ItemDef : StateTreeRegistryEntry
    {
        /// <summary>What a screen shows — the human label, free to say anything while
        /// <c>name</c> stays the wiring-facing string.</summary>
        public string displayName = "";

        public ItemKind kind = ItemKind.Trinket;

        /// <summary>The face an inventory grid draws. Data, like the label: a view asks the
        /// row what this item looks like rather than holding a sprite per screen, so one
        /// change here is every screen changed.</summary>
        public Sprite icon;

        /// <summary>
        /// The item's COLOUR — what it looks like when it is lying on the floor rather than
        /// sitting in a grid.
        ///
        /// It was already implicit: the icon is drawn in a colour, and that colour is the thing
        /// a player learns to recognise. Leaving it inside the sprite meant the world could not
        /// use it, so every pickup in the level was the same white cube and finding one told you
        /// nothing about what it was. Stated here, the bag and the floor agree by construction.
        /// </summary>
        public Color tint = Color.white;

        // ---- what the item DOES (M25) — behaviour as picked rows, never code -----------

        [Tooltip("Consumable: the effect USING one applies to the user — a heal, a buff — "
            + "picked from the effect registry this registry depends on. Using spends one "
            + "from the bag. Empty = the item cannot be used.")]
        public StateTreeEntryRef<EffectDef> useEffect = new StateTreeEntryRef<EffectDef>();

        [Tooltip("Equipment: the SLOT this item occupies while worn — picked from the slot "
            + "registry. One item per slot; equipping into an occupied slot swaps. Empty = "
            + "the item cannot be worn.")]
        public StateTreeEntryRef<EquipmentSlotDef> slot = new StateTreeEntryRef<EquipmentSlotDef>();

        [Tooltip("Equipment: the thing that appears in the hand. A prefab on the ROW, because "
            + "what a sword looks like is a fact about that sword and not about whoever is "
            + "holding it — the same choice the projectile rows make about their ball.")]
        public GameObject worldPrefab;

        [Tooltip("Weapon: effects this lands on WHOEVER IS STRUCK, on top of the ability's own "
            + "damage — the poison on the dagger, the stagger on the hammer. Applied by the "
            + "ability through ApplyEquippedEffectsTask, so a weapon changes what a swing does "
            + "without the swing knowing which weapon it is.")]
        public List<StateTreeEntryRef<EffectDef>> hitEffects =
            new List<StateTreeEntryRef<EffectDef>>();

        [Tooltip("Weapon: effects a landed hit gives the WIELDER — the axe's fury, the "
            + "lifesteal on a cursed blade. Same road, other end.")]
        public List<StateTreeEntryRef<EffectDef>> wielderEffects =
            new List<StateTreeEntryRef<EffectDef>>();

        [Tooltip("Equipment: what wearing it GRANTS — Modifier-operation effect rows, "
            + "applied as revertible attribute modifiers for as long as the item is worn "
            + "and removed cleanly on unequip or swap.")]
        public List<StateTreeEntryRef<EffectDef>> wornEffects =
            new List<StateTreeEntryRef<EffectDef>>();

        public override string Describe()
        {
            if (!string.IsNullOrEmpty(useEffect.entryName))
                return "consumable · '" + useEffect.entryName + "' on use";
            if (!string.IsNullOrEmpty(slot.entryName))
            {
                var grants = wornEffects.Count == 0 ? "nothing"
                    : wornEffects.Count == 1 && wornEffects[0] != null
                        ? "'" + wornEffects[0].entryName + "'"
                        : wornEffects.Count + " effects";
                return "equipment · " + slot.entryName + " slot · grants " + grants
                    + " while worn";
            }
            return null;
        }
    }

    /// <summary>
    /// The item data registry — the whole declaration of a registry KIND under M13: the
    /// entry class above plus this line. The dashboard inspector, the tree-header Data list
    /// and the entry pickers all come from the base by reflection. The economics stand: the
    /// NEXT catalog (quests, recipes, weapon archetypes) is another pair like this, zero
    /// editor code.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Item Registry", fileName = "ItemRegistry")]
    public sealed class ItemRegistry : StateTreeRegistry<ItemDef>
    {
    }
}
