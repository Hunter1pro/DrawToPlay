using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The right-hand pane of the State Tree window: everything about ONE state, editing the
    /// authored sub-assets in place.
    ///
    /// Three deliberate choices:
    /// 1. Task and condition parameters are drawn generically — SerializedObject over the
    ///    sub-asset, every visible property as a PropertyField, bound. A new library component
    ///    (Runtime/StateTree/Library) therefore needs zero editor work to be authorable, which
    ///    is the difference between a tool and a maintenance burden.
    /// 2. The transition list is the wiring UI. Order is evaluation order in
    ///    <c>StateTreeRunner.TickTree</c> — first passing condition wins — so it is presented as
    ///    an ordered list with explicit move up/down, and the interrupt flag
    ///    (<c>checkWhileRunning</c>) is labelled with what it actually does rather than with the
    ///    field name.
    /// 3. Node ids and display names commit on Enter/blur (<c>isDelayed</c>), not per keystroke:
    ///    an id edit rewrites every transition that targets it, which must happen once per
    ///    rename, not once per character.
    /// </summary>
    internal sealed class StateTreeInspectorPane
    {
        private static readonly Color k_BoxBackground = new Color(0f, 0f, 0f, 0.16f);
        private static readonly Color k_TransitionBackground = new Color(0.30f, 0.55f, 0.92f, 0.12f);
        private static readonly Color k_InterruptBackground = new Color(0.95f, 0.58f, 0.25f, 0.12f);

        private const string k_NoConditionChoice = "None (always passes)";
        private const string k_NoTargetChoice = "<none>";
        private const string k_AddTaskChoice = "Add Task…";

        private readonly ScrollView m_Root;
        private readonly Action m_StructuralChanged;
        private readonly Action m_Edited;

        private StateTreeAsset m_Tree;
        private StateTreeNodeAsset m_Node;

        internal StateTreeInspectorPane(ScrollView root, Action structuralChanged, Action edited)
        {
            m_Root = root;
            m_StructuralChanged = structuralChanged;
            m_Edited = edited;
        }

        internal void Rebuild(StateTreeAsset tree, StateTreeNodeAsset node)
        {
            m_Tree = tree;
            m_Node = node;

            m_Root.Clear();

            if (tree == null)
            {
                m_Root.Add(Hint("No state tree selected."));
                return;
            }

            if (node == null)
            {
                m_Root.Add(Hint("Select a state on the left."));
                return;
            }

            BuildHeader();
            BuildIdentity();
            BuildValidation();
            BuildTasks();
            BuildTransitions();
        }

        // --- sections ---------------------------------------------------------------------

        private void BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6f;

            var title = new Label(string.IsNullOrEmpty(m_Node.displayName)
                ? m_Node.nodeId
                : m_Node.displayName);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1f;
            header.Add(title);

            var ping = new Button(() => EditorGUIUtility.PingObject(m_Node)) { text = "Ping" };
            ping.tooltip = "Highlight this state's sub-asset in the Project window.";
            header.Add(ping);

            m_Root.Add(header);
        }

        private void BuildIdentity()
        {
            var idField = new TextField("Node Id") { value = m_Node.nodeId, isDelayed = true };
            idField.tooltip = "The string transitions target. Renaming rewires every transition "
                + "that pointed here.";
            idField.RegisterValueChangedCallback(evt => CommitNodeId(idField, evt.newValue));
            m_Root.Add(idField);

            var nameField = new TextField("Display Name")
            {
                value = m_Node.displayName,
                isDelayed = true
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                var group = StateTreeEditorOps.BeginUndoGroup("Rename State");
                Undo.RecordObject(m_Node, "Rename State");
                m_Node.displayName = evt.newValue;
                EditorUtility.SetDirty(m_Node);
                StateTreeEditorOps.EndUndoGroup(group);
                m_StructuralChanged?.Invoke();
            });
            m_Root.Add(nameField);

            if (m_Node == m_Tree.root)
            {
                var entry = StateTreeEditorOps.ResolveEntryNode(m_Node);
                var entryId = entry != null ? entry.nodeId : "(none)";
                m_Root.Add(new HelpBox(
                    "This is the tree root. The runner enters the first leaf below it — currently "
                    + $"'{entryId}'. Give the root tasks or transitions only if you want it to be a "
                    + "state in its own right.", HelpBoxMessageType.Info));
            }
        }

        /// <summary>Everything the runner would log as an error at play time, surfaced while it
        /// is still cheap to fix. Dangling targets are the common one: they survive deleting a
        /// state, and the runner only complains when the transition actually fires.</summary>
        private void BuildValidation()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(m_Node.nodeId))
                problems.Add("Node id is empty — no transition can target this state.");

            var nodes = StateTreeEditorOps.CollectNodes(m_Tree);
            var duplicates = 0;
            for (var i = 0; i < nodes.Count; ++i)
            {
                if (nodes[i] != m_Node && nodes[i].nodeId == m_Node.nodeId)
                    ++duplicates;
            }

            if (duplicates > 0)
            {
                problems.Add($"Node id '{m_Node.nodeId}' is used by {duplicates} other state(s). "
                    + "The runner's id index keeps only the last one.");
            }

            for (var i = 0; i < m_Node.transitions.Count; ++i)
            {
                var transition = m_Node.transitions[i];
                if (transition == null)
                {
                    problems.Add($"Transition {i + 1} is null.");
                    continue;
                }

                if (string.IsNullOrEmpty(transition.targetNodeId))
                {
                    problems.Add($"Transition {i + 1} has no target.");
                    continue;
                }

                if (!ContainsId(nodes, transition.targetNodeId))
                {
                    problems.Add($"Transition {i + 1} targets '{transition.targetNodeId}', which no "
                        + "state in this tree defines.");
                }
            }

            for (var i = 0; i < m_Node.tasks.Count; ++i)
            {
                if (m_Node.tasks[i] == null)
                    problems.Add($"Task slot {i + 1} is empty.");
            }

            if (problems.Count == 0)
                return;

            m_Root.Add(new HelpBox(string.Join("\n", problems), HelpBoxMessageType.Warning));
        }

        private void BuildTasks()
        {
            m_Root.Add(SectionLabel($"Tasks ({m_Node.tasks.Count})"));

            var note = Hint("All tasks in a state run together; the state completes when every "
                + "one of them has finished.");
            note.style.marginBottom = 4f;
            m_Root.Add(note);

            for (var i = 0; i < m_Node.tasks.Count; ++i)
            {
                var index = i;
                var task = m_Node.tasks[index];

                var box = Box(k_BoxBackground);

                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.alignItems = Align.Center;

                var label = new Label(task != null ? task.GetType().Name : "(missing task)");
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.flexGrow = 1f;
                header.Add(label);

                var remove = new Button(() => RemoveTask(index)) { text = "✕" };
                remove.tooltip = "Delete this task sub-asset.";
                remove.style.width = 22f;
                header.Add(remove);
                box.Add(header);

                box.Add(BuildParameterFields(task));
                m_Root.Add(box);
            }

            var types = StateTreeEditorOps.CollectConcreteTypes<StateTreeTaskAsset>();
            var choices = new List<string> { k_AddTaskChoice };
            choices.AddRange(StateTreeEditorOps.BuildTypeChoices(types));

            var dropdown = new DropdownField(string.Empty, choices, 0);
            dropdown.tooltip = "Every StateTreeTaskAsset subclass in the project.";
            dropdown.style.marginTop = 4f;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var index = dropdown.index;
                dropdown.SetValueWithoutNotify(k_AddTaskChoice);
                if (index <= 0 || index > types.Count)
                    return;

                AddTask(types[index - 1]);
            });
            m_Root.Add(dropdown);
        }

        private void BuildTransitions()
        {
            m_Root.Add(SectionLabel($"Transitions ({m_Node.transitions.Count})"));

            var note = Hint("Evaluated top to bottom; the first transition whose condition passes "
                + "wins. Interrupts are checked every tick before the tasks run and cancel them.");
            note.style.marginBottom = 4f;
            m_Root.Add(note);

            var nodes = StateTreeEditorOps.CollectNodes(m_Tree);
            var conditionTypes = StateTreeEditorOps.CollectConcreteTypes<StateTreeConditionAsset>();

            for (var i = 0; i < m_Node.transitions.Count; ++i)
            {
                var index = i;
                var transition = m_Node.transitions[index];
                if (transition == null)
                    continue;

                var box = Box(transition.checkWhileRunning
                    ? k_InterruptBackground
                    : k_TransitionBackground);

                box.Add(BuildTransitionHeader(index, transition));
                box.Add(BuildTransitionTarget(index, transition, nodes));
                box.Add(BuildTransitionInterrupt(transition));
                box.Add(BuildTransitionCondition(transition, conditionTypes));
                box.Add(BuildParameterFields(transition.condition));

                m_Root.Add(box);
            }

            var add = new Button(AddTransition) { text = "Add Transition" };
            add.style.marginTop = 4f;
            m_Root.Add(add);
        }

        private VisualElement BuildTransitionHeader(int index, StateTreeTransition transition)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var label = new Label(transition.checkWhileRunning
                ? $"{index + 1}. interrupt"
                : $"{index + 1}. on completion");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.flexGrow = 1f;
            header.Add(label);

            var up = new Button(() => MoveTransition(index, -1)) { text = "▲" };
            up.tooltip = "Evaluate this transition earlier.";
            up.style.width = 22f;
            up.SetEnabled(index > 0);
            header.Add(up);

            var down = new Button(() => MoveTransition(index, 1)) { text = "▼" };
            down.tooltip = "Evaluate this transition later.";
            down.style.width = 22f;
            down.SetEnabled(index < m_Node.transitions.Count - 1);
            header.Add(down);

            var remove = new Button(() => RemoveTransition(index)) { text = "✕" };
            remove.tooltip = "Delete this transition and its condition sub-asset.";
            remove.style.width = 22f;
            header.Add(remove);

            return header;
        }

        /// <summary>Target picker. Choices are index-addressed, never matched back by their
        /// label — display names are not unique and a tree mid-edit can hold two states with the
        /// same id, so string matching would silently pick the wrong one.</summary>
        private VisualElement BuildTransitionTarget(int index, StateTreeTransition transition,
            List<StateTreeNodeAsset> nodes)
        {
            var ids = new List<string> { string.Empty };
            var labels = new List<string> { k_NoTargetChoice };

            for (var i = 0; i < nodes.Count; ++i)
            {
                ids.Add(nodes[i].nodeId);
                labels.Add(FormatNode(nodes[i]));
            }

            var selected = ids.IndexOf(transition.targetNodeId ?? string.Empty);
            if (selected < 0)
            {
                ids.Add(transition.targetNodeId);
                labels.Add($"<missing: {transition.targetNodeId}>");
                selected = ids.Count - 1;
            }

            var dropdown = new DropdownField("Target", labels, selected);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var choice = dropdown.index;
                if (choice < 0 || choice >= ids.Count)
                    return;

                var group = StateTreeEditorOps.BeginUndoGroup("Set Transition Target");
                Undo.RecordObject(m_Node, "Set Transition Target");
                m_Node.transitions[index].targetNodeId = ids[choice];
                EditorUtility.SetDirty(m_Node);
                StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
                StateTreeEditorOps.EndUndoGroup(group);
                DeferStructuralChange();
            });

            return dropdown;
        }

        private VisualElement BuildTransitionInterrupt(StateTreeTransition transition)
        {
            var toggle = new Toggle("Interrupt") { value = transition.checkWhileRunning };
            toggle.tooltip = "On: checked every tick before the state's tasks run; firing it "
                + "cancels them (OnExit receives Cancelled). Off: checked only once every task in "
                + "the state has finished.";
            toggle.RegisterValueChangedCallback(evt =>
            {
                var group = StateTreeEditorOps.BeginUndoGroup("Set Transition Interrupt");
                Undo.RecordObject(m_Node, "Set Transition Interrupt");
                transition.checkWhileRunning = evt.newValue;
                EditorUtility.SetDirty(m_Node);
                StateTreeEditorOps.EndUndoGroup(group);
                DeferStructuralChange();
            });
            return toggle;
        }

        private VisualElement BuildTransitionCondition(StateTreeTransition transition,
            List<Type> conditionTypes)
        {
            var labels = new List<string> { k_NoConditionChoice };
            labels.AddRange(StateTreeEditorOps.BuildTypeChoices(conditionTypes));

            var selected = 0;
            if (transition.condition != null)
            {
                var typeIndex = conditionTypes.IndexOf(transition.condition.GetType());
                if (typeIndex >= 0)
                {
                    selected = typeIndex + 1;
                }
                else
                {
                    // A condition whose type no longer resolves (script deleted): show the truth
                    // rather than silently reporting "None", which reads as "always passes".
                    labels.Add($"<missing: {transition.condition.GetType().Name}>");
                    selected = labels.Count - 1;
                }
            }

            var dropdown = new DropdownField("Condition", labels, selected);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var choice = dropdown.index;
                if (choice < 0 || choice > conditionTypes.Count)
                    return;

                var type = choice == 0 ? null : conditionTypes[choice - 1];
                if (transition.condition == null ? type == null : type == transition.condition.GetType())
                    return;

                var group = StateTreeEditorOps.BeginUndoGroup("Set Transition Condition");
                StateTreeEditorOps.SetTransitionCondition(m_Tree, m_Node, transition, type,
                    "Set Transition Condition");
                StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
                StateTreeEditorOps.EndUndoGroup(group);
                DeferStructuralChange();
            });

            return dropdown;
        }

        /// <summary>Generic parameter block for one task/condition sub-asset. Nothing here knows
        /// any component type: whatever the class serialises is what the author sees.</summary>
        private VisualElement BuildParameterFields(UnityEngine.Object target)
        {
            var container = new VisualElement();
            container.style.marginTop = 2f;

            if (target == null)
                return container;

            var serialized = new SerializedObject(target);
            var iterator = serialized.GetIterator();
            var enterChildren = true;
            var any = false;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script")
                    continue;

                container.Add(new PropertyField(iterator.Copy()));
                any = true;
            }

            if (!any)
                container.Add(Hint("No parameters."));

            container.Bind(serialized);

            // The binding system already writes through and registers its own undo; this only
            // tells the window an edit happened so the batched save timer restarts.
            container.RegisterCallback<SerializedPropertyChangeEvent>(_ => m_Edited?.Invoke());
            return container;
        }

        // --- commands ---------------------------------------------------------------------

        private void CommitNodeId(TextField field, string requested)
        {
            var oldId = m_Node.nodeId;
            var newId = StateTreeEditorOps.MakeUniqueNodeId(m_Tree, requested, m_Node);
            if (newId == oldId)
            {
                field.SetValueWithoutNotify(oldId);
                return;
            }

            var group = StateTreeEditorOps.BeginUndoGroup("Rename State");
            Undo.RecordObject(m_Node, "Rename State");
            m_Node.nodeId = newId;
            EditorUtility.SetDirty(m_Node);
            StateTreeEditorOps.RetargetTransitions(m_Tree, oldId, newId, "Rename State");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            field.SetValueWithoutNotify(newId);
            m_StructuralChanged?.Invoke();
        }

        private void AddTask(Type type)
        {
            var group = StateTreeEditorOps.BeginUndoGroup("Add Task");
            StateTreeEditorOps.CreateTask(m_Tree, m_Node, type, "Add Task");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            DeferStructuralChange();
        }

        private void RemoveTask(int index)
        {
            if (index < 0 || index >= m_Node.tasks.Count)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup("Remove Task");
            StateTreeEditorOps.RemoveTask(m_Tree, m_Node, m_Node.tasks[index], "Remove Task");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            m_StructuralChanged?.Invoke();
        }

        private void AddTransition()
        {
            var nodes = StateTreeEditorOps.CollectNodes(m_Tree);
            var target = string.Empty;
            for (var i = 0; i < nodes.Count; ++i)
            {
                if (nodes[i] != m_Node && nodes[i] != m_Tree.root)
                {
                    target = nodes[i].nodeId;
                    break;
                }
            }

            var group = StateTreeEditorOps.BeginUndoGroup("Add Transition");
            StateTreeEditorOps.AddTransition(m_Tree, m_Node, target, "Add Transition");
            StateTreeEditorOps.EndUndoGroup(group);
            m_StructuralChanged?.Invoke();
        }

        private void RemoveTransition(int index)
        {
            var group = StateTreeEditorOps.BeginUndoGroup("Remove Transition");
            StateTreeEditorOps.RemoveTransition(m_Tree, m_Node, index, "Remove Transition");
            StateTreeEditorOps.EndUndoGroup(group);
            m_StructuralChanged?.Invoke();
        }

        private void MoveTransition(int index, int delta)
        {
            var group = StateTreeEditorOps.BeginUndoGroup("Reorder Transition");
            StateTreeEditorOps.MoveTransition(m_Tree, m_Node, index, delta, "Reorder Transition");
            StateTreeEditorOps.EndUndoGroup(group);
            m_StructuralChanged?.Invoke();
        }

        /// <summary>Rebuilding the pane from inside a DropdownField's own value-changed callback
        /// destroys the element that is mid-notification, so those paths defer one frame.</summary>
        private void DeferStructuralChange()
        {
            m_Root.schedule.Execute(() => m_StructuralChanged?.Invoke()).ExecuteLater(0);
        }

        // --- small builders ---------------------------------------------------------------

        private static bool ContainsId(List<StateTreeNodeAsset> nodes, string id)
        {
            for (var i = 0; i < nodes.Count; ++i)
            {
                if (nodes[i].nodeId == id)
                    return true;
            }

            return false;
        }

        internal static string FormatNode(StateTreeNodeAsset node)
        {
            if (node == null)
                return "(null)";

            return string.IsNullOrEmpty(node.displayName) || node.displayName == node.nodeId
                ? node.nodeId
                : $"{node.nodeId} — {node.displayName}";
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 10f;
            return label;
        }

        private static Label Hint(string text)
        {
            var label = new Label(text);
            label.style.opacity = 0.7f;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement Box(Color background)
        {
            var box = new VisualElement();
            box.style.backgroundColor = background;
            box.style.paddingLeft = 6f;
            box.style.paddingRight = 6f;
            box.style.paddingTop = 4f;
            box.style.paddingBottom = 6f;
            box.style.marginBottom = 4f;
            box.style.borderTopLeftRadius = 4f;
            box.style.borderTopRightRadius = 4f;
            box.style.borderBottomLeftRadius = 4f;
            box.style.borderBottomRightRadius = 4f;
            return box;
        }
    }
}
