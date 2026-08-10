using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE LEVEL EDITOR for a manifest-driven level: open an area's scene and this overlay
    /// shows that level's object rows — add, remove, configure, and DRAG THEM IN THE SCENE.
    ///
    /// The scene holds only the arena, so there is nothing to select in it; the placements
    /// live in the level's <see cref="LevelObjectRegistry"/>. This panel is where they are
    /// authored: the open scene picks the level (its <see cref="LevelContent.scenePath"/>
    /// matches), a kind dropdown plus Add appends a row at the view's centre, each row is a
    /// button that selects it, and the selected row draws through the normal property
    /// drawers — so kind and entry are the same dropdowns the dashboard shows, with no
    /// duplicate UI to keep in step.
    ///
    /// Positions are handles in the scene view: drag to move a row, click to select it. That
    /// is the part a list cannot do, and the reason this is an overlay rather than a window.
    /// </summary>
    [Overlay(typeof(SceneView), k_Id, "Level Manifest", true,
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top,
        defaultLayout = Layout.Panel,
        defaultWidth = k_Width)]
    internal sealed class LevelManifestOverlay : Overlay
    {
        private const string k_Id = "Scene View/Level Manifest";
        private const float k_Width = 300f;

        /// <summary>Radius of a row's scene handle, in SCREEN pixels — placements have no
        /// size of their own until they spawn, so the handle is a constant target at any
        /// zoom.</summary>
        private const float k_HandlePixels = 9f;

        private VisualElement m_Root;
        private LevelContent m_Level;
        private SerializedObject m_Objects;
        private int m_Selected = -1;
        private string m_AddKind = "";

        public override void OnCreated()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorSceneManagerHooks();
            Undo.undoRedoPerformed += Rebuild;
        }

        public override void OnWillBeDestroyed()
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
            m_Selected = -1;
            Rebuild();
        }

        public override VisualElement CreatePanelContent()
        {
            m_Root = new VisualElement { style = { width = new StyleLength(k_Width) } };
            Rebuild();
            return m_Root;
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
            m_Root.Add(BuildAddRow());
            m_Root.Add(BuildList());
            m_Root.Add(BuildSelected());
            SceneView.RepaintAll();
        }

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
            foreach (string field in new[]
                { "name", "group", "kind", "entry", "position", "tags", "config" })
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
        private void OnSceneGui(SceneView view)
        {
            if (m_Level == null || m_Level.objects == null)
                return;

            List<LevelObjectDef> rows = m_Level.objects.entries;
            for (int i = 0; i < rows.Count; i++)
            {
                LevelObjectDef def = rows[i];
                if (def == null)
                    continue;

                var world = new Vector3(def.position.x, def.position.y, 0f);
                float size = HandleUtility.GetHandleSize(world) * 0.12f;
                bool selected = i == m_Selected;

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
                    Undo.RecordObject(m_Level.objects, "Move Level Object");
                    def.position = new Vector2(moved.x, moved.y);
                    EditorUtility.SetDirty(m_Level.objects);
                    Rebuild();
                }
            }
        }

        // ---- lookups ---------------------------------------------------------------------

        /// <summary>The level whose scene is open — the one thing that makes this an overlay
        /// on the scene rather than a floating asset editor.</summary>
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

        private static void CollectKindNames(List<string> into)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LevelObjectKindRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null)
                    continue;
                for (int j = 0; j < registry.entries.Count; j++)
                {
                    LevelObjectKindDef row = registry.entries[j];
                    if (row != null && !string.IsNullOrEmpty(row.name) && !into.Contains(row.name))
                        into.Add(row.name);
                }
            }
        }

        private static LevelObjectKindDef FindKind(string name)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LevelObjectKindRegistry));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                var row = registry != null ? registry.FindByName(name) as LevelObjectKindDef : null;
                if (row != null)
                    return row;
            }
            return null;
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
