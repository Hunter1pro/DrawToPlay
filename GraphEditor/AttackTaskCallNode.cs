using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Lands one hit on the target's HealthComponent using the weapon def's damage, and emits damageDealt.
    /// Calls <see cref="AttackTask"/> latently: Success once the swing lands, Failure when there is
    /// nothing in reach or the cooldown has not run out. Its parameter ports mirror that type's
    /// fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Combat", null, "Attack")]
    public class AttackTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(AttackTask);
    }
}
