using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Base of the three Return nodes, carrying the graph's RETURN CONTRACT as pins — the
    /// Unreal Return node: one input data pin per OUTPUT variable declared on the Blackboard
    /// panel, so returning and setting the returns is ONE node and the contract is visible
    /// where the chain ends. A WIRED pin returns its value on that path; an unwired pin
    /// returns nothing there (absence is a non-event — wire a Const node to return a
    /// constant). The Set Output nodes remain for setting a return mid-chain.
    ///
    /// The bake LOWERS wired pins into Set Output instructions ahead of the return, so the
    /// runtime model does not change. Adding or renaming an Output variable redefines these
    /// nodes' pins on the next graph change (<see cref="TaskGraph.OnGraphChanged"/>); a
    /// rename orphans wires exactly as renaming any contract does.
    /// </summary>
    [Serializable]
    public abstract class ReturnNodeBase : Node, ITaskGraphNode
    {
        /// <inheritdoc />
        public abstract GraphTaskNodeKind nodeKind { get; }

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);

            foreach (IVariable variable in OutputVariables(Graph))
            {
                string name = KeyVariables.NameOf(variable);
                Type type = KeyVariables.TypeOf(variable);
                const string tooltip = "Returned under this name when this Return runs. "
                    + "Unwired = not returned on this path.";
                if (type == typeof(float) || type == typeof(int))
                    TaskGraphPorts.AddData<float>(context, name, name, tooltip);
                else if (type == typeof(string))
                    TaskGraphPorts.AddData<string>(context, name, name, tooltip);
                else if (type == typeof(bool))
                    TaskGraphPorts.AddData<bool>(context, name, name, tooltip);
            }
        }

        /// <summary>The graph's Output-kind variables, guarded like every read of the lazy
        /// variable model. Empty when the node is not on a graph yet.</summary>
        internal static IEnumerable<IVariable> OutputVariables(Graph graph)
        {
            var outputs = new List<IVariable>();
            IEnumerable<IVariable> variables = null;
            try
            {
                variables = graph != null ? graph.GetVariables() : null;
            }
            catch (Exception)
            {
                // A half-built graph model answers with a throw; no pins is the honest result.
            }
            if (variables == null)
                return outputs;

            foreach (IVariable variable in variables)
            {
                if (variable == null)
                    continue;
                try
                {
                    if (variable.VariableKind != VariableKind.Output)
                        continue;
                }
                catch (Exception)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(KeyVariables.NameOf(variable)))
                    outputs.Add(variable);
            }
            return outputs;
        }
    }
}
