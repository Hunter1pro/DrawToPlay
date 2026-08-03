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
    ///
    /// Picking WHICH task or condition to add is not a dropdown. Both add-sites open
    /// <see cref="StateTreeNodePicker"/>, a searchable categorised popup, because the node
    /// library is the part of this toolset that grows without bound: a flat alphabetical list of
    /// class names stops being navigable long before the library stops growing. The mutations
    /// behind the picker are unchanged — the same <see cref="StateTreeEditorOps"/> calls the
    /// dropdowns made.
    ///
    /// A task does not have to be a class. The task picker also lists AUTHORED trees — any tree
    /// marked "reusable task" — and picking one wires a <see cref="RunSubTreeTask"/> to it, so a
    /// behaviour assembled by wiring five states is reusable exactly like a compiled task. Two
    /// things change here: composite task boxes name the tree they run (a class name
    /// would be the same on all of them), and they carry the loop guard, because a tree that runs
    /// itself — directly or through another tree — is the one wiring mistake this model makes
    /// easy to express and impossible to execute.
    ///
    /// AND A TASK DOES NOT HAVE TO EXIST YET. "+ Graph Task" (beside Add Task, and pinned in the
    /// picker) is the authoring loop in one gesture: name it, and it becomes a scaffold GRAPH
    /// under Assets/DrawToPlay/Tasks, a task on this state, and an open canvas to extend. It has
    /// two flavours, which is why the button is a menu: a TASK GRAPH is a logic program (branch,
    /// blackboard, node calls — a <see cref="GraphTaskAsset"/> baked from a .taskgraph file), a
    /// SUB-TREE TASK is states wired together (a <see cref="RunSubTreeTask"/> pointed at the tree
    /// a .statetree file bakes). Composite rows carry the other half — "Edit in Graph" when the
    /// tree came from a graph file, "Convert to Graph…" when it did not, which re-authors a
    /// hand-built tree as a graph and re-points this task at it, leaving the original asset on
    /// disk for the author to delete once satisfied. Everything past that boundary is reached
    /// through <see cref="StateTreeGraphBridge"/>, and every one of these commands reports what
    /// stopped it: a frontend that will not compile must read as a message, never as a button that
    /// does nothing.
    ///
    /// BOTH AUTHORED KINDS ARE HELD BY REFERENCE, THROUGH A WRAPPER. A
    /// <see cref="GraphTaskAsset"/> IS a task — the main asset of its .taskgraph file — so a state
    /// could hold the imported object itself, and must not: everything in this window assumes a
    /// task in <c>node.tasks</c> is a sub-asset of THIS tree (<c>RefreshSubAssetNames</c> renames
    /// it, removing a state destroys it), so pointing at an imported main asset would let "delete
    /// this state" destroy a library asset every other tree references. A state therefore holds a
    /// <see cref="RunGraphTask"/> wrapper that POINTS at the graph, exactly as
    /// <see cref="RunSubTreeTask"/> points at a tree. Editing the graph reaches every state that
    /// runs it with nothing to re-sync, and the wrapper's own field is what the loop guard, the
    /// "Open" button and the sub-asset name all read.
    ///
    /// AND THE WRAPPER IS WHERE THE GRAPH GETS TUNED. A logic graph's variables are its PARAMETERS,
    /// so the graph-task box grows a Parameters section: one row per variable, each an override
    /// checkbox beside a value field that shows the graph's own default while unticked. That is the
    /// Blueprint instance model — the graph is the class, this state's task is an instance — and it
    /// is what stops "the same behaviour but faster" from meaning a second graph file. The override
    /// list is stored on the wrapper (per state, per use), never on the shared graph, and the raw
    /// list is hidden from the generic field block below because a nameless array of structs cannot
    /// show a default, cannot catch a typo'd name, and would be a second way to edit the same data.
    ///
    /// With no state selected the pane edits the TREE: its name, its kind, and the toggle that
    /// makes it appear in every other tree's task picker. Those fields exist nowhere else in the
    /// window, and "mark this tree as a task" is the whole entry point to composition.
    /// </summary>
    internal sealed class StateTreeInspectorPane
    {
        private static readonly Color k_BoxBackground = new Color(0f, 0f, 0f, 0.16f);
        private static readonly Color k_TransitionBackground = new Color(0.30f, 0.55f, 0.92f, 0.12f);
        private static readonly Color k_InterruptBackground = new Color(0.95f, 0.58f, 0.25f, 0.12f);
        private static readonly Color k_SubTreeBackground = new Color(0.55f, 0.40f, 0.92f, 0.14f);

        /// <summary>The logic-graph tint. Deliberately not the sub-tree purple: the two authored
        /// kinds are edited in different windows and fail in different ways, so a glance at a
        /// state's task list has to separate them.</summary>
        private static readonly Color k_GraphTaskBackground = new Color(0.20f, 0.72f, 0.62f, 0.14f);

        private const string k_NoConditionChoice = "None (always passes)";
        private const string k_NoTargetChoice = "<none>";
        private const string k_SubTreeProperty = "subTree";

        /// <summary>The wrapper field an author can re-point by dropping a graph on it — the one
        /// property change that has to rebuild the row, because the row is named after it.</summary>
        private const string k_GraphProperty = "graph";

        /// <summary>The wrapper's per-state parameter overrides. Drawn by
        /// <see cref="BuildProgramParameters"/> and hidden from the generic field block, which would
        /// otherwise show the same data twice as a nameless array of structs.</summary>
        private const string k_OverridesProperty = "overrides";

        private const string k_SetOverrideUndo = "Override Task Parameter";
        private const string k_ClearOverrideUndo = "Clear Task Parameter Override";
        private const string k_EditOverrideUndo = "Set Task Parameter";

        /// <summary>Title shared by every dialog in the graph-task loop, so a failure is
        /// recognisable as "the graph side said no" wherever it comes from.</summary>
        private const string k_GraphDialogTitle = "Graph Tasks";

        private readonly ScrollView m_Root;
        private readonly Action m_StructuralChanged;
        private readonly Action m_Edited;

        private StateTreeAsset m_Tree;
        private StateTreeNodeAsset m_Node;

        /// <summary>The kind to restore when "reusable task" is switched back off. Remembered
        /// rather than assumed, so un-ticking the toggle on a "player_flow" tree does not quietly
        /// turn it into an enemy AI tree.</summary>
        private string m_LastNonTaskKind = StateTreeEditorOps.DefaultTreeKind;

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
                BuildTreeSettings();
                return;
            }

            BuildHeader();
            BuildIdentity();
            BuildValidation();
            BuildTasks();
            BuildTransitions();
        }

        // --- tree settings ----------------------------------------------------------------

        /// <summary>Shown when no state is selected — the tree's own fields, which the window
        /// otherwise never exposes. The important control is the "reusable task" toggle: it is
        /// the single act that turns a finished tree into a task other trees can pick, so it is
        /// presented as a decision ("is this a reusable task?") rather than as the string field
        /// it is stored in. The raw kind stays editable underneath, because the runtime treats it
        /// as free-form data and the tool must not narrow that.</summary>
        private void BuildTreeSettings()
        {
            var isTask = StateTreeEditorOps.IsTaskTree(m_Tree);
            if (!isTask && !string.IsNullOrWhiteSpace(m_Tree.treeKind))
                m_LastNonTaskKind = m_Tree.treeKind;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6f;

            var title = new Label(m_Tree.name);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1f;
            header.Add(title);

            var ping = new Button(() => EditorGUIUtility.PingObject(m_Tree)) { text = "Ping" };
            ping.tooltip = "Highlight this tree asset in the Project window.";
            header.Add(ping);
            m_Root.Add(header);

            var nameField = new TextField("Tree Name")
            {
                value = m_Tree.treeName,
                isDelayed = true
            };
            nameField.tooltip = "The name this tree goes by in other trees' task picker. Empty "
                + "falls back to the asset file name.";
            nameField.RegisterValueChangedCallback(evt =>
            {
                var group = StateTreeEditorOps.BeginUndoGroup("Rename State Tree");
                StateTreeEditorOps.SetTreeName(m_Tree, evt.newValue, "Rename State Tree");
                StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
                StateTreeEditorOps.EndUndoGroup(group);
                DeferStructuralChange();
            });
            m_Root.Add(nameField);

            var kindField = new TextField("Tree Kind")
            {
                value = m_Tree.treeKind,
                isDelayed = true
            };
            kindField.tooltip = "Free-form classification ('enemy_ai', 'player_flow', 'task'). "
                + "Only 'task' has a meaning to the editor.";
            kindField.RegisterValueChangedCallback(evt => CommitTreeKind(evt.newValue));
            m_Root.Add(kindField);

            var toggle = new Toggle("Mark as reusable task") { value = isTask };
            toggle.tooltip = "On: this tree is listed in every other tree's Add Task… picker "
                + "under Authored/, and running it there is one task.";
            toggle.RegisterValueChangedCallback(evt => CommitTreeKind(evt.newValue
                ? StateTreeEditorOps.TaskTreeKind
                : (string.IsNullOrWhiteSpace(m_LastNonTaskKind)
                    ? StateTreeEditorOps.DefaultTreeKind
                    : m_LastNonTaskKind)));
            m_Root.Add(toggle);

            m_Root.Add(new HelpBox(isTask
                ? "This tree is a reusable task. Adding it to a state of another tree runs it on "
                + "that tree's blackboard and owner; it finishes when it reaches one of the states "
                + "listed in the composite task's success/failure lists."
                : "Mark this tree as a reusable task to pick it as a single task inside other "
                + "trees — behaviour authored by wiring, reused like a compiled task.",
                HelpBoxMessageType.Info));

            if (isTask && m_Tree.root == null)
            {
                m_Root.Add(new HelpBox("This tree has no root state, so running it as a task does "
                    + "nothing. Create a root before using it elsewhere.",
                    HelpBoxMessageType.Warning));
            }

            m_Root.Add(SectionLabel("States"));
            var nodes = StateTreeEditorOps.CollectNodes(m_Tree);
            m_Root.Add(Hint(nodes.Count == 0
                ? "This tree has no states yet — press Add State in the toolbar."
                : $"{nodes.Count} state(s). Select one on the left to edit it."));
        }

        private void CommitTreeKind(string kind)
        {
            var group = StateTreeEditorOps.BeginUndoGroup("Set Tree Kind");
            StateTreeEditorOps.SetTreeKind(m_Tree, kind, "Set Tree Kind");
            StateTreeEditorOps.EndUndoGroup(group);
            DeferStructuralChange();
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
                var composite = task as RunSubTreeTask;
                var program = task as RunGraphTask;

                // The wrapper's field IS the origin, so this is a lookup, not a search.
                var origin = program != null && program.graph != null
                    ? AssetDatabase.GetAssetPath(program.graph)
                    : null;

                var tint = k_BoxBackground;
                if (composite != null)
                    tint = k_SubTreeBackground;
                else if (program != null)
                    tint = k_GraphTaskBackground;

                var box = Box(tint);

                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.alignItems = Align.Center;

                var label = new Label(program != null
                    ? ProgramLabel(program.graph)
                    : TaskLabel(task));
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.flexGrow = 1f;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                header.Add(label);

                if (composite != null && composite.subTree != null && composite.subTree != m_Tree)
                {
                    var open = new Button(() => OpenTree(composite.subTree)) { text = "Open" };
                    open.tooltip = "Edit the sub-tree in this window.";
                    header.Add(open);
                    header.Add(BuildGraphButton(composite));
                }
                else if (program != null && !string.IsNullOrEmpty(origin))
                {
                    var open = new Button(() => OpenGraphOrReport(origin)) { text = "Open" };
                    open.tooltip = $"Open '{origin}' on the graph canvas. Saving the graph changes "
                        + "what this state runs — there is no copy.";
                    header.Add(open);
                }

                var remove = new Button(() => RemoveTask(index)) { text = "✕" };
                remove.tooltip = "Delete this task sub-asset.";
                remove.style.width = 22f;
                header.Add(remove);
                box.Add(header);

                if (composite != null)
                    box.Add(BuildSubTreeStatus(composite));

                if (program != null)
                {
                    box.Add(BuildProgramStatus(program));
                    box.Add(BuildProgramParameters(program));
                }

                // The override list is drawn as the Parameters section above — as checkboxes against
                // the graph's own parameter list, which is the only place the NAMES are known — so
                // the raw list is hidden rather than drawn twice with one of the two unable to show
                // a default or catch a typo'd name.
                var fields = BuildParameterFields(task,
                    program != null ? k_OverridesProperty : null);
                if (composite != null)
                {
                    // The sub-tree can also be swapped by dropping an asset on the generic
                    // ObjectField below, which is the path that can produce a loop — so that one
                    // property (and only that one, or every keystroke in the state lists would
                    // rebuild the pane under the author's cursor) re-runs the guard.
                    fields.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
                    {
                        if (evt.changedProperty != null
                            && evt.changedProperty.propertyPath == k_SubTreeProperty)
                            DeferStructuralChange();
                    });
                }
                else if (program != null)
                {
                    // Same reasoning for the wrapper's graph field: the row is named after it and
                    // its Open button is built from it, so re-pointing it has to rebuild the row.
                    fields.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
                    {
                        if (evt.changedProperty != null
                            && evt.changedProperty.propertyPath == k_GraphProperty)
                            DeferStructuralChange();
                    });
                }

                box.Add(fields);
                m_Root.Add(box);
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4f;

            var add = new Button { text = "Add Task…" };
            add.tooltip = "Search every task type, every tree marked as a reusable task AND every "
                + "logic graph, by name, category or description.";
            add.style.flexGrow = 1f;
            add.clicked += () => StateTreeNodePicker.Show(StateTreeNodePicker.ScreenRectOf(add),
                typeof(StateTreeTaskAsset), AddTask, "Add Task", AddSubTreeTask, CanRunSubTree,
                CreateSubTreeTaskGraph, AddGraphTask, CreateTaskGraph);
            row.Add(add);

            // The same commands as the picker's pinned rows, one click closer. They are worth both
            // places: from inside the picker they are the answer to "none of these is what I
            // want", and out here they are the answer to "I already know I am writing a new one".
            // A menu rather than two buttons because the choice is between two flavours of one
            // decision, and naming it once ("+ Graph Task") is what makes that visible.
            var graph = new Button { text = "+ Graph Task ▾" };
            graph.style.flexShrink = 0f;
            graph.tooltip = "Create a new task as a graph under " + StateTreeGraphBridge.TaskFolder
                + ", add it to this state, and open its canvas.";
            graph.clicked += () => ShowGraphTaskMenu(graph);
            row.Add(graph);

            m_Root.Add(row);

            // Reported once, up front, rather than only on click: a command that cannot run should
            // look like a command that cannot run. The two flavours fail independently — one graph
            // kind can be broken while the other is fine — so each is asked separately and the
            // button only disappears as an option when BOTH are gone.
            var hasTaskGraph = StateTreeGraphBridge.TryResolveTaskGraphAuthoring(out var noProgram);
            var hasSubTree = StateTreeGraphBridge.TryResolveAuthoring(out var noSubTree);
            graph.SetEnabled(hasTaskGraph || hasSubTree);

            if (!hasTaskGraph)
            {
                m_Root.Add(new HelpBox("New task graphs are unavailable: " + noProgram,
                    HelpBoxMessageType.Warning));
            }

            if (!hasSubTree)
            {
                m_Root.Add(new HelpBox("New sub-tree tasks are unavailable: " + noSubTree,
                    HelpBoxMessageType.Warning));
            }
        }

        /// <summary>The two authoring flavours, as a menu. Each item is disabled rather than
        /// hidden when its half of the frontend is missing: an author who used it yesterday must
        /// find out that it is broken, not that it never existed.</summary>
        private void ShowGraphTaskMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();

            var program = new GUIContent("Task Graph… (logic nodes)");
            if (StateTreeGraphBridge.TryResolveTaskGraphAuthoring(out _))
                menu.AddItem(program, false, CreateTaskGraph);
            else
                menu.AddDisabledItem(program);

            var subTree = new GUIContent("Sub-Tree Task… (wired states)");
            if (StateTreeGraphBridge.TryResolveAuthoring(out _))
                menu.AddItem(subTree, false, CreateSubTreeTaskGraph);
            else
                menu.AddDisabledItem(subTree);

            // GenericMenu positions in the current window's GUI space, which for a UI Toolkit
            // panel filling an EditorWindow is what worldBound already is.
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>The composite row's graph button, which is two commands wearing one slot
        /// because the author's question is one question — "let me edit this on the canvas" — and
        /// the answer depends on something they should not have to check first: whether the tree
        /// is a <c>.statetree</c> graph file or a hand-authored asset. Graph-backed trees open;
        /// the rest offer the conversion that makes them open.</summary>
        private Button BuildGraphButton(RunSubTreeTask task)
        {
            var path = AssetDatabase.GetAssetPath(task.subTree);

            if (StateTreeGraphBridge.IsGraphAssetPath(path))
            {
                var edit = new Button(() => OpenGraphOrReport(path)) { text = "Edit in Graph" };
                edit.tooltip = $"Open '{path}' on the graph canvas. Saving the graph re-bakes the "
                    + "tree this task runs.";
                return edit;
            }

            var convert = new Button(() => ConvertToGraph(task)) { text = "Convert to Graph…" };
            convert.tooltip = "This tree was authored by hand. Write it out as a graph file, "
                + "re-point this task at the graph, and open it.";
            convert.SetEnabled(!string.IsNullOrEmpty(path));
            return convert;
        }

        /// <summary>Composite tasks are labelled by what they run: "Run Sub Tree" on five boxes
        /// tells the author nothing, and the tree name is the only thing that differs.</summary>
        private static string TaskLabel(StateTreeTaskAsset task)
        {
            if (task == null)
                return "(missing task)";

            return task is RunSubTreeTask composite
                ? $"Sub Tree · {StateTreeEditorOps.TreeDisplayName(composite.subTree)}"
                : task.GetType().Name;
        }

        /// <summary>A logic-graph wrapper is labelled the same way — by the graph it runs, which is
        /// the only thing that differs between two of them.</summary>
        private static string ProgramLabel(GraphTaskAsset graph)
        {
            return $"Task Graph · {StateTreeEditorOps.GraphTaskDisplayName(graph)}";
        }

        /// <summary>What the box says under the title. The unassigned case is an error rather than
        /// a note because the runtime treats it as one: <see cref="RunGraphTask"/> logs and the
        /// task fails the moment the state is entered.</summary>
        private VisualElement BuildProgramStatus(RunGraphTask program)
        {
            var container = new VisualElement();

            if (program.graph == null)
            {
                container.Add(new HelpBox("No graph assigned — this task fails as soon as the "
                    + "state is entered. Drop a .taskgraph asset on the field below, or add it "
                    + "again with Add Task….", HelpBoxMessageType.Warning));
                return container;
            }

            var nodes = program.graph.nodes != null ? program.graph.nodes.Count : 0;
            if (nodes == 0)
            {
                container.Add(new HelpBox("This program is empty, so the task succeeds "
                    + "immediately. Open the graph and wire On Tick to something.",
                    HelpBoxMessageType.Info));
                return container;
            }

            var hint = Hint($"{nodes} program nodes, run straight from the graph — editing the "
                + "graph changes what this state does, with nothing to re-sync.");
            hint.style.marginTop = 2f;
            container.Add(hint);
            return container;
        }

        // --- graph task parameters --------------------------------------------------------

        /// <summary>
        /// The per-state override list, drawn against the GRAPH's parameter list — the Blueprint
        /// instance model: the graph declares the knobs and their defaults, and a state that runs it
        /// changes the ones it cares about. Hence a checkbox per row rather than a plain value field:
        /// "3" typed into an unchecked row and "3" typed into a checked one mean different things
        /// (follow the graph vs pin this state to 3), and the difference must survive the graph
        /// author changing their mind about the default.
        ///
        /// The section is built from <c>graph.parameters</c>, never from the override list, so a row
        /// exists for every knob whether or not this state has touched it — a knob nobody knows about
        /// is a knob nobody turns. Overrides naming something the graph no longer declares are the
        /// exception, listed after the rows as warnings with a way to delete them: renaming a
        /// variable in the graph strands them silently otherwise, and the runtime's answer is a
        /// single log line nobody reads.
        /// </summary>
        private VisualElement BuildProgramParameters(RunGraphTask program)
        {
            var container = new VisualElement();

            // No graph is already reported as an error above, and every override becomes valid again
            // the moment one is assigned — calling them all stale would be noise AND wrong.
            if (program.graph == null)
                return container;

            var parameters = program.graph.parameters;
            var count = parameters != null ? parameters.Count : 0;
            var stale = CollectStaleOverrides(program);

            if (count == 0 && stale.Count == 0)
                return container;

            var title = new Label($"Parameters ({count})");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 6f;
            container.Add(title);

            if (count > 0)
            {
                var hint = Hint("Tick a parameter to give this state its own value. Unticked rows "
                    + "show what the graph itself uses, and follow it when the graph changes.");
                hint.style.marginBottom = 2f;
                container.Add(hint);

                for (var i = 0; i < count; ++i)
                {
                    var parameter = parameters[i];
                    if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                        container.Add(BuildParameterRow(program, parameter));
                }
            }

            for (var i = 0; i < stale.Count; ++i)
                container.Add(BuildStaleOverrideRow(program, stale[i]));

            return container;
        }

        /// <summary>One knob: the override checkbox, the name, and the value field for its kind.
        /// The field is disabled and dimmed while the checkbox is off, showing the graph's default —
        /// which is what the state actually runs, so it is shown rather than blanked.</summary>
        private VisualElement BuildParameterRow(RunGraphTask program, GraphTaskParameter parameter)
        {
            var row = new VisualElement();
            row.AddToClassList("unity-base-field");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var overridden = IsOverridden(program, parameter.name);

            var toggle = new Toggle();
            toggle.style.flexShrink = 0f;
            toggle.style.marginRight = 2f;
            toggle.tooltip = "Give this state its own value for this parameter. Off: the state uses "
                + "whatever the graph is authored with.";
            toggle.SetValueWithoutNotify(overridden);
            row.Add(toggle);

            var read = IsParameterRead(program.graph, parameter.name);
            var label = new Label(read ? parameter.name : parameter.name + "  (unused)");
            label.AddToClassList("unity-base-field__label");
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.tooltip = read
                ? $"Graph variable '{parameter.name}' ({KindLabel(parameter.kind)}). Default: "
                    + DefaultLabel(parameter)
                : $"Graph variable '{parameter.name}' ({KindLabel(parameter.kind)}) is declared but "
                    + "no node in the graph reads it, so overriding it changes nothing. A variable "
                    + "used only on a library call's parameter port reads this way: those are baked "
                    + "into the graph and cannot be overridden per state.";
            row.Add(label);

            var input = BuildParameterInput(program, parameter);
            input.AddToClassList("unity-base-field__input");
            input.style.flexGrow = 1f;
            WriteParameterInput(input, program, parameter);
            ApplyOverrideStyle(input, overridden);
            row.Add(input);

            toggle.RegisterValueChangedCallback(evt =>
            {
                SetOverride(program, parameter, evt.newValue);
                WriteParameterInput(input, program, parameter);
                ApplyOverrideStyle(input, evt.newValue);
            });

            return row;
        }

        /// <summary>The value editor for one parameter kind. Its callback writes straight into the
        /// override entry, which exists whenever the field is editable — the field is disabled while
        /// the row is not overridden, so there is no state where a keystroke has nowhere to go.
        /// </summary>
        private VisualElement BuildParameterInput(RunGraphTask program, GraphTaskParameter parameter)
        {
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                {
                    var field = new TextField { isDelayed = true };
                    field.RegisterValueChangedCallback(evt => CommitOverride(program, parameter,
                        entry => entry.stringValue = evt.newValue ?? string.Empty));
                    return field;
                }

                case GraphTaskParameterKind.Bool:
                {
                    var field = new Toggle();
                    field.RegisterValueChangedCallback(evt => CommitOverride(program, parameter,
                        entry => entry.floatValue = evt.newValue ? 1f : 0f));
                    return field;
                }

                default:
                {
                    var field = new FloatField { isDelayed = true };
                    field.RegisterValueChangedCallback(evt => CommitOverride(program, parameter,
                        entry => entry.floatValue = evt.newValue));
                    return field;
                }
            }
        }

        /// <summary>Push the EFFECTIVE value into the field: the override when there is one, the
        /// graph's default when there is not. Without notify — this is the tool writing to itself,
        /// not the author writing to the asset.</summary>
        private static void WriteParameterInput(VisualElement input, RunGraphTask program,
            GraphTaskParameter parameter)
        {
            var entry = ActiveOverride(program, parameter.name);

            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                    ((TextField)input).SetValueWithoutNotify(entry != null
                        ? entry.stringValue ?? string.Empty
                        : parameter.stringValue ?? string.Empty);
                    break;

                case GraphTaskParameterKind.Bool:
                    ((Toggle)input).SetValueWithoutNotify(
                        (entry != null ? entry.floatValue : parameter.floatValue) != 0f);
                    break;

                default:
                    ((FloatField)input).SetValueWithoutNotify(
                        entry != null ? entry.floatValue : parameter.floatValue);
                    break;
            }
        }

        private static void ApplyOverrideStyle(VisualElement input, bool overridden)
        {
            input.SetEnabled(overridden);
            input.style.opacity = overridden ? 1f : 0.55f;
        }

        /// <summary>Turn an override on or off. On seeds the entry from the graph's current default,
        /// so ticking the box and typing nothing pins the value the author was already looking at;
        /// off DELETES the entry, because an override that exists but does nothing is the state this
        /// UI cannot show and the runtime would still carry.</summary>
        private void SetOverride(RunGraphTask program, GraphTaskParameter parameter, bool on)
        {
            var undoName = on ? k_SetOverrideUndo : k_ClearOverrideUndo;
            var group = StateTreeEditorOps.BeginUndoGroup(undoName);
            Undo.RecordObject(program, undoName);

            if (program.overrides == null)
                program.overrides = new List<GraphTaskParameterOverride>();

            var index = IndexOfOverride(program, parameter.name);
            if (on && index < 0)
            {
                program.overrides.Add(new GraphTaskParameterOverride
                {
                    name = parameter.name,
                    enabled = true,
                    floatValue = parameter.floatValue,
                    stringValue = parameter.stringValue ?? string.Empty
                });
            }
            else if (on)
            {
                // An entry left behind switched off — hand-edited YAML, or a merge. Re-arm it in
                // place rather than adding a second entry with the same name, which the runtime
                // would resolve by an order nothing here controls.
                program.overrides[index].enabled = true;
            }
            else if (index >= 0)
            {
                program.overrides.RemoveAt(index);
            }

            EditorUtility.SetDirty(program);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        private void CommitOverride(RunGraphTask program, GraphTaskParameter parameter,
            Action<GraphTaskParameterOverride> write)
        {
            var entry = ActiveOverride(program, parameter.name);
            if (entry == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditOverrideUndo);
            Undo.RecordObject(program, k_EditOverrideUndo);
            write(entry);
            EditorUtility.SetDirty(program);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        /// <summary>An override naming a parameter the graph no longer declares — almost always a
        /// variable renamed or deleted on the canvas. It is dead weight the runtime warns about
        /// once, so it is surfaced where it can be deleted instead.</summary>
        private VisualElement BuildStaleOverrideRow(RunGraphTask program, string name)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2f;

            var help = new HelpBox($"'{name}' is overridden here, but the graph has no parameter by "
                + "that name — it was probably renamed or deleted. The override does nothing.",
                HelpBoxMessageType.Warning);
            help.style.flexGrow = 1f;
            row.Add(help);

            var remove = new Button { text = "Remove" };
            remove.style.flexShrink = 0f;
            remove.tooltip = $"Delete the '{name}' override from this task.";
            remove.clicked += () =>
            {
                RemoveOverride(program, name);
                row.RemoveFromHierarchy();
            };
            row.Add(remove);

            return row;
        }

        private void RemoveOverride(RunGraphTask program, string name)
        {
            var index = IndexOfOverride(program, name);
            if (index < 0)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_ClearOverrideUndo);
            Undo.RecordObject(program, k_ClearOverrideUndo);
            program.overrides.RemoveAt(index);
            EditorUtility.SetDirty(program);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        /// <summary>Override names this task carries that the graph does not declare, in list order
        /// and without repeats.</summary>
        private static List<string> CollectStaleOverrides(RunGraphTask program)
        {
            var stale = new List<string>();
            if (program.overrides == null)
                return stale;

            var declared = new HashSet<string>(StringComparer.Ordinal);
            var parameters = program.graph != null ? program.graph.parameters : null;
            if (parameters != null)
            {
                for (var i = 0; i < parameters.Count; ++i)
                {
                    if (parameters[i] != null && parameters[i].name != null)
                        declared.Add(parameters[i].name);
                }
            }

            for (var i = 0; i < program.overrides.Count; ++i)
            {
                var entry = program.overrides[i];
                if (entry == null || string.IsNullOrEmpty(entry.name))
                    continue;
                if (declared.Contains(entry.name) || stale.Contains(entry.name))
                    continue;
                stale.Add(entry.name);
            }

            return stale;
        }

        private static int IndexOfOverride(RunGraphTask program, string name)
        {
            if (program.overrides == null)
                return -1;

            for (var i = 0; i < program.overrides.Count; ++i)
            {
                var entry = program.overrides[i];
                if (entry != null && string.Equals(entry.name, name, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>The override entry that is actually in force for a parameter — present AND
        /// switched on, which is what the runtime applies.</summary>
        private static GraphTaskParameterOverride ActiveOverride(RunGraphTask program, string name)
        {
            var index = IndexOfOverride(program, name);
            if (index < 0)
                return null;

            var entry = program.overrides[index];
            return entry != null && entry.enabled ? entry : null;
        }

        private static bool IsOverridden(RunGraphTask program, string name)
            => ActiveOverride(program, name) != null;

        /// <summary>Whether any instruction in the baked program pulls this parameter. A declared
        /// variable no node reads produces a knob that does nothing, which the row says out loud
        /// rather than leaving the author to test it.</summary>
        private static bool IsParameterRead(GraphTaskAsset graph, string name)
        {
            var nodes = graph.nodes;
            if (nodes == null)
                return false;

            for (var i = 0; i < nodes.Count; ++i)
            {
                var node = nodes[i];
                if (node == null)
                    continue;
                if (node.kind != GraphTaskNodeKind.GetParamFloat
                    && node.kind != GraphTaskNodeKind.GetParamString
                    && node.kind != GraphTaskNodeKind.GetParamBool)
                    continue;
                if (string.Equals(node.stringValue, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string KindLabel(GraphTaskParameterKind kind)
        {
            switch (kind)
            {
                case GraphTaskParameterKind.String:
                    return "text";
                case GraphTaskParameterKind.Bool:
                    return "checkbox";
                default:
                    return "number";
            }
        }

        private static string DefaultLabel(GraphTaskParameter parameter)
        {
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                    return $"\"{parameter.stringValue ?? string.Empty}\"";
                case GraphTaskParameterKind.Bool:
                    return parameter.floatValue != 0f ? "on" : "off";
                default:
                    return parameter.floatValue.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The loop guard, and the one place an author can see it. The picker never
        /// offers a tree that would close a loop, so reaching an error here means the asset was
        /// dropped straight onto the field — hence a way out (Clear) next to the message rather
        /// than a silent revert, which would look like the drop had not registered.</summary>
        private VisualElement BuildSubTreeStatus(RunSubTreeTask task)
        {
            var container = new VisualElement();

            if (task.subTree == null)
            {
                container.Add(new HelpBox("No sub-tree assigned — this task fails as soon as the "
                    + "state is entered.", HelpBoxMessageType.Warning));
                return container;
            }

            if (task.subTree == m_Tree)
            {
                container.Add(new HelpBox("A tree cannot run itself: the composition would never "
                    + "bottom out. Pick a different tree.", HelpBoxMessageType.Error));
                container.Add(ClearSubTreeButton(task));
                return container;
            }

            if (StateTreeEditorOps.CreatesCycle(task.subTree, m_Tree))
            {
                container.Add(new HelpBox(
                    $"'{StateTreeEditorOps.TreeDisplayName(task.subTree)}' runs this tree again, "
                    + "directly or through another sub-tree. The runtime aborts the chain rather "
                    + "than recursing forever.", HelpBoxMessageType.Error));
                container.Add(ClearSubTreeButton(task));
                return container;
            }

            // The default success/failure lists are generic names ("success", "exit"); a sub-tree
            // that uses none of them can only ever be ended by a parent interrupt. That is legal
            // and occasionally intended, so it is a warning with the available ids attached —
            // dangling-target reporting, applied across the tree boundary.
            var states = StateTreeEditorOps.CollectNodes(task.subTree);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < states.Count; ++i)
                ids.Add(states[i].nodeId);

            if (!AnyKnownState(task.successStates, ids) && !AnyKnownState(task.failureStates, ids))
            {
                container.Add(new HelpBox("None of the success/failure states below exist in "
                    + $"'{StateTreeEditorOps.TreeDisplayName(task.subTree)}', so this task runs "
                    + "until something in this tree interrupts it. Its states are: "
                    + JoinIds(states) + ".", HelpBoxMessageType.Warning));
            }

            return container;
        }

        private static bool AnyKnownState(List<string> listed, HashSet<string> ids)
        {
            if (listed == null)
                return false;

            for (var i = 0; i < listed.Count; ++i)
            {
                if (!string.IsNullOrWhiteSpace(listed[i]) && ids.Contains(listed[i].Trim()))
                    return true;
            }

            return false;
        }

        private static string JoinIds(List<StateTreeNodeAsset> nodes)
        {
            const int max = 8;
            var names = new List<string>(Mathf.Min(nodes.Count, max));
            for (var i = 0; i < nodes.Count && i < max; ++i)
                names.Add(nodes[i].nodeId);

            var joined = names.Count > 0 ? string.Join(", ", names) : "(none)";
            return nodes.Count > max ? joined + ", …" : joined;
        }

        private Button ClearSubTreeButton(RunSubTreeTask task)
        {
            var clear = new Button(() => ClearSubTree(task)) { text = "Clear Sub Tree" };
            clear.tooltip = "Unassign the sub-tree so the task can be re-pointed.";
            return clear;
        }

        private void BuildTransitions()
        {
            m_Root.Add(SectionLabel($"Transitions ({m_Node.transitions.Count})"));

            var note = Hint("Evaluated top to bottom; the first transition whose condition passes "
                + "wins. Interrupts are checked every tick before the tasks run and cancel them.");
            note.style.marginBottom = 4f;
            m_Root.Add(note);

            var nodes = StateTreeEditorOps.CollectNodes(m_Tree);

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
                box.Add(BuildTransitionCondition(transition));
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

        /// <summary>The condition slot. A transition holds exactly one condition, so this is a
        /// swap, not a list — the picker names the replacement and the ✕ is the only way back to
        /// "None (always passes)", which the picker itself cannot express (it deals in types).
        /// The base-field USS classes are borrowed on purpose: they are what makes this composite
        /// row line up with the real fields (Target, Interrupt) either side of it.</summary>
        private VisualElement BuildTransitionCondition(StateTreeTransition transition)
        {
            var row = new VisualElement();
            row.AddToClassList("unity-base-field");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var label = new Label("Condition");
            label.AddToClassList("unity-base-field__label");
            row.Add(label);

            var current = transition.condition != null ? transition.condition.GetType() : null;
            var button = new Button
            {
                text = current != null
                    ? StateTreeNodePicker.DisplayNameOf(current)
                    : k_NoConditionChoice
            };
            button.AddToClassList("unity-base-field__input");
            button.style.flexGrow = 1f;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.tooltip = current != null
                ? $"{current.FullName}\n\nClick to replace this condition with another."
                : "This transition fires as soon as it is evaluated. Click to add a condition.";
            button.clicked += () => StateTreeNodePicker.Show(
                StateTreeNodePicker.ScreenRectOf(button), typeof(StateTreeConditionAsset),
                type => SetCondition(transition, type),
                current != null ? "Change Condition" : "Add Condition");
            row.Add(button);

            var clear = new Button(() => SetCondition(transition, null)) { text = "✕" };
            clear.tooltip = "Remove the condition — the transition then always passes.";
            clear.style.width = 22f;
            clear.style.flexShrink = 0f;
            clear.SetEnabled(current != null);
            row.Add(clear);

            return row;
        }

        /// <summary>Generic parameter block for one task/condition sub-asset. Nothing here knows
        /// any component type: whatever the class serialises is what the author sees.</summary>
        /// <param name="target">The sub-asset to draw.</param>
        /// <param name="hiddenProperty">One property to leave out, for the single case where a
        /// purpose-built control above already edits it. Named rather than inferred, so a field only
        /// disappears where something visibly replaced it.</param>
        private VisualElement BuildParameterFields(UnityEngine.Object target,
            string hiddenProperty = null)
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
                if (hiddenProperty != null && iterator.propertyPath == hiddenProperty)
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
            if (type == null || m_Tree == null || m_Node == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup("Add Task");
            StateTreeEditorOps.CreateTask(m_Tree, m_Node, type, "Add Task");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            DeferStructuralChange();
        }

        /// <summary>The other half of "Add Task…": the picked item was an authored tree, so the
        /// state gets a composite task wired to it. Same undo shape as
        /// <see cref="AddTask"/> — one gesture, one step.</summary>
        private void AddSubTreeTask(StateTreeAsset subTree)
        {
            if (subTree == null || m_Tree == null || m_Node == null)
                return;

            // Unreachable through the picker (CanRunSubTree filtered it out) but not through a
            // stale popup left open while the other tree changed underneath it.
            if (!CanRunSubTree(subTree))
            {
                EditorUtility.DisplayDialog("Cannot add sub-tree",
                    $"'{StateTreeEditorOps.TreeDisplayName(subTree)}' runs '{m_Tree.name}', so "
                    + "adding it here would close a loop.", "OK");
                return;
            }

            var group = StateTreeEditorOps.BeginUndoGroup("Add Sub Tree Task");
            StateTreeEditorOps.CreateSubTreeTask(m_Tree, m_Node, subTree, "Add Sub Tree Task");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            DeferStructuralChange();
        }

        private bool CanRunSubTree(StateTreeAsset candidate)
        {
            return candidate != null && !StateTreeEditorOps.CreatesCycle(candidate, m_Tree);
        }

        private void ClearSubTree(RunSubTreeTask task)
        {
            if (task == null || m_Tree == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup("Clear Sub Tree");
            Undo.RecordObject(task, "Clear Sub Tree");
            task.subTree = null;
            EditorUtility.SetDirty(task);
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            DeferStructuralChange();
        }

        /// <summary>Follow a composite task into the tree it runs. Deferred, because loading a
        /// different tree rebuilds this pane — including the button that is still dispatching the
        /// click.</summary>
        private void OpenTree(StateTreeAsset tree)
        {
            if (tree == null)
                return;

            m_Root.schedule.Execute(() => StateTreeEditorWindow.Open(tree)).ExecuteLater(0);
        }

        // --- graph tasks ------------------------------------------------------------------

        /// <summary>Name a task, and get back a graph you are already editing and a state that
        /// already runs it — the SUB-TREE flavour, whose graph is states wired together. The order
        /// is deliberate and each step guards the next: the frontend is asked whether it can
        /// author BEFORE the author is asked for a name, the scaffold is verified to have imported
        /// BEFORE anything is wired, and the wiring is one undo step that leaves the file on disk
        /// if undone (asset creation is not undoable — established in m7b, and the reason the
        /// dialog says "created" before it says "added").</summary>
        private void CreateSubTreeTaskGraph()
        {
            if (m_Tree == null || m_Node == null)
                return;

            if (!StateTreeGraphBridge.TryResolveAuthoring(out var error))
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle, error, "OK");
                return;
            }

            StateTreeGraphBridge.EnsureFolder(StateTreeGraphBridge.TaskFolder);

            var path = EditorUtility.SaveFilePanelInProject("New Sub-Tree Task",
                SuggestedTaskName(), StateTreeGraphBridge.graphExtension,
                "Name the task. It is created as a graph of STATES you can extend on the canvas, "
                + "and added to this state straight away.", StateTreeGraphBridge.TaskFolder);
            if (string.IsNullOrEmpty(path))
                return;

            var treeName = System.IO.Path.GetFileNameWithoutExtension(path);
            var created = StateTreeGraphBridge.CreateTaskScaffold(path, treeName, out error);
            if (created == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    "The task graph was not created, and nothing was added to this state.\n\n"
                    + error, "OK");
                return;
            }

            var tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(created);
            if (tree == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{created}' was created, but it did not bake a {nameof(StateTreeAsset)}, so "
                    + "there is nothing for a task to run. Nothing was added to this state — the "
                    + "Console will say why the import failed.", "OK");
                return;
            }

            var group = StateTreeEditorOps.BeginUndoGroup("Add Sub-Tree Task");
            var task = StateTreeEditorOps.CreateSubTreeTask(m_Tree, m_Node, tree,
                "Add Sub-Tree Task");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            if (task == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{created}' was created, but it could not be added to this state. Add it "
                    + "with Add Task… once you have looked at it.", "OK");
            }
            else if (!StateTreeEditorOps.IsTaskTree(tree))
            {
                // The kind is authored ON the graph (the Entry node's Tree Kind port), so a
                // scaffold that came back with any other kind still runs here but is invisible to
                // every other state's picker — which is exactly the promise this command made.
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{created}' baked with tree kind '{tree.treeKind}' rather than "
                    + $"'{StateTreeEditorOps.TaskTreeKind}'. It runs on this state, but it will "
                    + "not be offered in other states' Add Task… picker until the graph's Entry "
                    + $"node sets Tree Kind to '{StateTreeEditorOps.TaskTreeKind}'.", "OK");
            }

            OpenGraphOrReport(created);
            DeferStructuralChange();
        }

        /// <summary>The other flavour: name a task, get a LOGIC graph you are already editing and a
        /// state that already runs it. Same guard order as the sub-tree flavour — can the frontend
        /// author, did the scaffold import, only then wire — because the failure everyone regrets
        /// is a state left pointing at something that does not exist.</summary>
        private void CreateTaskGraph()
        {
            if (m_Tree == null || m_Node == null)
                return;

            if (!StateTreeGraphBridge.TryResolveTaskGraphAuthoring(out var error))
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle, error, "OK");
                return;
            }

            StateTreeGraphBridge.EnsureFolder(StateTreeGraphBridge.TaskFolder);

            var path = EditorUtility.SaveFilePanelInProject("New Task Graph", SuggestedTaskName(),
                StateTreeGraphBridge.taskGraphExtension,
                "Name the task. It is created as a LOGIC graph you can extend on the canvas, and a "
                + "copy of it is added to this state straight away.",
                StateTreeGraphBridge.TaskFolder);
            if (string.IsNullOrEmpty(path))
                return;

            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var created = StateTreeGraphBridge.CreateTaskGraphScaffold(path, name, out error);
            if (created == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    "The task graph was not created, and nothing was added to this state.\n\n"
                    + error, "OK");
                return;
            }

            var program = AssetDatabase.LoadMainAssetAtPath(created) as GraphTaskAsset;
            if (program == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{created}' was created, but its main asset is not a "
                    + $"{nameof(GraphTaskAsset)}, so there is no program for a task to run. "
                    + "Nothing was added to this state — the Console will say why the import "
                    + "failed.", "OK");
                return;
            }

            var group = StateTreeEditorOps.BeginUndoGroup("Add Task Graph");
            var task = StateTreeEditorOps.CreateGraphTaskReference(m_Tree, m_Node, program, -1,
                "Add Task Graph");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            if (task == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{created}' was created, but it could not be added to this state. Add it "
                    + "with Add Task… once you have looked at it.", "OK");
            }

            OpenGraphOrReport(created);
            DeferStructuralChange();
        }

        /// <summary>The picker handed over an authored LOGIC graph. The state gets a wrapper
        /// pointing at it — same undo shape as <see cref="AddTask"/> and
        /// <see cref="AddSubTreeTask"/>, one gesture, one step.</summary>
        private void AddGraphTask(GraphTaskAsset graph)
        {
            if (graph == null || m_Tree == null || m_Node == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup("Add Task Graph");
            var task = StateTreeEditorOps.CreateGraphTaskReference(m_Tree, m_Node, graph, -1,
                "Add Task Graph");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            if (task == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    "That logic graph could not be added to this state.", "OK");
            }

            DeferStructuralChange();
        }

        /// <summary>Re-author a hand-built tree as a graph and follow it. The source asset is left
        /// untouched on disk — a conversion that also deleted the original would be the one
        /// destructive command in this window, and the round-trip it depends on is exactly the
        /// thing the author has not verified yet. Only THIS task is re-pointed for the same
        /// reason: rewriting every other reference in the project is not a decision one composite
        /// row is entitled to make, and the dialog says so before anything happens.</summary>
        private void ConvertToGraph(RunSubTreeTask task)
        {
            if (m_Tree == null || task == null || task.subTree == null)
                return;

            if (!StateTreeGraphBridge.TryResolveAuthoring(out var error))
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle, error, "OK");
                return;
            }

            var source = task.subTree;
            var sourceName = StateTreeEditorOps.TreeDisplayName(source);
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{sourceName}' is not saved as its own asset, so there is no tree file to "
                    + "convert.", "OK");
                return;
            }

            // Beside the source, same name, graph extension — and uniqued, because a previous
            // conversion sitting there is a file someone may still be editing.
            var target = AssetDatabase.GenerateUniqueAssetPath(DirectoryOf(sourcePath) + "/"
                + System.IO.Path.GetFileNameWithoutExtension(sourcePath) + "."
                + StateTreeGraphBridge.graphExtension);

            if (!EditorUtility.DisplayDialog("Convert to Graph",
                $"Write '{sourceName}' out as a graph at\n\n{target}\n\nThis task is then "
                + "re-pointed at the tree that graph bakes (one undo step), and the graph opens.\n\n"
                + $"'{sourcePath}' is left exactly as it is — check the graph, then delete the old "
                + "asset yourself. Anything else referencing it keeps pointing at it.",
                "Convert", "Cancel"))
                return;

            var created = StateTreeGraphBridge.ConvertTreeToGraph(source, target, out error);
            if (created == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{sourceName}' was not converted, and this task is unchanged.\n\n" + error,
                    "OK");
                return;
            }

            var converted = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(created);
            if (converted == null)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"'{created}' was written, but it did not bake a {nameof(StateTreeAsset)}. "
                    + "This task still runs the original tree — the Console will say why the "
                    + "import failed.", "OK");
                return;
            }

            var group = StateTreeEditorOps.BeginUndoGroup("Convert Sub Tree To Graph");
            var repointed = StateTreeEditorOps.RepointSubTreeTask(m_Tree, task, converted,
                "Convert Sub Tree To Graph");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            if (!repointed)
            {
                EditorUtility.DisplayDialog(k_GraphDialogTitle,
                    $"The graph was written to '{created}', but this task still runs the original "
                    + "tree: pointing it at the graph would close a composition loop. The graph is "
                    + "on disk and can be assigned by hand once the loop is gone.", "OK");
            }

            OpenGraphOrReport(created);
            DeferStructuralChange();
        }

        /// <summary>Open a graph file, or say why it did not. The two causes need different fixes
        /// (the frontend assembly is not loaded / nothing is imported at that path) and the bridge
        /// knows which applies, so this never invents a third.</summary>
        private void OpenGraphOrReport(string assetPath)
        {
            if (StateTreeGraphBridge.OpenGraphAsset(assetPath, out var error))
                return;

            EditorUtility.DisplayDialog(k_GraphDialogTitle,
                $"'{assetPath}' did not open.\n\n{error}", "OK");
        }

        /// <summary>Default file name for a new graph task: the state it is being added to, which
        /// is what the author just named and the closest thing to what the task is for.</summary>
        private string SuggestedTaskName()
        {
            var id = m_Node != null ? m_Node.nodeId : null;
            if (string.IsNullOrWhiteSpace(id))
                return "NewTask";

            var trimmed = id.Trim().Replace(" ", string.Empty);
            if (trimmed.Length == 0)
                return "NewTask";

            var name = char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
            return name.EndsWith("Task", StringComparison.Ordinal) ? name : name + "Task";
        }

        private static string DirectoryOf(string assetPath)
        {
            var directory = System.IO.Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(directory) ? "Assets" : directory.Replace('\\', '/');
        }

        /// <summary>Swap (or clear) the condition on one transition. Same Ops call the dropdown
        /// made — the no-op guard matters because the picker can legitimately re-pick the type
        /// that is already there, and re-creating the sub-asset would silently reset its
        /// parameters.</summary>
        private void SetCondition(StateTreeTransition transition, Type type)
        {
            if (m_Tree == null || m_Node == null || transition == null)
                return;

            var current = transition.condition != null ? transition.condition.GetType() : null;
            if (current == type)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup("Set Transition Condition");
            StateTreeEditorOps.SetTransitionCondition(m_Tree, m_Node, transition, type,
                "Set Transition Condition");
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
