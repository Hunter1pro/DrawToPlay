using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Draws the collision geometry a <see cref="TerrainBlob"/> actually handed to
    /// PhysicsCore2D on top of the art it was derived from — the Collision stage of the
    /// terrain flow (draw-tool-port-brief.md §6.4.4: "debug overlay draws the generated
    /// PhysicsCore2D geometry over the art so collision-vs-visual drift is visible").
    ///
    /// Drift is EXPECTED in M1 and this overlay is how you measure it: the rendered ring is
    /// wobbled (DrawnShapeRenderer runs DrawKit.Displace over the baked ring) while collision
    /// derives from the RAW ring, so the two disagree by roughly the edge-noise amplitude.
    ///
    /// Solid mode draws each convex piece of the decomposition in its own palette colour, so
    /// the split lines between pieces are readable. Chain mode draws each loop as a bold line
    /// with periodic normal ticks, because chains are ONE-SIDED — the tick is the only way to
    /// see which side of the loop actually collides.
    ///
    /// State is a single EditorPrefs bool (<see cref="EnabledPrefKey"/>) shared with the
    /// Draw-to-Play overlay panel and the Flow window's Collision stage, so all three toggle
    /// the same thing.
    /// </summary>
    [InitializeOnLoad]
    public static class CollisionDebugOverlay
    {
        /// <summary>EditorPrefs key backing <see cref="enabled"/>. Public because the Flow
        /// window's Collision stage drives the same toggle.</summary>
        public const string EnabledPrefKey = "PowerOfFire.DrawToPlay.DebugCollision";

        private const float k_PolygonOutlineWidth = 2f;
        private const float k_ChainWidth = 4f;
        private const float k_TickWidth = 2f;

        /// <summary>Fill alpha for Solid pieces: dark enough to read the piece boundaries,
        /// light enough that the art underneath stays visible (the whole point).</summary>
        private const float k_FillAlpha = 0.14f;

        /// <summary>Normal tick length in SCREEN pixels (converted per blob), so ticks stay
        /// readable at any zoom — the same screen-space sizing rule the M0 tools use.</summary>
        private const float k_TickPixels = 9f;

        /// <summary>One tick every N chain segments. Chain loops are baked at
        /// DrawnShapeAsset.bakeInterval, so per-segment ticks would be a solid smear.</summary>
        private const int k_TickSegmentStride = 4;

        /// <summary>Local Z the overlay is drawn at. DrawnShapeRenderer's layers recede in +Z
        /// with the outline at 0, so a small negative Z puts the overlay in front of the art.
        /// Belt-and-braces only: zTest is forced to Always while drawing.</summary>
        private const float k_OverlayZ = -0.001f;

        /// <summary>Cycled per polygon / per chain loop index. Chosen to stay distinct against
        /// each other and against the amber/red stroke colours in DrawToolSettings.</summary>
        private static readonly Color[] k_Palette =
        {
            new Color(0.35f, 0.85f, 1f),
            new Color(1f, 0.72f, 0.3f),
            new Color(0.55f, 1f, 0.45f),
            new Color(1f, 0.45f, 0.75f),
            new Color(0.75f, 0.6f, 1f),
            new Color(1f, 0.95f, 0.45f)
        };

        private static TerrainBlob[] s_Blobs = System.Array.Empty<TerrainBlob>();
        private static bool s_BlobsValid;

        static CollisionDebugOverlay()
        {
            // Defensive unsubscribe: a domain reload rebuilds the delegate list, but this also
            // makes the ctor safe if it is ever invoked twice.
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>Is the collision debug overlay drawing? Persisted in EditorPrefs, so it
        /// survives domain reloads and editor restarts like the rest of the tool state.</summary>
        public static bool enabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, false);
            set
            {
                if (EditorPrefs.GetBool(EnabledPrefKey, false) == value)
                    return;
                EditorPrefs.SetBool(EnabledPrefKey, value);
                s_BlobsValid = false;
                SceneView.RepaintAll();
            }
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            var currentEvent = Event.current;

            // One scene scan per GUI pass: the Layout event that precedes every Repaint drops
            // the cache, the Repaint refills it, and every blob drawn in that pass reuses it.
            if (currentEvent.type == EventType.Layout)
                s_BlobsValid = false;

            if (currentEvent.type != EventType.Repaint || !enabled)
                return;

            if (!s_BlobsValid)
            {
                // Stage-scoped rather than a global find: inside a prefab stage this returns
                // the blobs actually being edited, and in the main stage it is the open scenes.
                var stage = StageUtility.GetCurrentStageHandle();
                s_Blobs = stage.IsValid()
                    ? stage.FindComponentsOfType<TerrainBlob>()
                    : System.Array.Empty<TerrainBlob>();
                s_BlobsValid = true;
            }

            if (s_Blobs.Length == 0)
                return;

            // Debug geometry that can be hidden by the art it is drawn over is useless.
            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            try
            {
                for (int i = 0; i < s_Blobs.Length; i++)
                {
                    var blob = s_Blobs[i];
                    if (blob == null)
                        continue;
                    DrawBlob(blob);
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        private static void DrawBlob(TerrainBlob blob)
        {
            if (!blob.TryGetDerivedGeometry(out var polygons, out var chainLoops))
                return;

            var shapeTransform = blob.transform;

            // PHYSICS truth, not render truth. PhysicsCore2D poses a body from the Transform's
            // world position and its Z rotation only — there is no third axis and no scale in
            // 2D physics — and TerrainBlob bakes lossyScale into the vertices it derives. So
            // the pose below is position + Z rotation, NOT localToWorldMatrix: using the render
            // matrix would re-apply scale and quietly hide exactly the drift this overlay is
            // for. (Identical results in the common lossyScale == 1 case.)
            var pose = Matrix4x4.TRS(
                shapeTransform.position,
                Quaternion.Euler(0f, 0f, shapeTransform.eulerAngles.z),
                Vector3.one);

            if (polygons != null)
            {
                for (int i = 0; i < polygons.Count; i++)
                    DrawPolygon(polygons[i], pose, k_Palette[i % k_Palette.Length]);
            }

            if (chainLoops == null || chainLoops.Count == 0)
                return;

            // The physics pose is Z-rotation only, so its plane normal is always +Z regardless
            // of any (unsupported) X/Y rotation on the Transform.
            float worldPerPixel = DrawToolSettings.WorldPerPixel(shapeTransform.position,
                shapeTransform.position, Vector3.forward);
            float tickLength = k_TickPixels * worldPerPixel;

            for (int i = 0; i < chainLoops.Count; i++)
                DrawChainLoop(chainLoops[i], pose, k_Palette[i % k_Palette.Length], tickLength);
        }

        /// <summary>One convex piece of the Solid decomposition: alpha fill plus a crisp
        /// outline in the same hue.</summary>
        private static void DrawPolygon(Vector2[] ring, Matrix4x4 pose, Color color)
        {
            if (ring == null || ring.Length < 3)
                return;

            var points = ToWorld(ring, pose);

            // The closing duplicate at the end only costs the fan a zero-area triangle, so one
            // closed array serves both the fill and the outline.
            using (new Handles.DrawingScope(new Color(color.r, color.g, color.b, k_FillAlpha)))
                Handles.DrawAAConvexPolygon(points);

            using (new Handles.DrawingScope(color))
                Handles.DrawAAPolyLine(k_PolygonOutlineWidth, points);
        }

        /// <summary>One chain loop plus the normal ticks that show which side collides.</summary>
        private static void DrawChainLoop(Vector2[] loop, Matrix4x4 pose, Color color, float tickLength)
        {
            if (loop == null || loop.Length < 2)
                return;

            var points = ToWorld(loop, pose);

            using (new Handles.DrawingScope(color))
            {
                Handles.DrawAAPolyLine(k_ChainWidth, points);

                // Chains are one-sided and the collision normal points to the RIGHT of the
                // segment direction (unity-physicscore2d-chains-api: "A chain can have a
                // counter-clockwise winding order (normal points right of segment direction)").
                // Right of (dx, dy) in Unity's Y-up space is (dy, -dx), so the tick direction
                // falls straight out of the loop's winding: under the M1 contract (outer rings
                // positive area / CCW, holes negative / CW) it always points into free space,
                // away from the solid. A loop that comes back wound the other way ticks inward
                // — which is the bug this overlay exists to surface. Computing it from the
                // already-posed world points is safe because the pose is a rigid Z rotation.
                int stride = Mathf.Max(1, k_TickSegmentStride);
                for (int i = 0; i < loop.Length; i += stride)
                {
                    Vector3 start = points[i];
                    Vector3 end = points[i + 1];
                    Vector3 edge = end - start;
                    var normal = new Vector3(edge.y, -edge.x, 0f);
                    if (normal.sqrMagnitude < 1e-12f)
                        continue;

                    Vector3 mid = (start + end) * 0.5f;
                    Handles.DrawAAPolyLine(k_TickWidth, mid, mid + normal.normalized * tickLength);
                }
            }
        }

        /// <summary>Ring vertices (shape-local, scale already baked in by TerrainBlob) to world
        /// points, with the first vertex repeated so callers can treat the result as a closed
        /// line. Allocates per ring per repaint, which is fine for authoring-time debug draw.</summary>
        private static Vector3[] ToWorld(IReadOnlyList<Vector2> ring, Matrix4x4 pose)
        {
            var points = new Vector3[ring.Count + 1];
            for (int i = 0; i < ring.Count; i++)
                points[i] = pose.MultiplyPoint3x4(new Vector3(ring[i].x, ring[i].y, k_OverlayZ));
            points[points.Length - 1] = points[0];
            return points;
        }
    }
}
