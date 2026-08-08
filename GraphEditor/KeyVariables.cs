using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The key⟷variable seam, shared by both canvases — but meaning something DIFFERENT on
    /// each, the same split the runtime has:
    ///
    /// On a STATE canvas (.statetree) the variable panel is the tree's KEY HEADER: a string
    /// variable IS a <see cref="StateTreeKeyDeclaration"/> (its name is the key name), and a
    /// key-semantic port connected to one is the M12 id-wire, canvas flavor — the bake writes
    /// <c>keyId</c> from the variable's stable identity, so renaming the variable renames the
    /// key everywhere the wire reaches. An unconnected port stays a typed-in string: the
    /// free-typed flavor, still legal.
    ///
    /// On a TASK canvas (.taskgraph) the variable panel is the program's PARAMETERS: a program
    /// does not own vocabulary (its host tree does), so a key rides as a String parameter whose
    /// VALUE is the key name — declared once, wired to every port that reads it, retargetable
    /// per state through the parameter override the call site already has.
    /// </summary>
    internal static class KeyVariables
    {
        /// <summary>Separator format of the entry marker's Key Kinds port:
        /// <c>name=Kind;name=Kind</c>. The port exists because a kind is editor metadata a
        /// string variable cannot carry — see <see cref="ParseKindOverrides"/>.</summary>
        private const char k_PairSeparator = ';';

        private const char k_KindSeparator = '=';

        /// <summary>The variable a key-semantic port takes its value from, when it is wired to
        /// one. False for constants, unconnected ports, and half-built variable models (Graph
        /// Toolkit builds them lazily and a mid-edit one throws rather than answering).</summary>
        public static bool TryConnectedVariable(IPort port, out IVariable variable)
        {
            variable = null;
            if (port == null || !port.IsConnected)
                return false;
            try
            {
                if (port.FirstConnectedPort?.GetNode() is IVariableNode variableNode)
                    variable = variableNode.Variable;
            }
            catch (Exception)
            {
                variable = null;
            }
            return variable != null;
        }

        /// <summary>A variable's name, or empty — the same guarded read every consumer of this
        /// surface needs, stated once.</summary>
        public static string NameOf(IVariable variable)
        {
            try
            {
                return variable?.Name ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>A variable's declared data type, or null under the same lazy-model caveat.</summary>
        public static Type TypeOf(IVariable variable)
        {
            try
            {
                return variable?.DataType;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The kind of the key a graph variable declares, derived from its USES: every field its
        /// nodes are wired into votes through its <see cref="StateTreeKeyAttribute"/>. A field
        /// declared for one kind is a STRONG vote; a generic atom's field
        /// (<c>any: true</c> — set/get/has-key) only hints, so its vote counts only when no
        /// strong vote exists and all hints agree. No votes at all falls back to
        /// <see cref="StateTreeKeyKind.String"/> silently — a name is a string, and an unused
        /// declaration is not a problem. <paramref name="disagreement"/> is set ONLY when votes
        /// conflicted, which is the one outcome worth a warning (the marker's Key Kinds port is
        /// the fix).
        /// </summary>
        public static StateTreeKeyKind DeriveKind(IVariable variable, out string disagreement)
        {
            disagreement = null;
            var strong = new List<StateTreeKeyKind>();
            var hints = new List<StateTreeKeyKind>();

            var nodes = new List<IVariableNode>();
            try
            {
                variable.GetNodes(nodes);
            }
            catch (Exception)
            {
                return StateTreeKeyKind.String;
            }

            for (int i = 0; i < nodes.Count; i++)
                CollectVotes(nodes[i], strong, hints);

            if (strong.Count > 0)
            {
                for (int i = 1; i < strong.Count; i++)
                {
                    if (strong[i] != strong[0])
                    {
                        disagreement = $"its uses disagree ({strong[0]} vs {strong[i]}); "
                            + $"{strong[0]} won";
                        break;
                    }
                }
                return strong[0];
            }

            for (int i = 1; i < hints.Count; i++)
            {
                if (hints[i] != hints[0])
                {
                    disagreement = "only generic atoms use it and their kind hints disagree; "
                        + "String assumed";
                    return StateTreeKeyKind.String;
                }
            }
            return hints.Count > 0 ? hints[0] : StateTreeKeyKind.String;
        }

        /// <summary>One variable node's outgoing wires → kind votes, through the library field
        /// each connected port mirrors.</summary>
        private static void CollectVotes(IVariableNode node, List<StateTreeKeyKind> strong,
            List<StateTreeKeyKind> hints)
        {
            if (!(node is INode graphNode))
                return;

            foreach (IPort output in graphNode.GetOutputPorts())
            {
                if (output == null || !output.IsConnected)
                    continue;
                var connected = new List<IPort>();
                output.GetConnectedPorts(connected);
                for (int i = 0; i < connected.Count; i++)
                {
                    IPort input = connected[i];
                    INode consumer = input?.GetNode();
                    Type libraryType = LibraryTypeOf(consumer);
                    if (libraryType == null)
                        continue;

                    IReadOnlyList<FieldInfo> fields =
                        LibraryParameterPorts.GetParameterFields(libraryType);
                    for (int f = 0; f < fields.Count; f++)
                    {
                        FieldInfo field = fields[f];
                        if (field.FieldType != typeof(StateTreeKeyField)
                            || !string.Equals(field.Name, input.Name, StringComparison.Ordinal))
                            continue;
                        var attribute = field.GetCustomAttribute<StateTreeKeyAttribute>();
                        if (attribute == null)
                            break;
                        (attribute.any ? hints : strong).Add(attribute.kind);
                        break;
                    }
                }
            }
        }

        /// <summary>The runtime library type a wrapper node stands for, whichever canvas it is
        /// on — null for anything that wraps nothing (states, transitions, flow).</summary>
        private static Type LibraryTypeOf(INode node)
        {
            switch (node)
            {
                case StateTaskBlockNode block:
                    return block.taskType;
                case TaskCallNode call:
                    return call.taskType;
                case StateTreeConditionNode condition:
                    return condition.conditionType;
                default:
                    return null;
            }
        }

        /// <summary>
        /// <c>"portal:next=Event;level:goto=String"</c> → name→kind. This is the entry marker's
        /// Key Kinds port: a kind is picker/validation metadata that a string variable has
        /// nowhere to carry, so declarations whose kind DeriveKind cannot recover (an Event
        /// consumed only by generic atoms) keep it here. Unknown kind names are skipped —
        /// reported by the caller, which has a log.
        /// </summary>
        public static Dictionary<string, StateTreeKeyKind> ParseKindOverrides(string text,
            List<string> problems)
        {
            var kinds = new Dictionary<string, StateTreeKeyKind>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text))
                return kinds;

            string[] pairs = text.Split(k_PairSeparator);
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i].Trim();
                if (pair.Length == 0)
                    continue;
                int split = pair.LastIndexOf(k_KindSeparator);
                string name = split > 0 ? pair.Substring(0, split).Trim() : string.Empty;
                string kindText = split > 0 ? pair.Substring(split + 1).Trim() : string.Empty;
                if (name.Length == 0
                    || !Enum.TryParse(kindText, ignoreCase: true, out StateTreeKeyKind kind))
                {
                    problems?.Add($"Key Kinds entry '{pair}' is not 'name{k_KindSeparator}Kind' "
                        + "with a known kind; it was ignored.");
                    continue;
                }
                kinds[name] = kind;
            }
            return kinds;
        }

        /// <summary>The inverse, written by the converter so a converted tree's kinds survive
        /// the round trip exactly.</summary>
        public static string FormatKindOverrides(List<StateTreeKeyDeclaration> declarations)
        {
            if (declarations == null || declarations.Count == 0)
                return string.Empty;
            var parts = new List<string>(declarations.Count);
            for (int i = 0; i < declarations.Count; i++)
            {
                StateTreeKeyDeclaration declaration = declarations[i];
                if (declaration != null && !string.IsNullOrEmpty(declaration.name))
                    parts.Add(declaration.name + k_KindSeparator + declaration.kind);
            }
            return string.Join(k_PairSeparator.ToString(), parts);
        }
    }
}
