using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A placement's reference to the DEFINITION row it is an instance of — id-wired like
    /// every typed reference, but with the registry chosen at AUTHORING time rather than by a
    /// C# type: which table to pick from is whatever the placement's
    /// <see cref="LevelObjectKindDef.definitions"/> names ("unit" → the unit table, "pickup" →
    /// the item table). That is why this is its own type instead of
    /// <see cref="StateTreeEntryRef{TEntry}"/>: the drawer resolves the list through the
    /// sibling kind, not through a generic argument.
    /// </summary>
    [Serializable]
    public sealed class LevelObjectEntryRef
    {
        /// <summary>The row's id — the reference. Empty for a kind that has no definition
        /// table (a door).</summary>
        public string entryId = "";

        /// <summary>The row's name — the display cache, and what a spawner reads.</summary>
        public string entryName = "";
    }
}
