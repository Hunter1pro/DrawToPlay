using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The direct State Tree editor — a LIVE VIEW of a <see cref="StateTreeAsset"/>. Every row in
    /// the tree is a real <see cref="StateTreeNodeAsset"/> sub-asset and every edit writes into
    /// the asset a <see cref="StateTreeRunner"/> loads. There is no intermediate model, no export
    /// and no bake step: change a range here, press Play, the behaviour is different.
    ///
    /// Ported from the HeavenlyTreasures StateTreeWindow / RuntimeStateTreeTool pattern (toolbar
    /// add / add-child / remove, tree on the left, inspector-in-place on the right, TypeCache
    /// dropdowns for components, auto-open on selection). Three things are ours, because the
    /// underlying model differs:
    ///
    /// * Their nodes are plain [Serializable] classes with a Parent back-reference kept in sync
    ///   by hand — hence their tree-integrity repair passes. Ours are ScriptableObject
    ///   SUB-ASSETS with children-only links, so the parent is derived on demand
    ///   (<c>StateTreeEditorOps.FindParent</c>) and cannot desynchronise. What replaces the
    ///   repair pass is sub-asset lifecycle: creation registers undo and joins the asset file,
    ///   removal clears references first and destroys through Undo.
    /// * Their routes are two fixed Success/Fail node ids. Ours is an ordered
    ///   <see cref="StateTreeTransition"/> list per state (target + condition + interrupt flag),
    ///   so the wiring UI lives in the inspector (see <see cref="StateTreeInspectorPane"/>).
    /// * Play-mode highlight is new here: <c>StateTreeRunner</c> deep-copies the tree on start,
    ///   so the running nodes are clones and the highlight matches by nodeId string
    ///   (see <see cref="StateTreeRunnerProbe"/>).
    ///
    /// Saving is batched. Sub-asset edits mark objects dirty immediately (so the runner and the
    /// inspector see them at once) and AssetDatabase.SaveAssets runs once the author stops
    /// typing, on window close, and before entering play mode.
    /// </summary>
    public sealed class StateTreeEditorWindow : EditorWindow
    {
        private const string k_AutoOpenKey = "PowerOfFire.DrawToPlay.StateTreeAutoOpen";
        private const string k_DragKey = "PowerOfFire.DrawToPlay.StateTreeDrag";
        private static string k_TreeFolder => DrawToPlayFolders.Trees;

        /// <summary>Seconds of quiet before the batched save fires. Long enough that dragging a
        /// float field does not write the asset on every frame, short enough that a crash never
        /// costs more than one edit.</summary>
        private const double k_SaveDelay = 0.75d;

        private static readonly Color k_RowActive = new Color(0.42f, 0.78f, 0.42f, 0.45f);
        private static readonly Color k_RowPrevious = new Color(0.95f, 0.74f, 0.25f, 0.22f);
        private static readonly Color k_RowClear = new Color(0f, 0f, 0f, 0f);
        private static readonly Color k_PanelBackground = new Color(0f, 0f, 0f, 0.10f);

        [SerializeField] private StateTreeAsset m_Tree;
        [SerializeField] private string m_SelectedNodeId = string.Empty;

        private readonly StateTreeRunnerProbe m_Probe = new StateTreeRunnerProbe();

        private Dictionary<StateTreeNodeAsset, int> m_RowIds =
            new Dictionary<StateTreeNodeAsset, int>();
        private Dictionary<int, StateTreeNodeAsset> m_RowNodes =
            new Dictionary<int, StateTreeNodeAsset>();
        private readonly List<string> m_RowOrder = new List<string>();
        private int m_NextRowId = 1;

        private ObjectField m_TreeField;
        private TreeView m_TreeView;
        private VisualElement m_EmptyHint;
        private Button m_CreateRootButton;
        private ScrollView m_InspectorScroll;
        private StateTreeInspectorPane m_Inspector;
        private Label m_StatusLabel;

        /// <summary>The whole bottom strip, so <see cref="UpdateStatus"/> can remove it
        /// outside play mode — at edit time it repeated what the pane already shows, which
        /// made it permanent chrome (user: a bar that is always there is a bar nobody reads).</summary>
        private VisualElement m_StatusBar;
        private DropdownField m_RunnerDropdown;

        private StateTreeNodeAsset m_SelectedNode;
        private bool m_SaveScheduled;
        private double m_LastEditTime;

        /// <summary>Selecting a tree asset in the Project window brings this window up, the way
        /// the HeavenlyTreasures AIDraftAutoOpen hook does. Kept behind a preference because a
        /// window that steals focus on every click is a window people close.</summary>
        public static bool autoOpenOnSelection
        {
            get => EditorPrefs.GetBool(k_AutoOpenKey, true);
            set => EditorPrefs.SetBool(k_AutoOpenKey, value);
        }

        /// <summary>The asset currently being edited — no copy, no proxy.</summary>
        public StateTreeAsset treeAsset => m_Tree;

        /// <summary>Node ids of the rows the tree view is showing, in display order. Exists for
        /// the M7b verification menu, which asserts the window renders the real states.</summary>
        public IReadOnlyList<string> visibleNodeIds => m_RowOrder;

        /// <summary>The state the matching runner is in while playing, "" otherwise.</summary>
        public string activeNodeId => m_Probe.activeNodeId;

        // --- entry points -----------------------------------------------------------------

        [MenuItem("Tools/Draw To Play/State Tree Editor")]
        private static void OpenFromMenu()
        {
            Open(ResolveTree(Selection.activeObject));
        }

        public static StateTreeEditorWindow Open(StateTreeAsset tree)
        {
            var window = GetWindow<StateTreeEditorWindow>();
            window.Show();
            if (tree != null)
                window.LoadTree(tree);
            return window;
        }

        /// <summary>Double-clicking a tree asset (or any of its sub-assets) opens this window
        /// instead of pinging the inspector.</summary>
        [OnOpenAsset(1)]
        private static bool OnOpenTreeAsset(EntityId entityId, int line)
        {
            var tree = ResolveTree(EditorUtility.EntityIdToObject(entityId));
            if (tree == null)
                return false;

            Open(tree);
            return true;
        }

        /// <summary>Everything that reasonably means "this state tree": the asset, any of its
        /// sub-assets (nodes, tasks, conditions all live in the same file), a runner component,
        /// or a GameObject carrying one.</summary>
        internal static StateTreeAsset ResolveTree(UnityEngine.Object target)
        {
            switch (target)
            {
                case null:
                    return null;
                case StateTreeAsset tree:
                    return tree;
                case StateTreeRunner runner:
                    return runner.data;
                case GameObject gameObject:
                {
                    var runner = gameObject.GetComponentInChildren<StateTreeRunner>();
                    return runner != null ? runner.data : null;
                }
            }

            if (target is StateTreeNodeAsset || target is StateTreeTaskAsset
                || target is StateTreeConditionAsset)
            {
                var path = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(path))
                    return AssetDatabase.LoadMainAssetAtPath(path) as StateTreeAsset;
            }

            return null;
        }

        public void LoadTree(StateTreeAsset tree)
        {
            if (tree != m_Tree)
            {
                FlushSave();
                m_Tree = tree;
                m_SelectedNode = null;
                m_SelectedNodeId = string.Empty;
                m_Probe.SetTree(tree);
            }

            m_TreeField?.SetValueWithoutNotify(tree);
            RebuildAll();
        }

        /// <summary>Select a state by id. Returns false when the tree has no such state — the
        /// verification menu relies on that answer.</summary>
        public bool SelectNode(string nodeId)
        {
            var nodes = StateTreeEditorOps.CollectNodes(m_Tree);
            for (var i = 0; i < nodes.Count; ++i)
            {
                if (nodes[i].nodeId != nodeId)
                    continue;

                SetSelectedNode(nodes[i]);
                if (m_TreeView != null && m_RowIds.TryGetValue(nodes[i], out var rowId))
                    m_TreeView.SetSelectionByIdWithoutNotify(new List<int> { rowId });
                return true;
            }

            return false;
        }

        // --- lifecycle --------------------------------------------------------------------

        private void OnEnable()
        {
            titleContent = new GUIContent("State Tree");
            minSize = new Vector2(560f, 320f);

            m_Probe.changed += OnRunnerStateChanged;
            m_Probe.SetTree(m_Tree);

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.projectChanged -= OnProjectChanged;

            m_Probe.changed -= OnRunnerStateChanged;
            m_Probe.Clear();
            FlushSave();
        }

        /// <summary>Follow the Project-window selection while open (the window is a view of
        /// whatever tree the author is looking at). Opening when closed is the separate
        /// <see cref="StateTreeEditorAutoOpen"/> hook's job.</summary>
        private void OnSelectionChange()
        {
            var tree = ResolveTree(Selection.activeObject);
            if (tree != null && tree != m_Tree)
                LoadTree(tree);
        }

        private void OnUndoRedoPerformed()
        {
            // Undo can destroy the selected node outright; re-resolve it by id.
            m_SelectedNode = null;
            RebuildAll();
            if (!string.IsNullOrEmpty(m_SelectedNodeId))
                SelectNode(m_SelectedNodeId);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // The asset must be on disk before the domain reloads, so what the runner
                    // starts is exactly what the window shows.
                    FlushSave();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    m_Probe.Clear();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    RefreshRows();
                    break;
            }
        }

        private void OnEditorUpdate()
        {
            m_Probe.Poll();

            if (m_SaveScheduled && EditorApplication.timeSinceStartup - m_LastEditTime > k_SaveDelay)
                FlushSave();
        }

        // --- construction -----------------------------------------------------------------

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();

            root.Add(BuildToolbar());

            var split = new TwoPaneSplitView(0, 320f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            root.Add(split);

            split.Add(BuildTreePane());
            split.Add(BuildInspectorPane());

            root.Add(BuildStatusBar());

            RebuildAll();
        }

        private VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar();

            m_TreeField = new ObjectField
            {
                objectType = typeof(StateTreeAsset),
                allowSceneObjects = false,
                value = m_Tree
            };
            m_TreeField.style.width = 220f;
            m_TreeField.RegisterValueChangedCallback(evt => LoadTree(evt.newValue as StateTreeAsset));
            toolbar.Add(m_TreeField);

            var treeSettings = new ToolbarButton(ShowTreeSettings) { text = "Tree…" };
            treeSettings.tooltip = "Edit the tree's own name and kind, including \"reusable "
                + "task\" — which lists this tree in every other tree's Add Task picker.";
            toolbar.Add(treeSettings);

            var addState = new ToolbarButton(AddState) { text = "Add State" };
            addState.tooltip = "Add a state under the root.";
            toolbar.Add(addState);

            var addChild = new ToolbarButton(AddChildState) { text = "Add Child" };
            addChild.tooltip = "Add a state under the selected one. A parent with no tasks and no "
                + "transitions is organisational: the runner enters its first child instead.";
            toolbar.Add(addChild);

            var remove = new ToolbarButton(RemoveSelectedState) { text = "Remove" };
            remove.tooltip = "Delete the selected state, its children, and their task/condition "
                + "sub-assets.";
            toolbar.Add(remove);

            var up = new ToolbarButton(() => MoveSelectedState(-1)) { text = "▲" };
            up.tooltip = "Move the selected state up among its siblings. The FIRST child of an "
                + "organisational parent is the one the runner enters.";
            toolbar.Add(up);

            var down = new ToolbarButton(() => MoveSelectedState(1)) { text = "▼" };
            down.tooltip = "Move the selected state down among its siblings.";
            toolbar.Add(down);

            var spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            var newTree = new ToolbarButton(CreateTreeAsset) { text = "New Tree…" };
            newTree.tooltip = "Create a State Tree asset with an empty root.";
            toolbar.Add(newTree);

            var save = new ToolbarButton(FlushSave) { text = "Save" };
            save.tooltip = "Write pending edits now (they are also saved automatically).";
            toolbar.Add(save);

            var autoOpen = new ToolbarToggle { text = "Auto-open", value = autoOpenOnSelection };
            autoOpen.tooltip = "Open this window whenever a State Tree asset is selected.";
            autoOpen.RegisterValueChangedCallback(evt => autoOpenOnSelection = evt.newValue);
            toolbar.Add(autoOpen);

            return toolbar;
        }

        private VisualElement BuildTreePane()
        {
            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.minWidth = 240f;
            pane.style.paddingLeft = 6f;
            pane.style.paddingTop = 4f;

            var header = new Label("States");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            pane.Add(header);

            m_EmptyHint = new VisualElement();
            m_EmptyHint.Add(new HelpBox(
                "No state tree loaded. Select one in the Project window, drop it in the field "
                + "above, or press New Tree…", HelpBoxMessageType.Info));
            m_CreateRootButton = new Button(CreateRootState) { text = "Create Root State" };
            m_EmptyHint.Add(m_CreateRootButton);
            pane.Add(m_EmptyHint);

            m_TreeView = new TreeView
            {
                fixedItemHeight = 24f,
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                autoExpand = true,
                reorderable = true
            };
            m_TreeView.style.flexGrow = 1f;
            m_TreeView.makeItem = () => new VisualElement();
            m_TreeView.bindItem = BindRow;
            m_TreeView.unbindItem = (element, index) => element.Clear();
            m_TreeView.selectionChanged += OnRowSelectionChanged;

            m_TreeView.canStartDrag += _ => m_Tree != null;
            m_TreeView.setupDragAndDrop += args =>
            {
                var start = new StartDragArgs("State", DragVisualMode.Move);
                start.SetGenericData(k_DragKey, new List<int>(args.selectedIds));
                return start;
            };
            m_TreeView.dragAndDropUpdate += args =>
                args.dragAndDropData.GetGenericData(k_DragKey) is List<int>
                    ? DragVisualMode.Move
                    : DragVisualMode.Rejected;
            m_TreeView.handleDrop += args =>
            {
                var ids = args.dragAndDropData.GetGenericData(k_DragKey) as List<int>;
                return ApplyDrop(ids, args.parentId, args.childIndex);
            };

            pane.Add(m_TreeView);
            return pane;
        }

        private VisualElement BuildInspectorPane()
        {
            m_InspectorScroll = new ScrollView(ScrollViewMode.Vertical);
            m_InspectorScroll.style.flexGrow = 1f;
            m_InspectorScroll.style.paddingLeft = 8f;
            m_InspectorScroll.style.paddingRight = 8f;
            m_InspectorScroll.style.paddingTop = 4f;
            m_InspectorScroll.style.backgroundColor = k_PanelBackground;
            m_InspectorScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            m_Inspector = new StateTreeInspectorPane(m_InspectorScroll, OnStructuralChanged, OnEdited);
            return m_InspectorScroll;
        }

        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement();
            m_StatusBar = bar;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 6f;
            bar.style.paddingRight = 6f;
            bar.style.paddingTop = 2f;
            bar.style.paddingBottom = 2f;
            bar.style.backgroundColor = k_PanelBackground;
            // Play-mode surface only — UpdateStatus shows it when there is a live story
            // (the runner trace) and keeps edit mode free of permanent chrome.
            bar.style.display = DisplayStyle.None;

            m_StatusLabel = new Label(string.Empty);
            m_StatusLabel.style.flexGrow = 1f;
            bar.Add(m_StatusLabel);

            m_RunnerDropdown = new DropdownField(new List<string>(), 0);
            m_RunnerDropdown.style.width = 200f;
            m_RunnerDropdown.style.display = DisplayStyle.None;
            m_RunnerDropdown.tooltip = "Which runner in the scene this window follows.";
            m_RunnerDropdown.RegisterValueChangedCallback(_ =>
            {
                var index = m_RunnerDropdown.index;
                if (index >= 0 && index < m_Probe.runners.Count)
                    m_Probe.SelectRunner(m_Probe.runners[index]);
            });
            bar.Add(m_RunnerDropdown);

            return bar;
        }

        // --- tree view --------------------------------------------------------------------

        private void RebuildAll()
        {
            RebuildTree();
            RebuildInspector();
        }

        /// <summary>What the view currently SHOWS — compared against the asset on project
        /// changes, because an external rewrite (a demo builder's Verify) destroys and
        /// recreates every sub-asset while this window keeps drawing the corpses. Our own
        /// batched saves never destroy nodes, so a destroyed entry here (or a changed count)
        /// is precisely the external-rewrite signature and the only projectChanged that may
        /// rebuild — rebuilding on every save would eat the focus the batching protects.</summary>
        private List<StateTreeNodeAsset> m_ViewNodes = new List<StateTreeNodeAsset>();

        private void OnProjectChanged()
        {
            if (m_Tree == null || m_TreeView == null)
                return;

            var stale = false;
            for (var i = 0; i < m_ViewNodes.Count && !stale; ++i)
                stale = m_ViewNodes[i] == null;
            if (!stale)
                stale = StateTreeEditorOps.CollectNodes(m_Tree).Count != m_ViewNodes.Count;
            if (stale)
                RebuildAll();
        }

        private void RebuildTree()
        {
            if (m_TreeView == null)
                return;

            var nodes = StateTreeEditorOps.CollectNodes(m_Tree);
            m_ViewNodes = nodes;
            RebuildRowIds(nodes);

            m_RowOrder.Clear();
            for (var i = 0; i < nodes.Count; ++i)
                m_RowOrder.Add(nodes[i].nodeId);

            var items = new List<TreeViewItemData<StateTreeNodeAsset>>();
            if (m_Tree != null && m_Tree.root != null)
            {
                var visited = new HashSet<StateTreeNodeAsset> { m_Tree.root };
                items.Add(BuildItem(m_Tree.root, 0, visited));
            }

            m_TreeView.SetRootItems(items);
            m_TreeView.Rebuild();
            m_TreeView.ExpandAll();

            var hasRows = items.Count > 0;
            m_TreeView.style.display = hasRows ? DisplayStyle.Flex : DisplayStyle.None;
            m_EmptyHint.style.display = hasRows ? DisplayStyle.None : DisplayStyle.Flex;
            m_CreateRootButton.style.display = m_Tree != null && m_Tree.root == null
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (m_SelectedNode != null && m_RowIds.TryGetValue(m_SelectedNode, out var rowId))
                m_TreeView.SetSelectionByIdWithoutNotify(new List<int> { rowId });

            UpdateStatus();

            // TreeView binds rows on the next layout pass; asking for a repaint here is what
            // makes a structural edit (or a programmatic Open) show up without waiting for the
            // author to move the mouse over the window.
            Repaint();
        }

        /// <summary>A TreeView id may appear exactly once, so a node reached twice (an authored
        /// cycle, or the same state listed under two parents) is shown at its first location and
        /// skipped afterwards — the inspector's validation is where that gets reported, not a
        /// broken tree view.</summary>
        private TreeViewItemData<StateTreeNodeAsset> BuildItem(StateTreeNodeAsset node, int depth,
            HashSet<StateTreeNodeAsset> visited)
        {
            var children = new List<TreeViewItemData<StateTreeNodeAsset>>();
            if (depth < StateTreeEditorOps.DepthGuard)
            {
                for (var i = 0; i < node.children.Count; ++i)
                {
                    var child = node.children[i];
                    if (child == null || !visited.Add(child))
                        continue;

                    children.Add(BuildItem(child, depth + 1, visited));
                }
            }

            return new TreeViewItemData<StateTreeNodeAsset>(RowIdFor(node), node, children);
        }

        private int RowIdFor(StateTreeNodeAsset node)
        {
            if (m_RowIds.TryGetValue(node, out var id))
                return id;

            id = m_NextRowId++;
            m_RowIds[node] = id;
            m_RowNodes[id] = node;
            return id;
        }

        /// <summary>Row ids stay stable across rebuilds so selection and expansion survive an
        /// edit. Rebuilt from the live node list rather than copied, because a dictionary keyed
        /// by destroyed Objects is a duplicate-key exception waiting to happen.</summary>
        private void RebuildRowIds(List<StateTreeNodeAsset> nodes)
        {
            var byNode = new Dictionary<StateTreeNodeAsset, int>(nodes.Count);
            var byId = new Dictionary<int, StateTreeNodeAsset>(nodes.Count);

            for (var i = 0; i < nodes.Count; ++i)
            {
                var node = nodes[i];
                if (!m_RowIds.TryGetValue(node, out var id))
                    id = m_NextRowId++;

                byNode[node] = id;
                byId[id] = node;
            }

            m_RowIds = byNode;
            m_RowNodes = byId;
        }

        private void BindRow(VisualElement element, int index)
        {
            element.Clear();

            var node = m_TreeView.GetItemDataForIndex<StateTreeNodeAsset>(index);
            if (node == null)
            {
                element.Add(new Label("(missing state)"));
                return;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 4f;
            row.style.paddingRight = 2f;
            row.style.borderTopLeftRadius = 4f;
            row.style.borderTopRightRadius = 4f;
            row.style.borderBottomLeftRadius = 4f;
            row.style.borderBottomRightRadius = 4f;
            row.style.backgroundColor = RowTint(node);

            var title = new Label(RowTitle(node));
            title.style.flexGrow = 1f;
            title.style.overflow = Overflow.Hidden;
            row.Add(title);

            var badge = new Label(RowBadge(node));
            badge.style.opacity = 0.65f;
            badge.style.marginRight = 4f;
            row.Add(badge);

            var addChild = new Button(() => AddChildTo(node)) { text = "+" };
            addChild.tooltip = "Add a child state.";
            addChild.style.width = 22f;
            row.Add(addChild);

            var menu = new Button(() => ShowRowMenu(node)) { text = "⋮" };
            menu.style.width = 22f;
            row.Add(menu);

            element.Add(row);
        }

        private Color RowTint(StateTreeNodeAsset node)
        {
            if (!m_Probe.isTracking)
                return k_RowClear;
            if (!string.IsNullOrEmpty(m_Probe.activeNodeId) && node.nodeId == m_Probe.activeNodeId)
                return k_RowActive;
            if (!string.IsNullOrEmpty(m_Probe.previousNodeId) && node.nodeId == m_Probe.previousNodeId)
                return k_RowPrevious;
            return k_RowClear;
        }

        private string RowTitle(StateTreeNodeAsset node)
        {
            var text = StateTreeInspectorPane.FormatNode(node);
            if (m_Tree != null && node == m_Tree.root)
                text += "  (root)";
            else if (m_Tree != null && node == StateTreeEditorOps.ResolveEntryNode(m_Tree.root))
                text += "  ▶ entry";
            return text;
        }

        private string RowBadge(StateTreeNodeAsset node)
        {
            var interrupts = 0;
            for (var i = 0; i < node.transitions.Count; ++i)
            {
                if (node.transitions[i] != null && node.transitions[i].checkWhileRunning)
                    ++interrupts;
            }

            var badge = $"T{node.tasks.Count} →{node.transitions.Count}";
            if (interrupts > 0)
                badge += $" ⚡{interrupts}";
            if (!string.IsNullOrEmpty(node.roleKind))
                badge = "⟨" + node.roleKind + "⟩ " + badge;
            // The GHOST EDGE (M22), in badge form: where completion goes when no declared edge
            // fires — '⇢id' the implicit sibling, '⇢↑' a bubble, '⇢■' finishes the tree,
            // '∞' resident, '⊣' hold. A default the author can see instead of a trap.
            var ghost = StateTreeEditorOps.ImplicitBadge(m_Tree, node);
            return string.IsNullOrEmpty(ghost) ? badge : badge + "  " + ghost;
        }

        private void OnRowSelectionChanged(IEnumerable<object> selection)
        {
            StateTreeNodeAsset selected = null;
            foreach (var item in selection)
            {
                selected = item as StateTreeNodeAsset;
                break;
            }

            SetSelectedNode(selected);
        }

        /// <summary>Deselect, which is how the tree-level fields (name, kind, "reusable task")
        /// are reached: the inspector pane shows them when no state is selected, and a tree view
        /// otherwise offers no way back out of a selection.</summary>
        private void ShowTreeSettings()
        {
            m_TreeView?.SetSelectionByIdWithoutNotify(new List<int>());
            SetSelectedNode(null);
        }

        private void SetSelectedNode(StateTreeNodeAsset node)
        {
            m_SelectedNode = node;
            m_SelectedNodeId = node != null ? node.nodeId : string.Empty;
            RebuildInspector();
        }

        private void RebuildInspector()
        {
            m_Inspector?.Rebuild(m_Tree, m_SelectedNode);
        }

        private void RefreshRows()
        {
            m_TreeView?.RefreshItems();
            UpdateStatus();
            Repaint();
        }

        private void ShowRowMenu(StateTreeNodeAsset node)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Child State"), false, () => AddChildTo(node));
            menu.AddItem(new GUIContent("Move Up"), false, () => MoveState(node, -1));
            menu.AddItem(new GUIContent("Move Down"), false, () => MoveState(node, 1));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Ping Sub-Asset"), false, () => EditorGUIUtility.PingObject(node));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Remove State"), false, () => RemoveState(node));
            menu.ShowAsContext();
        }

        // --- structural commands ----------------------------------------------------------

        private void AddState()
        {
            if (m_Tree == null)
                return;

            if (m_Tree.root == null)
            {
                CreateRootState();
                return;
            }

            AddChildTo(m_Tree.root);
        }

        private void AddChildState()
        {
            if (m_Tree == null)
                return;

            AddChildTo(m_SelectedNode != null ? m_SelectedNode : m_Tree.root);
        }

        private void AddChildTo(StateTreeNodeAsset parent)
        {
            if (m_Tree == null || parent == null)
                return;

            // THE RULES TYPE THE GESTURE (M23, the HT ability editor's law on state trees):
            // in a tree a service claims, Add Child under the root creates the first kind the
            // rules allow there — an 'effect' state, born holding its seed task — and under a
            // leaf kind it refuses outright. The type stays changeable in the inspector,
            // among what the rules offer.
            var service = StateTreeEditorOps.ServiceClaiming(m_Tree);
            string childKind = null;
            if (service != null)
            {
                string parentRole = StateTreeEditorOps.EffectiveRoleOf(m_Tree, parent);
                var allowed = service.AllowedUnder(parentRole);
                if (!string.IsNullOrEmpty(parentRole) && allowed.Count == 0)
                {
                    EditorUtility.DisplayDialog("Add Child",
                        $"A '{parentRole}' state is a leaf in the '{service.serviceName}' "
                        + "service's rules — nothing nests under it.", "OK");
                    return;
                }
                if (allowed.Count > 0)
                    childKind = allowed[0];
            }

            var group = StateTreeEditorOps.BeginUndoGroup("Add State");
            var node = StateTreeEditorOps.CreateNode(m_Tree, parent,
                childKind ?? "state", childKind != null
                    ? char.ToUpperInvariant(childKind[0]) + childKind.Substring(1)
                    : "State",
                "Add State");
            if (node != null && childKind != null)
            {
                node.roleKind = childKind;
                var seedType = StateTreeEditorOps.SeedTaskType(service, childKind);
                if (seedType != null)
                    StateTreeEditorOps.CreateTask(m_Tree, node, seedType, "Add State");
                EditorUtility.SetDirty(node);
            }
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            OnStructuralChanged();
            if (node != null)
                SelectNode(node.nodeId);
        }

        private void CreateRootState()
        {
            if (m_Tree == null || m_Tree.root != null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup("Create Root State");
            var name = string.IsNullOrEmpty(m_Tree.treeName) ? m_Tree.name : m_Tree.treeName;
            var root = StateTreeEditorOps.CreateNode(m_Tree, null, "root", name, "Create Root State");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            OnStructuralChanged();
            if (root != null)
                SelectNode(root.nodeId);
        }

        private void RemoveSelectedState()
        {
            RemoveState(m_SelectedNode);
        }

        private void RemoveState(StateTreeNodeAsset node)
        {
            if (m_Tree == null || node == null)
                return;

            if (node.children.Count > 0 && !EditorUtility.DisplayDialog("Remove state?",
                    $"'{node.nodeId}' has {node.children.Count} child state(s). Removing it removes "
                    + "them and every task/condition they own.", "Remove", "Cancel"))
                return;

            var parent = StateTreeEditorOps.FindParent(m_Tree, node);

            var group = StateTreeEditorOps.BeginUndoGroup("Remove State");
            StateTreeEditorOps.RemoveNode(m_Tree, node, "Remove State");
            StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            m_SelectedNode = parent;
            m_SelectedNodeId = parent != null ? parent.nodeId : string.Empty;
            OnStructuralChanged();
        }

        private void MoveSelectedState(int delta)
        {
            MoveState(m_SelectedNode, delta);
        }

        private void MoveState(StateTreeNodeAsset node, int delta)
        {
            if (m_Tree == null || node == null)
                return;

            var group = StateTreeEditorOps.BeginUndoGroup("Reorder State");
            var moved = StateTreeEditorOps.MoveNodeSibling(m_Tree, node, delta, "Reorder State");
            StateTreeEditorOps.EndUndoGroup(group);

            if (moved)
                OnStructuralChanged();
        }

        /// <summary>Drag-and-drop reparent/reorder. The model has exactly one root, so a drop
        /// with no parent row (the tree view's "top level") is rejected rather than silently
        /// turned into something else.</summary>
        private DragVisualMode ApplyDrop(List<int> ids, int parentId, int childIndex)
        {
            if (m_Tree == null || ids == null || ids.Count == 0)
                return DragVisualMode.Rejected;

            if (!m_RowNodes.TryGetValue(parentId, out var parent) || parent == null)
                return DragVisualMode.Rejected;

            var group = StateTreeEditorOps.BeginUndoGroup("Move State");
            var moved = false;
            for (var i = 0; i < ids.Count; ++i)
            {
                if (!m_RowNodes.TryGetValue(ids[i], out var node) || node == null)
                    continue;

                moved |= StateTreeEditorOps.MoveNode(m_Tree, node, parent,
                    childIndex < 0 ? -1 : childIndex + i, "Move State");
            }

            if (moved)
                StateTreeEditorOps.RefreshSubAssetNames(m_Tree);
            StateTreeEditorOps.EndUndoGroup(group);

            if (!moved)
                return DragVisualMode.Rejected;

            OnStructuralChanged();
            return DragVisualMode.Move;
        }

        private void CreateTreeAsset()
        {
            DrawToPlayFolders.Ensure(k_TreeFolder);

            var path = EditorUtility.SaveFilePanelInProject("New State Tree", "StateTree", "asset",
                "Create a State Tree asset", k_TreeFolder);
            if (string.IsNullOrEmpty(path))
                return;

            // Asset creation is not undoable (AssetDatabase.CreateAsset never was), so this path
            // deliberately skips the undo group the edit commands use.
            var tree = CreateInstance<StateTreeAsset>();
            var treeName = Path.GetFileNameWithoutExtension(path);
            tree.name = treeName;
            tree.treeName = treeName;
            AssetDatabase.CreateAsset(tree, path);

            var root = CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            root.displayName = treeName;
            root.name = StateTreeEditorOps.NodeAssetName(0, root.nodeId);
            AssetDatabase.AddObjectToAsset(root, tree);
            tree.root = root;

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();

            LoadTree(tree);
            SelectNode(root.nodeId);
        }

        // --- edit plumbing ----------------------------------------------------------------

        private void OnStructuralChanged()
        {
            if (m_Tree != null)
                EditorUtility.SetDirty(m_Tree);

            ScheduleSave();
            RebuildAll();
        }

        private void OnEdited()
        {
            if (m_Tree != null)
                EditorUtility.SetDirty(m_Tree);

            ScheduleSave();
            RefreshRows();
        }

        private void ScheduleSave()
        {
            m_SaveScheduled = true;
            m_LastEditTime = EditorApplication.timeSinceStartup;
        }

        private void FlushSave()
        {
            if (!m_SaveScheduled)
                return;

            m_SaveScheduled = false;
            AssetDatabase.SaveAssets();
        }

        // --- play-mode status -------------------------------------------------------------

        private void OnRunnerStateChanged()
        {
            RefreshRows();
        }

        /// <summary>The bottom bar exists only while it has a live story to tell: playing
        /// with a tree loaded. Everything it used to say at edit time (state count, entry,
        /// "edits apply on the next run") repeated what the panes already show, so outside
        /// play mode the bar is GONE rather than idling as chrome.</summary>
        private void UpdateStatus()
        {
            if (m_StatusLabel == null)
                return;

            var show = EditorApplication.isPlaying && m_Tree != null;
            if (m_StatusBar != null)
                m_StatusBar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                SetRunnerChoices();
                return;
            }

            if (m_Probe.runner == null)
            {
                m_StatusLabel.text = "Playing — no runner in the scene uses this tree.";
                SetRunnerChoices();
                return;
            }

            var previous = string.IsNullOrEmpty(m_Probe.previousNodeId)
                ? "(start)"
                : m_Probe.previousNodeId;
            var active = string.IsNullOrEmpty(m_Probe.activeNodeId) ? "(stopped)" : m_Probe.activeNodeId;
            m_StatusLabel.text = $"▶ {m_Probe.runner.name}:  {previous}  →  {active}";
            SetRunnerChoices();
        }

        private void SetRunnerChoices()
        {
            if (m_RunnerDropdown == null)
                return;

            var runners = m_Probe.runners;
            if (!EditorApplication.isPlaying || runners.Count < 2)
            {
                m_RunnerDropdown.style.display = DisplayStyle.None;
                return;
            }

            var choices = new List<string>(runners.Count);
            var selected = 0;
            for (var i = 0; i < runners.Count; ++i)
            {
                choices.Add(runners[i].name);
                if (runners[i] == m_Probe.runner)
                    selected = i;
            }

            m_RunnerDropdown.style.display = DisplayStyle.Flex;
            m_RunnerDropdown.choices = choices;
            m_RunnerDropdown.SetValueWithoutNotify(choices[selected]);
        }
    }
}
