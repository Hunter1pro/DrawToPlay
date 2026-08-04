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
    /// AND THE WRAPPER IS WHERE THE CALLEE GETS TUNED. A logic graph's variables are its PARAMETERS,
    /// so the graph-task box grows a Parameters section: one row per variable, each an override
    /// checkbox beside a value field that shows the graph's own default while unticked. That is the
    /// Blueprint instance model — the graph is the class, this state's task is an instance — and it
    /// is what stops "the same behaviour but faster" from meaning a second graph file. The override
    /// list is stored on the wrapper (per state, per use), never on the shared callee, and the raw
    /// list is hidden from the generic field block below because a nameless array of structs cannot
    /// show a default, cannot catch a typo'd name, and would be a second way to edit the same data.
    ///
    /// A SUB-TREE IS TUNED THE SAME WAY, THROUGH THE SAME ROWS. A nested tree's blackboard keys are
    /// its parameters just as a graph's variables are, so the composite box renders through the one
    /// row builder the graph box uses — <see cref="BuildParameterOverrides"/> over a
    /// <see cref="ParameterSurface"/> that says where the declarations come from and where the
    /// overrides go. The two authored kinds differ in exactly the three things that description
    /// carries (the noun, the stale-name advice, and whether "declared but nothing reads it" is a
    /// question that can be answered honestly), which is the argument for one code path: a checkbox
    /// that behaves differently in the purple box than in the teal one is a bug the author has to
    /// discover, and a second copy of this logic is where that bug comes from.
    ///
    /// With no state selected the pane edits the TREE: its name, its kind, and the toggle that makes
    /// it appear in every other tree's task picker. Those fields exist nowhere else in the window and
    /// "mark this tree as a task" is the whole entry point to composition.
    ///
    /// THE TREE'S OWN PARAMETER DECLARATION IS NOT ONE OF THEM — it is above everything, in both
    /// views, from one builder (<see cref="BuildTreeParameters"/>) mounted twice. Declaring a
    /// parameter is not a tree-settings chore done once before the states exist; it is what an author
    /// reaches for WHILE wiring a state — "this number wants to be tunable" — and a list reachable
    /// only by deselecting everything is a list that gets skipped, with the raw ScriptableObject
    /// inspector as the fallback. It is a foldout so the cost of that promotion is one row when the
    /// author is working on something else, and the open/closed choice is remembered for the session.
    ///
    /// AN OVERRIDE BINDS TO A PARAMETER'S ID, NEVER TO ITS NAME. A declaration carries a GUID minted
    /// when the row is added (a baked graph's carries the identity of the variable it came from), and
    /// the rows above, both interpreters and the stale detection all resolve through it. That is what
    /// makes RENAMING a parameter safe: every state that overrides it follows automatically, because
    /// none of them ever knew the name. What does NOT follow is the other half — the name IS the
    /// blackboard key, so the tasks and conditions INSIDE the tree read it as authored text — which is
    /// why a rename offers to retarget those reads and reports how many it found, rather than doing it
    /// silently (a value match can be a coincidence) or not at all (a rename that half-works is worse
    /// than one that refuses).
    ///
    /// Rows that predate ids are healed on sight: drawing a declaration with an empty id stamps one,
    /// and drawing an override with an empty id adopts the id of the one declaration that carries its
    /// name. Both write without an undo entry, because neither is something the author did — it is the
    /// asset catching up with the model the first time it is opened, after which the name is decoration.
    ///
    /// AND A DECLARED PARAMETER CAN BE WIRED TO A FIELD. Declaring "speed" is only half the story: the
    /// tasks that should USE it are edited two sections below, and until M7i the only way to connect
    /// the two was to type the parameter's name into whichever field happened to be a blackboard key —
    /// which works for the handful of library components that read the blackboard and not at all for
    /// the plain <c>float damage</c> of everything else. So every bindable field of a task or condition
    /// grows a LINK control (<see cref="BuildBindableField"/>): pick a parameter and the field is
    /// written from it when the tree starts, the literal beside it disabled and replaced by
    /// "← &lt;name&gt;" so what actually runs is never ambiguous. The popup lists NAMES and stores IDS,
    /// same as everything else here, which is why renaming a parameter leaves every link intact.
    ///
    /// A link is a row on the STATE, addressed by the target's position in the task or transition list,
    /// and that is the one thing this window cannot let drift: the rows are renumbered by
    /// <see cref="StateTreeEditorOps"/> inside the same mutations that move those positions, so
    /// deleting a task takes its links with it rather than handing them to the next task along.
    ///
    /// THE OVERRIDE ROWS TAKE THE SAME CONTROL, one level up. A state that runs a sub-tree or a graph
    /// can pin a parameter to a literal (the checkbox) or PASS ITS OWN THROUGH — this tree declares
    /// "speed", the callee declares "speed", and the link says they are the same knob rather than two
    /// numbers that have to be kept equal by hand. Stale links on either control read exactly like a
    /// stale override: a warning that says what broke, what runs instead, and offers the one gesture
    /// that clears it.
    ///
    /// AND VALUES COME BACK. Everything above is the way IN — a parameter reaching a field before the
    /// tree runs. A task that has finished also has something to say (how much damage it dealt, what
    /// it found, how long it took), and until M7j the only way to carry that to the next state was for
    /// the task to write a blackboard key it hard-codes, which makes the key an implementation detail
    /// two classes have to agree on in silence. So a task DECLARES its outputs — <c>[TaskOutput]</c>
    /// fields on a C# task, Set Output nodes in a graph — and the TRANSITION chooses which of them to
    /// carry, in a "Route outputs" foldout at the bottom of each transition box: source task, output,
    /// and the blackboard key it lands under. The transition is the right owner because leaving a
    /// state is exactly the moment the answer exists and the next state does not yet: routing is what
    /// a function's <c>return</c> statement does, written where the jump is.
    ///
    /// A ROUTE IS NAME-KEYED, not id-keyed — the one place in this window that is. That is deliberate
    /// and it is the difference between a knob and a contract: a parameter is tuned by whoever calls
    /// you and may be renamed freely because nothing outside points at it, while an output is what
    /// you PROMISE, so renaming one is a breaking change and has to read as one. The inspector shows
    /// an unrecognised name as stale rather than repairing it, and says why.
    ///
    /// Routes address their source task by POSITION, exactly like the parameter links, and are
    /// renumbered by the same file for the same reason — with a shorter list of sites, because a
    /// route rides on the transition it belongs to and only the TASK list can move beneath it.
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

        /// <summary>What a route with no output picked shows. The same angle-bracket vocabulary as
        /// <see cref="k_NoTargetChoice"/>, because it means the same thing: a slot the author has
        /// not filled in yet, which the runner will skip.</summary>
        private const string k_NoOutputChoice = "<none>";

        private const string k_SubTreeProperty = "subTree";

        /// <summary>The wrapper field an author can re-point by dropping a graph on it — the one
        /// property change that has to rebuild the row, because the row is named after it.</summary>
        private const string k_GraphProperty = "graph";

        /// <summary>The wrapper's per-state parameter overrides — the field name is the same on both
        /// wrappers, which is what lets one constant hide it on both. Drawn by
        /// <see cref="BuildParameterOverrides"/> and hidden from the generic field block, which would
        /// otherwise show the same data twice as a nameless array of structs.</summary>
        private const string k_OverridesProperty = "overrides";

        private const string k_SetOverrideUndo = "Override Task Parameter";
        private const string k_ClearOverrideUndo = "Clear Task Parameter Override";
        private const string k_EditOverrideUndo = "Set Task Parameter";

        /// <summary>Linking a field and linking an override row are separate gestures on separate
        /// assets — the node for one, the task wrapper for the other — so they get separate undo
        /// labels rather than one "Link Parameter" that says nothing about what comes back.</summary>
        private const string k_LinkFieldUndo = "Link Field To Parameter";

        private const string k_UnlinkFieldUndo = "Unlink Field";
        private const string k_LinkSourceUndo = "Link Parameter To Tree Parameter";
        private const string k_UnlinkSourceUndo = "Unlink Parameter";

        /// <summary>Routing is its own gesture on its own list, so it gets its own labels rather
        /// than riding on the transition's — "undo Add Route" and "undo Add Transition" are two
        /// different amounts of work to lose.</summary>
        private const string k_AddRouteUndo = "Add Output Route";

        private const string k_RemoveRouteUndo = "Remove Output Route";
        private const string k_EditRouteUndo = "Edit Output Route";

        /// <summary>What a bound row shows in place of its value. An arrow rather than a word
        /// because it says the direction: the value comes FROM there, and this field is no longer
        /// where it is decided.</summary>
        private const string k_BoundPrefix = "← ";

        /// <summary>Text of the control that opens the parameter popup. Not a glyph: every other
        /// symbol button in this window (✕, ▲, ▼) means something an author can guess, and "bind
        /// this to a parameter" is not one of those.</summary>
        private const string k_LinkLabel = "Link";

        /// <summary>Click ergonomics for the override/link rows (user feedback: the bare
        /// checkbox and glyph buttons were hard to hit). One place sets the minimum
        /// target sizes; the row label also toggles, giving the checkbox a text-sized
        /// hit area like every built-in Unity toggle row.</summary>
        private const float k_RowMinHeight = 22f;
        private const float k_ControlMinHeight = 20f;
        private const float k_LinkMinWidth = 52f;

        private static void EnlargeToggle(Toggle toggle)
        {
            toggle.style.minWidth = 18f;
            toggle.style.minHeight = k_ControlMinHeight;
            toggle.style.justifyContent = Justify.Center;
            toggle.style.marginRight = 4f;
            toggle.style.paddingLeft = 2f;
            toggle.style.paddingRight = 2f;
        }

        private static void EnlargeRowButton(Button button, float minWidth)
        {
            button.style.minWidth = minWidth;
            button.style.minHeight = k_ControlMinHeight;
            button.style.paddingLeft = 6f;
            button.style.paddingRight = 6f;
            button.style.marginLeft = 4f;
        }

        /// <summary>Make a row label flip its toggle — the label is the biggest thing on
        /// the row, so it becomes the easy target.</summary>
        private static void LabelTogglesValue(Label label, Toggle toggle)
        {
            label.RegisterCallback<ClickEvent>(_ =>
            {
                if (toggle.enabledSelf)
                    toggle.value = !toggle.value;
            });
        }

        /// <summary><see cref="GenericMenu"/> reads '/' as a submenu separator with no way to
        /// escape it, so a parameter named "move/speed" would silently become a submenu called
        /// "move" — this is substituted into the LABEL only (the id is what gets stored), which
        /// keeps the row pickable and readable.</summary>
        private const char k_MenuSeparatorStandIn = '∕';

        private const string k_AddParameterUndo = "Declare Tree Parameter";
        private const string k_RemoveParameterUndo = "Remove Tree Parameter";
        private const string k_EditParameterUndo = "Edit Tree Parameter";

        /// <summary>Its own undo label rather than <see cref="k_EditParameterUndo"/>: a rename can
        /// carry a retarget across every task and condition in the tree, and one gesture that
        /// rewrites twenty sub-assets should say what it was in the undo history.</summary>
        private const string k_RenameParameterUndo = "Rename Tree Parameter";

        /// <summary>Title of the rename dialog — the one place this window asks a question whose
        /// "no" is a perfectly good answer.</summary>
        private const string k_RenameDialogTitle = "Rename Parameter";

        /// <summary>Kind choices in ENUM ORDER, so a dropdown's index IS the kind — the same
        /// index-addressing the transition target picker uses, and for the same reason: matching a
        /// label back to a value breaks the day someone rewords a label. They are the words the
        /// tooltips use too, so the vocabulary is declared once
        /// (<see cref="KindLabel"/> reads this array).</summary>
        private static readonly string[] k_ParameterKindChoices = { "number", "text", "checkbox" };

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

        /// <summary>Whether the declaration foldout is open. Remembered on the pane rather than on
        /// the element, because the pane is rebuilt from scratch on every structural edit and a
        /// foldout that sprang back open each time a state was added would be worse than one that
        /// could not be closed at all. Open by default: a declaration nobody can see is the problem
        /// this section exists to fix.</summary>
        private bool m_ParametersOpen = true;

        /// <summary>Which transitions' "Route outputs" foldouts the author has opened, by transition
        /// index. Remembered on the pane for the same reason <see cref="m_ParametersOpen"/> is —
        /// adding a route rebuilds the pane, and a foldout that closed itself each time would make
        /// the second route harder to add than the first.
        ///
        /// Keyed by INDEX rather than by identity, so reordering transitions carries the open state
        /// to whatever now sits at that position. That is a display nicety and nothing else: unlike
        /// the route rows themselves, which <see cref="StateTreeEditorOps"/> renumbers, a foldout
        /// opening one box too far costs a click. Closed by default, opened by content: a transition
        /// that already routes something shows it without being asked.</summary>
        private readonly HashSet<int> m_OpenRouteFoldouts = new HashSet<int>();

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

            // Above the state, not below it: the declaration belongs to the TREE, and a section that
            // followed the state's own tasks would read as part of that state.
            m_Root.Add(BuildTreeParameters());

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

            m_Root.Add(BuildTreeParameters());

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

        // --- tree parameters: the declaration ---------------------------------------------

        /// <summary>
        /// The tree's OWN parameter list — the declaration every override row in this window is
        /// drawn against, and, for a tree run as a sub-tree task, its BLACKBOARD CONTRACT: each name
        /// is a key the tree's tasks and conditions read, seeded to the effective value every time a
        /// parent state enters it.
        ///
        /// Shown for EVERY tree, not only the ones marked reusable, for two reasons. A root tree
        /// never runs as a task and still reads ambient keys; writing them down here is the only
        /// place this toolset offers to say what a tree expects, which is worth more than the rows
        /// it costs. And a tree is usually parameterised BEFORE anyone decides it is reusable — a
        /// list that appears only after the toggle is flipped is a list nobody fills in.
        ///
        /// A row is name, kind and default, in that order, because that is the order the author
        /// thinks in: the name is the contract, the kind decides what the default field even is.
        ///
        /// ONE BUILDER, TWO MOUNTS — the tree-settings view and the top of every state's view. It is
        /// a <see cref="Foldout"/> rather than a section because of the second mount: with a state
        /// selected this is context above the subject, so it has to be collapsible, and the choice is
        /// remembered on the pane (<see cref="m_ParametersOpen"/>) because the pane is rebuilt on
        /// every edit. The title carries the tree's name for the same reason — sitting above a state's
        /// own sections, "Parameters" alone would read as that state's.
        /// </summary>
        /// <returns>The foldout, to be mounted by the caller.</returns>
        private VisualElement BuildTreeParameters()
        {
            HealDeclarationIds(m_Tree);

            var parameters = m_Tree.parameters;
            var count = parameters != null ? parameters.Count : 0;

            var foldout = new Foldout
            {
                text = $"Parameters · {StateTreeEditorOps.TreeDisplayName(m_Tree)} ({count})",
                value = m_ParametersOpen
            };
            foldout.style.marginTop = 6f;
            foldout.style.marginBottom = 4f;

            // Every Toggle inside this foldout — the checkbox kind's default field — sends a
            // ChangeEvent<bool> that bubbles right through here, so the foldout's own state is the
            // only one this listens to.
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                    m_ParametersOpen = evt.newValue;
            });

            var note = Hint(count == 0
                ? "None declared. A parameter is a blackboard key this tree reads, with the default "
                + "it takes when nobody overrides it — declare one and every state that runs this "
                + "tree as a task can tune it there."
                : "Each row is a blackboard key this tree reads. A state that runs this tree as a "
                + "task sees these as its parameters and may override any of them; the effective "
                + "value is written to the shared blackboard every time that state is entered.");
            note.style.marginBottom = 4f;
            foldout.Add(note);

            for (var i = 0; i < count; ++i)
            {
                if (parameters[i] != null)
                    foldout.Add(BuildDeclarationRow(i));
            }

            var add = new Button(AddTreeParameter) { text = "Add Parameter" };
            add.style.marginTop = 4f;
            add.tooltip = "Declare a blackboard key this tree reads, with the default it takes when "
                + "nothing overrides it.";
            foldout.Add(add);

            return foldout;
        }

        /// <summary>One declared parameter. The refusal box below the row is why this returns a
        /// container rather than the row itself: a rejected name has to be explained WHERE it was
        /// typed, and a dialog for something the author fixes by typing again would be four clicks
        /// of ceremony around one keystroke.</summary>
        private VisualElement BuildDeclarationRow(int index)
        {
            var parameter = m_Tree.parameters[index];

            var container = new VisualElement();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            container.Add(row);

            var refusal = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            refusal.style.display = DisplayStyle.None;
            container.Add(refusal);

            // Persistent, unlike the refusal above: a locked name is ACCEPTED and then keeps being
            // wrong, so its warning has to stay on screen rather than flash once on commit.
            var reserved = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            container.Add(reserved);
            UpdateReservedWarning(reserved, parameter.name);

            var name = new TextField
            {
                value = parameter.name ?? string.Empty,
                isDelayed = true
            };
            name.style.flexGrow = 1f;
            name.style.marginRight = 2f;
            name.tooltip = "The blackboard key. States that override this parameter are bound to it "
                + "by id and follow a rename by themselves; the tasks and conditions INSIDE this tree "
                + "read the key as text, so renaming offers to retarget those.";
            name.RegisterValueChangedCallback(
                evt => RenameTreeParameter(index, evt.newValue, name, refusal, reserved));
            row.Add(name);

            var kind = new DropdownField(new List<string>(k_ParameterKindChoices),
                (int)parameter.kind);
            kind.style.width = 92f;
            kind.style.flexShrink = 0f;
            kind.tooltip = "What the key holds. A checkbox rides in the same field as a number, so "
                + "switching between those two keeps the value.";
            kind.RegisterValueChangedCallback(evt => SetTreeParameterKind(index, kind.index,
                container));
            row.Add(kind);

            var value = BuildDeclarationValue(index, parameter);
            value.style.width = 112f;
            value.style.flexShrink = 0f;
            value.style.marginLeft = 2f;
            value.tooltip = "The value this key takes when the calling state does not override it.";
            row.Add(value);

            var remove = new Button(() => RemoveTreeParameter(index)) { text = "✕" };
            remove.tooltip = "Undeclare this parameter. States that override it keep their row as a "
                + "stale warning until it is removed there too — deleting it here cannot reach "
                + "them.";
            remove.style.width = 22f;
            remove.style.flexShrink = 0f;
            row.Add(remove);

            return container;
        }

        /// <summary>The default editor for one declared kind, writing straight into the declaration
        /// on the tree asset.</summary>
        private VisualElement BuildDeclarationValue(int index, GraphTaskParameter parameter)
        {
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                {
                    var field = new TextField
                    {
                        value = parameter.stringValue ?? string.Empty,
                        isDelayed = true
                    };
                    field.RegisterValueChangedCallback(evt => CommitDeclaration(index,
                        entry => entry.stringValue = evt.newValue ?? string.Empty));
                    return field;
                }

                case GraphTaskParameterKind.Bool:
                {
                    var field = new Toggle { value = parameter.floatValue != 0f };
                    field.RegisterValueChangedCallback(evt => CommitDeclaration(index,
                        entry => entry.floatValue = evt.newValue ? 1f : 0f));
                    return field;
                }

                default:
                {
                    var field = new FloatField { value = parameter.floatValue, isDelayed = true };
                    field.RegisterValueChangedCallback(evt => CommitDeclaration(index,
                        entry => entry.floatValue = evt.newValue));
                    return field;
                }
            }
        }

        /// <summary>
        /// Rename a declared parameter, or REFUSE and say why. Names are not uniqued silently the
        /// way node ids are, and the difference matters: a node id is an editor-side handle this
        /// window rewires on rename, while a parameter name is a blackboard key baked into whatever
        /// reads it. "speed" quietly becoming "speed 1" would leave the tree reading a key nothing
        /// writes — a rename the author cannot see is worse than a rename that did not happen.
        ///
        /// A blank name is refused for the same reason rather than treated as "unnamed": the empty
        /// string is a perfectly valid dictionary key, so a blank row would seed one and every other
        /// blank row would collide with it.
        ///
        /// THE ID IS NOT TOUCHED, which is the whole point of having one: every state that overrides
        /// this parameter — in this tree or any other, opened or not — keeps overriding it, because
        /// none of them resolved by name in the first place. What a rename DOES strand is the readers
        /// inside this tree, whose blackboard key is authored text, so the author is offered the
        /// retarget and told how many fields it would rewrite. Declining leaves the tree reading the
        /// old key, which is occasionally exactly right (the parameter was renamed because it now
        /// means something else) and is in any case the author's call to make, not this window's.
        /// </summary>
        private void RenameTreeParameter(int index, string requested, TextField field,
            HelpBox refusal, HelpBox reserved)
        {
            if (!TryGetDeclaration(index, out var entry))
                return;

            var current = entry.name ?? string.Empty;
            var trimmed = (requested ?? string.Empty).Trim();

            if (string.Equals(trimmed, current, StringComparison.Ordinal))
            {
                Refuse(refusal, null);
                field.SetValueWithoutNotify(current);
                return;
            }

            if (trimmed.Length == 0)
            {
                Refuse(refusal, "A parameter needs a name: the name IS the blackboard key.");
                field.SetValueWithoutNotify(current);
                return;
            }

            if (DeclaresName(trimmed, index))
            {
                Refuse(refusal, $"'{trimmed}' is already declared by this tree. Two parameters "
                    + "sharing a name are one blackboard key, and whichever is seeded last would "
                    + "silently win.");
                field.SetValueWithoutNotify(current);
                return;
            }

            Refuse(refusal, null);

            // One gesture, one undo step — including the retarget, which is part of the same answer
            // to the same question and would be maddening to undo separately.
            var group = StateTreeEditorOps.BeginUndoGroup(k_RenameParameterUndo);
            Undo.RecordObject(m_Tree, k_RenameParameterUndo);
            entry.name = trimmed;
            EditorUtility.SetDirty(m_Tree);

            OfferRetarget(current, trimmed);

            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();

            field.SetValueWithoutNotify(trimmed);
            UpdateReservedWarning(reserved, trimmed);
        }

        /// <summary>
        /// Ask whether the tree's own readers should follow the rename, and rewrite them if so. Asked
        /// only when there is something to ask about: a parameter nothing in this tree reads is the
        /// common case for a tree that exists to be CALLED, and a dialog that says "0 fields found"
        /// is a dialog that trains authors to dismiss dialogs.
        ///
        /// The count is quoted before the fact rather than reported after, because it is the whole
        /// basis for the decision — matching is by value, so the number includes any text field that
        /// happens to hold the old name, and an author who sees "7" where they expected "2" should be
        /// able to say no.
        /// </summary>
        private void OfferRetarget(string oldName, string newName)
        {
            var reads = StateTreeEditorOps.CountBlackboardReads(m_Tree, oldName);
            if (reads <= 0)
                return;

            var message = $"'{oldName}' is now '{newName}'.\n\nStates that override this parameter "
                + "follow the rename by themselves — they are bound to it by id. The tasks and "
                + "conditions inside this tree are not: the name IS the blackboard key, and they hold "
                + "it as text.\n\n"
                + $"{reads} field(s) in this tree hold exactly '{oldName}'. Retarget them to "
                + $"'{newName}'?\n\nMatching is by value, so a field holding that text for an "
                + "unrelated reason is included too. Logic graphs this tree runs are separate assets "
                + "and are never touched. The rename and the retarget are one undo step.";

            if (!EditorUtility.DisplayDialog(k_RenameDialogTitle, message, "Retarget", "Rename Only"))
                return;

            StateTreeEditorOps.RetargetBlackboardReads(m_Tree, oldName, newName,
                k_RenameParameterUndo);
        }

        /// <summary>
        /// The library's four cross-component blackboard keys. Declaring one as a parameter is legal
        /// and occasionally deliberate — a sub-tree that really does want to set the caller's move
        /// speed for the duration — so this WARNS rather than refuses. What it must not do is stay
        /// quiet: seeding writes the key on every activation and v1 neither saves nor restores what
        /// was there, so a tree that declares "target" silently replaces the caller's target the
        /// first time its state is entered, and never puts it back.
        ///
        /// <see cref="StateTreeLibraryUtil.TargetKey"/> gets an extra sentence because it fails
        /// worse than the other three: it holds a GameObject, and a declared parameter can only seed
        /// a number, a checkbox or text, so the readers do not get a wrong target — they get
        /// something that is not a target at all.
        /// </summary>
        private static void UpdateReservedWarning(HelpBox box, string name)
        {
            string extra;
            if (string.Equals(name, StateTreeLibraryUtil.TargetKey, StringComparison.Ordinal))
            {
                extra = " That key holds a GameObject, and a parameter can only seed a number, a "
                    + "checkbox or text — every task that reads the target would find one of those "
                    + "instead.";
            }
            else if (string.Equals(name, StateTreeLibraryUtil.MoveSpeedKey, StringComparison.Ordinal)
                || string.Equals(name, StateTreeLibraryUtil.AttackRangeKey, StringComparison.Ordinal)
                || string.Equals(name, StateTreeLibraryUtil.DetectRangeKey, StringComparison.Ordinal))
            {
                extra = string.Empty;
            }
            else
            {
                box.style.display = DisplayStyle.None;
                return;
            }

            box.text = $"'{name}' is one of the library's locked blackboard keys. Every entry of a "
                + "state that runs this tree overwrites whatever the caller had there, and nothing "
                + "puts it back when the state exits." + extra
                + " Rename it unless taking the key over is the intent.";
            box.style.display = DisplayStyle.Flex;
        }

        private static void Refuse(HelpBox box, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                box.style.display = DisplayStyle.None;
                return;
            }

            box.text = message;
            box.style.display = DisplayStyle.Flex;
        }

        /// <summary>Whether any OTHER declared parameter already carries this name. Ordinal, because
        /// the blackboard is a <c>Dictionary&lt;string, object&gt;</c> and its comparer is: two names
        /// that differ only in case are two keys, and refusing them as duplicates would refuse
        /// something that works.</summary>
        private bool DeclaresName(string name, int except)
        {
            var parameters = m_Tree != null ? m_Tree.parameters : null;
            if (parameters == null)
                return false;

            for (var i = 0; i < parameters.Count; ++i)
            {
                if (i == except || parameters[i] == null)
                    continue;
                if (string.Equals(parameters[i].name, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void SetTreeParameterKind(int index, int choice, VisualElement row)
        {
            if (choice < 0 || choice >= k_ParameterKindChoices.Length)
                return;

            CommitDeclaration(index, entry => entry.kind = (GraphTaskParameterKind)choice);

            // The default field was built FOR one kind, so it is replaced rather than reinterpreted
            // — and one frame later, because the dropdown that asked for this is still dispatching
            // inside the element about to be destroyed.
            m_Root.schedule.Execute(() => ReplaceDeclarationRow(index, row)).ExecuteLater(0);
        }

        private void ReplaceDeclarationRow(int index, VisualElement row)
        {
            var parent = row.parent;
            if (parent == null || !TryGetDeclaration(index, out _))
                return;

            parent.Insert(parent.IndexOf(row), BuildDeclarationRow(index));
            row.RemoveFromHierarchy();
        }

        private void AddTreeParameter()
        {
            if (m_Tree == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_AddParameterUndo);
            Undo.RecordObject(m_Tree, k_AddParameterUndo);

            if (m_Tree.parameters == null)
                m_Tree.parameters = new List<GraphTaskParameter>();

            m_Tree.parameters.Add(new GraphTaskParameter
            {
                id = NewParameterId(),
                name = UniqueParameterName(),
                kind = GraphTaskParameterKind.Float,
                stringValue = string.Empty
            });

            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        /// <summary>A declaration's identity, minted once when the row is created and never again —
        /// not on rename, not on retype, not on reorder. Everything that binds to a parameter binds
        /// to this, so regenerating it anywhere would silently unbind every state that overrides it.
        /// </summary>
        private static string NewParameterId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Give an id to any declaration that has none — the one-time upgrade for lists authored
        /// before parameters had identity, run when the list is drawn, which is the first moment
        /// anything can go wrong for the lack of one.
        ///
        /// No <see cref="Undo"/> record: the author did not do this, and an undo step for "the file
        /// caught up with the model" would sit in the history looking like a change they could revert
        /// into a broken state. Dirty-only, once, and thereafter the name is decoration.
        ///
        /// Only lists on assets this window OWNS get here — a tree the author opened, and the sub-tree
        /// a composite task points at. A baked graph's declarations are import artifacts: minting ids
        /// into one would be undone by the next reimport, and mint DIFFERENT ids each time, which is
        /// worse than leaving them alone. Those get their ids from the bake.
        /// </summary>
        private static void HealDeclarationIds(StateTreeAsset tree)
        {
            var parameters = tree != null ? tree.parameters : null;
            if (parameters == null)
                return;

            var healed = false;
            for (var i = 0; i < parameters.Count; ++i)
            {
                var entry = parameters[i];
                if (entry == null || !string.IsNullOrEmpty(entry.id))
                    continue;

                entry.id = NewParameterId();
                healed = true;
            }

            if (healed)
                EditorUtility.SetDirty(tree);
        }

        private void RemoveTreeParameter(int index)
        {
            if (!TryGetDeclaration(index, out _))
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_RemoveParameterUndo);
            Undo.RecordObject(m_Tree, k_RemoveParameterUndo);
            m_Tree.parameters.RemoveAt(index);
            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        /// <summary>Every write to one declared row, in one place: record the TREE (the list lives
        /// on the tree asset, not on a sub-asset), mutate, dirty, and tell the window an edit
        /// happened so the batched save timer restarts.</summary>
        private void CommitDeclaration(int index, Action<GraphTaskParameter> write)
        {
            if (!TryGetDeclaration(index, out var entry))
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditParameterUndo);
            Undo.RecordObject(m_Tree, k_EditParameterUndo);
            write(entry);
            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        private bool TryGetDeclaration(int index, out GraphTaskParameter parameter)
        {
            parameter = null;
            var parameters = m_Tree != null ? m_Tree.parameters : null;
            if (parameters == null || index < 0 || index >= parameters.Count)
                return false;

            parameter = parameters[index];
            return parameter != null;
        }

        /// <summary>A name no other row carries. New rows ARE named — an empty one would be refused
        /// by the rename path the moment it was touched, which is a strange way to greet an author
        /// who just pressed Add.</summary>
        private string UniqueParameterName()
        {
            const string stem = "parameter";
            if (!DeclaresName(stem, -1))
                return stem;

            for (var i = 2; i < 1000; ++i)
            {
                var candidate = stem + i;
                if (!DeclaresName(candidate, -1))
                    return candidate;
            }

            return stem + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Redraw the pane in place — for every edit that changes the SHAPE of a section
        /// rather than a value in it: adding or removing a declared parameter, and linking or
        /// unlinking, which turns a plain field into a bound one and back. Deferred, because the
        /// button that asked for it is a child of what is about to be cleared. Whatever is selected
        /// stays selected: these edits are made from both views now, and one that bounced the author
        /// back to the tree view would be a worse surprise than the rows it saves redrawing.
        /// </summary>
        private void RebuildPane()
        {
            m_Edited?.Invoke();
            m_Root.schedule.Execute(() =>
            {
                if (m_Tree != null)
                    Rebuild(m_Tree, m_Node);
            }).ExecuteLater(0);
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

            var bindings = m_Node.bindings;
            for (var i = 0; bindings != null && i < bindings.Count; ++i)
            {
                var problem = DescribeBindingProblem(bindings[i]);
                if (problem != null)
                    problems.Add(problem);
            }

            // Routes are reported here as well as beside the row they belong to, because the row is
            // inside a foldout: a route that silently carries nothing must be visible from the state
            // without the author first guessing which transition to open.
            for (var i = 0; i < m_Node.transitions.Count; ++i)
            {
                var transition = m_Node.transitions[i];
                var routes = transition != null ? transition.outputRoutes : null;
                for (var r = 0; routes != null && r < routes.Count; ++r)
                {
                    var problem = DescribeRouteProblem(routes[r]);
                    if (problem != null)
                        problems.Add($"Transition {i + 1}: {problem}");
                }
            }

            if (problems.Count == 0)
                return;

            m_Root.Add(new HelpBox(string.Join("\n", problems), HelpBoxMessageType.Warning));
        }

        /// <summary>
        /// What is wrong with one parameter link, or null when nothing is. Reported up here as well
        /// as beside the field because the two questions are different: the row beside the field
        /// answers "why is this one not taking my parameter", and this answers "is anything on this
        /// state quietly not running" — which includes the links whose FIELD is gone, and which
        /// therefore have no row to sit beside any more.
        ///
        /// The tests are in the executor's own order, so the first thing that stops the write is the
        /// first thing reported: target, field, parameter, kind. Every one of them is the id-only
        /// staleness of M7h — a link never breaks because something was renamed, only because
        /// something was deleted or retyped.
        /// </summary>
        private string DescribeBindingProblem(StateTreeFieldBinding row)
        {
            if (row == null)
                return "A parameter link row is empty. The runner skips it.";

            var isTask = row.targetKind == StateTreeFieldBinding.TargetKind.Task;
            var slot = isTask
                ? $"task {row.targetIndex + 1}"
                : $"transition {row.targetIndex + 1}'s condition";
            var count = isTask ? m_Node.tasks.Count : m_Node.transitions.Count;
            var field = string.IsNullOrEmpty(row.fieldName) ? "(unnamed field)" : row.fieldName;

            if (row.targetIndex < 0 || row.targetIndex >= count)
            {
                return $"A parameter link targets {slot}, which this state does not have — the "
                    + "task or transition it was made on was deleted. The runner skips it.";
            }

            var target = StateTreeEditorOps.ResolveBindingTarget(m_Node, row.targetKind,
                row.targetIndex);
            if (target == null)
            {
                return $"'{field}' is linked on {slot}, which is empty. The runner skips it.";
            }

            if (!StateTreeEditorOps.TryGetBindableKind(target, field, out var fieldKind))
            {
                return $"'{field}' is linked on {slot}, but {target.GetType().Name} has no "
                    + "bindable field by that name. The runner skips it.";
            }

            var source = StateTreeEditorOps.FindParameterById(m_Tree.parameters, row.parameterId);
            if (source == null)
            {
                return $"'{field}' on {slot} is linked to a parameter this tree no longer declares "
                    + "— it was deleted (a rename would have kept the link). The field's own value "
                    + "runs instead.";
            }

            if (source.kind != fieldKind)
            {
                return $"'{field}' on {slot} is linked to '{source.name}', which is a "
                    + $"{KindLabel(source.kind)} where the field takes a {KindLabel(fieldKind)}. "
                    + "The runner skips it and the field's own value runs instead.";
            }

            return null;
        }

        /// <summary>
        /// What is wrong with one output route, or null when nothing is. Written as a standalone
        /// sentence so the SAME text serves both mounts — the state's validation box prefixes the
        /// transition it belongs to, and the row itself needs no prefix because it is sitting on it.
        ///
        /// The tests are in the order the runtime hits them (task, then output), so the first thing
        /// that stops the write is the first thing reported. The last two are editor-only knowledge
        /// the runtime cannot express as cheaply: a sub-tree task publishes nothing at all in v1, and
        /// an output name the source does not declare is the one failure this model makes possible —
        /// routes are matched BY NAME, so renaming an output is a breaking change, and saying so here
        /// is the whole reason the editor knows what a task publishes.
        /// </summary>
        private string DescribeRouteProblem(TransitionOutputRoute route)
        {
            if (route == null)
                return "a route row is empty. The runner skips it.";

            if (route.taskIndex < 0 || route.taskIndex >= m_Node.tasks.Count)
            {
                return $"a route reads task {route.taskIndex + 1}, which this state does not have "
                    + "— the task it was made on was deleted. The runner skips it.";
            }

            var task = m_Node.tasks[route.taskIndex];
            if (task == null)
            {
                return $"a route reads task slot {route.taskIndex + 1}, which is empty. The runner "
                    + "skips it.";
            }

            if (string.IsNullOrEmpty(route.outputName))
                return "a route has no output picked, so it carries nothing forward.";

            if (task is RunSubTreeTask)
            {
                return $"'{route.outputName}' is routed out of {TaskBoxLabel(task)}, and a sub-tree "
                    + "task publishes no outputs — a nested tree's own states route internally. "
                    + "Nothing is written.";
            }

            // An EMPTY list is not evidence: a graph that has not been re-baked declares nothing
            // either, and calling every route on it stale would be noise the author cannot act on.
            // Only a source that publishes SOMETHING can say a name is not one of them.
            var outputs = StateTreeEditorOps.CollectTaskOutputs(task);
            if (outputs.Count > 0 && !PublishesOutput(outputs, route.outputName))
            {
                return $"'{route.outputName}' is routed out of {TaskBoxLabel(task)}, which does not "
                    + "publish it. Outputs are matched by NAME, so renaming one breaks every route "
                    + "that carried it. The runner warns once and skips it.";
            }

            return null;
        }

        private static bool PublishesOutput(List<TaskOutputValue> outputs, string name)
        {
            for (var i = 0; i < outputs.Count; ++i)
            {
                if (string.Equals(outputs[i].name, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
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

                var label = new Label(TaskBoxLabel(task));
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
                {
                    box.Add(BuildSubTreeStatus(composite));

                    // Nothing assigned means nothing DECLARED, and every override on the task
                    // becomes valid again the moment a tree is picked — calling them all stale
                    // while the slot is empty would be noise AND wrong.
                    if (composite.subTree != null)
                        box.Add(BuildParameterOverrides(SurfaceOf(composite)));
                }

                if (program != null)
                {
                    box.Add(BuildProgramStatus(program));
                    if (program.graph != null)
                        box.Add(BuildParameterOverrides(SurfaceOf(program)));
                }

                // The override list is drawn as the Parameters section above — as checkboxes against
                // the callee's own declaration, which is the only place the NAMES are known — so the
                // raw list is hidden rather than drawn twice with one of the two unable to show a
                // default or catch a typo'd name.
                var fields = BuildParameterFields(task,
                    program != null || composite != null ? k_OverridesProperty : null,
                    StateTreeFieldBinding.TargetKind.Task, index);
                if (composite != null)
                {
                    // The sub-tree can also be swapped by dropping an asset on the generic
                    // ObjectField below, which is the path that can produce a loop — so that one
                    // property (and only that one, or every keystroke in the state lists would
                    // rebuild the pane under the author's cursor) re-runs the guard.
                    //
                    // Compared BY ASSET PATH, not object identity: a .taskgraph reimport (which
                    // the debounced SaveAssets triggers ~0.75s after ANY pane edit while a graph
                    // is open) RECREATES the imported main asset, so the reference "changes"
                    // without the author re-pointing anything — and rebuilding on that tore the
                    // pane down under the cursor on every typing pause (user-reported). Only an
                    // actual re-point (different file) is structural.
                    var subTreePath = AssetDatabase.GetAssetPath(composite.subTree);
                    fields.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
                    {
                        if (evt.changedProperty == null
                            || evt.changedProperty.propertyPath != k_SubTreeProperty)
                            return;
                        var nowPath = AssetDatabase.GetAssetPath(composite.subTree);
                        if (nowPath == subTreePath)
                            return;
                        subTreePath = nowPath;
                        DeferStructuralChange();
                    });
                }
                else if (program != null)
                {
                    // Same reasoning for the wrapper's graph field — and this is the row where the
                    // path comparison is load-bearing, because .taskgraph mains are ScriptedImporter
                    // artifacts and DO get recreated by every save-triggered reimport.
                    var graphPath = AssetDatabase.GetAssetPath(program.graph);
                    fields.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
                    {
                        if (evt.changedProperty == null
                            || evt.changedProperty.propertyPath != k_GraphProperty)
                            return;
                        var nowPath = AssetDatabase.GetAssetPath(program.graph);
                        if (nowPath == graphPath)
                            return;
                        graphPath = nowPath;
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

        /// <summary>What the box at the top of a task says, in one place — because it is now said in
        /// two: the task list, and the source dropdown of an output route. A route naming its source
        /// anything other than what the box above it says would make the author match them up by
        /// counting.</summary>
        private static string TaskBoxLabel(StateTreeTaskAsset task)
        {
            return task is RunGraphTask program ? ProgramLabel(program.graph) : TaskLabel(task);
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

        // --- task parameters: the overrides -----------------------------------------------

        /// <summary>
        /// One task wrapper's parameter surface — what makes the SAME rows serve both authored
        /// kinds. A logic graph declares its parameters as graph variables; a sub-tree declares them
        /// in its own header list as blackboard keys. From the calling state's side those are one
        /// thing: named knobs with defaults, and this state's overrides of them. Everything below is
        /// written against this description, so a third authored kind costs a factory and nothing
        /// else.
        /// </summary>
        private sealed class ParameterSurface
        {
            /// <summary>The sub-asset CARRYING the overrides, which is the undo and dirty target. It
            /// is the wrapper, never the graph or the sub-tree: an override is per use, and writing
            /// it to the callee would change it for every other caller.</summary>
            internal StateTreeTaskAsset owner;

            /// <summary>What the callee declares. Empty is legal and means "no knobs".</summary>
            internal List<GraphTaskParameter> declared;

            /// <summary>This state's overrides as stored — may be null on data that predates the
            /// field, which is why nothing dereferences it directly.</summary>
            internal Func<List<GraphTaskParameterOverride>> read;

            /// <summary>The same list, created on the wrapper if it was null. Only write paths call
            /// it, so merely LOOKING at a task that has never been overridden allocates nothing.
            /// </summary>
            internal Func<List<GraphTaskParameterOverride>> write;

            /// <summary>What one declaration is, in the author's words. Used in every row tooltip,
            /// because "graph variable" and "blackboard key" are the same idea seen from two
            /// different windows and only the wrong one is confusing.</summary>
            internal string noun;

            /// <summary>The sentence under the section title.</summary>
            internal string hint;

            /// <summary>Why a name is stale — the one message whose FIX differs by kind (edit the
            /// canvas vs edit the sub-tree's own Parameters list).</summary>
            internal Func<string, string> staleMessage;

            /// <summary>True when the callee declares the name but nothing in it reads the value.
            /// Null when the question cannot be answered honestly: a sub-tree's parameters are
            /// blackboard keys and anything downstream — a nested graph, a condition, a task added
            /// tomorrow — may read one, so the row says nothing rather than claiming "unused".
            /// </summary>
            internal Predicate<string> unused;

            /// <summary>Tooltip tail for a row <see cref="unused"/> flagged.</summary>
            internal string unusedTooltip;
        }

        /// <summary>The logic-graph surface: variables declared on the canvas, read by
        /// <c>GetParam*</c> instructions — which is why this is the kind that CAN answer "does
        /// anything read this?".</summary>
        private static ParameterSurface SurfaceOf(RunGraphTask program)
        {
            var graph = program.graph;
            return new ParameterSurface
            {
                owner = program,
                declared = graph.parameters,
                read = () => program.overrides,
                write = () => program.overrides ??= new List<GraphTaskParameterOverride>(),
                noun = "Graph variable",
                hint = "Tick a parameter to give this state its own value. Unticked rows show what "
                    + "the graph itself uses, and follow it when the graph changes.",
                staleMessage = name => $"'{name}' is overridden here, but the graph has no parameter "
                    + "by that name — it was probably renamed or deleted. The override does nothing.",
                unused = name => !IsParameterRead(graph, name),
                unusedTooltip = "is declared but no node in the graph reads it, so overriding it "
                    + "changes nothing. A variable used only on a library call's parameter port "
                    + "reads this way: those are baked into the graph and cannot be overridden per "
                    + "state."
            };
        }

        /// <summary>The sub-tree surface: the nested tree's declared blackboard keys, seeded on
        /// every entry. Same rows, same storage, different vocabulary — and no "unused" claim,
        /// because a blackboard key can be read by anything the sub-tree reaches.
        ///
        /// The callee's declarations are healed here rather than only in its own window: the rows
        /// below cannot bind to a parameter that has no id, and waiting until somebody happens to
        /// open that tree would mean every override on this state read as stale in the meantime.
        /// The sub-tree is a real asset when it declares anything at all — a tree baked from a
        /// <c>.statetree</c> graph carries no declarations — so this writes to a file the author
        /// owns.</summary>
        private static ParameterSurface SurfaceOf(RunSubTreeTask composite)
        {
            var subTree = composite.subTree;
            HealDeclarationIds(subTree);

            return new ParameterSurface
            {
                owner = composite,
                declared = subTree.parameters,
                read = () => composite.overrides,
                write = () => composite.overrides ??= new List<GraphTaskParameterOverride>(),
                noun = "Blackboard parameter",
                hint = "Tick a parameter to give this state's run of the sub-tree its own value. "
                    + "Unticked rows show the sub-tree's own default. Either way the effective "
                    + "value is written to the shared blackboard under that name every time this "
                    + "state is entered — including a re-entry, which puts it back.",
                staleMessage = name => $"'{name}' is overridden here, but "
                    + $"'{StateTreeEditorOps.TreeDisplayName(subTree)}' declares no parameter by "
                    + "that name — it was probably renamed or removed from that tree's own "
                    + "Parameters list. The override does nothing.",
                unused = null,
                unusedTooltip = null
            };
        }

        /// <summary>
        /// The per-state override list, drawn against the CALLEE's declaration — the Blueprint
        /// instance model: the callee declares the knobs and their defaults, and a state that runs it
        /// changes the ones it cares about. Hence a checkbox per row rather than a plain value field:
        /// "3" typed into an unchecked row and "3" typed into a checked one mean different things
        /// (follow the callee vs pin this state to 3), and the difference must survive the callee's
        /// author changing their mind about the default.
        ///
        /// The section is built from the declaration, never from the override list, so a row exists
        /// for every knob whether or not this state has touched it — a knob nobody knows about is a
        /// knob nobody turns. Overrides that resolve to nothing are the exception, listed after the
        /// rows as warnings with a way to delete them: deleting on the far side strands them silently
        /// otherwise, and the runtime's answer is a single log line nobody reads.
        ///
        /// EVERY BINDING HERE IS BY ID. A row finds its override by the declaration's id and by
        /// nothing else, which is what lets the callee's author rename a parameter without reaching
        /// into every state that overrides it. The name is what the row SHOWS, never what it matches.
        /// </summary>
        private VisualElement BuildParameterOverrides(ParameterSurface surface)
        {
            HealOverrideIds(surface);

            var container = new VisualElement();

            var parameters = surface.declared;
            var count = parameters != null ? parameters.Count : 0;
            var stale = CollectStaleOverrides(surface);

            if (count == 0 && stale.Count == 0)
                return container;

            var title = new Label($"Parameters ({count})");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 6f;
            container.Add(title);

            if (count > 0)
            {
                var hint = Hint(surface.hint);
                hint.style.marginBottom = 2f;
                container.Add(hint);

                for (var i = 0; i < count; ++i)
                {
                    var parameter = parameters[i];
                    if (parameter == null || string.IsNullOrEmpty(parameter.name))
                        continue;

                    container.Add(string.IsNullOrEmpty(parameter.id)
                        ? BuildIdlessParameterRow(parameter)
                        : BuildParameterRow(surface, parameter));
                }
            }

            for (var i = 0; i < stale.Count; ++i)
                container.Add(BuildStaleOverrideRow(surface, stale[i]));

            return container;
        }

        /// <summary>
        /// A declared parameter with no id, which cannot be overridden and says so where the row
        /// would have been.
        ///
        /// Only a BAKED declaration reaches this: a tree's own list is healed when it is drawn, so an
        /// idless one came from a graph baked before parameters had identity. Minting an id here would
        /// be worse than useless — the artifact is rebuilt on every reimport, so the id would change
        /// underneath any override that adopted it — and the fix is one gesture on the far side, which
        /// is what the row says instead.
        /// </summary>
        private static VisualElement BuildIdlessParameterRow(GraphTaskParameter parameter)
        {
            var help = new HelpBox($"'{parameter.name}' was baked before parameters had ids, so this "
                + "state cannot override it. Open the graph and save it — the re-bake gives it one, "
                + $"and its default ({DefaultLabel(parameter)}) is what runs until then.",
                HelpBoxMessageType.Warning);
            help.style.marginTop = 2f;
            return help;
        }

        /// <summary>
        /// Adopt an id for any override that has none — the other half of the one-time upgrade, and
        /// the reason a state configured before ids existed keeps running exactly as it did.
        ///
        /// EXACTLY ONE declaration may carry the name, or the row is left alone to be reported as
        /// stale. Two declarations sharing a name is already refused by the declaration editor and
        /// only reachable through hand-edited YAML, and guessing which of them an old override meant
        /// would silently pick a value the author never chose — the one outcome worse than a warning.
        ///
        /// No undo record, same reasoning as <see cref="HealDeclarationIds"/>: the author did not do
        /// this. The owner is a task sub-asset of the open tree, so it is always writable.
        /// </summary>
        private static void HealOverrideIds(ParameterSurface surface)
        {
            var overrides = surface.read();
            if (overrides == null)
                return;

            var healed = false;
            for (var i = 0; i < overrides.Count; ++i)
            {
                var entry = overrides[i];
                if (entry == null || !string.IsNullOrEmpty(entry.id)
                    || string.IsNullOrEmpty(entry.name))
                    continue;

                var id = SoleDeclaredId(surface, entry.name);
                if (string.IsNullOrEmpty(id))
                    continue;

                entry.id = id;
                healed = true;
            }

            if (healed)
                EditorUtility.SetDirty(surface.owner);
        }

        /// <summary>The id of the ONE declaration carrying this name, or empty when none does, when
        /// several do, or when the one that does has no id of its own.</summary>
        private static string SoleDeclaredId(ParameterSurface surface, string name)
        {
            var parameters = surface.declared;
            if (parameters == null)
                return string.Empty;

            // Counted rather than inferred from the id found so far: a first match that is itself
            // idless would otherwise let a second one through as if it were the only one.
            var matches = 0;
            var found = string.Empty;
            for (var i = 0; i < parameters.Count; ++i)
            {
                var parameter = parameters[i];
                if (parameter == null
                    || !string.Equals(parameter.name, name, StringComparison.Ordinal))
                    continue;
                if (++matches > 1)
                    return string.Empty;

                found = parameter.id ?? string.Empty;
            }

            return found;
        }

        /// <summary>
        /// One knob: the override checkbox, the name, the value field for its kind, and the control
        /// that hands the knob to one of THIS tree's own parameters instead of a literal.
        ///
        /// The field is disabled and dimmed while the checkbox is off, showing the callee's default —
        /// which is what the state actually runs, so it is shown rather than blanked — and disabled
        /// again, for the opposite reason, when the row is linked: the value then comes from the
        /// parent tree at run time and the literal is a leftover.
        ///
        /// LINKING IMPLIES OVERRIDING. "Take this from my 'speed'" is an override — of course it is —
        /// so the control ticks the checkbox itself rather than being greyed out until the author
        /// ticks it first, which would be asking them to answer a question they already answered.
        /// </summary>
        private VisualElement BuildParameterRow(ParameterSurface surface,
            GraphTaskParameter parameter)
        {
            var container = new VisualElement();

            var row = new VisualElement();
            row.AddToClassList("unity-base-field");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = k_RowMinHeight;
            container.Add(row);

            var overridden = IsOverridden(surface, parameter);

            var toggle = new Toggle();
            toggle.style.flexShrink = 0f;
            EnlargeToggle(toggle);
            toggle.tooltip = "Give this state its own value for this parameter. Off: the state uses "
                + "the default declared alongside it.";
            toggle.SetValueWithoutNotify(overridden);
            row.Add(toggle);

            var unused = surface.unused != null && surface.unused(parameter.name);
            var label = new Label(unused ? parameter.name + "  (unused)" : parameter.name);
            label.AddToClassList("unity-base-field__label");
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;

            var identity = $"{surface.noun} '{parameter.name}' ({KindLabel(parameter.kind)})";
            label.tooltip = unused
                ? identity + " " + surface.unusedTooltip
                : identity + ". Default: " + DefaultLabel(parameter);
            LabelTogglesValue(label, toggle);
            row.Add(label);

            var entry = ActiveOverride(surface, parameter);
            var sourceId = entry != null ? entry.sourceParameterId : null;
            var linked = !string.IsNullOrEmpty(sourceId);
            var source = StateTreeEditorOps.FindParameterById(
                m_Tree != null ? m_Tree.parameters : null, sourceId);
            var live = IsLiveLink(source, parameter.kind);

            var input = BuildParameterInput(surface, parameter);
            input.AddToClassList("unity-base-field__input");
            input.style.flexGrow = 1f;
            WriteParameterInput(input, surface, parameter);

            // Disabled for a STALE link too, not only a live one: the runtime drops an unresolvable
            // pass-through row rather than falling back to its literal
            // (StateTreeExecutor.ResolveSourceValues), so the number in the field is not what runs
            // either way, and an editable field that does nothing is the lie this control exists to
            // stop being told one level down.
            ApplyOverrideStyle(input, overridden && !linked);
            row.Add(input);

            var compatible = CompatibleParameters(parameter.kind);
            if (compatible.Count > 0 || live)
            {
                var pick = new Button { text = live ? k_BoundPrefix + source.name : k_LinkLabel };
                pick.style.flexShrink = 0f;
                EnlargeRowButton(pick, k_LinkMinWidth);
                pick.style.maxWidth = 140f;
                pick.style.overflow = Overflow.Hidden;
                pick.style.textOverflow = TextOverflow.Ellipsis;
                pick.style.whiteSpace = WhiteSpace.NoWrap;
                pick.tooltip = live
                    ? $"This state passes its own '{source.name}' through as "
                    + $"'{parameter.name}', so the value beside it is not what runs. Click to pass "
                    + "a different parameter."
                    : $"Pass one of THIS tree's {KindLabel(parameter.kind)} parameters through as "
                    + $"'{parameter.name}', instead of the value typed here — the callee then "
                    + "follows whatever this tree was given.";
                pick.clicked += () => ShowParameterMenu(pick, compatible, sourceId,
                    id => SetOverrideSource(surface, parameter, id, k_LinkSourceUndo));
                row.Add(pick);
            }

            if (linked)
            {
                var unlink = new Button { text = "✕" };
                unlink.style.width = 26f;
                unlink.style.minHeight = k_ControlMinHeight;
                unlink.style.flexShrink = 0f;
                unlink.tooltip = $"Stop passing a parameter through as '{parameter.name}' — the "
                    + "value in the field is used again.";
                unlink.clicked += () => SetOverrideSource(surface, parameter, string.Empty,
                    k_UnlinkSourceUndo);
                row.Add(unlink);
            }

            if (linked && !live)
            {
                var help = new HelpBox(StaleSourceMessage(parameter, source),
                    HelpBoxMessageType.Warning);
                help.style.marginTop = 2f;
                container.Add(help);
            }

            toggle.RegisterValueChangedCallback(evt =>
            {
                SetOverride(surface, parameter, evt.newValue);

                // Un-ticking a linked row deletes the row and the link with it, so the "← name"
                // beside it has just stopped being true — that one needs the pane, not a restyle.
                if (linked)
                {
                    RebuildPane();
                    return;
                }

                WriteParameterInput(input, surface, parameter);
                ApplyOverrideStyle(input, evt.newValue);
            });

            return container;
        }

        /// <summary>
        /// Why an override row's pass-through is doing nothing. Same two stories as a stale field
        /// link, told about the parent tree's list instead of the callee's — and with a different
        /// ending, because the two fail differently: a dead FIELD link leaves the field's own value
        /// in place, while a dead pass-through row is DROPPED from the list the callee is handed
        /// (StateTreeExecutor.ResolveSourceValues), so what runs is the callee's declared default —
        /// the same thing an unticked row does. It is quoted here rather than described, because
        /// "the default" is a number the author would otherwise have to go and look up in another
        /// asset.
        /// </summary>
        private static string StaleSourceMessage(GraphTaskParameter parameter,
            GraphTaskParameter source)
        {
            var ending = " This state falls back to the default declared alongside it "
                + $"({DefaultLabel(parameter)}), exactly as if the row were unticked.";

            return source == null
                ? $"'{parameter.name}' is set from a parameter this tree no longer declares — it "
                + "was deleted (a rename would have kept the link)." + ending
                : $"'{parameter.name}' is set from '{source.name}', which is now a "
                + $"{KindLabel(source.kind)} where this parameter is a "
                + $"{KindLabel(parameter.kind)}, so the link is skipped." + ending;
        }

        /// <summary>
        /// Point one override row at a parameter of the tree being edited — the pass-through half of
        /// M7i, stored as an id on the row exactly like the override's own binding.
        ///
        /// The row is created and ticked if it did not exist, because linking IS overriding (see
        /// <see cref="BuildParameterRow"/>), and the value fields are seeded from the callee's
        /// default for the same reason <see cref="SetOverride"/> seeds them: unlinking later must
        /// leave the author looking at the number they were looking at before, not at zero.
        ///
        /// An empty <paramref name="sourceId"/> is the unlink, deliberately through this same method:
        /// clearing the id leaves the row overriding with its literal, which is what the author sees
        /// the moment the arrow disappears.
        /// </summary>
        private void SetOverrideSource(ParameterSurface surface, GraphTaskParameter parameter,
            string sourceId, string undoName)
        {
            var group = StateTreeEditorOps.BeginUndoGroup(undoName);
            Undo.RecordObject(surface.owner, undoName);

            var overrides = surface.write();
            var index = IndexOfOverride(surface, parameter);

            GraphTaskParameterOverride entry;
            if (index < 0)
            {
                entry = NewOverrideRow(parameter);
                overrides.Add(entry);
            }
            else
            {
                entry = overrides[index];
                entry.enabled = true;
            }

            entry.sourceParameterId = sourceId ?? string.Empty;

            EditorUtility.SetDirty(surface.owner);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        /// <summary>A fresh override row for one declaration: bound by id, labelled by name, and
        /// holding the callee's current default so ticking the box and typing nothing pins the value
        /// the author was already looking at. One definition because two gestures create rows —
        /// the checkbox and the link — and a row that came from one behaving differently from a row
        /// that came from the other would be a bug found weeks later.</summary>
        private static GraphTaskParameterOverride NewOverrideRow(GraphTaskParameter parameter)
        {
            return new GraphTaskParameterOverride
            {
                id = parameter.id,
                name = parameter.name,
                enabled = true,
                floatValue = parameter.floatValue,
                stringValue = parameter.stringValue ?? string.Empty
            };
        }

        /// <summary>The value editor for one parameter kind. Its callback writes straight into the
        /// override entry, which exists whenever the field is editable — the field is disabled while
        /// the row is not overridden, so there is no state where a keystroke has nowhere to go.
        /// </summary>
        private VisualElement BuildParameterInput(ParameterSurface surface,
            GraphTaskParameter parameter)
        {
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.String:
                {
                    var field = new TextField { isDelayed = true };
                    field.RegisterValueChangedCallback(evt => CommitOverride(surface, parameter,
                        entry => entry.stringValue = evt.newValue ?? string.Empty));
                    return field;
                }

                case GraphTaskParameterKind.Bool:
                {
                    var field = new Toggle();
                    field.RegisterValueChangedCallback(evt => CommitOverride(surface, parameter,
                        entry => entry.floatValue = evt.newValue ? 1f : 0f));
                    return field;
                }

                default:
                {
                    var field = new FloatField { isDelayed = true };
                    field.RegisterValueChangedCallback(evt => CommitOverride(surface, parameter,
                        entry => entry.floatValue = evt.newValue));
                    return field;
                }
            }
        }

        /// <summary>Push the EFFECTIVE value into the field: the override when there is one, the
        /// callee's default when there is not. Without notify — this is the tool writing to itself,
        /// not the author writing to the asset.</summary>
        private static void WriteParameterInput(VisualElement input, ParameterSurface surface,
            GraphTaskParameter parameter)
        {
            var entry = ActiveOverride(surface, parameter);

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

        /// <summary>Turn an override on or off. On seeds the entry from the callee's current default,
        /// so ticking the box and typing nothing pins the value the author was already looking at;
        /// off DELETES the entry, because an override that exists but does nothing is the state this
        /// UI cannot show and the runtime would still carry.
        ///
        /// A new entry stores the name beside the id. Nothing resolves through it — that is the
        /// point of the id — but an override row in a YAML diff, or in the stale warning after the
        /// declaration is deleted, has to say WHICH knob it was, and a bare GUID says nothing.
        /// </summary>
        private void SetOverride(ParameterSurface surface, GraphTaskParameter parameter, bool on)
        {
            var undoName = on ? k_SetOverrideUndo : k_ClearOverrideUndo;
            var group = StateTreeEditorOps.BeginUndoGroup(undoName);
            Undo.RecordObject(surface.owner, undoName);

            var overrides = surface.write();

            var index = IndexOfOverride(surface, parameter);
            if (on && index < 0)
            {
                overrides.Add(NewOverrideRow(parameter));
            }
            else if (on)
            {
                // An entry left behind switched off — hand-edited YAML, or a merge. Re-arm it in
                // place rather than adding a second entry for the same parameter.
                overrides[index].enabled = true;
            }
            else
            {
                // EVERY row for the parameter, not just the resolved one: unticking the box has to
                // make "this state does not override that parameter" true, and a second row left
                // behind would quietly keep overriding it.
                RemoveOverrideRows(overrides, parameter);
            }

            EditorUtility.SetDirty(surface.owner);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        private void CommitOverride(ParameterSurface surface, GraphTaskParameter parameter,
            Action<GraphTaskParameterOverride> write)
        {
            var entry = ActiveOverride(surface, parameter);
            if (entry == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditOverrideUndo);
            Undo.RecordObject(surface.owner, k_EditOverrideUndo);
            write(entry);
            EditorUtility.SetDirty(surface.owner);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        /// <summary>An override that resolves to no declared parameter — deleted on the far side, or
        /// written before ids existed and no longer matchable by name. It is dead weight the runtime
        /// warns about once, so it is surfaced here where it can be deleted instead. The row holds the
        /// ENTRY, not an index or a name: an id-less row has no name worth matching on and the list
        /// shifts under every removal.</summary>
        private VisualElement BuildStaleOverrideRow(ParameterSurface surface,
            GraphTaskParameterOverride entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2f;

            var name = string.IsNullOrEmpty(entry.name) ? "(unnamed)" : entry.name;
            var help = new HelpBox(StaleMessage(surface, entry, name), HelpBoxMessageType.Warning);
            help.style.flexGrow = 1f;
            row.Add(help);

            var remove = new Button { text = "Remove" };
            remove.style.flexShrink = 0f;
            remove.tooltip = $"Delete the '{name}' override from this task.";
            remove.clicked += () =>
            {
                RemoveOverride(surface, entry);
                row.RemoveFromHierarchy();
            };
            row.Add(remove);

            return row;
        }

        /// <summary>Why a row is stale, which is two different stories. An override with an id names
        /// a parameter that was DELETED, and the fix is on the callee — that is
        /// <see cref="ParameterSurface.staleMessage"/>, the one message whose wording differs by kind.
        /// An override with no id at all is older than the model: it could not be matched to a
        /// declaration by name either, so nothing can be recovered from it and the honest advice is to
        /// delete it and tick the parameter again.</summary>
        private static string StaleMessage(ParameterSurface surface,
            GraphTaskParameterOverride entry, string name)
        {
            return string.IsNullOrEmpty(entry.id)
                ? $"'{name}' was overridden before parameters had ids, and no parameter of that name "
                + "is declared any more — so there is nothing left to attach it to. Delete it and "
                + "tick the parameter you meant."
                : surface.staleMessage(name);
        }

        private void RemoveOverride(ParameterSurface surface, GraphTaskParameterOverride entry)
        {
            var overrides = surface.read();
            if (overrides == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_ClearOverrideUndo);
            Undo.RecordObject(surface.owner, k_ClearOverrideUndo);
            overrides.Remove(entry);
            EditorUtility.SetDirty(surface.owner);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        /// <summary>Drop every row bound to a parameter. Every, because the caller is answering a
        /// question about the PARAMETER — "stop overriding this" — and leaving a duplicate behind
        /// would answer it with a row that keeps overriding.</summary>
        private static void RemoveOverrideRows(List<GraphTaskParameterOverride> overrides,
            GraphTaskParameter parameter)
        {
            for (var i = overrides.Count - 1; i >= 0; --i)
            {
                var entry = overrides[i];
                if (entry != null && entry.Matches(parameter))
                    overrides.RemoveAt(i);
            }
        }

        /// <summary>Override rows this task carries that bind to nothing the callee declares, in list
        /// order. <c>Declaration</c> is the runtime's own answer to that question, so a row this
        /// window offers to delete is exactly a row the runtime would skip.</summary>
        private static List<GraphTaskParameterOverride> CollectStaleOverrides(
            ParameterSurface surface)
        {
            var stale = new List<GraphTaskParameterOverride>();
            var overrides = surface.read();
            if (overrides == null)
                return stale;

            for (var i = 0; i < overrides.Count; ++i)
            {
                var entry = overrides[i];
                if (entry != null && entry.Declaration(surface.declared) == null)
                    stale.Add(entry);
            }

            return stale;
        }

        /// <summary>
        /// The row this window EDITS for a parameter: the last enabled one, falling back to the last
        /// row of any kind so a leftover switched-off entry is re-armed in place rather than
        /// duplicated. Only <see cref="SetOverride"/> needs the fallback — everything that asks what
        /// the state actually runs goes through <see cref="ActiveOverride"/>, which is the runtime's
        /// own resolver.
        ///
        /// Last, not first, because that is what the appliers do — they walk the list applying as
        /// they go, so a later row overwrites an earlier one. Duplicate rows are only reachable
        /// through hand-edited YAML or a merge, and that is exactly when the inspector must not show
        /// a different value from the one that runs.
        /// </summary>
        private static int IndexOfOverride(ParameterSurface surface, GraphTaskParameter parameter)
        {
            var overrides = surface.read();
            if (overrides == null)
                return -1;

            var fallback = -1;
            for (var i = overrides.Count - 1; i >= 0; --i)
            {
                var entry = overrides[i];
                if (entry == null || !entry.Matches(parameter))
                    continue;
                if (entry.enabled)
                    return i;
                if (fallback < 0)
                    fallback = i;
            }

            return fallback;
        }

        /// <summary>The override entry actually in force for a parameter — the RUNTIME's answer,
        /// called rather than reimplemented so this window cannot show a value the interpreter does
        /// not use.</summary>
        private static GraphTaskParameterOverride ActiveOverride(ParameterSurface surface,
            GraphTaskParameter parameter)
            => GraphTaskParameterOverride.EnabledFor(surface.read(), parameter);

        private static bool IsOverridden(ParameterSurface surface, GraphTaskParameter parameter)
            => ActiveOverride(surface, parameter) != null;

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

        /// <summary>The one place a kind is put into words — the declaration dropdown offers the
        /// same list, so an author picks "checkbox" and every tooltip afterwards says "checkbox".
        /// </summary>
        private static string KindLabel(GraphTaskParameterKind kind)
        {
            var index = (int)kind;
            return index >= 0 && index < k_ParameterKindChoices.Length
                ? k_ParameterKindChoices[index]
                : kind.ToString();
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

            // The source choices are the same for every transition of this state, so they are built
            // once — and they are the TASK BOXES' labels, so a route reads as "task 2, the one above".
            var tasks = TaskChoiceLabels();

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
                box.Add(BuildParameterFields(transition.condition, null,
                    StateTreeFieldBinding.TargetKind.TransitionCondition, index));

                // Last in the box, because it is last in time: everything above decides WHETHER this
                // transition fires, and this decides what it carries when it does.
                box.Add(BuildOutputRoutes(index, transition, tasks));

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

        // --- transition output routes -----------------------------------------------------

        /// <summary>
        /// The return flow, on the transition that carries it. Each row says: take THIS output of
        /// THAT finished task and write it to the blackboard under this key.
        ///
        /// A foldout, and closed until it holds something, because routing is the exception rather
        /// than the rule — most transitions carry nothing, and three controls per transition that
        /// most states never use would push the wiring this section exists for off the screen. A
        /// transition that DOES route opens by itself, so the exception is never hidden.
        ///
        /// The rules the author has to know are in the note rather than in a tooltip, because both
        /// of them are surprising and neither is discoverable by trying: a route reads a task that
        /// FINISHED (an interrupt therefore carries only the tasks that were already done, and a
        /// cancelled task carries nothing), and the values are written on the way out, before the
        /// target state is entered — which is what makes them readable there.
        /// </summary>
        /// <param name="transitionIndex">Which transition this belongs to — what the Ops helpers
        /// address, and what the foldout's remembered open state is keyed by.</param>
        /// <param name="transition">The transition itself, already known non-null by the caller.</param>
        /// <param name="taskLabels">This state's task boxes' labels, built once by the caller.</param>
        private VisualElement BuildOutputRoutes(int transitionIndex, StateTreeTransition transition,
            List<string> taskLabels)
        {
            var routes = transition.outputRoutes;
            var count = routes != null ? routes.Count : 0;

            var foldout = new Foldout
            {
                text = count == 0 ? "Route outputs" : $"Route outputs ({count})",
                value = count > 0 || m_OpenRouteFoldouts.Contains(transitionIndex)
            };
            foldout.style.marginTop = 2f;

            // Every Toggle and every dropdown inside sends its own change event straight through
            // here, so the foldout listens only for its own — the same guard BuildTreeParameters
            // needs for the same reason.
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != foldout)
                    return;

                if (evt.newValue)
                    m_OpenRouteFoldouts.Add(transitionIndex);
                else
                    m_OpenRouteFoldouts.Remove(transitionIndex);
            });

            if (count == 0 && taskLabels.Count == 0)
            {
                foldout.Add(Hint("This state runs no tasks, so there is nothing to carry forward. "
                    + "Outputs come from a finished task — add one above first."));
                return foldout;
            }

            var note = Hint(count == 0
                ? "Nothing routed. A route copies a value a finished task produced onto the "
                + "blackboard as this transition fires, so the state it leads to can read it."
                : "Written as this transition fires, before the target state is entered. Only tasks "
                + "that FINISHED have outputs — an interrupt carries the ones that were already "
                + "done, and a cancelled task carries nothing.");
            note.style.marginBottom = 4f;
            foldout.Add(note);

            // Drawn before the "no tasks" case is answered, deliberately: rows can outlive every
            // task they read if the list was emptied by something other than this window (a merge,
            // hand-edited YAML), and a foldout that hid them would leave the author with a warning
            // in the validation box and no ✕ to press.
            for (var i = 0; i < count; ++i)
                foldout.Add(BuildOutputRouteRow(transitionIndex, i, routes[i], taskLabels));

            if (taskLabels.Count == 0)
            {
                foldout.Add(Hint("This state runs no tasks any more, so none of these rows reads "
                    + "anything. Remove them, or add back the task they were made against."));
                return foldout;
            }

            var add = new Button(() => AddOutputRoute(transitionIndex)) { text = "Add Route" };
            add.style.marginTop = 4f;
            add.tooltip = "Carry one of this state's finished tasks' outputs onto the blackboard "
                + "when this transition fires.";
            foldout.Add(add);

            return foldout;
        }

        /// <summary>
        /// One route: source task, output, destination key, remove. Three controls of equal weight
        /// on one row, because they are read left to right as one sentence — and the row is where
        /// its own problem is reported, so a route that carries nothing says so beside the thing
        /// that broke rather than only in the state's validation box.
        ///
        /// Both dropdowns are INDEX-addressed and never matched back by label, the same rule the
        /// transition target picker follows: two tasks of one class carry the same label, and an
        /// output name is authored text that may repeat across tasks.
        /// </summary>
        private VisualElement BuildOutputRouteRow(int transitionIndex, int routeIndex,
            TransitionOutputRoute route, List<string> taskLabels)
        {
            var container = new VisualElement();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = k_RowMinHeight;
            container.Add(row);

            if (route == null)
            {
                var empty = Hint("Empty route row.");
                empty.style.flexGrow = 1f;
                row.Add(empty);
                row.Add(RemoveRouteButton(transitionIndex, routeIndex));
                return container;
            }

            var labels = new List<string>(taskLabels);
            var taskChoice = route.taskIndex;
            if (taskChoice < 0 || taskChoice >= labels.Count)
            {
                labels.Add($"<missing: task {route.taskIndex + 1}>");
                taskChoice = labels.Count - 1;
            }

            var source = new DropdownField(labels, taskChoice);
            source.style.flexGrow = 1f;
            source.style.flexBasis = 0f;
            source.style.marginRight = 2f;
            source.style.minHeight = k_ControlMinHeight;
            source.tooltip = "Which of this state's tasks the value comes from. A task that was "
                + "cancelled by an interrupt produces nothing, the same way an abandoned function "
                + "call returns nothing.";
            source.RegisterValueChangedCallback(evt =>
            {
                var choice = source.index;
                if (choice < 0 || choice >= m_Node.tasks.Count)
                    return;

                CommitRoute(transitionIndex, routeIndex, entry => entry.taskIndex = choice);

                // A different task publishes different outputs, so the control beside this one is
                // now drawn against the wrong list — and may not even be the same KIND of control.
                RebuildPane();
            });
            row.Add(source);

            var task = route.taskIndex >= 0 && route.taskIndex < m_Node.tasks.Count
                ? m_Node.tasks[route.taskIndex]
                : null;
            row.Add(BuildRouteOutput(transitionIndex, routeIndex, route, task));

            var key = new TextField
            {
                value = route.blackboardKey ?? string.Empty,
                isDelayed = true
            };
            key.style.flexGrow = 1f;
            key.style.flexBasis = 0f;
            key.style.marginRight = 2f;
            key.style.minHeight = k_ControlMinHeight;

            // The placeholder IS the rule: empty writes under the output's own name, so showing that
            // name greyed out says what an empty field does without a second label to read. Asked of
            // the row rather than restated here — TransitionOutputRoute.ResolvedKey is what the
            // executor writes under, and a placeholder that disagreed with it would be invisible.
            var resolved = route.ResolvedKey();
            key.textEdition.placeholder = string.IsNullOrEmpty(resolved)
                ? "blackboard key"
                : resolved;
            key.tooltip = "Blackboard key the value lands under. Leave it empty to write it under "
                + "the output's own name — rename it here when the target state reads something "
                + "else, or when two routes would otherwise collide.";
            key.RegisterValueChangedCallback(evt => CommitRoute(transitionIndex, routeIndex,
                entry => entry.blackboardKey = evt.newValue ?? string.Empty));
            row.Add(key);

            row.Add(RemoveRouteButton(transitionIndex, routeIndex));

            var problem = DescribeRouteProblem(route);
            if (problem != null)
            {
                // Capitalised here and not in the validation box, where it follows "Transition 3: ".
                var help = new HelpBox(char.ToUpperInvariant(problem[0]) + problem.Substring(1),
                    HelpBoxMessageType.Warning);
                help.style.marginTop = 2f;
                container.Add(help);
            }

            return container;
        }

        /// <summary>
        /// The output control — a dropdown of what the source task publishes, or a free-text name
        /// when nothing can be discovered.
        ///
        /// The fallback is not a failure mode, it is the honest answer to a question the editor
        /// genuinely cannot settle: a graph whose file has not been re-baked declares no outputs yet,
        /// and a task class may publish through a path this window cannot see. Refusing to let the
        /// author type a name there would make a re-bake mandatory before wiring — so the name is
        /// typed, and the row reports at run time (and in the validation box, once the source does
        /// declare something) if it turns out to be wrong.
        ///
        /// A name the source no longer publishes stays selected as <c>&lt;missing: …&gt;</c> rather
        /// than being silently reset to nothing. That is the M7j identity rule made visible: outputs
        /// are matched BY NAME, so a rename on the far side is a break the author has to see and
        /// decide about, not something this window quietly papers over.
        /// </summary>
        private VisualElement BuildRouteOutput(int transitionIndex, int routeIndex,
            TransitionOutputRoute route, StateTreeTaskAsset task)
        {
            var current = route.outputName ?? string.Empty;
            var outputs = StateTreeEditorOps.CollectTaskOutputs(task);

            if (outputs.Count == 0)
            {
                var text = new TextField { value = current, isDelayed = true };
                text.style.flexGrow = 1f;
                text.style.flexBasis = 0f;
                text.style.marginRight = 2f;
                text.style.minHeight = k_ControlMinHeight;
                text.textEdition.placeholder = "output name";
                text.tooltip = task == null
                    ? "The output's name, matched at run time."
                    : $"{TaskBoxLabel(task)} declares no outputs this window can see — a graph that "
                    + "has not been re-baked is the usual reason. Type the name and it is matched "
                    + "at run time.";
                text.RegisterValueChangedCallback(evt =>
                {
                    CommitRoute(transitionIndex, routeIndex,
                        entry => entry.outputName = evt.newValue ?? string.Empty);

                    // The key field's placeholder is this name, and the row's warning is about it.
                    RebuildPane();
                });
                return text;
            }

            var names = new List<string> { string.Empty };
            var labels = new List<string> { k_NoOutputChoice };
            for (var i = 0; i < outputs.Count; ++i)
            {
                names.Add(outputs[i].name);
                labels.Add($"{outputs[i].name} ({KindLabel(outputs[i].kind)})");
            }

            var selected = names.IndexOf(current);
            if (selected < 0)
            {
                names.Add(current);
                labels.Add($"<missing: {current}>");
                selected = names.Count - 1;
            }

            var dropdown = new DropdownField(labels, selected);
            dropdown.style.flexGrow = 1f;
            dropdown.style.flexBasis = 0f;
            dropdown.style.marginRight = 2f;
            dropdown.style.minHeight = k_ControlMinHeight;
            dropdown.tooltip = "What that task publishes when it finishes. The name is the contract "
                + "— renaming it on the task's own side breaks this route rather than following it.";
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var choice = dropdown.index;
                if (choice < 0 || choice >= names.Count)
                    return;

                CommitRoute(transitionIndex, routeIndex, entry => entry.outputName = names[choice]);
                RebuildPane();
            });

            return dropdown;
        }

        private Button RemoveRouteButton(int transitionIndex, int routeIndex)
        {
            var remove = new Button(() => RemoveOutputRoute(transitionIndex, routeIndex))
            {
                text = "✕"
            };
            remove.style.width = 26f;
            remove.style.minHeight = k_ControlMinHeight;
            remove.style.flexShrink = 0f;
            remove.tooltip = "Stop carrying this output forward.";
            return remove;
        }

        /// <summary>This state's tasks, labelled exactly as their boxes above are and numbered by the
        /// position a route actually stores. The number is not decoration: two tasks of one class
        /// carry identical labels, and the row is addressed by index either way.</summary>
        private List<string> TaskChoiceLabels()
        {
            var labels = new List<string>();
            for (var i = 0; i < m_Node.tasks.Count; ++i)
                labels.Add($"{i + 1}. {TaskBoxLabel(m_Node.tasks[i])}");

            return labels;
        }

        /// <summary>Generic parameter block for one task/condition sub-asset. Nothing here knows
        /// any component type: whatever the class serialises is what the author sees — plus, for
        /// the fields a tree parameter can drive, the control that connects the two.</summary>
        /// <param name="target">The sub-asset to draw.</param>
        /// <param name="hiddenProperty">One property to leave out, for the single case where a
        /// purpose-built control above already edits it. Named rather than inferred, so a field only
        /// disappears where something visibly replaced it.</param>
        /// <param name="bindingKind">Which list <paramref name="targetIndex"/> indexes — what a
        /// link row on this state would have to say to find this object again.</param>
        /// <param name="targetIndex">The target's position in that list, or below zero to draw the
        /// fields with no link controls at all.</param>
        private VisualElement BuildParameterFields(UnityEngine.Object target,
            string hiddenProperty, StateTreeFieldBinding.TargetKind bindingKind, int targetIndex)
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
                // A [TaskOutput] field is a RETURN value, written by the task at runtime —
                // rendering it as an editable knob would invite authoring a value the task
                // overwrites. It surfaces in the transition "Route outputs" dropdowns instead.
                if (StateTreeEditorOps.IsTaskOutputField(target, iterator.propertyPath))
                    continue;

                // Only the top level is walked (NextVisible stops entering children after the
                // first step), so a path IS a field name — which is what a link row stores and what
                // the executor's reflection looks up.
                container.Add(BuildBindableField(new PropertyField(iterator.Copy()), target,
                    iterator.propertyPath, bindingKind, targetIndex));
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

        /// <summary>
        /// One field, plus the control that connects it to a declared parameter — the M7i link, and
        /// the answer to "I declared 'speed', now what reads it?" for every task that is not one of
        /// the handful whose fields happen to be blackboard keys.
        ///
        /// The control appears only where it can do something: the field has to be one the executor
        /// can write (<see cref="StateTreeEditorOps.TryGetBindableKind"/> asks by reflection, so a
        /// <c>[SerializeField]</c> private is drawn and not offered), and the tree has to declare at
        /// least one parameter of the matching kind. A field with nothing to bind to is left exactly
        /// as it was drawn before this existed, because a Link button that opens an empty popup is a
        /// worse answer than no button.
        ///
        /// A BOUND FIELD IS DISABLED, and says where its value comes from. That is the whole point of
        /// the control: the literal underneath is still in the asset and is still what a reader of the
        /// YAML sees, so leaving it editable would leave the author tuning a number that the tree
        /// start overwrites — the failure this window exists to prevent, one level down.
        ///
        /// A link whose parameter is GONE leaves the field enabled, because the literal is what runs
        /// again, and says so in the same warning-plus-remove shape a stale override gets
        /// (<see cref="BuildStaleOverrideRow"/>). The Link button stays available beside it so the
        /// fix is one gesture rather than remove-then-link.
        /// </summary>
        private VisualElement BuildBindableField(PropertyField field, UnityEngine.Object target,
            string fieldName, StateTreeFieldBinding.TargetKind kind, int targetIndex)
        {
            if (targetIndex < 0 || m_Node == null || m_Tree == null
                || !StateTreeEditorOps.TryGetBindableKind(target, fieldName, out var fieldKind))
                return field;

            var binding = StateTreeEditorOps.FindFieldBinding(m_Node, kind, targetIndex, fieldName);
            var compatible = CompatibleParameters(fieldKind);
            if (binding == null && compatible.Count == 0)
                return field;

            var source = binding != null
                ? StateTreeEditorOps.FindParameterById(m_Tree.parameters, binding.parameterId)
                : null;
            var live = IsLiveLink(source, fieldKind);

            var container = new VisualElement();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            container.Add(row);

            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;

            // The same disabled-and-dimmed the override rows use, and it means the same thing in
            // both places: what you are looking at is not what runs.
            ApplyOverrideStyle(field, !live);
            row.Add(field);

            if (compatible.Count > 0 || live)
            {
                var pick = new Button { text = live ? k_BoundPrefix + source.name : k_LinkLabel };
                pick.style.flexShrink = 0f;
                EnlargeRowButton(pick, k_LinkMinWidth);
                pick.style.maxWidth = 140f;
                pick.style.overflow = Overflow.Hidden;
                pick.style.textOverflow = TextOverflow.Ellipsis;
                pick.style.whiteSpace = WhiteSpace.NoWrap;
                pick.tooltip = live
                    ? $"'{fieldName}' is written from the tree parameter '{source.name}' "
                    + $"({KindLabel(source.kind)}) every time this tree starts, so the value beside "
                    + "it is not what runs. Click to bind it to a different parameter."
                    : $"Write this field from one of this tree's {KindLabel(fieldKind)} parameters "
                    + "when the tree starts, instead of the value typed here.";
                pick.clicked += () => ShowParameterMenu(pick, compatible,
                    binding != null ? binding.parameterId : null,
                    id => SetFieldLink(kind, targetIndex, fieldName, id));
                row.Add(pick);
            }

            if (binding != null)
            {
                var unlink = new Button { text = "✕" };
                unlink.style.width = 26f;
                unlink.style.minHeight = k_ControlMinHeight;
                unlink.style.flexShrink = 0f;
                unlink.tooltip = $"Stop writing '{fieldName}' from a parameter — the value in the "
                    + "field runs again.";
                unlink.clicked += () => ClearFieldLink(kind, targetIndex, fieldName);
                row.Add(unlink);
            }

            if (binding != null && !live)
            {
                var help = new HelpBox(StaleBindingMessage(fieldName, source, fieldKind),
                    HelpBoxMessageType.Warning);
                help.style.marginTop = 2f;
                container.Add(help);
            }

            return container;
        }

        /// <summary>Whether a link actually feeds its target: the parameter is still declared, and
        /// still of the kind the target takes. ONE definition, asked by both link controls, because
        /// it is also the test the runtime makes before it writes — <c>StateTreeExecutor.TryWrite</c>
        /// for a field, <c>ResolveSourceValues</c> for a pass-through, both of which drop a
        /// kind-mismatched row rather than converting it — and a link this window draws as live and
        /// the runtime skips is the one outcome none of this may produce.</summary>
        private static bool IsLiveLink(GraphTaskParameter source, GraphTaskParameterKind kind)
            => source != null && source.kind == kind;

        /// <summary>Why a field's link is doing nothing — the two stories a stale override tells,
        /// in the same words. A parameter that is GONE was deleted, never renamed: a link holds an
        /// id, so a rename is invisible to it. A parameter that is still there but of the wrong
        /// KIND was retyped in the declaration list above, which the author can see and undo.
        /// </summary>
        private static string StaleBindingMessage(string fieldName, GraphTaskParameter source,
            GraphTaskParameterKind fieldKind)
        {
            return source == null
                ? $"'{fieldName}' is linked to a parameter this tree no longer declares — it was "
                + "deleted (a rename would have kept the link). The value in the field runs "
                + "instead."
                : $"'{fieldName}' is linked to '{source.name}', which is now a "
                + $"{KindLabel(source.kind)} where this field takes a {KindLabel(fieldKind)}. The "
                + "link is skipped and the value in the field runs instead.";
        }

        /// <summary>The declared parameters a field or override row of one kind may bind to. Rows
        /// with no id are excluded because nothing can bind to them (a baked declaration from before
        /// ids — the same rule <see cref="BuildIdlessParameterRow"/> explains), and rows with no name
        /// because the name is the blackboard key the value is seeded under.</summary>
        private List<GraphTaskParameter> CompatibleParameters(GraphTaskParameterKind kind)
        {
            var found = new List<GraphTaskParameter>();
            var parameters = m_Tree != null ? m_Tree.parameters : null;
            if (parameters == null)
                return found;

            for (var i = 0; i < parameters.Count; ++i)
            {
                var entry = parameters[i];
                if (entry != null && entry.kind == kind && !string.IsNullOrEmpty(entry.id)
                    && !string.IsNullOrEmpty(entry.name))
                    found.Add(entry);
            }

            return found;
        }

        /// <summary>The parameter popup, shared by both link controls. Names are shown and the id is
        /// what the callback receives — the whole binding model in one method — and the current
        /// choice is ticked so re-opening it reads as "change this" rather than "make one".</summary>
        private static void ShowParameterMenu(VisualElement anchor,
            List<GraphTaskParameter> choices, string currentId, Action<string> picked)
        {
            var menu = new GenericMenu();
            for (var i = 0; i < choices.Count; ++i)
            {
                var entry = choices[i];
                var id = entry.id;
                var label = (entry.name ?? string.Empty).Replace('/', k_MenuSeparatorStandIn);
                menu.AddItem(new GUIContent(label),
                    string.Equals(id, currentId, StringComparison.Ordinal), () => picked(id));
            }

            menu.DropDown(anchor.worldBound);
        }

        /// <summary>Bind a field, through the Ops helper that owns the row list — so the link is
        /// undone in one step and is renumbered by the same file that renumbers everything else when
        /// a task or transition moves.</summary>
        private void SetFieldLink(StateTreeFieldBinding.TargetKind kind, int targetIndex,
            string fieldName, string parameterId)
        {
            if (m_Node == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_LinkFieldUndo);
            StateTreeEditorOps.SetFieldBinding(m_Node, kind, targetIndex, fieldName, parameterId,
                k_LinkFieldUndo);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private void ClearFieldLink(StateTreeFieldBinding.TargetKind kind, int targetIndex,
            string fieldName)
        {
            if (m_Node == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_UnlinkFieldUndo);
            StateTreeEditorOps.RemoveFieldBinding(m_Node, kind, targetIndex, fieldName,
                k_UnlinkFieldUndo);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
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

            // Parameter->field links do not survive the graph round-trip (the baker has no
            // bindings concept); say so up front rather than dropping them silently.
            var boundNodes = 0;
            foreach (var treeNode in StateTreeEditorOps.CollectNodes(source))
                if (treeNode != null && treeNode.bindings != null && treeNode.bindings.Count > 0)
                    boundNodes++;
            var bindingWarning = boundNodes == 0 ? "" :
                $"\n\nWARNING: {boundNodes} state(s) carry parameter->field links; the baked "
                + "graph does NOT keep them — re-link on the converted tree or keep this asset.";

            if (!EditorUtility.DisplayDialog("Convert to Graph",
                $"Write '{sourceName}' out as a graph at\n\n{target}\n\nThis task is then "
                + "re-pointed at the tree that graph bakes (one undo step), and the graph opens.\n\n"
                + $"'{sourcePath}' is left exactly as it is — check the graph, then delete the old "
                + "asset yourself. Anything else referencing it keeps pointing at it." + bindingWarning,
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

        /// <summary>Add a route, already pointing somewhere. The first task and its first output are
        /// the common case (one task, one output), and a row that arrives wired is one gesture
        /// instead of three — while a state with several tasks gets a row that is at least valid,
        /// which is the difference between "pick the right one" and "pick something".</summary>
        private void AddOutputRoute(int transitionIndex)
        {
            if (m_Node == null || m_Node.tasks.Count == 0)
                return;

            var outputs = StateTreeEditorOps.CollectTaskOutputs(m_Node.tasks[0]);
            var name = outputs.Count > 0 ? outputs[0].name : string.Empty;

            var group = StateTreeEditorOps.BeginUndoGroup(k_AddRouteUndo);
            StateTreeEditorOps.AddOutputRoute(m_Node, transitionIndex, 0, name, k_AddRouteUndo);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private void RemoveOutputRoute(int transitionIndex, int routeIndex)
        {
            if (m_Node == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_RemoveRouteUndo);
            StateTreeEditorOps.RemoveOutputRoute(m_Node, transitionIndex, routeIndex,
                k_RemoveRouteUndo);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        /// <summary>Edit one field of one route. Written here rather than behind an Ops helper for
        /// the same reason the transition's own target and interrupt are: a route is a plain
        /// serialized row on the node, so the undo target is the node and the write is the
        /// assignment — there is no sub-asset lifecycle for Ops to own. The Ops helpers exist for
        /// the two mutations that DO change the list's shape, which is what the index remap cares
        /// about.</summary>
        private void CommitRoute(int transitionIndex, int routeIndex,
            Action<TransitionOutputRoute> write)
        {
            var transition = StateTreeEditorOps.TransitionAt(m_Node, transitionIndex);
            var rows = transition != null ? transition.outputRoutes : null;
            if (rows == null || routeIndex < 0 || routeIndex >= rows.Count || rows[routeIndex] == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditRouteUndo);
            Undo.RecordObject(m_Node, k_EditRouteUndo);
            write(rows[routeIndex]);
            EditorUtility.SetDirty(m_Node);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
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
