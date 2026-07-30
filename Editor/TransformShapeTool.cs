using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Spine-style transform gizmo for a <see cref="DrawnShapeRenderer"/> — the Unity port of
    /// terrain_paint.gd's MOVE mode (_move_input, _gizmo_pivot_view and the gizmo half of
    /// _forward_canvas_draw_over_viewport). The pivot sits at the SHAPE's baked-ring bounds
    /// center, not the object origin: drag anywhere inside to move, drag the 46 px ring to
    /// rotate about that pivot (Shift snaps 15°, exactly as Godot does), and the object's
    /// position is compensated so the shape spins in place. One undo step per drag.
    /// </summary>
    [EditorTool("Transform Drawn Shape", typeof(DrawnShapeRenderer))]
    public sealed class TransformShapeTool : EditorTool
    {
        private static readonly int s_ControlHint = "PowerOfFire.DrawToPlay.TransformShapeTool".GetHashCode();

        private Transform m_DragTransform;
        private bool m_Moving;
        private bool m_Rotating;

        private Vector3 m_PivotWorld;
        private Vector3 m_StartPosition;
        private Quaternion m_StartRotation;
        private Vector3 m_DragStartWorld;
        private float m_StartAngleDegrees;
        private float m_DeltaDegrees;
        private int m_UndoGroup;

        private Vector2 m_CachedLocalPivot;

        private GUIContent m_ToolbarIcon;

        public override GUIContent toolbarIcon => m_ToolbarIcon ??= DrawToolSettings.BuildToolbarIcon(
            "MoveTool",
            "Shape",
            "Transform Drawn Shape: drag to move, drag the ring to rotate about the shape center (Shift = 15°).");

        public override void OnWillBeDeactivated()
        {
            m_Moving = false;
            m_Rotating = false;
            m_DragTransform = null;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            var renderer = ResolveRenderer();
            if (renderer == null)
                return;

            var shapeTransform = renderer.transform;
            var planeOrigin = shapeTransform.position;
            var planeNormal = shapeTransform.forward.sqrMagnitude > 1e-8f
                ? shapeTransform.forward.normalized
                : Vector3.forward;

            var currentEvent = Event.current;
            var controlId = GUIUtility.GetControlID(s_ControlHint, FocusType.Passive);

            // Godot re-reads the pivot every frame; a baked ring is not free, so refresh it once
            // per GUI cycle (Layout) and freeze it for the duration of a rotation.
            if (currentEvent.type == EventType.Layout && !m_Rotating)
                RefreshLocalPivot(renderer);

            var pivotWorld = m_Rotating ? m_PivotWorld : shapeTransform.TransformPoint(m_CachedLocalPivot);
            var pivotGuiPoint = HandleUtility.WorldToGUIPoint(pivotWorld);
            var nearRing = Mathf.Abs(Vector2.Distance(currentEvent.mousePosition, pivotGuiPoint) -
                                     DrawToolSettings.GizmoRingPixels) < DrawToolSettings.GizmoRingPickPixels;

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    // Consume every click so the default picking/handles can't steal the drag
                    // (Godot: "consumes all clicks so the path vertex gizmo can't steal it").
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.MouseMove:
                    sceneView.Repaint();
                    break;

                case EventType.MouseDown:
                {
                    if (currentEvent.button != 0 || currentEvent.alt || HandleUtility.nearestControl != controlId)
                        break;

                    if (!DrawToolSettings.TryScreenToPlane(currentEvent.mousePosition, planeOrigin, planeNormal, out var world))
                        break;

                    m_DragTransform = shapeTransform;
                    m_StartPosition = shapeTransform.position;
                    m_StartRotation = shapeTransform.rotation;
                    m_DeltaDegrees = 0f;

                    if (nearRing)
                    {
                        m_Rotating = true;
                        m_Moving = false;
                        m_PivotWorld = pivotWorld;
                        m_StartAngleDegrees = PlaneAngleDegrees(world - m_PivotWorld);
                        m_UndoGroup = BeginUndoGroup("Rotate Shape");
                        Undo.RecordObject(shapeTransform, "Rotate Shape");
                    }
                    else
                    {
                        m_Moving = true;
                        m_Rotating = false;
                        m_DragStartWorld = world;
                        m_UndoGroup = BeginUndoGroup("Move Shape");
                        Undo.RecordObject(shapeTransform, "Move Shape");
                    }

                    GUIUtility.hotControl = controlId;
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;
                }

                case EventType.MouseDrag:
                {
                    if (GUIUtility.hotControl != controlId || m_DragTransform == null)
                        break;
                    if (!DrawToolSettings.TryScreenToPlane(currentEvent.mousePosition, planeOrigin, planeNormal, out var world))
                        break;

                    if (m_Rotating)
                    {
                        // Godot measures the angle in view space; measuring it on the drawing
                        // plane instead keeps the sign correct under Unity's Y-up world.
                        var delta = Mathf.DeltaAngle(m_StartAngleDegrees, PlaneAngleDegrees(world - m_PivotWorld));
                        if (currentEvent.shift)
                            delta = Mathf.Round(delta / DrawToolSettings.RotationSnapDegrees) *
                                    DrawToolSettings.RotationSnapDegrees;

                        var spin = Quaternion.AngleAxis(delta, planeNormal);
                        m_DragTransform.rotation = spin * m_StartRotation;
                        m_DragTransform.position = m_PivotWorld + spin * (m_StartPosition - m_PivotWorld);
                        m_DeltaDegrees = delta;
                    }
                    else if (m_Moving)
                    {
                        m_DragTransform.position = m_StartPosition + (world - m_DragStartWorld);
                    }

                    currentEvent.Use();
                    sceneView.Repaint();
                    break;
                }

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    GUIUtility.hotControl = 0;
                    if (m_Moving || m_Rotating)
                        Undo.CollapseUndoOperations(m_UndoGroup);
                    m_Moving = false;
                    m_Rotating = false;
                    m_DragTransform = null;
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.KeyDown:
                    if (currentEvent.keyCode != KeyCode.Escape || m_Moving || m_Rotating)
                        break;
                    Tools.current = Tool.Move;
                    currentEvent.Use();
                    break;

                case EventType.Repaint:
                    DrawGizmo(pivotWorld, planeNormal, planeOrigin, nearRing);
                    break;
            }
        }

        /// <summary>Port of _gizmo_pivot_view (lines 208-224): bounds center of the shape ring in
        /// local space, or the object origin when there is no usable ring.</summary>
        private void RefreshLocalPivot(DrawnShapeRenderer renderer)
        {
            m_CachedLocalPivot = Vector2.zero;

            if (!renderer.hasShape)
                return;

            var ring = renderer.GetBakedRing();
            if (ring == null || ring.Count < 2)
                return;

            var min = ring[0];
            var max = ring[0];
            for (var i = 1; i < ring.Count; ++i)
            {
                min = Vector2.Min(min, ring[i]);
                max = Vector2.Max(max, ring[i]);
            }

            m_CachedLocalPivot = (min + max) * 0.5f;
        }

        /// <summary>Port of the gizmo drawing in _forward_canvas_draw_over_viewport
        /// (lines 814-828): pivot halo + dot, the 46 px rotation ring, and the live angle
        /// readout while rotating.</summary>
        private void DrawGizmo(Vector3 pivotWorld, Vector3 planeNormal, Vector3 planeOrigin, bool nearRing)
        {
            var worldPerPixel = DrawToolSettings.WorldPerPixel(pivotWorld, planeOrigin, planeNormal);
            var hot = nearRing || m_Rotating;

            using (new Handles.DrawingScope(DrawToolSettings.PivotHaloColor))
                Handles.DrawSolidDisc(pivotWorld, planeNormal, DrawToolSettings.PivotHaloPixels * worldPerPixel);

            using (new Handles.DrawingScope(DrawToolSettings.PivotDotColor))
                Handles.DrawSolidDisc(pivotWorld, planeNormal, DrawToolSettings.PivotDotPixels * worldPerPixel);

            var ringRadius = DrawToolSettings.GizmoRingPixels * worldPerPixel;
            var ring = BuildRing(pivotWorld, planeNormal, ringRadius, 64);
            using (new Handles.DrawingScope(hot ? DrawToolSettings.RingHotColor : DrawToolSettings.RingColor))
                Handles.DrawAAPolyLine(hot ? 3f : 2f, ring);

            if (!m_Rotating)
                return;

            if (DrawToolSettings.TryScreenToPlane(Event.current.mousePosition, planeOrigin, planeNormal, out var cursor))
            {
                using (new Handles.DrawingScope(new Color(1f, 0.72f, 0.3f, 0.6f)))
                    Handles.DrawAAPolyLine(1.5f, pivotWorld, cursor);
            }

            using (new Handles.DrawingScope(DrawToolSettings.RingLabelColor))
                Handles.Label(pivotWorld + new Vector3((ringRadius + 10f * worldPerPixel), 6f * worldPerPixel, 0f),
                    $"{m_DeltaDegrees:F0}°");
        }

        private static Vector3[] BuildRing(Vector3 center, Vector3 normal, float radius, int segments)
        {
            // An orthonormal basis on the drawing plane: for a 2D shape this is world X/Y.
            var axisX = Vector3.right;
            if (Mathf.Abs(Vector3.Dot(normal, Vector3.forward)) < 0.999f)
            {
                axisX = Vector3.Cross(normal, Vector3.up);
                if (axisX.sqrMagnitude < 1e-8f)
                    axisX = Vector3.Cross(normal, Vector3.forward);
                if (axisX.sqrMagnitude < 1e-8f)
                    axisX = Vector3.right;
            }

            axisX.Normalize();
            var axisY = Vector3.Cross(normal, axisX).normalized;

            var points = new Vector3[segments + 1];
            for (var i = 0; i <= segments; ++i)
            {
                var t = Mathf.PI * 2f * i / segments;
                points[i] = center + (axisX * Mathf.Cos(t) + axisY * Mathf.Sin(t)) * radius;
            }

            return points;
        }

        private static float PlaneAngleDegrees(Vector3 offset) => Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;

        /// <summary>Single-target gizmo (Godot edits one node at a time): the first live
        /// DrawnShapeRenderer in the tool's targets.</summary>
        private DrawnShapeRenderer ResolveRenderer()
        {
            if (m_DragTransform != null && (m_Moving || m_Rotating))
            {
                var dragged = m_DragTransform.GetComponent<DrawnShapeRenderer>();
                if (dragged != null)
                    return dragged;
            }

            foreach (var candidate in targets)
            {
                if (candidate is DrawnShapeRenderer renderer && renderer != null && renderer.gameObject.activeInHierarchy)
                    return renderer;

                // Defensive: depending on how the selection was made the tool target can be the
                // GameObject rather than the component.
                if (candidate is GameObject go && go != null)
                {
                    var component = go.GetComponent<DrawnShapeRenderer>();
                    if (component != null && go.activeInHierarchy)
                        return component;
                }
            }

            return null;
        }

        private static int BeginUndoGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }
    }
}
