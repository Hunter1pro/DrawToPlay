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
    /// shows that level's object rows — add, remove, configure, and DRAG THEM IN THE SCENE.
    ///
    /// The scene holds only the arena, so there is nothing to select in it; the placements
    /// live in the level's <see cref="LevelObjectRegistry"/>. This panel is where they are
    /// authored: the open scene picks the level (its <see cref="LevelContent.scenePath"/>
    /// matches), a kind dropdown plus Add appends a row at the view's centre, each row is a
    /// button that selects it, and the selected row draws through the normal property
    /// drawers — so kind, tree and parameters are the same fields the dashboard shows, with
    /// no duplicate UI to keep in step.
    ///
    /// A dockable WINDOW, not a scene overlay. It started as an overlay for the scene
    /// handles' sake, but the handles never needed that — any window can subscribe to
    /// <see cref="SceneView.duringSceneGui"/> — and a selected row's full editor (tree,
    /// parameters, tags) is taller than a scene view can host: the overlay covered the level
    /// it was editing and cut its own bottom off. As a window it scrolls, the user docks it
    /// where they like (first open lands beside the Inspector), and the scene stays visible
    /// while the handles keep working exactly as before: drag to move a row, click to
    /// select it.
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
        private LevelContent m_Level;
        private SerializedObject m_Objects;

        // SERIALIZED, because an EditorWindow is serialize/deserialized when it is DOCKED and
        // on every domain reload — a plain field comes back as its C# default, which is how
        // the selection silently jumped from the row being edited to row zero the moment the
        // window was dragged into a layout.
        [SerializeField] private int m_Selected = -1;
        [SerializeField] private string m_AddKind = "";

        /// <summary>Runs again after every domain reload, which is exactly what these
        /// subscriptions need — the overlay's OnCreated only ran once per overlay life.</summary>
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorSceneManagerHooks();
            Undo.undoRedoPerformed += Rebuild;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
            Undo.undoRedoPerformed -= Rebuild;
        }

        private void EditorSceneManagerHooks()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private void OnSceneOpened(Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode)
        {
            m_Level = null;
            m_Kinds = null;
            m_Selected = -1;
            Rebuild();
        }

        /// <summary>The window's whole body is one SCROLL VIEW — the overlay's fatal flaw was
        /// a fixed panel that cut the selected row's editor off at the scene view's edge.</summary>
        private void CreateGUI()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            rootVisualElement.Add(scroll);

            m_Root = new VisualElement();
            m_Root.style.paddingLeft = 6f;
            m_Root.style.paddingRight = 6f;
            m_Root.style.paddingTop = 6f;
            m_Root.style.paddingBottom = 6f;
            scroll.Add(m_Root);
            Rebuild();
        }

        // ---- panel -----------------------------------------------------------------------

        private void Rebuild()
        {
            if (m_Root == null)
                return;
            m_Root.Clear();

            ResolveLevel();
            if (m_Level == null)
            {
                m_Root.Add(Hint("No level owns the open scene. Open an area scene — the level "
                    + "whose Scene Path matches is edited here."));
                return;
            }

            m_Root.Add(new Label(m_Level.displayName.Length > 0
                ? m_Level.displayName + "  (" + m_Level.name + ")"
                : m_Level.name)
            { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4f } });

            if (m_Level.objects == null)
            {
                m_Root.Add(Hint("This level has no object registry yet."));
                m_Root.Add(new Button(CreateObjectsRegistry) { text = "Create objects registry" });
                return;
            }

            m_Objects = new SerializedObject(m_Level.objects);
            m_Root.Add(BuildStyleRow());
            m_Root.Add(BuildAddRow());
            m_Root.Add(BuildList());
            m_Root.Add(BuildSelected());
            m_Root.Add(BuildSaveRow());
            SceneView.RepaintAll();
        }

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

        /// <summary>The chosen ghost shape, remembered per user across sessions.</summary>
        private static LevelGhostStyle style
        {
            get => (LevelGhostStyle)EditorPrefs.GetInt(k_StyleKey, (int)LevelGhostStyle.Mesh);
            set => EditorPrefs.SetInt(k_StyleKey, (int)value);
        }

        private const string k_StyleKey = "PowerOfFire.DrawToPlay.LevelManifest.GhostStyle";

        private VisualElement BuildAddRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

            var kinds = new List<string>();
            CollectKindNames(kinds);
            if (kinds.Count == 0)
            {
                row.Add(Hint("No LevelObjectKindRegistry in the project — nothing to add."));
                return row;
            }
            if (string.IsNullOrEmpty(m_AddKind) || !kinds.Contains(m_AddKind))
                m_AddKind = kinds[0];

            var kindField = new DropdownField(kinds, kinds.IndexOf(m_AddKind));
            kindField.style.flexGrow = 1f;
            kindField.RegisterValueChangedCallback(changed => m_AddKind = changed.newValue);
            row.Add(kindField);

            var add = new Button(() => AddRow(m_AddKind)) { text = "Add" };
            add.style.flexShrink = 0f;
            add.tooltip = "Append a placement of this kind at the centre of the scene view, "
                + "then drag its handle to position it.";
            row.Add(add);
            return row;
        }

        private VisualElement BuildList()
        {
            var list = new VisualElement { style = { marginTop = 6f, marginBottom = 6f } };
            List<LevelObjectDef> rows = m_Level.objects.entries;
            if (rows.Count == 0)
            {
                list.Add(Hint("No placements yet."));
                return list;
            }

            var lastGroup = "\0";
            for (int i = 0; i < rows.Count; i++)
            {
                LevelObjectDef def = rows[i];
                if (def == null)
                    continue;
                if (def.group != lastGroup)
                {
                    lastGroup = def.group;
                    list.Add(new Label(string.IsNullOrEmpty(lastGroup) ? "(no group)" : lastGroup)
                    {
                        style = { opacity = 0.6f, marginTop = 4f }
                    });
                }

                int index = i;
                var line = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var select = new Button(() => Select(index))
                {
                    text = (string.IsNullOrEmpty(def.name) ? "(unnamed)" : def.name)
                        + "  ·  " + def.kind.entryName
                };
                select.style.flexGrow = 1f;
                select.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (index == m_Selected)
                    select.style.unityFontStyleAndWeight = FontStyle.Bold;
                line.Add(select);

                var remove = new Button(() => RemoveRow(index)) { text = "✕" };
                remove.style.width = 22f;
                remove.style.flexShrink = 0f;
                line.Add(remove);
                list.Add(line);
            }
            return list;
        }

        /// <summary>The selected row, drawn through the normal drawers — the kind and entry
        /// dropdowns come free, so this panel never duplicates them.</summary>
        private VisualElement BuildSelected()
        {
            var box = new VisualElement();
            List<LevelObjectDef> rows = m_Level.objects.entries;
            if (m_Selected < 0 || m_Selected >= rows.Count)
            {
                box.Add(Hint("Select a placement to configure it."));
                return box;
            }

            SerializedProperty element = m_Objects.FindProperty("entries")
                .GetArrayElementAtIndex(m_Selected);
            // "attributes" is the DATA half of the row and "tree"/"parameters" the FLOW half —
            // what this one is worth, and the mind it runs with its arguments. Hiding either
            // here was how a placement could only be specialised in code. Both sections draw
            // themselves against whatever the row picks — one checkbox per declared knob, per
            // declared attribute — so reusing a def and specialising one are the same gesture.
            foreach (string field in new[]
                { "name", "group", "kind", "entry", "attributes", "tree", "parameters",
                    "position", "facing", "tags" })
            {
                SerializedProperty child = element.FindPropertyRelative(field);
                if (child == null)
                    continue;
                var propertyField = new PropertyField(child);
                propertyField.Bind(m_Objects);
                propertyField.RegisterValueChangeCallback(_ => SceneView.RepaintAll());
                box.Add(propertyField);
            }
            return box;
        }

        // ---- edits -----------------------------------------------------------------------

        private void AddRow(string kindName)
        {
            LevelObjectKindDef kind = FindKind(kindName);
            Vector2 at = ViewCentre();

            Undo.RecordObject(m_Level.objects, "Add Level Object");
            var def = new LevelObjectDef
            {
                id = System.Guid.NewGuid().ToString("N"),
                name = kindName + " " + (m_Level.objects.entries.Count + 1),
                group = kindName + "s",
                position = at
            };
            if (kind != null)
            {
                def.kind.entryId = kind.id;
                def.kind.entryName = kind.name;
            }
            m_Level.objects.entries.Add(def);
            EditorUtility.SetDirty(m_Level.objects);
            m_Selected = m_Level.objects.entries.Count - 1;
            Rebuild();
        }

        private void RemoveRow(int index)
        {
            Undo.RecordObject(m_Level.objects, "Remove Level Object");
            m_Level.objects.entries.RemoveAt(index);
            EditorUtility.SetDirty(m_Level.objects);
            if (m_Selected >= m_Level.objects.entries.Count)
                m_Selected = m_Level.objects.entries.Count - 1;
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
            bool selected)
        {
            if (Event.current.type != EventType.Repaint)
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

        /// <summary>
        /// The colour a KIND is drawn in — derived, not authored, so a project that adds a kind
        /// gets a distinct colour for free and the same kind looks the same in every level.
        /// Authoring it would be one more field to fill in and one more thing left at white.
        ///
        /// FROM THE KIND'S POSITION IN THE REGISTRY, spaced by the golden ratio. Hashing the name
        /// was the obvious idea and it does not work: hashes are uniform, not SEPARATED, and this
        /// project's own two kinds landed 0.036 of the wheel apart — 'npc' and 'pickup' both drew
        /// green, which is precisely the distinction the colour exists to make. The registry is an
        /// ordered list, and stepping by 0.618 of the wheel per index is the standard way to keep
        /// consecutive entries maximally far apart, so the first several kinds are unmistakable.
        ///
        /// A kind the registry does not list — a row left behind by a renamed kind — still has to
        /// draw, and falls back to a hash. <c>string.GetHashCode</c> is deliberately not used: it
        /// is randomised per process, so the level would change colour on every editor restart.
        /// </summary>
        /// <param name="kind">The kind row's name; empty is legal and gets the neutral tint.</param>
        /// <returns>A saturated, bright colour — alpha is the caller's business.</returns>
        private Color ColourOfKind(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return k_NoKindTint;

            LevelObjectKindRegistry registry = Kinds();
            if (registry != null)
            {
                for (int i = 0; i < registry.entries.Count; i++)
                {
                    if (registry.entries[i] != null
                        && string.Equals(registry.entries[i].name, kind, StringComparison.Ordinal))
                        return Hue(i * 0.618034f % 1f);
                }
            }

            uint hash = 2166136261u;
            for (int i = 0; i < kind.Length; i++)
                hash = (hash ^ kind[i]) * 16777619u;
            return Hue(hash % 3600u / 3600f);
        }

        private static Color Hue(float hue)
        {
            return Color.HSVToRGB(hue, 0.7f, 1f);
        }

        /// <summary>What a placement with no kind is drawn in — a neutral that is not on the wheel,
        /// so "unset" never looks like one of the kinds.</summary>
        private static readonly Color k_NoKindTint = new Color(0.7f, 0.7f, 0.72f);

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
            if (m_Level == null || m_Level.objects == null)
                return;

            LevelObjectRegistry manifest = m_Level.objects;
            List<LevelObjectDef> rows = manifest.entries;
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

                DrawHologram(manifest, def, world, selected);

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
                    Rebuild();
                }
            }
        }

        // ---- lookups ---------------------------------------------------------------------

        /// <summary>The level whose scene is open — the one thing that makes this a scene
        /// editor rather than a floating asset editor.</summary>
        private void ResolveLevel()
        {
            if (m_Level != null)
                return;
            string scenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(scenePath))
                return;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LevelContent));
            for (int i = 0; i < guids.Length; i++)
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelContent>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (level != null && level.scenePath == scenePath)
                {
                    m_Level = level;
                    return;
                }
            }
        }

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
        private LevelObjectKindRegistry Kinds()
        {
            if (m_Kinds != null || m_Level == null)
                return m_Kinds;

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
                        return m_Kinds = levels.kinds;
                }
            }
            return null;
        }

        /// <summary>Resolved once per level — see <see cref="Kinds"/>. Cleared with
        /// <see cref="m_Level"/>, and worth caching because the hologram asks on every repaint.</summary>
        private LevelObjectKindRegistry m_Kinds;

        private void CollectKindNames(List<string> into)
        {
            LevelObjectKindRegistry registry = Kinds();
            if (registry == null)
                return;
            for (int j = 0; j < registry.entries.Count; j++)
            {
                LevelObjectKindDef row = registry.entries[j];
                if (row != null && !string.IsNullOrEmpty(row.name) && !into.Contains(row.name))
                    into.Add(row.name);
            }
        }

        private LevelObjectKindDef FindKind(string name)
        {
            LevelObjectKindRegistry registry = Kinds();
            return registry != null ? registry.FindByName(name) as LevelObjectKindDef : null;
        }

        private static Vector2 ViewCentre()
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null ? (Vector2)view.pivot : Vector2.zero;
        }

        private static Label Hint(string text)
        {
            return new Label(text)
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    opacity = 0.7f,
                    paddingTop = 4f,
                    paddingBottom = 4f
                }
            };
        }
    }
}
