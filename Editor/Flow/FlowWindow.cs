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
                // Also forget what the window learned about the graph frontend: the usual reason
                // to press Refresh after seeing "the frontend is not loaded" is that you have
                // just resolved the package, and re-opening the window to clear a cache would be
                // a silly thing to have to know.
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

            // The two graph stages (§6.1 Behavior for a player, §6.2 AI for an enemy) are the
            // same editor with different palettes, so they open the same way: the tab is a
            // shortcut into the graph window exactly as the Draw tab is a shortcut into the Draw
            // tool. Failures are notes in the stage panel, never console errors — an entity that
            // has no graph (or a project where the experimental package has not resolved) must
            // stay browsable.
            if (string.Equals(stage.id, FlowValidators.CharacterBehaviorStageId, StringComparison.Ordinal) ||
                string.Equals(stage.id, FlowValidators.EnemyAIStageId, StringComparison.Ordinal))
            {
                OpenGraphForSelection(notes);
            }

            RebuildStagePanel(notes);
            RefreshBadges();
        }

        /// <summary>Open the selected entity's graph asset, or offer to create one.
        ///
        /// Open path: the graph is a normal asset, so AssetDatabase.OpenAsset hands it to the
        /// window its importer registered. Create path:
        /// <c>GraphDatabase.PromptInProjectBrowserToCreateNewAsset&lt;T&gt;</c> — "Creates a new
        /// graph asset and activates the naming field in the Project Browser" (GraphDatabase.cs).
        /// Naming it is half of deciding what it is for, so the prompt is the right creation
        /// gesture here even though it means the asset lands wherever the Project window is
        /// pointed rather than in the conventional folder.
        ///
        /// Both calls go through <see cref="StateTreeGraphBridge"/> rather than being typed
        /// against Graph Toolkit: this window must keep working when the experimental package
        /// does not (§7.3, §8 Risks).</summary>
        private static void OpenGraphForSelection(List<string> notes)
        {
            if (!StateTreeGraphBridge.TryResolve(out var error))
            {
                notes.Add(error);
                notes.Add("You can still author the tree by hand: build a StateTreeAsset in the " +
                          "Inspector (or start from a preset) and drop it on the runner. The " +
                          "runner cannot tell the difference — that is what the frontend/model " +
                          "boundary buys.");
                return;
            }

            var entityName = ResolveEntityName();
            var path = StateTreeGraphBridge.GraphAssetPath(entityName);

            if (StateTreeGraphBridge.OpenGraphAsset(path))
            {
                notes.Add($"Opened {path}.");
                notes.Add("Remember to bake after editing — the runner reads the baked asset, " +
                          "never the graph.");
                return;
            }

            if (!StateTreeGraphBridge.PromptCreate(entityName, out var createError))
            {
                notes.Add(createError);
                return;
            }

            notes.Add($"No graph for '{entityName}' at {path} yet — a new one is waiting to be " +
                      "named in the Project Browser (it is created where the Project window is " +
                      $"pointed; move it to {StateTreeGraphBridge.GraphFolder} to have this tab " +
                      "find it next time).");
        }

        /// <summary>Which entity the graph belongs to. The runner's GameObject when the selection
        /// is anywhere inside a rigged entity — clicking a limb should open the character's
        /// brain, not look for a graph called "LeftForearm" — else the selection itself.</summary>
        private static string ResolveEntityName()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
                return "New State Tree";

            var runner = selected.GetComponentInParent<StateTreeRunner>();
            return runner != null ? runner.gameObject.name : selected.name;
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
