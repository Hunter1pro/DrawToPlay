using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Adds a RETURN parameter (an Output variable) to a .taskgraph — the declaring half of
    /// the return contract, offered from BOTH sides: the graph's own tooling, and the State
    /// Tree window's Returns section ("+"), which reaches this by REFLECTION because the main
    /// editor assembly deliberately never references this one (the §7.3 boundary — same
    /// pattern as <c>StateTreeGraphBridge</c> discovering the baker).
    ///
    /// Declaring from the call site still edits the CALLEE: the graph is shared by live
    /// reference, so the new pin appears for every state that runs it — exactly like adding
    /// an out parameter to a function.
    /// </summary>
    public static class TaskGraphReturnAuthoring
    {
        /// <summary>Adds an Output variable named <paramref name="name"/> of
        /// <paramref name="kindName"/> ("Float" / "String" / "Bool") to the graph at
        /// <paramref name="graphPath"/> and reimports it. Null on success, else the reason.
        /// Signature is reflection-called — keep it (string, string, string).</summary>
        public static string AddReturnParameter(string graphPath, string name, string kindName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "A return needs a name.";
            name = name.Trim();

            Type type = kindName == "Float" ? typeof(float)
                : kindName == "String" ? typeof(string)
                : kindName == "Bool" ? typeof(bool)
                : null;
            if (type == null)
                return $"'{kindName}' is not a return kind (Float, String or Bool).";

            TaskGraph graph;
            try
            {
                graph = GraphDatabase.LoadGraph<TaskGraph>(graphPath);
            }
            catch (Exception exception)
            {
                return $"Could not load '{graphPath}': {exception.Message}";
            }
            if (graph == null)
                return $"'{graphPath}' is not a task graph.";

            foreach (IVariable variable in SafeVariables(graph))
            {
                if (string.Equals(KeyVariables.NameOf(variable), name, StringComparison.Ordinal))
                    return $"The graph already declares a variable called '{name}'.";
            }

            try
            {
                object defaultValue = type == typeof(float) ? (object)0f
                    : type == typeof(bool) ? (object)false
                    : string.Empty;
                if (graph.CreateVariable(name, type, defaultValue, VariableKind.Output) == null)
                    return "The graph refused the variable.";
            }
            catch (Exception exception)
            {
                return $"Creating '{name}' failed: {exception.Message}";
            }

            TaskGraphAuthoring.SaveAndImport(graph, graphPath);
            return null;
        }

        private static System.Collections.Generic.IEnumerable<IVariable> SafeVariables(
            TaskGraph graph)
        {
            try
            {
                return graph.GetVariables()
                    ?? (System.Collections.Generic.IEnumerable<IVariable>)Array.Empty<IVariable>();
            }
            catch (Exception)
            {
                return Array.Empty<IVariable>();
            }
        }
    }
}
