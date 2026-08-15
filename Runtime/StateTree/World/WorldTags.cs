namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The tag vocabulary the TOOLSET itself relies on — one place, because a tag is matched by
    /// exact text and two spellings of "combatant" would be two different worlds. Game content
    /// is free to invent its own tags beside these; these are only the ones code queries.
    /// </summary>
    public static class WorldTags
    {
        /// <summary>Everything that participates in combat targeting — carried automatically
        /// by whatever gives an object a health attribute, queried by
        /// <see cref="TargetDetectedCondition"/>. The registry-backed replacement for the M6
        /// polled health scan.</summary>
        public const string Combatant = "combatant";
    }
}
