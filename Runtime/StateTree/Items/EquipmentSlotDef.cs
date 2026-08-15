using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A PLACE EQUIPMENT GOES (M25) — the slot as a row: "weapon hand", "trinket". How many
    /// slots a character has is how many rows exist; the ONE rule every slot enforces —
    /// one item at a time, equipping into an occupied slot swaps — lives in the service.
    /// Items claim a slot by picking its row.
    /// </summary>
    [Serializable]
    public sealed class EquipmentSlotDef : StateTreeRegistryEntry
    {
        [Tooltip("The slot's label in the bag — 'Trinket', 'Weapon'.")]
        public string displayName = "";
    }
}
