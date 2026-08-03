using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Compares two numbers and produces the bool a <see cref="BranchNode"/> wants. The workhorse
    /// data node: "have I waited long enough", "is the counter at three", "is health under a half"
    /// are all this node with a <see cref="GetBlackboardFloatNode"/> on the left.
    ///
    /// The RIGHT-HAND SIDE HAS A TYPED-IN FALLBACK (the program model keeps it in the instruction's
    /// own <c>floatValue</c>), so the common "value vs constant" shape needs one node, not two. The
    /// left-hand side has no such slot; an unwired left reads 0, and a literal typed there is carried
    /// by a constant instruction the bake adds for it.
    ///
    /// EQUALITY IS APPROXIMATE. <see cref="Op.Equal"/> and <see cref="Op.NotEqual"/> compare with an
    /// epsilon of 1e-4 at runtime, because exact float equality on values that came from a physics
    /// step or an accumulating timer is a bug waiting to happen.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.CompareFloat"/>: <c>data[0]</c> left, <c>data[1]</c>
    /// right (or <c>floatValue</c> when unwired), <c>stringValue</c> the operator symbol.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "Compare Float")]
    public class CompareFloatNode : Node, ITaskGraphNode
    {
        /// <summary>
        /// How the two sides are compared. AN ENUM PORT RATHER THAN A NODE OPTION, for the reason
        /// stated once in <see cref="StateTreeGraph"/>: <see cref="INodeOption"/> exposes
        /// <c>TryGetValue</c> and no setter (UnityEditor.GraphToolkitModule, INodeOption IL
        /// 21204-21249) while <see cref="IPort"/> exposes both (IPort IL 21296-21305), so an option
        /// cannot be written programmatically and <see cref="TaskGraphAuthoring"/> could not author a
        /// comparison. Graph Toolkit draws an enum port inline as a dropdown, so it also reads better
        /// than a string.
        /// </summary>
        public enum Op
        {
            /// <summary>left &lt; right.</summary>
            Less,

            /// <summary>left &lt;= right.</summary>
            LessOrEqual,

            /// <summary>left &gt; right.</summary>
            Greater,

            /// <summary>left &gt;= right.</summary>
            GreaterOrEqual,

            /// <summary>|left - right| &lt;= 1e-4.</summary>
            Equal,

            /// <summary>|left - right| &gt; 1e-4.</summary>
            NotEqual
        }

        /// <summary>Name of the left-hand float data pin (<c>data[0]</c>).</summary>
        public const string LeftPortName = "left";

        /// <summary>Name of the right-hand float data pin (<c>data[1]</c>).</summary>
        public const string RightPortName = "right";

        /// <summary>Name of the enum port carrying the comparison.</summary>
        public const string OpPortName = "op";

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.CompareFloat;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<float>(context, LeftPortName, "Left",
                "Left-hand value. Unwired reads 0.");

            context.AddInputPort<Op>(OpPortName)
                .WithDisplayName("Op")
                .WithTooltip("How the two sides are compared. Equal/Not Equal use a 1e-4 epsilon.")
                .WithDefaultValue(Op.GreaterOrEqual)
                .Build();

            TaskGraphPorts.AddData<float>(context, RightPortName, "Right",
                "Right-hand value. Wire it, or type a constant.");

            TaskGraphPorts.AddResult<bool>(context, "The result of the comparison.");
        }

        /// <summary>
        /// The operator symbol the runtime program stores — the exact strings the interpreter
        /// switches on ("&lt;", "&lt;=", "&gt;", "&gt;=", "==", "!="). Lives here rather than in the
        /// baker because this class owns <see cref="Op"/>, so the enum and its encoding cannot drift.
        /// </summary>
        /// <param name="op">The authored comparison.</param>
        /// <returns>The symbol for <c>GraphTaskNode.stringValue</c>.</returns>
        public static string ToOperator(Op op)
        {
            switch (op)
            {
                case Op.Less: return "<";
                case Op.LessOrEqual: return "<=";
                case Op.Greater: return ">";
                case Op.GreaterOrEqual: return ">=";
                case Op.Equal: return "==";
                case Op.NotEqual: return "!=";
                default: return ">=";
            }
        }
    }
}
