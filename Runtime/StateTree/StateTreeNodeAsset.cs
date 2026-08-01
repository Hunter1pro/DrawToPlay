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
    }

}
