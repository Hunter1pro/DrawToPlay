using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Shoves the target away from the owner with the weapon's knockback, optionally mass-independent.
    /// Calls <see cref="KnockbackEffectTask"/>; a one-shot impulse, so the chain continues out of
    /// Success in the same tick. Its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Combat", null, "Knockback")]
    public class KnockbackEffectTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(KnockbackEffectTask);
    }
}
