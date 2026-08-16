namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT A CRAFT CAME TO (§4d) — the announcement's payload, one routed object rather than
    /// a handful of keys nobody can find later.
    ///
    /// Published on the failures too, and that is the point: "you are two timber short" is the
    /// most useful thing a station ever says, and a contract that only reported successes would
    /// force the refusal to travel by some second channel. A HUD line, a quest, a tutorial
    /// waiting for the first hull all read the same object.
    /// </summary>
    public sealed class CraftResult
    {
        /// <summary>Where a craft lands on the root blackboard.</summary>
        public const string Key = "craft.last";

        /// <summary>The recipe that was asked for — the row, not a name to re-resolve.</summary>
        public CraftRecipeDef recipe;

        public string recipeName = "";

        /// <summary>What was made, when something was.</summary>
        public ItemDef item;

        public string itemName = "";

        public int count;

        /// <summary>False when it was refused — see <see cref="refusal"/> for which way.</summary>
        public bool made;

        /// <summary>Why not, in the words a player could be shown: "needs 3 timber". Empty on
        /// a success.</summary>
        public string refusal = "";
    }
}
