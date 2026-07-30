using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Scene-view Draw tool — the Unity port of terrain_paint.gd's DRAW mode
    /// (_draw_input / _shape_input / _shape_poly / _finish_draw / _spawn_shape /
    /// _apply_stroke_poly). Strokes SCULPT the active <see cref="DrawnShapeRenderer"/>:
    /// overlapping the shape unions, Ctrl/Cmd carves, a stroke that misses (or lands fully
    /// inside) spawns a new sibling shape, and an empty shape adopts the stroke.
    /// One undo step per stroke gesture.
    ///
    /// Input mapping deviations from Godot (documented in the M0 report):
    ///  - Godot uses Shift for "carve". Unity reserves Shift for additive picking, so
    ///    SUBTRACT is Ctrl/Cmd + LMB drag; ADD is a plain LMB drag.
    ///  - In Circle/Rect mode Shift ALSO carves, which is the Godot gesture verbatim
    ///    (_shape_input line 623) and is what makes "Shift-drag inside a shape = hole".
    /// </summary>
    [EditorTool("Draw Shape")]
    public sealed class DrawShapeTool : EditorTool
    {
        private static readonly int s_ControlHint = "PowerOfFire.DrawToPlay.DrawShapeTool".GetHashCode();

        private const string k_DrawnAssetFolder = "Assets/DrawToPlay/Drawn";
        private const string k_DrawnAssetName = "DrawnShape";

        /// <summary>Mirror of <see cref="DrawnShapeAsset.bakeInterval"/>'s default; only used to
        /// bake an `existing` curve, so it is irrelevant when no asset exists yet.</summary>
        private const float k_DefaultBakeInterval = 0.125f;

        private readonly List<Vector2> m_StrokePoints = new List<Vector2>();

        private DrawnShapeRenderer m_ActiveShape;
        private Matrix4x4 m_ShapeToWorld = Matrix4x4.identity;
        private Matrix4x4 m_WorldToShape = Matrix4x4.identity;
        private Vector3 m_PlaneOrigin = Vector3.zero;
        private Vector3 m_PlaneNormal = Vector3.forward;

        private bool m_Drawing;
        private bool m_Shaping;
        private bool m_Subtract;
        private Vector2 m_ShapeAnchor;
        private Vector2 m_ShapeCursor;
        private Vector2 m_LastSampleGuiPoint;

        private GUIContent m_ToolbarIcon;

        public override GUIContent toolbarIcon => m_ToolbarIcon ??= DrawToolSettings.BuildToolbarIcon(
            "EditCollider",
            "Draw",
            "Draw Shape: drag to sculpt the selected drawn shape (Ctrl/Cmd = carve).");

        public override void OnActivated()
        {
            CacheShapeSpace(ResolveTarget());

            // A curve restored by undo lives in the asset; the derived mesh has to be rebuilt.
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            ResetGesture();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            var currentEvent = Event.current;
            var controlId = GUIUtility.GetControlID(s_ControlHint, FocusType.Passive);

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    // Claim the fallback control so a drag in empty space draws instead of
                    // rubber-band selecting (Godot consumes the same events in _draw_input).
                    HandleUtility.AddDefaultControl(controlId);
                    if (!m_Drawing && !m_Shaping)
                        CacheShapeSpace(ResolveTarget());
                    break;

                case EventType.MouseDown:
                    if (currentEvent.button != 0 || currentEvent.alt || HandleUtility.nearestControl != controlId)
                        break;
                    BeginGesture(currentEvent);
                    GUIUtility.hotControl = controlId;
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    UpdateGesture(currentEvent);
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    GUIUtility.hotControl = 0;
                    EndGesture();
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.KeyDown:
                    if (currentEvent.keyCode != KeyCode.Escape)
                        break;
                    // Godot: Esc cancels the in-flight gesture, else leaves draw mode.
                    if (m_Drawing || m_Shaping)
                    {
                        if (GUIUtility.hotControl == controlId)
                            GUIUtility.hotControl = 0;
                        ResetGesture();
                    }
                    else
                    {
                        Tools.current = Tool.Move;
                    }

                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.Repaint:
                    DrawPreview();
                    break;
            }
        }

        // --- gesture ----------------------------------------------------------------------

        private void BeginGesture(Event currentEvent)
        {
            m_ActiveShape = ResolveTarget();
            CacheShapeSpace(m_ActiveShape);

            var mode = DrawToolSettings.shapeMode;

            // Godot `_draw_subtract = event.shift_pressed`; remapped to Ctrl/Cmd here, with
            // Shift kept as a carve modifier for the geometric modes (see class summary).
            m_Subtract = currentEvent.control || currentEvent.command ||
                         (mode != DrawToolSettings.ShapeMode.Free && currentEvent.shift);

            var point = ScreenToShape(currentEvent.mousePosition);

            if (mode == DrawToolSettings.ShapeMode.Free)
            {
                m_Drawing = true;
                m_Shaping = false;
                m_StrokePoints.Clear();
                m_StrokePoints.Add(point);
                m_LastSampleGuiPoint = currentEvent.mousePosition;
            }
            else
            {
                m_Shaping = true;
                m_Drawing = false;
                m_StrokePoints.Clear();
                m_ShapeAnchor = point;
                m_ShapeCursor = point;
            }
        }

        private void UpdateGesture(Event currentEvent)
        {
            if (m_Shaping)
            {
                m_ShapeCursor = ScreenToShape(currentEvent.mousePosition);
                return;
            }

            if (!m_Drawing)
                return;

            // Godot samples when the cursor moved more than 1.5 screen px (_draw_input line 609).
            if (m_StrokePoints.Count > 0 &&
                Vector2.Distance(m_LastSampleGuiPoint, currentEvent.mousePosition) <= DrawToolSettings.StrokeSampleStepPixels)
                return;

            m_StrokePoints.Add(ScreenToShape(currentEvent.mousePosition));
            m_LastSampleGuiPoint = currentEvent.mousePosition;
        }

        private void EndGesture()
        {
            if (m_Shaping)
            {
                var mode = DrawToolSettings.shapeMode;
                var poly = BuildShapePoly(mode);
                m_Shaping = false;
                if (poly.Count >= 3)
                    ApplyStrokePoly(poly, mode == DrawToolSettings.ShapeMode.Rect); // Godot: rect = crisp
                return;
            }

            if (!m_Drawing)
                return;

            m_Drawing = false;
            FinishFreehand();
            m_StrokePoints.Clear();
        }

        private void ResetGesture()
        {
            m_Drawing = false;
            m_Shaping = false;
            m_StrokePoints.Clear();
        }

        /// <summary>Port of _finish_draw (closed-shape branch, lines 658-689): resample BEFORE
        /// smoothing so the auto-close chord becomes real points, then sculpt.</summary>
        private void FinishFreehand()
        {
            if (m_StrokePoints.Count < 3)
                return;

            var tolerances = TolerancesAtCentroid(m_StrokePoints);
            var spacing = Mathf.Max(tolerances.fit, tolerances.resampleFloor);

            var resampled = DrawKit.Resample(m_StrokePoints, spacing, true);
            if (resampled == null || resampled.Count < 3)
                return;

            var smoothed = DrawKit.Smooth(resampled, 3, true);
            if (smoothed == null || smoothed.Count < 3)
                return;

            ApplyStrokePoly(smoothed, false);
        }

        /// <summary>Port of _shape_poly (lines 640-655). Circle = 48-segment ring around the
        /// anchor with the radius under the cursor; Rect = the 4 drag-box corners. Godot has
        /// no aspect/square modifier here, so neither does this.</summary>
        private List<Vector2> BuildShapePoly(DrawToolSettings.ShapeMode mode)
        {
            var poly = new List<Vector2>();
            var worldPerPixel = DrawToolSettings.WorldPerPixel(ShapeToWorld(m_ShapeAnchor), m_PlaneOrigin, m_PlaneNormal);

            if (mode == DrawToolSettings.ShapeMode.Circle)
            {
                var radius = Vector2.Distance(m_ShapeAnchor, m_ShapeCursor);
                if (radius < DrawToolSettings.CircleMinRadiusPixels * worldPerPixel)
                    return poly;

                for (var i = 0; i < DrawToolSettings.CircleSegments; ++i)
                {
                    var t = Mathf.PI * 2f * i / DrawToolSettings.CircleSegments;
                    poly.Add(m_ShapeAnchor + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius);
                }

                return poly;
            }

            if (mode == DrawToolSettings.ShapeMode.Rect)
            {
                var min = Vector2.Min(m_ShapeAnchor, m_ShapeCursor);
                var max = Vector2.Max(m_ShapeAnchor, m_ShapeCursor);
                if ((max - min).magnitude < DrawToolSettings.RectMinDiagonalPixels * worldPerPixel)
                    return poly;

                poly.Add(min);
                poly.Add(new Vector2(max.x, min.y));
                poly.Add(max);
                poly.Add(new Vector2(min.x, max.y));
            }

            return poly;
        }

        // --- sculpt pipeline --------------------------------------------------------------

        /// <summary>Port of _apply_stroke_poly (lines 712-747): union / carve / hole / spawn,
        /// one undo step each, shared by freehand and the geometric modes.</summary>
        private void ApplyStrokePoly(IReadOnlyList<Vector2> strokePoly, bool crisp)
        {
            var tolerances = TolerancesAtCentroid(strokePoly);

            // Self-crossing strokes make unfittable bowties — clean them first.
            var stroke = DrawKit.CleanPolygon(strokePoly);
            if (stroke == null || stroke.Count < 3)
                return;

            // Force-new: skip all boolean logic, the stroke IS a new object.
            if (DrawToolSettings.forceNew && !m_Subtract)
            {
                SpawnFitted(stroke, tolerances.fit, crisp);
                return;
            }

            // Nothing selected: the first stroke ever becomes a brand new shape (Godot always
            // has a `_target`; here an empty selection takes the same spawn path).
            if (m_ActiveShape == null)
            {
                if (m_Subtract)
                    return;
                SpawnFitted(stroke, tolerances.fit, crisp);
                return;
            }

            var asset = m_ActiveShape.asset;
            var existing = asset != null ? asset.curve : null;
            var bakeInterval = asset != null ? asset.bakeInterval : k_DefaultBakeInterval;

            var result = DrawKit.MergeStroke(existing, stroke, m_Subtract, tolerances.fit, crisp, bakeInterval,
                insideAreaEpsilon: tolerances.insideAreaEpsilon,
                refitToleranceMin: tolerances.refitMin,
                refitToleranceMax: tolerances.refitMax);

            switch (result.op)
            {
                case DrawKit.StrokeOp.None:
                    return;

                // Union that missed, or union fully inside: a new sibling shape on top.
                case DrawKit.StrokeOp.Disjoint:
                case DrawKit.StrokeOp.Inside:
                    if (result.curve != null && result.curve.pointCount >= 3)
                        SpawnShape(result.curve);
                    return;

                case DrawKit.StrokeOp.Hole:
                {
                    if (result.curve == null || result.curve.pointCount < 3)
                        return;
                    var group = BeginUndoGroup("Punch Hole");
                    var holeAsset = EnsureAsset(m_ActiveShape);
                    if (holeAsset == null)
                        return;
                    Undo.RecordObject(holeAsset, "Punch Hole");
                    Undo.RecordObject(m_ActiveShape, "Punch Hole");
                    m_ActiveShape.AddHole(result.curve);
                    EditorUtility.SetDirty(holeAsset);
                    Undo.CollapseUndoOperations(group);
                    return;
                }

                // Carve removed everything. Godot adopts the empty curve (it never deletes the
                // node), so the Unity parity op is ClearGeometry.
                case DrawKit.StrokeOp.Cleared:
                {
                    var group = BeginUndoGroup("Carve Terrain");
                    var clearedAsset = EnsureAsset(m_ActiveShape);
                    if (clearedAsset == null)
                        return;
                    Undo.RecordObject(clearedAsset, "Carve Terrain");
                    Undo.RecordObject(m_ActiveShape, "Carve Terrain");
                    m_ActiveShape.ClearGeometry();
                    EditorUtility.SetDirty(clearedAsset);
                    Undo.CollapseUndoOperations(group);
                    return;
                }

                case DrawKit.StrokeOp.Adopt:
                case DrawKit.StrokeOp.Merged:
                {
                    if (result.curve == null || result.curve.pointCount < 3)
                        return;
                    var name = m_Subtract ? "Carve Terrain" : "Draw Terrain";
                    var group = BeginUndoGroup(name);
                    var targetAsset = EnsureAsset(m_ActiveShape);
                    if (targetAsset == null)
                        return;
                    Undo.RecordObject(targetAsset, name);
                    Undo.RecordObject(m_ActiveShape, name);
                    m_ActiveShape.AdoptCurve(result.curve);
                    EditorUtility.SetDirty(targetAsset);
                    Undo.CollapseUndoOperations(group);
                    return;
                }
            }
        }

        private void SpawnFitted(IReadOnlyList<Vector2> stroke, float tolerance, bool crisp)
        {
            var fitted = DrawKit.FitCurve(stroke, true, tolerance, crisp ? 0 : 3);
            if (fitted != null && fitted.pointCount >= 3)
                SpawnShape(fitted);
        }

        /// <summary>Port of _spawn_shape (lines 695-707) + curve_kit.spawn_like: a new sibling
        /// carrying the source shape's STYLE only (never its geometry), recentered so the
        /// gizmo sits on the shape, selected for immediate follow-up.</summary>
        private void SpawnShape(DrawnCurve fitted)
        {
            var group = BeginUndoGroup("Draw New Shape");

            var centroid = DrawKit.RecenterCurve(fitted);
            var source = m_ActiveShape != null ? m_ActiveShape.transform : null;

            var go = new GameObject(source != null ? source.name : "Drawn Shape");
            StageUtility.PlaceGameObjectInCurrentStage(go);
            if (source != null)
            {
                go.transform.SetParent(source.parent, false);
                go.transform.rotation = source.rotation;
                go.transform.localScale = source.localScale;
            }

            // Godot `node.position = _target.transform * centroid` — the centroid is in the
            // source's local space, so the new object lands on the drawn geometry.
            go.transform.position = source != null
                ? source.TransformPoint(centroid)
                : new Vector3(centroid.x, centroid.y, m_PlaneOrigin.z);

            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, "Draw New Shape");

            var renderer = go.AddComponent<DrawnShapeRenderer>();
            var asset = CreateShapeAsset(m_ActiveShape != null ? m_ActiveShape.asset : null);
            if (asset.curve == null)
                asset.curve = new DrawnCurve();
            asset.curve.CopyFrom(fitted);
            EditorUtility.SetDirty(asset);

            renderer.asset = asset;
            renderer.Regenerate();

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = go;
        }

        // --- assets -----------------------------------------------------------------------

        /// <summary>Style clone of `source` with geometry stripped (spawn_like semantics),
        /// saved under Assets/DrawToPlay/Drawn with a unique name. When `source` is null the
        /// asset keeps the DrawnShapeAsset defaults.</summary>
        private static DrawnShapeAsset CreateShapeAsset(DrawnShapeAsset source)
        {
            var asset = source != null
                ? Object.Instantiate(source)
                : ScriptableObject.CreateInstance<DrawnShapeAsset>();

            // spawn_like skips ["curve", "mask_png", "mask_rect", "hole_curves"].
            asset.curve = new DrawnCurve();
            asset.holeCurves = new List<DrawnCurve>();

            var folder = EnsureDrawnFolder();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{k_DrawnAssetName}.asset");
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Create every missing folder along Assets/DrawToPlay/Drawn.</summary>
        private static string EnsureDrawnFolder()
        {
            var segments = k_DrawnAssetFolder.Split('/');
            var path = segments[0]; // "Assets"
            for (var i = 1; i < segments.Length; ++i)
            {
                var next = $"{path}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, segments[i]);
                if (!AssetDatabase.IsValidFolder(next))
                    return path;
                path = next;
            }

            return path;
        }

        /// <summary>A renderer created from the menu starts without an asset; the first stroke
        /// that mutates it creates and assigns one (inside the stroke's undo group).</summary>
        private static DrawnShapeAsset EnsureAsset(DrawnShapeRenderer renderer)
        {
            if (renderer == null)
                return null;
            if (renderer.asset != null)
                return renderer.asset;

            var asset = CreateShapeAsset(null);
            Undo.RecordObject(renderer, "Create Drawn Shape Asset");
            renderer.asset = asset;
            EditorUtility.SetDirty(renderer);
            return asset;
        }

        // --- preview ----------------------------------------------------------------------

        /// <summary>Port of the stroke/shape preview in _forward_canvas_draw_over_viewport
        /// (lines 798-809 and 860-874).</summary>
        private void DrawPreview()
        {
            var color = m_Subtract ? DrawToolSettings.SubtractColor : DrawToolSettings.AddColor;

            if (m_Shaping)
            {
                var poly = BuildShapePoly(DrawToolSettings.shapeMode);
                if (poly.Count < 3)
                    return;

                var ring = new Vector3[poly.Count + 1];
                for (var i = 0; i < poly.Count; ++i)
                    ring[i] = ShapeToWorld(poly[i]);
                ring[poly.Count] = ring[0];

                using (new Handles.DrawingScope(color))
                    Handles.DrawAAPolyLine(2f, ring);
                return;
            }

            if (!m_Drawing || m_StrokePoints.Count < 2)
                return;

            var line = new Vector3[m_StrokePoints.Count];
            for (var i = 0; i < m_StrokePoints.Count; ++i)
                line[i] = ShapeToWorld(m_StrokePoints[i]);

            using (new Handles.DrawingScope(color))
            {
                Handles.DrawAAPolyLine(2f, line);

                // Start dot + the dashed closing chord: closed shapes auto-close on release.
                var worldPerPixel = DrawToolSettings.WorldPerPixel(line[0], m_PlaneOrigin, m_PlaneNormal);
                Handles.DrawSolidDisc(line[0], m_PlaneNormal, 4f * worldPerPixel);
                Handles.DrawDottedLine(line[line.Length - 1], line[0], 6f);
            }
        }

        // --- helpers ----------------------------------------------------------------------

        private static DrawnShapeRenderer ResolveTarget()
        {
            var active = Selection.activeGameObject;
            return active != null ? active.GetComponent<DrawnShapeRenderer>() : null;
        }

        /// <summary>Cache the stroke space for the whole gesture — Godot's `_view_xform()`
        /// includes the target's global transform, so points are recorded in shape-local space
        /// (world space when nothing is selected, ready for a later spawn).</summary>
        private void CacheShapeSpace(DrawnShapeRenderer renderer)
        {
            m_ActiveShape = renderer;
            if (renderer != null)
            {
                var transform = renderer.transform;
                m_ShapeToWorld = transform.localToWorldMatrix;
                m_WorldToShape = transform.worldToLocalMatrix;
                m_PlaneOrigin = transform.position;
                m_PlaneNormal = transform.forward.sqrMagnitude > 1e-8f ? transform.forward.normalized : Vector3.forward;
                return;
            }

            m_ShapeToWorld = Matrix4x4.identity;
            m_WorldToShape = Matrix4x4.identity;
            m_PlaneOrigin = Vector3.zero;
            m_PlaneNormal = Vector3.forward;
        }

        private Vector3 ShapeToWorld(Vector2 point) => m_ShapeToWorld.MultiplyPoint3x4(point);

        private Vector2 ScreenToShape(Vector2 guiPoint)
        {
            DrawToolSettings.TryScreenToPlane(guiPoint, m_PlaneOrigin, m_PlaneNormal, out var world);
            return m_WorldToShape.MultiplyPoint3x4(world);
        }

        /// <summary>World-unit tolerances measured at the stroke centroid — the Unity
        /// equivalent of `3.0 / zoom` at the place the user is actually drawing.</summary>
        private DrawToolSettings.Tolerances TolerancesAtCentroid(IReadOnlyList<Vector2> points)
        {
            var centroid = Vector2.zero;
            if (points != null && points.Count > 0)
            {
                for (var i = 0; i < points.Count; ++i)
                    centroid += points[i];
                centroid /= points.Count;
            }

            var worldPerPixel = DrawToolSettings.WorldPerPixel(ShapeToWorld(centroid), m_PlaneOrigin, m_PlaneNormal);
            return DrawToolSettings.TolerancesAt(worldPerPixel);
        }

        private static int BeginUndoGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        private static void OnUndoRedoPerformed()
        {
            // The asset's curve is restored by undo, but the derived mesh is not; rebuild the
            // shapes that are currently selected (the ones a stroke can have touched).
            foreach (var go in Selection.gameObjects)
            {
                if (go == null)
                    continue;
                var renderer = go.GetComponent<DrawnShapeRenderer>();
                if (renderer != null)
                    renderer.Regenerate();
            }

            SceneView.RepaintAll();
        }
    }
}
