using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// OUR OWN GRAPH DOCUMENT (M30.6) — an authored program, as plain serialized data.
    ///
    /// The complaint that started this was specific: wiring is awkward, a parameter cannot be
    /// made outside a node and connected, returns cannot be typed, and nothing composes into
    /// something reusable elsewhere. All four are properties of the AUTHORING model, not of the
    /// runtime, so this replaces the authoring model and leaves the runtime alone — a document
    /// bakes to exactly the flat program the interpreter already runs
    /// (<see cref="GraphTaskAsset"/>), and a graph authored either way is indistinguishable to it.
    ///
    /// IT IS A LIST OF NODES AND A LIST OF WIRES, and that is the whole model. A wire names two
    /// node IDS and a pin, so nodes can be reordered, renamed and moved without touching a single
    /// connection — the thing that makes a graph survive editing. Indices appear only at bake
    /// time, where the program needs them.
    ///
    /// ENTRY NODES ARE MARKERS, not instructions: "the tick starts here" is a wire from a marker
    /// to the first thing that runs, which is how the program's three entry indices are found.
    ///
    /// Editor-side by design (the runtime never sees an authoring type — the same boundary the
    /// toolkit path keeps), and deliberately dull: everything interesting is in the baker and the
    /// window, where it can be reviewed.
    /// </summary>
    public sealed class TaskGraphDocument : ScriptableObject
    {
        [Tooltip("What the baked program is called. Empty takes this document's name.")]
        public string programName = "";

        [Tooltip("The nodes, in the order they were added — which is the order the program is "
            + "emitted in, so a document's bake is stable across edits elsewhere.")]
        public List<TaskGraphDocNode> nodes = new List<TaskGraphDocNode>();

        [Tooltip("The wires. Each names two node ids and a pin, never an index.")]
        public List<TaskGraphDocWire> wires = new List<TaskGraphDocWire>();

        [Tooltip("The knobs a caller may override — created OUTSIDE any node and read by "
            + "GetParam nodes, which is the wiring complaint answered.")]
        public List<GraphTaskParameter> parameters = new List<GraphTaskParameter>();

        /// <summary>The node with this id, or null.</summary>
        public TaskGraphDocNode Node(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && nodes[i].id == id)
                    return nodes[i];
            }
            return null;
        }

        /// <summary>The marker for a chain, or null when this document does not start one.</summary>
        public TaskGraphDocNode Entry(TaskGraphEntry which)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && nodes[i].entry == which)
                    return nodes[i];
            }
            return null;
        }

        /// <summary>An id nothing else in this document uses.</summary>
        public string MintId(string hint)
        {
            string root = string.IsNullOrEmpty(hint) ? "node" : hint.ToLowerInvariant();
            for (int i = 1; ; i++)
            {
                string candidate = root + "-" + i;
                if (Node(candidate) == null)
                    return candidate;
            }
        }
    }

    /// <summary>Which chain a marker starts. <see cref="None"/> is an ordinary instruction.</summary>
    public enum TaskGraphEntry
    {
        None = 0,
        Enter = 1,
        Tick = 2,
        Exit = 3
    }

    /// <summary>
    /// One authored node: an instruction of the program's own vocabulary, or a marker.
    ///
    /// The literal fields are the SAME three the instruction has, because the program already
    /// carries a literal slot for the pins that can have one (a Set's value, a Wait's seconds, a
    /// Compare's right side). An authored literal therefore rides in the instruction rather than
    /// becoming a constant node nobody asked for — the bake stays one-for-one, which is what
    /// makes it checkable against the other baker.
    /// </summary>
    [Serializable]
    public sealed class TaskGraphDocNode
    {
        [Tooltip("Stable within this document — what wires refer to. Never an index.")]
        public string id = "";

        [Tooltip("The instruction this node is. Ignored on a marker.")]
        public GraphTaskNodeKind kind;

        [Tooltip("Marker: which chain starts here. None is an ordinary instruction.")]
        public TaskGraphEntry entry = TaskGraphEntry.None;

        [Tooltip("Where it sits on the canvas — authoring only; the bake never reads it.")]
        public Vector2 position;

        [Tooltip("What the author called it. Empty shows the kind.")]
        public string title = "";

        [Tooltip("Literal for a constant, a Wait's seconds, a Compare's right side.")]
        public float floatValue;

        [Tooltip("The key, the cue name, the output name, the compare operator.")]
        public string stringValue = "";

        [Tooltip("Second literal — a Set String's unwired value, whose stringValue is spent on "
            + "the key.")]
        public string stringValue2 = "";

        [Tooltip("The call this node makes (DoTask). A sub-asset of this document.")]
        public StateTreeTaskAsset task;

        [Tooltip("The condition this node evaluates. A sub-asset of this document.")]
        public StateTreeConditionAsset condition;

        /// <summary>Is this a chain marker rather than something that runs?</summary>
        public bool IsMarker => entry != TaskGraphEntry.None;

        public string Label => string.IsNullOrEmpty(title) ? kind.ToString() : title;
    }

    /// <summary>
    /// One connection. EXEC wires say what runs next; DATA wires say where a value comes from.
    ///
    /// Both directions are stored the way an author draws them — from the thing that produces to
    /// the thing that consumes — and the bake turns that into the program's two arrays without
    /// the author ever seeing an index.
    /// </summary>
    [Serializable]
    public sealed class TaskGraphDocWire
    {
        [Tooltip("The node this leaves.")]
        public string from = "";

        [Tooltip("Which of its exec out-pins (0 = next / success, 1 = else / failure). "
            + "Unused on a data wire: a value node has one out.")]
        public int fromPin;

        [Tooltip("The node this arrives at.")]
        public string to = "";

        [Tooltip("Which data in-pin it fills. Unused on an exec wire: an instruction has one "
            + "way in.")]
        public int toPin;

        [Tooltip("True for a value, false for control.")]
        public bool data;
    }
}
