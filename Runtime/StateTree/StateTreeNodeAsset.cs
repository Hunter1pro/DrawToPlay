using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Port of state_tree_node.gd: tasks run while active, children are
    /// organizational nesting (entry resolves to the first leaf), transitions lead out.
    /// The runner treats the tree as flat — children are recursed only for the id index.</summary>
    public sealed class StateTreeNodeAsset : ScriptableObject
    {
        public string nodeId = "";
        public string displayName = "";
        public List<StateTreeTaskAsset> tasks = new List<StateTreeTaskAsset>();
        public List<StateTreeNodeAsset> children = new List<StateTreeNodeAsset>();
        public List<StateTreeTransition> transitions = new List<StateTreeTransition>();

        /// <summary>
        /// This state's wires from the TREE's declared parameters into the serialized fields of the
        /// tasks and transition conditions above (M7i). A parameter otherwise only reaches a task
        /// through the blackboard, by a key the task's author hand-typed — which no plain
        /// <c>public float speed</c> field can do — so this list is what makes a declared parameter
        /// connectable at all.
        ///
        /// Applied by <see cref="StateTreeExecutor.StartTree"/> to the DEEP COPY it runs, once per
        /// start: the authored sub-assets are never written to, and a sub-tree re-activated with a
        /// different override re-copies and re-binds. See <see cref="StateTreeFieldBinding"/> for
        /// why the target is an index and the parameter an id.
        ///
        /// Empty for every node authored before M7i, which is exactly what "nothing is bound"
        /// means — the extension is additive and nothing above it moved.
        /// </summary>
        public List<StateTreeFieldBinding> bindings = new List<StateTreeFieldBinding>();

        /// <summary>The M12 key wires: which of this node's key-semantic string fields are
        /// connected to a DECLARED key (by id). The executor rewrites each linked field to
        /// the declaration's current name at StartTree; the field's own text is the fallback
        /// for unmanaged (free-typed) keys. Node-level list, so it rides
        /// <see cref="StateTreeAsset.DeepCopy"/>'s Instantiate like <see cref="bindings"/>
        /// does — only TRANSITION fields need DeepCopyNode's hand-copy.</summary>
        public List<StateTreeKeyLink> keyLinks = new List<StateTreeKeyLink>();
    }

}
