using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// I DECLARE THESE CATALOGS (M30.6) — the neighbourhood rule, open to anything.
    ///
    /// Registries say it with Depends On and defs with Declares; both are the same sentence, and
    /// an authoring document that wants to offer typed values has to be able to say it too
    /// without <see cref="StateTreeOffers"/> learning about every kind of asset that might.
    ///
    /// Implement it and every picker in this toolset offers your declared rows — which is the
    /// whole of the rule: what you declare is what you may name.
    /// </summary>
    public interface IStateTreeNeighbourhood
    {
        /// <summary>The catalogs this asset declares. Never null; empty is a real answer.</summary>
        IReadOnlyList<StateTreeRegistryAsset> DeclaredCatalogs { get; }
    }
}
