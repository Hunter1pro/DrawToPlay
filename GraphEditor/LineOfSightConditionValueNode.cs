using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Casts a ray from the owner to the target and is true when nothing blocks it.
    /// Evaluates <see cref="LineOfSightCondition"/> as a bool. It costs a PHYSICS QUERY on every read,
    /// and <see cref="BoolAndNode"/> does NOT short-circuit — the interpreter pulls both operands
    /// because a condition is allowed side effects — so putting it behind a cheap test in an And saves
    /// nothing. To skip the cast, put the cheap test on a <see cref="BranchNode"/> and read this one
    /// only on the branch that needs it. Its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Conditions/Perception", null, "Line Of Sight")]
    public class LineOfSightConditionValueNode : ConditionValueNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(LineOfSightCondition);
    }
}
