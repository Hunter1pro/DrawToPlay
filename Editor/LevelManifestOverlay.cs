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
            m_Kinds = null;
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
        /// Drawn straight from the prefab's mesh filters rather than by instantiating anything: a
        /// scene that spawns preview objects has objects in it, which is the one thing a
        /// manifest-driven level is supposed not to have. What cannot be drawn that way — a rigged
        /// character above all — gets a capsule instead (<see cref="DrawStandIn"/>).
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

            s_Hologram.color = selected
                ? new Color(0.35f, 0.85f, 1f, 0.45f)
                : new Color(0.55f, 0.75f, 0.9f, 0.22f);
            // AFTER the colour, and before any draw: SetPass is what binds the material's CURRENT
            // values. Without it Graphics.DrawMeshNow inherits whatever pass the scene view last
            // bound, which is why nothing recognisable appeared.
            s_Hologram.SetPass(0);

            // The row's own facing, through the manifest — the same call the spawner makes, so a
            // ghost cannot be turned a different way from the thing it is a ghost OF.
            Quaternion facing = manifest.Facing(def.facing);

            if (prefab == null)
            {
                DrawStandIn(world, facing, manifest.Forward(def.facing));
                return;
            }

            var drew = false;
            Matrix4x4 place = Matrix4x4.TRS(world, facing * prefab.transform.rotation, Vector3.one);
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                    continue;
                // The child's own transform, relative to the prefab root, so a multi-part prefab
                // keeps its shape.
                Matrix4x4 local = prefab.transform.worldToLocalMatrix
                    * filters[i].transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.DrawMeshNow(mesh, place * local, sub);
                drew = true;
            }

            // A RIGGED CHARACTER GETS THE CAPSULE, and this is the case that motivates having one.
            // A skinned mesh's vertices are not where the character is: they are in bone space,
            // and the bind poses put them into a shape. Drawn straight, M21's mannequin has mesh
            // bounds of 2cm on a transform scaled ×100 — measured — so it rasterises as a thin
            // vertical sliver, which is worse than nothing because it looks like a rendering fault
            // rather than a missing feature. Posing it properly would mean instantiating the
            // prefab and baking it, and putting objects in the scene is the one thing a
            // manifest-driven level exists to avoid.
            var rigged = false;
            SkinnedMeshRenderer[] skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length && !rigged; i++)
                rigged = skins[i].sharedMesh != null;

            // A prefab whose look is all code, particles or sprites has nothing to draw either. It
            // is still an object standing somewhere, so it gets the stand-in rather than vanishing.
            if (rigged || !drew)
                DrawStandIn(world, facing, manifest.Forward(def.facing));
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
        /// overlay offers no kinds rather than the wrong ones.</returns>
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
