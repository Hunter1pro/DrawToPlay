using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Lands one hit on the target's HealthComponent using the weapon def's damage, and emits damageDealt.
    /// Bakes into one <see cref="AttackTask"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Tasks/Combat", null, "Attack")]
    public class AttackTaskNode : StateTaskBlockNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(AttackTask);
    }
}
