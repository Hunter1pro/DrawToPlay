using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT A STATION IS OFFERING, as one routed object — the read model a craft panel draws
    /// and the only thing it knows.
    ///
    /// The skin does not resolve a recipe, count a bag or decide whether you can afford
    /// anything: it is handed a station's name, a line per cost with both numbers already in
    /// it, and a bool. That is the same bargain the bag's slot lines struck, and it is what
    /// keeps a second panel from growing a second copy of the rules.
    /// </summary>
    public sealed class CraftOffer
    {
        /// <summary>What the station is called in the world — the panel's title.</summary>
        public string stationName = "";

        /// <summary>The recipe row's name — what a request asking for this must carry.</summary>
        public string recipeName = "";

        /// <summary>What to call it on screen.</summary>
        public string displayName = "";

        /// <summary>One line per cost: the item, how many it takes, how many are carried.</summary>
        public readonly List<CraftCostLine> costs = new List<CraftCostLine>();

        /// <summary>Everything is carried — the button is live.</summary>
        public bool affordable;

        /// <summary>What the button would answer with right now, so a panel can show the
        /// requirement BEFORE the press rather than only after a refusal.</summary>
        public string blocker = "";
    }

    /// <summary>One cost of a recipe, with the player's side of it already counted.</summary>
    public sealed class CraftCostLine
    {
        public ItemDef item;

        public string itemName = "";

        public int need;

        public int held;

        public bool met => held >= need;
    }
}
