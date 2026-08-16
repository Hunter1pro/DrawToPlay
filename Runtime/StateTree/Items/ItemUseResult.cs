namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT USING AN ITEM CAME TO (§4d) — a contract class a state hands forward as ONE
    /// routed payload, landing under <see cref="Key"/>. Anything downstream — a quest
    /// reacting to a potion, an analytics hook, a tutorial waiting for the first heal —
    /// reads the whole story from one key, and the contract grows by growing THIS CLASS:
    /// no new blackboard keys, no rewiring of readers that ignore the new field.
    /// </summary>
    public sealed class ItemUseResult
    {
        /// <summary>Where the bag's use flow lands it on the root blackboard.</summary>
        public const string Key = "ui.bag.last-use";

        /// <summary>The row that was used — the full definition, not a name to re-resolve.</summary>
        public ItemDef item;

        public string itemName = "";

        /// <summary>False when the use was refused (not carried, not usable) — published
        /// either way, because "it did not work" is contract data too.</summary>
        public bool used;
    }
}
