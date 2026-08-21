using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE BAG AS A CAPABILITY (M36.4) — what a consumer that only COUNTS may ask the scope for.
    ///
    /// HT's doctrine, finally applied here: a consumer asks for what it needs, not for the class
    /// that happens to provide it. Seven of the bag's consumers — the bench, a pickup, a gift, a
    /// take, a gated choice, the boat check, the objective bridge — never wear, use, or save
    /// anything; they add, remove, count and look a row up. Asking for this instead of
    /// <see cref="InventoryService"/> is what lets a def name a different class in the same slot:
    /// a stub that only counts, a bag with rules, whatever the project installs.
    ///
    /// Equipment, use and save stay on the class on purpose: they are a second capability with
    /// their own consumers, and moving them is a sweep this slice does not pretend to be.
    /// </summary>
    public interface IBag
    {
        /// <summary>The catalog row by its name, or null — the one lookup every consumer makes
        /// before it counts anything.</summary>
        ItemDef Row(string itemName);

        /// <summary>Put some in the bag on this scope; returns the new total.</summary>
        int Add(StateTreeContext scope, string itemName, int count = 1);

        /// <summary>All-or-nothing: false leaves the bag untouched.</summary>
        bool Remove(StateTreeContext scope, string itemName, int count = 1);

        int Count(StateTreeContext scope, string itemName);

        bool Has(StateTreeContext scope, string itemName, int count = 1);

        /// <summary>Something in the bag moved — what a listener redraws on.</summary>
        event Action changed;
    }
}
