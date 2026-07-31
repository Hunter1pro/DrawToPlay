using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Bowyer-Watson Delaunay triangulation of a 2D point set — the stand-in for Godot's
    /// Geometry2D.triangulate_delaunay (curve_shape_2d.gd _sync_skin line 572). Unity ships
    /// no equivalent, so the skin lattice needs its own. Pure math: no Unity objects, no
    /// scene access, unit-agnostic (every tolerance is derived from the input's own extent).
    ///
    /// Contract, mirroring Godot's: the output is a flat list of index triples into the
    /// INPUT array, each triple counter-clockwise (positive signed area, Y-up — what
    /// ShapeTessellator.MeshBuilder.AddTriangleCcw expects). Coincident points are welded;
    /// the dropped duplicate simply never appears in a triple, so callers keep their own
    /// vertex arrays index-aligned with the input.
    ///
    /// Robustness notes (this is the part that decides whether a lattice triangulates):
    ///  - the super triangle is sized from the input bounds (classic 20x construction) and
    ///    its three vertices live past the end of the caller's index range, so they are
    ///    trivially identified and dropped at the end;
    ///  - circumcentres/radii are accumulated in double even though the input is float;
    ///  - the in-circumcircle test is INCLUSIVE within a relative epsilon. SkinMeshBuilder
    ///    feeds a perfectly regular grid, whose four cell corners are exactly cocircular;
    ///    a strict test loses triangles to float ties there, an inclusive one dissolves the
    ///    whole cocircular fan and re-fans it around the new point;
    ///  - a degenerate (collinear) triangle has no circumcircle, so it is marked always-bad
    ///    and dissolves at the next insertion; whatever survives to the end is dropped by
    ///    the final area test.
    /// </summary>
    public static class Delaunay
    {
        /// <summary>Points closer than extent * this are one point (see <see cref="Weld"/>).</summary>
        private const double k_WeldRatio = 1e-6;

        /// <summary>Relative slack on the squared circumradius comparison — covers the double
        /// rounding of a circumcircle built from float inputs, so cocircular grid corners all
        /// test "inside" together instead of splitting on the last bit.</summary>
        private const double k_CircleEpsilon = 1e-9;

        /// <summary>|2 * signed area| below extent² * this counts as collinear.</summary>
        private const double k_DegenerateRatio = 1e-12;

        /// <summary>Super-triangle reach in multiples of the input extent.</summary>
        private const double k_SuperFar = 20.0;

        private struct Triangle
        {
            public int a;
            public int b;
            public int c;
            public double centerX;
            public double centerY;
            public double radiusSq;
            public bool degenerate;
        }

        /// <summary>Triangulate <paramref name="points"/> into counter-clockwise index triples
        /// appended to <paramref name="triangles"/> (cleared first).</summary>
        public static void Triangulate(IReadOnlyList<Vector2> points, List<int> triangles)
        {
            if (triangles == null)
                return;
            triangles.Clear();
            int n = points == null ? 0 : points.Count;
            if (n < 3)
                return;

            if (!Bounds(points, out Vector2 min, out Vector2 max))
                return;
            double width = (double)max.x - min.x;
            double height = (double)max.y - min.y;
            double extent = System.Math.Max(width, height);
            if (extent <= 0.0)
                return;                     // every point coincident: no triangle exists

            double weld = extent * k_WeldRatio;
            double degenerate = extent * extent * k_DegenerateRatio;

            var order = new List<int>(n);
            Weld(points, weld, order);
            if (order.Count < 3)
                return;

            // 0..n-1 are the caller's points, n..n+2 the super triangle
            var px = new double[n + 3];
            var py = new double[n + 3];
            for (int i = 0; i < n; i++)
            {
                px[i] = points[i].x;
                py[i] = points[i].y;
            }
            double midX = ((double)min.x + max.x) * 0.5;
            double midY = ((double)min.y + max.y) * 0.5;
            double far = extent * k_SuperFar;
            px[n] = midX - far;
            py[n] = midY - extent;
            px[n + 1] = midX;
            py[n + 1] = midY + far;
            px[n + 2] = midX + far;
            py[n + 2] = midY - extent;

            var tris = new List<Triangle>(order.Count * 2 + 8);
            tris.Add(MakeTriangle(px, py, n, n + 1, n + 2, degenerate));

            var bad = new List<int>();
            var edgeA = new List<int>();
            var edgeB = new List<int>();
            var edgeShared = new List<bool>();

            for (int k = 0; k < order.Count; k++)
            {
                int pi = order[k];
                double x = px[pi];
                double y = py[pi];

                bad.Clear();
                for (int t = 0; t < tris.Count; t++)
                {
                    if (Contains(tris[t], x, y))
                        bad.Add(t);
                }
                if (bad.Count == 0)
                    continue;               // outside the super triangle: cannot happen, guard anyway

                edgeA.Clear();
                edgeB.Clear();
                edgeShared.Clear();
                for (int i = 0; i < bad.Count; i++)
                {
                    Triangle t = tris[bad[i]];
                    AddEdge(edgeA, edgeB, edgeShared, t.a, t.b);
                    AddEdge(edgeA, edgeB, edgeShared, t.b, t.c);
                    AddEdge(edgeA, edgeB, edgeShared, t.c, t.a);
                }

                // compact out the bad triangles (bad is ascending by construction — a
                // swap-with-last removal would move an unprocessed triangle under a stale index)
                int write = 0;
                int next = 0;
                for (int t = 0; t < tris.Count; t++)
                {
                    if (next < bad.Count && bad[next] == t)
                    {
                        next++;
                        continue;
                    }
                    tris[write++] = tris[t];
                }
                tris.RemoveRange(write, tris.Count - write);

                // re-fan the hole: every edge that only one bad triangle owned is a boundary
                for (int i = 0; i < edgeA.Count; i++)
                {
                    if (edgeShared[i])
                        continue;
                    tris.Add(MakeTriangle(px, py, edgeA[i], edgeB[i], pi, degenerate));
                }
            }

            for (int t = 0; t < tris.Count; t++)
            {
                Triangle tri = tris[t];
                if (tri.a >= n || tri.b >= n || tri.c >= n)
                    continue;               // touches the super triangle
                double cross = Cross(px, py, tri.a, tri.b, tri.c);
                if (System.Math.Abs(cross) <= degenerate)
                    continue;               // collinear leftovers never reach the mesh
                triangles.Add(tri.a);
                triangles.Add(tri.b);
                triangles.Add(tri.c);
            }
        }

        // --- internals -------------------------------------------------------------------

        private static bool Bounds(IReadOnlyList<Vector2> points, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;
            bool any = false;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                if (!IsFinite(p))
                    continue;
                if (!any)
                {
                    min = p;
                    max = p;
                    any = true;
                    continue;
                }
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return any;
        }

        private static bool IsFinite(Vector2 p)
        {
            return !float.IsNaN(p.x) && !float.IsNaN(p.y)
                && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);
        }

        /// <summary>Insertion order with coincident points removed. Bowyer-Watson cannot
        /// insert a point that already exists (its "hole" is empty), and the skin lattice can
        /// legitimately land a grid point on a ring point, so the weld is not optional.
        /// A uniform grid hash keeps this linear; hash collisions only cost extra distance
        /// tests, never correctness.</summary>
        private static void Weld(IReadOnlyList<Vector2> points, double weld, List<int> order)
        {
            double cell = weld > 0.0 ? weld : 1e-9;
            double weldSq = weld * weld;
            var buckets = new Dictionary<long, List<int>>();
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                if (!IsFinite(p))
                    continue;
                long gx = (long)System.Math.Floor(p.x / cell);
                long gy = (long)System.Math.Floor(p.y / cell);
                bool duplicate = false;
                for (long ox = -1; ox <= 1 && !duplicate; ox++)
                {
                    for (long oy = -1; oy <= 1 && !duplicate; oy++)
                    {
                        if (!buckets.TryGetValue(Key(gx + ox, gy + oy), out List<int> bucket))
                            continue;
                        for (int b = 0; b < bucket.Count; b++)
                        {
                            Vector2 q = points[bucket[b]];
                            double dx = (double)p.x - q.x;
                            double dy = (double)p.y - q.y;
                            if (dx * dx + dy * dy <= weldSq)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                    }
                }
                if (duplicate)
                    continue;
                long key = Key(gx, gy);
                if (!buckets.TryGetValue(key, out List<int> cellPoints))
                {
                    cellPoints = new List<int>(4);
                    buckets[key] = cellPoints;
                }
                cellPoints.Add(i);
                order.Add(i);
            }
        }

        private static long Key(long gx, long gy)
        {
            unchecked
            {
                return gx * 73856093L ^ gy * 83492791L;
            }
        }

        /// <summary>Record edge (u, v); a second sighting marks it interior. Sightings beyond
        /// the second (possible around dissolved degenerate slivers) keep it interior rather
        /// than adding a duplicate boundary edge.</summary>
        private static void AddEdge(List<int> edgeA, List<int> edgeB, List<bool> shared, int u, int v)
        {
            for (int i = 0; i < edgeA.Count; i++)
            {
                if ((edgeA[i] == u && edgeB[i] == v) || (edgeA[i] == v && edgeB[i] == u))
                {
                    shared[i] = true;
                    return;
                }
            }
            edgeA.Add(u);
            edgeB.Add(v);
            shared.Add(false);
        }

        private static bool Contains(in Triangle t, double x, double y)
        {
            if (t.degenerate)
                return true;
            double dx = x - t.centerX;
            double dy = y - t.centerY;
            return dx * dx + dy * dy <= t.radiusSq * (1.0 + k_CircleEpsilon);
        }

        private static double Cross(double[] px, double[] py, int a, int b, int c)
        {
            return (px[b] - px[a]) * (py[c] - py[a]) - (py[b] - py[a]) * (px[c] - px[a]);
        }

        /// <summary>Counter-clockwise triangle + circumcircle. The circumcentre denominator is
        /// exactly 2 * the signed area, so one cross product decides both the winding and
        /// whether the triangle is degenerate.</summary>
        private static Triangle MakeTriangle(double[] px, double[] py, int a, int b, int c,
            double degenerateArea)
        {
            double cross = Cross(px, py, a, b, c);
            if (cross < 0.0)
            {
                int swap = b;
                b = c;
                c = swap;
                cross = -cross;
            }

            var tri = new Triangle { a = a, b = b, c = c };
            if (cross <= degenerateArea)
            {
                tri.degenerate = true;
                return tri;
            }

            double ax = px[a], ay = py[a];
            double bx = px[b], by = py[b];
            double cx = px[c], cy = py[c];
            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            double d = 2.0 * cross;
            tri.centerX = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            tri.centerY = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            double rx = ax - tri.centerX;
            double ry = ay - tri.centerY;
            tri.radiusSq = rx * rx + ry * ry;
            return tri;
        }
    }
}
