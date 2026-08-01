using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Base for every task block: one graph block = one <see cref="StateTreeTaskAsset"/> instance
    /// in the baked state's <c>tasks</c> list, in block order.
    ///
    /// Subclasses carry no code beyond naming their runtime type — the parameter ports are
    /// generated from that type's public fields by <see cref="LibraryParameterPorts"/>, so the port
    /// names and the runtime field names are the same strings by construction rather than by
    /// discipline.
    ///
    /// This class is abstract on purpose: abstract block types are skipped by the block palette
    /// (PublicGraphFactory, UnityEditor.GraphToolkitModule IL 345348-345366), so only the concrete
    /// wrappers are offered inside a state.
    /// </summary>
    [Serializable]
    public abstract class StateTaskBlockNode : BlockNode
    {
        /// <summary>
        /// The <see cref="StateTreeTaskAsset"/> subclass this block bakes into. The bake creates one
        /// instance of it per block and copies every parameter port into the field of the same name.
        /// </summary>
        public abstract Type taskType { get; }

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            LibraryParameterPorts.DefineParameterPorts(context, taskType);
        }
    }
}
