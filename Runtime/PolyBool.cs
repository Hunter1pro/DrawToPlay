using System.Collections.Generic;
using UnityEngine;
using Clip2 = Clipper2Lib;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Thin wrapper over the vendored Clipper2 library (Runtime/ThirdParty/Clipper2,
    /// Boost license) exposing exactly the Godot Geometry2D subset that curve_kit.gd
    /// uses. Godot's Geometry2D is itself Clipper underneath, so semantics match the
    /// original tool by construction.
    ///
    /// Output normalization contract (all boolean ops):
    ///  - every returned ring is a simple polygon, no duplicate closing point
    ///  - outer boundaries have POSITIVE signed area, holes NEGATIVE
    ///    (normalize Clipper2 output orientation if needed — verify empirically,
    ///     do not assume)
    ///  - coordinates round-trip through Clipper2's floating-point PathsD API
    ///    (precision suitable for world units at 1/32 Godot px scale — use a
    ///     precision/scale of at least 4 decimal digits).
    ///
    /// The Clipper2 types stay behind the <c>Clip2</c> alias so every vendored call is
    /// visible at a glance and no name can collide with UnityEngine.
    /// </summary>
    public static class PolyBool
    {
        // --- Clipper2 facts verified in the vendored source, not assumed -----------------
        //
        // * Clipper.Core.cs (~line 492): enum FillRule { EvenOdd, NonZero, Positive,
        //   Negative }. Godot's Geometry2D runs Clipper2 with NonZero, so this port does.
        // * Clipper.Engine.cs ClipperD(int roundingDecimalPrecision) (~line 3292) scales
        //   every coordinate by 10^precision into the int64 engine and back out again, so
        //   `precision` IS the coordinate resolution in the caller's units.
        // * Winding of the output: Clipper.Engine.cs BuildPaths (~line 3048) states
        //   "closed paths should always return a Positive orientation except when
        //   ReverseSolution == true", and ReverseSolution is left false (ClipperBase ctor,
        //   ~line 384, only sets PreserveCollinear). Clipper.Offset.cs Group ctor (~line 62)
        //   documents the matching convention on input: "the lowermost path must be an outer
        //   path, so if its orientation is negative, then flag that the whole group is
        //   'reversed'". => Clipper2 emits OUTER rings with positive Clipper.Area and HOLES
        //   with negative Clipper.Area.
        // * Sign of Clipper.Area(PathD) (Clipper.cs ~line 273): it accumulates
        //   (prev.y + pt.y) * (prev.x - pt.x); expanding the sum telescopes the x_i*y_i
        //   terms away and leaves sum(x_i*y_i+1 - x_i+1*y_i), i.e. exactly twice the
        //   shoelace area used by DrawKit.SignedArea — SAME sign, same magnitude.
        // => Clipper2's raw output already satisfies this class's winding contract, so
        //    Normalize() never has to flip a ring; it only strips duplicate closing points
        //    and drops degenerate rings. If the vendored library is ever updated to a
        //    version that reverses this, Normalize() is the single place to fix.
        //
        // Godot's Geometry2D uses PRECISION = 5 on values in px; 5 digits on world units
        // (1 unit ~= 32 px) is 1e-5 units ~= 1/320 px, i.e. finer than the original.
        private const int k_Precision = 5;

        private const Clip2.FillRule k_FillRule = Clip2.FillRule.NonZero;

        // Godot's Geometry2D calls PreserveCollinear(false) before every boolean op; the
        // curve_kit.resample doc comment ("Clipper strips collinear points, so a long
        // straight edge is just its two corners") depends on that behaviour.
        private const bool k_PreserveCollinear = false;

        // Two points closer than one coordinate step (10^-k_Precision) are indistinguishable
        // to the engine, so anything nearer counts as a duplicate closing point.
        private const float k_WeldDistanceSqr = 1e-10f;

        // Geometry2D.segment_intersects_segment parity (Godot CMP_EPSILON). It is compared
        // against a perpendicular-distance / segment-length ratio, which is dimensionless,
        // so this value needs no px -> world-unit conversion.
        private const float k_CmpEpsilon = 1e-5f;

        // Godot is_point_in_polygon casts its ray at a point pushed past the polygon bounds
        // by these (deliberately irrational-looking) factors so the ray misses vertices.
        private static readonly Vector2 k_RayPush = new Vector2(1.221313f, 1.512312f);

        /// <summary>Boolean union of two closed polygons — Godot Geometry2D.merge_polygons.
        /// Returns all result rings (outers and holes, per the normalization contract).</summary>
        public static List<List<Vector2>> Union(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            return Execute(Clip2.ClipType.Union, a, b);
        }

        /// <summary>Boolean difference a - b — Godot Geometry2D.clip_polygons.</summary>
        public static List<List<Vector2>> Difference(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            return Execute(Clip2.ClipType.Difference, a, b);
        }

        /// <summary>Round-join, round-cap polyline offset — Godot
        /// Geometry2D.offset_polyline(line, delta, JOIN_ROUND, END_ROUND).</summary>
        public static List<List<Vector2>> OffsetPolyline(IReadOnlyList<Vector2> line, float delta)
        {
            var result = new List<List<Vector2>>();
            if (line == null || line.Count == 0 || delta <= 0f)
                return result;

            // Clipper.cs InflatePaths(PathsD, delta, joinType, endType, miterLimit,
            // precision, arcTolerance) — miterLimit is unused for round joins; arcTolerance 0
            // selects Clipper2's default (delta/500), which gives smoother caps than Godot.
            var paths = new Clip2.PathsD(1) { ToPathD(line) };
            Clip2.PathsD solution = Clip2.Clipper.InflatePaths(paths, delta,
                Clip2.JoinType.Round, Clip2.EndType.Round, 2.0, k_Precision);
            Normalize(solution, result);
            return result;
        }

        /// <summary>True when the ring is a hole under the normalization contract
        /// (signed area &lt; 0). Replacement for Godot is_polygon_clockwise checks.</summary>
        public static bool IsHole(IReadOnlyList<Vector2> ring)
        {
            // Godot's is_polygon_clockwise(p) sums (v2.x - v1.x) * (v2.y + v1.y), i.e.
            // -2 * shoelace, and returns sum > 0 — exactly "shoelace < 0". It also returns
            // false for rings with fewer than 3 points, which ShoelaceArea reproduces.
            return ShoelaceArea(ring) < 0.0;
        }

        /// <summary>Point-in-polygon test — Godot Geometry2D.is_point_in_polygon.</summary>
        public static bool IsPointInPolygon(Vector2 point, IReadOnlyList<Vector2> poly)
        {
            int c = poly == null ? 0 : poly.Count;
            if (c < 3)
                return false;

            // Godot pushes a point outside the polygon's bounds and counts edge crossings of
            // the segment [point -> outside] (even-odd rule).
            Vector2 furtherAway = poly[0];
            Vector2 furtherAwayOpposite = poly[0];
            for (int i = 1; i < c; i++)
            {
                furtherAway = Vector2.Max(furtherAway, poly[i]);
                furtherAwayOpposite = Vector2.Min(furtherAwayOpposite, poly[i]);
            }
            Vector2 span = furtherAway - furtherAwayOpposite;
            furtherAway += new Vector2(span.x * k_RayPush.x, span.y * k_RayPush.y);

            int intersections = 0;
            for (int i = 0; i < c; i++)
            {
                Vector2 v1 = poly[i];
                Vector2 v2 = poly[(i + 1) % c];
                if (SegmentIntersectsSegment(v1, v2, point, furtherAway, out _))
                    intersections++;
            }
            return (intersections & 1) == 1;
        }

        /// <summary>Segment-segment intersection — Godot Geometry2D.segment_intersects_segment.
        /// Returns true and the hit point when segments (a1,a2) and (b1,b2) cross.</summary>
        public static bool SegmentIntersectsSegment(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 hit)
        {
            // Line-for-line port of Godot's Geometry2D::segment_intersects_segment: rotate
            // b1/b2 into the frame of segment a (scaled by |a|^2 so the perpendicular
            // components come out as distance/|a| ratios), reject when both endpoints of b
            // sit on the same side of line a, then require the crossing to land inside a.
            hit = Vector2.zero;

            Vector2 b = a2 - a1;
            Vector2 c = b1 - a1;
            Vector2 d = b2 - a1;

            float abLen = Vector2.Dot(b, b);
            if (abLen <= 0f)
                return false;

            Vector2 bn = b / abLen;
            c = new Vector2(c.x * bn.x + c.y * bn.y, c.y * bn.x - c.x * bn.y);
            d = new Vector2(d.x * bn.x + d.y * bn.y, d.y * bn.x - d.x * bn.y);

            if ((c.y < k_CmpEpsilon && d.y < k_CmpEpsilon) || (c.y > -k_CmpEpsilon && d.y > -k_CmpEpsilon))
                return false;

            float abPos = d.x + (c.x - d.x) * d.y / (d.y - c.y);

            // fail if segment b crosses the line through a outside of segment a
            if (abPos < 0f || abPos > 1f)
                return false;

            hit = a1 + b * abPos;
            return true;
        }

        // --- internals -------------------------------------------------------------------

        private static List<List<Vector2>> Execute(Clip2.ClipType clipType,
            IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            var result = new List<List<Vector2>>();

            var clipper = new Clip2.ClipperD(k_Precision) { PreserveCollinear = k_PreserveCollinear };
            if (a != null && a.Count > 0)
                clipper.AddSubject(ToPathD(a));
            if (b != null && b.Count > 0)
                clipper.AddClip(ToPathD(b));

            var solution = new Clip2.PathsD();
            if (!clipper.Execute(clipType, k_FillRule, solution))
                return result;

            Normalize(solution, result);
            return result;
        }

        private static Clip2.PathD ToPathD(IReadOnlyList<Vector2> pts)
        {
            var path = new Clip2.PathD(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                path.Add(new Clip2.PointD((double)pts[i].x, (double)pts[i].y));
            return path;
        }

        /// <summary>Apply the class contract to a raw Clipper2 solution: drop degenerate
        /// rings, strip a duplicate closing point, keep Clipper2's winding (already
        /// outers-positive / holes-negative — see the verification notes above).</summary>
        private static void Normalize(Clip2.PathsD solution, List<List<Vector2>> result)
        {
            if (solution == null)
                return;

            foreach (Clip2.PathD path in solution)
            {
                int count = path.Count;
                if (count < 3)
                    continue;

                // Clipper2's engine never emits the closing point, but ClipperOffset returns
                // its input paths verbatim when |delta| < 0.5 scaled units, so strip it.
                double dx = path[0].x - path[count - 1].x;
                double dy = path[0].y - path[count - 1].y;
                if (dx * dx + dy * dy < k_WeldDistanceSqr)
                    count--;
                if (count < 3)
                    continue;

                var ring = new List<Vector2>(count);
                for (int i = 0; i < count; i++)
                    ring.Add(new Vector2((float)path[i].x, (float)path[i].y));
                result.Add(ring);
            }
        }

        /// <summary>Shoelace area in double precision — the sign authority for IsHole.
        /// Same formula (and sign) as Clipper.Area(PathD) and DrawKit.SignedArea.</summary>
        private static double ShoelaceArea(IReadOnlyList<Vector2> ring)
        {
            int n = ring == null ? 0 : ring.Count;
            if (n < 3)
                return 0.0;

            double a = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = ring[i];
                Vector2 q = ring[(i + 1) % n];
                a += (double)p.x * q.y - (double)q.x * p.y;
            }
            return a * 0.5;
        }
    }
}
