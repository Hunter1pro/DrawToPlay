using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Shoves the target away from the owner with the weapon's knockback, optionally mass-independent.
    /// Bakes into one <see cref="KnockbackEffectTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Combat", null, "Knockback")]
    public class KnockbackEffectTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(KnockbackEffectTask);
    }
}
