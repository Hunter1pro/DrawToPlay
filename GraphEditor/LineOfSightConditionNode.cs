using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Casts a ray from the owner to the target and is true when nothing blocks it.
    /// Bakes into one <see cref="LineOfSightCondition"/>; its parameter ports mirror that type's fields 1:1.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Perception", null, "Line Of Sight")]
    public class LineOfSightConditionNode : StateTreeConditionNode
    {
        /// <inheritdoc />
        public override Type conditionType => typeof(LineOfSightCondition);
    }
}
