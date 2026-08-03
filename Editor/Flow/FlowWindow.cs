using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The creation-flow window (draw-tool-port-brief.md §6): a stage tab strip across the
    /// top, a state badge per tab, and the selected stage's description + checklist below.
    /// Clicking a tab activates that stage's tool and turns on the overlays it needs — it is
    /// a shortcut into the same tools, never a wizard step. Nothing here is modal, no tab is
    /// ever disabled, and working out of order is legal; the badges just tell the truth.
    ///
    /// The window has no knowledge of any specific flow: it renders whatever
    /// <see cref="FlowDefinition"/> it is given and asks <see cref="FlowValidators"/> for
    /// badge state by stage id. M1 ships the Terrain flow's Sculpt and Collision tabs.
    /// </summary>
    public sealed class FlowWindow : EditorWindow
    {
        // The collision debug toggle is one EditorPrefs bool ("PowerOfFire.DrawToPlay.DebugCollision")
        // owned by CollisionDebugOverlay and shared with the Draw-to-Play scene overlay panel.
        // This window drives it through CollisionDebugOverlay.enabled rather than writing the
        // key itself, so all three toggles stay the same switch.

        /// <summary>Where trees authored from the Behavior / AI tabs land. Beside the preset trees
        /// in spirit, in their own folder in practice, so "Create Enemy Preset Trees" can never
        /// overwrite something a user authored here.</summary>
        private const string k_TreeFolder = "Assets/DrawToPlay/Trees";

        // Badge palette. Deliberately mid-tone so it reads on both editor skins.
        private static readonly Color k_BadgeUnknown = new Color(0.55f, 0.55f, 0.55f, 0.35f);
        private static readonly Color k_BadgeEmpty = new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color k_BadgeInProgress = new Color(0.95f, 0.74f, 0.25f, 1f);
        private static readonly Color k_BadgeComplete = new Color(0.42f, 0.78f, 0.42f, 1f);
        private static readonly Color k_BadgeInvalidated = new Color(0.95f, 0.48f, 0.25f, 1f);

        private static readonly Color k_TabIdle = new Color(0f, 0f, 0f, 0.16f);
        private static readonly Color k_TabActive = new Color(0.30f, 0.55f, 0.92f, 0.32f);
        private static readonly Color k_TabActiveEdge = new Color(0.42f, 0.66f, 1f, 0.95f);
        private static readonly Color k_PanelBackground = new Color(0f, 0f, 0f, 0.10f);

        [SerializeField] private FlowDefinition m_Definition;
        [SerializeField] private int m_ActiveStageIndex;

        private readonly List<StageTab> m_Tabs = new List<StageTab>();

        private ObjectField m_DefinitionField;
        private VisualElement m_TabStrip;
        private VisualElement m_StagePanel;

        [MenuItem("Tools/Draw To Play/Flow Window")]
        private static void Open()
        {
            var window = GetWindow<FlowWindow>();
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Draw Flow");
            minSize = new Vector2(340f, 240f);

            // Auto-load the built-in Terrain flow when it exists; never create it here.
            if (m_Definition == null)
                m_Definition = TerrainFlowAsset.LoadIfPresent();

            EditorApplication.hierarchyChanged += OnEditorStateChanged;
            Selection.selectionChanged += OnEditorStateChanged;
            Undo.undoRedoPerformed += OnEditorStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnEditorStateChanged;
            Selection.selectionChanged -= OnEditorStateChanged;
            Undo.undoRedoPerformed -= OnEditorStateChanged;
        }

        // --- construction -----------------------------------------------------------------

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 6f;
            root.style.paddingBottom = 6f;

            root.Add(BuildHeader());

            m_TabStrip = new VisualElement();
            m_TabStrip.style.flexDirection = FlexDirection.Row;
            m_TabStrip.style.flexWrap = Wrap.Wrap;
            m_TabStrip.style.marginTop = 6f;
            root.Add(m_TabStrip);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.marginTop = 6f;
            root.Add(scroll);

            m_StagePanel = new VisualElement();
            m_StagePanel.style.paddingLeft = 10f;
            m_StagePanel.style.paddingRight = 10f;
            m_StagePanel.style.paddingTop = 8f;
            m_StagePanel.style.paddingBottom = 10f;
            m_StagePanel.style.backgroundColor = k_PanelBackground;
            SetBorderRadius(m_StagePanel, 4f);
            scroll.Add(m_StagePanel);

            RebuildTabs();
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            m_DefinitionField = new ObjectField("Flow")
            {
                objectType = typeof(FlowDefinition),
                allowSceneObjects = false,
                value = m_Definition
            };
            m_DefinitionField.style.flexGrow = 1f;
            m_DefinitionField.RegisterValueChangedCallback(evt =>
            {
                m_Definition = evt.newValue as FlowDefinition;
                m_ActiveStageIndex = 0;
                RebuildTabs();
            });
            header.Add(m_DefinitionField);

            var refresh = new Button(() =>
            {
                // Also forget the cached graph-frontend lookups. No tab reaches them any more
                // (M7b: the Behavior/AI tabs open the direct editor), but "Verify M7 Graph
                // Frontend" still reads that cache, and Refresh is the one button a user presses
                // after recompiling something — having to reopen a window to clear a cache would
                // be a silly thing to have to know.
                StateTreeGraphBridge.InvalidateCache();
                RefreshBadges();
            })
            { text = "Refresh" };
            refresh.tooltip = "Re-run every stage validator now.";
            refresh.style.marginLeft = 4f;
            header.Add(refresh);

            return header;
        }

        /// <summary>Rebuild the tab strip from the current definition, then draw the active
        /// stage. Pure UI — activating tools and toggling overlays only happens on a click.</summary>
        private void RebuildTabs()
        {
            if (m_TabStrip == null)
                return;

            m_Tabs.Clear();
            m_TabStrip.Clear();

            var count = m_Definition != null ? m_Definition.stageCount : 0;
            m_ActiveStageIndex = count > 0 ? Mathf.Clamp(m_ActiveStageIndex, 0, count - 1) : 0;

            for (var i = 0; i < count; ++i)
            {
                var stage = m_Definition.GetStage(i);
                if (stage == null)
                    continue;

                m_Tabs.Add(BuildTab(stage, i));
            }

            RebuildStagePanel();
            RefreshBadges();
        }

        private StageTab BuildTab(FlowStage stage, int index)
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 4f;
            root.style.paddingBottom = 4f;
            root.style.marginRight = 4f;
            root.style.marginBottom = 4f;
            root.style.backgroundColor = k_TabIdle;
            SetBorderRadius(root, 4f);

            var badge = new VisualElement();
            badge.style.width = 8f;
            badge.style.height = 8f;
            badge.style.marginRight = 6f;
            badge.style.backgroundColor = k_BadgeUnknown;
            SetBorderRadius(badge, 4f);
            root.Add(badge);

            var label = new Label(string.IsNullOrEmpty(stage.title) ? stage.id : stage.title);
            root.Add(label);

            // Clickable rather than a Button so the badge can live inside the tab.
            root.AddManipulator(new Clickable(() => OnTabClicked(index)));
            m_TabStrip.Add(root);

            return new StageTab { stage = stage, root = root, badge = badge };
        }

        // --- interaction ------------------------------------------------------------------

        private void OnTabClicked(int index)
        {
            var stage = m_Definition != null ? m_Definition.GetStage(index) : null;
            if (stage == null)
                return;

            m_ActiveStageIndex = index;

            var notes = new List<string>();
            ActivateStageTool(stage, notes);

            // Collision authoring is only readable with the generated geometry drawn over the
            // art, so entering the stage switches the overlay on. It is a plain preference the
            // user can switch back off — the tab never locks anything.
            if (string.Equals(stage.id, FlowValidators.TerrainCollisionStageId, StringComparison.Ordinal))
            {
                CollisionDebugOverlay.enabled = true;
                notes.Add("Collision debug overlay enabled.");
            }

            // The two behaviour stages (§6.1 Behavior for a player, §6.2 AI for an enemy) are the
            // same editor with a different tree kind, so they open the same way: the tab is a
            // shortcut into the State Tree Editor exactly as the Draw tab is a shortcut into the
            // Draw tool. Failures are notes in the stage panel, never console errors — an entity
            // with no tree yet must stay browsable.
            if (string.Equals(stage.id, FlowValidators.CharacterBehaviorStageId, StringComparison.Ordinal) ||
                string.Equals(stage.id, FlowValidators.EnemyAIStageId, StringComparison.Ordinal))
            {
                OpenStateTreeEditorForSelection(stage.id, notes);
            }

            RebuildStagePanel(notes);
            RefreshBadges();
        }

        /// <summary>Open the selected entity's tree in the State Tree Editor, creating one when
        /// the entity has none.
        ///
        /// M7b MOVED THIS OFF THE GRAPH. Until M7b these two tabs opened the Graph Toolkit
        /// frontend, which authors a SEPARATE document that a bake step then converts into the
        /// <see cref="StateTreeAsset"/> the runner executes. That indirection is the whole problem
        /// it caused: what you edit is not what runs, so every change costs a bake, and a tree the
        /// runner is running has no editor at all unless it happens to have come from a graph.
        /// The direct editor edits the runtime asset itself — the same object the runner
        /// deep-copies on StartTree — so authoring and running are the same file and there is no
        /// step in between to forget. The graph frontend is untouched and still reachable through
        /// its own menus (Assets ▸ Create ▸ Draw To Play ▸ State Tree Graph, and Tools ▸ Draw To
        /// Play ▸ Bake State Tree Graph); it is a visualisation now, not the way in.
        ///
        /// WHICH TREE. The runner's own <c>data</c> when it has one — that is by definition the
        /// tree this entity is running, whether it came from a preset, the Inspector, this editor
        /// or a bake. Otherwise the conventional path for the entity's name, loaded if it is
        /// already there and created (root state included) if it is not, and then assigned to the
        /// runner so the badge, the play-mode highlight and the next click all agree about what
        /// this entity's brain is.
        ///
        /// Creating the .asset is NOT undoable (AssetDatabase creation never is — the same caveat
        /// the drawn-shape tools carry); assigning it to the runner is.</summary>
        private static void OpenStateTreeEditorForSelection(string stageId, List<string> notes)
        {
            var runner = ResolveEntityRunner();
            var entityName = ResolveEntityName(runner);
            var tree = runner != null ? runner.data : null;

            if (tree == null && !TryProvideTree(stageId, entityName, runner, notes, out tree))
                return;

            // Statement, not an assignment: the window owns its own return contract, and this call
            // site only needs it opened on the right asset.
            StateTreeEditorWindow.Open(tree);

            notes.Add($"Opened '{tree.name}' in the State Tree Editor. Every edit writes straight " +
                      "into that asset — the runner reads the same file, so there is nothing to " +
                      "bake and nothing to keep in step.");
        }

        /// <summary>Find (or create) the tree for an entity that has none assigned, as ONE undo
        /// step (m0's undo contract — a tab click is a gesture). False means nothing could be
        /// opened and <paramref name="notes"/> already says why.</summary>
        private static bool TryProvideTree(string stageId, string entityName, StateTreeRunner runner,
            List<string> notes, out StateTreeAsset tree)
        {
            var path = TreeAssetPath(entityName);
            var group = StateTreeEditorOps.BeginUndoGroup("Provide State Tree");

            tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(path);
            if (tree != null)
            {
                notes.Add($"'{entityName}' had no tree assigned, but {path} already exists — " +
                          "opening that rather than creating a second one.");
            }
            else
            {
                tree = CreateTree(path, entityName, TreeKindFor(stageId));
                if (tree == null)
                {
                    StateTreeEditorOps.EndUndoGroup(group);
                    notes.Add($"Could not create {path}: the folder could not be made. Create a " +
                              "State Tree through Assets ▸ Create ▸ Draw To Play ▸ State Tree and " +
                              "drop it on the runner instead.");
                    return false;
                }

                notes.Add($"Created {path} with one root state. Add states under it in the editor; " +
                          "the runner enters the first leaf under the root.");
            }

            if (runner != null)
            {
                Undo.RecordObject(runner, "Provide State Tree");
                runner.data = tree;
                EditorUtility.SetDirty(runner);
                notes.Add($"Assigned it to the StateTreeRunner on '{runner.gameObject.name}'.");
            }
            else
            {
                notes.Add("Nothing in the selection has a StateTreeRunner, so the tree is not " +
                          "wired to anything yet — add a runner to the entity and drop this asset " +
                          "on its Data field.");
            }

            StateTreeEditorOps.EndUndoGroup(group);
            return true;
        }

        /// <summary>Build a new tree asset with the organizational root state every preset uses, so
        /// the thing the editor opens is already a valid tree rather than a null root the runner
        /// would refuse to start. The root goes in through
        /// <see cref="StateTreeEditorOps.CreateNode"/> — the same call the editor's own toolbar
        /// makes — so a tree started from this tab and a tree grown in the window have identical
        /// sub-asset naming and undo behaviour, with one definition of how a state is born.
        /// Null when the folder could not be created.</summary>
        private static StateTreeAsset CreateTree(string assetPath, string treeName, string treeKind)
        {
            // EnsureFolder is plain AssetDatabase and touches nothing graph-related; it lives on
            // the bridge only because that is where the shared copy ended up. Borrowing it beats a
            // third copy of the same fifteen lines.
            if (StateTreeGraphBridge.EnsureFolder(k_TreeFolder) != k_TreeFolder)
                return null;

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = treeName;
            tree.treeName = treeName;
            tree.treeKind = treeKind;

            // The main asset has to be on disk before anything can be added to it.
            AssetDatabase.CreateAsset(tree, assetPath);

            // parent == null means "become the tree root" (StateTreeEditorOps.CreateNode).
            StateTreeEditorOps.CreateNode(tree, null, "root", treeName, "Provide State Tree");

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
            return tree;
        }

        /// <summary>§6.2's enemy tail authors an AI tree, §6.1's Behavior stage a player flow tree.
        /// The runtime does not branch on this — it is the label the graph frontend's EntryNode
        /// writes and the one honest record of which tab a tree was started from.</summary>
        private static string TreeKindFor(string stageId)
        {
            return string.Equals(stageId, FlowValidators.CharacterBehaviorStageId, StringComparison.Ordinal)
                ? "player_flow"
                : "enemy_ai";
        }

        /// <summary>Project path for an entity's tree. One folder rather than "next to the entity"
        /// because a tree is a project asset shared by every instance of an archetype, exactly like
        /// the preset trees it sits beside.</summary>
        private static string TreeAssetPath(string entityName)
        {
            return $"{k_TreeFolder}/{SanitizeFileName(entityName)}.asset";
        }

        /// <summary>The runner whose brain this tab edits: the nearest one at or above the
        /// selection, so clicking a limb opens the character's brain rather than looking for a tree
        /// called "LeftForearm".</summary>
        private static StateTreeRunner ResolveEntityRunner()
        {
            var selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponentInParent<StateTreeRunner>() : null;
        }

        /// <summary>What to call the entity: the runner's GameObject, else the selection, else a
        /// neutral name so the tab still does something useful with nothing selected.</summary>
        private static string ResolveEntityName(StateTreeRunner runner)
        {
            if (runner != null)
                return runner.gameObject.name;

            var selected = Selection.activeGameObject;
            return selected != null ? selected.name : "New State Tree";
        }

        /// <summary>Make a GameObject name safe to use as a file name (kept local rather than
        /// shared so this tab does not depend on the graph bridge's private members).</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Entity";

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(name.Length);
            for (var i = 0; i < name.Length; ++i)
            {
                var c = name[i];
                var bad = c == '.';
                for (var j = 0; j < invalid.Length && !bad; ++j)
                    bad = invalid[j] == c;

                builder.Append(bad ? '_' : c);
            }

            var result = builder.ToString().Trim();
            return result.Length > 0 ? result : "Entity";
        }

        /// <summary>Put the user in the stage's tool. A missing/unusable type is reported in
        /// the stage panel instead of the console: a stage whose tool ships in a later
        /// milestone must stay browsable.</summary>
        private static void ActivateStageTool(FlowStage stage, List<string> notes)
        {
            if (stage == null || string.IsNullOrEmpty(stage.toolTypeName))
                return;

            var type = ResolveToolType(stage.toolTypeName);
            if (type == null)
            {
                notes.Add($"Tool type '{stage.toolTypeName}' was not found — no tool activated.");
                return;
            }

            if (!typeof(EditorTool).IsAssignableFrom(type))
            {
                notes.Add($"Type '{stage.toolTypeName}' is not an EditorTool — no tool activated.");
                return;
            }

            ToolManager.SetActiveTool(type);
            notes.Add($"Activated the {ObjectNames.NicifyVariableName(type.Name)}.");
        }

        /// <summary>Type.GetType first (the common case: a tool in this editor assembly, or an
        /// assembly-qualified name), then a TypeCache sweep so a flow asset can name a tool in
        /// another editor assembly with its plain full name.</summary>
        private static Type ResolveToolType(string toolTypeName)
        {
            var direct = Type.GetType(toolTypeName, false);
            if (direct != null)
                return direct;

            foreach (var candidate in TypeCache.GetTypesDerivedFrom(typeof(EditorTool)))
            {
                if (string.Equals(candidate.FullName, toolTypeName, StringComparison.Ordinal) ||
                    string.Equals(candidate.Name, toolTypeName, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        private void OnEditorStateChanged()
        {
            RefreshBadges();
        }

        // --- rendering --------------------------------------------------------------------

        /// <summary>Re-run every validator and repaint the tab badges + active tab highlight.</summary>
        private void RefreshBadges()
        {
            if (m_TabStrip == null)
                return;

            for (var i = 0; i < m_Tabs.Count; ++i)
            {
                var tab = m_Tabs[i];
                var status = Evaluate(tab.stage);

                tab.badge.style.backgroundColor = BadgeColor(status);
                tab.root.tooltip = $"{StatusText(status)} — {DescribeStage(tab.stage)}";

                var active = i == m_ActiveStageIndex;
                tab.root.style.backgroundColor = active ? k_TabActive : k_TabIdle;
                tab.root.style.borderBottomWidth = active ? 2f : 0f;
                tab.root.style.borderBottomColor = k_TabActiveEdge;
            }
        }

        private void RebuildStagePanel(List<string> notes = null)
        {
            if (m_StagePanel == null)
                return;

            m_StagePanel.Clear();

            if (m_Definition == null)
            {
                m_StagePanel.Add(BuildBody(
                    "No flow loaded. Assign a FlowDefinition above, or create the built-in Terrain flow " +
                    "(Sculpt / Collision)."));

                var create = new Button(() =>
                {
                    var asset = TerrainFlowAsset.CreateOrLoad();
                    if (asset == null)
                        return;

                    m_Definition = asset;
                    m_ActiveStageIndex = 0;
                    if (m_DefinitionField != null)
                        m_DefinitionField.value = asset;
                    RebuildTabs();
                })
                { text = "Create Terrain Flow" };
                create.style.alignSelf = Align.FlexStart;
                create.style.marginTop = 6f;
                m_StagePanel.Add(create);
                return;
            }

            var stage = m_Definition.GetStage(m_ActiveStageIndex);
            if (stage == null)
            {
                m_StagePanel.Add(BuildBody($"'{m_Definition.flowName}' has no stages yet."));
                return;
            }

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var title = new Label(string.IsNullOrEmpty(stage.title) ? stage.id : stage.title);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14f;
            titleRow.Add(title);

            var status = Evaluate(stage);
            var statusLabel = new Label(StatusText(status));
            statusLabel.style.marginLeft = 8f;
            statusLabel.style.color = BadgeColor(status);
            titleRow.Add(statusLabel);
            m_StagePanel.Add(titleRow);

            if (!string.IsNullOrEmpty(stage.description))
                m_StagePanel.Add(BuildBody(stage.description));

            if (stage.checklist != null && stage.checklist.Count > 0)
            {
                var heading = new Label("Checklist");
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                heading.style.marginTop = 8f;
                m_StagePanel.Add(heading);

                for (var i = 0; i < stage.checklist.Count; ++i)
                {
                    var line = stage.checklist[i];
                    if (string.IsNullOrEmpty(line))
                        continue;
                    m_StagePanel.Add(BuildChecklistRow(line));
                }
            }

            if (notes == null || notes.Count == 0)
                return;

            for (var i = 0; i < notes.Count; ++i)
            {
                var note = BuildBody(notes[i]);
                note.style.marginTop = i == 0 ? 10f : 2f;
                note.style.opacity = 0.75f;
                m_StagePanel.Add(note);
            }
        }

        private static VisualElement BuildChecklistRow(string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 3f;

            var bullet = new Label("•");
            bullet.style.marginRight = 6f;
            row.Add(bullet);

            var body = new Label(text);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.flexShrink = 1f;
            row.Add(body);

            return row;
        }

        private static Label BuildBody(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 4f;
            return label;
        }

        private static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        // --- status -----------------------------------------------------------------------

        /// <summary>Null means "no validator registered for this stage id" — a neutral badge,
        /// never a guess.</summary>
        private static StageStatus? Evaluate(FlowStage stage)
        {
            if (stage == null)
                return null;
            return FlowValidators.TryEvaluate(stage.id, out var status) ? status : (StageStatus?)null;
        }

        private static Color BadgeColor(StageStatus? status)
        {
            if (!status.HasValue)
                return k_BadgeUnknown;

            switch (status.Value)
            {
                case StageStatus.InProgress:
                    return k_BadgeInProgress;
                case StageStatus.Complete:
                    return k_BadgeComplete;
                case StageStatus.Invalidated:
                    return k_BadgeInvalidated;
                default:
                    return k_BadgeEmpty;
            }
        }

        private static string StatusText(StageStatus? status)
        {
            if (!status.HasValue)
                return "No validator";

            switch (status.Value)
            {
                case StageStatus.InProgress:
                    return "In progress";
                case StageStatus.Complete:
                    return "Complete";
                case StageStatus.Invalidated:
                    return "Needs review";
                default:
                    return "Not started";
            }
        }

        private static string DescribeStage(FlowStage stage)
        {
            if (stage == null)
                return string.Empty;
            return string.IsNullOrEmpty(stage.description) ? stage.id : stage.description;
        }

        /// <summary>Live tab widgets, rebuilt whenever the definition changes.</summary>
        private sealed class StageTab
        {
            public FlowStage stage;
            public VisualElement root;
            public VisualElement badge;
        }
    }
}
