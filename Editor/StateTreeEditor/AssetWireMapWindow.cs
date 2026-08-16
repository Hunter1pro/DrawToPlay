using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE GLOBAL WIRE MAP (phase 2, the UE reference-viewer shape): a FOCUS asset in the
    /// middle, who references it fanning LEFT, what it references fanning RIGHT — one layer
    /// at a time, each node carrying its own expander, so the map opens to deeper layers
    /// where you look instead of drawing the whole project at once.
    ///
    /// Reads <see cref="AssetWireScan"/> through <see cref="AssetWireGraph"/> — row wires
    /// roll up to the registry that owns the row, edges remember a sample wire for their
    /// tooltip. Plain UIElements (boxes + a painted edge layer) rather than an experimental
    /// graph framework: a VIEWER needs pan, zoom and boxes, not ports.
    /// </summary>
    internal sealed class AssetWireMapWindow : EditorWindow
    {
        private const float k_ColumnWidth = 280f;
        private const float k_NodeWidth = 220f;
        private const float k_NodeHeight = 52f;
        private const float k_RowGap = 16f;

        [SerializeField] private UnityEngine.Object m_Focus;

        /// <summary>Which nodes have their next layer OPEN, per direction. Cleared when the
        /// focus changes — a new question starts folded.</summary>
        private readonly HashSet<UnityEngine.Object> m_OpenLeft =
            new HashSet<UnityEngine.Object>();
        private readonly HashSet<UnityEngine.Object> m_OpenRight =
            new HashSet<UnityEngine.Object>();

        private VisualElement m_Canvas;
        private VisualElement m_EdgeLayer;
        private Vector2 m_Pan = new Vector2(60f, 40f);
        private float m_Zoom = 1f;
        private readonly List<(Vector2 from, Vector2 to)> m_EdgeLines =
            new List<(Vector2, Vector2)>();

        [MenuItem("Tools/Draw To Play/Asset Wire Map")]
        private static void Open()
        {
            GetWindow<AssetWireMapWindow>("Asset Wire Map");
        }

        private void CreateGUI()
        {
            rootVisualElement.Add(BuildToolbar());

            var viewport = new VisualElement();
            viewport.style.flexGrow = 1f;
            viewport.style.overflow = Overflow.Hidden;
            rootVisualElement.Add(viewport);

            m_Canvas = new VisualElement();
            m_Canvas.style.position = Position.Absolute;
            m_Canvas.style.transformOrigin = new TransformOrigin(0f, 0f);
            viewport.Add(m_Canvas);

            m_EdgeLayer = new VisualElement();
            m_EdgeLayer.style.position = Position.Absolute;
            m_EdgeLayer.pickingMode = PickingMode.Ignore;
            m_EdgeLayer.generateVisualContent += PaintEdges;
            m_Canvas.Add(m_EdgeLayer);

            // Pan: drag anywhere that is not a node. Zoom: the wheel, around the origin —
            // simple and predictable; the map recenters on focus change anyway.
            var dragging = false;
            viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == viewport || evt.target == m_Canvas
                    || evt.target == m_EdgeLayer)
                {
                    dragging = true;
                    viewport.CapturePointer(evt.pointerId);
                }
            });
            viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging)
                    return;
                m_Pan += (Vector2)evt.deltaPosition;
                ApplyTransform();
            });
            viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                dragging = false;
                if (viewport.HasPointerCapture(evt.pointerId))
                    viewport.ReleasePointer(evt.pointerId);
            });
            viewport.RegisterCallback<WheelEvent>(evt =>
            {
                m_Zoom = Mathf.Clamp(m_Zoom * (evt.delta.y < 0f ? 1.1f : 0.9f), 0.3f, 1.6f);
                ApplyTransform();
                evt.StopPropagation();
            });

            Rebuild();
        }

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 4f;
            bar.style.paddingTop = 2f;
            bar.style.paddingBottom = 2f;

            var focus = new ObjectField("Focus")
            {
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = false,
                value = m_Focus
            };
            focus.style.flexGrow = 1f;
            focus.tooltip = "The asset the map is ABOUT — a tree, a registry, a prefab, "
                + "a service definition (whose callers are its incoming edges). "
                + "Referencers fan left, references fan right.";
            focus.RegisterValueChangedCallback(evt => SetFocus(evt.newValue));
            bar.Add(focus);

            var fromSelection = new Button(() => SetFocus(Selection.activeObject))
            {
                text = "Use Selection"
            };
            fromSelection.tooltip = "Focus the asset selected in the Project window.";
            bar.Add(fromSelection);

            var rescan = new Button(() =>
            {
                AssetWireScan.Invalidate();
                Rebuild();
            }) { text = "Rescan" };
            rescan.tooltip = "Rebuild the wire index. Also happens on any project change.";
            bar.Add(rescan);

            return bar;
        }

        private void SetFocus(UnityEngine.Object next)
        {
            m_Focus = next;
            m_OpenLeft.Clear();
            m_OpenRight.Clear();
            m_Pan = new Vector2(60f, 40f);
            Rebuild();
        }

        private void ApplyTransform()
        {
            m_Canvas.style.translate = new Translate(m_Pan.x, m_Pan.y);
            m_Canvas.style.scale = new Scale(new Vector2(m_Zoom, m_Zoom));
        }

        // ------------------------------------------------------------------- the layout

        private sealed class MapNode
        {
            public UnityEngine.Object asset;
            public int depth;                 // negative = referencer side
            public Vector2 position;          // top-left, canvas space
            public List<AssetWireGraph.GraphEdge> incoming;
            public List<AssetWireGraph.GraphEdge> outgoing;
        }

        private void Rebuild()
        {
            if (m_Canvas == null)
                return;
            for (int i = m_Canvas.childCount - 1; i >= 0; i--)
            {
                if (m_Canvas[i] != m_EdgeLayer)
                    m_Canvas.RemoveAt(i);
            }
            m_EdgeLines.Clear();
            ApplyTransform();

            if (m_Focus == null)
            {
                var hint = new Label("Drop an asset into Focus (or select one and press "
                    + "Use Selection) — a tree, a registry, a prefab. ▸ on a node opens its "
                    + "next layer; drag pans, wheel zooms.");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.opacity = 0.7f;
                hint.style.maxWidth = 460f;
                m_Canvas.Add(hint);
                m_EdgeLayer.MarkDirtyRepaint();
                return;
            }

            AssetWireScan.Index index = AssetWireScan.Get();

            // BFS each side separately, one column per depth, opened nodes only.
            var nodes = new Dictionary<UnityEngine.Object, MapNode>();
            MapNode Node(UnityEngine.Object asset, int depth)
            {
                if (nodes.TryGetValue(asset, out MapNode held))
                    return held;
                var made = new MapNode
                {
                    asset = asset,
                    depth = depth,
                    incoming = AssetWireGraph.IncomingOf(index, asset),
                    outgoing = AssetWireGraph.OutgoingOf(index, asset)
                };
                nodes.Add(asset, made);
                return made;
            }

            MapNode focus = Node(m_Focus, 0);
            var edges = new List<(MapNode from, MapNode to)>();

            var leftQueue = new Queue<MapNode>();
            leftQueue.Enqueue(focus);
            while (leftQueue.Count > 0)
            {
                MapNode current = leftQueue.Dequeue();
                if (current.depth < 0 && !m_OpenLeft.Contains(current.asset))
                    continue;
                foreach (AssetWireGraph.GraphEdge edge in current.incoming)
                {
                    if (nodes.ContainsKey(edge.other))
                    {
                        edges.Add((nodes[edge.other], current));
                        continue;
                    }
                    MapNode other = Node(edge.other, current.depth - 1);
                    edges.Add((other, current));
                    leftQueue.Enqueue(other);
                }
            }

            var rightQueue = new Queue<MapNode>();
            rightQueue.Enqueue(focus);
            while (rightQueue.Count > 0)
            {
                MapNode current = rightQueue.Dequeue();
                if (current.depth > 0 && !m_OpenRight.Contains(current.asset))
                    continue;
                foreach (AssetWireGraph.GraphEdge edge in current.outgoing)
                {
                    if (nodes.ContainsKey(edge.other))
                    {
                        if (nodes[edge.other] != current)
                            edges.Add((current, nodes[edge.other]));
                        continue;
                    }
                    MapNode other = Node(edge.other, current.depth + 1);
                    edges.Add((current, other));
                    rightQueue.Enqueue(other);
                }
            }

            // Columns: x by depth, y stacked and centered per column.
            var columns = new Dictionary<int, List<MapNode>>();
            var minDepth = 0;
            foreach (MapNode node in nodes.Values)
            {
                if (!columns.TryGetValue(node.depth, out List<MapNode> column))
                {
                    column = new List<MapNode>();
                    columns.Add(node.depth, column);
                }
                column.Add(node);
                minDepth = Mathf.Min(minDepth, node.depth);
            }
            var tallest = 0;
            foreach (List<MapNode> column in columns.Values)
                tallest = Mathf.Max(tallest, column.Count);
            var mapHeight = tallest * (k_NodeHeight + k_RowGap);

            foreach (KeyValuePair<int, List<MapNode>> pair in columns)
            {
                List<MapNode> column = pair.Value;
                column.Sort((a, b) => string.CompareOrdinal(a.asset.name, b.asset.name));
                var columnHeight = column.Count * (k_NodeHeight + k_RowGap);
                for (int i = 0; i < column.Count; i++)
                {
                    column[i].position = new Vector2(
                        (pair.Key - minDepth) * k_ColumnWidth,
                        (mapHeight - columnHeight) * 0.5f
                            + i * (k_NodeHeight + k_RowGap));
                }
            }

            foreach ((MapNode from, MapNode to) in edges)
            {
                m_EdgeLines.Add((
                    from.position + new Vector2(k_NodeWidth, k_NodeHeight * 0.5f),
                    to.position + new Vector2(0f, k_NodeHeight * 0.5f)));
            }

            // The painted layer only renders inside its own rect — size it to the map.
            var bounds = Vector2.zero;
            foreach (MapNode node in nodes.Values)
            {
                bounds = Vector2.Max(bounds,
                    node.position + new Vector2(k_NodeWidth, k_NodeHeight));
            }
            m_EdgeLayer.style.width = bounds.x + 40f;
            m_EdgeLayer.style.height = bounds.y + 40f;

            foreach (MapNode node in nodes.Values)
                m_Canvas.Add(BuildNode(node, node == focus));
            m_EdgeLayer.MarkDirtyRepaint();
        }

        private VisualElement BuildNode(MapNode node, bool isFocus)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = node.position.x;
            box.style.top = node.position.y;
            box.style.width = k_NodeWidth;
            box.style.height = k_NodeHeight;
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            box.style.backgroundColor = isFocus
                ? new Color(0.24f, 0.37f, 0.28f)
                : new Color(0.22f, 0.22f, 0.22f);
            box.style.borderTopWidth = box.style.borderBottomWidth = 1f;
            box.style.borderLeftWidth = box.style.borderRightWidth = 1f;
            var border = isFocus
                ? new Color(0.45f, 0.75f, 0.5f)
                : new Color(0.45f, 0.45f, 0.45f);
            box.style.borderTopColor = box.style.borderBottomColor = border;
            box.style.borderLeftColor = box.style.borderRightColor = border;
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius = 4f;
            box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 4f;

            // ◂ opens THIS node's referencers (leftward growth); focus and left side only —
            // the right side asks the other question.
            var showLeft = (node.depth <= 0) && node.incoming.Count > 0 && !isFocus;
            if (showLeft)
            {
                var openLeft = m_OpenLeft.Contains(node.asset);
                var expander = new Button(() =>
                {
                    if (!m_OpenLeft.Remove(node.asset))
                        m_OpenLeft.Add(node.asset);
                    Rebuild();
                }) { text = openLeft ? "▸" : "◂" + node.incoming.Count };
                expander.tooltip = openLeft
                    ? "Collapse this node's referencers."
                    : node.incoming.Count + " referencer(s) — open the next layer left.";
                expander.style.flexShrink = 0f;
                box.Add(expander);
            }

            var icon = new Image
            {
                image = AssetPreview.GetMiniThumbnail(node.asset),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.style.width = 18f;
            icon.style.height = 18f;
            icon.style.flexShrink = 0f;
            icon.style.marginLeft = 3f;
            box.Add(icon);

            var title = new Label(node.asset.name + "\n" + KindLabel(node.asset));
            title.style.flexGrow = 1f;
            title.style.fontSize = 11f;
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.overflow = Overflow.Hidden;
            title.tooltip = TooltipFor(node) + "\n\nClick pings; double-click refocuses "
                + "the map here.";
            title.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                    SetFocus(node.asset);
                else
                    EditorGUIUtility.PingObject(node.asset);
            });
            box.Add(title);

            var showRight = (node.depth >= 0) && node.outgoing.Count > 0 && !isFocus;
            if (showRight)
            {
                var openRight = m_OpenRight.Contains(node.asset);
                var expander = new Button(() =>
                {
                    if (!m_OpenRight.Remove(node.asset))
                        m_OpenRight.Add(node.asset);
                    Rebuild();
                }) { text = openRight ? "◂" : node.outgoing.Count + "▸" };
                expander.tooltip = openRight
                    ? "Collapse this node's references."
                    : node.outgoing.Count + " reference(s) — open the next layer right.";
                expander.style.flexShrink = 0f;
                box.Add(expander);
            }
            return box;
        }

        private static string KindLabel(UnityEngine.Object asset)
        {
            switch (asset)
            {
                case StateTreeAsset _: return "state tree";
                case ProgressionTable _: return "progression";
                case StateTreeRegistryAsset _: return "registry";
                case GraphTaskAsset _: return "task graph";
                case GameObject _: return "prefab";
                default: return asset.GetType().Name;
            }
        }

        private static string TooltipFor(MapNode node)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(node.asset.name);
            if (node.incoming.Count > 0)
            {
                sb.Append("\n⟵ " + node.incoming.Count + " referencer(s), e.g. "
                    + node.incoming[0].sample);
            }
            if (node.outgoing.Count > 0)
            {
                sb.Append("\n⟶ " + node.outgoing.Count + " reference(s), e.g. "
                    + node.outgoing[0].sample);
            }
            return sb.ToString();
        }

        private void PaintEdges(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            painter.strokeColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            painter.lineWidth = 1.5f;
            foreach ((Vector2 from, Vector2 to) in m_EdgeLines)
            {
                painter.BeginPath();
                painter.MoveTo(from);
                var reach = Mathf.Max(30f, (to.x - from.x) * 0.45f);
                painter.BezierCurveTo(from + new Vector2(reach, 0f),
                    to - new Vector2(reach, 0f), to);
                painter.Stroke();
            }
        }
    }
}
