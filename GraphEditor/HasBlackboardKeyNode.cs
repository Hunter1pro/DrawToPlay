using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// True when the blackboard holds anything at all under this key — the "do I have a target yet?"
    /// test, and the only way to tell an unset key from one set to 0 or "".
    ///
    /// The AI library's convention is that a cleared target REMOVES the key
    /// (<see cref="TargetDetectedCondition"/>'s <c>clearTargetWhenNone</c>), so
    /// <c>Has("target")</c> is the honest way for a graph task to ask whether there is one.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.HasBlackboardKey"/>: <c>stringValue</c> the key.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Blackboard", null, "Has Key")]
    public class HasBlackboardKeyNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the string port holding the blackboard key. Baked as a constant.</summary>
        public const string KeyPortName = "key";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.HasBlackboardKey;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<string>(context, KeyPortName, "Key",
                "Blackboard key to test. Baked as a constant — wiring it has no effect.");
            TaskGraphPorts.AddResult<bool>(context, "True when the key exists, whatever its value.");
        }
    }
}
