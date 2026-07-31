using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Tessellates a DrawnShapeAsset into a renderable Mesh: fill (keyholed + ear-clipped,
    /// vertex-colored with fillShade), outline ribbon, rim strip, optional shadow — the
    /// Unity port of curve_shape_2d.gd's _draw path. Regenerates non-destructively from
    /// the asset; the generated mesh is never saved (HideFlags.DontSave).
    /// Editor tools talk to shapes exclusively through this component's public surface.
    ///
    /// Layering (Godot draw order): shadow, fill, rim, outline. Each layer owns a submesh
    /// with its own generated material at an increasing render queue, so the order holds
    /// regardless of how URP sorts; the tiny z steps below are belt-and-braces for a
    /// ZWrite-On material.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DrawnShapeRenderer : MonoBehaviour
    {
        // Names double as ownership marks: after a domain reload the (deliberately
        // unserialized) references below are null while the native objects still hang off
        // MeshFilter/MeshRenderer — matching by name adopts them back instead of leaking.
        private const string k_MeshName = "DrawnShape (generated)";
        private const string k_ShadowMaterialName = "DrawnShape Shadow (generated)";
        private const string k_FillMaterialName = "DrawnShape Fill (generated)";
        private const string k_LineMaterialName = "DrawnShape Lines (generated)";

        private const int k_ShadowSubmesh = 0;
        private const int k_FillSubmesh = 1;
        private const int k_LineSubmesh = 2;
        private const int k_SubmeshCount = 3;

        // Layers recede behind the transform plane; the outline sits exactly on it.
        private const float k_LayerZStep = 0.0005f;
        private const float k_ShadowZ = 3f * k_LayerZStep;
        private const float k_FillZ = 2f * k_LayerZStep;
        private const float k_RimZ = 1f * k_LayerZStep;
        private const float k_OutlineZ = 0f;

        // 1 Unity world unit == 32 Godot px (DrawnShapeAsset): conversion for the literal
        // pixel constants baked into curve_shape_2d.gd's _draw.
        private const float k_PixelToUnit = 1f / 32f;

        // Ear-clipping tolerance in world units: above float32 noise at ~100 units, far
        // below DrawKit.Keyhole's ~0.0016-unit bridge offset.
        private const float k_GeometryEpsilon = 1e-5f;

        private const int k_TransparentQueue = 3000;

        // Unlit, transparent, vertex-coloured. "Sprites/Default" is a built-in shader whose
        // only pass carries no LightMode tag, so URP's 2D renderer draws it through its
        // SRPDefaultUnlit pass (Renderer2D/DrawRenderer2DPass), it multiplies the vertex
        // colour into _MainTex, and it is Cull Off / ZWrite Off — exactly what these meshes
        // need. The URP sprite shader is the fallback. NOTE for players: a code-found
        // built-in shader must be listed under Graphics > Always Included Shaders to survive
        // a build; M0 is an authoring-time milestone so nothing depends on that yet.
        private static readonly string[] k_ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/2D/Sprite-Unlit-Default"
        };

        public DrawnShapeAsset asset;

        /// <summary>True when the asset holds a usable outline (>= 3 control points).</summary>
        public bool hasShape => asset != null && asset.curve != null && asset.curve.pointCount >= 3;

        private MeshFilter m_MeshFilter;
        private MeshRenderer m_MeshRenderer;
        private Mesh m_Mesh;
        private Material m_ShadowMaterial;
        private Material m_FillMaterial;
        private Material m_LineMaterial;
        private ShapeTessellator.MeshBuilder m_Builder;
        private readonly List<IReadOnlyList<Vector2>> m_Holes = new List<IReadOnlyList<Vector2>>();
        private readonly List<int> m_FillTriangles = new List<int>();
        private readonly List<Color> m_FillColors = new List<Color>();
        private readonly List<Vector2> m_RimRun = new List<Vector2>();

        private void OnEnable()
        {
            Regenerate();
        }

        // Also the editor entry point: without [ExecuteAlways] OnEnable does not run in edit
        // mode, but OnValidate does (script load, scene open, inspector edit). It can fire
        // before OnEnable, so Regenerate resolves everything it needs itself; nothing here
        // defers through UnityEditor (runtime asmdef) and nothing destroys objects, which is
        // what makes it safe from inside OnValidate.
        private void OnValidate()
        {
            Regenerate();
        }

        private void OnDestroy()
        {
            ReleaseGenerated();
        }

        /// <summary>Rebuild the mesh from the asset. Safe to call repeatedly.</summary>
        /// <summary>Raised after every Regenerate — derived consumers (physics, skinning)
        /// rebuild from the new geometry here. The editor tools funnel every sculpt gesture
        /// through Regenerate, so subscribing gives live regen-on-sculpt for free.</summary>
        public event System.Action geometryChanged;

        public void Regenerate()
        {
            if (!EnsureComponents())
                return;
            if (m_Builder == null)
                m_Builder = new ShapeTessellator.MeshBuilder();
            m_Builder.Clear();
            m_Builder.EnsureSubmeshCount(k_SubmeshCount);
            BuildGeometry(m_Builder);
            var mesh = EnsureMesh();
            m_Builder.ToMesh(mesh);
            if (m_MeshFilter.sharedMesh != mesh)
                m_MeshFilter.sharedMesh = mesh;
            EnsureMaterials();
            geometryChanged?.Invoke();
        }

        /// <summary>Replace the outline with a fitted curve and regenerate — port of
        /// curve_shape_2d.gd adopt_curve. Does NOT touch holes or style.</summary>
        public void AdoptCurve(DrawnCurve fitted)
        {
            if (asset == null)
                return;
            // Godot swaps the Curve2D resource in wholesale; the asset takes ownership of
            // the instance the caller fitted.
            asset.curve = fitted ?? new DrawnCurve();
            Regenerate();
        }

        /// <summary>Add a hole ring (MergeStroke Hole op) and regenerate.</summary>
        public void AddHole(DrawnCurve hole)
        {
            if (asset == null || hole == null || hole.pointCount < 3)
                return;
            if (asset.holeCurves == null)
                asset.holeCurves = new List<DrawnCurve>();
            asset.holeCurves.Add(hole);
            Regenerate();
        }

        /// <summary>Remove outline and holes (MergeStroke Cleared op) and regenerate.</summary>
        public void ClearGeometry()
        {
            if (asset == null)
                return;
            if (asset.curve != null)
                asset.curve.Clear();
            if (asset.holeCurves != null)
                asset.holeCurves.Clear();
            Regenerate();
        }

        /// <summary>The raw baked outline ring in local space — closing duplicate removed,
        /// pre-wobble (sculpt booleans operate on true geometry; Displace is render-only).
        /// Empty list when there is no shape. Port of curve_shape_2d.gd _ring_of.</summary>
        public List<Vector2> GetBakedRing()
        {
            if (!hasShape)
                return new List<Vector2>();
            return BuildBakedRing(asset.curve, BakeInterval());
        }

        private float BakeInterval()
        {
            return Mathf.Max(asset.bakeInterval, 1e-4f);
        }

        /// <summary>Port of _ring_of: bake, then drop the point that duplicates the start on a
        /// manually closed loop. Godot compares against 0.5 px at a 4 px bake interval — the
        /// same 0.125 ratio DrawKit.MergeStroke uses, so sculpt and render see identical rings.</summary>
        private static List<Vector2> BuildBakedRing(DrawnCurve curve, float interval)
        {
            var points = curve != null ? curve.GetBakedPoints(interval) : null;
            if (points == null)
                return new List<Vector2>();
            float epsilon = Mathf.Max(interval * 0.125f, 1e-6f);
            if (points.Count > 1
                && (points[0] - points[points.Count - 1]).sqrMagnitude < epsilon * epsilon)
                points.RemoveAt(points.Count - 1);
            return points;
        }

        // --- geometry ---------------------------------------------------------------

        /// <summary>Blend factor along the form chain [base, target1, target2, ...] —
        /// Godot CurveShape2D.morph. 0 = the drawn base; N = fully morph target N.
        /// Animated per-frame by PoseAnimator, hence a serialized component field
        /// (the asset holds the TARGETS, the scene holds the current blend).</summary>
        [Range(0f, 8f)] public float morphWeight;

        /// <summary>The RENDER ring — port of _points(): baked outline → form-morph blend
        /// (_morphed_ring: rings resampled to a matched count and re-anchored via
        /// DrawKit.AlignRing, so redrawing a form never breaks interpolation) → edge
        /// wobble. GetBakedRing stays raw (sculpt/collision see true geometry); THIS is
        /// the single source for every deformable render consumer (fill, skin, paint).</summary>
        public List<Vector2> BuildRenderRing()
        {
            var style = asset;
            if (style == null)
                return null;
            float interval = BakeInterval();
            var raw = BuildBakedRing(style.curve, interval);
            if (raw.Count < 3)
                return raw;

            var morphed = MorphedRing(raw, style, interval);
            return DrawKit.Displace(morphed, style.edgeNoiseAmp, style.edgeNoiseWavelength,
                style.edgeNoiseSeed, true);
        }

        /// <summary>Port of _morphed_ring (curve_shape_2d.gd lines 432-449), verbatim
        /// constants: matched count clamped [16, 128], positional correspondence.</summary>
        private List<Vector2> MorphedRing(List<Vector2> baseRing, DrawnShapeAsset style, float interval)
        {
            var targets = style.morphTargets;
            if (morphWeight <= 0f || targets == null || targets.Count == 0)
                return baseRing;
            float m = Mathf.Clamp(morphWeight, 0f, targets.Count);
            int seg = Mathf.Min(Mathf.FloorToInt(m), targets.Count - 1);
            float t = m - seg;
            var ringA = seg == 0 ? baseRing : BuildBakedRing(targets[seg - 1], interval);
            var ringB = BuildBakedRing(targets[seg], interval);
            if (ringA.Count < 3 || ringB.Count < 3)
                return baseRing;
            if (m >= targets.Count)
                return ringB;
            int n = Mathf.Clamp(Mathf.Max(ringA.Count, ringB.Count), 16, 128);
            var ar = DrawKit.ResampleCount(ringA, n);
            var br = DrawKit.AlignRing(ar, DrawKit.ResampleCount(ringB, n));
            var blended = new List<Vector2>(n);
            for (int k = 0; k < n; k++)
                blended.Add(Vector2.LerpUnclamped(ar[k], br[k], t));
            return blended;
        }

        private void BuildGeometry(ShapeTessellator.MeshBuilder builder)
        {
            if (!hasShape)
                return;
            var style = asset;
            float interval = BakeInterval();
            var outer = BuildRenderRing();
            if (outer == null || outer.Count < 3)
                return;

            CollectHoles(outer, interval);
            var fill = m_Holes.Count > 0 ? DrawKit.Keyhole(outer, m_Holes) : outer;
            if (fill == null || fill.Count < 3)
                fill = outer;

            float uvScale = Mathf.Max(style.textureScale, 1e-4f);
            ShapeTessellator.Triangulate(fill, m_FillTriangles, k_GeometryEpsilon);

            // shadow silhouette — same triangulation, translated (Godot re-fills the offset
            // ring; a pure translation cannot change the tessellation)
            builder.currentSubmesh = k_ShadowSubmesh;
            if (style.shadowColor.a > 0f)
                ShapeTessellator.AppendTriangles(builder, fill, m_FillTriangles,
                    style.shadowOffset, k_ShadowZ, ToVertexColor(style.shadowColor), uvScale);

            builder.currentSubmesh = k_FillSubmesh;
            BuildFillColors(fill, m_FillColors);
            ShapeTessellator.AppendTriangles(builder, fill, m_FillTriangles, Vector2.zero,
                k_FillZ, m_FillColors, uvScale);

            builder.currentSubmesh = k_LineSubmesh;
            if (style.rimWidth > 0f)
                AppendRim(builder, outer, interval, uvScale);
            if (style.outlineWidth > 0f)
                ShapeTessellator.AppendRibbon(builder, outer, true, style.outlineWidth,
                    k_OutlineZ, ToVertexColor(style.outlineColor), uvScale);
        }

        /// <summary>Baked + wobbled hole rings, kept only while they stay inside the outline.
        /// Port of terrain_blob_2d.gd _solid_poly's hole loop (curve_shape_2d.gd itself has no
        /// holes): each hole is displaced with edgeNoiseSeed + its 1-based index, so holes
        /// wobble independently of the outline and of each other.</summary>
        private void CollectHoles(IReadOnlyList<Vector2> outer, float interval)
        {
            m_Holes.Clear();
            var holeCurves = asset.holeCurves;
            if (holeCurves == null)
                return;
            var style = asset;
            int index = 0;
            for (int i = 0; i < holeCurves.Count; i++)
            {
                // Godot increments before skipping, so a null/short entry still consumes a
                // seed offset and the surviving holes keep their noise.
                index++;
                var holeCurve = holeCurves[i];
                if (holeCurve == null || holeCurve.pointCount < 3)
                    continue;
                var ring = BuildBakedRing(holeCurve, interval);
                if (ring.Count < 3)
                    continue;
                var displaced = DrawKit.Displace(ring, style.edgeNoiseAmp,
                    style.edgeNoiseWavelength, style.edgeNoiseSeed + index, true);
                if (displaced == null || displaced.Count < 3)
                    continue;
                // Godot clips the hole to a 1 px inset of the outline first (a ring touching
                // the edge makes the keyholed polygon self-intersect). PolyBool's M0 surface
                // has no polygon offset/intersection, so a hole that is not fully inside is
                // dropped instead of clipped — the MergeStroke Hole op only ever produces
                // fully-interior rings.
                if (!IsRingInside(displaced, outer))
                    continue;
                m_Holes.Add(displaced);
            }
        }

        private static bool IsRingInside(IReadOnlyList<Vector2> ring, IReadOnlyList<Vector2> outer)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                if (!PolyBool.IsPointInPolygon(ring[i], outer))
                    return false;
            }
            return true;
        }

        /// <summary>Port of _vertex_colors: flat fillColor (white when a fill texture drives
        /// the colour), or a vertical luminance gradient when fillShade > 0. Godot works in
        /// Y-down, so its f == 0 is the TOP of the shape; here f is measured from maxY down,
        /// which keeps the documented "top lighter, bottom darker" result in Unity's Y-up
        /// space.</summary>
        private void BuildFillColors(IReadOnlyList<Vector2> points, List<Color> colors)
        {
            colors.Clear();
            var style = asset;
            Color baseColor = style.fillTexture != null ? Color.white : style.fillColor;
            if (style.fillShade <= 0f)
            {
                Color flat = ToVertexColor(baseColor);
                for (int i = 0; i < points.Count; i++)
                    colors.Add(flat);
                return;
            }
            float minY = points[0].y;
            float maxY = points[0].y;
            for (int i = 1; i < points.Count; i++)
            {
                minY = Mathf.Min(minY, points[i].y);
                maxY = Mathf.Max(maxY, points[i].y);
            }
            // Godot floors the span at 1 px purely to avoid a divide by zero
            float span = Mathf.Max(maxY - minY, k_PixelToUnit);
            float top = 1f + style.fillShade * 0.25f;
            float bottom = 1f - style.fillShade * 0.5f;
            for (int i = 0; i < points.Count; i++)
            {
                float f = (maxY - points[i].y) / span;
                float lum = Mathf.Lerp(top, bottom, f);
                colors.Add(ToVertexColor(new Color(baseColor.r * lum, baseColor.g * lum,
                    baseColor.b * lum, baseColor.a)));
            }
        }

        /// <summary>Port of the rim pass in _draw: consecutive outline edges whose outward
        /// normal points up (dot with up > 0.45) and whose midpoint probe lands OUTSIDE the
        /// shape become one open stroke of width rimWidth, centred on the edge exactly like
        /// Godot's draw_polyline. Drawn before the outline, so a rim narrower than the outline
        /// is hidden by it — same as the original.</summary>
        private void AppendRim(ShapeTessellator.MeshBuilder builder, IReadOnlyList<Vector2> points,
            float interval, float uvScale)
        {
            int n = points.Count;
            if (n < 3)
                return;
            var style = asset;
            Color color = ToVertexColor(style.rimColor);
            // Godot probes 1 px above the edge midpoint; at the default bake interval this is
            // exactly 1 Godot px (0.03125 world units) and it scales with the asset.
            float probe = Mathf.Max(interval * 0.25f, 1e-4f);
            m_RimRun.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % n];
                Vector2 edge = b - a;
                Vector2 normal = new Vector2(edge.y, -edge.x);
                float length = normal.magnitude;
                bool upFacing = false;
                if (length > 1e-9f)
                {
                    normal /= length;
                    if (normal.y < 0f)
                        normal = -normal;    // Y-up counterpart of Godot's "if n.y > 0: n = -n"
                    Vector2 mid = (a + b) * 0.5f + new Vector2(0f, probe);
                    upFacing = normal.y > 0.45f && !PolyBool.IsPointInPolygon(mid, points);
                }
                if (upFacing)
                {
                    if (m_RimRun.Count == 0)
                        m_RimRun.Add(a);
                    m_RimRun.Add(b);
                    continue;
                }
                FlushRim(builder, style.rimWidth, color, uvScale);
            }
            FlushRim(builder, style.rimWidth, color, uvScale);
        }

        private void FlushRim(ShapeTessellator.MeshBuilder builder, float width, Color color,
            float uvScale)
        {
            if (m_RimRun.Count > 1)
                ShapeTessellator.AppendRibbon(builder, m_RimRun, false, width, k_RimZ, color,
                    uvScale);
            m_RimRun.Clear();
        }

        /// <summary>Mesh vertex colours bypass Unity's colour-space conversion, so convert
        /// explicitly to keep an authored colour looking the same in a linear project. No-op
        /// in gamma projects (the Sandbox is gamma).</summary>
        private static Color ToVertexColor(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        // --- generated objects ------------------------------------------------------

        private bool EnsureComponents()
        {
            if (m_MeshFilter == null)
                TryGetComponent(out m_MeshFilter);
            if (m_MeshRenderer == null)
                TryGetComponent(out m_MeshRenderer);
            return m_MeshFilter != null && m_MeshRenderer != null;
        }

        private Mesh EnsureMesh()
        {
            if (m_Mesh != null)
                return m_Mesh;
            var existing = m_MeshFilter.sharedMesh;
            if (existing != null && existing.name == k_MeshName)
            {
                m_Mesh = existing;
                return m_Mesh;
            }
            m_Mesh = new Mesh
            {
                name = k_MeshName,
                hideFlags = HideFlags.DontSave
            };
            return m_Mesh;
        }

        private void EnsureMaterials()
        {
            var shader = ResolveShader();
            if (shader == null)
                return;
            int baseQueue = shader.renderQueue;
            if (baseQueue < 0)
                baseQueue = k_TransparentQueue;
            m_ShadowMaterial = EnsureMaterial(m_ShadowMaterial, k_ShadowSubmesh,
                k_ShadowMaterialName, shader, baseQueue);
            m_FillMaterial = EnsureMaterial(m_FillMaterial, k_FillSubmesh, k_FillMaterialName,
                shader, baseQueue + 1);
            m_LineMaterial = EnsureMaterial(m_LineMaterial, k_LineSubmesh, k_LineMaterialName,
                shader, baseQueue + 2);
            // only the fill samples the texture; shadow/rim/outline stay solid like Godot's
            // draw_colored_polygon and draw_polyline
            var fillTexture = asset != null ? asset.fillTexture : null;
            if (m_FillMaterial.mainTexture != fillTexture)
                m_FillMaterial.mainTexture = fillTexture;
            var current = m_MeshRenderer.sharedMaterials;
            if (current != null && current.Length == k_SubmeshCount
                && current[k_ShadowSubmesh] == m_ShadowMaterial
                && current[k_FillSubmesh] == m_FillMaterial
                && current[k_LineSubmesh] == m_LineMaterial)
                return;
            m_MeshRenderer.sharedMaterials = new[] { m_ShadowMaterial, m_FillMaterial, m_LineMaterial };
        }

        private Material EnsureMaterial(Material material, int slot, string name, Shader shader,
            int renderQueue)
        {
            if (material == null)
            {
                var current = m_MeshRenderer.sharedMaterials;
                if (current != null && slot < current.Length && current[slot] != null
                    && current[slot].name == name)
                    material = current[slot];
            }
            if (material == null)
                material = new Material(shader)
                {
                    name = name,
                    hideFlags = HideFlags.DontSave
                };
            else if (material.shader != shader)
                material.shader = shader;
            if (material.renderQueue != renderQueue)
                material.renderQueue = renderQueue;
            return material;
        }

        private static Shader ResolveShader()
        {
            for (int i = 0; i < k_ShaderCandidates.Length; i++)
            {
                var shader = Shader.Find(k_ShaderCandidates[i]);
                if (shader != null)
                    return shader;
            }
            return null;
        }

        private void ReleaseGenerated()
        {
            DestroyGenerated(m_Mesh);
            DestroyGenerated(m_ShadowMaterial);
            DestroyGenerated(m_FillMaterial);
            DestroyGenerated(m_LineMaterial);
            m_Mesh = null;
            m_ShadowMaterial = null;
            m_FillMaterial = null;
            m_LineMaterial = null;
        }

        /// <summary>Destroying is confined to OnDestroy: DestroyImmediate is illegal from
        /// OnValidate/OnDisable, and the steady-state rebuild refills the same Mesh instead
        /// of replacing it, so nothing accumulates.</summary>
        private static void DestroyGenerated(Object generated)
        {
            if (generated == null)
                return;
            if (Application.isPlaying)
                Destroy(generated);
            else
                DestroyImmediate(generated);
        }
    }
}
