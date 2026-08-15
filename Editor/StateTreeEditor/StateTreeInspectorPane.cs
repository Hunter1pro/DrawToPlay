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
    /// under Assets/DrawToPlayExamples/Tasks, a task on this state, and an open canvas to extend. It has
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
    ///
    /// AND THE VALUE THAT CAME BACK REACHES A FIELD. A route leaves its value on the blackboard, which
    /// the graph and the conditions can read and a plain <c>public float damage</c> cannot — so the
    /// return flow stopped one step short of the thing it was for. The field link therefore has a
    /// SECOND source (M7k): a blackboard KEY, read at every ENTRY of the state rather than once at
    /// tree start, which is what makes "attack routes damage, the next state's task takes damage"
    /// one wiring job instead of two halves that never met. The popup shows the two sources in one
    /// list, with the keys THIS state's incoming transitions actually route listed by name
    /// (<see cref="StateTreeEditorOps.CollectIncomingRoutedKeys"/>) — the tree's own wiring read
    /// backwards, so the key is picked rather than remembered, and filtered to the ones that carry
    /// this field's KIND, because a suggestion the executor would drop at entry is not a suggestion —
    /// and a free-form entry beneath them, because a key can equally be written by a graph or a
    /// distant state.
    ///
    /// The two sources read differently on purpose: "← name" for a parameter, "⚑ key" for a
    /// blackboard key, and the key stays editable in place because it is authored text rather than a
    /// pick from a list. So does their failure language — an unresolvable PARAMETER is a warning
    /// (nothing can ever fix it but the author), while a key nothing routes is INFORMATION, because
    /// entering a state through a path that routes nothing is the normal case the runtime is
    /// deliberately silent about, and only an EMPTY key is a fault.
    ///
    /// AND WHERE THE WINDOW KNOWS BOTH ENDS OF A NAME, THE NAME IS LOCKED (M7m). Everything above
    /// the return flow binds by id and survives renaming; the return flow itself is name-keyed by
    /// design, which leaves two edits able to disconnect a working wire in silence. The answer is
    /// neither a dialog (M7l, rejected: noise) nor a silent rename-follow (first M7m cut, rejected:
    /// still lets half a path be picked up at all): a COMPLETE wire — a route whose resolved key a
    /// field on the target state reads (<see cref="RouteKeyWired"/>/<see cref="BindingKeyWired"/>)
    /// — freezes its inline key at BOTH ends, so an editable key is always one that can only
    /// COMPLETE a wire, never break one. Changing a wire is deliberate and has its own controls:
    /// the link menu rebinds a field, the unlink button frees it, the route row removes or re-aims
    /// the writing end. And a
    /// route left stale by an output renamed on the far side offers the repair when it is unambiguous
    /// (<see cref="BuildMissingOutputFix"/>): exactly one output nothing else on the transition
    /// carries, proposed as a button rather than applied, because the break is still the author's to
    /// decide about.
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

        /// <summary>Its own label rather than <see cref="k_LinkFieldUndo"/>: the two sources are
        /// applied at different times and an author undoing "link to a parameter" when they linked
        /// a key would be told the wrong story about what is coming back.</summary>
        private const string k_LinkFieldKeyUndo = "Link Field To Blackboard Key";

        /// <summary>Retyping the key of an existing link goes through the same Ops writer as making
        /// one, so it gets a label that says which of the two happened.</summary>
        private const string k_EditFieldKeyUndo = "Set Field Blackboard Key";

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

        /// <summary>What a WIRED field shows in place of its value (M7m): the value is a task
        /// output that exists only at runtime, so the honest display is that there is nothing to
        /// display — not the authored number, which a routing entry overwrites.</summary>
        private const string k_RoutedValueText = "unknown until entry";

        /// <summary>What a row bound to a BLACKBOARD KEY shows instead of <see cref="k_BoundPrefix"/>
        /// — a flag rather than an arrow, because the two sources are not the same promise and must
        /// not read as the same one. An arrow points at a declaration that is definitely there; a flag
        /// marks a key that may or may not have been written by the time the state is entered, and the
        /// key itself is the editable field immediately to its right (the row reads "⚑ key").</summary>
        private const string k_KeyBoundGlyph = "⚑";

        /// <summary>Text of the control that opens the parameter popup. Not a glyph: every other
        /// symbol button in this window (✕, ▲, ▼) means something an author can guess, and "bind
        /// this to a parameter" is not one of those.</summary>
        private const string k_LinkLabel = "Link";

        /// <summary>The popup's heading over the keys this state's incoming transitions route. A
        /// disabled item rather than a submenu: these are suggestions among the parameters, not a
        /// separate mode, and burying them one level down would hide the one list that makes routed
        /// values discoverable at all.</summary>
        private const string k_RoutedHeading = "Routed into this state:";

        /// <summary>The escape hatch under the suggestions — any key at all, typed in place. The
        /// ellipsis is Unity's own convention for "this asks you for something", and what it asks for
        /// is the inline field the row grows.</summary>
        private const string k_KeyPickLabel = "Blackboard key…";

        /// <summary>Indent for the suggestion items, so the heading above them reads as a heading.
        /// Spaces rather than a submenu, for the reason <see cref="k_RoutedHeading"/> gives.</summary>
        private const string k_MenuIndent = "    ";

        /// <summary>Marks a routed key whose KIND this window could not work out — a graph that has
        /// not been re-baked, or a free-text output name nothing declares. Those keys are still
        /// offered (hiding the only key that arrives at a state on the strength of a guess would be
        /// worse than the mismatch it prevents), so the list says which of its entries it is sure
        /// about. A question mark rather than a word: the item is a key, and a sentence appended to
        /// one would read as part of it.</summary>
        private const string k_UnknownKindSuffix = " (?)";

        /// <summary>Click ergonomics for the override/link rows (user feedback: the bare
        /// checkbox and glyph buttons were hard to hit). One place sets the minimum
        /// target sizes; the row label also toggles, giving the checkbox a text-sized
        /// hit area like every built-in Unity toggle row.</summary>
        private const float k_RowMinHeight = 22f;
        private const float k_ControlMinHeight = 20f;
        private const float k_LinkMinWidth = 52f;

        /// <summary>Hit area for a one-glyph button, matching the ✕ buttons beside it — a word-sized
        /// minimum on a single character would just be a button with a lot of air in it.</summary>
        private const float k_GlyphMinWidth = 26f;

        /// <summary>Room for a readable key beside the value it feeds. Wide enough that the common
        /// keys ("damage", "target") are not ellipsed into uselessness, small enough that the value
        /// field it shares the row with is still editable.</summary>
        private const float k_KeyFieldMinWidth = 84f;

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

        /// <summary>The key-contract glyph — a key, as ⚑ is a flag: ⚑ sources a field's VALUE,
        /// ⚿ wires a field's NAME to a declaration.</summary>
        private const string k_KeyGlyph = "⚿";

        private const string k_AddKeyUndo = "Declare Tree Key";
        private const string k_RemoveKeyUndo = "Undeclare Tree Key";
        private const string k_EditKeyUndo = "Edit Tree Key";
        private const string k_RenameKeyUndo = "Rename Tree Key";
        private const string k_LinkKeyUndo = "Wire Field to Key";
        private const string k_UnlinkKeyUndo = "Unwire Field Key";
        private const string k_EditImportsUndo = "Edit Key Imports";

        /// <summary>Key-kind labels in ENUM ORDER, index-addressed like
        /// <see cref="k_ParameterKindChoices"/> and sharing its first three words — the two
        /// vocabularies meet in tooltips and must not name the same thing twice.</summary>
        private static readonly string[] k_KeyKindChoices =
            { "number", "text", "checkbox", "object", "event", "tag", "screen" };

        /// <summary>The data-entry glyph — a stack of rows, as ⚑ sources values and ⚿ wires
        /// names: ⛃ picks a registry ENTRY.</summary>
        private const string k_EntryGlyph = "⛃";

        private const string k_PickEntryUndo = "Pick Registry Entry";
        private const string k_ClearEntryUndo = "Clear Registry Entry";
        private const string k_EditDataUndo = "Edit Tree Data";

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

        /// <summary>Whether the key-contract foldout is open — remembered for the reason
        /// <see cref="m_ParametersOpen"/> is. CLOSED by default, unlike parameters: keys are
        /// wired from the fields that use them (⚿), so the section is reference material rather
        /// than the working surface, and two open foldouts above every state is a wall.</summary>
        private bool m_KeysOpen;

        /// <summary>Whether the data foldout is open — closed by default for the keys
        /// foldout's reason.</summary>
        private bool m_DataOpen;

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

        /// <summary>The keys this state's incoming transitions route — and what each of them carries
        /// — computed at most once per rebuild and thrown away with it. Every bindable field of every
        /// task asks the same question ("is my key one of the ones that arrive here, and is it my
        /// kind?") and the answer costs a walk of the whole tree plus a reflection pass over each
        /// source task, so it is asked once for a state with forty fields rather than forty times.
        /// Cleared at the top of <see cref="Rebuild"/>, because the answer changes when any transition
        /// anywhere in the tree does.</summary>
        private List<StateTreeEditorOps.RoutedKey> m_IncomingRoutedKeys;

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
            m_IncomingRoutedKeys = null;

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
            m_Root.Add(BuildTreeKeys());
            m_Root.Add(BuildTreeData());

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
            m_Root.Add(BuildTreeKeys());
            m_Root.Add(BuildTreeData());

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

        // --- tree keys: the M12 contract --------------------------------------------------

        /// <summary>
        /// The tree's declared KEYS — the names its tasks and conditions address state by: a
        /// blackboard entry, a context value, a world tag. A parameter above is a VALUE the tree
        /// receives; a key is a NAME it speaks. Declaring one gives the name an identity: fields
        /// wire to the declaration by id (the ⚿ button beside them), so renaming here renames
        /// every wired use, and the executor rewrites the runtime copy at StartTree so even trees
        /// that only see this one through an import catch up when they next run.
        ///
        /// Same two mounts as <see cref="BuildTreeParameters"/> and the same remembered foldout,
        /// for the same reasons.
        /// </summary>
        private VisualElement BuildTreeKeys()
        {
            HealKeyDeclarationIds(m_Tree);

            var keys = m_Tree.keys;
            var count = keys != null ? keys.Count : 0;
            var uses = m_Tree.uses;
            var imports = uses != null ? uses.Count : 0;

            var foldout = new Foldout
            {
                text = $"Keys · {StateTreeEditorOps.TreeDisplayName(m_Tree)} ({count}"
                    + (imports > 0 ? $" + {imports} imported tree(s))" : ")"),
                value = m_KeysOpen
            };
            foldout.style.marginTop = 2f;
            foldout.style.marginBottom = 4f;
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                    m_KeysOpen = evt.newValue;
            });

            var note = Hint(count == 0 && imports == 0
                ? "None declared. A key is a name this tree's tasks and conditions address state "
                + "by — a blackboard entry, a context value, a world tag. Declare it here and "
                + "fields wire to it (⚿) instead of holding matching text: renames follow, typos "
                + "stop compiling into silence."
                : "The names this tree owns. Fields wired to one (⚿) follow renames by id; the "
                + "kind filters which fields' pickers offer it. Another tree's keys are spoken "
                + "here by importing that tree below — or, at runtime, by mounting this tree "
                + "under it.");
            note.style.marginBottom = 4f;
            foldout.Add(note);

            for (var i = 0; i < count; ++i)
            {
                if (keys[i] != null)
                    foldout.Add(BuildKeyRow(i));
            }

            var add = new Button(AddTreeKey) { text = "Add Key" };
            add.style.marginTop = 4f;
            add.tooltip = "Declare a key this tree owns. Wire fields to it with their ⚿ button; "
                + "rename it here and every wired field follows.";
            foldout.Add(add);

            foldout.Add(BuildKeyImports());
            return foldout;
        }

        /// <summary>One declared key: name, kind, what it means, gone — the same
        /// refusal-under-the-row shape as <see cref="BuildDeclarationRow"/>.</summary>
        private VisualElement BuildKeyRow(int index)
        {
            var declaration = m_Tree.keys[index];

            var container = new VisualElement();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            container.Add(row);

            var refusal = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            refusal.style.display = DisplayStyle.None;
            container.Add(refusal);

            var name = new TextField
            {
                value = declaration.name ?? string.Empty,
                isDelayed = true
            };
            name.style.flexGrow = 1f;
            name.style.flexBasis = 0f;
            name.style.minWidth = k_KeyFieldMinWidth;
            name.style.marginRight = 2f;
            name.tooltip = "The name — what runtime dictionaries and tags actually use. Renaming "
                + "rewrites every field in THIS tree wired to it, silently: the wires are by id. "
                + "Trees that import this one catch up when they next start.";
            name.RegisterValueChangedCallback(evt => RenameTreeKey(index, evt.newValue, name,
                refusal));
            row.Add(name);

            var kind = new DropdownField(new List<string>(k_KeyKindChoices),
                (int)declaration.kind);
            kind.style.width = 92f;
            kind.style.flexShrink = 0f;
            kind.tooltip = "What the name addresses — the filter deciding which fields' ⚿ pickers "
                + "offer this key. An event is a presence key (raised by writing it, consumed by "
                + "clearing it); a tag is world vocabulary the registry matches.";
            kind.RegisterValueChangedCallback(evt => SetTreeKeyKind(index, kind.index));
            row.Add(kind);

            var description = new TextField
            {
                value = declaration.description ?? string.Empty,
                isDelayed = true
            };
            description.style.flexGrow = 1.2f;
            description.style.flexBasis = 0f;
            description.style.marginLeft = 2f;
            description.textEdition.placeholder = "what this key means";
            description.tooltip = "Shown wherever the key is offered.";
            description.RegisterValueChangedCallback(evt => CommitKeyDeclaration(index,
                entry => entry.description = evt.newValue ?? string.Empty));
            row.Add(description);

            var remove = new Button(() => RemoveTreeKey(index)) { text = "✕" };
            remove.tooltip = "Undeclare this key. Fields wired to it keep their text and warn in "
                + "place until re-wired or unwired — deleting it here cannot reach other trees.";
            remove.style.width = 22f;
            remove.style.flexShrink = 0f;
            row.Add(remove);

            return container;
        }

        /// <summary>
        /// Rename a declared key — refusing blanks and in-tree duplicates for the reasons
        /// <see cref="RenameTreeParameter"/> does — and rewrite every wired field in this tree to
        /// the new name. NO dialog and no offer, where the parameter path asks: a parameter's
        /// readers hold matching TEXT the editor can only guess about, while a key's wires are
        /// id-bound rows this window holds both ends of — the M7m doctrine one level up. One undo
        /// step covers the name and every rewrite.
        /// </summary>
        private void RenameTreeKey(int index, string requested, TextField field, HelpBox refusal)
        {
            if (!TryGetKeyDeclaration(index, out var entry))
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
                Refuse(refusal, "A key needs a name: the name IS what runs.");
                field.SetValueWithoutNotify(current);
                return;
            }

            if (DeclaresKeyName(trimmed, index))
            {
                Refuse(refusal, $"'{trimmed}' is already declared by this tree. Two declarations "
                    + "sharing a name are one runtime key wearing two ids — every wire looks fine "
                    + "and half of them mean the other one.");
                field.SetValueWithoutNotify(current);
                return;
            }

            Refuse(refusal, null);

            var group = StateTreeEditorOps.BeginUndoGroup(k_RenameKeyUndo);
            Undo.RecordObject(m_Tree, k_RenameKeyUndo);
            entry.name = trimmed;
            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.RewriteLinkedKeyFields(m_Tree, entry.id, trimmed, k_RenameKeyUndo);
            StateTreeEditorOps.EndUndoGroup(group);

            field.SetValueWithoutNotify(trimmed);
            RebuildPane();
        }

        private void AddTreeKey()
        {
            if (m_Tree == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_AddKeyUndo);
            Undo.RecordObject(m_Tree, k_AddKeyUndo);

            if (m_Tree.keys == null)
                m_Tree.keys = new List<StateTreeKeyDeclaration>();

            m_Tree.keys.Add(new StateTreeKeyDeclaration
            {
                id = NewParameterId(),
                name = UniqueKeyName(),
                kind = StateTreeKeyKind.Float
            });

            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private void RemoveTreeKey(int index)
        {
            if (!TryGetKeyDeclaration(index, out _))
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_RemoveKeyUndo);
            Undo.RecordObject(m_Tree, k_RemoveKeyUndo);
            m_Tree.keys.RemoveAt(index);
            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        /// <summary>Rebuilds rather than swapping the row like the parameter path: a key's kind
        /// changes which field pickers offer it and which wired rows warn of a mismatch, and those
        /// live all over the pane.</summary>
        private void SetTreeKeyKind(int index, int choice)
        {
            if (choice < 0 || choice >= k_KeyKindChoices.Length)
                return;

            CommitKeyDeclaration(index, entry => entry.kind = (StateTreeKeyKind)choice);
            RebuildPane();
        }

        private void CommitKeyDeclaration(int index, Action<StateTreeKeyDeclaration> write)
        {
            if (!TryGetKeyDeclaration(index, out var entry))
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditKeyUndo);
            Undo.RecordObject(m_Tree, k_EditKeyUndo);
            write(entry);
            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Edited?.Invoke();
        }

        private bool TryGetKeyDeclaration(int index, out StateTreeKeyDeclaration declaration)
        {
            declaration = null;
            var keys = m_Tree != null ? m_Tree.keys : null;
            if (keys == null || index < 0 || index >= keys.Count)
                return false;

            declaration = keys[index];
            return declaration != null;
        }

        private bool DeclaresKeyName(string name, int except)
        {
            var keys = m_Tree != null ? m_Tree.keys : null;
            for (var i = 0; keys != null && i < keys.Count; ++i)
            {
                if (i == except || keys[i] == null)
                    continue;
                if (string.Equals(keys[i].name, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private string UniqueKeyName()
        {
            const string stem = "key";
            if (!DeclaresKeyName(stem, -1))
                return stem;

            for (var i = 2; i < 1000; ++i)
            {
                var candidate = stem + i;
                if (!DeclaresKeyName(candidate, -1))
                    return candidate;
            }

            return stem + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>The keys mirror of <see cref="HealDeclarationIds"/>, with its no-undo
        /// reasoning.</summary>
        private static void HealKeyDeclarationIds(StateTreeAsset tree)
        {
            var keys = tree != null ? tree.keys : null;
            if (keys == null)
                return;

            var healed = false;
            for (var i = 0; i < keys.Count; ++i)
            {
                var entry = keys[i];
                if (entry == null || !string.IsNullOrEmpty(entry.id))
                    continue;

                entry.id = NewParameterId();
                healed = true;
            }

            if (healed)
                EditorUtility.SetDirty(tree);
        }

        /// <summary>The `uses` list — the HORIZONTAL share, a visible dependency instead of
        /// matching text. Vertical sharing has no UI because it has no data: mounting this tree
        /// under another at runtime is the share.</summary>
        private VisualElement BuildKeyImports()
        {
            var container = new VisualElement();
            container.style.marginTop = 6f;

            var title = new Label("Imported keys");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(title);

            var uses = m_Tree.uses;
            for (var i = 0; uses != null && i < uses.Count; ++i)
                container.Add(BuildImportRow(i));

            var add = new ObjectField("Import from")
            {
                objectType = typeof(StateTreeAsset),
                allowSceneObjects = false
            };
            add.tooltip = "Drop a tree here to speak its declared keys — they appear in this "
                + "tree's ⚿ pickers under the imported tree's name, along with its own imports.";
            add.RegisterValueChangedCallback(evt =>
            {
                var import = evt.newValue as StateTreeAsset;
                add.SetValueWithoutNotify(null);
                AddKeyImport(import);
            });
            container.Add(add);

            return container;
        }

        private VisualElement BuildImportRow(int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var field = new ObjectField
            {
                value = m_Tree.uses[index],
                objectType = typeof(StateTreeAsset),
                allowSceneObjects = false
            };
            field.style.flexGrow = 1f;
            field.RegisterValueChangedCallback(evt =>
            {
                var replacement = evt.newValue as StateTreeAsset;
                if (replacement == null || replacement == m_Tree
                    || ImportsKeyTree(replacement, index))
                {
                    field.SetValueWithoutNotify(m_Tree.uses[index]);
                    return;
                }

                var group = StateTreeEditorOps.BeginUndoGroup(k_EditImportsUndo);
                Undo.RecordObject(m_Tree, k_EditImportsUndo);
                m_Tree.uses[index] = replacement;
                EditorUtility.SetDirty(m_Tree);
                StateTreeEditorOps.EndUndoGroup(group);
                RebuildPane();
            });
            row.Add(field);

            var remove = new Button(() => RemoveKeyImport(index)) { text = "✕" };
            remove.tooltip = "Stop importing this tree's keys. Fields wired to one of them keep "
                + "their text and warn in place.";
            remove.style.width = 22f;
            remove.style.flexShrink = 0f;
            row.Add(remove);

            return row;
        }

        private void AddKeyImport(StateTreeAsset import)
        {
            if (m_Tree == null || import == null || import == m_Tree
                || ImportsKeyTree(import, -1))
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditImportsUndo);
            Undo.RecordObject(m_Tree, k_EditImportsUndo);

            if (m_Tree.uses == null)
                m_Tree.uses = new List<StateTreeAsset>();
            m_Tree.uses.Add(import);

            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private void RemoveKeyImport(int index)
        {
            var uses = m_Tree != null ? m_Tree.uses : null;
            if (uses == null || index < 0 || index >= uses.Count)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditImportsUndo);
            Undo.RecordObject(m_Tree, k_EditImportsUndo);
            uses.RemoveAt(index);
            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private bool ImportsKeyTree(StateTreeAsset candidate, int except)
        {
            var uses = m_Tree != null ? m_Tree.uses : null;
            for (var i = 0; uses != null && i < uses.Count; ++i)
            {
                if (i != except && uses[i] == candidate)
                    return true;
            }

            return false;
        }

        // --- key-semantic fields: the ⚿ wire ----------------------------------------------

        /// <summary>Every task/condition field enters here: a field marked
        /// <see cref="StateTreeKeyAttribute"/> gets the key-contract treatment, everything else
        /// goes straight to the ⚑ machinery unchanged.</summary>
        private VisualElement BuildFieldRow(PropertyField field, UnityEngine.Object target,
            string fieldName, StateTreeFieldBinding.TargetKind kind, int targetIndex)
        {
            if (targetIndex >= 0 && m_Node != null && m_Tree != null)
            {
                if (StateTreeEditorOps.TryGetEntryRefField(target, fieldName,
                        out var entryReference))
                    return BuildEntryRefField(target, fieldName, entryReference);
                if (StateTreeEditorOps.TryGetRegistryRefField(target, fieldName,
                        out var registryReference))
                    return BuildRegistryRefField(fieldName, registryReference);
                if (StateTreeEditorOps.TryGetServiceRefField(target, fieldName,
                        out var serviceReference))
                    return BuildServiceRefField(fieldName, serviceReference);
                if (StateTreeEditorOps.TryGetKeyField(target, fieldName, out var keyKind,
                        out var anyKind))
                    return BuildKeyContractField(target, fieldName, keyKind, anyKind);
            }

            return BuildBindableField(field, target, fieldName, kind, targetIndex);
        }

        // --- data registries: the ⛃ reference -----------------------------------------------

        /// <summary>
        /// A typed ENTRY reference (M13). There is no text to type and no lock to need: the
        /// row shows the entry's CURRENT name resolved from the tree's registries by id (so
        /// renames in the dashboard are already reflected), the ⛃ menu offers every entry of
        /// the right class the tree's data lists, and ✕ empties the slot. An id that resolves
        /// nowhere gets the stale-link shape; an empty slot just says so — whether that is an
        /// error is the task's own business at runtime.
        /// </summary>
        private VisualElement BuildEntryRefField(UnityEngine.Object target, string fieldName,
            IStateTreeEntryRef reference)
        {
            var container = new VisualElement();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = k_RowMinHeight;
            container.Add(row);

            StateTreeRegistryEntry entry = StateTreeEditorOps.FindTreeEntry(m_Tree,
                reference.EntryType, reference.EntryId);
            var unresolved = !string.IsNullOrEmpty(reference.EntryId) && entry == null;

            var display = new TextField(ObjectNames.NicifyVariableName(fieldName))
            {
                value = entry != null ? entry.name : unresolved ? "(missing entry)" : "(none)",
                isReadOnly = true
            };
            display.AddToClassList(TextField.alignedFieldUssClassName);
            display.style.flexGrow = 1f;
            display.style.flexShrink = 1f;
            display.style.minHeight = k_ControlMinHeight;
            ApplyOverrideStyle(display, false);
            display.tooltip = entry != null
                ? $"'{fieldName}' references the {reference.EntryType.Name} entry "
                + $"'{entry.name}' by id — rename it in the registry dashboard and this "
                + "reference follows."
                : $"'{fieldName}' is a typed {reference.EntryType.Name} reference — pick an "
                + "entry from the tree's data (⛃).";
            row.Add(display);

            var pick = new Button { text = k_EntryGlyph };
            pick.style.flexShrink = 0f;
            EnlargeRowButton(pick, k_GlyphMinWidth);
            pick.tooltip = "Pick an entry from the registries this tree lists in its Data "
                + "section.";
            pick.clicked += () => ShowEntryPickMenu(target, fieldName, reference);
            row.Add(pick);

            if (!string.IsNullOrEmpty(reference.EntryId))
            {
                var clear = new Button { text = "✕" };
                clear.style.width = 26f;
                clear.style.minHeight = k_ControlMinHeight;
                clear.style.flexShrink = 0f;
                clear.tooltip = "Empty the slot.";
                clear.clicked += () =>
                {
                    var group = StateTreeEditorOps.BeginUndoGroup(k_ClearEntryUndo);
                    StateTreeEditorOps.ClearEntryRef(target, fieldName, k_ClearEntryUndo);
                    StateTreeEditorOps.EndUndoGroup(group);
                    RebuildPane();
                };
                row.Add(clear);
            }

            if (unresolved)
            {
                var cached = EntryNameCache(target, fieldName);
                var help = new HelpBox($"'{fieldName}' references an entry that no registry in "
                    + "this tree's Data section holds"
                    + (string.IsNullOrEmpty(cached) ? string.Empty : $" (it was '{cached}')")
                    + " — deleted, or its registry was removed. The task will run with an empty "
                    + "reference. Re-pick (⛃) or clear (✕).", HelpBoxMessageType.Warning);
                help.style.marginTop = 2f;
                container.Add(help);
            }
            else if (m_Tree.registries == null || m_Tree.registries.Count == 0)
            {
                container.Add(Hint("The tree lists no registries yet — add one in the Data "
                    + "section of the tree header."));
            }

            return container;
        }

        private static string EntryNameCache(UnityEngine.Object target, string fieldName)
        {
            var field = target != null
                ? target.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                : null;
            object reference = field?.GetValue(target);
            var nameField = reference?.GetType().GetField("entryName",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            return nameField?.GetValue(reference) as string ?? string.Empty;
        }

        /// <summary>A capability requirement (M15): nothing to author — which instance answers
        /// is a fact of WHERE the tree runs, so the row just states the contract. No warning
        /// flavor at edit time: the asset cannot know its future spine.</summary>
        private VisualElement BuildServiceRefField(string fieldName,
            IStateTreeServiceRef reference)
        {
            var display = new TextField(ObjectNames.NicifyVariableName(fieldName))
            {
                value = "← " + reference.ServiceType.Name + " from the spine",
                isReadOnly = true
            };
            display.AddToClassList(TextField.alignedFieldUssClassName);
            display.style.minHeight = k_ControlMinHeight;
            ApplyOverrideStyle(display, false);
            display.tooltip = $"'{fieldName}' is injected at start from the running owner's "
                + $"context chain — the nearest host providing {reference.ServiceType.Name} "
                + "answers, so the same tree mounted under two players gets each player's own "
                + "service. Provide the capability with a service component or an installer.";
            return display;
        }

        /// <summary>A whole-registry reference: nothing to edit — the row states which of the
        /// tree's registries answers it, by the executor's first-assignable rule, or warns
        /// that none will.</summary>
        private VisualElement BuildRegistryRefField(string fieldName,
            IStateTreeRegistryRef reference)
        {
            StateTreeRegistryAsset registry = StateTreeEditorOps.FindTreeRegistry(m_Tree,
                reference.EntryType);
            if (registry != null)
            {
                var display = new TextField(ObjectNames.NicifyVariableName(fieldName))
                {
                    value = "← " + registry.name,
                    isReadOnly = true
                };
                display.AddToClassList(TextField.alignedFieldUssClassName);
                display.style.minHeight = k_ControlMinHeight;
                ApplyOverrideStyle(display, false);
                display.tooltip = $"'{fieldName}' resolves to the tree's first "
                    + $"{reference.EntryType.Name} registry at start — currently "
                    + $"'{registry.name}' from the Data section.";
                return display;
            }

            var help = new HelpBox($"'{fieldName}' wants a registry of "
                + $"{reference.EntryType.Name} entries and this tree's Data section lists none "
                + "— add the registry asset there.", HelpBoxMessageType.Warning);
            help.style.marginTop = 2f;
            return help;
        }

        /// <summary>The ⛃ menu: every entry of the right class from every registry the tree
        /// lists, pathed registry/group/entry so the registry's own grouping IS the menu's.</summary>
        private void ShowEntryPickMenu(UnityEngine.Object target, string fieldName,
            IStateTreeEntryRef reference)
        {
            var menu = new GenericMenu();
            var offered = 0;
            var registries = m_Tree.registries;

            for (var r = 0; registries != null && r < registries.Count; ++r)
            {
                var registry = registries[r];
                if (registry == null
                    || !reference.EntryType.IsAssignableFrom(registry.entryType))
                    continue;

                var prefix = registry.name.Replace('/', k_MenuSeparatorStandIn) + "/";
                for (var i = 0; i < registry.Count; ++i)
                {
                    StateTreeRegistryEntry entry = registry.EntryAt(i);
                    if (entry == null || string.IsNullOrEmpty(entry.id)
                        || string.IsNullOrEmpty(entry.name))
                        continue;

                    var path = prefix
                        + (string.IsNullOrEmpty(entry.group)
                            ? string.Empty
                            : entry.group.Trim('/') + "/")
                        + entry.name.Replace('/', k_MenuSeparatorStandIn);
                    var picked = entry;
                    menu.AddItem(new GUIContent(path),
                        string.Equals(entry.id, reference.EntryId, StringComparison.Ordinal),
                        () =>
                        {
                            var group = StateTreeEditorOps.BeginUndoGroup(k_PickEntryUndo);
                            StateTreeEditorOps.SetEntryRef(target, fieldName, picked,
                                k_PickEntryUndo);
                            StateTreeEditorOps.EndUndoGroup(group);
                            RebuildPane();
                        });
                    ++offered;
                }
            }

            if (offered == 0)
            {
                menu.AddDisabledItem(new GUIContent(
                    $"No {reference.EntryType.Name} entries — list the registry in the tree's "
                    + "Data section first"));
            }

            menu.ShowAsContext();
        }

        // --- tree data: the registries ------------------------------------------------------

        /// <summary>The tree's DATA connection (M13): which registries its typed reference
        /// fields resolve against. The list is the whole mechanism — entries are edited on
        /// the registry asset itself (its inspector is the dashboard).</summary>
        private VisualElement BuildTreeData()
        {
            var registries = m_Tree.registries;
            var count = registries != null ? registries.Count : 0;

            var foldout = new Foldout
            {
                text = $"Data · {StateTreeEditorOps.TreeDisplayName(m_Tree)} ({count})",
                value = m_DataOpen
            };
            foldout.style.marginTop = 2f;
            foldout.style.marginBottom = 4f;
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                    m_DataOpen = evt.newValue;
            });

            var note = Hint(count == 0
                ? "No registries listed. A registry is a catalog of typed entries (items, "
                + "recipes, archetypes) edited on its own asset like a dashboard; list it here "
                + "and every typed reference field (⛃) in this tree picks from it."
                : "The catalogs this tree speaks. A typed reference field (⛃) picks entries "
                + "from these; a whole-registry field takes the first listed registry of its "
                + "entry class. Select a registry asset to edit its entries.");
            note.style.marginBottom = 4f;
            foldout.Add(note);

            for (var i = 0; i < count; ++i)
                foldout.Add(BuildRegistryRow(i));

            var add = new ObjectField("Add registry")
            {
                objectType = typeof(StateTreeRegistryAsset),
                allowSceneObjects = false
            };
            add.tooltip = "Drop a registry asset here to make its entries pickable in this "
                + "tree.";
            add.RegisterValueChangedCallback(evt =>
            {
                var registry = evt.newValue as StateTreeRegistryAsset;
                add.SetValueWithoutNotify(null);
                AddTreeRegistry(registry);
            });
            foldout.Add(add);

            return foldout;
        }

        private VisualElement BuildRegistryRow(int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var registry = m_Tree.registries[index];
            var field = new ObjectField(registry != null ? registry.entryType.Name : "(missing)")
            {
                value = registry,
                objectType = typeof(StateTreeRegistryAsset),
                allowSceneObjects = false
            };
            field.style.flexGrow = 1f;
            field.tooltip = "The label is the entry class this registry holds — what decides "
                + "which ⛃ fields it answers.";
            field.RegisterValueChangedCallback(evt =>
            {
                var replacement = evt.newValue as StateTreeRegistryAsset;
                if (replacement == null || ListsTreeRegistry(replacement, index))
                {
                    field.SetValueWithoutNotify(m_Tree.registries[index]);
                    return;
                }

                var group = StateTreeEditorOps.BeginUndoGroup(k_EditDataUndo);
                Undo.RecordObject(m_Tree, k_EditDataUndo);
                m_Tree.registries[index] = replacement;
                EditorUtility.SetDirty(m_Tree);
                StateTreeEditorOps.EndUndoGroup(group);
                RebuildPane();
            });
            row.Add(field);

            var remove = new Button(() => RemoveTreeRegistry(index)) { text = "✕" };
            remove.tooltip = "Stop listing this registry. Reference fields aimed at its "
                + "entries warn in place until re-picked.";
            remove.style.width = 22f;
            remove.style.flexShrink = 0f;
            row.Add(remove);

            return row;
        }

        private void AddTreeRegistry(StateTreeRegistryAsset registry)
        {
            if (m_Tree == null || registry == null || ListsTreeRegistry(registry, -1))
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditDataUndo);
            Undo.RecordObject(m_Tree, k_EditDataUndo);

            if (m_Tree.registries == null)
                m_Tree.registries = new List<StateTreeRegistryAsset>();
            m_Tree.registries.Add(registry);

            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private void RemoveTreeRegistry(int index)
        {
            var registries = m_Tree != null ? m_Tree.registries : null;
            if (registries == null || index < 0 || index >= registries.Count)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditDataUndo);
            Undo.RecordObject(m_Tree, k_EditDataUndo);
            registries.RemoveAt(index);
            EditorUtility.SetDirty(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private bool ListsTreeRegistry(StateTreeRegistryAsset candidate, int except)
        {
            var registries = m_Tree != null ? m_Tree.registries : null;
            for (var i = 0; registries != null && i < registries.Count; ++i)
            {
                if (i != except && registries[i] == candidate)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// A key-semantic field — since M14 a <see cref="StateTreeKeyField"/> wrapper, so the
        /// wire is read from and written to the FIELD ITSELF: no parallel row, no
        /// field-by-index addressing, and nothing here to keep in agreement with the executor.
        /// UNWIRED, the row is the editable text plus the ⚿ button. WIRED, the declaration
        /// owns the name (the M7m rule one level up): a read-only display of the declaration's
        /// CURRENT name — which after a rename in an imported tree is the truth whatever the
        /// serialized fallback still says — and changing anything is a deliberate gesture:
        /// ⚿ re-wires, ✕ unwires. A wire whose declaration is GONE degrades exactly like the
        /// executor does: the typed text runs, the field is editable again, and the row says
        /// so in the stale-link shape.
        /// </summary>
        private VisualElement BuildKeyContractField(UnityEngine.Object target, string fieldName,
            StateTreeKeyKind keyKind, bool anyKind)
        {
            StateTreeKeyField key = StateTreeEditorOps.GetKeyField(target, fieldName);
            if (key == null)
                return new VisualElement();

            var container = new VisualElement();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = k_RowMinHeight;
            container.Add(row);

            // Edit-time resolution is own + imports only — the mount chain is a runtime fact.
            var declaration = key.isWired
                ? StateTreeKeyResolver.Find(m_Tree, null, key.keyId)
                : null;
            var stale = key.isWired && declaration == null;

            if (declaration != null)
            {
                var standIn = new TextField(ObjectNames.NicifyVariableName(fieldName))
                {
                    value = declaration.name,
                    isReadOnly = true
                };
                standIn.AddToClassList(TextField.alignedFieldUssClassName);
                standIn.style.flexGrow = 1f;
                standIn.style.flexShrink = 1f;
                standIn.style.minHeight = k_ControlMinHeight;
                ApplyOverrideStyle(standIn, false);
                standIn.tooltip = $"'{fieldName}' is wired to the declared key "
                    + $"'{declaration.name}' ({KeyKindLabel(declaration.kind)}) by id — renaming "
                    + "the declaration renames every wired use with it."
                    + (string.IsNullOrEmpty(declaration.description)
                        ? string.Empty
                        : " " + declaration.description);
                row.Add(standIn);
            }
            else
            {
                var text = new TextField(ObjectNames.NicifyVariableName(fieldName))
                {
                    value = key.text ?? string.Empty,
                    isDelayed = true
                };
                text.AddToClassList(TextField.alignedFieldUssClassName);
                text.style.flexGrow = 1f;
                text.style.flexShrink = 1f;
                text.style.minHeight = k_ControlMinHeight;
                text.tooltip = $"'{fieldName}' names a "
                    + (anyKind ? "key" : $"{KeyKindLabel(keyKind)} key")
                    + " as free text. Wire it to a declared key (⚿) and renames follow by id.";
                text.RegisterValueChangedCallback(evt =>
                {
                    var group = StateTreeEditorOps.BeginUndoGroup(k_EditFieldKeyUndo);
                    StateTreeEditorOps.SetKeyFieldText(target, fieldName, evt.newValue,
                        k_EditFieldKeyUndo);
                    StateTreeEditorOps.EndUndoGroup(group);
                    m_Edited?.Invoke();
                });
                row.Add(text);
            }

            row.Add(BuildKeyPickButton(target, fieldName, keyKind, anyKind, key));
            if (key.isWired)
                row.Add(BuildKeyUnlinkButton(target, fieldName, stale));

            if (stale)
            {
                var help = new HelpBox($"'{fieldName}' is wired to a declared key that no longer "
                    + "resolves here — deleted, or its tree dropped from the imports. The typed "
                    + "text runs as a plain key (a mounted ancestor may still resolve the id at "
                    + "runtime). Re-wire it (⚿) or unwire it (✕).",
                    HelpBoxMessageType.Warning);
                help.style.marginTop = 2f;
                container.Add(help);
            }
            else if (declaration != null && !anyKind && declaration.kind != keyKind)
            {
                // Not on an any-kind field: those atoms genuinely take a key of every kind
                // (clearing an event, presence-testing a string is Tuesday) — a note would
                // cry wolf.
                var help = new HelpBox($"'{declaration.name}' is declared as "
                    + $"{KeyKindLabel(declaration.kind)}; this field expects a "
                    + $"{KeyKindLabel(keyKind)} key. The name still flows — kinds filter the "
                    + "pickers, the runtime never checks them — but one of the two is probably "
                    + "mislabeled.", HelpBoxMessageType.Info);
                help.style.marginTop = 2f;
                container.Add(help);
            }

            return container;
        }

        private Button BuildKeyPickButton(UnityEngine.Object target, string fieldName,
            StateTreeKeyKind keyKind, bool anyKind, StateTreeKeyField key)
        {
            var pick = new Button { text = k_KeyGlyph };
            pick.style.flexShrink = 0f;
            EnlargeRowButton(pick, k_GlyphMinWidth);
            pick.tooltip = !key.isWired
                ? $"'{fieldName}' names a "
                + (anyKind ? "key" : $"{KeyKindLabel(keyKind)} key")
                + " as plain text. Wire it to a declared key instead — the wire is by id, so "
                + "renaming the declaration renames this field with it."
                : "Wire this field to a different declared key.";
            pick.clicked += () => ShowKeyContractMenu(target, fieldName, keyKind, anyKind, key);
            return pick;
        }

        private Button BuildKeyUnlinkButton(UnityEngine.Object target, string fieldName,
            bool stale)
        {
            var unlink = new Button { text = "✕" };
            unlink.style.width = 26f;
            unlink.style.minHeight = k_ControlMinHeight;
            unlink.style.flexShrink = 0f;
            unlink.tooltip = stale
                ? "Drop the dead wire — the typed text keeps running, now without the warning."
                : "Unwire this field from the declared key. The name it holds stays as plain text "
                + "and stops following renames.";
            unlink.clicked += () =>
            {
                var group = StateTreeEditorOps.BeginUndoGroup(k_UnlinkKeyUndo);
                StateTreeEditorOps.UnwireKeyField(target, fieldName, k_UnlinkKeyUndo);
                StateTreeEditorOps.EndUndoGroup(group);
                RebuildPane();
            };
            return unlink;
        }

        /// <summary>A ⚿ menu row waiting to be emitted: the label carries its source prefix, the
        /// declaration is what picking it wires.</summary>
        private struct KeyMenuEntry
        {
            public string label;
            public StateTreeKeyDeclaration declaration;
        }

        /// <summary>
        /// The ⚿ menu: this tree's declarations at the top level, each import's under its tree
        /// name — presentation mirroring resolution order, nearest first. Kind-filtered, because
        /// that is what the kinds are FOR — except on an any-kind field, where every other kind
        /// follows below a separator, each row saying which kind it is: the generic atoms work on
        /// all of them and a filter would refuse real wires. The last item promotes the text
        /// already in the field to a declaration on this tree in one gesture, offered only while
        /// no visible declaration carries that name — otherwise the item above IS the right
        /// gesture.
        /// </summary>
        private void ShowKeyContractMenu(UnityEngine.Object target, string fieldName,
            StateTreeKeyKind keyKind, bool anyKind, StateTreeKeyField key)
        {
            var linkedId = key.isWired ? key.keyId : null;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var matching = new List<KeyMenuEntry>();
            CollectKeyMenuEntries(matching, keyKind, seen);
            var others = new List<KeyMenuEntry>();
            if (anyKind)
                CollectKeyMenuEntries(others, null, seen);

            var menu = new GenericMenu();
            for (var i = 0; i < matching.Count; ++i)
                AddKeyMenuItem(menu, matching[i], string.Empty, linkedId, target, fieldName);

            if (matching.Count == 0 && others.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(
                    (anyKind ? "No keys declared" : $"No {KeyKindLabel(keyKind)} keys declared")
                    + " — add one in the tree's Keys section"));
            }

            if (others.Count > 0)
            {
                if (matching.Count > 0)
                    menu.AddSeparator(string.Empty);
                for (var i = 0; i < others.Count; ++i)
                    AddKeyMenuItem(menu, others[i],
                        "  · " + KeyKindLabel(others[i].declaration.kind), linkedId, target,
                        fieldName);
            }

            var text = key.text;
            if (!string.IsNullOrEmpty(text) && !VisibleKeyNameExists(text))
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent(
                    $"Declare '{text.Replace('/', k_MenuSeparatorStandIn)}' on this tree"),
                    false, () => DeclareAndLinkKey(text, keyKind, target, fieldName));
            }

            menu.ShowAsContext();
        }

        private void AddKeyMenuItem(GenericMenu menu, KeyMenuEntry entry, string suffix,
            string linkedId, UnityEngine.Object target, string fieldName)
        {
            var declaration = entry.declaration;
            menu.AddItem(new GUIContent(entry.label + suffix),
                string.Equals(declaration.id, linkedId, StringComparison.Ordinal),
                () => LinkFieldKey(target, fieldName, declaration));
        }

        /// <summary>One pass over everything visible, in resolution order: own declarations bare,
        /// each import's under its tree name — an import listing everything IT can see, because
        /// its own imports arrive through it and belong under its name. Deduped by id ACROSS
        /// passes through the shared set — nearest wins, so the first appearance is the resolving
        /// one. A null filter takes every kind (the any-kind second pass).</summary>
        private void CollectKeyMenuEntries(List<KeyMenuEntry> into, StateTreeKeyKind? filterKind,
            HashSet<string> seen)
        {
            CollectSourceKeyEntries(into, m_Tree, string.Empty, filterKind, seen, ownOnly: true);

            var uses = m_Tree.uses;
            for (var i = 0; uses != null && i < uses.Count; ++i)
            {
                var import = uses[i];
                if (import == null || import == m_Tree)
                    continue;

                var prefix = StateTreeEditorOps.TreeDisplayName(import)
                    .Replace('/', k_MenuSeparatorStandIn) + "/";
                CollectSourceKeyEntries(into, import, prefix, filterKind, seen, ownOnly: false);
            }
        }

        private static void CollectSourceKeyEntries(List<KeyMenuEntry> into,
            StateTreeAsset source, string prefix, StateTreeKeyKind? filterKind,
            HashSet<string> seen, bool ownOnly)
        {
            var declarations = new List<StateTreeKeyDeclaration>();
            if (ownOnly)
            {
                var own = source.keys;
                for (var i = 0; own != null && i < own.Count; ++i)
                {
                    if (own[i] != null)
                        declarations.Add(own[i]);
                }
            }
            else
            {
                StateTreeKeyResolver.CollectVisible(source, declarations);
            }

            for (var i = 0; i < declarations.Count; ++i)
            {
                var entry = declarations[i];
                if (string.IsNullOrEmpty(entry.id) || string.IsNullOrEmpty(entry.name)
                    || (filterKind.HasValue && entry.kind != filterKind.Value)
                    || !seen.Add(entry.id))
                    continue;

                into.Add(new KeyMenuEntry
                {
                    label = prefix + entry.name.Replace('/', k_MenuSeparatorStandIn),
                    declaration = entry
                });
            }
        }

        private void LinkFieldKey(UnityEngine.Object target, string fieldName,
            StateTreeKeyDeclaration declaration)
        {
            var group = StateTreeEditorOps.BeginUndoGroup(k_LinkKeyUndo);
            StateTreeEditorOps.WireKeyField(target, fieldName, declaration, k_LinkKeyUndo);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        /// <summary>The promote: declaration and wire in one undo step. The name arrives already
        /// carried by the field, so none of the rename refusals can apply — except a duplicate on
        /// this tree, which the menu's visibility check already ruled out.</summary>
        private void DeclareAndLinkKey(string name, StateTreeKeyKind keyKind,
            UnityEngine.Object target, string fieldName)
        {
            if (m_Tree == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_LinkKeyUndo);
            Undo.RecordObject(m_Tree, k_LinkKeyUndo);

            if (m_Tree.keys == null)
                m_Tree.keys = new List<StateTreeKeyDeclaration>();

            var declaration = new StateTreeKeyDeclaration
            {
                id = NewParameterId(),
                name = name,
                kind = keyKind
            };
            m_Tree.keys.Add(declaration);
            EditorUtility.SetDirty(m_Tree);

            StateTreeEditorOps.WireKeyField(target, fieldName, declaration, k_LinkKeyUndo);
            StateTreeEditorOps.EndUndoGroup(group);
            RebuildPane();
        }

        private static string KeyKindLabel(StateTreeKeyKind kind)
        {
            var index = (int)kind;
            return index >= 0 && index < k_KeyKindChoices.Length
                ? k_KeyKindChoices[index]
                : kind.ToString();
        }

        private bool VisibleKeyNameExists(string name)
        {
            var visible = new List<StateTreeKeyDeclaration>();
            StateTreeKeyResolver.CollectVisible(m_Tree, visible);
            for (var i = 0; i < visible.Count; ++i)
            {
                if (visible[i] != null
                    && string.Equals(visible[i].name, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
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

            // THE M22 COMPLETION KNOBS, beside the identity because they are what the state
            // MEANS: when it counts as done, and where done goes when no edge says.
            var completeField = new EnumField("Complete When", m_Node.completeWhen);
            completeField.tooltip = "All Tasks — every blocking task finished (the default). "
                + "Any Task — one finisher is enough; the rest are cancelled on leave. "
                + "Never — a resident state: interrupts are the only way out.";
            completeField.RegisterValueChangedCallback(evt =>
            {
                var group = StateTreeEditorOps.BeginUndoGroup("Edit Completion");
                Undo.RecordObject(m_Node, "Edit Completion");
                m_Node.completeWhen = (StateTreeCompleteWhen)evt.newValue;
                EditorUtility.SetDirty(m_Node);
                StateTreeEditorOps.EndUndoGroup(group);
                m_StructuralChanged?.Invoke();
            });
            m_Root.Add(completeField);

            var flowField = new EnumField("On Complete", m_Node.completionFlow);
            flowField.tooltip = "Where completion goes when none of the declared transitions "
                + "fire. Next Sibling — the implicit sequence: children in order, a last child "
                + "bubbles to its parent, the root finishing ends the tree. Hold — complete "
                + "but stay, keeping the declared edges live.";
            flowField.RegisterValueChangedCallback(evt =>
            {
                var group = StateTreeEditorOps.BeginUndoGroup("Edit Completion");
                Undo.RecordObject(m_Node, "Edit Completion");
                m_Node.completionFlow = (StateTreeCompletionFlow)evt.newValue;
                EditorUtility.SetDirty(m_Node);
                StateTreeEditorOps.EndUndoGroup(group);
                m_StructuralChanged?.Invoke();
            });
            m_Root.Add(flowField);

            // The GHOST EDGE, spelled out (M22): what actually happens on completion with the
            // settings above and no declared edge firing. Dim, because it is the default — but
            // present, because a default nobody can see is a trap.
            var ghost = StateTreeEditorOps.DescribeImplicitFlow(m_Tree, m_Node);
            if (!string.IsNullOrEmpty(ghost))
            {
                var ghostLabel = new Label(ghost);
                ghostLabel.style.whiteSpace = WhiteSpace.Normal;
                ghostLabel.style.opacity = 0.65f;
                ghostLabel.style.marginLeft = 4f;
                ghostLabel.style.marginBottom = 2f;
                m_Root.Add(ghostLabel);
            }

            // THE ROLE (M23 tree-nesting): in a tree a service claims, what this state IS in
            // that service's vocabulary — offered from the rules, changeable among what the
            // parent allows, exactly the HT ability editor's changeable-but-lawful type.
            var service = StateTreeEditorOps.ServiceClaiming(m_Tree);
            if (service != null && m_Node != m_Tree.root)
            {
                StateTreeNodeAsset parent = StateTreeEditorOps.ParentOf(m_Tree, m_Node);
                string parentRole = parent != null
                    ? StateTreeEditorOps.EffectiveRoleOf(m_Tree, parent)
                    : service.treeKind;
                var allowed = service.AllowedUnder(parentRole);
                var choices = new List<string> { "(state)" };
                for (var i = 0; i < allowed.Count; ++i)
                    choices.Add(allowed[i]);
                string current = string.IsNullOrEmpty(m_Node.roleKind)
                    ? "(state)"
                    : m_Node.roleKind;
                bool illegal = current != "(state)" && !choices.Contains(current);
                if (illegal)
                    choices.Add(current);   // shown so the error can name it, never offered

                var roleField = new DropdownField("Role", choices,
                    Mathf.Max(0, choices.IndexOf(current)));
                roleField.tooltip = "What this state IS to the '" + service.serviceName
                    + "' service. '(state)' is a plain state — transparent to the rules. "
                    + "Under '" + parentRole + "' the rules allow: ["
                    + string.Join(", ", allowed) + "].";
                roleField.RegisterValueChangedCallback(evt =>
                {
                    var group = StateTreeEditorOps.BeginUndoGroup("Edit Role");
                    Undo.RecordObject(m_Node, "Edit Role");
                    m_Node.roleKind = evt.newValue == "(state)" ? "" : evt.newValue;
                    EditorUtility.SetDirty(m_Node);
                    StateTreeEditorOps.EndUndoGroup(group);
                    m_StructuralChanged?.Invoke();
                });
                m_Root.Add(roleField);

                if (illegal)
                {
                    m_Root.Add(new HelpBox("A '" + current + "' cannot sit under '"
                        + parentRole + "' — the '" + service.serviceName + "' rules allow ["
                        + string.Join(", ", allowed) + "]. Re-pick the role or move the "
                        + "state.", HelpBoxMessageType.Error));
                }

                // RECOGNITION (M23 review): an ⟨ability⟩ state is not an anonymous container —
                // it IS a row of the catalog, and the inspector says WHICH: identity, tags,
                // cooldown, continuation, and the way to the row. A claimed tree no row names
                // is warned about the other way: an ability the catalog cannot reach.
                if (m_Node.roleKind == AbilityDef.RootKind
                    && service.registry is AbilityRegistry abilities)
                {
                    AbilityDef row = null;
                    for (var i = 0; i < abilities.entries.Count && row == null; ++i)
                    {
                        if (abilities.entries[i] != null && abilities.entries[i].tree == m_Tree)
                            row = abilities.entries[i];
                    }

                    if (row != null)
                    {
                        var parts = new List<string>();
                        if (row.abilityTags.Count > 0)
                            parts.Add("tags [" + string.Join(", ", row.abilityTags) + "]");
                        if (row.blockTags.Count > 0)
                            parts.Add("blocks [" + string.Join(", ", row.blockTags) + "]");
                        if (row.cancelTags.Count > 0)
                            parts.Add("cancels [" + string.Join(", ", row.cancelTags) + "]");
                        if (row.cooldownSeconds > 0f)
                            parts.Add("cooldown " + row.cooldownSeconds.ToString("0.##") + "s");
                        if (!string.IsNullOrEmpty(row.nextOnFinish.entryName))
                            parts.Add("then '" + row.nextOnFinish.entryName + "'");

                        var box = new VisualElement();
                        box.style.flexDirection = FlexDirection.Row;
                        box.style.alignItems = Align.Center;
                        var who = new Label("⛃ This is ability '" + row.name + "'"
                            + (parts.Count > 0 ? " — " + string.Join(" · ", parts) : "")
                            + ".");
                        who.style.whiteSpace = WhiteSpace.Normal;
                        who.style.flexGrow = 1f;
                        who.style.marginLeft = 4f;
                        box.Add(who);
                        var open = new Button(() =>
                        {
                            Selection.activeObject = abilities;
                            EditorGUIUtility.PingObject(abilities);
                        })
                        { text = "Row" };
                        open.tooltip = "Open '" + abilities.name + "' — the catalog this "
                            + "ability lives in. Tags, cooldown and the continuation are "
                            + "edited THERE; this tree is what the row runs.";
                        open.style.flexShrink = 0f;
                        box.Add(open);
                        m_Root.Add(box);
                    }
                    else
                    {
                        m_Root.Add(new HelpBox("No row of '" + abilities.name + "' names this "
                            + "tree — the catalog cannot reach this ability. Add a row and "
                            + "pick this tree as its Tree.", HelpBoxMessageType.Warning));
                    }
                }

                // The same recognition for the ATOMS: a state applying an effect row or
                // showing a cue row says which, in the row's own words, with the way to its
                // registry. Task-driven rather than role-driven, so a plain state using the
                // same atoms is recognized identically.
                for (var i = 0; i < m_Node.tasks.Count; ++i)
                {
                    switch (m_Node.tasks[i])
                    {
                        case ApplyEffectTask applies:
                        {
                            var (registry, entry) = RowInClosure(service, applies.effect.entryName);
                            if (entry is EffectDef effect)
                            {
                                var parts = new List<string>
                                {
                                    effect.attribute + " "
                                        + (effect.magnitude >= 0f ? "+" : "")
                                        + effect.magnitude.ToString("0.##"),
                                    effect.duration.ToString()
                                };
                                if (effect.duration != AbilityEffectDuration.Instant)
                                    parts.Add(effect.seconds.ToString("0.##") + "s ×"
                                        + Mathf.Max(1, effect.maxStacks));
                                if (!string.IsNullOrEmpty(effect.cue.entryName))
                                    parts.Add("cue '" + effect.cue.entryName + "'");
                                m_Root.Add(RecognizedRow("⛃ Applies effect '" + effect.name
                                    + "' — " + string.Join(" · ", parts) + " → "
                                    + applies.target + ".", registry));
                            }
                            else if (!string.IsNullOrEmpty(applies.effect.entryName))
                            {
                                m_Root.Add(new HelpBox("Effect '" + applies.effect.entryName
                                    + "' resolves to no row in the service's registries.",
                                    HelpBoxMessageType.Warning));
                            }
                            break;
                        }
                        case ShowCueTask shows:
                        {
                            var (registry, entry) = RowInClosure(service, shows.cue.entryName);
                            if (entry is CueDef cueRow)
                            {
                                m_Root.Add(RecognizedRow("⛃ Shows cue '" + cueRow.name + "' — "
                                    + (cueRow.prefab != null ? cueRow.prefab.name : "(no prefab)")
                                    + " · " + cueRow.secondsAlive.ToString("0.##") + "s"
                                    + (cueRow.attachToTarget ? " · attached" : "") + ".",
                                    registry));
                            }
                            else if (!string.IsNullOrEmpty(shows.cue.entryName))
                            {
                                m_Root.Add(new HelpBox("Cue '" + shows.cue.entryName
                                    + "' resolves to no row in the service's registries.",
                                    HelpBoxMessageType.Warning));
                            }
                            break;
                        }
                    }
                }
            }

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

        /// <summary>A row plus the registry it lives in, found through the service's dependsOn
        /// closure — the same reach the runtime lookups use, so what this recognizes is what
        /// would resolve.</summary>
        private static (StateTreeRegistryAsset registry, StateTreeRegistryEntry entry)
            RowInClosure(ServiceDef service, string entryName)
        {
            if (service == null || service.registry == null || string.IsNullOrEmpty(entryName))
                return (null, null);
            var reachable = new List<StateTreeRegistryAsset>();
            service.registry.CollectWithDependencies(reachable);
            for (var i = 0; i < reachable.Count; ++i)
            {
                if (reachable[i] == service.registry)
                    continue;
                StateTreeRegistryEntry entry = reachable[i].FindByName(entryName);
                if (entry != null)
                    return (reachable[i], entry);
            }
            return (null, null);
        }

        /// <summary>One recognition line — the sentence plus the Row button to the registry
        /// the fact is edited in. One shape for ability, effect and cue, on purpose.</summary>
        private static VisualElement RecognizedRow(string sentence,
            StateTreeRegistryAsset registry)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            var who = new Label(sentence);
            who.style.whiteSpace = WhiteSpace.Normal;
            who.style.flexGrow = 1f;
            who.style.marginLeft = 4f;
            box.Add(who);
            if (registry != null)
            {
                var open = new Button(() =>
                {
                    Selection.activeObject = registry;
                    EditorGUIUtility.PingObject(registry);
                })
                { text = "Row" };
                open.tooltip = "Open '" + registry.name + "' — the row's numbers are edited "
                    + "there; this state only picks and places it.";
                open.style.flexShrink = 0f;
                box.Add(open);
            }
            return box;
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
        ///
        /// The first two tests are about the ROW's target and are asked of both sources; the ones
        /// after them are not, and a key row must never be run through the parameter path — it has no
        /// id, so every one of those tests would report the same non-problem on every key binding in
        /// the tree. A key row has exactly one authoring fault this box can name: no key. That its key
        /// might not be written is NOT reported here — that is information, it is answered beside the
        /// field (<see cref="BuildKeyBindingNote"/>), and putting a maybe in a box of definite
        /// problems is how a validation box stops being read.
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

            if (row.sourceKind == StateTreeFieldBinding.SourceKind.BlackboardKey)
            {
                return string.IsNullOrEmpty(row.blackboardKey)
                    ? $"'{field}' on {slot} reads a blackboard key, but no key is typed. Nothing is "
                    + "read on entry and the field's own value runs."
                    : null;
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

                // The RETURN half of any task's signature — a program's declared outputs or
                // a C# task's [TaskOutput] fields, one connectable row each.
                box.Add(BuildTaskReturns(task));

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

            /// <summary>True when this String parameter is KEY-SEMANTIC in the callee (it feeds
            /// key fields of embedded calls), so its row offers the declared-key picker — the
            /// income-parameter connection: pick one of THIS tree's keys and the program reads
            /// that key. Null when the surface cannot know (a sub-tree).</summary>
            internal Predicate<string> isKey;
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
                unusedTooltip = "is declared but nothing in the graph reads it, so overriding it "
                    + "changes nothing. A variable wired only into a NON-KEY port of a library "
                    + "call reads this way: those bake as fixed values. (A String variable on a "
                    + "KEY port does count — the program re-reads it per activation.)",
                isKey = name => IsKeyParameter(graph, name)
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

            // A KEY-WIRED row (bound with the ⚿ picker) is the M14 rule applied here: the id
            // is the wire, the text only its fallback — so the field LOCKS while the wire
            // stands (hand-editing a bound name would silently break the bind; unbind via the
            // ⚿ menu's free-text entry), and the shown name follows the declaration.
            var keyWired = entry != null && !string.IsNullOrEmpty(entry.keyId);
            if (keyWired)
                HealWiredKeyName(surface, entry);

            var input = BuildParameterInput(surface, parameter);
            input.AddToClassList("unity-base-field__input");
            input.style.flexGrow = 1f;
            WriteParameterInput(input, surface, parameter);

            // Disabled for a STALE link too, not only a live one: the runtime drops an unresolvable
            // pass-through row rather than falling back to its literal
            // (StateTreeExecutor.ResolveSourceValues), so the number in the field is not what runs
            // either way, and an editable field that does nothing is the lie this control exists to
            // stop being told one level down.
            ApplyOverrideStyle(input, overridden && !linked && !keyWired);
            row.Add(input);

            // The INCOME-parameter connection: a key-semantic String parameter binds to one of
            // THIS tree's declared keys with a pick, not a retype — the callee then reads that
            // key, and the tree's vocabulary is the single place the name lives.
            if (parameter.kind == GraphTaskParameterKind.String
                && surface.isKey != null && surface.isKey(parameter.name))
            {
                var pickKey = new Button { text = "⚿" };
                pickKey.style.width = 26f;
                pickKey.style.minHeight = k_ControlMinHeight;
                pickKey.style.flexShrink = 0f;
                pickKey.tooltip = $"'{parameter.name}' names a KEY the program reads. Bind it to "
                    + "one of this tree's declared keys — ticks the override and writes the "
                    + "key's name as its value.";
                pickKey.clicked += () => ShowDeclaredKeyMenu(pickKey, surface, parameter);
                row.Add(pickKey);
            }

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
            for (var i = 0; nodes != null && i < nodes.Count; ++i)
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

            // A String parameter feeding a KEY field of an embedded call is a real read too:
            // the interpreter re-applies its effective value to the per-activation copy
            // (GraphTaskKeyBinding), so overriding it retargets the program's key.
            var bindings = graph.keyBindings;
            for (var i = 0; bindings != null && i < bindings.Count; ++i)
            {
                var binding = bindings[i];
                if (binding != null
                    && string.Equals(binding.parameter, name, StringComparison.Ordinal))
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

            // Beside the control that shows the break, not under it: the fix is a different pick in
            // the same slot, and a button two rows down would read as being about the whole route.
            var fix = BuildMissingOutputFix(transitionIndex, routeIndex, route, task);
            if (fix != null)
                row.Add(fix);

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

            // A key with a reader on the target state is a COMPLETE wire, and a complete wire's
            // name is immutable in place (M7m): renaming half of a path is never the intent, so
            // neither end offers the rename. The field unlocks the moment the wire is undone by a
            // deliberate gesture — unlink the reading field, remove the route, re-aim the output.
            var transition = StateTreeEditorOps.TransitionAt(m_Node, transitionIndex);
            if (RouteKeyWired(transition, route))
            {
                ApplyOverrideStyle(key, false);
                key.tooltip = $"Wired: field(s) on '{transition.targetNodeId}' read "
                    + $"'{resolved}' at entry, so the name is locked at both ends. To change the "
                    + "wire, unlink the reading field(s) on the target state, or remove this route.";
            }
            else
            {
                key.tooltip = "Blackboard key the value lands under. Leave it empty to write it "
                    + "under the output's own name. Once a field on the target state reads this "
                    + "key, the wire is complete and the name locks at both ends.";
            }

            key.RegisterValueChangedCallback(
                evt => CommitRouteKey(transitionIndex, routeIndex, evt.newValue));
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

        /// <summary>
        /// The one-press repair for a route whose output name the source task no longer publishes —
        /// offered only when there is exactly one thing it could possibly mean.
        ///
        /// An output is a NAME CONTRACT (M7j), so this window shows a break rather than repairing it,
        /// and that is still the rule: nothing here changes anything until the button is pressed, and
        /// the stale row keeps saying what it says. What the rule was never meant to cost is the
        /// commonest case by a distance — a task publishes ONE output, the author renamed it, and
        /// every route that carried it now needs the same two clicks through a dropdown with one real
        /// entry in it. When the answer is unambiguous, the window may as well say so.
        ///
        /// UNAMBIGUOUS IS DEFINED NARROWLY, because a wrong guess here silently reroutes a value. The
        /// candidate must be the only output of the task that no OTHER route of this transition is
        /// already carrying: two free outputs means the author has a choice to make and the dropdown
        /// is where it is made, and an output another row already takes is the one thing a rename
        /// certainly did not become. A task that publishes nothing detectable (a graph awaiting a
        /// re-bake) gets no button either — its control is a free-text field, and there is no
        /// candidate to propose.
        /// </summary>
        /// <returns>The button, or null when the row is not broken or the answer is not unique.</returns>
        private Button BuildMissingOutputFix(int transitionIndex, int routeIndex,
            TransitionOutputRoute route, StateTreeTaskAsset task)
        {
            var candidate = SuggestedOutputFix(transitionIndex, routeIndex, route, task);
            if (candidate == null)
                return null;

            var fix = new Button(() =>
            {
                CommitRoute(transitionIndex, routeIndex, entry => entry.outputName = candidate);

                // Same reason the dropdown rebuilds: the key field's placeholder is this name and
                // the row's warning was about it.
                RebuildPane();
            })
            {
                text = $"→ use '{candidate}'"
            };

            fix.style.flexShrink = 0f;
            fix.style.maxWidth = 160f;
            fix.style.overflow = Overflow.Hidden;
            fix.style.textOverflow = TextOverflow.Ellipsis;
            fix.style.whiteSpace = WhiteSpace.NoWrap;
            EnlargeRowButton(fix, k_LinkMinWidth);
            fix.tooltip = $"{TaskBoxLabel(task)} no longer publishes '{route.outputName}', and "
                + $"'{candidate}' is the only output of it nothing else on this transition carries. "
                + "Point this route at it.";
            return fix;
        }

        /// <summary>The whole decision behind <see cref="BuildMissingOutputFix"/>, separated from the
        /// button so what the window OFFERS is one named rule rather than a condition spread through
        /// a builder: is this row actually stale, and is there exactly one thing it could have
        /// meant.</summary>
        /// <returns>The output name to propose, or null for every row that gets no button.</returns>
        private string SuggestedOutputFix(int transitionIndex, int routeIndex,
            TransitionOutputRoute route, StateTreeTaskAsset task)
        {
            // An unpicked name is not a rename: the dropdown shows <none> and the row already says
            // it carries nothing, which is a different problem with a different fix.
            if (route == null || string.IsNullOrEmpty(route.outputName))
                return null;

            // An EMPTY list is not evidence of a break — the same rule DescribeRouteProblem follows,
            // and the same reason: a graph awaiting a re-bake declares nothing either.
            var outputs = StateTreeEditorOps.CollectTaskOutputs(task);
            if (outputs.Count == 0 || PublishesOutput(outputs, route.outputName))
                return null;

            return SoleUnroutedOutput(outputs,
                StateTreeEditorOps.TransitionAt(m_Node, transitionIndex), routeIndex);
        }

        /// <summary>The one output of <paramref name="outputs"/> that no other route of
        /// <paramref name="transition"/> reads, or null when there is none or more than one.
        ///
        /// Names are compared, not entries: a graph that sets one name twice publishes ONE output
        /// (<see cref="StateTreeEditorOps.CollectTaskOutputs"/> reports declarations, not distinct
        /// names), and counting it as two would refuse the repair on a task that has exactly one
        /// thing to return.</summary>
        private static string SoleUnroutedOutput(List<TaskOutputValue> outputs,
            StateTreeTransition transition, int exceptRouteIndex)
        {
            string found = null;
            for (var i = 0; i < outputs.Count; ++i)
            {
                var name = outputs[i].name;
                if (string.IsNullOrEmpty(name)
                    || string.Equals(name, found, StringComparison.Ordinal))
                    continue;
                if (RoutesOutput(transition, exceptRouteIndex, name))
                    continue;
                if (found != null)
                    return null;

                found = name;
            }

            return found;
        }

        /// <summary>Whether any route of this transition OTHER than the one being repaired already
        /// carries <paramref name="name"/>. Every row of the transition counts, whichever task it
        /// reads: two rows carrying one name is legal, but it is not what a rename produced, and
        /// proposing it would be proposing a duplicate.</summary>
        private static bool RoutesOutput(StateTreeTransition transition, int exceptRouteIndex,
            string name)
        {
            var rows = transition != null ? transition.outputRoutes : null;
            for (var i = 0; rows != null && i < rows.Count; ++i)
            {
                if (i == exceptRouteIndex || rows[i] == null)
                    continue;
                if (string.Equals(rows[i].outputName, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
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
                // The base-class RETURN routes are the curated Returns section above — the raw
                // list drawn here too would be the same data twice, once as plumbing.
                if (iterator.propertyPath == "returns"
                    && target is StateTreeTaskAsset)
                    continue;
                // A [TaskOutput] field is a RETURN value, written by the task at runtime —
                // rendering it as an editable knob would invite authoring a value the task
                // overwrites. It surfaces in the transition "Route outputs" dropdowns instead.
                if (StateTreeEditorOps.IsTaskOutputField(target, iterator.propertyPath))
                    continue;

                // Only the top level is walked (NextVisible stops entering children after the
                // first step), so a path IS a field name — which is what a link row stores and what
                // the executor's reflection looks up.
                container.Add(BuildFieldRow(new PropertyField(iterator.Copy()), target,
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
        /// The control appears wherever it can do something, which since M7k is every field the
        /// executor can write (<see cref="StateTreeEditorOps.TryGetBindableKind"/> asks by reflection,
        /// so a <c>[SerializeField]</c> private is drawn and not offered). It used to also require the
        /// tree to declare a parameter of the matching kind — a popup with nothing in it being worse
        /// than no button — and that condition is gone because the popup is never empty any more: a
        /// BLACKBOARD KEY is always an option, and on a tree with no declarations at all it is the
        /// only one that matters.
        ///
        /// A PARAMETER-BOUND FIELD IS DISABLED, and says where its value comes from. That is the whole
        /// point of the control: the literal underneath is still in the asset and is still what a
        /// reader of the YAML sees, so leaving it editable would leave the author tuning a number that
        /// the tree start overwrites — the failure this window exists to prevent, one level down.
        ///
        /// A KEY-BOUND FIELD IS NOT DISABLED, and the difference is the runtime rule rather than a
        /// styling choice: a missing key at entry is skipped SILENTLY and the field keeps the value it
        /// has (entering through a path that routes nothing is normal, not an error), so the literal is
        /// this binding's default rather than dead weight. Greying it out would hide the value that
        /// actually runs on every unrouted entry. What the row shows instead is the flag glyph and the
        /// key beside it, editable in place, because a key is authored text that gets retyped — while
        /// a parameter link is picked once from a list and never edited as a string.
        ///
        /// A link whose parameter is GONE leaves the field enabled, because the literal is what runs
        /// again, and says so in the same warning-plus-remove shape a stale override gets
        /// (<see cref="BuildStaleOverrideRow"/>). The Link button stays available beside it so the
        /// fix is one gesture rather than remove-then-link.
        /// </summary>
        private VisualElement BuildBindableField(PropertyField field, UnityEngine.Object target,
            string fieldName, StateTreeFieldBinding.TargetKind kind, int targetIndex,
            VisualElement keySlot = null)
        {
            if (targetIndex < 0 || m_Node == null || m_Tree == null
                || !StateTreeEditorOps.TryGetBindableKind(target, fieldName, out var fieldKind))
                return field;

            var binding = StateTreeEditorOps.FindFieldBinding(m_Node, kind, targetIndex, fieldName);
            var keyBound = binding != null
                && binding.sourceKind == StateTreeFieldBinding.SourceKind.BlackboardKey;
            var compatible = CompatibleParameters(fieldKind);

            var source = binding != null && !keyBound
                ? StateTreeEditorOps.FindParameterById(m_Tree.parameters, binding.parameterId)
                : null;
            var live = IsLiveLink(source, fieldKind);

            var container = new VisualElement();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = k_RowMinHeight;
            container.Add(row);

            // The same disabled-and-dimmed the override rows use, and it means the same thing in
            // both places: what you are looking at is not what runs. A key-bound literal dims too:
            // it does still run on entries where nothing wrote the key, but that makes it a
            // FALLBACK (the source button's tooltip says so), and an editable-looking field beside
            // a wire claims to be the value in charge — the one thing it is not.
            //
            // A WIRED field goes one further and shows no literal at all (M7m): what runs is a task
            // output produced at runtime, so even a dimmed number is a confident display of the
            // wrong thing. The stand-in says "unknown"; the authored fallback rides its tooltip.
            if (keyBound && BindingKeyWired(binding.blackboardKey))
            {
                row.Add(BuildRoutedValueStandIn(target, fieldName, binding.blackboardKey));
            }
            else
            {
                field.style.flexGrow = 1f;
                field.style.flexShrink = 1f;
                ApplyOverrideStyle(field, !live && !keyBound);
                row.Add(field);
            }

            // A key-semantic field's ⚿ button rides in this row (M12), passed in only while no ⚑
            // source is in force — a sourced value is not a name for that menu to re-point.
            if (keySlot != null)
                row.Add(keySlot);

            var pick = new Button
            {
                text = keyBound ? k_KeyBoundGlyph : live ? k_BoundPrefix + source.name : k_LinkLabel
            };
            pick.style.flexShrink = 0f;
            EnlargeRowButton(pick, keyBound ? k_GlyphMinWidth : k_LinkMinWidth);
            pick.style.maxWidth = 140f;
            pick.style.overflow = Overflow.Hidden;
            pick.style.textOverflow = TextOverflow.Ellipsis;
            pick.style.whiteSpace = WhiteSpace.NoWrap;
            pick.tooltip = keyBound
                ? $"'{fieldName}' is read from the blackboard key beside it every time this state is "
                + "entered, before its tasks start — so a transition that routes a finished task's "
                + "output into this state feeds it. When nothing has written that key, the value on "
                + "the left is what runs. Click to change the source."
                : live
                    ? $"'{fieldName}' is written from the tree parameter '{source.name}' "
                    + $"({KindLabel(source.kind)}) every time this tree starts, so the value beside "
                    + "it is not what runs. Click to bind it to a different parameter or to a "
                    + "blackboard key."
                    : $"Write this field from one of this tree's {KindLabel(fieldKind)} parameters "
                    + "when the tree starts, or from a blackboard key every time this state is "
                    + "entered, instead of the value typed here.";
            pick.clicked += () => ShowFieldSourceMenu(pick, compatible, fieldName, fieldKind,
                binding, kind, targetIndex);
            row.Add(pick);

            if (keyBound)
                row.Add(BuildBindingKeyField(fieldName, binding, kind, targetIndex));

            if (binding != null)
            {
                var unlink = new Button { text = "✕" };
                unlink.style.width = 26f;
                unlink.style.minHeight = k_ControlMinHeight;
                unlink.style.flexShrink = 0f;
                unlink.tooltip = keyBound
                    ? $"Stop reading '{fieldName}' from the blackboard — the value in the field runs "
                    + "on every entry again."
                    : $"Stop writing '{fieldName}' from a parameter — the value in the "
                    + "field runs again.";
                unlink.clicked += () => ClearFieldLink(kind, targetIndex, fieldName);
                row.Add(unlink);
            }

            if (keyBound)
            {
                var note = BuildKeyBindingNote(fieldName, binding.blackboardKey);
                if (note != null)
                    container.Add(note);
            }
            else if (binding != null && !live)
            {
                var help = new HelpBox(StaleBindingMessage(fieldName, source, fieldKind),
                    HelpBoxMessageType.Warning);
                help.style.marginTop = 2f;
                container.Add(help);
            }

            return container;
        }

        /// <summary>
        /// What a WIRED field shows in place of its value. The number in the asset is not what will
        /// run — a routing transition overwrites it at entry with a task output that exists only at
        /// runtime — so showing it, even dimmed, would be a confident display of the wrong thing.
        /// The stand-in states the truth of edit time: the value is unknown here. The authored
        /// number survives only as the fallback for entry paths that route nothing, and the tooltip
        /// carries it, read once at build from the field itself so the two can never disagree.
        /// </summary>
        private VisualElement BuildRoutedValueStandIn(UnityEngine.Object target, string fieldName,
            string key)
        {
            var standIn = new TextField(ObjectNames.NicifyVariableName(fieldName))
            {
                value = k_RoutedValueText,
                isReadOnly = true
            };
            standIn.AddToClassList(TextField.alignedFieldUssClassName);
            standIn.style.flexGrow = 1f;
            standIn.style.flexShrink = 1f;
            standIn.style.minHeight = k_ControlMinHeight;
            ApplyOverrideStyle(standIn, false);

            var info = target != null
                ? target.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                : null;
            var fallback = info != null ? info.GetValue(target) : null;
            standIn.tooltip = $"'{fieldName}' is overwritten from blackboard key '{key}' when this "
                + "state is entered — the value is a task's output, produced at runtime, so it is "
                + "unknown while editing. Only an entry through a path that routes nothing keeps "
                + (fallback != null
                    ? $"the authored value ({fallback})."
                    : "the authored value.");
            return standIn;
        }

        /// <summary>The key itself, edited where it is read. Delayed like every other committing text
        /// field in this window (a node id, a route's key): each commit rewrites the row through Ops
        /// and rebuilds the pane, and doing that per keystroke would fill the undo stack with
        /// half-typed keys.</summary>
        private VisualElement BuildBindingKeyField(string fieldName, StateTreeFieldBinding binding,
            StateTreeFieldBinding.TargetKind kind, int targetIndex)
        {
            var key = new TextField
            {
                value = binding.blackboardKey ?? string.Empty,
                isDelayed = true
            };
            key.style.flexGrow = 1f;
            key.style.flexBasis = 0f;
            key.style.minWidth = k_KeyFieldMinWidth;
            key.style.minHeight = k_ControlMinHeight;
            key.style.marginLeft = 2f;
            key.textEdition.placeholder = "blackboard key";

            // The mirror of the route row's lock (M7m): while a transition into this state routes
            // this key, the wire is complete and its name is immutable in place. Editing resumes
            // when the wire is undone deliberately — the ⚑ menu rebinds, ✕ unlinks, the route's
            // own row removes or re-aims the writing end.
            if (BindingKeyWired(binding.blackboardKey))
            {
                ApplyOverrideStyle(key, false);
                key.tooltip = $"Wired: a transition into this state routes "
                    + $"'{binding.blackboardKey}', so the name is locked at both ends. To read a "
                    + "different key, pick it from the ⚑ menu; to free the field, unlink it.";
            }
            else
            {
                key.tooltip = $"The blackboard key '{fieldName}' takes its value from at entry. "
                    + "Type the key a transition routes into this state — or any key the tree "
                    + "writes; matching is by exact text. Once an incoming route writes this key, "
                    + "the wire is complete and the name locks at both ends.";
            }

            key.RegisterValueChangedCallback(evt => SetFieldKeyLink(kind, targetIndex, fieldName,
                evt.newValue, k_EditFieldKeyUndo));
            return key;
        }

        /// <summary>
        /// What a key binding has to say about itself, or null when it is unremarkable.
        ///
        /// Two outcomes and two severities, and the split is the M7k rule made visible. An EMPTY key
        /// is a warning: the row cannot read anything, ever, and the author is looking at a link that
        /// is doing nothing. A key NO INCOMING ROUTE WRITES is only information — a value can reach the
        /// blackboard from a graph's Set Blackboard node, from a task, or from a state three
        /// transitions away, none of which this scan can see — so it says what it knows ("nothing
        /// routes it here") and what happens if that is the whole story, rather than claiming a fault
        /// it cannot establish.
        ///
        /// A DECLARED key says nothing at all (M12): the tree's header owns that name, which is the
        /// author stating "this key exists and something speaks it" — repeating a hedge under every
        /// reader of a declared key would train authors to skim past the one note that matters, the
        /// unrouted UNDECLARED key that looks exactly like a typo.
        /// </summary>
        private HelpBox BuildKeyBindingNote(string fieldName, string blackboardKey)
        {
            HelpBox help;
            if (string.IsNullOrEmpty(blackboardKey))
            {
                help = new HelpBox($"'{fieldName}' is bound to a blackboard key, but no key is typed "
                    + "— nothing is read and the value in the field runs.",
                    HelpBoxMessageType.Warning);
            }
            else if (!IsRoutedIn(blackboardKey) && !VisibleKeyNameExists(blackboardKey))
            {
                help = new HelpBox($"No transition into this state routes '{blackboardKey}' and no "
                    + "declared key carries that name. Fine if a graph, a task, or an earlier state "
                    + "writes it — but a typo would look exactly like this, and on any entry where "
                    + "the key is missing the value in the field is kept. Declaring the key in the "
                    + "tree's Keys section records the intent and retires this note.",
                    HelpBoxMessageType.Info);
            }
            else
            {
                return null;
            }

            help.style.marginTop = 2f;
            return help;
        }

        /// <summary>The keys routed into the selected state, computed once per pane rebuild. See
        /// <see cref="m_IncomingRoutedKeys"/> for why it is cached rather than asked per field.</summary>
        private List<StateTreeEditorOps.RoutedKey> IncomingRoutedKeys()
        {
            if (m_IncomingRoutedKeys == null)
            {
                m_IncomingRoutedKeys = StateTreeEditorOps.CollectIncomingRoutedKeys(m_Tree,
                    m_Node != null ? m_Node.nodeId : null);
            }

            return m_IncomingRoutedKeys;
        }

        /// <summary>Whether anything routes one key INTO this state, kind ignored. The hint asks this
        /// and the menu does not: an incoming route of the wrong kind is not a source the author can
        /// pick, but it is still a reason the key is not a mystery — so a note that called it unrouted
        /// would be telling them something false.</summary>
        private bool IsRoutedIn(string key)
        {
            var routed = IncomingRoutedKeys();
            for (var i = 0; i < routed.Count; ++i)
            {
                if (string.Equals(routed[i].key, key, StringComparison.Ordinal))
                    return true;
            }

            return false;
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
        /// <summary>True when the program reads <paramref name="name"/> as a KEY — it feeds a
        /// key field of an embedded call through a <see cref="GraphTaskKeyBinding"/>.</summary>
        private static bool IsKeyParameter(GraphTaskAsset graph, string name)
        {
            var bindings = graph != null ? graph.keyBindings : null;
            for (var i = 0; bindings != null && i < bindings.Count; ++i)
            {
                var binding = bindings[i];
                if (binding != null
                    && string.Equals(binding.parameter, name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>A wired row's TEXT follows its declaration — the display half of the
        /// rename rule (the runtime half lives in StateTreeExecutor.ResolveSourceValues). A
        /// wire whose declaration is gone keeps its last text as the fallback, unhealed.</summary>
        private void HealWiredKeyName(ParameterSurface surface, GraphTaskParameterOverride entry)
        {
            var declarations = new List<StateTreeKeyDeclaration>();
            StateTreeKeyResolver.CollectVisible(m_Tree, declarations);
            for (var i = 0; i < declarations.Count; ++i)
            {
                var declaration = declarations[i];
                if (declaration == null
                    || !string.Equals(declaration.id, entry.keyId, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(declaration.name, entry.stringValue, StringComparison.Ordinal))
                {
                    entry.stringValue = declaration.name;
                    EditorUtility.SetDirty(surface.owner);
                }
                return;
            }
        }

        /// <summary>The declared-key menu for a key-semantic parameter row: every key visible
        /// from this tree, current value ticked; picking one overrides the parameter with the
        /// key's NAME (the runtime wire — programs read keys by name).</summary>
        private void ShowDeclaredKeyMenu(VisualElement anchor, ParameterSurface surface,
            GraphTaskParameter parameter)
        {
            var declarations = new List<StateTreeKeyDeclaration>();
            StateTreeKeyResolver.CollectVisible(m_Tree, declarations);

            var entry = ActiveOverride(surface, parameter);
            var currentId = entry != null ? entry.keyId : null;
            var currentText = entry != null ? entry.stringValue : null;
            var wired = !string.IsNullOrEmpty(currentId);

            var menu = new GenericMenu();
            if (declarations.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("This tree declares no keys yet — add them "
                    + "in the Keys section."));
            }
            for (var i = 0; i < declarations.Count; ++i)
            {
                var declaration = declarations[i];
                if (declaration == null || string.IsNullOrEmpty(declaration.name))
                    continue;
                var keyName = declaration.name;
                var keyId = declaration.id;
                var label = (keyName + "  (" + declaration.kind + ")")
                    .Replace('/', k_MenuSeparatorStandIn);
                var ticked = wired
                    ? string.Equals(keyId, currentId, StringComparison.Ordinal)
                    : string.Equals(keyName, currentText, StringComparison.Ordinal);
                menu.AddItem(new GUIContent(label), ticked,
                    () =>
                    {
                        SetOverride(surface, parameter, true);
                        CommitOverride(surface, parameter, row =>
                        {
                            row.stringValue = keyName;
                            row.keyId = keyId;
                        });
                        RebuildPane();
                    });
            }
            if (wired)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Unbind (free text)"), false, () =>
                {
                    CommitOverride(surface, parameter, row => row.keyId = string.Empty);
                    RebuildPane();
                });
            }
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>
        /// The OUTCOME half of a program's signature: what it returns (Output variables on the
        /// graph's panel and Set Output declarations), one CONNECTABLE row per output — the
        /// Blueprint return pin, tree side. The ⚿ picker binds an output to one of this tree's
        /// declared keys (a <see cref="TaskReturnRoute"/> on the task): when the
        /// activation ends, the value is published there, however the state ended. Transitions
        /// can still route per exit wire; this is the unconditional signature connection.
        /// </summary>
        private VisualElement BuildTaskReturns(StateTreeTaskAsset task)
        {
            var container = new VisualElement();
            var outputs = CollectDeclaredOutputs(task);
            var count = 0;
            for (var i = 0; outputs != null && i < outputs.Count; ++i)
                if (!string.IsNullOrEmpty(outputs[i].name))
                    count++;

            // A program can DECLARE a new return from here (it edits the graph — the callee
            // owns its signature, this is just the gesture offered where you are). A plain C#
            // task declares them in code ([TaskOutput]), so with none there is nothing to draw.
            var program = task as RunGraphTask;
            var graphPath = program != null && program.graph != null
                ? AssetDatabase.GetAssetPath(program.graph)
                : null;
            if (count == 0 && string.IsNullOrEmpty(graphPath))
                return container;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginTop = 6f;
            var title = new Label($"Returns ({count})");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1f;
            header.Add(title);
            if (!string.IsNullOrEmpty(graphPath))
            {
                var add = new Button { text = "+" };
                add.style.width = 26f;
                add.style.minHeight = k_ControlMinHeight;
                add.tooltip = "Declare a new return on the graph (an Output variable): the "
                    + "Return nodes gain its pin, and every state running this graph sees it.";
                var path = graphPath;
                add.clicked += () => ReturnParameterPrompt.Show(path, RebuildPane);
                header.Add(add);
            }
            container.Add(header);

            var hint = Hint(count == 0
                ? "This program declares no returns yet. + declares one — an Output variable "
                    + "on the graph's panel, a pin on its Return nodes."
                : "What this task returns when it finishes. ⚿ publishes a return to one of "
                    + "this tree's declared keys, however the state then leaves; a "
                    + "transition's route rows can still carry it per exit wire.");
            hint.style.marginBottom = 2f;
            container.Add(hint);

            for (var i = 0; outputs != null && i < outputs.Count; ++i)
            {
                var output = outputs[i];
                if (string.IsNullOrEmpty(output.name))
                    continue;
                container.Add(BuildReturnRow(task, output));
            }
            return container;
        }

        /// <summary>One return pin: name, kind, and where it lands.</summary>
        private VisualElement BuildReturnRow(StateTreeTaskAsset task, TaskOutputValue output)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = k_RowMinHeight;
            row.style.marginLeft = 6f;

            TaskReturnRoute route = FindReturnRoute(task, output.name);
            var bound = route != null && !string.IsNullOrEmpty((string)route.key);

            var label = new Label("→ " + output.name + "  (" + KindLabel(output.kind) + ")");
            label.style.flexGrow = 1f;
            label.style.color = new Color(0.75f, 0.78f, 0.85f);
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);

            var target = new Label(bound ? "⚿ " + (string)route.key : "—");
            target.style.color = bound
                ? new Color(0.72f, 0.85f, 0.75f)
                : new Color(0.5f, 0.52f, 0.58f);
            target.style.marginRight = 4f;
            target.tooltip = bound
                ? $"Published to '{(string)route.key}' when the activation ends."
                : "Not published anywhere — route it on a transition, or bind a key here.";
            row.Add(target);

            var pick = new Button { text = "⚿" };
            pick.style.width = 26f;
            pick.style.minHeight = k_ControlMinHeight;
            pick.style.flexShrink = 0f;
            pick.tooltip = $"Publish '{output.name}' to one of this tree's declared keys when "
                + "the program's activation ends.";
            pick.clicked += () => ShowReturnKeyMenu(pick, task, output.name);
            row.Add(pick);

            if (bound)
            {
                var unbind = new Button { text = "✕" };
                unbind.style.width = 26f;
                unbind.style.minHeight = k_ControlMinHeight;
                unbind.style.flexShrink = 0f;
                unbind.tooltip = "Stop publishing this return.";
                unbind.clicked += () =>
                {
                    Undo.RecordObject(task, k_ReturnRouteUndo);
                    task.returns.RemoveAll(entry => entry != null
                        && string.Equals(entry.output, output.name, StringComparison.Ordinal));
                    EditorUtility.SetDirty(task);
                    m_Edited?.Invoke();
                    RebuildPane();
                };
                row.Add(unbind);
            }

            return row;
        }

        private const string k_ReturnRouteUndo = "Route Program Return";

        /// <summary>What a task RETURNS, whichever way it declares it: a program's baked
        /// declaredOutputs, or a plain C# task's [TaskOutput] fields (the four blackboard
        /// kinds; anything else is not routable and not listed).</summary>
        private static List<TaskOutputValue> CollectDeclaredOutputs(StateTreeTaskAsset task)
        {
            var outputs = new List<TaskOutputValue>();
            if (task is RunGraphTask program)
            {
                var declared = program.graph != null ? program.graph.declaredOutputs : null;
                for (var i = 0; declared != null && i < declared.Count; ++i)
                    if (!string.IsNullOrEmpty(declared[i].name))
                        outputs.Add(declared[i]);
                return outputs;
            }

            var fields = task.GetType().GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            for (var i = 0; i < fields.Length; ++i)
            {
                var field = fields[i];
                if (!Attribute.IsDefined(field, typeof(TaskOutputAttribute)))
                    continue;
                var type = field.FieldType;
                if (type == typeof(float) || type == typeof(int))
                    outputs.Add(new TaskOutputValue { name = field.Name, kind = GraphTaskParameterKind.Float });
                else if (type == typeof(bool))
                    outputs.Add(new TaskOutputValue { name = field.Name, kind = GraphTaskParameterKind.Bool });
                else if (type == typeof(string))
                    outputs.Add(new TaskOutputValue { name = field.Name, kind = GraphTaskParameterKind.String });
            }
            return outputs;
        }

        private static TaskReturnRoute FindReturnRoute(StateTreeTaskAsset task, string output)
        {
            var routes = task.returns;
            for (var i = 0; routes != null && i < routes.Count; ++i)
            {
                if (routes[i] != null
                    && string.Equals(routes[i].output, output, StringComparison.Ordinal))
                    return routes[i];
            }
            return null;
        }

        /// <summary>The declared-key menu for a return pin — the outcome twin of
        /// <see cref="ShowDeclaredKeyMenu"/>.</summary>
        private void ShowReturnKeyMenu(VisualElement anchor, StateTreeTaskAsset task, string output)
        {
            var declarations = new List<StateTreeKeyDeclaration>();
            StateTreeKeyResolver.CollectVisible(m_Tree, declarations);

            TaskReturnRoute existing = FindReturnRoute(task, output);
            var current = existing != null ? (string)existing.key : null;

            var menu = new GenericMenu();
            if (declarations.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("This tree declares no keys yet — add them "
                    + "in the Keys section."));
            }
            for (var i = 0; i < declarations.Count; ++i)
            {
                var declaration = declarations[i];
                if (declaration == null || string.IsNullOrEmpty(declaration.name))
                    continue;
                var keyName = declaration.name;
                var keyId = declaration.id;
                var label = (keyName + "  (" + declaration.kind + ")")
                    .Replace('/', k_MenuSeparatorStandIn);
                menu.AddItem(new GUIContent(label),
                    string.Equals(keyName, current, StringComparison.Ordinal),
                    () =>
                    {
                        Undo.RecordObject(task, k_ReturnRouteUndo);
                        var route = FindReturnRoute(task, output);
                        if (route == null)
                        {
                            route = new TaskReturnRoute { output = output };
                            (task.returns ??= new List<TaskReturnRoute>()).Add(route);
                        }
                        route.key.text = keyName;
                        route.key.keyId = keyId;
                        EditorUtility.SetDirty(task);
                        m_Edited?.Invoke();
                        RebuildPane();
                    });
            }
            menu.DropDown(anchor.worldBound);
        }

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

        /// <summary>
        /// The field link popup: the tree's parameters, then the keys that ARRIVE at this state, then
        /// any key at all — one menu, because they are answers to one question ("where does this
        /// field's value come from?") and a mode switch between them would make the author decide
        /// before they can see the options.
        ///
        /// THE MIDDLE SECTION IS THE POINT OF M7k. A transition routes a finished task's output onto
        /// the blackboard under a key; until now nothing in this window connected that key back to the
        /// field that wants it, so the author had to remember the key and know that a plain
        /// <c>public float</c> could not read it anyway. Listing the keys the tree's own wiring
        /// delivers HERE turns that into a pick — and the list is honest about being incomplete, which
        /// is what "Blackboard key…" underneath it is for: a key can also be written by a graph, by a
        /// task, or by a state that is not adjacent, and refusing to let the author type one would
        /// make those cases unreachable.
        ///
        /// The parameter section is unchanged and comes FIRST because it is the stronger guarantee —
        /// a declared parameter is definitely there when the tree starts, a routed key only if the
        /// path that writes it was taken.
        ///
        /// BOTH SECTIONS ARE FILTERED BY KIND, and for the same reason: the parameters have always
        /// been (<see cref="CompatibleParameters"/>), because a link the executor drops with a warning
        /// is not a link, and a routed key whose source task returns text is exactly that link seen
        /// from the other end. The keys differ only in that their kind can be UNKNOWN — a graph that
        /// has not been re-baked declares nothing, and a free-text output name is a promise this
        /// window cannot check — so those stay in the list, marked, rather than being hidden on a
        /// guess.
        /// </summary>
        /// <param name="compatible">Declared parameters of the field's kind, possibly none — the
        /// section is then skipped and replaced by the one line that explains why.</param>
        /// <param name="binding">The row in force, so the current source can be ticked. Null when the
        /// field is unbound.</param>
        private void ShowFieldSourceMenu(VisualElement anchor, List<GraphTaskParameter> compatible,
            string fieldName, GraphTaskParameterKind fieldKind, StateTreeFieldBinding binding,
            StateTreeFieldBinding.TargetKind kind, int targetIndex)
        {
            var keyBound = binding != null
                && binding.sourceKind == StateTreeFieldBinding.SourceKind.BlackboardKey;
            var boundId = binding != null && !keyBound ? binding.parameterId : null;
            var boundKey = keyBound ? binding.blackboardKey ?? string.Empty : null;
            var routed = IncomingRoutedKeys();

            var menu = new GenericMenu();
            for (var i = 0; i < compatible.Count; ++i)
            {
                var entry = compatible[i];
                var id = entry.id;
                var label = (entry.name ?? string.Empty).Replace('/', k_MenuSeparatorStandIn);
                menu.AddItem(new GUIContent(label),
                    string.Equals(id, boundId, StringComparison.Ordinal),
                    () => SetFieldLink(kind, targetIndex, fieldName, id));
            }

            if (compatible.Count > 0)
            {
                menu.AddSeparator(string.Empty);
            }
            else
            {
                // Not an error and not silence: the popup opens on trees with no declarations at all
                // now, and an author who expected a parameter here needs to know none is declared
                // rather than to wonder whether the list failed to build.
                menu.AddDisabledItem(new GUIContent(
                    $"This tree declares no {KindLabel(fieldKind)} parameters"));
                menu.AddSeparator(string.Empty);
            }

            menu.AddDisabledItem(new GUIContent(k_RoutedHeading));

            var offered = 0;
            var boundOffered = false;
            for (var i = 0; i < routed.Count; ++i)
            {
                var entry = routed[i];

                if (!OffersRoutedKey(entry, fieldKind))
                    continue;

                // The LABEL is escaped and the closure carries the real key — same rule as the
                // parameter names above, and it matters more here because a key is free text.
                var key = entry.key;
                var label = k_MenuIndent + key.Replace('/', k_MenuSeparatorStandIn)
                    + (entry.kindKnown ? string.Empty : k_UnknownKindSuffix);
                var current = string.Equals(key, boundKey, StringComparison.Ordinal);
                boundOffered |= current;
                menu.AddItem(new GUIContent(label), current,
                    () => SetFieldKeyLink(kind, targetIndex, fieldName, key, k_LinkFieldKeyUndo));
                ++offered;
            }

            if (offered == 0)
            {
                // Two different silences, and the author acts on them differently: nothing arrives
                // here at all (wire a route, or type a key something else writes), versus something
                // arrives and none of it is this field's type (the route is there, the field is the
                // wrong shape for it).
                menu.AddDisabledItem(new GUIContent(k_MenuIndent + (routed.Count == 0
                    ? "nothing is routed into this state"
                    : $"nothing routed here carries a {KindLabel(fieldKind)}")));
            }

            menu.AddSeparator(string.Empty);

            // Seeded with the FIELD NAME, which is the key an author who is naming both sides would
            // have typed anyway — and with the CURRENT key when there already is one, so re-picking
            // the source the row already has cannot throw away what was typed into it.
            //
            // Ticked only for a key that is not among the items above, so exactly one item in the menu
            // is ever ticked: two ticks would read as two sources, and a row has one. Asked of what
            // was OFFERED rather than of what is routed, because a bound key the kind filter dropped
            // is not represented up there either.
            var seed = string.IsNullOrEmpty(boundKey) ? fieldName : boundKey;
            menu.AddItem(new GUIContent(k_KeyPickLabel), keyBound && !boundOffered,
                () => SetFieldKeyLink(kind, targetIndex, fieldName, seed, k_LinkFieldKeyUndo));

            menu.DropDown(anchor.worldBound);
        }

        /// <summary>Whether one routed key is offered to a field of <paramref name="fieldKind"/> —
        /// the difference between a suggestion and a trap. A route carrying text into a float field
        /// makes a binding the executor drops with a warning at entry, so offering it would be
        /// offering something that cannot work; a key whose kind this window could not establish is
        /// offered anyway (and marked with <see cref="k_UnknownKindSuffix"/>), because hiding the only
        /// key that arrives at a state on the strength of a guess is the worse failure.</summary>
        private static bool OffersRoutedKey(StateTreeEditorOps.RoutedKey entry,
            GraphTaskParameterKind fieldKind)
            => !entry.kindKnown || entry.kind == fieldKind;

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

        /// <summary>Point a field at a blackboard key — the same one gesture, one undo step, one Ops
        /// writer as <see cref="SetFieldLink"/>, differing only in which source the row records.
        ///
        /// Used by BOTH the popup and the inline editor, which is why the undo label is a parameter:
        /// picking a source and retyping a key are different edits to undo, and the row they produce
        /// is identical.</summary>
        private void SetFieldKeyLink(StateTreeFieldBinding.TargetKind kind, int targetIndex,
            string fieldName, string blackboardKey, string undoName)
        {
            if (m_Node == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(undoName);
            StateTreeEditorOps.SetFieldBindingKey(m_Node, kind, targetIndex, fieldName,
                blackboardKey, undoName);
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

        /// <summary>
        /// Commit a route's destination key. Because the field LOCKS while the wire is complete
        /// (<see cref="RouteKeyWired"/>), this only ever runs on a key nothing reads yet — an edit
        /// here can COMPLETE a wire (typing the key a field on the target state is waiting for) but
        /// can never break one, which is the M7m rule enforced by construction rather than by
        /// dialogs or by rename-following: a wire's name is not edited, a wire is made or unmade.
        /// </summary>
        private void CommitRouteKey(int transitionIndex, int routeIndex, string requested)
        {
            var transition = StateTreeEditorOps.TransitionAt(m_Node, transitionIndex);
            var rows = transition != null ? transition.outputRoutes : null;
            if (rows == null || routeIndex < 0 || routeIndex >= rows.Count || rows[routeIndex] == null)
                return;

            var route = rows[routeIndex];
            var oldKey = route.blackboardKey ?? string.Empty;
            var value = requested ?? string.Empty;
            if (string.Equals(oldKey, value, StringComparison.Ordinal))
                return;

            var group = StateTreeEditorOps.BeginUndoGroup(k_EditRouteUndo);
            Undo.RecordObject(m_Node, k_EditRouteUndo);
            route.blackboardKey = value;
            EditorUtility.SetDirty(m_Node);
            StateTreeEditorOps.EndUndoGroup(group);

            // Typing the key a target field waits on completes the wire, and the field this very
            // gesture ran in must lock NOW rather than on the next incidental redraw; a plain edit
            // keeps the light path, which is what keeps typing here from rebuilding the world.
            if (RouteKeyWired(transition, route))
                RebuildPane();
            else
                m_Edited?.Invoke();
        }

        /// <summary>
        /// Whether this route and a field on the state its transition leads to form a COMPLETE
        /// wire — the route writes a key, a binding on the target reads it at entry. Asked through
        /// the RESOLVED key, because an empty key writes under the output's own name and a wire is
        /// what the executor sees, not what the text box holds. Scoped to the transition's target,
        /// the one state where the two ends are provably the same wire: the same key elsewhere may
        /// be written by a graph, a task, or a different route entirely.
        /// </summary>
        private bool RouteKeyWired(StateTreeTransition transition, TransitionOutputRoute route)
        {
            var resolved = route != null ? route.ResolvedKey() : null;
            return transition != null && !string.IsNullOrEmpty(resolved)
                && StateTreeEditorOps.CountEntryBindings(m_Tree, transition.targetNodeId,
                    resolved) > 0;
        }

        /// <summary>The same question from the field's end: is the key this binding reads written
        /// by a route on a transition INTO this state? While it is, the wire is complete and the
        /// inline key is locked (M7m) — rebinding goes through the link menu, freeing through the
        /// unlink button.</summary>
        private bool BindingKeyWired(string key)
            => !string.IsNullOrEmpty(key) && IsRoutedIn(key);

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
