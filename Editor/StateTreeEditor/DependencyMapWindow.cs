using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE MAP (M30.5) — the project's whole reference graph in one window, because "what points
    /// at this?" is the question this toolset answered worst.
    ///
    /// Every edge here is one somebody AUTHORED: a registry's Depends On, a def's catalog and the
    /// body it spawns, a tree's Data, a row's picked reference, a caller writing a request key.
    /// Nothing is inferred and nothing is invented — the map reads the same index the pickers and
    /// the ⛓ buttons read (<see cref="AssetWireScan"/>), so a map that shows an edge is a map of
    /// wires that exist, and one that shows a lonely asset is telling you something true.
    ///
    /// ARROWS POINT AT WHAT IS DEPENDED ON: A → B reads "A refers to B". Follow them forwards to
    /// see what something needs, backwards to see who would break if it went.
    ///
    /// It reads first and writes once: a registry or a def can DECLARE another catalog from here,
    /// which is the one edit that belongs on a map — the neighbourhood rule is what every picker
    /// in this toolset obeys, and this is where you can see that a neighbourhood is missing.
    /// </summary>
    internal sealed class DependencyMapWindow : EditorWindow
    {
        [MenuItem("Tools/Draw To Play/Dependency Map")]
        internal static void Open()
        {
            GetWindow<DependencyMapWindow>("Dependencies").Show();
        }

        private DependencyGraph m_Graph = new DependencyGraph();

        private Vector2 m_Pan;
        private Vector2 m_SidePanel;
        private string m_Search = "";
        private Object m_Selected;
        private Object m_Focus;
        private bool m_Built;

        private readonly bool[] m_Kinds = { true, true, true, true, true, true };
        private readonly bool[] m_EdgeKinds = { true, true, true };

        private void OnEnable()
        {
            m_Built = false;
            EditorApplication.projectChanged += Invalidate;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Invalidate;
        }

        private void Invalidate()
        {
            m_Built = false;
            Repaint();
        }

        private void OnGUI()
        {
            if (!m_Built)
                Rebuild();

            DrawToolbar();

            Rect side = new Rect(0f, EditorStyles.toolbar.fixedHeight, 260f,
                position.height - EditorStyles.toolbar.fixedHeight);
            Rect canvas = new Rect(side.xMax, side.y, position.width - side.width, side.height);

            DrawCanvas(canvas);
            DrawSide(side);
        }

        // ---- the index, as a graph -----------------------------------------------------

        private void Rebuild()
        {
            m_Graph = DependencyGraph.Build(AssetWireScan.Get());
            m_Built = true;
        }

        // ---- chrome --------------------------------------------------------------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                AssetWireScan.Invalidate();
                Invalidate();
            }

            GUILayout.Space(6f);
            for (int i = 0; i < m_Kinds.Length; i++)
            {
                m_Kinds[i] = GUILayout.Toggle(m_Kinds[i], ((DependencyGraph.NodeKind)i).ToString(),
                    EditorStyles.toolbarButton, GUILayout.Width(66f));
            }

            GUILayout.Space(6f);
            for (int i = 0; i < m_EdgeKinds.Length; i++)
            {
                m_EdgeKinds[i] = GUILayout.Toggle(m_EdgeKinds[i],
                    ((DependencyGraph.EdgeKind)i).ToString().ToLowerInvariant() + " edges",
                    EditorStyles.toolbarButton, GUILayout.Width(96f));
            }

            GUILayout.FlexibleSpace();
            if (m_Focus != null)
            {
                GUILayout.Label("focused on " + m_Focus.name, EditorStyles.miniLabel);
                if (GUILayout.Button("show all", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    m_Focus = null;
            }
            m_Search = GUILayout.TextField(m_Search, EditorStyles.toolbarSearchField,
                GUILayout.Width(180f));
            EditorGUILayout.EndHorizontal();
        }

        private bool Visible(DependencyGraph.Node node)
        {
            if (!m_Kinds[(int)node.kind])
                return false;
            if (m_Focus != null && !Near(node))
                return false;
            return string.IsNullOrEmpty(m_Search)
                || node.label.IndexOf(m_Search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Focus is a NEIGHBOURHOOD, not a node: the thing and everything one hop away,
        /// which is the answer to "what does this touch" — the whole project at once answers a
        /// different question and answers it badly.</summary>
        private bool Near(DependencyGraph.Node node)
        {
            if (node.asset == m_Focus)
                return true;
            return m_Graph.Touching(m_Graph.IndexOf(m_Focus), m_Graph.IndexOf(node.asset));
        }

        private void DrawCanvas(Rect canvas)
        {
            GUI.Box(canvas, GUIContent.none, EditorStyles.helpBox);
            GUI.BeginClip(canvas);
            Handles.BeginGUI();

            for (int i = 0; i < m_Graph.edges.Count; i++)
            {
                DependencyGraph.Edge edge = m_Graph.edges[i];
                if (!m_EdgeKinds[(int)edge.kind])
                    continue;
                DependencyGraph.Node from = m_Graph.nodes[edge.from];
                DependencyGraph.Node to = m_Graph.nodes[edge.to];
                if (!Visible(from) || !Visible(to))
                    continue;

                Vector2 a = new Vector2(from.rect.xMax, from.rect.center.y) + m_Pan;
                Vector2 b = new Vector2(to.rect.xMin, to.rect.center.y) + m_Pan;
                bool lit = m_Selected != null
                    && (from.asset == m_Selected || to.asset == m_Selected);
                Color colour = ColourOf(edge.kind);
                colour.a = lit ? 1f : 0.32f;
                Handles.DrawBezier(a, b, a + Vector2.right * 60f, b + Vector2.left * 60f,
                    colour, null, lit ? 3f : 1.6f);
            }

            Handles.EndGUI();

            for (int i = 0; i < m_Graph.nodes.Count; i++)
            {
                DependencyGraph.Node node = m_Graph.nodes[i];
                if (!Visible(node))
                    continue;
                Rect rect = node.rect;
                rect.position += m_Pan;

                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = node.asset == m_Selected
                    ? new Color(0.45f, 0.75f, 1f)
                    : ColourOf(node.kind);
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                GUI.backgroundColor = previous;

                var label = new GUIContent(node.label, node.type + "  ·  " + node.outgoing
                    + " out, " + node.incoming + " in");
                GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 16f), label,
                    EditorStyles.boldLabel);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 16f, rect.width - 12f, 16f),
                    node.type + "  ·  →" + node.outgoing + "  ←" + node.incoming,
                    EditorStyles.miniLabel);

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    m_Selected = node.asset;
                    EditorGUIUtility.PingObject(node.asset);
                    if (Event.current.clickCount > 1)
                        m_Focus = node.asset;
                    Event.current.Use();
                    Repaint();
                }
            }

            // Drag anywhere else pans: no zoom on purpose, because a map you can read is worth
            // more than one you can see all of at once.
            if (Event.current.type == EventType.MouseDrag && canvas.Contains(
                Event.current.mousePosition + canvas.position))
            {
                m_Pan += Event.current.delta;
                Event.current.Use();
                Repaint();
            }

            GUI.EndClip();
        }

        private static Color ColourOf(DependencyGraph.NodeKind kind)
        {
            switch (kind)
            {
                case DependencyGraph.NodeKind.Registry: return new Color(0.62f, 0.78f, 0.62f);
                case DependencyGraph.NodeKind.Def: return new Color(0.85f, 0.72f, 0.5f);
                case DependencyGraph.NodeKind.Tree: return new Color(0.68f, 0.68f, 0.9f);
                case DependencyGraph.NodeKind.Graph: return new Color(0.8f, 0.65f, 0.85f);
                case DependencyGraph.NodeKind.Prefab: return new Color(0.6f, 0.8f, 0.85f);
                default: return Color.white;
            }
        }

        private static Color ColourOf(DependencyGraph.EdgeKind kind)
        {
            switch (kind)
            {
                case DependencyGraph.EdgeKind.Row: return new Color(0.45f, 0.8f, 0.45f);
                case DependencyGraph.EdgeKind.Request: return new Color(0.95f, 0.65f, 0.3f);
                default: return new Color(0.6f, 0.7f, 0.95f);
            }
        }

        // ---- what the selection touches ------------------------------------------------

        private void DrawSide(Rect side)
        {
            GUILayout.BeginArea(side, EditorStyles.helpBox);
            m_SidePanel = EditorGUILayout.BeginScrollView(m_SidePanel);

            if (m_Selected == null)
            {
                EditorGUILayout.LabelField("Nothing selected", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(m_Graph.nodes.Count + " assets, " + m_Graph.edges.Count
                    + " authored edges.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Click a box to select and ping it; double-click to "
                    + "see only its neighbourhood. Arrows point at what is depended on.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            EditorGUILayout.LabelField(m_Selected.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(m_Selected.GetType().Name, EditorStyles.miniLabel);
            if (GUILayout.Button("Select in Project"))
                Selection.activeObject = m_Selected;

            DrawDeclare(m_Selected);

            int self = m_Graph.IndexOf(m_Selected);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Refers to", EditorStyles.boldLabel);
            DrawEdges(self, outgoing: true);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Referred to by", EditorStyles.boldLabel);
            DrawEdges(self, outgoing: false);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawEdges(int self, bool outgoing)
        {
            if (self < 0)
                return;
            var drawn = 0;
            for (int i = 0; i < m_Graph.edges.Count; i++)
            {
                DependencyGraph.Edge edge = m_Graph.edges[i];
                int other = outgoing
                    ? (edge.from == self ? edge.to : -1)
                    : (edge.to == self ? edge.from : -1);
                if (other < 0)
                    continue;

                drawn++;
                DependencyGraph.Node node = m_Graph.nodes[other];
                EditorGUILayout.BeginHorizontal();
                GUI.color = ColourOf(edge.kind);
                GUILayout.Label("■", GUILayout.Width(14f));
                GUI.color = Color.white;
                if (GUILayout.Button(new GUIContent(node.label
                        + (edge.count > 1 ? "  ×" + edge.count : ""), edge.first),
                    EditorStyles.miniButton))
                {
                    m_Selected = node.asset;
                    EditorGUIUtility.PingObject(node.asset);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (drawn == 0)
            {
                EditorGUILayout.LabelField(outgoing
                    ? "nothing — it stands alone"
                    : "nobody — nothing would break if it went",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>
        /// THE ONE EDIT A MAP SHOULD OFFER: declare a catalog. Every picker in this toolset
        /// offers what its owner declares, so "the menu is empty" is nearly always "the
        /// neighbourhood is missing" — and the map is where that is visible.
        /// </summary>
        private void DrawDeclare(Object selected)
        {
            var registry = selected as StateTreeRegistryAsset;
            var def = selected as ServiceDef;
            if (registry == null && def == null)
                return;

            if (!GUILayout.Button("+ declare a catalog…"))
                return;

            var menu = new GenericMenu();
            foreach (string guid in AssetDatabase.FindAssets("t:StateTreeRegistryAsset"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<StateTreeRegistryAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (candidate == null || candidate == selected)
                    continue;
                bool already = registry != null
                    ? registry.dependsOn.Contains(candidate)
                    : def.declares.Contains(candidate);
                if (already)
                {
                    menu.AddDisabledItem(new GUIContent(candidate.name + " (already)"));
                    continue;
                }
                StateTreeRegistryAsset target = candidate;
                menu.AddItem(new GUIContent(candidate.name), false, () =>
                {
                    Undo.RecordObject(selected, "Declare Catalog");
                    if (!DependencyGraph.Declare(selected, target))
                        return;
                    EditorUtility.SetDirty(selected);
                    AssetDatabase.SaveAssetIfDirty(selected);
                    AssetWireScan.Invalidate();
                    Invalidate();
                });
            }
            menu.ShowAsContext();
        }
    }
}
