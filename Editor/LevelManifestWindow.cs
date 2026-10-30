using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE LEVEL EDITOR for a manifest-driven level: open an area's scene and this window
    /// shows that level's object rows — place, remove, configure, and DRAG THEM IN THE SCENE.
    ///
    /// The scene holds only the arena, so there is nothing to select in it; the placements
    /// live in the level's <see cref="LevelObjectRegistry"/>. This panel is where they are
    /// authored: the open scene picks the level (its <see cref="LevelContent.scenePath"/>
    /// matches). On the side, a palette of the kinds the level's kind registry offers: drag a
    /// kind into the Scene view to place one where it lands, onto the list (or click it) to place
    /// one at the view's centre. The list groups the rows by kind, each group folding; the
    /// selected row draws through the normal property drawers — so kind, tree and parameters are
    /// the same fields the dashboard shows, with no duplicate UI to keep in step.
    ///
    /// A dockable WINDOW, not a scene overlay: a selected row's full editor (tree, parameters,
    /// tags) is taller than a scene view can host, and any window can draw handles through
    /// <see cref="SceneView.duringSceneGui"/> — drag to move a row, click to select it.
    /// </summary>
    internal sealed class LevelManifestWindow : EditorWindow
    {
        [MenuItem("Tools/Draw To Play/Level Manifest")]
        public static void Open()
        {
            // Beside the Inspector on FIRST open — the place the user said this belongs —
            // and wherever they drag it after that: dock position is theirs, this is only
            // the default. The type is internal, so a missing resolve degrades to floating.
            Type inspector = Type.GetType("UnityEditor.InspectorWindow,UnityEditor");
            LevelManifestWindow window = inspector != null
                ? GetWindow<LevelManifestWindow>(inspector)
                : GetWindow<LevelManifestWindow>();
            window.titleContent = new GUIContent("Level Manifest");
            window.Show();
        }

        /// <summary>Radius of a row's scene handle, in SCREEN pixels — placements have no
        /// size of their own until they spawn, so the handle is a constant target at any
        /// zoom.</summary>
        private const float k_HandlePixels = 9f;

        private VisualElement m_Root;
        private ScrollView m_Scroll;
        private LevelKindPalette m_Palette;
        /// <summary>Kept over a domain reload with <see cref="m_Selected"/>, which is this level's row; checked against
        /// the open scene before every use (<see cref="ResolveLevel"/>).</summary>
        [SerializeField] private LevelContent m_Level;
        private SerializedObject m_Objects;

        /// <summary>Each group's fold and its rows' lines, for the search to hide rows without a rebuild.</summary>
        private readonly List<(Foldout fold, string kind, List<(VisualElement line, int index)> rows)> m_Groups =
            new List<(Foldout, string, List<(VisualElement, int)>)>();

        /// <summary>The selected row's line in the list — renamed live while its name is typed.</summary>
        private Button m_SelectedLine;

        /// <summary>Set when a row was just placed: the next panel focuses its name.</summary>
        private bool m_FocusName;

        /// <summary>Where a kind dragged over the Scene view would land, while it is dragged.</summary>
        private Vector3? m_DropPreview;
        private string m_DropKind;

        // SERIALIZED, because an EditorWindow is serialize/deserialized when it is DOCKED and
        // on every domain reload — a plain field comes back as its C# default, which is how
        // the selection silently jumped from the row being edited to row zero the moment the
        // window was dragged into a layout.
        [SerializeField] private int m_Selected = -1;
        [SerializeField] private string m_RowFilter = "";

        /// <summary>Runs again after every domain reload, which is exactly what these
        /// subscriptions need — the overlay's OnCreated only ran once per overlay life.</summary>
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorSceneManagerHooks();
            Undo.undoRedoPerformed += OnUndoRedo;
            m_ResolvedFor = null;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.projectChanged -= OnProjectChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
            UnityEditor.SceneManagement.EditorSceneManager.newSceneCreated -= OnNewScene;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            m_Links = null;
            Rebuild();
        }

        private void EditorSceneManagerHooks()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
            // New level builds its scene with NewScene and saves it under its path: no sceneOpened is raised for it.
            UnityEditor.SceneManagement.EditorSceneManager.newSceneCreated += OnNewScene;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        private void OnSceneOpened(Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode) => Follow();

        private void OnNewScene(Scene scene, UnityEditor.SceneManagement.NewSceneSetup setup,
            UnityEditor.SceneManagement.NewSceneMode mode) => Follow();

        private void OnSceneSaved(Scene scene) => Follow();

        private void OnActiveSceneChanged(Scene from, Scene to) => Follow();

        /// <summary>Redraws when the open scene now belongs to another level, or to none.</summary>
        private void Follow()
        {
            if (ResolveLevel())
                Rebuild();
        }

        /// <summary>The kinds' palette on the left, a splitter, and the rows in one SCROLL VIEW on the
        /// right — a fixed panel would cut the selected row's editor off.</summary>
        private void CreateGUI()
        {
            var split = new TwoPaneSplitView(0, 132f, TwoPaneSplitViewOrientation.Horizontal)
            {
                viewDataKey = "PowerOfFire.DrawToPlay.LevelManifest.Split"
            };
            rootVisualElement.Add(split);

            var paletteScroll = new ScrollView(ScrollViewMode.Vertical);
            paletteScroll.style.minWidth = 72f;
            m_Palette = new LevelKindPalette((kind, preset) => PlaceAtCentre(kind, preset));
            paletteScroll.Add(m_Palette);
            split.Add(paletteScroll);

            m_Scroll = new ScrollView(ScrollViewMode.Vertical);
            m_Scroll.style.flexGrow = 1f;
            m_Scroll.style.minWidth = 160f;
            // A kind dropped anywhere on the list side is placed at the Scene view's centre.
            m_Scroll.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                if (LevelKindPalette.DraggedKind() != null && m_Level != null && m_Level.objects != null)
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            });
            m_Scroll.RegisterCallback<DragPerformEvent>(_ =>
            {
                string kind = LevelKindPalette.DraggedKind();
                if (kind == null || m_Level == null || m_Level.objects == null)
                    return;
                DragAndDrop.AcceptDrag();
                PlaceAtCentre(kind, LevelKindPalette.DraggedPreset());
            });
            split.Add(m_Scroll);

            m_Root = new VisualElement();
            m_Root.style.paddingLeft = 6f;
            m_Root.style.paddingRight = 6f;
            m_Root.style.paddingTop = 6f;
            m_Root.style.paddingBottom = 6f;
            m_Scroll.Add(m_Root);
            Rebuild();
        }

        /// <summary>Set by <see cref="Edit"/>: the level is the one handed in, never the open scene's.</summary>
        private bool m_Pinned;

        /// <summary>The open scene's path, as the window sees it; a test points it at a scene path of its own.</summary>
        internal Func<string> openScene = () => SceneManager.GetActiveScene().path;

        /// <summary>The level the window edits, resolved from the open scene first.</summary>
        internal LevelContent level
        {
            get
            {
                ResolveLevel();
                return m_Level;
            }
        }

        /// <summary>For tests: follow <paramref name="scene"/> as the open scene's path, as the window does the real one.</summary>
        internal void Follow(Func<string> scene)
        {
            openScene = scene;
            m_Pinned = false;
            m_ResolvedFor = null;
            if (m_Root == null)
                CreateGUI();
            else
                Rebuild();
        }

        /// <summary>For tests: edit <paramref name="level"/> (none: null) with <paramref name="kinds"/>, without a scene.</summary>
        internal void Edit(LevelContent level, LevelObjectKindRegistry kinds, string levelKey)
        {
            m_Level = level;
            m_Kinds = kinds;
            m_KindsSearched = true;
            m_LevelKey = levelKey;
            m_Links = null;
            m_Pinned = true;
            if (m_Root == null)
                CreateGUI();
            else
                Rebuild();
        }

        internal int selected => m_Selected;

        public const string NewLevelClass = "dtp-manifest-new-level";
        public const string LinkClass = "dtp-manifest-link";
        internal LevelKindPalette palette => m_Palette;

        // ---- panel -----------------------------------------------------------------------

        private void Rebuild()
        {
            if (m_Root == null)
                return;
            m_Root.Clear();
            m_Groups.Clear();
            m_SelectedLine = null;

            ResolveLevel();
            m_Palette?.Show(m_Level != null && m_Level.objects != null ? Kinds() : null, Presets());
            if (m_Level == null)
            {
                var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart } };
                Label hint = Hint("No level owns the open scene. Open an area scene — the level "
                    + "whose Scene Path matches is edited here.");
                hint.style.flexGrow = 1f;
                hint.style.flexShrink = 1f;
                top.Add(hint);
                top.Add(EditorGuides.HelpButton(false));
                m_Root.Add(top);
                if (LevelSetups.current != null)
                {
                    var create = new Button(() => LevelSetups.Prompt()) { text = "+ New level…" };
                    create.AddToClassList(NewLevelClass);
                    create.style.alignSelf = Align.FlexStart;
                    create.tooltip = "Makes a level — its scene and data from the game's template — and opens it.";
                    m_Root.Add(create);
                }
                return;
            }

            VisualElement header = StateTreeInspectorLook.Header("▦", new Color(0.45f, 0.8f, 1f),
                m_Level.displayName.Length > 0
                    ? m_Level.displayName + "  (" + m_Level.name + ")"
                    : m_Level.name, out _);
            header.Add(EditorGuides.HelpButton(false));
            m_Root.Add(header);
            m_Root.Add(BuildLinks());

            if (m_Level.objects == null)
            {
                m_Root.Add(Hint("This level has no object registry yet."));
                m_Root.Add(new Button(CreateObjectsRegistry) { text = "Create objects registry" });
                return;
            }

            m_Objects = new SerializedObject(m_Level.objects);
            m_Root.Add(BuildStyleRow());
            m_Root.Add(BuildList());
            m_Root.Add(BuildSelected());
            m_Root.Add(BuildSaveRow());
            SceneView.RepaintAll();
        }

        /// <summary>The level's connections, as the game's setup sees them: its Level node in the episode (◎ shows it in
        /// the State Tree window) and the tree it runs (◎ selects it, ▶ opens it) — or what is missing, which the first
        /// object placed makes. Nothing when no setup is registered.</summary>
        private VisualElement BuildLinks()
        {
            var block = new VisualElement { style = { marginTop = 2f, marginBottom = 4f } };
            if (LevelSetups.current == null)
                return block;
            m_Links ??= LevelLinks.Of(m_Level);
            LevelLinks links = m_Links;

            if (!links.inEpisode)
                block.Add(LinkRow("Episode", "— in no episode", null, null));
            else if (links.episodeNode != null)
            {
                StateTreeAsset episode = links.episode;
                StateTreeNodeAsset node = links.episodeNode;
                string shown = (string.IsNullOrEmpty(node.displayName) ? node.nodeId : node.displayName)
                    + "  (" + episode.name + ")";
                block.Add(LinkRow("Episode", shown,
                    () => StateTreeEditorWindow.ShowAdded(episode, node), null));
            }
            else
                block.Add(LinkRow("Episode", "missing — made with the next object placed, or now", null, null,
                    MakeButton("+ Add to episode", "Adds its Level node, with one zone, under " + links.episode.name
                        + " now — and its tree when that is missing too. One undo step.")));

            if (links.session != null)
            {
                StateTreeAsset session = links.session;
                block.Add(LinkRow("Session", session.name,
                    () =>
                    {
                        Selection.activeObject = session;
                        EditorGUIUtility.PingObject(session);
                    },
                    () => StateTreeEditorWindow.Open(session)));
            }
            else if (links.sessionMade != null)
            {
                // Made, but the scene does not run it — its wire went with a scene closed unsaved.
                StateTreeAsset made = links.sessionMade;
                block.Add(LinkRow("Session", "not run by its scene: " + made.name, null, () => StateTreeEditorWindow.Open(made),
                    links.inEpisode
                        ? MakeButton("+ Run it in the scene", "Wires " + made.name + " into the level's scene now (save the "
                            + "scene after) — and its Level node in " + links.episode.name + " when that is missing too. "
                            + "Nothing new is made.")
                        : null));
            }
            else
                block.Add(LinkRow("Session", links.inEpisode ? "missing — made with the next object placed, or now" : "missing",
                    null, null, links.inEpisode
                        ? MakeButton("+ Create session tree", "Makes the level's own tree and wires it into its scene now "
                            + "— and its Level node in " + links.episode.name + " when that is missing too. One undo step.")
                        : null));
            return block;
        }

        /// <summary>What the first object placed would make, made now; null when the level has no object registry to
        /// connect.</summary>
        private Button MakeButton(string text, string tooltip)
        {
            if (m_Level == null || m_Level.objects == null)
                return null;
            var make = new Button(() => Connect()) { text = text, tooltip = tooltip };
            make.AddToClassList(ConnectClass);
            return make;
        }

        public const string ConnectClass = "dtp-manifest-connect";

        /// <summary>The level's missing episode entry and tree, made now — the first object's connect, in one undo
        /// step; nothing when the level is in no episode or has both. The lines say what was made.</summary>
        internal List<string> Connect()
        {
            Follow();
            if (m_Level == null)
                return new List<string>();
            int group = StateTreeEditorOps.BeginUndoGroup("Connect " + m_Level.name);
            List<string> made = LevelSetups.ConnectIfMissing(m_Level);
            StateTreeEditorOps.EndUndoGroup(group);
            m_Links = null;
            if (made.Count > 0)
                Debug.Log("[Level Manifest] " + m_Level.name + ": " + string.Join("; ", made));
            Rebuild();
            return made;
        }

        private static VisualElement LinkRow(string label, string text, Action select, Action open, Button make = null)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            row.AddToClassList(LinkClass);
            var name = new Label(label) { style = { width = 64f, opacity = 0.7f } };
            row.Add(name);
            var value = new Label(text) { name = "value" };
            value.style.flexGrow = 1f;
            value.style.whiteSpace = WhiteSpace.Normal;
            if (select == null && (text.StartsWith("missing", StringComparison.Ordinal)
                    || text.StartsWith("not run", StringComparison.Ordinal)))
                value.style.color = new Color(1f, 0.6f, 0.4f);
            row.Add(value);
            if (select != null)
                row.Add(new Button(select) { text = "◎", tooltip = "Show it.", style = { width = 22f } });
            if (open != null)
                row.Add(new Button(open) { text = "▶", tooltip = "Open it in the State Tree window.", style = { width = 22f } });
            if (make != null)
                row.Add(make);
            return row;
        }

        /// <summary>The level's connections, resolved once per level and again after a scene, project or undo change.</summary>
        private LevelLinks m_Links;

        internal LevelLinks links => m_Links;

        /// <summary>
        /// CLEARING SAVED PROGRESS, from where the level is being edited.
        ///
        /// WHY IT BELONGS HERE. A manifest is what a level was authored as; a save is what has
        /// happened to it since, and while you are building a level the two disagree constantly —
        /// you kill the raider once to see the drop, and now the raider is not there to test
        /// anything else with. Hunting down a JSON file in a platform-specific folder is the
        /// alternative, and it is the reason people end up deleting the wrong folder.
        ///
        /// THREE BUTTONS, because there are three different things you want. THIS LEVEL is the
        /// common one: put the fight back without losing the items and conversations that got you
        /// there. SHARED is the opposite test — walk into a finished level with empty pockets.
        /// ALL is starting over.
        ///
        /// This window knows nothing about saves. It finds implementations of
        /// <see cref="ILevelProgressStore"/> by type and asks them; a project with no save has no
        /// row here, and a project with two gets two.
        /// </summary>
        private VisualElement BuildSaveRow()
        {
            var container = new VisualElement { style = { marginTop = 8f } };
            IReadOnlyList<ILevelProgressStore> stores = Stores();
            if (stores.Count == 0)
                return container;

            var any = false;
            for (int i = 0; i < stores.Count; i++)
            {
                ILevelProgressStore store = stores[i];
                bool has;
                try
                {
                    has = store.hasSave;
                }
                catch (Exception)
                {
                    continue;   // a store that cannot answer is a store with nothing to offer
                }
                if (!has)
                    continue;

                any = true;
                container.Add(new Label(store.displayName)
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4f }
                });

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(SaveButton("This level", store,
                    "Forget what happened in THIS level — its dead, its taken pickups — and leave "
                        + "the rest of the session alone.",
                    () => store.ClearLevel(PlacementIds(), m_Level.name)));
                row.Add(SaveButton("Shared", store,
                    "Forget what is carried and what has been said, and leave every level's own "
                        + "state alone.",
                    store.ClearShared));
                row.Add(SaveButton("All", store,
                    "Throw the whole save away and start over.",
                    store.ClearAll));
                container.Add(row);
            }

            if (!any)
                container.Add(Hint("No saved progress yet."));
            return container;
        }

        /// <summary>One clear button, with the confirmation a destructive edit outside the undo
        /// system has to have — nothing here can be taken back with Ctrl+Z.</summary>
        private Button SaveButton(string label, ILevelProgressStore store, string tooltip,
            Action clear)
        {
            var button = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Clear saved progress",
                    tooltip + "\n\n" + store.displayName + " — this cannot be undone.",
                    "Clear " + label, "Cancel"))
                    return;
                clear();
                Rebuild();
            })
            { text = label, tooltip = tooltip };
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            return button;
        }

        /// <summary>The ids of this level's rows — what "this level's saved state" MEANS to a
        /// save keyed on placements.</summary>
        private List<string> PlacementIds()
        {
            var ids = new List<string>();
            if (m_Level == null || m_Level.objects == null)
                return ids;
            for (int i = 0; i < m_Level.objects.entries.Count; i++)
            {
                LevelObjectDef row = m_Level.objects.entries[i];
                if (row != null && !string.IsNullOrEmpty(row.id))
                    ids.Add(row.id);
            }
            return ids;
        }

        /// <summary>Every save this project has, built fresh each time the panel is — a store is
        /// a handle to a file, not state, and holding one across a domain reload buys nothing.</summary>
        private static IReadOnlyList<ILevelProgressStore> Stores()
        {
            var stores = new List<ILevelProgressStore>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ILevelProgressStore>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                try
                {
                    stores.Add((ILevelProgressStore)Activator.CreateInstance(type));
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[Level Manifest] could not create " + type.Name
                        + " to offer its save: " + error.Message);
                }
            }
            return stores;
        }

        /// <summary>
        /// How the ghosts are drawn — see <see cref="LevelGhostStyle"/>.
        ///
        /// A per-USER setting, in EditorPrefs rather than on the level: it changes nothing about
        /// the level and two people looking at the same one are allowed to want different pictures.
        /// It is remembered because it is a working preference, and re-picking it every time the
        /// scene opened would make it not worth having.
        /// </summary>
        private VisualElement BuildStyleRow()
        {
            var field = new EnumField("Ghosts", style);
            field.tooltip = "Mesh — the prefab's real geometry at 1:1, rigged characters posed.\n"
                + "Capsule — one shape per placement, coloured by kind, readable at any zoom.";
            field.RegisterValueChangedCallback(changed =>
            {
                style = (LevelGhostStyle)changed.newValue;
                SceneView.RepaintAll();
            });

            // A prefab edited while this window is open keeps showing its old shape, because the
            // posed meshes are cached. This is the way back, and it is next to the toggle because
            // that is where someone looking at a wrong-shaped ghost will look.
            var refresh = new Button(() =>
            {
                LevelGhostMeshes.Clear();
                SceneView.RepaintAll();
            })
            { text = "↻" };
            refresh.tooltip = "Rebuild the ghost meshes — after editing one of the prefabs.";
            refresh.style.flexShrink = 0f;
            refresh.style.width = 24f;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            field.style.flexGrow = 1f;
            row.Add(field);
            row.Add(refresh);
            return row;
        }

        /// <summary>The chosen ghost shape, remembered per user across sessions and
        /// cached — this is read per row per repaint, and EditorPrefs is a disk hit.</summary>
        private static LevelGhostStyle style
        {
            get
            {
                s_Style ??= (LevelGhostStyle)EditorPrefs.GetInt(
                    k_StyleKey, (int)LevelGhostStyle.Mesh);
                return s_Style.Value;
            }
            set
            {
                s_Style = value;
                EditorPrefs.SetInt(k_StyleKey, (int)value);
            }
        }

        private static LevelGhostStyle? s_Style;

        private const string k_StyleKey = "PowerOfFire.DrawToPlay.LevelManifest.GhostStyle";

        /// <summary>
        /// The rows, grouped by kind: one fold per kind (its icon, name and count), the rows inside by
        /// name. A fold remembers whether it was open; the selected row's group always opens. The
        /// search hides rows in place, so typing in it never rebuilds the panel.
        /// </summary>
        private VisualElement BuildList()
        {
            var list = new VisualElement { style = { marginTop = 6f, marginBottom = 6f } };
            list.Add(StateTreeInspectorLook.Section("Placements"));
            List<LevelObjectDef> rows = m_Level.objects.entries;
            if (rows.Count == 0)
            {
                list.Add(Hint("No placements yet. Drag a kind from the left into the Scene view, "
                    + "or click it to place one at the view's centre."));
                return list;
            }

            var search = new ToolbarSearchField { value = m_RowFilter ?? "" };
            search.style.width = StyleKeyword.Auto;
            search.style.marginLeft = 0f;
            search.style.marginRight = 0f;
            search.tooltip = "Show only the rows whose name, id, kind or entry contains this.";
            search.RegisterValueChangedCallback(changed =>
            {
                m_RowFilter = changed.newValue;
                ApplyFilter();
            });
            list.Add(search);

            LevelObjectKindRegistry kinds = Kinds();
            foreach (LevelManifestEdits.KindGroup group in LevelManifestEdits.GroupByKind(rows, kinds))
            {
                string kind = group.kind;
                bool holdsSelected = group.rows.Contains(m_Selected);
                if (holdsSelected)
                    SetFolded(kind, false);

                var fold = new Foldout { value = !IsFolded(kind) };
                fold.AddToClassList(GroupClass);
                fold.userData = kind;
                fold.style.marginTop = 2f;
                fold.RegisterValueChangedCallback(changed =>
                {
                    if (changed.target == fold && string.IsNullOrWhiteSpace(m_RowFilter))
                        SetFolded(kind, !changed.newValue);
                });
                DressGroupHeader(fold, kinds, kind, group.rows.Count);

                var lines = new List<(VisualElement, int)>();
                foreach (int index in group.rows)
                {
                    LevelObjectDef def = rows[index];
                    var line = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    var select = new Button(() => Select(index))
                    {
                        text = string.IsNullOrEmpty(def.name) ? "(unnamed)" : def.name,
                        tooltip = def.id + (string.IsNullOrEmpty(def.entryName) ? "" : "\n" + def.entryName)
                    };
                    select.style.flexGrow = 1f;
                    select.style.flexShrink = 1f;
                    select.style.unityTextAlign = TextAnchor.MiddleLeft;
                    LevelObjectPresetDef linked = m_Level.objects.PresetOf(def);
                    if (linked != null)
                        select.tooltip += "\nMade from the preset " + linked.name;
                    select.tooltip += "\nDrag onto a mission tree's zone or step to use it there.";
                    DragsOut(select, def);
                    if (index == m_Selected)
                    {
                        select.style.unityFontStyleAndWeight = FontStyle.Bold;
                        m_SelectedLine = select;
                    }
                    line.Add(select);

                    var remove = new Button(() => RemoveRow(index)) { text = "✕", tooltip = "Remove this placement." };
                    remove.style.width = 22f;
                    remove.style.flexShrink = 0f;
                    line.Add(remove);
                    fold.Add(line);
                    lines.Add((line, index));
                }
                m_Groups.Add((fold, kind, lines));
                list.Add(fold);
            }
            ApplyFilter();

            if (m_SelectedLine != null)
            {
                Button shown = m_SelectedLine;
                shown.schedule.Execute(() =>
                {
                    if (shown.panel != null && m_Scroll != null)
                        m_Scroll.ScrollTo(shown);
                });
            }
            return list;
        }

        public const string GroupClass = "dtp-manifest-group";

        /// <summary>The fold's own line: the kind's icon, its name, and how many rows it holds.</summary>
        private static void DressGroupHeader(Foldout fold, LevelObjectKindRegistry kinds, string kind, int count)
        {
            fold.text = kind + "  (" + count + ")";
            VisualElement input = fold.Q(className: Foldout.inputUssClassName);
            if (input == null)
                return;
            LevelObjectKindDef def = kinds != null ? kinds.FindByName(kind) as LevelObjectKindDef : null;
            VisualElement icon = LevelKindPalette.Icon(def, kind, LevelManifestEdits.KindColour(kinds, kind), 16f);
            icon.style.marginRight = 4f;
            input.Insert(Mathf.Min(1, input.childCount), icon);
        }

        /// <summary>Hides the rows the search leaves out, and the groups left empty; while searching,
        /// every group with a match shows open (its remembered fold is left alone).</summary>
        private void ApplyFilter()
        {
            if (m_Level == null || m_Level.objects == null)
                return;
            List<LevelObjectDef> rows = m_Level.objects.entries;
            bool searching = !string.IsNullOrWhiteSpace(m_RowFilter);
            foreach ((Foldout fold, string kind, List<(VisualElement line, int index)> lines) in m_Groups)
            {
                var shown = 0;
                foreach ((VisualElement line, int index) in lines)
                {
                    bool match = index < rows.Count && rows[index] != null
                        && LevelManifestEdits.Matches(rows[index], m_RowFilter);
                    line.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                    if (match)
                        shown++;
                }
                fold.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                fold.SetValueWithoutNotify(searching ? shown > 0 : !IsFolded(kind));
            }
        }

        /// <summary>Whether a kind's group is folded shut in this level — per user, in EditorPrefs,
        /// read once and cached (a rebuild asks per group).</summary>
        private bool IsFolded(string kind)
        {
            string key = FoldKey(kind);
            if (!s_Folded.TryGetValue(key, out bool folded))
                s_Folded[key] = folded = EditorPrefs.GetBool(key, false);
            return folded;
        }

        private void SetFolded(string kind, bool folded)
        {
            string key = FoldKey(kind);
            if (s_Folded.TryGetValue(key, out bool was) && was == folded)
                return;
            s_Folded[key] = folded;
            if (folded)
                EditorPrefs.SetBool(key, true);
            else
                EditorPrefs.DeleteKey(key);
        }

        private string FoldKey(string kind)
        {
            return FoldKey(m_Level != null ? m_Level.name : "", kind);
        }

        /// <summary>The EditorPrefs key a level's kind group keeps its fold under.</summary>
        internal static string FoldKey(string level, string kind)
        {
            return k_FoldKey + level + "/" + kind;
        }

        private static readonly Dictionary<string, bool> s_Folded = new Dictionary<string, bool>();

        private const string k_FoldKey = "PowerOfFire.DrawToPlay.LevelManifest.Folded.";

        /// <summary>The selected row, drawn through the normal drawers — the kind and entry
        /// dropdowns come free, so this panel never duplicates them.</summary>
        private VisualElement BuildSelected()
        {
            List<LevelObjectDef> rows = m_Level.objects.entries;
            if (m_Selected < 0 || m_Selected >= rows.Count || rows[m_Selected] == null)
            {
                var none = new VisualElement();
                none.Add(Hint("Select a placement to configure it."));
                return none;
            }

            string kindName = rows[m_Selected].kind.entryName;
            Color accent = LevelManifestEdits.KindColour(Kinds(), kindName);
            VisualElement box = StateTreeInspectorLook.Card(StateTreeInspectorLook.CardBackground, accent);
            box.Add(StateTreeInspectorLook.CardHeader("●", accent,
                string.IsNullOrEmpty(kindName) ? "Placement" : kindName, out _));

            SerializedProperty element = m_Objects.FindProperty("entries")
                .GetArrayElementAtIndex(m_Selected);
            // "attributes" is the DATA half of the row and "tree"/"parameters" the FLOW half —
            // what this one is worth, and the mind it runs with its arguments. Hiding either
            // here was how a placement could only be specialised in code. Both sections draw
            // themselves against whatever the row picks — one checkbox per declared knob, per
            // declared attribute — so reusing a def and specialising one are the same gesture.
            LevelObjectDef row = rows[m_Selected];
            LevelObjectPresetDef preset = m_Level.objects.PresetOf(row);
            string prefix = LevelPresetEdits.NamePrefix(LevelKey(), kindName);
            bool prefixed = !string.IsNullOrEmpty(kindName) && (row.name ?? "").StartsWith(prefix, StringComparison.Ordinal)
                && row.name.Length > prefix.Length;
            box.Add(BuildActions(row));
            if (prefixed)
                box.Add(BuildName(element, prefix));
            if (Presets() != null || preset != null)
                box.Add(Bound(element, "preset"));
            Foldout more = null;
            if (prefixed)
            {
                more = StateTreeInspectorLook.More(2, false, null);
                more.text = "More";
                more.tooltip = "The full name and the group.";
            }
            // A row made from a preset shows its core read-only, below: kind, tree, arguments and tags are the
            // preset's, and an edit here would be an ad-hoc change to one object.
            foreach (string field in preset != null
                ? new[] { "name", "group", "entry", "attributes", "position", "facing" }
                : new[] { "name", "group", "kind", "entry", "attributes", "tree", "parameters", "position", "facing", "tags" })
            {
                PropertyField propertyField = Bound(element, field);
                if (propertyField == null)
                    continue;
                if (more != null && (field == "name" || field == "group"))
                {
                    more.Add(propertyField);
                    continue;
                }
                box.Add(propertyField);
                if (field == "name" && m_FocusName)
                {
                    m_FocusName = false;
                    FocusNameWhenBound(propertyField);
                }
            }
            if (more != null)
                box.Add(more);

            if (preset != null)
                box.Add(BuildPreset(row, preset));

            SerializedProperty nameProperty = element.FindPropertyRelative("name");
            if (nameProperty != null)
            {
                box.TrackPropertyValue(nameProperty, changed =>
                {
                    if (m_SelectedLine != null)
                        m_SelectedLine.text = string.IsNullOrEmpty(changed.stringValue)
                            ? "(unnamed)"
                            : changed.stringValue;
                });
            }

            // The scene mirrors only what it draws — the handle and the ghost. Tracking
            // those few properties, rather than every change event the binder emits (list
            // rebinds emit them continuously), keeps an open panel from repainting the
            // scene per tick.
            foreach (string watched in new[] { "position", "facing", "kind", "name" })
            {
                SerializedProperty child = element.FindPropertyRelative(watched);
                if (child != null)
                    box.TrackPropertyValue(child, _ => SceneView.RepaintAll());
            }
            return box;
        }

        /// <summary>The name field's text, focused and selected once the binder has built it — a
        /// placed row is named next.</summary>
        private void FocusNameWhenBound(PropertyField field)
        {
            var tries = 0;
            var done = false;
            field.schedule.Execute(() =>
            {
                TextField text = field.Q<TextField>();
                if (text == null && ++tries < 20)
                    return;
                done = true;
                if (text == null)
                    return;
                Focus();
                text.Focus();
                text.SelectAll();
            }).Every(30).Until(() => done);
        }

        public const string NameClass = "dtp-manifest-name";
        public const string PresetClass = "dtp-manifest-preset";
        public const string ForkClass = "dtp-manifest-fork";

        private PropertyField Bound(SerializedProperty element, string field)
        {
            SerializedProperty child = element.FindPropertyRelative(field);
            if (child == null)
                return null;
            var propertyField = new PropertyField(child);
            propertyField.Bind(m_Objects);
            return propertyField;
        }

        /// <summary>The name as a designer types it: <c>level.kind.</c> fixed, only the last part typed. Emptied,
        /// it keeps the name it had (a fresh number when the row was placed).</summary>
        private VisualElement BuildName(SerializedProperty element, string prefix)
        {
            SerializedProperty name = element.FindPropertyRelative("name");
            var tail = new TextField("Name") { value = LevelPresetEdits.Tail(name.stringValue, prefix), isDelayed = true };
            tail.AddToClassList(NameClass);
            tail.tooltip = "The last part of the name; the start is the level's and the kind's. The full name is under More.";
            var fixedPart = new Label(prefix) { tooltip = tail.tooltip };
            fixedPart.style.opacity = 0.6f;
            fixedPart.style.flexShrink = 0f;
            fixedPart.style.unityTextAlign = TextAnchor.MiddleLeft;
            tail.Insert(Mathf.Min(1, tail.childCount), fixedPart);
            tail.RegisterValueChangedCallback(changed =>
                tail.SetValueWithoutNotify(LevelPresetEdits.Tail(Rename(name, prefix, changed.newValue), prefix)));
            tail.TrackPropertyValue(name, changed => tail.SetValueWithoutNotify(LevelPresetEdits.Tail(changed.stringValue, prefix)));
            if (m_FocusName)
            {
                m_FocusName = false;
                var done = false;
                tail.schedule.Execute(() =>
                {
                    if (tail.panel == null)
                        return;
                    done = true;
                    Focus();
                    tail.Focus();
                    tail.SelectAll();
                }).Every(30).Until(() => done);
            }
            return tail;
        }

        private string Rename(SerializedProperty name, string prefix, string typed)
        {
            string renamed = LevelPresetEdits.Renamed(name.stringValue, prefix, typed);
            if (renamed != name.stringValue)
            {
                m_Objects.Update();
                name.stringValue = renamed;
                m_Objects.ApplyModifiedProperties();
            }
            return renamed;
        }

        /// <summary>The selected row's name with <paramref name="typed"/> after its fixed prefix — what typing in
        /// the name field does.</summary>
        internal string RenameSelected(string typed)
        {
            LevelObjectDef row = m_Selected >= 0 && m_Selected < m_Level.objects.entries.Count ? m_Level.objects.entries[m_Selected] : null;
            if (row == null)
                return null;
            SerializedProperty name = m_Objects.FindProperty("entries").GetArrayElementAtIndex(m_Selected).FindPropertyRelative("name");
            return Rename(name, LevelPresetEdits.NamePrefix(LevelKey(), row.kind.entryName), typed);
        }

        /// <summary>What a selected row offers: a step about it in a mission, and a preset made from it.</summary>
        private VisualElement BuildActions(LevelObjectDef row)
        {
            var box = new VisualElement { style = { marginTop = 2f, marginBottom = 4f } };
            var line = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            box.Add(line);
            Button use = null;
            use = StateTreeInspectorLook.SmallButton("Use in mission…",
                "Add a step about this object to a zone of a mission tree under this level — the first step its preset fits.",
                () => UseInMission(row, use.worldBound));
            line.Add(use);
            if (Presets() == null)
                return box;

            var fork = new VisualElement { style = { flexDirection = FlexDirection.Row, display = DisplayStyle.None, marginTop = 2f } };
            fork.AddToClassList(ForkClass);
            LevelObjectPresetDef preset = m_Level.objects.PresetOf(row);
            var name = new TextField("New preset") { value = preset != null ? preset.name : row.kind.entryName };
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            fork.Add(name);
            fork.Add(StateTreeInspectorLook.SmallButton("Make", "Copy the preset with this object's settings as its "
                + "defaults, and switch this object to it.", () => MakePreset(row, name.value)));
            fork.Add(StateTreeInspectorLook.SmallButton("✕", "Cancel.", () => fork.style.display = DisplayStyle.None));
            name.RegisterCallback<KeyDownEvent>(key =>
            {
                if (key.keyCode == KeyCode.Return || key.keyCode == KeyCode.KeypadEnter)
                    MakePreset(row, name.value);
            });

            line.Add(StateTreeInspectorLook.SmallButton("New preset from this…", preset != null
                    ? "A copy of " + preset.name + " with this object's settings as its defaults; this object switches to it."
                    : "A preset from this object's kind, tree, arguments and tags; this object switches to it.",
                () =>
                {
                    fork.style.display = DisplayStyle.Flex;
                    name.Focus();
                    name.SelectAll();
                }));
            box.Add(fork);
            return box;
        }

        private void MakePreset(LevelObjectDef row, string name)
        {
            if (LevelPresetEdits.Fork(m_Level.objects, row, name) != null)
                Rebuild();
        }

        /// <summary>The preset's core, read-only (edited in the catalogue, where it reaches every object made
        /// from it), and its settings with this object's values — a reset puts one back on the preset's.</summary>
        private VisualElement BuildPreset(LevelObjectDef row, LevelObjectPresetDef preset)
        {
            var box = new VisualElement { style = { marginTop = 6f } };
            box.AddToClassList(PresetClass);
            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            Label title = StateTreeInspectorLook.Section("Preset: " + preset.name);
            title.style.flexGrow = 1f;
            head.Add(title);
            head.Add(StateTreeInspectorLook.SmallButton("Edit preset", "Select the catalogue: an edit to the preset "
                + "changes every object made from it.", () =>
            {
                Selection.activeObject = m_Level.objects.presets;
                EditorGUIUtility.PingObject(m_Level.objects.presets);
            }));
            box.Add(head);

            List<GraphTaskParameter> declared = preset.tree != null ? preset.tree.parameters : new List<GraphTaskParameter>();
            var settings = 0;
            foreach (string id in preset.settings)
            {
                GraphTaskParameter declaration = declared.Find(p => p != null && p.id == id && !string.IsNullOrEmpty(p.name));
                if (declaration == null)
                    continue;
                box.Add(SettingField(row, preset, declaration));
                settings++;
            }
            if (settings == 0)
                box.Add(Hint("No settings: every object of " + preset.name + " is the same."));

            var core = new VisualElement();
            core.SetEnabled(false);
            core.Add(new TextField("Kind") { value = preset.kind.entryName });
            core.Add(new ObjectField("Tree") { objectType = typeof(StateTreeAsset), value = preset.tree });
            foreach (GraphTaskParameterOverride value in preset.parameters.values)
            {
                if (value != null && value.enabled && !preset.IsSetting(value.id))
                    core.Add(new TextField(value.name) { value = Shown(value, declared) });
            }
            var tags = new List<string>();
            foreach (LevelObjectTagRef tag in preset.tags)
            {
                if (tag != null && !string.IsNullOrEmpty(tag.tag))
                    tags.Add(tag.tag);
            }
            if (tags.Count > 0)
                core.Add(new TextField("Tags") { value = string.Join(", ", tags) });
            var facts = new List<string>();
            foreach (LevelObjectPresetFact fact in preset.facts)
            {
                if (fact != null)
                    facts.Add(fact.Label);
            }
            if (facts.Count > 0)
                core.Add(new TextField("Writes") { value = string.Join(", ", facts) });
            var steps = new List<string>();
            foreach (string step in preset.steps)
            {
                StateTreeSchemaKind kind = StepKindDrawer.Find(step);
                steps.Add(kind != null ? kind.Label : step);
            }
            if (steps.Count > 0)
                core.Add(new TextField("Fits") { value = string.Join(", ", steps) });
            box.Add(core);
            return box;
        }

        private static string Shown(GraphTaskParameterOverride value, List<GraphTaskParameter> declared)
        {
            GraphTaskParameter declaration = value.Declaration(declared);
            if (declaration == null || declaration.kind == GraphTaskParameterKind.String)
                return value.stringValue ?? "";
            if (declaration.kind == GraphTaskParameterKind.Bool)
                return value.floatValue != 0f ? "on" : "off";
            return value.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>One setting: this object's value (bold) or the preset's, and ↺ to follow the preset again.</summary>
        private VisualElement SettingField(LevelObjectDef row, LevelObjectPresetDef preset, GraphTaskParameter declaration)
        {
            GraphTaskParameterOverride value = LevelPresetEdits.Setting(preset, row, declaration,
                out GraphTaskParameterOverride own);
            var line = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            string label = StateTreeSchemaRules.Pretty(declaration.name);
            VisualElement field;
            switch (declaration.kind)
            {
                case GraphTaskParameterKind.Bool:
                    var toggle = new Toggle(label) { value = value.floatValue != 0f };
                    toggle.RegisterValueChangedCallback(changed => SetSetting(row, declaration, "", changed.newValue ? 1f : 0f, "", ""));
                    field = toggle;
                    break;
                case GraphTaskParameterKind.Float:
                    var number = new FloatField(label) { value = value.floatValue, isDelayed = true };
                    number.RegisterValueChangedCallback(changed => SetSetting(row, declaration, "", changed.newValue, "", ""));
                    field = number;
                    break;
                default:
                    var parameter = new StateTreeStepParameter
                    {
                        declaration = declaration,
                        field = StateTreeSchemaRules.BoundField(preset.tree, declaration.id),
                        overrideRow = value
                    };
                    var inputs = new StateTreeStepParameterInputs(preset.tree, null, null, preset.tree,
                        (_, text, numberValue, entryId, keyId) => SetSetting(row, declaration, text, numberValue, entryId, keyId));
                    field = inputs.Picker(parameter, label)
                        ?? (StateTreeStepParameterInputs.IsText(parameter) ? inputs.Text(parameter, label) : inputs.NoPicker(parameter, label));
                    break;
            }
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;
            field.tooltip = own != null ? "This object's own value." : "The preset's value — set one to make it this object's own.";
            if (own != null)
                field.style.unityFontStyleAndWeight = FontStyle.Bold;
            line.Add(field);
            if (own != null)
                line.Add(StateTreeInspectorLook.SmallButton("↺", "Follow the preset's value again.", () =>
                {
                    LevelPresetEdits.ResetSetting(m_Level.objects, row, declaration);
                    Rebuild();
                }));
            return line;
        }

        private void SetSetting(LevelObjectDef row, GraphTaskParameter declaration, string text, float number,
            string entryId, string keyId)
        {
            LevelPresetEdits.SetSetting(m_Level.objects, row, declaration, text, number, entryId, keyId);
            Rebuild();
        }

        /// <summary>A row's line drags out — onto a mission tree in the State Tree window, where it becomes a step.</summary>
        private void DragsOut(VisualElement line, LevelObjectDef row)
        {
            Vector2 pressedAt = default;
            var pressed = false;
            line.RegisterCallback<PointerDownEvent>(down =>
            {
                if (down.button != 0)
                    return;
                pressed = true;
                pressedAt = down.position;
            }, TrickleDown.TrickleDown);
            line.RegisterCallback<PointerMoveEvent>(move =>
            {
                if (!pressed || ((Vector2)move.position - pressedAt).sqrMagnitude < 36f)
                    return;
                pressed = false;
                LevelObjectDrag.Start(m_Level.objects, row);
            });
            line.RegisterCallback<PointerUpEvent>(_ => pressed = false, TrickleDown.TrickleDown);
        }

        /// <summary>Every place a step about the row may go, as a menu: the mission trees whose Level is this one,
        /// their zones, and the steps its preset fits there.</summary>
        private void UseInMission(LevelObjectDef row, Rect at)
        {
            List<StateTreeObjectSteps.Use> uses = StateTreeObjectSteps.Uses(StateTreeObjectSteps.MissionTrees(),
                m_Level.objects, row);
            var menu = new GenericMenu();
            if (uses.Count == 0)
                menu.AddDisabledItem(new GUIContent(m_Level.objects.PresetOf(row) == null
                    ? "No preset: only an object made from a preset knows its steps"
                    : "No mission tree has a zone of this level that holds its steps"));
            foreach (StateTreeObjectSteps.Use use in uses)
            {
                StateTreeObjectSteps.Use picked = use;
                menu.AddItem(new GUIContent(use.path), false, () =>
                {
                    StateTreeNodeAsset node = StateTreeObjectSteps.Add(picked.tree, picked.schema, picked.place, -1,
                        picked.kind.kind, m_Level.objects, row);
                    if (node != null)
                        StateTreeEditorWindow.ShowAdded(picked.tree, node);
                });
            }
            menu.DropDown(at);
        }

        private LevelObjectPresetRegistry Presets()
        {
            return m_Level != null && m_Level.objects != null ? m_Level.objects.presets : null;
        }

        // ---- edits -----------------------------------------------------------------------

        /// <summary>A row of <paramref name="kindName"/> at the Scene view's centre — a palette click,
        /// or a kind dropped on the list.</summary>
        internal LevelObjectDef PlaceAtCentre(string kindName, string presetId = null)
        {
            Follow();
            if (m_Level == null || m_Level.objects == null)
                return null;
            Vector2 centre = StateTreeRowCreate.SceneCentre(m_Level.objects);
            return Place(kindName, m_Level.objects.ToWorld(centre), false, presetId);
        }

        /// <summary>A row of <paramref name="kindName"/> (made from the preset <paramref name="presetId"/>, when
        /// one is given) standing at <paramref name="world"/>: appended in one undo step, selected, its group
        /// opened and its name ready to type.</summary>
        internal LevelObjectDef Place(string kindName, Vector3 world, bool onSurface, string presetId = null)
        {
            // Never into a level whose scene is not the open one: a drop lands in the level it is dropped on.
            Follow();
            if (m_Level == null || m_Level.objects == null || string.IsNullOrEmpty(kindName))
                return null;
            LevelObjectPresetDef preset = Presets() != null && !string.IsNullOrEmpty(presetId)
                ? Presets().FindById(presetId) as LevelObjectPresetDef
                : null;
            int group = StateTreeEditorOps.BeginUndoGroup("Place " + kindName);
            LevelObjectDef row = LevelManifestEdits.CreateRow(m_Level.objects, Kinds(), kindName,
                LevelKey(), world, onSurface, preset);
            if (row == null)
                return null;
            // The first object of a level its story does not know yet: its episode entry and its tree are made
            // now, in the same undo step, so "Use in mission…" has a zone to offer at once.
            List<string> made = LevelSetups.ConnectIfMissing(m_Level);
            StateTreeEditorOps.EndUndoGroup(group);
            if (made.Count > 0)
            {
                m_Links = null;
                Debug.Log("[Level Manifest] " + m_Level.name + ": " + string.Join("; ", made));
            }
            m_Selected = m_Level.objects.entries.Count - 1;
            SetFolded(kindName, false);
            m_FocusName = true;
            Rebuild();
            return row;
        }

        private void RemoveRow(int index)
        {
            Undo.RecordObject(m_Level.objects, "Remove Level Object");
            m_Level.objects.entries.RemoveAt(index);
            EditorUtility.SetDirty(m_Level.objects);
            if (index == m_Selected)
                m_Selected = -1;
            else if (index < m_Selected)
                m_Selected--;
            Rebuild();
        }

        private void Select(int index)
        {
            m_Selected = index;
            Rebuild();
        }

        private void CreateObjectsRegistry()
        {
            string levelPath = AssetDatabase.GetAssetPath(m_Level);
            string directory = System.IO.Path.GetDirectoryName(levelPath);
            var registry = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            registry.name = m_Level.name + "Objects";
            AssetDatabase.CreateAsset(registry, directory + "/" + registry.name + ".asset");

            Undo.RecordObject(m_Level, "Create Level Objects");
            m_Level.objects = registry;
            EditorUtility.SetDirty(m_Level);
            AssetDatabase.SaveAssets();
            Rebuild();
        }

        // ---- the scene half --------------------------------------------------------------

        /// <summary>Every placement as a handle: click to select, drag to move. This is the
        /// half a list cannot do — a manifest is positions, and positions are authored by
        /// looking at them.</summary>
        /// <summary>
        /// A translucent stand-in for what a placement will spawn — the kind's
        /// <see cref="LevelObjectKindDef.prefab"/>, drawn where the row stands.
        ///
        /// WHY IT IS WORTH THE CODE. A manifest-driven level has nothing in the scene: the arena
        /// and a field of identical dots. That is enough to know a row EXISTS and nothing at all
        /// about whether the level reads — whether the guard can see the door, whether two units
        /// are standing in each other. A ghost of the actual mesh answers that at a glance, and it
        /// stays a ghost so it can never be mistaken for something selectable.
        ///
        /// TWO SHAPES, THE AUTHOR'S CHOICE (<see cref="LevelGhostStyle"/>): the prefab's real
        /// geometry at 1:1, or a capsule coloured by kind. Nothing is instantiated into the open
        /// scene either way — a scene that spawns preview objects has objects in it, which is the
        /// one thing a manifest-driven level is supposed not to have (rigged characters are posed
        /// in a preview scene instead, see <see cref="LevelGhostMeshes"/>).
        /// </summary>
        /// <param name="manifest">The level's manifest — read for its ground plane, so the ghost
        /// stands the way the spawner will stand it.</param>
        /// <param name="def">The placement row.</param>
        /// <param name="world">Where it stands.</param>
        /// <param name="selected">Whether this is the selected row — drawn a little more solid,
        /// so the selection reads without a second colour.</param>
        private void DrawHologram(LevelObjectRegistry manifest, LevelObjectDef def, Vector3 world,
            bool selected, Plane[] frustum)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            // An off-screen ghost costs nothing: a generous fixed box, because a placement's
            // ghost is at most a character or a door.
            if (frustum != null
                && !GeometryUtility.TestPlanesAABB(frustum, new Bounds(world, Vector3.one * 4f)))
                return;
            GameObject prefab = PreviewOf(def);

            if (s_Hologram == null)
            {
                // HIDDEN/INTERNAL-COLORED, not Unlit/Color: this is the shader Unity ships FOR
                // immediate-mode editor drawing, and the only one here whose blend, depth and cull
                // state are settable — Unlit/Color has no _SrcBlend, so the alpha set on it was
                // silently ignored and every "ghost" came out solid.
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                    return;
                s_Hologram = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                s_Hologram.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_Hologram.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                // Depth-tested but not depth-writing, so a ghost behind the wall reads as behind
                // it and two overlapping ghosts both show rather than one erasing the other.
                s_Hologram.SetInt("_ZWrite", 0);
                s_Hologram.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                s_Hologram.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                s_Hologram.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            // KIND DECIDES THE COLOUR, selection decides how solid — two channels rather than one,
            // so "which of these is an NPC" and "which one am I editing" are both readable at once.
            Color tint = ColourOfKind(def.kind.entryName);
            s_Hologram.color = new Color(tint.r, tint.g, tint.b, selected ? 0.55f : 0.28f);
            // AFTER the colour, and before any draw: SetPass is what binds the material's CURRENT
            // values. Without it Graphics.DrawMeshNow inherits whatever pass the scene view last
            // bound, which is why nothing recognisable appeared.
            s_Hologram.SetPass(0);

            // The row's own facing, through the manifest — the same call the spawner makes, so a
            // ghost cannot be turned a different way from the thing it is a ghost OF.
            Quaternion facing = manifest.Facing(def.facing);

            IReadOnlyList<LevelGhostMeshes.Part> parts = style == LevelGhostStyle.Mesh
                ? LevelGhostMeshes.Of(prefab)
                : null;

            // Capsule mode, and mesh mode for a prefab with no geometry at all (a look that is all
            // particles, sprites or code) — an object standing somewhere still has to show.
            if (parts == null || parts.Count == 0)
            {
                DrawStandIn(world, facing, manifest.Forward(def.facing));
                return;
            }

            Matrix4x4 place = Matrix4x4.TRS(world, facing * prefab.transform.rotation, Vector3.one);
            for (int i = 0; i < parts.Count; i++)
            {
                Mesh mesh = parts[i].mesh;
                if (mesh == null)
                    continue;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.DrawMeshNow(mesh, place * parts[i].local, sub);
            }
        }

        /// <summary>The colour a kind is drawn in — see <see cref="LevelManifestEdits.KindColour"/>;
        /// the palette tile and the group wear the same one.</summary>
        private Color ColourOfKind(string kind)
        {
            return LevelManifestEdits.KindColour(Kinds(), kind);
        }

        /// <summary>
        /// The shape a placement gets when there is no mesh to show: a person-sized CAPSULE.
        ///
        /// Not a dot and not a box. A dot has no size, so it says nothing about whether two
        /// placements are standing in each other or whether one blocks a doorway — which is most
        /// of what an author is looking at a level FOR. A box reads as a crate or a wall. A capsule
        /// reads as somebody standing there, is what a character controller actually is, and is
        /// obviously a placeholder, so nobody mistakes it for the finished thing.
        /// </summary>
        /// <param name="world">Where the placement stands; the capsule sits ON that point rather
        /// than centred through it, because a placement is a spot on the floor.</param>
        /// <param name="facing">The row's facing. A capsule is symmetric, so this changes nothing
        /// about the shape — it is here so the stand-in is oriented like the mesh it stands in
        /// for, and it is the tick below that actually shows the direction.</param>
        /// <param name="forward">Which way that facing looks, in the level's own plane.</param>
        private void DrawStandIn(Vector3 world, Quaternion facing, Vector3 forward)
        {
            if (s_StandIn == null)
            {
                // Borrowed from a throwaway primitive rather than built by hand: it is Unity's own
                // capsule, so the stand-in is exactly the shape everyone already recognises as a
                // placeholder character.
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                MeshFilter filter = temp.GetComponent<MeshFilter>();
                s_StandIn = filter != null ? filter.sharedMesh : null;
                UnityEngine.Object.DestroyImmediate(temp);
                if (s_StandIn == null)
                    return;
            }

            // Unity's capsule is 2 units tall about its centre, so lifting it by one puts its feet
            // on the placement.
            Graphics.DrawMeshNow(s_StandIn,
                Matrix4x4.TRS(world + new Vector3(0f, 1f, 0f), facing,
                    new Vector3(k_StandInWidth, 1f, k_StandInWidth)), 0);

            // Which way it looks. Drawn as a line from the chest rather than as a second mesh: it
            // has to read at any zoom, and it must not be mistakable for another placement.
            Vector3 chest = world + new Vector3(0f, 1.1f, 0f);
            Handles.DrawLine(chest, chest + forward * (k_StandInWidth * 1.6f));
        }

        /// <summary>How wide the stand-in is relative to Unity's capsule — narrowed to person
        /// proportions rather than the barrel the primitive ships as.</summary>
        private const float k_StandInWidth = 0.7f;

        /// <summary>The primitive capsule's mesh, borrowed once and shared.</summary>
        private static Mesh s_StandIn;

        /// <summary>The prefab a row's KIND says it looks like, or null.</summary>
        private GameObject PreviewOf(LevelObjectDef def)
        {
            LevelObjectKindRegistry kinds = Kinds();
            return kinds != null && kinds.FindByName(def.kind.entryName) is LevelObjectKindDef kind
                ? kind.prefab
                : null;
        }

        /// <summary>The ghost material, made once and shared by every placement.</summary>
        private static Material s_Hologram;

        private void OnSceneGui(SceneView view)
        {
            EventType type = Event.current.type;
            if (type == EventType.Layout || type == EventType.DragUpdated || type == EventType.DragPerform)
                Follow();
            if (m_Level == null || m_Level.objects == null)
                return;

            LevelObjectRegistry manifest = m_Level.objects;
            if (HandleKindDrop(view, manifest))
                return;
            List<LevelObjectDef> rows = manifest.entries;
            Plane[] frustum = Event.current.type == EventType.Repaint && view.camera != null
                ? GeometryUtility.CalculateFrustumPlanes(view.camera)
                : null;
            for (int i = 0; i < rows.Count; i++)
            {
                LevelObjectDef def = rows[i];
                if (def == null)
                    continue;

                // THROUGH THE MANIFEST'S PLANE, never assumed: the same call the spawner makes,
                // so the handle is where the object will be rather than where a 2D level's would
                // have been. See LevelGroundPlane.
                Vector3 world = manifest.ToWorld(def.position);
                float size = HandleUtility.GetHandleSize(world) * 0.12f;
                bool selected = i == m_Selected;

                DrawHologram(manifest, def, world, selected, frustum);

                using (new Handles.DrawingScope(selected
                    ? new Color(1f, 0.85f, 0.3f)
                    : new Color(0.45f, 0.8f, 1f, 0.85f)))
                {
                    if (Handles.Button(world, Quaternion.identity, size, size, Handles.DotHandleCap))
                    {
                        m_Selected = i;
                        Rebuild();
                    }
                    Handles.Label(world + new Vector3(size * 1.4f, size * 1.2f, 0f), def.name);
                }

                if (!selected)
                    continue;

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(world, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(manifest, "Move Level Object");
                    def.position = manifest.ToPlan(moved);
                    EditorUtility.SetDirty(manifest);
                    // No Rebuild here: tearing down and rebinding the whole panel per drag
                    // frame is what made dragging stutter. The bound fields refresh
                    // themselves from the SerializedObject.
                }
            }
        }

        /// <summary>
        /// A palette kind dragged over the Scene view: a ring where it would land while it moves, and on
        /// release a new row there — on the surface under the pointer (what the scene renders, else a
        /// collider), or on the level's ground plane where there is none.
        /// </summary>
        /// <returns>True when the event was the drag's, and used.</returns>
        private bool HandleKindDrop(SceneView view, LevelObjectRegistry manifest)
        {
            Event current = Event.current;
            switch (current.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    string kind = LevelKindPalette.DraggedKind();
                    if (kind == null)
                        return false;
                    Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                    Vector2 gui = current.mousePosition;
                    bool lands = LevelManifestEdits.DropPoint(manifest, ray,
                        (Ray _, out Vector3 point) => SurfaceUnder(gui, ray, out point),
                        out Vector3 world, out bool onSurface);
                    DragAndDrop.visualMode = lands ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    m_DropKind = kind;
                    m_DropPreview = lands ? world : (Vector3?)null;
                    if (current.type == EventType.DragPerform && lands)
                    {
                        DragAndDrop.AcceptDrag();
                        m_DropPreview = null;
                        Place(kind, world, onSurface, LevelKindPalette.DraggedPreset());
                    }
                    current.Use();
                    view.Repaint();
                    return true;
                case EventType.DragExited:
                    if (m_DropPreview != null)
                    {
                        m_DropPreview = null;
                        view.Repaint();
                    }
                    return false;
                case EventType.Repaint:
                    if (m_DropPreview is Vector3 at && LevelKindPalette.DraggedKind() != null)
                    {
                        Vector3 up = manifest.ToWorld(Vector2.zero, 1f) - manifest.ToWorld(Vector2.zero);
                        float size = HandleUtility.GetHandleSize(at) * 0.3f;
                        using (new Handles.DrawingScope(ColourOfKind(m_DropKind)))
                        {
                            Handles.DrawWireDisc(at, up, size, 2f);
                            Handles.Label(at + up * size, m_DropKind);
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>The first thing the Scene view shows under <paramref name="gui"/>, else the first
        /// collider along <paramref name="ray"/> (triggers ignored).</summary>
        private static bool SurfaceUnder(Vector2 gui, Ray ray, out Vector3 point)
        {
            if (HandleUtility.PlaceObject(gui, out point, out _))
                return true;
            if (Physics.Raycast(ray, out RaycastHit hit, k_DropReach, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            point = default;
            return false;
        }

        /// <summary>How far a drop looks for a collider.</summary>
        private const float k_DropReach = 2000f;

        // ---- lookups ---------------------------------------------------------------------

        /// <summary>
        /// The level whose scene is open — the one thing that makes this a scene editor rather than a floating asset
        /// editor. Checked against the open scene every time, not kept: New level builds its scene with NewScene and
        /// saves it under its path, which raises no sceneOpened, and a level kept from before went on taking the new
        /// level's objects. No level owns the scene: none — never the last one.
        /// </summary>
        /// <returns>True when the level changed.</returns>
        private bool ResolveLevel()
        {
            if (m_Pinned)
                return false;
            string scenePath = openScene() ?? "";
            if (m_Level != null && scenePath.Length > 0 && m_Level.scenePath == scenePath)
                return false;
            // A miss is kept until the scene or the project changes: the search walks the project.
            if (m_Level == null && m_ResolvedFor == scenePath)
                return false;
            m_ResolvedFor = scenePath;
            LevelContent found = LevelSetups.LevelOfScene(scenePath);
            if (ReferenceEquals(found, m_Level))
                return false;
            m_Level = found;
            m_Kinds = null;
            m_KindsSearched = false;
            m_LevelKey = null;
            m_Links = null;
            m_Selected = -1;
            return true;
        }

        /// <summary>The scene path the last search was for; null: search again.</summary>
        private string m_ResolvedFor;

        /// <summary>
        /// THIS LEVEL'S kind catalog — the <see cref="LevelRegistry.kinds"/> of the registry that
        /// CATALOGS this level, not the first one found in the project.
        ///
        /// WHY THAT DISTINCTION IS THE WHOLE FUNCTION. A project has more than one level catalog
        /// (the raider areas and the M21 demo are two), and each names its own kinds. Taking the
        /// first meant the M21 yard was asked about the raider catalog: its 'npc' rows matched
        /// nothing, every preview came back null, and the ghosts silently did not draw — a wrong
        /// answer that looked exactly like an unimplemented feature. Kinds also drive the Add
        /// button's list, where the same slip offered 'door' and 'unit' in a level whose spawner
        /// has never heard of them.
        ///
        /// The level is found by the row that POINTS AT IT, which is the only link that exists —
        /// a <see cref="LevelContent"/> does not name its catalog, on purpose (M16: the content is
        /// the level, the catalog is a list of names).
        /// </summary>
        /// <returns>The catalog, or null when nothing catalogs this level yet — in which case the
        /// window offers no kinds rather than the wrong ones.</returns>
        /// <summary>A registry that turns up later (created, or Verify wiring the ref) is
        /// noticed here — and the panel redrawn, since "nothing to add" may have changed.</summary>
        private void OnProjectChanged()
        {
            m_ResolvedFor = null;
            m_Links = null;
            m_Kinds = null;
            m_KindsSearched = false;
            m_LevelKey = null;
            m_Palette?.Forget();
            Rebuild();
        }

        /// <summary>The MISS is cached too: without the flag, a level whose catalog is not
        /// wired re-ran this project scan per row per repaint — the lag this window had.</summary>
        private bool m_KindsSearched;

        private LevelObjectKindRegistry Kinds()
        {
            if (m_Kinds != null || m_KindsSearched || m_Level == null)
                return m_Kinds;
            m_KindsSearched = true;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LevelRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var levels = AssetDatabase.LoadAssetAtPath<LevelRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (levels == null || levels.kinds == null)
                    continue;
                for (int j = 0; j < levels.entries.Count; j++)
                {
                    if (levels.entries[j] != null && levels.entries[j].content == m_Level)
                    {
                        m_LevelKey = LevelManifestEdits.LevelKey(m_Level, levels.entries[j].name);
                        return m_Kinds = levels.kinds;
                    }
                }
            }
            return null;
        }

        /// <summary>Resolved once per level — see <see cref="Kinds"/>. Cleared with
        /// <see cref="m_Level"/>, and worth caching because the hologram asks on every repaint.</summary>
        private LevelObjectKindRegistry m_Kinds;

        /// <summary>The level's short name new rows are named after ("hall") — its catalog row's name,
        /// found with <see cref="m_Kinds"/>.</summary>
        private string m_LevelKey;

        private string LevelKey()
        {
            Kinds();
            return m_LevelKey ??= LevelManifestEdits.LevelKey(m_Level, null);
        }

        private static Label Hint(string text)
        {
            Label note = StateTreeInspectorLook.Note(text);
            note.style.paddingTop = 4f;
            note.style.paddingBottom = 4f;
            return note;
        }
    }
}
