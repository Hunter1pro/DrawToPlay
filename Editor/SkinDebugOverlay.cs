using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Draws the skin binding of the selected shape on top of its art — the port of
    /// terrain_paint.gd's _draw_skin_debug (lines 757-794) and the "debug colors" half of
    /// milestone M3 (draw-tool-port-brief.md §8).
    ///
    /// The visual language is the original's, because it answers the only two questions that
    /// matter while rigging: WHICH bone owns a piece of the silhouette (every outline/lattice
    /// vertex gets a dot in its DOMINANT bone's palette colour, so the hand-over between bones
    /// reads as a colour boundary) and WHERE that bone actually is (each bone is a segment in
    /// the same colour from its origin to its rest tip, with a ringed joint dot). A dot whose
    /// colour does not match the bone lying under it is a mis-binding you can see without
    /// posing anything.
    ///
    /// State is a single EditorPrefs bool (<see cref="EnabledPrefKey"/>), public through
    /// <see cref="enabled"/> exactly like <see cref="CollisionDebugOverlay"/>, so the
    /// Draw-to-Play overlay panel and the Flow window can drive the same toggle.
    /// </summary>
    [InitializeOnLoad]
    public static class SkinDebugOverlay
    {
        /// <summary>EditorPrefs key backing <see cref="enabled"/>. Public because the overlay
        /// panel and Flow window drive the same toggle.</summary>
        public const string EnabledPrefKey = "PowerOfFire.DrawToPlay.DebugSkin";

        /// <summary>terrain_paint.gd _BONE_PALETTE (line 757), verbatim and in order — the
        /// colours are the shared vocabulary between the dots and the bone segments, so they
        /// are not "some distinct colours", they are THESE colours in THIS order.</summary>
        private static readonly Color[] k_BonePalette =
        {
            new Color(0.4f, 0.75f, 1f),
            new Color(1f, 0.6f, 0.3f),
            new Color(0.55f, 1f, 0.5f),
            new Color(1f, 0.45f, 0.85f),
            new Color(1f, 0.95f, 0.4f),
            new Color(0.7f, 0.55f, 1f)
        };

        // Godot draws into the viewport, so every radius/width below is in SCREEN pixels and
        // matches the original call site. Radii convert to world units per shape (the overlay
        // has to stay legible at any zoom); Handles line widths are already pixels.
        private const float k_VertexDotPixels = 2f;      // draw_circle(pt, 2.0, palette[best])
        private const float k_JointDotPixels = 5f;       // draw_circle(a, 5.0, col)
        private const float k_JointCorePixels = 2.2f;    // draw_circle(a, 2.2, near-black)
        private const float k_BoneLineWidth = 3f;        // draw_line(a, tip, col, 3.0, true)
        private const float k_BoneLineAlpha = 0.85f;     // Color(col, 0.85)

        /// <summary>The dark core of a joint dot — it is what keeps a joint readable when it
        /// sits on top of same-coloured vertex dots.</summary>
        private static readonly Color k_JointCoreColor = new Color(0.08f, 0.08f, 0.1f);

        /// <summary>Local Z the overlay is drawn at, matching CollisionDebugOverlay: the shape's
        /// layers recede in +Z with the outline at 0, so a small negative Z puts the overlay in
        /// front of the art. Belt-and-braces only — zTest is forced to Always while drawing.</summary>
        private const float k_OverlayZ = -0.001f;

        private static readonly List<DrawnShapeSkin> s_Skins = new List<DrawnShapeSkin>();
        private static bool s_SkinsValid;

        static SkinDebugOverlay()
        {
            // Defensive unsubscribe: a domain reload rebuilds the delegate list, but this also
            // makes the ctor safe if it is ever invoked twice.
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>Is the skin debug overlay drawing? Persisted in EditorPrefs, so it survives
        /// domain reloads and editor restarts like the rest of the tool state.</summary>
        public static bool enabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, false);
            set
            {
                if (EditorPrefs.GetBool(EnabledPrefKey, false) == value)
                    return;
                EditorPrefs.SetBool(EnabledPrefKey, value);
                s_SkinsValid = false;
                SceneView.RepaintAll();
            }
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            var currentEvent = Event.current;

            // One selection scan per GUI pass: the Layout event that precedes every Repaint
            // drops the cache and the Repaint refills it, which is also what picks up a
            // selection change without subscribing to Selection.selectionChanged.
            if (currentEvent.type == EventType.Layout)
                s_SkinsValid = false;

            if (currentEvent.type != EventType.Repaint || !enabled)
                return;

            if (!s_SkinsValid)
            {
                CollectSelectedSkins();
                s_SkinsValid = true;
            }

            if (s_Skins.Count == 0)
                return;

            // Debug geometry that can be hidden by the art it is drawn over is useless.
            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            try
            {
                for (int i = 0; i < s_Skins.Count; i++)
                {
                    var skin = s_Skins[i];
                    if (skin == null)
                        continue;
                    DrawSkin(skin);
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        /// <summary>Selected-shape scoped, like the Godot plugin's single _target. The one
        /// addition: selecting a BONE (or the Skeleton root) shows every shape bound to that
        /// rig. Godot never needed it — the plugin keeps drawing for the shape it tracks while
        /// you drag a bone in the scene tree — but Unity's Selection is the tracking here, so
        /// without this the overlay would blink off exactly while you pose the rig it is
        /// there to explain.</summary>
        private static void CollectSelectedSkins()
        {
            s_Skins.Clear();
            var selected = Selection.gameObjects;
            DrawnShapeSkin[] stageSkins = null;

            for (int i = 0; i < selected.Length; i++)
            {
                var gameObject = selected[i];
                if (gameObject == null)
                    continue;

                if (gameObject.TryGetComponent<DrawnShapeSkin>(out var skin))
                {
                    AddSkin(skin);
                    continue;
                }

                var rig = gameObject.GetComponentInParent<ShapeRig>();
                if (rig == null)
                    continue;

                if (stageSkins == null)
                {
                    // Stage-scoped rather than a global find: inside a prefab stage this
                    // returns the skins actually being edited (same rule as
                    // CollisionDebugOverlay's blob scan).
                    var stage = StageUtility.GetCurrentStageHandle();
                    stageSkins = stage.IsValid()
                        ? stage.FindComponentsOfType<DrawnShapeSkin>()
                        : System.Array.Empty<DrawnShapeSkin>();
                }

                for (int j = 0; j < stageSkins.Length; j++)
                {
                    if (stageSkins[j] != null && stageSkins[j].rig == rig)
                        AddSkin(stageSkins[j]);
                }
            }
        }

        private static void AddSkin(DrawnShapeSkin skin)
        {
            // Multi-select of a shape AND its rig would otherwise draw everything twice, which
            // doubles the alpha on the bone segments and reads as a different colour.
            if (skin != null && !s_Skins.Contains(skin))
                s_Skins.Add(skin);
        }

        private static void DrawSkin(DrawnShapeSkin skin)
        {
            var rig = skin.rig;
            if (rig == null || !skin.isSkinned)
                return;
            if (!skin.TryGetInfluences(out var vertices, out var dominantBone, out var boneNames))
                return;
            if (boneNames == null || boneNames.Count == 0)
                return;

            var shapeTransform = skin.transform;
            var planeOrigin = shapeTransform.position;
            var planeNormal = shapeTransform.forward.sqrMagnitude > 1e-8f
                ? shapeTransform.forward.normalized
                : Vector3.forward;
            float worldPerPixel = DrawToolSettings.WorldPerPixel(planeOrigin, planeOrigin, planeNormal);

            DrawVertexDots(shapeTransform, vertices, dominantBone, planeNormal, worldPerPixel);
            DrawBones(rig, boneNames, planeNormal, worldPerPixel);
        }

        /// <summary>One dot per skin vertex in its dominant bone's colour.
        ///
        /// The positions are the shape's UNDEFORMED local vertices, exactly like the original
        /// (Godot hands _draw_skin_debug the same _points() ring the mesh was built from), so
        /// on a posed rig the dots stay on the rest silhouette while the bone segments below
        /// move. That is the intended reading — dots answer "who owns this vertex", bones
        /// answer "where is that bone now" — and it is also the only honest option here: the
        /// pinned TryGetInfluences surface exposes the DOMINANT bone per vertex, not the top-2
        /// weights and bindposes a correct re-skin of the dots would need.</summary>
        private static void DrawVertexDots(Transform shapeTransform, List<Vector2> vertices,
            List<int> dominantBone, Vector3 planeNormal, float worldPerPixel)
        {
            // Godot guards the same way (weights row length == point count) because a stale
            // weight array against a freshly redrawn ring would mis-colour every dot.
            if (vertices == null || dominantBone == null || vertices.Count == 0
                || dominantBone.Count != vertices.Count)
                return;

            var localToWorld = shapeTransform.localToWorldMatrix;
            float radius = k_VertexDotPixels * worldPerPixel;

            using (new Handles.DrawingScope(Color.white))
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    int bone = dominantBone[i];
                    if (bone < 0)
                        continue;    // nothing weighted this vertex — leave it undrawn
                    Handles.color = k_BonePalette[bone % k_BonePalette.Length];
                    var local = new Vector3(vertices[i].x, vertices[i].y, k_OverlayZ);
                    Handles.DrawSolidDisc(localToWorld.MultiplyPoint3x4(local), planeNormal, radius);
                }
            }
        }

        /// <summary>Each bone as a segment from its origin to its tip plus a ringed joint dot,
        /// drawn from the LIVE bone Transforms (Godot uses bn.global_position /
        /// global_transform * (length, 0), i.e. the current pose, not the rest).
        ///
        /// The palette index is the bone's index in <paramref name="boneNames"/> — the same
        /// index space the dominant-bone indices above live in. That shared indexing is the
        /// whole trick: a dot's colour names the bone segment it belongs to.</summary>
        private static void DrawBones(ShapeRig rig, List<string> boneNames, Vector3 planeNormal,
            float worldPerPixel)
        {
            var asset = rig.rig;
            float jointRadius = k_JointDotPixels * worldPerPixel;
            float coreRadius = k_JointCorePixels * worldPerPixel;

            using (new Handles.DrawingScope(Color.white))
            {
                for (int i = 0; i < boneNames.Count; i++)
                {
                    var bone = rig.FindBone(boneNames[i]);
                    if (bone == null)
                        continue;    // named in the binding but missing from the scene rig

                    var color = k_BonePalette[i % k_BonePalette.Length];
                    int assetIndex = asset != null ? asset.IndexOf(boneNames[i]) : -1;
                    float length = assetIndex >= 0 ? asset.bones[assetIndex].length : 0f;
                    var start = Lift(bone.position);

                    if (length > 1e-6f)
                    {
                        // Local +X is the bone's own axis (rests are authored as a rotation
                        // about Z with the length along X), and TransformPoint carries the
                        // bone's scale exactly like Godot's global_transform * Vector2(len, 0).
                        var tip = Lift(bone.TransformPoint(new Vector3(length, 0f, 0f)));
                        Handles.color = new Color(color.r, color.g, color.b, k_BoneLineAlpha);
                        Handles.DrawAAPolyLine(k_BoneLineWidth, start, tip);
                    }

                    Handles.color = color;
                    Handles.DrawSolidDisc(start, planeNormal, jointRadius);
                    Handles.color = k_JointCoreColor;
                    Handles.DrawSolidDisc(start, planeNormal, coreRadius);
                }
            }
        }

        /// <summary>Nudge a world point toward the camera by the same step the shape-local
        /// vertices get, so bones and dots share one overlay plane.</summary>
        private static Vector3 Lift(Vector3 world)
        {
            world.z += k_OverlayZ;
            return world;
        }
    }
}
