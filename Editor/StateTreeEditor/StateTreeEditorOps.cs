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
    /// AND THIS FILE OWNS THE INDEX REMAP. A state's parameter links
    /// (<see cref="StateTreeNodeAsset.bindings"/>, M7i) address their target by POSITION in
    /// <c>node.tasks</c> / <c>node.transitions</c>, so every mutation here that moves a position
    /// has to move the rows with it — otherwise deleting task 1 leaves task 2's link writing into
    /// task 1's field, which is a corruption no error message can describe because every row
    /// involved is individually valid. The four cases are enumerated where they happen:
    /// <see cref="RemoveTask"/> and <see cref="RemoveTransition"/> drop and renumber
    /// (<see cref="DropBindingsAt"/>), <see cref="MoveTransition"/> swaps, and
    /// <see cref="CreateGraphTaskReference"/> shifts when it inserts rather than appends. The
    /// APPENDS — <see cref="CreateTask"/>, <see cref="CreateSubTreeTask"/>,
    /// <see cref="AddTransition"/> — move nothing and are deliberately left alone;
    /// <see cref="SetTransitionCondition"/> moves nothing either but replaces the OBJECT at an
    /// index, which is its own case (<see cref="PruneBindings"/>). <see cref="RemoveNode"/>
    /// destroys the node that carries the rows, so there is nothing left to remap.
    ///
    /// THE OUTPUT ROUTES (M7j) ARE THE SAME PROBLEM WITH A DIFFERENT ADDRESS, and the difference is
    /// what makes the list of sites SHORTER rather than longer. A route
    /// (<c>StateTreeTransition.outputRoutes</c>) lives ON a transition and names a TASK by index, so
    /// only the TASK list can move underneath it — the two sites are exactly the two task mutations
    /// that are not appends: <see cref="RemoveTask"/> (drop and renumber,
    /// <see cref="DropRoutesAt"/>) and <see cref="CreateGraphTaskReference"/> with an insertion
    /// point (<see cref="ShiftRoutesFrom"/>). Every TRANSITION mutation is deliberately left alone
    /// and each says so where it happens: <see cref="RemoveTransition"/> destroys the rows with
    /// their transition, <see cref="MoveTransition"/> carries them along in the object it swaps, and
    /// <see cref="SetTransitionCondition"/> replaces a condition, which no route addresses. The
    /// routes are walked across ALL of the node's transitions in both helpers, because a task index
    /// is a fact about the state, not about the transition the row happens to sit on.
    ///
    /// AND IT OWNS THE ASSISTED RENAMES, which are the same problem again with the index replaced by
    /// a NAME. Three wires in this toolset are joined by matching text rather than by identity — a
    /// transition's <c>targetNodeId</c> (<see cref="RetargetTransitions"/>), a parameter's name used
    /// as a blackboard key by the tasks that read it (<see cref="RetargetBlackboardReads"/>), and the
    /// key an output route writes for a field binding to read at entry
    /// (<see cref="CountEntryBindings"/>) — so renaming either end of one of them disconnects it
    /// with nothing broken enough to report. The node id rename is fixed SILENTLY, because a node id
    /// is an editor-side handle and a rename that skipped it would simply break the tree. The
    /// route-chain key cannot be renamed AT ALL while both ends exist — the inspector LOCKS the
    /// inline key on any complete wire (M7m, the count above is the predicate), because renaming
    /// half of a path is never what the author means and detaching has its own gestures. Only the
    /// parameter-name reads are counted and OFFERED, because a task's blackboard read may
    /// legitimately be fed by something other than the parameter that shares its name.
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

        // --- parameter bindings -----------------------------------------------------------

        /// <summary>
        /// Bind one serialized field of one of this state's tasks (or of one transition's condition)
        /// to a parameter the tree declares — the M7i link, written to
        /// <see cref="StateTreeNodeAsset.bindings"/> and applied by the executor when the tree
        /// starts.
        ///
        /// A row addresses its target BY INDEX into <c>node.tasks</c> / <c>node.transitions</c>,
        /// which is what makes the rest of this section necessary: every mutation in this file that
        /// moves those indices has to move the rows with them, or a link authored against task 2
        /// silently starts writing to task 1. See <see cref="DropBindingsAt"/> and its callers.
        ///
        /// ONE ROW PER SLOT, enforced by rewriting rather than editing in place: the executor walks
        /// the list writing as it goes, so a second row for the same field would silently win, and a
        /// list that already carried duplicates (hand-edited YAML, a merge) comes out of this with
        /// one. The binding is by parameter ID only — the M7h rule — so renaming the parameter it
        /// points at keeps the link.
        ///
        /// A PARAMETER IS ONE OF TWO SOURCES a field can have (M7k), and this writes the other one's
        /// slot empty rather than leaving it: a row that still carried a blackboard key would read as
        /// a key binding in a YAML diff, and the day some future reader looks at the wrong field
        /// first is the day the link silently changes meaning. <see cref="SetFieldBindingKey"/> is
        /// the mirror of this and does the same in reverse.
        /// </summary>
        /// <param name="node">The state that owns the link rows.</param>
        /// <param name="kind">Whether the target is a task or a transition's condition.</param>
        /// <param name="targetIndex">Index into that list.</param>
        /// <param name="fieldName">Public serialized instance field on the target.</param>
        /// <param name="parameterId">Declared parameter's <see cref="GraphTaskParameter.id"/>.</param>
        /// <param name="undoName">Undo label recorded on the node asset.</param>
        /// <returns>False when the arguments describe no row at all; the caller reports rather
        /// than believing a mutation happened.</returns>
        internal static bool SetFieldBinding(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int targetIndex, string fieldName,
            string parameterId, string undoName)
        {
            if (node == null || targetIndex < 0 || string.IsNullOrEmpty(fieldName)
                || string.IsNullOrEmpty(parameterId))
                return false;

            WriteFieldBinding(node, kind, targetIndex, fieldName,
                StateTreeFieldBinding.SourceKind.Parameter, parameterId, string.Empty, undoName);
            return true;
        }

        /// <summary>
        /// Bind the same kind of field to a BLACKBOARD KEY instead of to a declared parameter — the
        /// M7k source, and the half that closes the loop a transition's output routes opened: a route
        /// writes a finished task's return under a key as the tree leaves a state, and this is how the
        /// state it arrives at gets that value into a plain <c>public float</c> nobody can otherwise
        /// reach.
        ///
        /// The two sources differ in WHEN, which is why they are not one field with a nullable id: a
        /// parameter is written once at tree start (its value is fixed for the call), a key is read at
        /// every ENTRY of the state, so re-entering after a different transition picks up a different
        /// value. That is also why an empty key is accepted here where an empty parameter id is
        /// refused: the author picks "blackboard key" first and types the key second, and a helper that
        /// rejected the intermediate state would make the row impossible to create from a menu. An
        /// empty key is an inert row and the inspector says so.
        /// </summary>
        /// <param name="blackboardKey">Key to read at entry. Null is stored as empty — the row exists,
        /// reads nothing, and is reported rather than silently dropped.</param>
        /// <returns>False when the arguments describe no row at all, same contract as
        /// <see cref="SetFieldBinding"/>.</returns>
        internal static bool SetFieldBindingKey(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int targetIndex, string fieldName,
            string blackboardKey, string undoName)
        {
            if (node == null || targetIndex < 0 || string.IsNullOrEmpty(fieldName))
                return false;

            WriteFieldBinding(node, kind, targetIndex, fieldName,
                StateTreeFieldBinding.SourceKind.BlackboardKey, string.Empty,
                blackboardKey ?? string.Empty, undoName);
            return true;
        }

        /// <summary>The one writer both link helpers go through, so "one row per slot" and "the
        /// unused source slot is cleared" are single facts rather than two copies that drift. The
        /// caller has already refused the arguments that describe no row.</summary>
        private static void WriteFieldBinding(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int targetIndex, string fieldName,
            StateTreeFieldBinding.SourceKind sourceKind, string parameterId, string blackboardKey,
            string undoName)
        {
            Undo.RecordObject(node, undoName);

            if (node.bindings == null)
                node.bindings = new List<StateTreeFieldBinding>();

            RemoveBindingRows(node, kind, targetIndex, fieldName);
            node.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = kind,
                targetIndex = targetIndex,
                fieldName = fieldName,
                sourceKind = sourceKind,
                parameterId = parameterId,
                blackboardKey = blackboardKey
            });

            EditorUtility.SetDirty(node);
        }

        /// <summary>Drop the link on one field, so the field's own authored value is what runs
        /// again. Every row for that slot goes, for the reason <see cref="SetFieldBinding"/>
        /// keeps only one: "this field is not linked" has to become true.</summary>
        /// <returns>False when there was nothing to remove — no undo entry is added in that
        /// case.</returns>
        internal static bool RemoveFieldBinding(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int targetIndex, string fieldName,
            string undoName)
        {
            if (node == null || node.bindings == null || string.IsNullOrEmpty(fieldName))
                return false;
            if (FindFieldBinding(node, kind, targetIndex, fieldName) == null)
                return false;

            Undo.RecordObject(node, undoName);
            RemoveBindingRows(node, kind, targetIndex, fieldName);
            EditorUtility.SetDirty(node);
            return true;
        }

        /// <summary>The row in force for one field: the LAST match, because that is the one the
        /// executor's walk leaves in the field. Duplicates are only reachable through hand-edited
        /// YAML, and that is exactly when the inspector must not show a different link from the one
        /// that runs.</summary>
        internal static StateTreeFieldBinding FindFieldBinding(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int targetIndex, string fieldName)
        {
            var rows = node != null ? node.bindings : null;
            if (rows == null || string.IsNullOrEmpty(fieldName))
                return null;

            StateTreeFieldBinding found = null;
            for (var i = 0; i < rows.Count; ++i)
            {
                if (IsBindingFor(rows[i], kind, targetIndex, fieldName))
                    found = rows[i];
            }

            return found;
        }

        /// <summary>The object a link row writes into: the task at that index, or the condition of
        /// the transition at that index. Null for every row the executor would skip.</summary>
        internal static UnityEngine.Object ResolveBindingTarget(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int targetIndex)
        {
            if (node == null || targetIndex < 0)
                return null;

            if (kind == StateTreeFieldBinding.TargetKind.Task)
                return targetIndex < node.tasks.Count ? node.tasks[targetIndex] : null;

            return targetIndex < node.transitions.Count
                ? node.transitions[targetIndex]?.condition
                : null;
        }

        /// <summary>
        /// Whether a named field of a task or condition can be linked at all, and to which
        /// parameter kind. This is the ONE definition the editor offers links from, deliberately
        /// reflection-based rather than driven by the SerializedProperty type: the executor finds
        /// its target with <c>GetField(name, Public | Instance)</c>, so a <c>[SerializeField]</c>
        /// private field is visible in the inspector and unreachable at run time, and offering a
        /// link on one would be offering something that cannot work.
        ///
        /// An <c>int</c> field takes a number parameter alongside <c>float</c>, matching the
        /// executor's kind-compatible write (Float → float|int, Bool → bool, String → string).
        /// </summary>
        internal static bool TryGetBindableKind(UnityEngine.Object target, string fieldName,
            out GraphTaskParameterKind kind)
        {
            kind = GraphTaskParameterKind.Float;
            if (target == null || string.IsNullOrEmpty(fieldName))
                return false;

            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.IsNotSerialized || field.IsInitOnly)
                return false;
            // An output is not an input: binding a parameter INTO a return field would be
            // overwritten by the task and read as a working link that does nothing.
            if (field.IsDefined(typeof(TaskOutputAttribute), true))
                return false;

            return TryGetBindableKind(field.FieldType, out kind);
        }

        /// <summary>True when the named public field on <paramref name="target"/> carries
        /// [TaskOutput] — the pane hides such fields from the editable list (they are return
        /// values, surfaced through the transition Route outputs dropdowns instead).</summary>
        internal static bool IsTaskOutputField(UnityEngine.Object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return false;
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            return field != null && field.IsDefined(typeof(TaskOutputAttribute), true);
        }

        internal static bool TryGetBindableKind(Type fieldType, out GraphTaskParameterKind kind)
        {
            if (fieldType == typeof(float) || fieldType == typeof(int))
            {
                kind = GraphTaskParameterKind.Float;
                return true;
            }

            if (fieldType == typeof(bool))
            {
                kind = GraphTaskParameterKind.Bool;
                return true;
            }

            if (fieldType == typeof(string))
            {
                kind = GraphTaskParameterKind.String;
                return true;
            }

            kind = GraphTaskParameterKind.Float;
            return false;
        }

        // --- key contracts (M12, wire-in-the-field since M14) -----------------------------

        /// <summary>Whether a named field is KEY-SEMANTIC — a <see cref="StateTreeKeyField"/>
        /// (M14: the wire lives in the field) — and which key kind its
        /// <see cref="StateTreeKeyAttribute"/> declares. A wrapper without the attribute is
        /// still key-semantic; it just filters as any-kind.</summary>
        internal static bool TryGetKeyField(UnityEngine.Object target, string fieldName,
            out StateTreeKeyKind kind, out bool anyKind)
        {
            kind = StateTreeKeyKind.Float;
            anyKind = true;
            if (target == null || string.IsNullOrEmpty(fieldName))
                return false;

            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.FieldType != typeof(StateTreeKeyField))
                return false;

            var marks = field.GetCustomAttributes(typeof(StateTreeKeyAttribute), true);
            if (marks.Length > 0)
            {
                var mark = (StateTreeKeyAttribute)marks[0];
                kind = mark.kind;
                anyKind = mark.any;
            }

            return true;
        }

        /// <summary>The live wrapper behind a key field — what the inspector reads and every
        /// wire operation writes.</summary>
        internal static StateTreeKeyField GetKeyField(UnityEngine.Object target,
            string fieldName)
        {
            var field = target != null
                ? target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)
                : null;
            return field != null && field.FieldType == typeof(StateTreeKeyField)
                ? field.GetValue(target) as StateTreeKeyField
                : null;
        }

        /// <summary>Wire a key field to a declaration: the id is the wire, and the field's
        /// TEXT is set to the declaration's current name in the same act — the serialized
        /// fallback must agree with the contract on the day the wire is made.</summary>
        internal static void WireKeyField(UnityEngine.Object target, string fieldName,
            StateTreeKeyDeclaration declaration, string undoName)
        {
            if (declaration != null)
                WireKeyField(target, fieldName, declaration.id, declaration.name, undoName);
        }

        /// <summary>The raw wire — id and name from either kind of declaration (a key, or a
        /// tree PARAMETER: both seed the blackboard by name and rename by id).</summary>
        internal static void WireKeyField(UnityEngine.Object target, string fieldName,
            string wireId, string wireName, string undoName)
        {
            StateTreeKeyField key = GetKeyField(target, fieldName);
            if (key == null || string.IsNullOrEmpty(wireId))
                return;

            Undo.RecordObject(target, undoName);
            key.keyId = wireId;
            key.text = wireName ?? string.Empty;
            EditorUtility.SetDirty(target);
        }

        /// <summary>Unwire a key field. The text STAYS — that is what unwiring means: the
        /// typed name is free again and runs as-is.</summary>
        internal static void UnwireKeyField(UnityEngine.Object target, string fieldName,
            string undoName)
        {
            StateTreeKeyField key = GetKeyField(target, fieldName);
            if (key == null)
                return;

            Undo.RecordObject(target, undoName);
            key.keyId = string.Empty;
            EditorUtility.SetDirty(target);
        }

        /// <summary>Set a key field's authored text (its fallback / free-typed name).</summary>
        internal static void SetKeyFieldText(UnityEngine.Object target, string fieldName,
            string text, string undoName)
        {
            StateTreeKeyField key = GetKeyField(target, fieldName);
            if (key == null)
                return;

            Undo.RecordObject(target, undoName);
            key.text = text ?? string.Empty;
            EditorUtility.SetDirty(target);
        }

        /// <summary>Rewrite every key field this tree wires to one declaration to the
        /// declaration's new name — the rename following the id, silently, because this
        /// window holds both ends of every in-tree wire. Wires in OTHER trees that import
        /// this one live in assets this window must not touch; their stale text is harmless
        /// — the id wins at StartTree.</summary>
        /// <returns>How many fields were rewritten.</returns>
        internal static int RewriteLinkedKeyFields(StateTreeAsset tree, string keyId,
            string newName, string undoName)
        {
            if (tree == null || string.IsNullOrEmpty(keyId))
                return 0;

            var rewritten = 0;
            var nodes = CollectNodes(tree);
            for (var n = 0; n < nodes.Count; ++n)
            {
                var node = nodes[n];
                if (node == null)
                    continue;
                for (var i = 0; i < node.tasks.Count; ++i)
                    rewritten += RewriteKeyFieldsOn(node.tasks[i], keyId, newName, undoName);
                for (var i = 0; i < node.transitions.Count; ++i)
                {
                    if (node.transitions[i] != null)
                    {
                        rewritten += RewriteKeyFieldsOn(node.transitions[i].condition, keyId,
                            newName, undoName);
                    }
                }
            }

            return rewritten;
        }

        private static int RewriteKeyFieldsOn(UnityEngine.Object target, string keyId,
            string newName, string undoName)
        {
            if (target == null)
                return 0;

            var rewritten = 0;
            var fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (var i = 0; i < fields.Length; ++i)
            {
                if (fields[i].FieldType != typeof(StateTreeKeyField))
                    continue;
                if (!(fields[i].GetValue(target) is StateTreeKeyField key)
                    || !string.Equals(key.keyId, keyId, StringComparison.Ordinal))
                    continue;

                Undo.RecordObject(target, undoName);
                key.text = newName ?? string.Empty;
                EditorUtility.SetDirty(target);
                ++rewritten;
            }

            return rewritten;
        }

        // --- data registries (M13) --------------------------------------------------------

        /// <summary>Whether a named field is a TYPED ENTRY REFERENCE, and which entry class
        /// it wants. Asked of the live instance (the interface knows its generic argument),
        /// with the same executor-visibility rule as every other link surface.</summary>
        internal static bool TryGetEntryRefField(UnityEngine.Object target, string fieldName,
            out IStateTreeEntryRef reference)
        {
            reference = null;
            if (target == null || string.IsNullOrEmpty(fieldName))
                return false;

            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !typeof(IStateTreeEntryRef).IsAssignableFrom(field.FieldType))
                return false;

            reference = field.GetValue(target) as IStateTreeEntryRef;
            return reference != null;
        }

        /// <summary>The whole-registry flavor — nothing to edit on it, but the row it draws
        /// says which of the tree's registries will answer.</summary>
        internal static bool TryGetRegistryRefField(UnityEngine.Object target, string fieldName,
            out IStateTreeRegistryRef reference)
        {
            reference = null;
            if (target == null || string.IsNullOrEmpty(fieldName))
                return false;

            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !typeof(IStateTreeRegistryRef).IsAssignableFrom(field.FieldType))
                return false;

            reference = field.GetValue(target) as IStateTreeRegistryRef;
            return reference != null;
        }

        /// <summary>The capability flavor (M15) — like the registry ref, nothing to edit; the
        /// row it draws states what the spine must provide at run time.</summary>
        internal static bool TryGetServiceRefField(UnityEngine.Object target, string fieldName,
            out IStateTreeServiceRef reference)
        {
            reference = null;
            if (target == null || string.IsNullOrEmpty(fieldName))
                return false;

            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !typeof(IStateTreeServiceRef).IsAssignableFrom(field.FieldType))
                return false;

            reference = field.GetValue(target) as IStateTreeServiceRef;
            return reference != null;
        }

        /// <summary>Aim a typed entry reference — id is the wire, the name rides along as the
        /// serialized display cache. Recording the TARGET records the nested serializable.</summary>
        internal static void SetEntryRef(UnityEngine.Object target, string fieldName,
            StateTreeRegistryEntry entry, string undoName)
        {
            if (entry == null || !TryGetEntryRefField(target, fieldName, out _))
                return;

            Undo.RecordObject(target, undoName);
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            object reference = field.GetValue(target);
            WriteRefString(reference, "entryId", entry.id);
            WriteRefString(reference, "entryName", entry.name);
            EditorUtility.SetDirty(target);
        }

        internal static void ClearEntryRef(UnityEngine.Object target, string fieldName,
            string undoName)
        {
            if (!TryGetEntryRefField(target, fieldName, out _))
                return;

            Undo.RecordObject(target, undoName);
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            object reference = field.GetValue(target);
            WriteRefString(reference, "entryId", string.Empty);
            WriteRefString(reference, "entryName", string.Empty);
            EditorUtility.SetDirty(target);
        }

        /// <summary>The entry an id resolves to across a TREE's registries, honoring the
        /// same first-assignable-registry order the executor walks.</summary>
        internal static StateTreeRegistryEntry FindTreeEntry(StateTreeAsset tree,
            Type entryType, string entryId)
        {
            if (tree == null || string.IsNullOrEmpty(entryId))
                return null;

            var registries = tree.registries;
            for (var i = 0; registries != null && i < registries.Count; ++i)
            {
                var registry = registries[i];
                if (registry == null || !entryType.IsAssignableFrom(registry.entryType))
                    continue;
                var entry = registry.FindById(entryId);
                if (entry != null)
                    return entry;
            }

            return null;
        }

        /// <summary>The registry a whole-registry reference will get — first assignable in
        /// tree order, the executor's rule.</summary>
        internal static StateTreeRegistryAsset FindTreeRegistry(StateTreeAsset tree,
            Type entryType)
        {
            var registries = tree != null ? tree.registries : null;
            for (var i = 0; registries != null && i < registries.Count; ++i)
            {
                if (registries[i] != null
                    && entryType.IsAssignableFrom(registries[i].entryType))
                    return registries[i];
            }

            return null;
        }

        private static void WriteRefString(object reference, string fieldName, string value)
        {
            var field = reference != null
                ? reference.GetType().GetField(fieldName,
                    BindingFlags.Public | BindingFlags.Instance)
                : null;
            field?.SetValue(reference, value);
        }

        /// <summary>The declaration one id names, or null for a link bound to nothing. Mirrors
        /// <see cref="GraphTaskParameterOverride.Matches"/> exactly — non-empty id on both sides,
        /// and a declaration with no NAME is unmatchable (it is an inspector row mid-edit, and the
        /// name is the blackboard key the seeded value lands under).</summary>
        internal static GraphTaskParameter FindParameterById(List<GraphTaskParameter> declared,
            string id)
        {
            if (declared == null || string.IsNullOrEmpty(id))
                return null;

            for (var i = 0; i < declared.Count; ++i)
            {
                var entry = declared[i];
                if (entry != null && !string.IsNullOrEmpty(entry.name)
                    && string.Equals(entry.id, id, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        private static bool IsBindingFor(StateTreeFieldBinding row,
            StateTreeFieldBinding.TargetKind kind, int targetIndex, string fieldName)
        {
            return row != null && row.targetKind == kind && row.targetIndex == targetIndex
                && string.Equals(row.fieldName, fieldName, StringComparison.Ordinal);
        }

        private static void RemoveBindingRows(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int targetIndex, string fieldName)
        {
            var rows = node.bindings;
            if (rows == null)
                return;

            for (var i = rows.Count - 1; i >= 0; --i)
            {
                if (IsBindingFor(rows[i], kind, targetIndex, fieldName))
                    rows.RemoveAt(i);
            }
        }

        /// <summary>
        /// The index remap for a REMOVAL: rows that pointed at the removed slot die with it, and
        /// every row past it follows its target down one.
        ///
        /// The three remap helpers take no undo label and record nothing on purpose — each is
        /// called from inside a mutation that has already done <c>Undo.RecordObject(node, …)</c>,
        /// and recording again AFTER the list changed would capture the mutated state as the state
        /// to undo to.
        /// </summary>
        private static void DropBindingsAt(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int index)
        {
            var rows = node != null ? node.bindings : null;
            if (rows == null || index < 0)
                return;

            for (var i = rows.Count - 1; i >= 0; --i)
            {
                var row = rows[i];
                if (row == null || row.targetKind != kind)
                    continue;

                if (row.targetIndex == index)
                    rows.RemoveAt(i);
                else if (row.targetIndex > index)
                    --row.targetIndex;
            }
        }

        /// <summary>The index remap for an INSERTION: everything at or past the insertion point
        /// addresses one slot later than it did.</summary>
        private static void ShiftBindingsFrom(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int index)
        {
            var rows = node != null ? node.bindings : null;
            if (rows == null || index < 0)
                return;

            for (var i = 0; i < rows.Count; ++i)
            {
                var row = rows[i];
                if (row != null && row.targetKind == kind && row.targetIndex >= index)
                    ++row.targetIndex;
            }
        }

        /// <summary>The index remap for a REORDER. <c>else if</c> rather than two ifs: a row moved
        /// from a to b must not then be moved back by the second test.</summary>
        private static void SwapBindings(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int a, int b)
        {
            var rows = node != null ? node.bindings : null;
            if (rows == null || a == b)
                return;

            for (var i = 0; i < rows.Count; ++i)
            {
                var row = rows[i];
                if (row == null || row.targetKind != kind)
                    continue;

                if (row.targetIndex == a)
                    row.targetIndex = b;
                else if (row.targetIndex == b)
                    row.targetIndex = a;
            }
        }

        /// <summary>Drop the rows of one slot that the object now sitting in it cannot satisfy —
        /// the SWAP case, where the index is unchanged but the thing at it is a different class
        /// with different fields. A null <paramref name="target"/> (the condition was cleared)
        /// takes every row for the slot, which is the same answer arrived at one field at a
        /// time.</summary>
        private static void PruneBindings(StateTreeNodeAsset node,
            StateTreeFieldBinding.TargetKind kind, int index, UnityEngine.Object target)
        {
            var rows = node != null ? node.bindings : null;
            if (rows == null || index < 0)
                return;

            for (var i = rows.Count - 1; i >= 0; --i)
            {
                var row = rows[i];
                if (row == null || row.targetKind != kind || row.targetIndex != index)
                    continue;
                if (!TryGetBindableKind(target, row.fieldName, out _))
                    rows.RemoveAt(i);
            }
        }

        // --- transition output routes -----------------------------------------------------

        /// <summary>
        /// Add a route to one transition: "when you fire, take output X of task N and write it to
        /// the blackboard" — the M7j return flow, and the other half of the M7i input bindings. A
        /// binding writes a parameter INTO a task before the tree runs; a route reads a value OUT of
        /// a task that has finished, at the moment the state is left.
        ///
        /// Rows are ADDED, never uniqued, unlike <see cref="SetFieldBinding"/>: two routes carrying
        /// the same output to two different blackboard keys is a legitimate thing to author (the
        /// runtime writes both), whereas two links into one field is a contradiction. The only
        /// key that ever collides is <c>blackboardKey</c>, and last-writer-wins there is the same
        /// rule every other blackboard write in this toolset follows.
        ///
        /// <paramref name="taskIndex"/> is a position in <c>node.tasks</c>, which is why this file
        /// has to renumber routes whenever that list moves — see <see cref="DropRoutesAt"/> and
        /// <see cref="ShiftRoutesFrom"/>.
        /// </summary>
        /// <param name="node">The state that owns the transition — and the undo target, because the
        /// transition is a plain serialized class, not an asset of its own.</param>
        /// <param name="transitionIndex">Index into <c>node.transitions</c>.</param>
        /// <param name="taskIndex">Index into <c>node.tasks</c> — the task whose output is read.</param>
        /// <param name="outputName">The output's contract name. Empty is allowed: a row can be
        /// added before the author has picked one, and the inspector says so.</param>
        /// <param name="undoName">Undo label recorded on the node asset.</param>
        /// <returns>False when the arguments describe no row at all, so the caller reports rather
        /// than believing a mutation happened.</returns>
        internal static bool AddOutputRoute(StateTreeNodeAsset node, int transitionIndex,
            int taskIndex, string outputName, string undoName)
        {
            var transition = TransitionAt(node, transitionIndex);
            if (transition == null || taskIndex < 0)
                return false;

            Undo.RecordObject(node, undoName);

            if (transition.outputRoutes == null)
                transition.outputRoutes = new List<TransitionOutputRoute>();

            transition.outputRoutes.Add(new TransitionOutputRoute
            {
                taskIndex = taskIndex,
                outputName = outputName ?? string.Empty,

                // Empty means "under the output's own name" (the runtime rule), which is what the
                // author almost always wants — and pre-filling it with the name would turn a
                // later rename of the output into two edits instead of one.
                blackboardKey = string.Empty
            });

            EditorUtility.SetDirty(node);
            return true;
        }

        /// <summary>Drop one route. Addressed by its position in the transition's own list rather
        /// than by content: rows are deliberately not unique (see <see cref="AddOutputRoute"/>), so
        /// "the row the author pressed ✕ on" is the only unambiguous way to say which one.</summary>
        /// <returns>False when there was nothing to remove — no undo entry is added in that
        /// case.</returns>
        internal static bool RemoveOutputRoute(StateTreeNodeAsset node, int transitionIndex,
            int routeIndex, string undoName)
        {
            var transition = TransitionAt(node, transitionIndex);
            var rows = transition != null ? transition.outputRoutes : null;
            if (rows == null || routeIndex < 0 || routeIndex >= rows.Count)
                return false;

            Undo.RecordObject(node, undoName);
            rows.RemoveAt(routeIndex);
            EditorUtility.SetDirty(node);
            return true;
        }

        /// <summary>The transition at one index, or null. Shared by the route helpers and the
        /// inspector so a stale index is answered the same way everywhere.</summary>
        internal static StateTreeTransition TransitionAt(StateTreeNodeAsset node, int index)
        {
            if (node == null || index < 0 || index >= node.transitions.Count)
                return null;

            return node.transitions[index];
        }

        /// <summary>
        /// The outputs one task publishes, as the inspector offers them — the editor's half of the
        /// M7j capture rule, and deliberately written to mirror it rather than to be permissive.
        ///
        /// Two sources, mirroring the executor's two producers. A task that answers for itself at run
        /// time (<see cref="IStateTreeOutputSource"/> — a graph, and the wrapper that forwards one)
        /// has no fields to read, so what it will produce is known only from the bake:
        /// <c>GraphTaskAsset.declaredOutputs</c>, asked of the graph a <see cref="RunGraphTask"/>
        /// points at rather than of the wrapper. Everything else is a plain C# task whose outputs are
        /// its <c>[TaskOutput]</c> fields — the same <c>IsDefined(…, inherit: true)</c> over
        /// <c>Public | Instance</c> the executor reflects over at completion, so what is offered here
        /// and what is captured there cannot drift.
        ///
        /// Fields of a type the capture cannot carry are LEFT OUT rather than listed and refused:
        /// <c>[TaskOutput]</c> on a Vector2 is an authoring mistake this window cannot fix, and
        /// offering it would promise a route that silently never writes. An empty result is not the
        /// same as "no outputs" — a graph that has not been re-baked also answers nothing, and so
        /// would a future <see cref="IStateTreeOutputSource"/> with no declaration to read — which is
        /// why the inspector falls back to a free-text name rather than to a disabled row.
        /// </summary>
        /// <returns>A fresh list, never null; names in declaration order, duplicates included
        /// exactly as declared (a graph that sets one name twice is one output, and the baker is
        /// what decides that — this only reports).</returns>
        /// <summary>A string field's current value, or null when the field is not one.</summary>
        internal static string StringFieldValue(object target, string fieldName)
        {
            FieldInfo field = target?.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            return field != null && field.FieldType == typeof(string)
                ? field.GetValue(target) as string
                : null;
        }

        /// <summary>Write a string field, undo-correctly. False when there is nothing to
        /// write or the value is unchanged.</summary>
        internal static bool SetStringField(UnityEngine.Object target, string fieldName,
            string value, string undoName)
        {
            FieldInfo field = target != null
                ? target.GetType().GetField(fieldName,
                    BindingFlags.Public | BindingFlags.Instance)
                : null;
            if (field == null || field.FieldType != typeof(string))
                return false;
            if (string.Equals(field.GetValue(target) as string, value, StringComparison.Ordinal))
                return false;

            Undo.RecordObject(target, undoName);
            field.SetValue(target, value ?? string.Empty);
            EditorUtility.SetDirty(target);
            return true;
        }

        internal static List<TaskOutputValue> CollectTaskOutputs(StateTreeTaskAsset task)
        {
            var found = new List<TaskOutputValue>();
            if (task == null)
                return found;

            var graph = task as GraphTaskAsset;
            if (graph == null && task is RunGraphTask wrapper)
                graph = wrapper.graph;

            if (graph != null)
            {
                var declared = graph.declaredOutputs;
                for (var i = 0; declared != null && i < declared.Count; ++i)
                {
                    if (!string.IsNullOrEmpty(declared[i].name))
                        found.Add(declared[i]);
                }

                return found;
            }

            // Public instance only, matching the executor's own GetFields flags: an output the
            // capture cannot reach must not be offered as one.
            var fields = task.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (var i = 0; i < fields.Length; ++i)
            {
                var field = fields[i];
                if (!field.IsDefined(typeof(TaskOutputAttribute), true))
                    continue;
                if (!TryGetBindableKind(field.FieldType, out var kind))
                    continue;

                found.Add(new TaskOutputValue { name = field.Name, kind = kind });
            }

            // Runtime-built outputs, DECLARED (§4e): the class-level contract attribute is
            // what makes an IStateTreeOutputSource's output pickable. The payload TYPE
            // rides in objectValue — an editor-only carrier the label reads back.
            var contracts = (TaskOutputContractAttribute[])task.GetType()
                .GetCustomAttributes(typeof(TaskOutputContractAttribute), true);
            for (var i = 0; i < contracts.Length; ++i)
            {
                if (string.IsNullOrEmpty(contracts[i].name))
                    continue;
                found.Add(new TaskOutputValue
                {
                    name = contracts[i].name,
                    kind = GraphTaskParameterKind.String,
                    objectValue = contracts[i].payloadType
                });
            }

            return found;
        }

        /// <summary>
        /// The blackboard keys that are written ON THE WAY INTO one state — every output route
        /// carried by every transition anywhere in the tree whose target is this state's id.
        ///
        /// This is what makes the M7k key binding pickable rather than typed from memory. A route
        /// writes its key as the transition fires and BEFORE the target state is entered, so these
        /// are exactly the keys a field on that state can expect to find at entry: the suggestion list
        /// is the tree's own wiring read backwards, not a guess.
        ///
        /// Scanned by TARGET ID rather than by object, because a transition names a state by id and
        /// two states may share one (the window reports that separately) — and self-transitions count,
        /// since a state that re-enters itself reads what its own exit routed. The keys come back
        /// DISTINCT, ordinal, in the order the tree's traversal meets them, so a menu built from them
        /// is stable across rebuilds instead of reshuffling under the cursor.
        ///
        /// The key asked for is <see cref="TransitionOutputRoute.ResolvedKey"/>, never the raw field:
        /// an empty <c>blackboardKey</c> means "under the output's own name", and a suggestion list
        /// that showed blanks where the executor writes names would be a list of the wrong thing.
        ///
        /// EACH KEY COMES BACK WITH WHAT IT CARRIES, because a suggestion list is only as good as the
        /// links it produces: a route out of a task returning text offered to a <c>public float</c>
        /// makes a binding the executor drops with a warning, which is a worse outcome than not
        /// offering it. The kind is read from the SOURCE task's own declarations
        /// (<see cref="CollectTaskOutputs"/> — the same list the route's dropdown is built from), and
        /// stays UNKNOWN rather than guessed wherever that cannot answer; see
        /// <see cref="RoutedKey.kindKnown"/> for what the caller does with that.
        /// </summary>
        /// <returns>A fresh list, never null. Empty means nothing routes into the state — which is
        /// not an error: a key can also be written by a graph, by a task, or by a state further
        /// away, which is why the inspector treats an unrouted key as information, not a fault.
        /// </returns>
        internal static List<RoutedKey> CollectIncomingRoutedKeys(StateTreeAsset tree, string nodeId)
        {
            var keys = new List<RoutedKey>();
            if (tree == null || string.IsNullOrEmpty(nodeId))
                return keys;

            var nodes = CollectNodes(tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                var source = nodes[i];
                var transitions = source.transitions;
                for (var t = 0; t < transitions.Count; ++t)
                {
                    var transition = transitions[t];
                    if (transition == null || !string.Equals(transition.targetNodeId, nodeId,
                        StringComparison.Ordinal))
                        continue;

                    var routes = transition.outputRoutes;
                    for (var r = 0; routes != null && r < routes.Count; ++r)
                    {
                        var row = routes[r];
                        if (row == null)
                            continue;

                        var key = row.ResolvedKey();
                        if (string.IsNullOrEmpty(key))
                            continue;

                        MergeRoutedKey(keys, key,
                            TryGetRouteKind(source, row, out var kind), kind);
                    }
                }
            }

            return keys;
        }

        /// <summary>
        /// One key an incoming route writes, and what it carries — the answer the field link menu
        /// filters on, so a route that returns text is not offered to a <c>public float</c>.
        ///
        /// <see cref="kindKnown"/> is a third state rather than a defaulted kind, and the distinction
        /// is the whole reason this is a struct instead of a dictionary. A route out of a graph that
        /// has not been re-baked, or a free-text output name nothing declares, has a kind this window
        /// genuinely cannot work out — and hiding the only key that arrives at a state because a
        /// GUESS said it was the wrong type would be worse than the mismatch it prevents. Unknown is
        /// offered, marked.
        /// </summary>
        internal struct RoutedKey
        {
            /// <summary>What the executor writes under — <see cref="TransitionOutputRoute.ResolvedKey"/>,
            /// never the raw field.</summary>
            public string key;

            /// <summary>Meaningful only while <see cref="kindKnown"/> is true.</summary>
            public GraphTaskParameterKind kind;

            /// <summary>Whether every route that writes this key was found to carry the same, known
            /// kind. Two routes writing one key with different kinds is legal (the blackboard has no
            /// types) and leaves this false, because neither answer would be the truth.</summary>
            public bool kindKnown;
        }

        /// <summary>Fold one route's contribution into the distinct list. Ordinal, in first-met order,
        /// so a menu built from it is stable across rebuilds instead of reshuffling under the cursor.
        /// Disagreement DOWNGRADES: once a key is unknown it stays unknown, because the field that
        /// binds to it will be fed by whichever transition was taken.</summary>
        private static void MergeRoutedKey(List<RoutedKey> keys, string key, bool known,
            GraphTaskParameterKind kind)
        {
            for (var i = 0; i < keys.Count; ++i)
            {
                if (!string.Equals(keys[i].key, key, StringComparison.Ordinal))
                    continue;

                var existing = keys[i];
                if (existing.kindKnown && (!known || existing.kind != kind))
                {
                    existing.kindKnown = false;
                    keys[i] = existing;
                }

                return;
            }

            keys.Add(new RoutedKey { key = key, kind = kind, kindKnown = known });
        }

        /// <summary>What one route carries, read from the task it names — the same
        /// <see cref="CollectTaskOutputs"/> the route's own dropdown is built from, so a key's kind
        /// here and the output's kind there cannot disagree. False for every route whose source this
        /// window cannot resolve: a missing task, an unnamed output, or a name the source does not
        /// publish (which the row is already reported as stale for).</summary>
        private static bool TryGetRouteKind(StateTreeNodeAsset source, TransitionOutputRoute route,
            out GraphTaskParameterKind kind)
        {
            kind = GraphTaskParameterKind.Float;
            if (source == null || route == null || string.IsNullOrEmpty(route.outputName))
                return false;
            if (route.taskIndex < 0 || route.taskIndex >= source.tasks.Count)
                return false;

            var outputs = CollectTaskOutputs(source.tasks[route.taskIndex]);
            for (var i = 0; i < outputs.Count; ++i)
            {
                if (!string.Equals(outputs[i].name, route.outputName, StringComparison.Ordinal))
                    continue;

                kind = outputs[i].kind;
                return true;
            }

            return false;
        }

        /// <summary>
        /// How many entry-time field bindings of one state read <paramref name="key"/> — the
        /// question the M7m wire LOCK stands on: a route whose resolved key this counts non-zero
        /// for is a complete wire, and the inspector freezes its name at both ends. Counts only
        /// blackboard-source rows, because a parameter row that happens to carry the same text in
        /// its unused key slot is not read from the blackboard at all.
        ///
        /// Matched by NODE ID rather than by object, like every other route scan here, so a tree
        /// with two states sharing an id (which the window reports separately) counts both.
        /// </summary>
        internal static int CountEntryBindings(StateTreeAsset tree, string nodeId, string key)
        {
            if (tree == null || string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(key))
                return 0;

            var count = 0;
            var nodes = CollectNodes(tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                var node = nodes[i];
                if (!string.Equals(node.nodeId, nodeId, StringComparison.Ordinal))
                    continue;

                var rows = node.bindings;
                for (var r = 0; rows != null && r < rows.Count; ++r)
                {
                    var row = rows[r];
                    if (row != null
                        && row.sourceKind == StateTreeFieldBinding.SourceKind.BlackboardKey
                        && string.Equals(row.blackboardKey, key, StringComparison.Ordinal))
                        ++count;
                }
            }

            return count;
        }

        /// <summary>The route half of the index remap for a task REMOVAL: rows that read the removed
        /// task die with it, and every row past it follows its task down one. Walks every transition
        /// of the state, because the index is a fact about the state's task list and a row for it can
        /// sit on any transition.
        ///
        /// Records nothing and takes no undo label, for the same reason the binding helpers do not:
        /// the mutation that calls it has already recorded the node, and recording again after the
        /// list changed would capture the mutated state as the state to undo to.</summary>
        private static void DropRoutesAt(StateTreeNodeAsset node, int taskIndex)
        {
            if (node == null || taskIndex < 0)
                return;

            for (var t = 0; t < node.transitions.Count; ++t)
            {
                var rows = node.transitions[t] != null ? node.transitions[t].outputRoutes : null;
                if (rows == null)
                    continue;

                for (var i = rows.Count - 1; i >= 0; --i)
                {
                    var row = rows[i];
                    if (row == null)
                        continue;

                    if (row.taskIndex == taskIndex)
                        rows.RemoveAt(i);
                    else if (row.taskIndex > taskIndex)
                        --row.taskIndex;
                }
            }
        }

        /// <summary>The route half of the index remap for a task INSERTION: every row at or past the
        /// insertion point reads one slot later than it did.</summary>
        private static void ShiftRoutesFrom(StateTreeNodeAsset node, int taskIndex)
        {
            if (node == null || taskIndex < 0)
                return;

            for (var t = 0; t < node.transitions.Count; ++t)
            {
                var rows = node.transitions[t] != null ? node.transitions[t].outputRoutes : null;
                if (rows == null)
                    continue;

                for (var i = 0; i < rows.Count; ++i)
                {
                    var row = rows[i];
                    if (row != null && row.taskIndex >= taskIndex)
                        ++row.taskIndex;
                }
            }
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
            {
                node.tasks.Insert(insertAt, task);

                // The one task mutation in this file that is not an append, and therefore the one
                // that moves existing task indices — every parameter link at or past the insertion
                // point now addresses the task after the one it was authored against, and so does
                // every output route, which names its source task the same way.
                ShiftBindingsFrom(node, StateTreeFieldBinding.TargetKind.Task, insertAt);
                ShiftRoutesFrom(node, insertAt);
            }
            else
            {
                node.tasks.Add(task);
            }

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

            // The transition's INDEX is unchanged, but the object at it is a different class: links
            // authored against the old condition's fields would either dangle or — worse — hit a
            // same-named field of the new one that means something else. Rows the replacement can
            // still satisfy survive, which is what makes re-picking the same type (the picker
            // allows it) a no-op here too. The output routes are untouched: a condition decides
            // WHETHER the transition fires, and a route only says what it carries when it does.
            PruneBindings(node, StateTreeFieldBinding.TargetKind.TransitionCondition,
                node.transitions.IndexOf(transition), created);
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

            // Read the index BEFORE the removal: it is what the surviving parameter links and
            // output routes are renumbered against, and afterwards the list no longer knows where
            // the task was. Both are dropped-and-renumbered, and both have to be: a route left
            // behind would carry the NEXT task's output forward under the deleted task's name.
            var index = node.tasks.IndexOf(task);
            node.tasks.Remove(task);
            DropBindingsAt(node, StateTreeFieldBinding.TargetKind.Task, index);
            DropRoutesAt(node, index);
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
            DropBindingsAt(node, StateTreeFieldBinding.TargetKind.TransitionCondition, index);

            // The output routes need nothing here: they are a field OF the transition just removed,
            // so they went with it, and they name tasks — a list this mutation does not touch.
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

            // The conditions moved with their transitions, so the parameter links have to move too
            // — a reorder that left them behind would silently retarget both of them. The output
            // routes are the opposite case and need nothing: they are stored ON the transition
            // object this swap moved, and what they address is the TASK list, which has not moved.
            SwapBindings(node, StateTreeFieldBinding.TargetKind.TransitionCondition, index, target);
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

        // --- implicit flow (M22) ----------------------------------------------------------

        /// <summary>
        /// The GHOST EDGE, in this frontend's idiom: where a completed state goes when none of
        /// its declared transitions fire, as a sentence for the inspector. A default an author
        /// can see is a convention; one they cannot is a trap — this is the seeing half.
        /// Mirrors <c>StateTreeExecutor.ImplicitAdvance</c> statically (conditions cannot be
        /// evaluated here, so a parent that HAS completion edges is reported as deciding).
        /// </summary>
        internal static string DescribeImplicitFlow(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            if (tree == null || node == null)
                return string.Empty;
            if (node.completeWhen == StateTreeCompleteWhen.Never)
                return "∞ Resident: this state never completes — interrupts are its only way out.";
            if (node.completionFlow == StateTreeCompletionFlow.Hold)
                return "⊣ Holds on completion: the declared edges stay live; nothing implicit.";

            StateTreeNodeAsset current = node;
            var guard = 0;
            while (current != null && guard++ < DepthGuard)
            {
                StateTreeNodeAsset parent = ParentOf(tree, current);
                if (parent == null)
                    return "⇢ Implicit: completing FINISHES the tree.";

                int slot = parent.children.IndexOf(current);
                if (slot >= 0 && slot + 1 < parent.children.Count
                    && parent.children[slot + 1] != null)
                {
                    StateTreeNodeAsset next = ResolveEntryNode(parent.children[slot + 1]);
                    string id = next != null ? next.nodeId : "?";
                    return current == node
                        ? "⇢ Implicit: on completion, flows to '" + id + "' (its next sibling)."
                        : "⇢ Implicit: bubbles up and flows to '" + id + "'.";
                }

                if (HasCompletionEdge(parent))
                    return "⇢ Implicit: bubbles to parent '" + parent.nodeId
                        + "', whose completion edges decide.";
                if (parent.completionFlow == StateTreeCompletionFlow.Hold)
                    return "⇢ Implicit: bubbles to parent '" + parent.nodeId + "', which holds.";
                current = parent;
            }
            return string.Empty;
        }

        /// <summary>The same fact as a BADGE for the outliner row: '∞' resident, '⊣' hold,
        /// '⇢id' the direct implicit sibling, '⇢↑' a bubble, '⇢■' finishes the tree.</summary>
        internal static string ImplicitBadge(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            if (tree == null || node == null)
                return string.Empty;
            if (node.completeWhen == StateTreeCompleteWhen.Never)
                return "∞";
            if (node.completionFlow == StateTreeCompletionFlow.Hold)
                return "⊣";

            StateTreeNodeAsset parent = ParentOf(tree, node);
            if (parent == null)
                return "⇢■";
            int slot = parent.children.IndexOf(node);
            if (slot >= 0 && slot + 1 < parent.children.Count && parent.children[slot + 1] != null)
            {
                StateTreeNodeAsset next = ResolveEntryNode(parent.children[slot + 1]);
                return next != null ? "⇢" + next.nodeId : "⇢?";
            }
            return "⇢↑";
        }

        private static bool HasCompletionEdge(StateTreeNodeAsset node)
        {
            for (var i = 0; i < node.transitions.Count; ++i)
            {
                if (node.transitions[i] != null && !node.transitions[i].checkWhileRunning)
                    return true;
            }
            return false;
        }

        // --- service rules on tree nesting (M23) --------------------------------------------

        /// <summary>The ServiceDef that claims trees of this kind — how the editor knows a
        /// tree is, say, an ABILITY, and whose law its nesting answers to. Null for a plain
        /// tree; first match wins, two services claiming one kind is its own problem.</summary>
        internal static ServiceDef ServiceClaiming(StateTreeAsset tree)
        {
            if (tree == null || string.IsNullOrEmpty(tree.treeKind))
                return null;
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(ServiceDef));
            for (var i = 0; i < guids.Length; i++)
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (def != null && !string.IsNullOrEmpty(def.treeKind)
                    && def.treeKind == tree.treeKind)
                    return def;
            }
            return null;
        }

        /// <summary>The kind a node ANSWERS TO under its service's rules: its own role, or —
        /// plain states being transparent grouping — the nearest typed ancestor's, ending at
        /// <see cref="ServiceDef.TreeRootKind"/>. The root is the TOP, not the ability: what
        /// may sit under it is a declared rule (root → ability, in the ability service).</summary>
        internal static string EffectiveRoleOf(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            var current = node;
            var guard = 0;
            while (current != null && guard++ < DepthGuard)
            {
                if (!string.IsNullOrEmpty(current.roleKind))
                    return current.roleKind;
                current = ParentOf(tree, current);
            }
            return ServiceDef.TreeRootKind;
        }

        /// <summary>The task type a kind's seed names, resolved over loaded assemblies by
        /// full or simple name — null when the seed is empty or names nothing.</summary>
        internal static Type SeedTaskType(ServiceDef service, string kind)
        {
            string typeName = service != null ? service.SeedTaskFor(kind) : "";
            if (string.IsNullOrEmpty(typeName))
                return null;
            foreach (Type type in TypeCache.GetTypesDerivedFrom<StateTreeTaskAsset>())
            {
                if (type.Name == typeName || type.FullName == typeName)
                    return type;
            }
            return null;
        }

        /// <summary>The authored parent of a node — a WALK, not an index: the editor works on
        /// small authored trees and holds no per-tree caches to invalidate.</summary>
        internal static StateTreeNodeAsset ParentOf(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            return tree != null && tree.root != null ? ParentOf(tree.root, node, 0) : null;
        }

        private static StateTreeNodeAsset ParentOf(StateTreeNodeAsset candidate,
            StateTreeNodeAsset node, int depth)
        {
            if (candidate == null || depth > DepthGuard)
                return null;
            for (var i = 0; i < candidate.children.Count; ++i)
            {
                if (candidate.children[i] == node)
                    return candidate;
                StateTreeNodeAsset found = ParentOf(candidate.children[i], node, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
