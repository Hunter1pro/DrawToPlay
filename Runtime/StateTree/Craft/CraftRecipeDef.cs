using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT SOMETHING COSTS AND WHAT IT BECOMES (M26) — HT's ShipyardData, generalised to a
    /// row: costs in, one result out, and how long the work takes.
    ///
    /// The costs and the result are ITEM ROWS, not names, which is the whole reason this is a
    /// registry entry rather than three fields on a station component: the picker offers the
    /// item catalog, a renamed item stays wired, and the wire map can draw the line from a
    /// recipe to the things it eats. A recipe that spent "wood" as text would be a spelling
    /// agreement between a station prefab and an item table.
    ///
    /// DURATION LIVES HERE and not on the ability, even though an ability plays the animation.
    /// A shipyard takes longer to build a hull than a bench takes to fletch an arrow, and that
    /// is a fact about the recipe; the ability just holds the pose for as long as it is told.
    /// </summary>
    [Serializable]
    public sealed class CraftRecipeDef : StateTreeRegistryEntry
    {
        /// <summary>What a station's panel and the HUD line call it.</summary>
        public string displayName = "";

        [Serializable]
        public sealed class Cost
        {
            [Tooltip("The item spent — picked from the catalog.")]
            public StateTreeEntryRef<ItemDef> item = new StateTreeEntryRef<ItemDef>();

            [Tooltip("How many of it.")]
            public int count = 1;
        }

        [Tooltip("What it eats. All of it, or nothing happens — a partial spend is the worst "
            + "outcome a craft can have.")]
        public List<Cost> costs = new List<Cost>();

        [Tooltip("What it makes.")]
        public StateTreeEntryRef<ItemDef> result = new StateTreeEntryRef<ItemDef>();

        [Tooltip("How many of it.")]
        public int resultCount = 1;

        [Tooltip("How long the work takes, in seconds — the ability holds its pose for this.")]
        public float seconds = 1.2f;

        /// <summary>The one dim line the registry dashboard shows: the whole trade, readable
        /// without opening the row.</summary>
        public override string Describe()
        {
            string spend = "";
            for (int i = 0; i < costs.Count; i++)
            {
                if (costs[i] == null || string.IsNullOrEmpty(costs[i].item.entryName))
                    continue;
                if (spend.Length > 0)
                    spend += " + ";
                spend += costs[i].count + "× " + costs[i].item.entryName;
            }
            if (spend.Length == 0)
                spend = "nothing";
            string made = string.IsNullOrEmpty(result.entryName)
                ? "nothing"
                : resultCount + "× " + result.entryName;
            return spend + " → " + made + " · " + seconds.ToString("0.#") + "s";
        }
    }
}
