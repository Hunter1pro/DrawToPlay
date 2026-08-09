using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>One tag as a ROW: the name is the tag text citizens carry and queries match;
    /// the description is what carrying it MEANS.</summary>
    [Serializable]
    public sealed class WorldTagDef : StateTreeRegistryEntry
    {
        [TextArea]
        public string description = "";
    }

    /// <summary>
    /// The KNOWN LIST OF TAGS — the world's perception vocabulary as data. Attach it to the
    /// root tree's registries and every Tag-kind field picks from these rows instead of
    /// free-typing a string; <see cref="LevelDef.usedTags"/> rows reference it to say which
    /// tags a level's objects carry (the manifest a future async-load-by-position reads).
    /// Code-queried tags (see <see cref="WorldTags"/>) should appear here too, named
    /// identically — the constant is for code, the row is for authors.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/World/Tag Registry",
        fileName = "WorldTagRegistry")]
    public sealed class WorldTagRegistry : StateTreeRegistry<WorldTagDef>
    {
    }
}
