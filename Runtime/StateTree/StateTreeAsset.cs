using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Root asset — port of state_tree_data.gd. ScriptableObject-only
    /// persistence (brief §7.1): the SO IS the source of truth, no JSON mirror.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/State Tree", fileName = "StateTree")]
    public sealed class StateTreeAsset : ScriptableObject
    {
        public string treeName = "";
        public string treeKind = "enemy_ai";
        public StateTreeNodeAsset root;

        /// <summary>Deep-copy the whole tree (nodes, tasks, conditions) — the
        /// data.duplicate(true) mirror that keeps shared assets from sharing task
        /// instance state across runners.</summary>
        public StateTreeAsset DeepCopy()
        {
            var copy = Instantiate(this);
            copy.root = DeepCopyNode(root);
            return copy;
        }

        private static StateTreeNodeAsset DeepCopyNode(StateTreeNodeAsset node, int depth = 0)
        {
            // An authored children cycle must not stack-overflow the editor; 256 matches
            // the chain guards used across the package (ShapeRig, RestRootMatrix).
            if (node == null || depth > 256)
                return null;
            var copy = Instantiate(node);
            copy.tasks = new List<StateTreeTaskAsset>(node.tasks.Count);
            foreach (var task in node.tasks)
                copy.tasks.Add(task != null ? Instantiate(task) : null);
            copy.transitions = new List<StateTreeTransition>(node.transitions.Count);
            foreach (var tr in node.transitions)
            {
                copy.transitions.Add(tr == null ? null : new StateTreeTransition
                {
                    targetNodeId = tr.targetNodeId,
                    condition = tr.condition != null ? Instantiate(tr.condition) : null,
                    checkWhileRunning = tr.checkWhileRunning
                });
            }
            copy.children = new List<StateTreeNodeAsset>(node.children.Count);
            foreach (var child in node.children)
                copy.children.Add(DeepCopyNode(child, depth + 1));
            return copy;
        }

        /// <summary>Destroy a DeepCopy graph — Unity Instantiate copies live until domain
        /// reload otherwise, so every runner restart would leak a tree. Play-mode and
        /// edit-mode (tests) safe.</summary>
        public static void DestroyCopy(StateTreeAsset copy)
        {
            if (copy == null)
                return;
            DestroyNodeCopy(copy.root, 0);
            DestroyTreeObject(copy);
        }

        private static void DestroyNodeCopy(StateTreeNodeAsset node, int depth)
        {
            if (node == null || depth > 256)
                return;
            foreach (var task in node.tasks)
                DestroyTreeObject(task);
            foreach (var tr in node.transitions)
                if (tr != null)
                    DestroyTreeObject(tr.condition);
            foreach (var child in node.children)
                DestroyNodeCopy(child, depth + 1);
            DestroyTreeObject(node);
        }

        private static void DestroyTreeObject(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
