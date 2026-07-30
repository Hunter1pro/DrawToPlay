using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Mesh-building helpers for <see cref="DrawnShapeRenderer"/>: an epsilon-tolerant
    /// ear-clipping triangulator that survives the weakly-simple polygons
    /// <see cref="DrawKit.Keyhole"/> produces (bridge vertices duplicated a hair apart,
    /// zero-width corridors, sliver ears), plus polygon and ribbon appenders that write
    /// into one shared vertex buffer so a whole shape is a single Mesh.
    ///
    /// Winding contract: every appender takes triangles in COUNTER-CLOCKWISE order
    /// (positive signed area, Y-up) and stores them reversed, because Unity treats
    /// clockwise-on-screen triangles as front facing. All vertices carry the normal
    /// (0,0,-1) so the geometry faces a camera that looks down +Z. Nothing here depends
    /// on culling though — the renderer's generated materials are Cull Off.
    ///
    /// Tolerances are lengths in the caller's units (world units here), never pixels.
    /// </summary>
    public static class ShapeTessellator
    {
        /// <summary>Face normal of every generated vertex (2D geometry faces -Z).</summary>
        private static readonly Vector3 k_Normal = new Vector3(0f, 0f, -1f);

        /// <summary>Scale-free convexity floor: sin(angle) of the corner at a candidate ear.</summary>
        private const float k_ConvexSinEpsilon = 1e-6f;

        /// <summary>Below this length a direction/edge is treated as degenerate.</summary>
        private const float k_MinEdgeLength = 1e-9f;

        /// <summary>Coincident-point tolerance used when cleaning input rings.</summary>
        private const float k_DedupEpsilon = 1e-6f;

        private const int k_UInt16VertexLimit = 65000;

        /// <summary>
        /// Vertex + per-submesh index accumulator, reused across rebuilds. Nested (rather
        /// than a second top-level type) to honour the one-class-per-file rule.
        /// </summary>
        public sealed class MeshBuilder
        {
            private readonly List<Vector3> m_Positions = new List<Vector3>();
            private readonly List<Vector3> m_Normals = new List<Vector3>();
            private readonly List<Color> m_Colors = new List<Color>();
            private readonly List<Vector2> m_Uvs = new List<Vector2>();
            private readonly List<List<int>> m_Submeshes = new List<List<int>>();
            private int m_CurrentSubmesh;

            public int vertexCount => m_Positions.Count;

            public int submeshCount => m_Submeshes.Count;

            /// <summary>Submesh that <see cref="AddTriangleCcw"/> writes to (auto-grows).</summary>
            public int currentSubmesh
            {
                get => m_CurrentSubmesh;
                set
                {
                    m_CurrentSubmesh = Mathf.Max(value, 0);
                    EnsureSubmeshCount(m_CurrentSubmesh + 1);
                }
            }

            public void EnsureSubmeshCount(int count)
            {
                while (m_Submeshes.Count < count)
                    m_Submeshes.Add(new List<int>());
            }

            /// <summary>Drop all geometry, keeping the submesh slots and their capacity.</summary>
            public void Clear()
            {
                m_Positions.Clear();
                m_Normals.Clear();
                m_Colors.Clear();
                m_Uvs.Clear();
                for (int i = 0; i < m_Submeshes.Count; i++)
                    m_Submeshes[i].Clear();
                m_CurrentSubmesh = 0;
            }

            public int AddVertex(Vector2 position, float z, Color color, Vector2 uv)
            {
                m_Positions.Add(new Vector3(position.x, position.y, z));
                m_Normals.Add(k_Normal);
                m_Colors.Add(color);
                m_Uvs.Add(uv);
                return m_Positions.Count - 1;
            }

            /// <summary>Append one triangle whose indices are in counter-clockwise order.</summary>
            public void AddTriangleCcw(int i0, int i1, int i2)
            {
                EnsureSubmeshCount(m_CurrentSubmesh + 1);
                var triangles = m_Submeshes[m_CurrentSubmesh];
                // reversed on purpose: Unity's front faces wind clockwise on screen
                triangles.Add(i0);
                triangles.Add(i2);
                triangles.Add(i1);
            }

            /// <summary>Overwrite <paramref name="mesh"/> with the accumulated geometry.</summary>
            public void ToMesh(Mesh mesh)
            {
                if (mesh == null)
                    return;
                mesh.Clear();
                if (m_Positions.Count == 0)
                {
                    mesh.subMeshCount = 0;
                    return;
                }
                mesh.indexFormat = m_Positions.Count > k_UInt16VertexLimit
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
                mesh.SetVertices(m_Positions);
                mesh.SetNormals(m_Normals);
                mesh.SetColors(m_Colors);
                mesh.SetUVs(0, m_Uvs);
                mesh.subMeshCount = m_Submeshes.Count;
                for (int i = 0; i < m_Submeshes.Count; i++)
                    mesh.SetTriangles(m_Submeshes[i], i, false);
                mesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// Ear-clip <paramref name="polygon"/> into <paramref name="triangles"/> (indices into
        /// <paramref name="polygon"/>, counter-clockwise triples). Built for weakly-simple
        /// input: coincident points are skipped, convexity is measured as a scale-free sine
        /// and containment as a signed perpendicular distance, so a keyhole bridge's
        /// collinear/duplicated vertices neither block ears nor create infinite loops.
        ///
        /// When a full pass finds no ear the predicate is relaxed once (only clearly
        /// interior points block), then once more (convexity only). If even that stalls —
        /// a fully collinear remainder — the leftover ring is dropped rather than looping.
        /// <paramref name="epsilon"/> is a length tolerance in the polygon's own units.
        /// </summary>
        public static void Triangulate(IReadOnlyList<Vector2> polygon, List<int> triangles,
            float epsilon = 1e-5f)
        {
            if (triangles == null)
                return;
            triangles.Clear();
            if (polygon == null || polygon.Count < 3)
                return;
            if (epsilon <= 0f)
                epsilon = 1e-5f;

            // working ring: keep original indices, drop points that land on their predecessor
            var ring = new List<int>(polygon.Count);
            float dedupSq = k_DedupEpsilon * k_DedupEpsilon;
            for (int i = 0; i < polygon.Count; i++)
            {
                if (ring.Count > 0 && (polygon[ring[ring.Count - 1]] - polygon[i]).sqrMagnitude <= dedupSq)
                    continue;
                ring.Add(i);
            }
            while (ring.Count > 3 && (polygon[ring[0]] - polygon[ring[ring.Count - 1]]).sqrMagnitude <= dedupSq)
                ring.RemoveAt(ring.Count - 1);
            if (ring.Count < 3)
                return;

            // ear clipping needs a known orientation to read convexity from the cross product
            if (SignedArea(polygon, ring) < 0f)
                ring.Reverse();

            int mode = 0;
            int failures = 0;
            int cursor = 0;
            while (ring.Count > 3)
            {
                int count = ring.Count;
                int prev = (cursor + count - 1) % count;
                int cur = cursor % count;
                int next = (cursor + 1) % count;
                if (IsEar(polygon, ring, prev, cur, next, mode, epsilon))
                {
                    EmitTriangle(polygon, ring[prev], ring[cur], ring[next], triangles);
                    ring.RemoveAt(cur);
                    cursor = cur % ring.Count;
                    failures = 0;
                    continue;
                }
                cursor = (cursor + 1) % count;
                failures++;
                if (failures <= count)
                    continue;
                // escalation is monotonic: a stalled ring must not be able to re-stall
                // O(n) times per mode, or the triangulator becomes O(n^3) on big shapes
                failures = 0;
                mode++;
                if (mode > 2)
                    return;
            }
            EmitTriangle(polygon, ring[0], ring[1], ring[2], triangles);
        }

        /// <summary>
        /// Append a triangulated polygon in one solid colour (used for the drop shadow).
        /// <paramref name="offset"/> shifts the positions; UVs stay position / uvScale.
        /// </summary>
        public static void AppendTriangles(MeshBuilder builder, IReadOnlyList<Vector2> polygon,
            IReadOnlyList<int> triangles, Vector2 offset, float z, Color color, float uvScale)
        {
            if (!CanAppend(builder, polygon, triangles))
                return;
            float inv = uvScale > k_MinEdgeLength ? 1f / uvScale : 1f;
            int baseIndex = builder.vertexCount;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 p = polygon[i] + offset;
                builder.AddVertex(p, z, color, p * inv);
            }
            AppendIndices(builder, triangles, baseIndex, polygon.Count);
        }

        /// <summary>
        /// Append a triangulated polygon with per-vertex colours (the shaded fill).
        /// <paramref name="colors"/> is indexed like <paramref name="polygon"/>; a short
        /// list falls back to its last entry.
        /// </summary>
        public static void AppendTriangles(MeshBuilder builder, IReadOnlyList<Vector2> polygon,
            IReadOnlyList<int> triangles, Vector2 offset, float z, IReadOnlyList<Color> colors,
            float uvScale)
        {
            if (!CanAppend(builder, polygon, triangles))
                return;
            if (colors == null || colors.Count == 0)
                return;
            float inv = uvScale > k_MinEdgeLength ? 1f / uvScale : 1f;
            int baseIndex = builder.vertexCount;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 p = polygon[i] + offset;
                Color color = colors[Mathf.Min(i, colors.Count - 1)];
                builder.AddVertex(p, z, color, p * inv);
            }
            AppendIndices(builder, triangles, baseIndex, polygon.Count);
        }

        /// <summary>
        /// Append a stroke of constant <paramref name="width"/> centred on
        /// <paramref name="path"/> — the Unity stand-in for Godot's draw_polyline. Joins use
        /// the averaged neighbour normal scaled to a true miter, clamped by
        /// <paramref name="miterLimit"/>; open paths get butt caps at both ends.
        /// </summary>
        public static void AppendRibbon(MeshBuilder builder, IReadOnlyList<Vector2> path,
            bool closed, float width, float z, Color color, float uvScale, float miterLimit = 4f)
        {
            if (builder == null || path == null || width <= 0f)
                return;

            var ring = new List<Vector2>(path.Count);
            float dedupSq = k_DedupEpsilon * k_DedupEpsilon;
            for (int i = 0; i < path.Count; i++)
            {
                if (ring.Count > 0 && (ring[ring.Count - 1] - path[i]).sqrMagnitude <= dedupSq)
                    continue;
                ring.Add(path[i]);
            }
            if (closed)
            {
                while (ring.Count > 2 && (ring[0] - ring[ring.Count - 1]).sqrMagnitude <= dedupSq)
                    ring.RemoveAt(ring.Count - 1);
            }
            int n = ring.Count;
            if (n < 2 || (closed && n < 3))
                return;

            float half = width * 0.5f;
            float minCos = 1f / Mathf.Max(miterLimit, 1f);
            float inv = uvScale > k_MinEdgeLength ? 1f / uvScale : 1f;
            int baseIndex = builder.vertexCount;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = ring[i];
                Vector2 normalPrev;
                Vector2 normalNext;
                if (closed)
                {
                    normalPrev = LeftNormal(p - ring[(i - 1 + n) % n]);
                    normalNext = LeftNormal(ring[(i + 1) % n] - p);
                }
                else
                {
                    normalPrev = i > 0 ? LeftNormal(p - ring[i - 1]) : LeftNormal(ring[1] - ring[0]);
                    normalNext = i < n - 1
                        ? LeftNormal(ring[i + 1] - p)
                        : LeftNormal(ring[n - 1] - ring[n - 2]);
                }
                Vector2 offset;
                Vector2 averaged = normalPrev + normalNext;
                float length = averaged.magnitude;
                if (length <= k_MinEdgeLength)
                {
                    offset = normalNext * half;      // 180 degree reversal: use the segment normal
                }
                else
                {
                    averaged /= length;
                    float scale = 1f / Mathf.Max(Vector2.Dot(averaged, normalNext), minCos);
                    offset = averaged * (half * scale);
                }
                Vector2 left = p + offset;
                Vector2 right = p - offset;
                builder.AddVertex(left, z, color, left * inv);
                builder.AddVertex(right, z, color, right * inv);
            }

            int segments = closed ? n : n - 1;
            for (int s = 0; s < segments; s++)
            {
                int a = baseIndex + s * 2;
                int b = baseIndex + ((s + 1) % n) * 2;
                // (left a, right a, right b) and (left a, right b, left b) are both CCW
                builder.AddTriangleCcw(a, a + 1, b + 1);
                builder.AddTriangleCcw(a, b + 1, b);
            }
        }

        /// <summary>Shoelace signed area of a polygon addressed through an index ring.</summary>
        private static float SignedArea(IReadOnlyList<Vector2> polygon, List<int> ring)
        {
            float area = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 p = polygon[ring[i]];
                Vector2 q = polygon[ring[(i + 1) % ring.Count]];
                area += p.x * q.y - q.x * p.y;
            }
            return area * 0.5f;
        }

        private static bool IsEar(IReadOnlyList<Vector2> polygon, List<int> ring, int prev, int cur,
            int next, int mode, float epsilon)
        {
            Vector2 a = polygon[ring[prev]];
            Vector2 b = polygon[ring[cur]];
            Vector2 c = polygon[ring[next]];
            Vector2 ab = b - a;
            Vector2 bc = c - b;
            float lengthAb = ab.magnitude;
            float lengthBc = bc.magnitude;
            if (lengthAb <= k_MinEdgeLength || lengthBc <= k_MinEdgeLength)
                return false;
            // scale-free convexity: sin of the interior corner, positive == convex on a CCW ring
            if (Cross(ab, bc) / (lengthAb * lengthBc) <= k_ConvexSinEpsilon)
                return false;
            if (mode >= 2)
                return true;
            // mode 0 blocks on anything inside the ear, mode 1 only on clearly interior points
            float threshold = mode == 0 ? epsilon : epsilon * 8f;
            for (int i = 0; i < ring.Count; i++)
            {
                if (i == prev || i == cur || i == next)
                    continue;
                if (PointInTriangle(polygon[ring[i]], a, b, c, threshold))
                    return false;
            }
            return true;
        }

        /// <summary>Strictly-inside test against a counter-clockwise triangle, with
        /// <paramref name="threshold"/> as a perpendicular distance in the input's units.</summary>
        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c,
            float threshold)
        {
            return EdgeDistance(a, b, p) > threshold
                && EdgeDistance(b, c, p) > threshold
                && EdgeDistance(c, a, p) > threshold;
        }

        /// <summary>Signed distance of <paramref name="p"/> from the line through
        /// <paramref name="e0"/>-<paramref name="e1"/>; positive == to the left.</summary>
        private static float EdgeDistance(Vector2 e0, Vector2 e1, Vector2 p)
        {
            Vector2 edge = e1 - e0;
            float length = edge.magnitude;
            if (length <= k_MinEdgeLength)
                return 0f;
            return Cross(edge, p - e0) / length;
        }

        private static void EmitTriangle(IReadOnlyList<Vector2> polygon, int i0, int i1, int i2,
            List<int> triangles)
        {
            // keep the counter-clockwise contract no matter how the working ring was walked
            if (Cross(polygon[i1] - polygon[i0], polygon[i2] - polygon[i0]) < 0f)
            {
                int swap = i1;
                i1 = i2;
                i2 = swap;
            }
            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
        }

        private static bool CanAppend(MeshBuilder builder, IReadOnlyList<Vector2> polygon,
            IReadOnlyList<int> triangles)
        {
            return builder != null
                && polygon != null && polygon.Count >= 3
                && triangles != null && triangles.Count >= 3;
        }

        private static void AppendIndices(MeshBuilder builder, IReadOnlyList<int> triangles,
            int baseIndex, int vertexCount)
        {
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0
                    || i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
                    continue;
                builder.AddTriangleCcw(baseIndex + i0, baseIndex + i1, baseIndex + i2);
            }
        }

        /// <summary>Unit normal 90 degrees counter-clockwise from <paramref name="dir"/>.</summary>
        private static Vector2 LeftNormal(Vector2 dir)
        {
            float length = dir.magnitude;
            if (length <= k_MinEdgeLength)
                return Vector2.zero;
            return new Vector2(-dir.y, dir.x) / length;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
