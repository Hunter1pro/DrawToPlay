using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// DOCUMENT → PROGRAM (M30.6) — our authoring surface baked into the flat instruction list the
    /// interpreter has always run.
    ///
    /// THE RUNTIME DOES NOT MOVE. That is the whole strategy: a program baked from here and a
    /// program baked from the toolkit graph are the same object, so graphs can be re-authored one
    /// file at a time with nothing downstream noticing, and the test that proves it compares this
    /// bake against that one instruction for instruction.
    ///
    /// The bake is two passes and no cleverness. Pass one gives every instruction its index (in
    /// document order, so the emission is stable). Pass two turns wires into slots: an exec wire
    /// fills <c>exec[pin]</c> with the target's index, a data wire fills <c>data[pin]</c> with the
    /// producer's index, and a literal that has a slot stays a literal. Markers are not
    /// instructions; what they point at becomes an entry index.
    ///
    /// WHAT IT REFUSES TO GUESS: a wire naming a node that is not there, a pin the instruction
    /// does not have, a data wire from something that produces no value. Each is reported by name
    /// and dropped, because a program with a wire nobody can explain is worse than one that is
    /// missing it and says so.
    /// </summary>
    public static class TaskGraphDocBaker
    {
        /// <summary>
        /// Bake it. Problems are appended in the author's language; a document with problems still
        /// bakes as far as it can, which is what makes the list worth reading.
        /// </summary>
        public static GraphTaskAsset Bake(TaskGraphDocument document, List<string> problems)
        {
            var program = ScriptableObject.CreateInstance<GraphTaskAsset>();
            if (document == null)
            {
                problems?.Add("There is no document to bake.");
                return program;
            }

            program.name = string.IsNullOrEmpty(document.programName)
                ? document.name
                : document.programName;

            var index = new Dictionary<string, int>();
            var order = new List<TaskGraphDocNode>();
            for (int i = 0; i < document.nodes.Count; i++)
            {
                TaskGraphDocNode node = document.nodes[i];
                if (node == null || node.IsMarker || string.IsNullOrEmpty(node.id))
                    continue;
                if (index.ContainsKey(node.id))
                {
                    problems?.Add("Two nodes share the id '" + node.id + "'; the second is "
                        + "ignored, and every wire naming it went to the first.");
                    continue;
                }
                index[node.id] = program.nodes.Count;
                order.Add(node);
                program.nodes.Add(GraphTaskProgram.NewInstruction(node.kind));
            }

            for (int i = 0; i < order.Count; i++)
                Fill(program.nodes[i], order[i], program);

            for (int i = 0; i < document.wires.Count; i++)
                Wire(document, document.wires[i], program, index, problems);

            program.enterEntry = EntryIndex(document, TaskGraphEntry.Enter, index, problems);
            program.tickEntry = EntryIndex(document, TaskGraphEntry.Tick, index, problems);
            program.exitEntry = EntryIndex(document, TaskGraphEntry.Exit, index, problems);

            for (int i = 0; i < document.parameters.Count; i++)
            {
                GraphTaskParameter declared = document.parameters[i];
                if (declared == null)
                    continue;
                // COPIED, never shared: the program is an asset of its own and a parameter list
                // aliased to the document would let a bake edit the thing it baked from.
                program.parameters.Add(new GraphTaskParameter
                {
                    name = declared.name,
                    kind = declared.kind,
                    floatValue = declared.floatValue,
                    stringValue = declared.stringValue,
                    id = declared.id,
                    type = declared.type
                });
            }

            DeclareOutputs(program);
            return program;
        }

        /// <summary>The literals and payloads an instruction carries in its own right.</summary>
        private static void Fill(GraphTaskNode instruction, TaskGraphDocNode node,
            GraphTaskAsset program)
        {
            instruction.floatValue = node.floatValue;
            instruction.stringValue = node.stringValue ?? string.Empty;
            instruction.stringValue2 = node.stringValue2 ?? string.Empty;

            // A CALL AND A CONDITION ARE COPIED INTO THE PROGRAM. The document's own sub-asset is
            // what an author edits; the program owns a copy, so re-baking cannot hand two
            // programs the same live object and a running game cannot write back into the
            // document it came from.
            if (node.task != null)
            {
                instruction.task = Object.Instantiate(node.task);
                instruction.task.name = node.task.name;
            }
            if (node.condition != null)
            {
                instruction.condition = Object.Instantiate(node.condition);
                instruction.condition.name = node.condition.name;
            }
        }

        private static void Wire(TaskGraphDocument document, TaskGraphDocWire wire,
            GraphTaskAsset program, Dictionary<string, int> index, List<string> problems)
        {
            if (wire == null)
                return;

            if (!index.TryGetValue(wire.to ?? "", out int target))
            {
                // A MARKER'S WIRE is not a mistake — it is how an entry is stated, and it was
                // already read as one. Anything else naming a node that is not here is.
                if (document.Node(wire.to) == null)
                    problems?.Add("A wire arrives at '" + wire.to + "', which this document has "
                        + "no node for.");
                return;
            }

            TaskGraphDocNode source = document.Node(wire.from);
            if (source == null)
            {
                problems?.Add("A wire leaves '" + wire.from + "', which this document has no node "
                    + "for.");
                return;
            }
            if (source.IsMarker)
                return;   // read as an entry, above

            if (!index.TryGetValue(wire.from, out int from))
                return;

            if (wire.data)
            {
                if (!GraphTaskProgram.IsValue(source.kind))
                {
                    problems?.Add("'" + source.Label + "' produces no value, so the wire into '"
                        + wire.to + "' cannot be filled.");
                    return;
                }
                if (wire.toPin < 0 || wire.toPin >= program.nodes[target].data.Length)
                {
                    problems?.Add("'" + wire.to + "' has no value pin " + wire.toPin + ".");
                    return;
                }
                program.nodes[target].data[wire.toPin] = from;
                return;
            }

            if (wire.fromPin < 0 || wire.fromPin >= program.nodes[from].exec.Length)
            {
                problems?.Add("'" + source.Label + "' has no exec pin " + wire.fromPin
                    + " to leave by.");
                return;
            }
            program.nodes[from].exec[wire.fromPin] = target;
        }

        /// <summary>Where a chain starts: the instruction the marker's wire arrives at, or -1 —
        /// which the interpreter reads as "this program has no such chain".</summary>
        private static int EntryIndex(TaskGraphDocument document, TaskGraphEntry which,
            Dictionary<string, int> index, List<string> problems)
        {
            TaskGraphDocNode marker = document.Entry(which);
            if (marker == null)
                return -1;

            for (int i = 0; i < document.wires.Count; i++)
            {
                TaskGraphDocWire wire = document.wires[i];
                if (wire == null || wire.data || wire.from != marker.id)
                    continue;
                if (index.TryGetValue(wire.to ?? "", out int target))
                    return target;
                problems?.Add("The " + which + " marker points at '" + wire.to
                    + "', which is not an instruction.");
                return -1;
            }

            problems?.Add("The " + which + " marker is wired to nothing, so that chain does "
                + "nothing.");
            return -1;
        }

        /// <summary>
        /// What the program RETURNS, read off the instructions that write it — the same answer the
        /// other baker produces, and the reason a transition can offer a graph's outputs in a
        /// dropdown instead of asking anybody to retype a name the graph already knows.
        /// </summary>
        private static void DeclareOutputs(GraphTaskAsset program)
        {
            for (int i = 0; i < program.nodes.Count; i++)
            {
                GraphTaskNode instruction = program.nodes[i];
                GraphTaskParameterKind kind;
                switch (instruction.kind)
                {
                    case GraphTaskNodeKind.SetOutputFloat: kind = GraphTaskParameterKind.Float; break;
                    case GraphTaskNodeKind.SetOutputString: kind = GraphTaskParameterKind.String; break;
                    case GraphTaskNodeKind.SetOutputBool: kind = GraphTaskParameterKind.Bool; break;
                    default: continue;
                }
                if (string.IsNullOrEmpty(instruction.stringValue))
                    continue;

                var known = false;
                for (int d = 0; d < program.declaredOutputs.Count; d++)
                {
                    if (program.declaredOutputs[d].name == instruction.stringValue)
                        known = true;
                }
                if (!known)
                {
                    program.declaredOutputs.Add(new TaskOutputValue
                    {
                        name = instruction.stringValue, kind = kind
                    });
                }
            }
        }
    }
}
