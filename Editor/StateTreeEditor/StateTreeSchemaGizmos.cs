using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The CONNECTION LAYER of a schema's mode: in the Scene view, each area a group carries
    /// (<see cref="IStateTreeArea"/> — a zone) as a circle, and a line from it to every manifest row a
    /// step targets (a placement parameter). Selecting a node in the window lights its marks;
    /// clicking a mark selects its node. In Play the active node's marks are green.
    ///
    /// The marks are computed when the window's tree, selection or data change — never per repaint,
    /// which only draws the cached list.
    /// </summary>
    [InitializeOnLoad]
    internal static class StateTreeSchemaGizmos
    {
        internal enum MarkKind
        {
            Area,
            Target
        }

        internal struct Mark
        {
            public MarkKind kind;
            public StateTreeNodeAsset node;
            public StateTreeNodeAsset area;
            public Vector3 position;
            public Vector3 from;
            public bool hasFrom;
            public float radius;
            public string label;
            public Color color;
        }

        private static readonly Color k_Selected = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color k_Active = new Color(0.4f, 0.95f, 0.4f, 1f);

        private static StateTreeEditorWindow s_Owner;
        private static List<Mark> s_Marks = new List<Mark>();
        private static StateTreeNodeAsset s_Selected;
        private static StateTreeNodeAsset s_Active;

        static StateTreeSchemaGizmos()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        internal static IReadOnlyList<Mark> marks => s_Marks;

        internal static void Show(StateTreeEditorWindow owner, StateTreeAsset tree, StateTreeSchema schema,
            StateTreeNodeAsset selected, StateTreeNodeAsset active)
        {
            s_Owner = owner;
            s_Marks = Collect(tree, schema);
            s_Selected = selected;
            s_Active = active;
            SceneView.RepaintAll();
        }

        internal static void Hide(StateTreeEditorWindow owner)
        {
            if (s_Owner != owner)
                return;
            s_Owner = null;
            s_Marks = new List<Mark>();
            s_Selected = null;
            s_Active = null;
            SceneView.RepaintAll();
        }

        /// <summary>Every mark the tree's domain nodes make, positions read off the level's manifest.</summary>
        internal static List<Mark> Collect(StateTreeAsset tree, StateTreeSchema schema)
        {
            var found = new List<Mark>();
            foreach (StateTreeNodeAsset top in StateTreeSchemaRules.DomainRoots(tree, schema))
                Walk(tree, schema, top, null, found, 0);
            return found;
        }

        private static void Walk(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            Mark? area, List<Mark> found, int depth)
        {
            if (node == null || depth > StateTreeEditorOps.DepthGuard)
                return;
            StateTreeSchemaKind kind = StateTreeSchemaRules.KindOf(schema, node);
            if (kind == null)
                return;

            if (StateTreeSchemaRules.ContextTask(kind, node) is IStateTreeArea zone
                && TryPlace(tree, schema, node, zone.areaCentre, out Vector3 centre))
            {
                var mark = new Mark
                {
                    kind = MarkKind.Area,
                    node = node,
                    area = node,
                    position = centre,
                    radius = Mathf.Max(0.1f, zone.areaRadius),
                    label = Label(kind, node),
                    color = kind.color
                };
                found.Add(mark);
                area = mark;
            }

            if (kind.isTemplate)
            {
                foreach (StateTreeStepParameter parameter in StateTreeSchemaRules.Parameters(kind, node))
                {
                    string target = parameter.isPlacement ? parameter.stringValue
                        : parameter.entryType == typeof(LevelObjectDef) ? parameter.stringValue
                        : null;
                    if (string.IsNullOrEmpty(target) || !TryPlace(tree, schema, node, target, out Vector3 at))
                        continue;
                    found.Add(new Mark
                    {
                        kind = MarkKind.Target,
                        node = node,
                        area = area?.node,
                        position = at,
                        from = area?.position ?? at,
                        hasFrom = area.HasValue,
                        label = Label(kind, node),
                        color = kind.color
                    });
                }
            }

            foreach (StateTreeNodeAsset child in StateTreeSchemaRules.DomainChildren(schema, node))
                Walk(tree, schema, child, area, found, depth + 1);
        }

        private static bool TryPlace(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            string placement, out Vector3 position)
        {
            position = default;
            LevelObjectDef row = StateTreeSchemaRules.Placement(tree, schema, node, placement,
                out LevelObjectRegistry manifest);
            if (row == null)
                return false;
            position = manifest.ToWorld(row.position);
            return true;
        }

        private static string Label(StateTreeSchemaKind kind, StateTreeNodeAsset node)
        {
            string name = string.IsNullOrEmpty(node.displayName) ? node.nodeId : node.displayName;
            return (string.IsNullOrEmpty(kind.icon) ? "" : kind.icon + " ") + name;
        }

        private static bool Lit(Mark mark, StateTreeNodeAsset which)
        {
            return which != null && (mark.node == which || mark.area == which);
        }

        private static void OnSceneGui(SceneView view)
        {
            if (s_Owner == null || s_Marks.Count == 0)
                return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int i = 0; i < s_Marks.Count; i++)
            {
                Mark mark = s_Marks[i];
                bool selected = Lit(mark, s_Selected);
                bool active = EditorApplication.isPlaying && Lit(mark, s_Active);
                Color color = active ? k_Active : selected ? k_Selected : mark.color;
                float thickness = selected || active ? 4f : 2f;
                Handles.color = color;

                if (mark.kind == MarkKind.Area)
                {
                    Handles.DrawWireDisc(mark.position, Vector3.up, mark.radius, thickness);
                    Handles.Label(mark.position + Vector3.up * 0.2f, mark.label, EditorStyles.boldLabel);
                    continue;
                }

                if (mark.hasFrom)
                    Handles.DrawLine(mark.from, mark.position, thickness);
                float size = HandleUtility.GetHandleSize(mark.position) * (selected ? 0.16f : 0.11f);
                if (Handles.Button(mark.position, Quaternion.identity, size, size, Handles.SphereHandleCap)
                    && s_Owner != null)
                {
                    s_Owner.SelectNode(mark.node.nodeId);
                    s_Owner.Focus();
                }
                if (selected)
                    Handles.Label(mark.position + Vector3.up * 0.35f, mark.label, EditorStyles.whiteBoldLabel);
            }
        }
    }
}
