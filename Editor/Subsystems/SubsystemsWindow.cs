using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE PROJECT, IN SUBSYSTEMS (M37.1) — the table of contents an engineer decides a project
    /// in: every def by scope, its class, what it asks / says / shows / has / is tuned by, and
    /// the scenes that install it. Sketches that have not generated yet are listed after, as
    /// the work in progress they are, and "New…" starts one.
    /// </summary>
    internal sealed class SubsystemsWindow : EditorWindow
    {
        [MenuItem("Tools/Draw To Play/Subsystems")]
        public static void Open()
        {
            GetWindow<SubsystemsWindow>("Subsystems").Show();
        }

        private List<SubsystemCatalog.Entry> m_Entries;
        private List<SubsystemSketch> m_Sketches;
        private Vector2 m_Scroll;
        private string m_Filter = "";

        private void OnEnable()
        {
            Refresh();
        }

        private void OnFocus()
        {
            Refresh();
        }

        private void Refresh()
        {
            m_Entries = SubsystemCatalog.Build();
            m_Sketches = SubsystemCatalog.Sketches();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_Filter = EditorGUILayout.TextField(m_Filter, EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(120f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻", EditorStyles.toolbarButton, GUILayout.Width(26f)))
                Refresh();
            if (GUILayout.Button("New…", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                NewSketch();
            EditorGUILayout.EndHorizontal();

            if (m_Entries == null)
                Refresh();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            int drew = DrawGroup(false, "no subsystems");

            // KINDS are defs too — the things a level spawns by placement — and they are what
            // the M21 spine has nine of. They are not installed, so they are not what this
            // window is for; they are listed after, so the count of "defs" and the count of
            // "subsystems" stop being confused for each other.
            var kinds = 0;
            for (int i = 0; i < m_Entries.Count; i++)
                if (m_Entries[i].isKind && Matches(m_Entries[i])) kinds++;
            if (kinds > 0)
            {
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField("Kinds — spawned by placement, not installed",
                    EditorStyles.boldLabel);
                DrawGroup(true, "");
            }

            var pending = 0;
            for (int i = 0; i < m_Sketches.Count; i++)
            {
                if (m_Sketches[i].generatedDef != null)
                    continue;
                if (pending++ == 0)
                {
                    EditorGUILayout.Space(10f);
                    EditorGUILayout.LabelField("Sketched, not yet generated", EditorStyles.boldLabel);
                }
                DrawSketch(m_Sketches[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Subsystems (or kinds) by scope; returns how many were drawn.</summary>
        private int DrawGroup(bool kinds, string emptyMessage)
        {
            StateTreeContextKind? heading = null;
            var drew = 0;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                SubsystemCatalog.Entry entry = m_Entries[i];
                if (entry.isKind != kinds || !Matches(entry))
                    continue;
                if (heading != entry.def.scope)
                {
                    heading = entry.def.scope;
                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField(heading.ToString(), EditorStyles.boldLabel);
                }
                DrawEntry(entry);
                drew++;
            }
            if (drew == 0 && !string.IsNullOrEmpty(emptyMessage))
                EditorGUILayout.LabelField(emptyMessage, EditorStyles.miniLabel);
            return drew;
        }

        private bool Matches(SubsystemCatalog.Entry entry)
        {
            if (string.IsNullOrEmpty(m_Filter))
                return true;
            string needle = m_Filter.ToLowerInvariant();
            return entry.def.name.ToLowerInvariant().Contains(needle)
                || (entry.typeName ?? "").ToLowerInvariant().Contains(needle);
        }

        private void DrawEntry(SubsystemCatalog.Entry entry)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(entry.def.name, entry.path),
                EditorStyles.linkLabel, GUILayout.Width(170f)))
            {
                Selection.activeObject = entry.def;
                EditorGUIUtility.PingObject(entry.def);
            }

            string type = entry.hasClass
                ? entry.serviceType.Name
                : entry.isKind ? "body: " + entry.def.body.prefab.name
                : string.IsNullOrEmpty(entry.typeName) ? "no class named" : "⚠ " + entry.typeName;
            GUILayout.Label(new GUIContent(type, entry.hasClass
                    ? entry.serviceType.FullName
                    : "The def names a class the project does not have."),
                entry.hasClass || entry.isKind ? EditorStyles.miniLabel : EditorStyles.miniBoldLabel,
                GUILayout.Width(150f));

            GUILayout.Label(Counts(entry), EditorStyles.miniLabel, GUILayout.Width(230f));

            GUILayout.Label(entry.installedIn.Count > 0
                    ? "⚙ " + string.Join(", ", entry.installedIn)
                    : "not installed in any scene",
                EditorStyles.centeredGreyMiniLabel);

            if (entry.sketch != null && GUILayout.Button(new GUIContent("sketch",
                "The sketch this def was generated from."), EditorStyles.miniButton,
                GUILayout.Width(54f)))
            {
                Selection.activeObject = entry.sketch;
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string Counts(SubsystemCatalog.Entry entry)
        {
            var parts = new List<string>();
            if (entry.requests > 0) parts.Add("asks " + entry.requests);
            if (entry.announcements > 0) parts.Add("says " + entry.announcements);
            if (entry.spawns > 0) parts.Add("shows " + entry.spawns);
            if (entry.attributes > 0) parts.Add("has " + entry.attributes);
            int declared = entry.serviceType != null
                ? ServiceSettings.DeclaredOn(entry.serviceType).Count : 0;
            if (declared > 0) parts.Add("tuned " + entry.settings + "/" + declared);
            return parts.Count > 0 ? string.Join(" · ", parts) : "declares nothing";
        }

        private static void DrawSketch(SubsystemSketch sketch)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(string.IsNullOrEmpty(sketch.serviceName)
                    ? "(unnamed)" : sketch.serviceName, AssetDatabase.GetAssetPath(sketch)),
                EditorStyles.linkLabel, GUILayout.Width(170f)))
            {
                Selection.activeObject = sketch;
                EditorGUIUtility.PingObject(sketch);
            }
            GUILayout.Label(sketch.scope.ToString(), EditorStyles.miniLabel, GUILayout.Width(150f));
            GUILayout.Label("asks " + sketch.requests.Count + " · says " + sketch.announcements.Count
                + " · tuned " + sketch.settings.Count, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void NewSketch()
        {
            string path = EditorUtility.SaveFilePanelInProject("New subsystem sketch",
                "SubsystemSketch", "asset", "Where the sketch lives. It stays after generation.");
            if (string.IsNullOrEmpty(path))
                return;
            var sketch = ScriptableObject.CreateInstance<SubsystemSketch>();
            sketch.serviceName = System.IO.Path.GetFileNameWithoutExtension(path)
                .Replace("Sketch", "").Replace("Subsystem", "").ToLowerInvariant();
            AssetDatabase.CreateAsset(sketch, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = sketch;
            Refresh();
        }
    }
}
