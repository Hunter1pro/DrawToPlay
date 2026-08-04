using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The asset half of the direct State Tree editor: every mutation the window performs on a
    /// live <see cref="StateTreeAsset"/> goes through here, so the sub-asset lifecycle exists in
    /// exactly one place.
    ///
    /// WHY THIS IS NOT A BAKE STEP. The window edits the same nodes/tasks/conditions a
    /// <see cref="StateTreeRunner"/> loads — there is no intermediate graph model and nothing to
    /// compile. That makes the sub-asset bookkeeping (create → AddObjectToAsset → register undo,
    /// remove → clear the reference first, then DestroyObjectImmediate) the load-bearing part of
    /// the tool rather than an implementation detail, hence a dedicated file.
    ///
    /// Sub-asset names follow <c>StateTreePresets</c> verbatim ("Node {order} {id}",
    /// "Task {id} {Type}", "Cond {from}-&gt;{to} {Type}") so a hand-authored tree and a preset
    /// tree are indistinguishable in the Project window — with two additions, "Task {id}
    /// SubTree:{tree}" and "Task {id} Graph:{graph}", because a class name is not what
    /// distinguishes one composite from another. <see cref="RefreshSubAssetNames"/> recomputes the
    /// whole set after any structural edit; it is idempotent by construction.
    ///
    /// Composite tasks (<see cref="CreateSubTreeTask"/>) are also why this file knows about
    /// TREE-level state: whether a tree is a reusable task is one string field on the asset, and
    /// the check that keeps a composition from closing a loop (<see cref="CreatesCycle"/>) has to
    /// read the authored graph of other assets entirely.
    ///
    /// Saving is NOT done here. Callers batch it (see StateTreeEditorWindow) because
    /// AssetDatabase.SaveAssets() on every keystroke stalls the editor on large trees.
    /// </summary>
    internal static class StateTreeEditorOps
    {
        /// <summary>Matches the runner's own recursion guard: an authored children cycle must
        /// never stack-overflow the editor either.</summary>
        internal const int DepthGuard = 256;

        /// <summary><see cref="StateTreeAsset.treeKind"/> value that marks a tree as a reusable
        /// composite task. It is the ONLY thing that puts a tree in another tree's task picker,
        /// so the string lives here rather than being spelled out at each site.</summary>
        internal const string TaskTreeKind = "task";

        /// <summary>Fallback kind when a tree stops being a reusable task and the editor has no
        /// previous kind to restore — the same default <see cref="StateTreeAsset"/> ships.</summary>
        internal const string DefaultTreeKind = "enemy_ai";

        // --- undo groups ------------------------------------------------------------------

        /// <summary>One undo step per user gesture (m0 undo contract). Same idiom as
        /// PoseSheetWindow so both windows collapse identically.</summary>
        internal static int BeginUndoGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        internal static void EndUndoGroup(int group)
        {
            Undo.CollapseUndoOperations(group);
        }

        // --- traversal --------------------------------------------------------------------

        internal static List<StateTreeNodeAsset> CollectNodes(StateTreeAsset tree)
        {
            var nodes = new List<StateTreeNodeAsset>();
            if (tree != null)
                CollectNodes(tree.root, nodes, new HashSet<StateTreeNodeAsset>(), 0);
            return nodes;
        }

        private static void CollectNodes(StateTreeNodeAsset node, List<StateTreeNodeAsset> into,
            HashSet<StateTreeNodeAsset> visited, int depth)
        {
            if (node == null || depth > DepthGuard || !visited.Add(node))
                return;

            into.Add(node);
            for (var i = 0; i < node.children.Count; ++i)
                CollectNodes(node.children[i], into, visited, depth + 1);
        }

        internal static StateTreeNodeAsset FindParent(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            if (tree == null || node == null || node == tree.root)
                return null;

            var all = CollectNodes(tree);
            for (var i = 0; i < all.Count; ++i)
            {
                if (all[i].children.Contains(node))
                    return all[i];
            }

            return null;
        }

        /// <summary>Cycle guard for drag-and-drop: a node may never be dropped into its own
        /// subtree, and the check has to survive an already-broken tree, hence the depth
        /// counter rather than pure recursion on trust.</summary>
        internal static bool IsSelfOrDescendant(StateTreeNodeAsset root, StateTreeNodeAsset candidate)
        {
            if (root == null || candidate == null)
                return false;
            if (root == candidate)
                return true;

            for (var i = 0; i < root.children.Count; ++i)
            {
                if (IsSelfOrDescendantRecursive(root.children[i], candidate, 1))
                    return true;
            }

            return false;
        }

        private static bool IsSelfOrDescendantRecursive(StateTreeNodeAsset node,
            StateTreeNodeAsset candidate, int depth)
        {
            if (node == null || depth > DepthGuard)
                return false;
            if (node == candidate)
                return true;

            for (var i = 0; i < node.children.Count; ++i)
            {
                if (IsSelfOrDescendantRecursive(node.children[i], candidate, depth + 1))
                    return true;
            }

            return false;
        }

        /// <summary>Mirror of <c>StateTreeRunner.ResolveEntryNode</c> (frozen runtime, so this is
        /// a copy rather than a call): organizational nodes resolve to their first leaf. The
        /// window shows the result so "which state actually starts?" is never a guess.</summary>
        internal static StateTreeNodeAsset ResolveEntryNode(StateTreeNodeAsset node)
        {
            var current = node;
            var guard = 0;
            while (current != null && current.tasks.Count == 0 && current.transitions.Count == 0
                && current.children.Count > 0 && guard++ < DepthGuard)
                current = current.children[0];
            return current;
        }

        // --- ids --------------------------------------------------------------------------

        /// <summary>Node ids are the only wiring currency the runner has (transitions target a
        /// string), so uniqueness is enforced on entry rather than validated afterwards.</summary>
        internal static string MakeUniqueNodeId(StateTreeAsset tree, string desired,
            StateTreeNodeAsset ignore)
        {
            var wanted = string.IsNullOrWhiteSpace(desired) ? "state" : desired.Trim();
            var nodes = CollectNodes(tree);

            var candidate = wanted;
            var suffix = 1;
            while (IsTaken(nodes, candidate, ignore))
            {
                candidate = wanted + suffix;
                ++suffix;
            }

            return candidate;
        }

        private static bool IsTaken(List<StateTreeNodeAsset> nodes, string id, StateTreeNodeAsset ignore)
        {
            for (var i = 0; i < nodes.Count; ++i)
            {
                if (nodes[i] != ignore && nodes[i].nodeId == id)
                    return true;
            }

            return false;
        }

        /// <summary>Renaming a state rewires every transition that pointed at it. Doing this
        /// silently is the whole reason the window can be treated as a live view: the runner
        /// resolves by string, so a rename that skipped this step would break the tree the next
        /// time it is entered, with no visible cause.</summary>
        internal static void RetargetTransitions(StateTreeAsset tree, string oldId, string newId,
            string undoName)
        {
            if (tree == null || string.IsNullOrEmpty(oldId) || oldId == newId)
                return;

            var nodes = CollectNodes(tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                var node = nodes[i];
                var touched = false;
                for (var t = 0; t < node.transitions.Count; ++t)
                {
                    var transition = node.transitions[t];
                    if (transition == null || transition.targetNodeId != oldId)
                        continue;

                    if (!touched)
                    {
                        Undo.RecordObject(node, undoName);
                        touched = true;
                    }

                    transition.targetNodeId = newId;
                }

                if (touched)
                    EditorUtility.SetDirty(node);
            }
        }

        /// <summary>
        /// How many authored text fields in this tree hold <paramref name="name"/> — the question
        /// <see cref="RetargetBlackboardReads"/> is offered on the strength of, asked without
        /// changing anything.
        /// </summary>
        /// <param name="tree">The tree to scan.</param>
        /// <param name="name">The blackboard key to look for.</param>
        /// <returns>The number of matching fields.</returns>
        internal static int CountBlackboardReads(StateTreeAsset tree, string name)
            => ScanBlackboardReads(tree, name, null, false, null);

        /// <summary>
        /// Rewrite every authored text field in this tree that holds <paramref name="oldName"/> so it
        /// holds <paramref name="newName"/> — the assisted half of renaming a declared parameter.
        ///
        /// A parameter's NAME is the blackboard key, and the tasks and conditions that read it hold
        /// that key as plain authored text. So unlike <see cref="RetargetTransitions"/> — which fixes
        /// the tree silently, because a node id is an editor-side handle and a rename that skipped it
        /// would simply break the tree — this is OFFERED rather than done: the callers of a renamed
        /// parameter are bound by id and need nothing, while the readers inside the tree may or may
        /// not be meant to follow. The caller asks; this executes.
        ///
        /// MATCHING IS BY VALUE, NOT BY FIELD NAME. The key fields are called <c>key</c>,
        /// <c>cooldownKey</c>, <c>timerKey</c>, <c>payloadKey</c> … and a library component added
        /// tomorrow will invent another name, so a list of field names is a list that goes out of
        /// date silently. The cost is the other direction: a text field holding that exact string for
        /// an unrelated reason (a cue name, a compare operator) is rewritten too. That is why the
        /// count is reported before the fact and the whole thing is one undo step.
        ///
        /// Only <c>string</c> fields, and only on this tree's own task and condition sub-assets.
        /// A <c>List&lt;string&gt;</c> (a composite task's success/failure state ids) is therefore
        /// never touched, which is correct — those are node ids, not blackboard keys — and neither is
        /// a logic graph this tree runs, because the graph is a separate asset whose keys are baked
        /// from its own canvas.
        /// </summary>
        /// <param name="tree">The tree whose sub-assets are rewritten.</param>
        /// <param name="oldName">The key to replace.</param>
        /// <param name="newName">The key to replace it with.</param>
        /// <param name="undoName">Undo label recorded on each touched sub-asset.</param>
        /// <returns>The number of fields rewritten.</returns>
        internal static int RetargetBlackboardReads(StateTreeAsset tree, string oldName,
            string newName, string undoName)
            => ScanBlackboardReads(tree, oldName, newName, true, undoName);

        /// <summary>The one walk both entry points share, so "how many would change" and "change
        /// them" can never disagree about what counts as a match.</summary>
        private static int ScanBlackboardReads(StateTreeAsset tree, string oldName, string newName,
            bool apply, string undoName)
        {
            if (tree == null || string.IsNullOrEmpty(oldName))
                return 0;
            if (apply && (newName == null || string.Equals(newName, oldName, StringComparison.Ordinal)))
                return 0;

            var count = 0;
            var nodes = CollectNodes(tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                var node = nodes[i];

                for (var t = 0; t < node.tasks.Count; ++t)
                    count += RewriteStringFields(node.tasks[t], oldName, newName, apply, undoName);

                for (var t = 0; t < node.transitions.Count; ++t)
                {
                    count += RewriteStringFields(node.transitions[t]?.condition, oldName, newName,
                        apply, undoName);
                }
            }

            return count;
        }

        /// <summary>Count (and optionally rewrite) the serialized text fields of one sub-asset whose
        /// value is exactly <paramref name="oldValue"/>. The undo record is taken once per object and
        /// only when something actually changes, so a scan that matches nothing adds no undo entry
        /// and dirties nothing.</summary>
        private static int RewriteStringFields(UnityEngine.Object target, string oldValue,
            string newValue, bool apply, string undoName)
        {
            if (target == null)
                return 0;

            var fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            var count = 0;
            var touched = false;

            for (var i = 0; i < fields.Length; ++i)
            {
                var field = fields[i];
                if (field.FieldType != typeof(string) || field.IsNotSerialized)
                    continue;
                if (!string.Equals(field.GetValue(target) as string, oldValue, StringComparison.Ordinal))
                    continue;

                ++count;
                if (!apply)
                    continue;

                if (!touched)
                {
                    Undo.RecordObject(target, undoName);
                    touched = true;
                }

                field.SetValue(target, newValue);
            }

            if (touched)
                EditorUtility.SetDirty(target);

            return count;
        }

        // --- sub-asset lifecycle ----------------------------------------------------------

        /// <summary>Create a state under <paramref name="parent"/> (null = become the tree
        /// root). The order — CreateInstance, AddObjectToAsset, RegisterCreatedObjectUndo, then
        /// record-and-mutate the owner — is the one Unity's own ScriptableObject graph tools
        /// use; registering the creation undo before the object belongs to an asset loses the
        /// asset membership on undo.</summary>
        internal static StateTreeNodeAsset CreateNode(StateTreeAsset tree, StateTreeNodeAsset parent,
            string nodeId, string displayName, string undoName)
        {
            if (tree == null)
                return null;

            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = MakeUniqueNodeId(tree, nodeId, null);
            node.displayName = string.IsNullOrEmpty(displayName) ? node.nodeId : displayName;
            node.name = NodeAssetName(CollectNodes(tree).Count, node.nodeId);

            AssetDatabase.AddObjectToAsset(node, tree);
            Undo.RegisterCreatedObjectUndo(node, undoName);

            if (parent != null)
            {
                Undo.RecordObject(parent, undoName);
                parent.children.Add(node);
                EditorUtility.SetDirty(parent);
            }
            else
            {
                Undo.RecordObject(tree, undoName);
                tree.root = node;
            }

            EditorUtility.SetDirty(tree);
            return node;
        }

        internal static StateTreeTaskAsset CreateTask(StateTreeAsset tree, StateTreeNodeAsset node,
            Type type, string undoName)
        {
            if (tree == null || node == null || type == null)
                return null;

            var task = ScriptableObject.CreateInstance(type) as StateTreeTaskAsset;
            if (task == null)
                return null;

            task.name = TaskAssetName(node.nodeId, type);
            AssetDatabase.AddObjectToAsset(task, tree);
            Undo.RegisterCreatedObjectUndo(task, undoName);

            Undo.RecordObject(node, undoName);
            node.tasks.Add(task);
            EditorUtility.SetDirty(node);
            EditorUtility.SetDirty(tree);
            return task;
        }

        /// <summary>Add a composite task: a whole authored tree, run as one task of
        /// <paramref name="node"/> through <see cref="RunSubTreeTask"/>. This is what "save a
        /// tree, pick it as a task" resolves to — the picker hands over the asset, the sub-asset
        /// lifecycle is identical to <see cref="CreateTask"/>, and the only extra work is wiring
        /// <c>subTree</c> before the object joins the asset file so the created-object undo
        /// captures it already wired.
        ///
        /// The self-reference is refused here as well as in the UI and at runtime: a tree that
        /// runs itself is an infinite composition, and the cheapest place to say no is the one
        /// that would otherwise write it to disk.</summary>
        internal static StateTreeTaskAsset CreateSubTreeTask(StateTreeAsset tree,
            StateTreeNodeAsset node, StateTreeAsset subTree, string undoName)
        {
            if (tree == null || node == null || subTree == null || subTree == tree)
                return null;

            var task = ScriptableObject.CreateInstance<RunSubTreeTask>();
            if (task == null)
                return null;

            task.subTree = subTree;
            task.name = SubTreeTaskAssetName(node.nodeId, subTree);
            AssetDatabase.AddObjectToAsset(task, tree);
            Undo.RegisterCreatedObjectUndo(task, undoName);

            Undo.RecordObject(node, undoName);
            node.tasks.Add(task);
            EditorUtility.SetDirty(node);
            EditorUtility.SetDirty(tree);
            return task;
        }

        /// <summary>Add a logic-graph task: the program a <c>.taskgraph</c> file bakes, run as one
        /// task of <paramref name="node"/> through <see cref="RunGraphTask"/>. Same shape as
        /// <see cref="CreateSubTreeTask"/> and for the same reason — the state holds a thin
        /// wrapper that is a sub-asset of THIS tree, and the wrapper holds the reference — which is
        /// what keeps "delete this state" from destroying a library asset other trees share, and
        /// what makes editing the graph reach every state that uses it with nothing to re-sync.
        ///
        /// <paramref name="insertAt"/> below zero (or past the end) appends.</summary>
        internal static StateTreeTaskAsset CreateGraphTaskReference(StateTreeAsset tree,
            StateTreeNodeAsset node, GraphTaskAsset graph, int insertAt, string undoName)
        {
            if (tree == null || node == null || graph == null)
                return null;

            var task = ScriptableObject.CreateInstance<RunGraphTask>();
            if (task == null)
                return null;

            task.graph = graph;
            task.name = GraphTaskAssetName(node.nodeId, graph);
            AssetDatabase.AddObjectToAsset(task, tree);
            Undo.RegisterCreatedObjectUndo(task, undoName);

            Undo.RecordObject(node, undoName);
            if (insertAt >= 0 && insertAt <= node.tasks.Count)
                node.tasks.Insert(insertAt, task);
            else
                node.tasks.Add(task);

            EditorUtility.SetDirty(node);
            EditorUtility.SetDirty(tree);
            return task;
        }

        /// <summary>Point an existing composite task at a DIFFERENT tree, in place. The one caller
        /// that needs this is "convert to graph": the tree the task ran has just been re-authored
        /// as a graph file, and the task has to follow it or the conversion has changed nothing
        /// the runner will see.
        ///
        /// Re-pointing rather than remove-and-re-add is what keeps the task's OWN parameters — its
        /// success/failure state lists, which the author may well have edited away from the
        /// defaults. Deleting the sub-asset and creating a fresh one would silently reset them,
        /// and the conversion is meant to preserve behaviour exactly.
        ///
        /// Returns false for every assignment the model refuses (no task, no tree, a tree running
        /// itself, or a composition that closes a loop) so the caller reports instead of believing
        /// a mutation happened. <paramref name="tree"/> is the OWNING tree — the one whose asset
        /// the task is a sub-asset of — and is what the cycle check is run against.</summary>
        internal static bool RepointSubTreeTask(StateTreeAsset tree, RunSubTreeTask task,
            StateTreeAsset subTree, string undoName)
        {
            if (task == null || subTree == null)
                return false;
            if (tree != null && (subTree == tree || CreatesCycle(subTree, tree)))
                return false;
            if (task.subTree == subTree)
                return true;

            Undo.RecordObject(task, undoName);
            task.subTree = subTree;
            EditorUtility.SetDirty(task);

            if (tree != null)
                EditorUtility.SetDirty(tree);

            return true;
        }

        /// <summary>Swap the condition on one transition. Replacing (rather than adding) is the
        /// only sane semantic: a transition holds exactly one condition reference, and the old
        /// sub-asset would otherwise be orphaned inside the asset file forever.</summary>
        internal static StateTreeConditionAsset SetTransitionCondition(StateTreeAsset tree,
            StateTreeNodeAsset node, StateTreeTransition transition, Type type, string undoName)
        {
            if (tree == null || node == null || transition == null)
                return null;

            var previous = transition.condition;

            StateTreeConditionAsset created = null;
            if (type != null)
            {
                created = ScriptableObject.CreateInstance(type) as StateTreeConditionAsset;
                if (created == null)
                    return previous;

                created.name = ConditionAssetName(node.nodeId, transition.targetNodeId, type);
                AssetDatabase.AddObjectToAsset(created, tree);
                Undo.RegisterCreatedObjectUndo(created, undoName);
            }

            Undo.RecordObject(node, undoName);
            transition.condition = created;
            EditorUtility.SetDirty(node);

            // Clear the reference before destroying, never the other way round: a sub-asset
            // destroyed while still referenced leaves a "Missing" entry that survives undo.
            DestroySubAsset(previous);
            EditorUtility.SetDirty(tree);
            return created;
        }

        internal static void RemoveTask(StateTreeAsset tree, StateTreeNodeAsset node,
            StateTreeTaskAsset task, string undoName)
        {
            if (node == null)
                return;

            Undo.RecordObject(node, undoName);
            node.tasks.Remove(task);
            EditorUtility.SetDirty(node);

            DestroySubAsset(task);
            if (tree != null)
                EditorUtility.SetDirty(tree);
        }

        internal static void RemoveTransition(StateTreeAsset tree, StateTreeNodeAsset node,
            int index, string undoName)
        {
            if (node == null || index < 0 || index >= node.transitions.Count)
                return;

            var transition = node.transitions[index];
            Undo.RecordObject(node, undoName);
            node.transitions.RemoveAt(index);
            EditorUtility.SetDirty(node);

            DestroySubAsset(transition?.condition);
            if (tree != null)
                EditorUtility.SetDirty(tree);
        }

        internal static void AddTransition(StateTreeAsset tree, StateTreeNodeAsset node,
            string targetNodeId, string undoName)
        {
            if (node == null)
                return;

            Undo.RecordObject(node, undoName);
            node.transitions.Add(new StateTreeTransition
            {
                targetNodeId = targetNodeId ?? string.Empty,
                condition = null,
                checkWhileRunning = false
            });
            EditorUtility.SetDirty(node);
            if (tree != null)
                EditorUtility.SetDirty(tree);
        }

        /// <summary>Order is evaluation order in the runner, so moving a transition IS a
        /// behavioural edit — exposed as up/down rather than a drag to keep it deliberate.</summary>
        internal static bool MoveTransition(StateTreeAsset tree, StateTreeNodeAsset node, int index,
            int delta, string undoName)
        {
            if (node == null)
                return false;

            var target = index + delta;
            if (index < 0 || index >= node.transitions.Count || target < 0
                || target >= node.transitions.Count)
                return false;

            Undo.RecordObject(node, undoName);
            var moved = node.transitions[index];
            node.transitions[index] = node.transitions[target];
            node.transitions[target] = moved;
            EditorUtility.SetDirty(node);
            if (tree != null)
                EditorUtility.SetDirty(tree);
            return true;
        }

        /// <summary>Remove a state and everything it owns. Transitions elsewhere that pointed at
        /// it are deliberately left alone (the window flags them): silently rewriting another
        /// state's wiring on a delete is a worse surprise than a visible dangling target.</summary>
        internal static void RemoveNode(StateTreeAsset tree, StateTreeNodeAsset node, string undoName)
        {
            if (tree == null || node == null)
                return;

            var parent = FindParent(tree, node);
            if (parent != null)
            {
                Undo.RecordObject(parent, undoName);
                parent.children.Remove(node);
                EditorUtility.SetDirty(parent);
            }
            else if (tree.root == node)
            {
                Undo.RecordObject(tree, undoName);
                tree.root = null;
            }

            var doomed = new List<StateTreeNodeAsset>();
            CollectNodes(node, doomed, new HashSet<StateTreeNodeAsset>(), 0);
            for (var i = 0; i < doomed.Count; ++i)
            {
                var victim = doomed[i];
                for (var t = 0; t < victim.tasks.Count; ++t)
                    DestroySubAsset(victim.tasks[t]);
                for (var t = 0; t < victim.transitions.Count; ++t)
                    DestroySubAsset(victim.transitions[t]?.condition);
                DestroySubAsset(victim);
            }

            EditorUtility.SetDirty(tree);
        }

        /// <summary>Reparent/reorder a state. Returns false for every move the model cannot
        /// express (moving the root, dropping a node into its own subtree, dropping outside the
        /// single root) so the drag handler can answer Rejected instead of half-applying.</summary>
        internal static bool MoveNode(StateTreeAsset tree, StateTreeNodeAsset node,
            StateTreeNodeAsset newParent, int childIndex, string undoName)
        {
            if (tree == null || node == null || newParent == null || node == tree.root)
                return false;
            if (IsSelfOrDescendant(node, newParent))
                return false;

            var oldParent = FindParent(tree, node);
            if (oldParent == null)
                return false;

            Undo.RecordObject(oldParent, undoName);
            var oldIndex = oldParent.children.IndexOf(node);
            if (oldIndex < 0)
                return false;
            oldParent.children.RemoveAt(oldIndex);
            EditorUtility.SetDirty(oldParent);

            // Same-parent moves shift the insertion point once the node is out of the list.
            var insertAt = childIndex < 0 ? newParent.children.Count : childIndex;
            if (oldParent == newParent && childIndex > oldIndex)
                --insertAt;

            Undo.RecordObject(newParent, undoName);
            insertAt = Mathf.Clamp(insertAt, 0, newParent.children.Count);
            newParent.children.Insert(insertAt, node);
            EditorUtility.SetDirty(newParent);
            EditorUtility.SetDirty(tree);
            return true;
        }

        /// <summary>Move a state among its siblings — the keyboard-friendly half of reorder, and
        /// the only reorder available for the entry state (children[0] decides where the runner
        /// starts, so it must be reachable without a mouse drag).</summary>
        internal static bool MoveNodeSibling(StateTreeAsset tree, StateTreeNodeAsset node, int delta,
            string undoName)
        {
            var parent = FindParent(tree, node);
            if (parent == null)
                return false;

            var index = parent.children.IndexOf(node);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= parent.children.Count)
                return false;

            Undo.RecordObject(parent, undoName);
            parent.children[index] = parent.children[target];
            parent.children[target] = node;
            EditorUtility.SetDirty(parent);
            EditorUtility.SetDirty(tree);
            return true;
        }

        internal static void DestroySubAsset(UnityEngine.Object target)
        {
            if (target == null)
                return;

            // Undo.DestroyObjectImmediate removes the sub-asset from its container AND keeps it
            // restorable; AssetDatabase.RemoveObjectFromAsset would drop it outside undo.
            Undo.DestroyObjectImmediate(target);
        }

        // --- authored trees as tasks ------------------------------------------------------

        /// <summary>The name an authored tree goes by everywhere in the editor: its
        /// <c>treeName</c>, or the asset file name when that was never filled in. One definition
        /// so the picker row, the task sub-asset name and the inspector label cannot drift.</summary>
        internal static string TreeDisplayName(StateTreeAsset tree)
        {
            if (tree == null)
                return "(none)";

            return !string.IsNullOrWhiteSpace(tree.treeName) ? tree.treeName.Trim() : tree.name;
        }

        internal static bool IsTaskTree(StateTreeAsset tree) => tree != null && IsTaskKind(tree.treeKind);

        internal static bool IsTaskKind(string kind)
        {
            return !string.IsNullOrWhiteSpace(kind)
                && string.Equals(kind.Trim(), TaskTreeKind, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Would running <paramref name="candidate"/> inside <paramref name="host"/>
        /// close a loop? True for the obvious case (a tree running itself) and for the indirect
        /// one (the candidate already runs the host, at any depth). The runtime has its own depth
        /// abort, but a composition that can only be discovered by watching an error log at play
        /// time is a composition the tool failed to prevent — so the picker filters on this and
        /// the inspector reports it.
        ///
        /// This is a STATIC read of the authored graph. A tree reached only through data set at
        /// runtime is outside what any editor check can see; that case is what the runtime guard
        /// is for.</summary>
        internal static bool CreatesCycle(StateTreeAsset candidate, StateTreeAsset host)
        {
            if (candidate == null || host == null)
                return false;
            if (candidate == host)
                return true;

            return ReferencesTree(candidate, host, new HashSet<StateTreeAsset>(), 0);
        }

        private static bool ReferencesTree(StateTreeAsset tree, StateTreeAsset target,
            HashSet<StateTreeAsset> visited, int depth)
        {
            if (tree == null || depth > DepthGuard || !visited.Add(tree))
                return false;

            var nodes = CollectNodes(tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                var tasks = nodes[i].tasks;
                for (var t = 0; t < tasks.Count; ++t)
                {
                    if (!(tasks[t] is RunSubTreeTask sub) || sub.subTree == null)
                        continue;
                    if (sub.subTree == target)
                        return true;
                    if (ReferencesTree(sub.subTree, target, visited, depth + 1))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Rename the tree. Callers refresh the OWNING tree's sub-asset names afterwards;
        /// composite tasks in OTHER trees carry this name too and go stale until those trees are
        /// next edited. That is deliberate — silently rewriting assets the author has not opened
        /// (and may not have checked out) to keep a cosmetic name current is the worse trade.
        /// </summary>
        internal static void SetTreeName(StateTreeAsset tree, string value, string undoName)
        {
            if (tree == null || tree.treeName == value)
                return;

            Undo.RecordObject(tree, undoName);
            tree.treeName = value ?? string.Empty;
            EditorUtility.SetDirty(tree);
        }

        /// <summary>Set the tree kind. <see cref="TaskTreeKind"/> is the value that makes the tree
        /// appear in other trees' task pickers; every other value is opaque to the editor.
        /// </summary>
        internal static void SetTreeKind(StateTreeAsset tree, string value, string undoName)
        {
            if (tree == null || tree.treeKind == value)
                return;

            Undo.RecordObject(tree, undoName);
            tree.treeKind = value ?? string.Empty;
            EditorUtility.SetDirty(tree);
        }

        // --- sub-asset naming -------------------------------------------------------------

        internal static string NodeAssetName(int order, string nodeId) => $"Node {order} {nodeId}";

        internal static string TaskAssetName(string nodeId, Type type) => $"Task {nodeId} {type.Name}";

        /// <summary>Composite tasks are named after what they RUN, not after their class: fifty
        /// "Task x RunSubTreeTask" rows in the Project window would be indistinguishable, which
        /// is the one thing the naming convention exists to prevent.</summary>
        internal static string SubTreeTaskAssetName(string nodeId, StateTreeAsset subTree)
            => $"Task {nodeId} SubTree:{TreeDisplayName(subTree)}";

        /// <summary>The same rule for the other authored kind: a wrapper is named after the graph
        /// it runs, because "Task x RunGraphTask" on five rows names nothing.</summary>
        internal static string GraphTaskAssetName(string nodeId, GraphTaskAsset graph)
            => $"Task {nodeId} Graph:{GraphTaskDisplayName(graph)}";

        /// <summary>The name a baked program goes by in the editor: the FILE name of the
        /// <c>.taskgraph</c> it is the main asset of — what the Project window shows and the only
        /// identity the author and the tool are guaranteed to agree on — falling back to the object
        /// name for a program that is not on disk.</summary>
        internal static string GraphTaskDisplayName(GraphTaskAsset graph)
        {
            if (graph == null)
                return "(none)";

            var path = AssetDatabase.GetAssetPath(graph);
            var file = string.IsNullOrEmpty(path)
                ? null
                : System.IO.Path.GetFileNameWithoutExtension(path);

            return !string.IsNullOrEmpty(file) ? file : graph.name;
        }

        private static string TaskAssetNameFor(string nodeId, StateTreeTaskAsset task)
        {
            if (task is RunSubTreeTask sub)
                return SubTreeTaskAssetName(nodeId, sub.subTree);

            return task is RunGraphTask program
                ? GraphTaskAssetName(nodeId, program.graph)
                : TaskAssetName(nodeId, task.GetType());
        }

        internal static string ConditionAssetName(string fromId, string toId, Type type)
            => $"Cond {fromId}->{toId} {type.Name}";

        /// <summary>Recompute every sub-asset name from the graph. Called after any structural
        /// edit because ids and targets move; deterministic, so running it twice is a no-op and
        /// it never adds an undo entry it does not need.</summary>
        internal static void RefreshSubAssetNames(StateTreeAsset tree)
        {
            if (tree == null)
                return;

            var nodes = CollectNodes(tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                var node = nodes[i];
                Rename(node, NodeAssetName(i, node.nodeId));

                for (var t = 0; t < node.tasks.Count; ++t)
                {
                    var task = node.tasks[t];
                    if (task != null)
                        Rename(task, TaskAssetNameFor(node.nodeId, task));
                }

                for (var t = 0; t < node.transitions.Count; ++t)
                {
                    var condition = node.transitions[t]?.condition;
                    if (condition != null)
                    {
                        Rename(condition, ConditionAssetName(node.nodeId,
                            node.transitions[t].targetNodeId, condition.GetType()));
                    }
                }
            }
        }

        private static void Rename(UnityEngine.Object target, string desired)
        {
            if (target == null || target.name == desired)
                return;

            Undo.RecordObject(target, "Rename State Tree Sub-Asset");
            target.name = desired;
            EditorUtility.SetDirty(target);
        }
    }
}
