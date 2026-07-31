using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Builds the skinned mesh of a drawn shape: the geometry half of curve_shape_2d.gd
    /// _sync_skin / _bone_weights / _sync_skin_outline / _biggest_poly (lines 544-748).
    /// Pure geometry — it never touches the scene, so <see cref="DrawnShapeSkin"/> owns all
    /// Transform/SkinnedMeshRenderer wiring and hands the REST segments in already mapped to
    /// shape-local space.
    ///
    /// Layout of the produced mesh (two submeshes, matching the base renderer's slots so the
    /// same generated materials can be reused): submesh 0 = fill (outline ring + interior
    /// lattice, Delaunay-triangulated and centroid-clipped, vertex-coloured like the base
    /// fill), submesh 1 = a mitered outline ribbon (skinning-safe local triangulation). Vertices are shape-LOCAL and
    /// <see cref="SkinData.boneWeights"/> is index-aligned with them, fill first.
    ///
    /// Godot parity notes: hole curves are ignored on the skin path (so is the drop shadow
    /// and the rim run) because _draw returns right after _sync_skin — a skinned shape in the
    /// original is exactly "fill polygon + outline band".
    /// </summary>
    public static class SkinMeshBuilder
    {
        /// <summary>Interior lattice floor — Godot maxf(skin_detail, 2.0) px at 32 px/unit.</summary>
        private const float k_MinLatticeStep = 2f / 32f;

        /// <summary>Ceiling on interior lattice points. Godot has no cap; a 100-unit blob at
        /// the default 6 px detail would otherwise feed ~250k points to an O(n·t) Bowyer-Watson.
        /// Above the cap the step is scaled up once (coarser skin, same behaviour).</summary>
        private const int k_MaxLatticePoints = 4096;

        /// <summary>Same ear-clipping tolerance the base renderer uses for its fill.</summary>
        private const float k_GeometryEpsilon = 1e-5f;

        private const float k_MinTextureScale = 1e-4f;

        /// <summary>Weight falloff floor: skinSoftness 0 with a vertex exactly on a bone
        /// segment divides by zero (Godot yields inf/inf = NaN there and silently corrupts the
        /// binding). Deliberate deviation — everything else in _bone_weights is verbatim.</summary>
        private const float k_MinSoftness = 1e-12f;

        /// <summary>Layer z of the base renderer's fill / outline submeshes (DrawnShapeRenderer
        /// k_FillZ / k_OutlineZ), so the skinned shape sorts exactly like the unskinned one.</summary>
        private const float k_FillZ = 0.001f;
        private const float k_OutlineZ = 0f;

        /// <summary>Submesh slots — deliberately the base renderer's fill(1)/line(2) order minus
        /// the shadow slot, i.e. materials[1] drives submesh 0 and materials[2] submesh 1.</summary>
        private const int k_FillSubmesh = 0;
        private const int k_BandSubmesh = 1;

        /// <summary>1 Godot px == 1/32 world units: the floor DrawnShapeRenderer uses when a
        /// span would otherwise be zero (BuildFillColors parity).</summary>
        private const float k_PixelToUnit = 1f / 32f;

        /// <summary>A bone's REST pose reduced to the segment the weights measure against,
        /// already in shape-LOCAL space (origin, and the tip at (length, 0) clamped to the
        /// outline by DrawKit.ClampSegmentTip) — curve_shape_2d.gd lines 624-630.</summary>
        public struct BoneSegment
        {
            public Vector2 start;
            public Vector2 tip;
        }

        /// <summary>Reusable scratch + the results a caller needs after <see cref="Build"/>:
        /// the per-vertex bone weights (mesh order) and the influence view the debug overlay
        /// reads. Lists are reused across rebuilds; treat the exposed ones as read-only and
        /// valid only until the next Build.</summary>
        public sealed class SkinData
        {
            /// <summary>Fill vertices in shape-local space (ring points first, then lattice).</summary>
            public readonly List<Vector2> influenceVertices = new List<Vector2>();

            /// <summary>Highest-weight bone index per <see cref="influenceVertices"/> entry.</summary>
            public readonly List<int> influenceDominantBone = new List<int>();

            /// <summary>Per-vertex weights for the WHOLE mesh (fill vertices then band
            /// vertices), ready for Mesh.boneWeights.</summary>
            public BoneWeight[] boneWeights = System.Array.Empty<BoneWeight>();

            public int vertexCount;

            internal readonly ShapeTessellator.MeshBuilder builder = new ShapeTessellator.MeshBuilder();
            internal readonly List<Vector2> lattice = new List<Vector2>();
            internal readonly List<Vector2> all = new List<Vector2>();
            internal readonly List<int> triangles = new List<int>();
            internal readonly List<Color> colors = new List<Color>();
            internal readonly List<BoneWeight> weights = new List<BoneWeight>();

            internal void Clear()
            {
                influenceVertices.Clear();
                influenceDominantBone.Clear();
                boneWeights = System.Array.Empty<BoneWeight>();
                vertexCount = 0;
                lattice.Clear();
                all.Clear();
                triangles.Clear();
                colors.Clear();
                weights.Clear();
            }
        }

        /// <summary>Fill <paramref name="mesh"/> (and <paramref name="data"/>) from the shape's
        /// displaced outline <paramref name="ring"/>, its style, and the bone REST
        /// <paramref name="segments"/>. Returns false when nothing renderable came out, in
        /// which case the mesh is left empty and the caller should stay unskinned.</summary>
        public static bool Build(Mesh mesh, SkinData data, IReadOnlyList<Vector2> ring,
            DrawnShapeAsset style, IReadOnlyList<BoneSegment> segments)
        {
            if (mesh == null || data == null || style == null)
                return false;
            data.Clear();
            if (ring == null || ring.Count < 3 || segments == null || segments.Count == 0)
                return false;

            // --- fill: ring + interior lattice, Delaunay, centroid-clipped (lines 555-580) ---
            BuildLattice(ring, style, data.lattice);
            data.all.AddRange(ring);
            data.all.AddRange(data.lattice);
            Delaunay.Triangulate(data.all, data.triangles);
            KeepTrianglesInside(data.all, data.triangles, ring);
            if (data.triangles.Count == 0)
                return false;

            BuildFillColors(data.all, style, data.colors);
            float softness = Mathf.Max(style.skinSoftness * style.skinSoftness, k_MinSoftness);
            float uvScale = Mathf.Max(style.textureScale, k_MinTextureScale);
            float invUv = 1f / uvScale;

            ShapeTessellator.MeshBuilder builder = data.builder;
            builder.Clear();
            builder.EnsureSubmeshCount(2);
            builder.currentSubmesh = k_FillSubmesh;
            for (int i = 0; i < data.all.Count; i++)
            {
                Vector2 p = data.all[i];
                builder.AddVertex(p, k_FillZ, data.colors[i], p * invUv);
                BoneWeight weight = Weight(p, segments, softness);
                data.weights.Add(weight);
                data.influenceVertices.Add(p);
                data.influenceDominantBone.Add(weight.boneIndex0);
            }
            for (int t = 0; t + 2 < data.triangles.Count; t += 3)
                builder.AddTriangleCcw(data.triangles[t], data.triangles[t + 1], data.triangles[t + 2]);

            // --- outline band as a second skinned submesh (lines 710-740) -------------------
            // DEVIATION from the Godot offset_polygon + keyhole band: that polygon must be
            // ear-clipped, and ear clipping legally emits long slivers ALONG the strip —
            // fine at rest, but a sliver whose corners sit far apart along the outline spans
            // bones that deform differently and blows up into large ghost triangles the
            // moment the rig poses (observed as rest-silhouette-shaped sails in M3 bring-up).
            // A mitered ribbon (the exact strip the base renderer draws) pairs each ring
            // vertex with its own outer/inner offset, so every triangle is LOCAL and deforms
            // with its neighbourhood. Same visual, skinning-safe by construction.
            if (style.outlineWidth > 0f)
            {
                int baseIndex = builder.vertexCount;
                builder.currentSubmesh = k_BandSubmesh;
                ShapeTessellator.AppendRibbon(builder, ring, true, style.outlineWidth,
                    k_OutlineZ, ToVertexColor(style.outlineColor), invUv);
                for (int i = baseIndex; i < builder.vertexCount; i++)
                    data.weights.Add(Weight(builder.GetPosition(i), segments, softness));
            }

            builder.ToMesh(mesh);
            if (mesh.vertexCount != data.weights.Count)
                return false;               // never ship a weight array Unity would reject
            data.boneWeights = data.weights.ToArray();
            data.vertexCount = data.weights.Count;
            return true;
        }

        // --- lattice ---------------------------------------------------------------------

        /// <summary>Port of the interior grid in _sync_skin (lines 556-569): half-step-offset
        /// samples over the ring's bounding box, kept when inside the ring. The float
        /// accumulation of the loop is Godot's, kept as-is so both produce the same rows.</summary>
        private static void BuildLattice(IReadOnlyList<Vector2> ring, DrawnShapeAsset style,
            List<Vector2> lattice)
        {
            Vector2 min = ring[0];
            Vector2 max = ring[0];
            for (int i = 1; i < ring.Count; i++)
            {
                min = Vector2.Min(min, ring[i]);
                max = Vector2.Max(max, ring[i]);
            }
            float step = Mathf.Max(style.skinDetail, k_MinLatticeStep);
            step = ClampStep(step, max.x - min.x, max.y - min.y);
            for (float y = min.y + step * 0.5f; y < max.y; y += step)
            {
                for (float x = min.x + step * 0.5f; x < max.x; x += step)
                {
                    var p = new Vector2(x, y);
                    if (PolyBool.IsPointInPolygon(p, ring))
                        lattice.Add(p);
                }
            }
        }

        private static float ClampStep(float step, float width, float height)
        {
            if (step <= 0f || width <= 0f || height <= 0f)
                return Mathf.Max(step, k_MinLatticeStep);
            float cells = width / step * (height / step);
            if (cells <= k_MaxLatticePoints)
                return step;
            return step * Mathf.Sqrt(cells / k_MaxLatticePoints);
        }

        /// <summary>Port of the centroid test in _sync_skin (lines 574-577): a Delaunay
        /// triangle survives only when its centroid is inside the ring, which is what carves
        /// concavities back out of the convex triangulation.</summary>
        private static void KeepTrianglesInside(IReadOnlyList<Vector2> points, List<int> triangles,
            IReadOnlyList<Vector2> ring)
        {
            int write = 0;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];
                Vector2 centroid = (points[i0] + points[i1] + points[i2]) / 3f;
                if (!PolyBool.IsPointInPolygon(centroid, ring))
                    continue;
                triangles[write++] = i0;
                triangles[write++] = i1;
                triangles[write++] = i2;
            }
            triangles.RemoveRange(write, triangles.Count - write);
        }

        // --- weights ---------------------------------------------------------------------

        /// <summary>Port of _bone_weights (lines 660-704) for one vertex: inverse-square-ish
        /// falloff r = 1 / (d² + softness²) against every bone's REST segment, top TWO bones
        /// kept and normalised. Godot builds one array per bone and Unity wants one struct per
        /// vertex, so the transpose is implicit — the arithmetic and the tie-breaking order of
        /// the best/second scan are unchanged.
        ///
        /// The skeleton-wide "self-heal degenerate rests" pass Godot runs first (lines 599-602)
        /// is deliberately NOT ported: it repairs Bone2D nodes whose rest was never authored,
        /// and RigAsset rests are always authored by the rig tool.</summary>
        private static BoneWeight Weight(Vector2 v, IReadOnlyList<BoneSegment> segments, float softness)
        {
            int best = -1;
            int second = -1;
            float rBest = 0f;
            float rSecond = 0f;
            for (int bi = 0; bi < segments.Count; bi++)
            {
                float d = PointSegmentDistance(v, segments[bi].start, segments[bi].tip);
                float r = 1f / (d * d + softness);
                if (best < 0 || r > rBest)
                {
                    second = best;
                    rSecond = rBest;
                    best = bi;
                    rBest = r;
                }
                else if (second < 0 || r > rSecond)
                {
                    second = bi;
                    rSecond = r;
                }
            }

            var weight = new BoneWeight();
            float total = rBest + (second >= 0 ? rSecond : 0f);
            if (best < 0 || total <= 0f || float.IsNaN(total) || float.IsInfinity(total))
            {
                // unreachable with a finite softness floor; a fully-weighted first bone is the
                // safe fallback (a zeroed BoneWeight would collapse the vertex onto the origin)
                weight.boneIndex0 = Mathf.Max(best, 0);
                weight.weight0 = 1f;
                return weight;
            }
            weight.boneIndex0 = best;
            weight.weight0 = rBest / total;
            weight.boneIndex1 = second >= 0 ? second : 0;
            weight.weight1 = second >= 0 ? rSecond / total : 0f;
            return weight;
        }

        /// <summary>Port of curve_kit.gd _pt_seg_dist (lines 43-47).</summary>
        private static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            float t = lenSq == 0f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }

        // --- outline band ----------------------------------------------------------------




        // --- fill colours ----------------------------------------------------------------

        /// <summary>Mirror of DrawnShapeRenderer.BuildFillColors (its own port of
        /// _vertex_colors, lines 515-532) — private there, and widening that component's API
        /// for one helper was not worth it, so the gradient lives twice. Any change to the
        /// base renderer's shading has to be mirrored here or a skinned shape will shade
        /// differently from an unskinned one.</summary>
        private static void BuildFillColors(IReadOnlyList<Vector2> points, DrawnShapeAsset style,
            List<Color> colors)
        {
            colors.Clear();
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

        /// <summary>Mirror of DrawnShapeRenderer.ToVertexColor: mesh colours bypass Unity's
        /// colour-space conversion, so convert explicitly.</summary>
        private static Color ToVertexColor(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }
    }
}
