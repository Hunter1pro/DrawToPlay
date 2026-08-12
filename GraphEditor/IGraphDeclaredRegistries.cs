using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Implemented by a canvas that names some of its data ON the canvas, instead of only being
    /// reached through a registry row.
    ///
    /// A <c>.statetree</c> does: its Entry node carries a registry port, and the bake copies it
    /// onto the tree's <see cref="StateTreeAsset.registries"/>. That registry is the tree's own
    /// declaration and must count as a root of <see cref="GraphRegistryScope"/> — otherwise a
    /// tree that already says which data it speaks would still be told nothing points at it.
    ///
    /// A <c>.taskgraph</c> does NOT: a program has no data list of its own (its host does), which
    /// is the whole reason the dependency edge lives on the registry.
    /// </summary>
    public interface IGraphDeclaredRegistries
    {
        /// <summary>The registries this canvas names for itself, before dependencies are
        /// followed.</summary>
        /// <param name="into">Accumulator; not cleared. Nulls must not be added.</param>
        void CollectDeclaredRegistries(List<StateTreeRegistryAsset> into);
    }
}
