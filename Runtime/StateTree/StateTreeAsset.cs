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

        /// <summary>
        /// The tree's PARAMETERS, and therefore its BLACKBOARD CONTRACT (M7g). A tree has no
        /// argument list of its own — the only way a state inside it receives a value is by
        /// reading a blackboard key — so declaring a parameter here does two jobs at once: it
        /// documents "this tree expects a key called X" and it becomes the knob the caller turns.
        /// <see cref="RunSubTreeTask"/> seeds each declared name into the shared blackboard on
        /// every activation, at the caller's override or at the default declared here, which is
        /// the same Blueprint-instance model <see cref="RunGraphTask"/> gives a logic graph.
        ///
        /// The row type is REUSED from the graph-task parameter model
        /// (<see cref="GraphTaskParameter"/> / <see cref="GraphTaskParameterKind"/>) rather than
        /// cloned: both authored-task flavours then present ONE parameter vocabulary to the
        /// inspector, the picker and the author, and a Bool still rides in
        /// <see cref="GraphTaskParameter.floatValue"/> (!= 0).
        ///
        /// Declared on EVERY tree, not only <c>treeKind == "task"</c> ones: a root tree's
        /// declaration is the documentation of the ambient keys it assumes exist.
        ///
        /// Empty for every tree authored before M7g, which is exactly what "no parameters" means
        /// — the extension is additive and nothing above it moved. <see cref="DeepCopy"/> carries
        /// it with no change of its own: <c>Instantiate</c> clones the SERIALIZED data, and a
        /// <c>List&lt;[Serializable] class&gt;</c> is serialized data (the same mechanism that
        /// carries <see cref="treeName"/>), so the copy gets its own list of its own rows.
        /// </summary>
        public List<GraphTaskParameter> parameters = new List<GraphTaskParameter>();

        /// <summary>The M12 KEY CONTRACT: the keys this tree owns, id-identified and freely
        /// renameable — a scope's contract is simply its mounted tree's list. Same
        /// serialization ride as <see cref="parameters"/>.</summary>
        public List<StateTreeKeyDeclaration> keys = new List<StateTreeKeyDeclaration>();

        /// <summary>Trees whose declarations this tree IMPORTS — the horizontal share, made a
        /// visible dependency instead of matching text. Vertical sharing needs no entry here:
        /// the mount chain resolves at runtime by itself.</summary>
        public List<StateTreeAsset> uses = new List<StateTreeAsset>();

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
                    checkWhileRunning = tr.checkWhileRunning,
                    // The executor routes outputs off the COPY's transitions, so a field left out of
                    // this hand-written list is a feature that silently never runs (M7j).
                    outputRoutes = TransitionOutputRoute.CopyList(tr.outputRoutes)
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
