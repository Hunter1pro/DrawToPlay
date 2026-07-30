using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Faithful C# port of the hunter project's curve_kit.gd (scripts/level/curve_kit.gd):
    /// a rough freehand scribble becomes a smooth closed shape (simplify + Catmull-Rom fit),
    /// and boolean sculpt strokes extend/carve existing shapes. Pure math, no editor deps —
    /// Burst-compilable later. Unit-agnostic: tolerances/spacings are in the same space as
    /// the points (callers convert screen px to world units).
    ///
    /// Winding convention (differs from Godot only in naming, not behavior): outer rings
    /// have POSITIVE signed area, holes NEGATIVE. Godot's is_polygon_clockwise(hole) == true
    /// maps to SignedArea(ring) &lt; 0 here. PolyBool normalizes Clipper2 output to match.
    /// </summary>
    public static class DrawKit
    {
        /// <summary>
        /// Godot px -> world unit factor (1 world unit ~= 32 Godot px, see
        /// draw-tool-port-brief / m0 conventions). Caller-supplied tolerances and spacings
        /// stay unit-agnostic; this factor exists only to pre-scale the LITERAL px epsilons,
        /// floors and nudges that are hard-coded inside curve_kit.gd, so that their intent
        /// ("a hair", "too small to matter") survives the unit change. Every use below names
        /// the original Godot value.
        /// </summary>
        private const float k_PxToUnit = 1f / 32f;

        /// <summary>curve_kit.resample: spacings below this are treated as "no resampling"
        /// (Godot 0.1 px).</summary>
        private const float k_MinSpacing = 0.1f * k_PxToUnit;

        /// <summary>curve_kit.resample / resample_count: segment shorter than this is skipped
        /// as degenerate (Godot 0.0001 px).</summary>
        private const float k_DegenerateSegment = 0.0001f * k_PxToUnit;

        /// <summary>curve_kit.resample: open-path endpoint is re-appended when the last
        /// resampled point missed it by more than this (Godot 0.01 px).</summary>
        private const float k_EndpointEpsilon = 0.01f * k_PxToUnit;

        /// <summary>curve_kit.fit_curve: resample spacing floor (Godot 0.3 px).</summary>
        private const float k_FitMinSpacing = 0.3f * k_PxToUnit;

        /// <summary>curve_kit._clamped_tangent: below this handle limit the tangent collapses
        /// to zero instead of being normalized (Godot 0.001 px).</summary>
        private const float k_MinTangentLimit = 0.001f * k_PxToUnit;

        /// <summary>curve_kit.resample_count: ring shorter than this is returned unchanged
        /// (Godot 0.001 px).</summary>
        private const float k_MinRingLength = 0.001f * k_PxToUnit;

        /// <summary>curve_kit.keyhole: the return bridge is offset this far sideways so the
        /// channel is never zero-width (Godot 0.05 px).</summary>
        private const float k_KeyholeBridgeOffset = 0.05f * k_PxToUnit;

        /// <summary>curve_kit.stroke_capsule: nudge that turns a single-point stroke into a
        /// two-point line so the offset produces a disc (Godot 0.01 px).</summary>
        private const float k_CapsuleNudge = 0.01f * k_PxToUnit;

        /// <summary>curve_kit.displace: wavelength floor (Godot 2.0 px).</summary>
        private const float k_MinWavelength = 2f * k_PxToUnit;

        /// <summary>curve_kit.displace: rings shorter than this get no wobble
        /// (Godot 1.0 px).</summary>
        private const float k_MinDisplaceLength = 1f * k_PxToUnit;

        /// <summary>What MergeStroke decided to do — port of the "op" strings in
        /// curve_kit.gd merge_stroke (lines 342-401).</summary>
        public enum StrokeOp
        {
            None,       // nothing to do (subtract on an empty node)
            Adopt,      // node was empty — curve is the fitted stroke
            Merged,     // union/carve result refitted into one smooth curve
            Disjoint,   // stroke missed the shape — caller should spawn a new node
            Inside,     // union fully inside — caller should spawn a NEW layered shape on top
            Hole,       // carve fully inside — caller should add curve as a hole ring
            Cleared     // carve removed everything — curve is empty
        }

        public struct StrokeResult
        {
            public StrokeOp op;
            public DrawnCurve curve; // null for None; empty curve for Cleared
        }

        /// <summary>Ramer-Douglas-Peucker: keep only points that matter at `tolerance`.
        /// Port of curve_kit.gd simplify/_rdp/_pt_seg_dist (lines 11-47).</summary>
        public static List<Vector2> Simplify(IReadOnlyList<Vector2> pts, float tolerance)
        {
            int n = pts == null ? 0 : pts.Count;
            if (n < 3)
                return Copy(pts);

            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;
            Rdp(pts, 0, n - 1, tolerance, keep);

            var result = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                    result.Add(pts[i]);
            }
            return result;
        }

        private static void Rdp(IReadOnlyList<Vector2> pts, int a, int b, float tol, bool[] keep)
        {
            if (b - a < 2)
                return;

            float maxd = -1f;
            int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = PointSegmentDistance(pts[i], pts[a], pts[b]);
                if (d > maxd)
                {
                    maxd = d;
                    idx = i;
                }
            }
            if (maxd > tol)
            {
                keep[idx] = true;
                Rdp(pts, a, idx, tol, keep);
                Rdp(pts, idx, b, tol, keep);
            }
        }

        private static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            float t = lenSq == 0f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Even arclength resampling — makes smoothing amplitude uniform everywhere.
        /// Port of curve_kit.gd resample (lines 50-79), including the closed-ring
        /// dedup of a final point closer than spacing*0.5 to the start, and the open-path
        /// endpoint append.</summary>
        public static List<Vector2> Resample(IReadOnlyList<Vector2> pts, float spacing, bool closed)
        {
            int n = pts == null ? 0 : pts.Count;
            if (n < 3 || spacing <= k_MinSpacing)
                return Copy(pts);

            var result = new List<Vector2>(n) { pts[0] };
            float acc = 0f;
            int m = closed ? n : n - 1;
            for (int i = 0; i < m; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % n];
                float seg = Vector2.Distance(a, b);
                if (seg < k_DegenerateSegment)
                    continue;
                float t = spacing - acc;
                while (t <= seg)
                {
                    result.Add(Vector2.LerpUnclamped(a, b, t / seg));
                    t += spacing;
                }
                acc = seg - (t - spacing);
            }

            if (closed)
            {
                if (result.Count > 1 &&
                    Vector2.Distance(result[result.Count - 1], result[0]) < spacing * 0.5f)
                    result.RemoveAt(result.Count - 1);
            }
            else if (Vector2.Distance(result[result.Count - 1], pts[n - 1]) > k_EndpointEpsilon)
            {
                result.Add(pts[n - 1]);
            }
            return result;
        }

        /// <summary>Neighbour-averaging smoothing passes (0.5/0.25/0.25 kernel); endpoints
        /// pinned on open strokes, closed loops wrap. Port of curve_kit.gd smooth (84-101).</summary>
        public static List<Vector2> Smooth(IReadOnlyList<Vector2> pts, int passes, bool closed)
        {
            var cur = Copy(pts);
            int n = cur.Count;
            if (n < 3)
                return cur;

            for (int p = 0; p < passes; p++)
            {
                var next = new List<Vector2>(n);
                for (int i = 0; i < n; i++)
                {
                    if (!closed && (i == 0 || i == n - 1))
                    {
                        next.Add(cur[i]);
                    }
                    else
                    {
                        Vector2 prev = cur[(i - 1 + n) % n];
                        Vector2 nxt = cur[(i + 1) % n];
                        next.Add(cur[i] * 0.5f + (prev + nxt) * 0.25f);
                    }
                }
                cur = next;
            }
            return cur;
        }

        /// <summary>Fit a smooth Bézier through raw stroke points: resample → smooth →
        /// simplify → Catmull-Rom tangents ((next - prev) / 6, clamped). smoothPasses &lt;= 0
        /// = CRISP: no smoothing, no handles — geometric shapes keep exact corners.
        /// Closed curves append a duplicate first point (with in-handle, zero out-handle).
        /// Port of curve_kit.gd fit_curve/_clamped_tangent (lines 104-148).</summary>
        public static DrawnCurve FitCurve(IReadOnlyList<Vector2> pts, bool closed,
            float tolerance = 3f, int smoothPasses = 3)
        {
            List<Vector2> even = Resample(pts, Mathf.Max(tolerance, k_FitMinSpacing), closed);
            // smoothPasses <= 0 = CRISP: no smoothing, no bezier handles —
            // geometric shapes (rect holes etc.) keep their exact corners
            List<Vector2> pre = smoothPasses <= 0 ? even : Smooth(even, smoothPasses, closed);
            List<Vector2> simple = Simplify(pre, tolerance);
            int n = simple.Count;
            if (closed && n > 2 && Vector2.Distance(simple[0], simple[n - 1]) < tolerance * 3f)
            {
                simple.RemoveAt(n - 1);
                n -= 1;
            }

            var curve = new DrawnCurve();
            if (n < 2)
                return curve;

            for (int i = 0; i < n; i++)
            {
                Vector2 tangent = Vector2.zero;
                if (smoothPasses > 0)
                {
                    Vector2 prev = closed ? simple[(i - 1 + n) % n] : simple[Mathf.Max(i - 1, 0)];
                    Vector2 next = closed ? simple[(i + 1) % n] : simple[Mathf.Min(i + 1, n - 1)];
                    tangent = ClampedTangent(simple[i], prev, next);
                }
                curve.AddPoint(simple[i], -tangent, tangent);
            }
            if (closed && n >= 3)
            {
                Vector2 t0 = Vector2.zero;
                if (smoothPasses > 0)
                    t0 = ClampedTangent(simple[0], simple[n - 1], simple[1]);
                curve.AddPoint(simple[0], -t0, Vector2.zero);
            }
            return curve;
        }

        /// <summary>Catmull-Rom handle, clamped to the local point spacing: an uneven
        /// simplify (tight cluster next to a far point) would otherwise produce a huge handle
        /// and the curve whips into a spike or loop. Port of _clamped_tangent (143-148).</summary>
        private static Vector2 ClampedTangent(Vector2 p, Vector2 prev, Vector2 next)
        {
            Vector2 tangent = (next - prev) / 6f;
            float lim = Mathf.Min(Vector2.Distance(p, prev), Vector2.Distance(p, next)) * 0.4f;
            if (tangent.magnitude > lim)
                tangent = lim < k_MinTangentLimit ? Vector2.zero : tangent.normalized * lim;
            return tangent;
        }

        /// <summary>Resample a closed ring to EXACTLY count evenly spaced points (morph
        /// blending needs matched counts). Port of curve_kit.gd resample_count (174-200).</summary>
        public static List<Vector2> ResampleCount(IReadOnlyList<Vector2> pts, int count)
        {
            int n = pts == null ? 0 : pts.Count;
            if (n < 3 || count < 3)
                return Copy(pts);

            var lens = new float[n];
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                lens[i] = total;
                total += Vector2.Distance(pts[i], pts[(i + 1) % n]);
            }
            if (total < k_MinRingLength)
                return Copy(pts);

            var result = new List<Vector2>(count);
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                float d = total * (float)k / (float)count;
                while (seg < n - 1 && lens[seg + 1] <= d)
                    seg++;
                Vector2 a = pts[seg];
                Vector2 b = pts[(seg + 1) % n];
                float segLen = Vector2.Distance(a, b);
                float t = segLen < k_DegenerateSegment ? 0f : (d - lens[seg]) / segLen;
                result.Add(Vector2.LerpUnclamped(a, b, Mathf.Clamp01(t)));
            }
            return result;
        }

        /// <summary>Re-index `other` (same size as `reference`) so it best matches point-by-
        /// point: searches start offset and winding with strided early-out cost.
        /// Port of curve_kit.gd align_ring (lines 207-241).</summary>
        public static List<Vector2> AlignRing(IReadOnlyList<Vector2> reference, IReadOnlyList<Vector2> other)
        {
            int n = reference == null ? 0 : reference.Count;
            if (other == null || other.Count != n || n < 3)
                return Copy(other);

            int stride = Mathf.Max(n / 32, 1);
            float bestCost = float.PositiveInfinity;
            int bestOff = 0;
            bool bestRev = false;
            for (int revI = 0; revI < 2; revI++)
            {
                var cand = Copy(other);
                if (revI == 1)
                    cand.Reverse();
                for (int off = 0; off < n; off++)
                {
                    float cost = 0f;
                    int k = 0;
                    bool complete = true;
                    while (k < n)
                    {
                        cost += (cand[(k + off) % n] - reference[k]).sqrMagnitude;
                        if (cost >= bestCost)
                        {
                            complete = false;
                            break;
                        }
                        k += stride;
                    }
                    if (complete && cost < bestCost)
                    {
                        bestCost = cost;
                        bestOff = off;
                        bestRev = revI == 1;
                    }
                }
            }

            var src = Copy(other);
            if (bestRev)
                src.Reverse();
            var result = new List<Vector2>(n);
            for (int k = 0; k < n; k++)
                result.Add(src[(k + bestOff) % n]);
            return result;
        }

        /// <summary>Shift curve control points so their centroid becomes the origin; returns
        /// the centroid. Port of curve_kit.gd recenter_curve (lines 281-290).</summary>
        public static Vector2 RecenterCurve(DrawnCurve curve)
        {
            int n = curve == null ? 0 : curve.pointCount;
            if (n == 0)
                return Vector2.zero;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < n; i++)
                centroid += curve.GetPosition(i);
            centroid /= (float)n;
            for (int i = 0; i < n; i++)
                curve.SetPosition(i, curve.GetPosition(i) - centroid);
            return centroid;
        }

        /// <summary>Fold hole rings into the outer boundary as ONE weakly-simple polygon
        /// (keyhole/bridge trick, return bridge offset 0.05 * unit sideways — scale the
        /// 0.05px Godot hair by 1/32 ≈ 0.0016 world units). Port of keyhole (298-330).</summary>
        public static List<Vector2> Keyhole(IReadOnlyList<Vector2> outer, IReadOnlyList<IReadOnlyList<Vector2>> holes)
        {
            var poly = Copy(outer);
            if (holes == null)
                return poly;

            for (int hi = 0; hi < holes.Count; hi++)
            {
                var h = Copy(holes[hi]);
                if (h.Count < 3 || poly.Count < 3)
                    continue;
                if ((SignedArea(poly) > 0f) == (SignedArea(h) > 0f))
                    h.Reverse();            // hole winds opposite to the outer

                int bi = 0;
                int bj = 0;
                float bd = float.PositiveInfinity;
                for (int i = 0; i < poly.Count; i++)
                {
                    for (int j = 0; j < h.Count; j++)
                    {
                        float d = (poly[i] - h[j]).sqrMagnitude;
                        if (d < bd)
                        {
                            bd = d;
                            bi = i;
                            bj = j;
                        }
                    }
                }

                // the return bridge is offset a hair sideways: a zero-width channel
                // (coincident edges) breaks convex decomposition, taking collision
                // down with it — 0.05px (scaled) keeps the polygon strictly simple
                Vector2 side = Orthogonal((h[bj] - poly[bi]).normalized) * k_KeyholeBridgeOffset;
                var merged = new List<Vector2>(poly.Count + h.Count + 2);
                for (int i = 0; i < bi + 1; i++)
                    merged.Add(poly[i]);
                for (int j = 0; j < h.Count; j++)
                    merged.Add(h[(bj + j) % h.Count]);
                merged.Add(h[bj] + side);
                merged.Add(poly[bi] + side);
                for (int i = bi + 1; i < poly.Count; i++)
                    merged.Add(poly[i]);
                poly = merged;
            }
            return poly;
        }

        /// <summary>Shoelace signed area. Port of curve_kit.gd _area (lines 333-339).</summary>
        public static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            int n = poly == null ? 0 : poly.Count;
            if (n == 0)
                return 0f;

            // accumulated in double so large rings don't lose the sign to float drift
            double a = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i];
                Vector2 q = poly[(i + 1) % n];
                a += (double)p.x * q.y - (double)q.x * p.y;
            }
            return (float)(a * 0.5);
        }

        /// <summary>Boolean-sculpt a closed stroke into an existing shape curve — the
        /// Inkarnate drawing model. Port of curve_kit.gd merge_stroke (lines 359-401),
        /// preserving ALL semantics: baked-ring closing-point dedup (&lt; 0.5px scaled →
        /// use bakeInterval * 0.125 as the dedup epsilon), biggest-outer selection,
        /// Disjoint when union yields &gt;1 outer ring, Inside/Hole detection when
        /// |area(best)| - |area(base)| &lt; insideAreaEpsilon, and the final refit with
        /// smoothPasses = crisp ? 0 : 1 at Mathf.Clamp(tolerance, refitToleranceMin,
        /// refitToleranceMax). The Godot constants (1.0 px², 0.1 px, 2.5 px) are exposed
        /// as parameters because this port is unit-agnostic — callers pass world-unit
        /// values (see DrawToolSettings). `bakeInterval` bakes `existing` (Godot
        /// get_baked_points parity).</summary>
        public static StrokeResult MergeStroke(DrawnCurve existing, IReadOnlyList<Vector2> stroke,
            bool subtract, float tolerance, bool crisp, float bakeInterval,
            float insideAreaEpsilon, float refitToleranceMin, float refitToleranceMax)
        {
            int passes = crisp ? 0 : 3;
            bool hasShape = existing != null && existing.pointCount >= 3;
            if (!hasShape)
            {
                if (subtract)
                    return new StrokeResult { op = StrokeOp.None, curve = null };
                return new StrokeResult
                {
                    op = StrokeOp.Adopt,
                    curve = FitCurve(stroke, true, tolerance, passes)
                };
            }

            List<Vector2> baseRing = existing.GetBakedPoints(bakeInterval);
            // Godot dedups a closing point within 0.5 px of the start; at 1 unit == 32 px
            // and the default bake interval that is bakeInterval * 0.125.
            float closeEpsilon = bakeInterval * 0.125f;
            if (baseRing.Count > 1 &&
                Vector2.Distance(baseRing[0], baseRing[baseRing.Count - 1]) < closeEpsilon)
                baseRing.RemoveAt(baseRing.Count - 1);

            List<List<Vector2>> res = subtract
                ? PolyBool.Difference(baseRing, stroke)
                : PolyBool.Union(baseRing, stroke);

            // keep the biggest outer boundary; holes can't live in a single-curve
            // shape — carve caves as hole rings instead
            var best = new List<Vector2>();
            float bestArea = -1f;
            int outers = 0;
            for (int i = 0; i < res.Count; i++)
            {
                List<Vector2> p = res[i];
                if (PolyBool.IsHole(p))
                    continue;
                outers++;
                float a = Mathf.Abs(SignedArea(p));
                if (a > bestArea)
                {
                    bestArea = a;
                    best = p;
                }
            }

            if (!subtract && outers > 1)
                return new StrokeResult
                {
                    op = StrokeOp.Disjoint,
                    curve = FitCurve(stroke, true, tolerance, passes)
                };
            if (best.Count < 3)
                return new StrokeResult { op = StrokeOp.Cleared, curve = new DrawnCurve() };

            // stroke fully inside: the outline is unchanged, so never refit it.
            // Union-inside = new layered shape; carve-inside = punch a hole.
            if (Mathf.Abs(Mathf.Abs(SignedArea(best)) - Mathf.Abs(SignedArea(baseRing))) < insideAreaEpsilon)
            {
                if (subtract)
                    return new StrokeResult
                    {
                        op = StrokeOp.Hole,
                        curve = FitCurve(stroke, true, tolerance, passes)
                    };
                return new StrokeResult
                {
                    op = StrokeOp.Inside,
                    curve = FitCurve(stroke, true, tolerance, passes)
                };
            }

            // refit tolerance is clamped: resculpting must not simplify the whole
            // outline just because the editor is zoomed out
            return new StrokeResult
            {
                op = StrokeOp.Merged,
                curve = FitCurve(best, true,
                    Mathf.Clamp(tolerance, refitToleranceMin, refitToleranceMax), crisp ? 0 : 1)
            };
        }

        /// <summary>Self-union cleanup: a self-crossing ring (bowtie) breaks triangulation;
        /// union of the polygon with itself returns the same region as simple geometry.
        /// Port of curve_kit.gd clean_polygon (lines 407-419).</summary>
        public static List<Vector2> CleanPolygon(IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3)
                return Copy(poly);

            var best = new List<Vector2>();
            float bestArea = -1f;
            List<List<Vector2>> merged = PolyBool.Union(poly, poly);
            for (int i = 0; i < merged.Count; i++)
            {
                List<Vector2> p = merged[i];
                if (PolyBool.IsHole(p))
                    continue;
                float a = Mathf.Abs(SignedArea(p));
                if (a > bestArea)
                {
                    bestArea = a;
                    best = p;
                }
            }
            return best.Count >= 3 ? best : Copy(poly);
        }

        /// <summary>The area a round brush covers along a stroke: thick round-capped
        /// polyline as one polygon (biggest outer of the round offset). Port of
        /// curve_kit.gd stroke_capsule (lines 426-437). Needed by M2 Paint sculpting.</summary>
        public static List<Vector2> StrokeCapsule(IReadOnlyList<Vector2> pts, float radius)
        {
            if (pts == null || pts.Count == 0 || radius <= 0f)
                return new List<Vector2>();

            var line = Copy(pts);
            if (line.Count == 1)
                line.Add(line[0] + new Vector2(k_CapsuleNudge, 0f));

            var best = new List<Vector2>();
            List<List<Vector2>> offset = PolyBool.OffsetPolyline(line, radius);
            for (int i = 0; i < offset.Count; i++)
            {
                List<Vector2> poly = offset[i];
                if (!PolyBool.IsHole(poly) && poly.Count > best.Count)
                    best = poly;
            }
            return best;
        }

        /// <summary>Organic edge wobble: displace points along normals with 2-octave fractal
        /// noise. Closed loops sample noise on a circle (seamless); open paths fade to zero
        /// at both ends. Port of curve_kit.gd displace (lines 444-478). Exact FastNoiseLite
        /// values are NOT required — seamlessness and amplitude/wavelength behavior are.
        /// Use Mathf.PerlinNoise-based 2-octave fBm remapped to [-1, 1], seed → offset.</summary>
        public static List<Vector2> Displace(IReadOnlyList<Vector2> pts, float amp, float wavelength,
            int noiseSeed, bool closed)
        {
            int n = pts == null ? 0 : pts.Count;
            if (amp <= 0f || n < 3)
                return Copy(pts);

            wavelength = Mathf.Max(wavelength, k_MinWavelength);
            var arc = new float[n];
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                arc[i] = total;
                if (closed || i < n - 1)
                    total += Vector2.Distance(pts[i], pts[(i + 1) % n]);
            }
            if (total < k_MinDisplaceLength)
                return Copy(pts);

            // Sampling radius for the closed case: the circle's circumference is
            // total / wavelength, so the ring crosses exactly one noise period per
            // `wavelength` of arc — and the loop closes seamlessly because cos/sin do.
            float radius = total / (2f * Mathf.PI * wavelength);
            Vector2 noiseOffset = NoiseOffset(noiseSeed);

            var result = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = closed ? pts[(i - 1 + n) % n] : pts[Mathf.Max(i - 1, 0)];
                Vector2 next = closed ? pts[(i + 1) % n] : pts[Mathf.Min(i + 1, n - 1)];
                Vector2 nrm = Orthogonal((next - prev).normalized);
                float s;
                if (closed)
                {
                    float t = 2f * Mathf.PI * arc[i] / total;
                    s = Fbm(Mathf.Cos(t) * radius + noiseOffset.x,
                            Mathf.Sin(t) * radius + noiseOffset.y);
                }
                else
                {
                    s = Fbm(arc[i] / wavelength + noiseOffset.x, noiseOffset.y);
                    // fade the wobble to zero at both ends so joints stay flush
                    s *= Mathf.Clamp01(Mathf.Min(arc[i], total - arc[i]) / wavelength);
                }
                result.Add(pts[i] + nrm * (s * amp));
            }
            return result;
        }

        /// <summary>Clamp a segment tip at the shape outline (only when the segment starts
        /// inside and exits). Port of curve_kit.gd clamp_segment_tip (lines 260-275).
        /// Needed by M3 skinning; included now because it is pure geometry.</summary>
        public static Vector2 ClampSegmentTip(Vector2 a, Vector2 tip, IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3 ||
                !PolyBool.IsPointInPolygon(a, poly) || PolyBool.IsPointInPolygon(tip, poly))
                return tip;

            Vector2 best = tip;
            float bestD = (tip - a).sqrMagnitude;
            for (int i = 0; i < poly.Count; i++)
            {
                if (PolyBool.SegmentIntersectsSegment(a, tip, poly[i], poly[(i + 1) % poly.Count],
                        out Vector2 hit))
                {
                    float d = (hit - a).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = hit;
                    }
                }
            }
            return best;
        }

        // --- internals -------------------------------------------------------------------

        /// <summary>GDScript's PackedVector2Array.duplicate() — always a fresh list, never a
        /// shared reference (null tolerated as empty).</summary>
        private static List<Vector2> Copy(IReadOnlyList<Vector2> pts)
        {
            return pts == null ? new List<Vector2>() : new List<Vector2>(pts);
        }

        /// <summary>Godot Vector2.orthogonal(): 90° rotation, (x, y) -> (y, -x). For a
        /// positive-area ring this points away from the interior.</summary>
        private static Vector2 Orthogonal(Vector2 v)
        {
            return new Vector2(v.y, -v.x);
        }

        /// <summary>2-octave fBm (gain 0.5, lacunarity 2) over Mathf.PerlinNoise, remapped
        /// from Unity's [0, 1] to [-1, 1] and normalised by the amplitude sum — the stand-in
        /// for Godot's FastNoiseLite(frequency 1, fractal_octaves 2, FBM).</summary>
        private static float Fbm(float x, float y)
        {
            float sum = (Mathf.PerlinNoise(x, y) * 2f - 1f)
                + 0.5f * (Mathf.PerlinNoise(x * 2f, y * 2f) * 2f - 1f);
            return sum / 1.5f;
        }

        /// <summary>Mathf.PerlinNoise has no seed input, so the seed becomes a coordinate
        /// offset instead. The offset is kept inside one lattice period (classic Perlin
        /// repeats every 256 units) and always has a fractional part, so distinct seeds give
        /// distinct, non-degenerate patterns and the same seed reproduces the same wobble.</summary>
        private static Vector2 NoiseOffset(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;   // Knuth multiplicative hash
                h ^= h >> 15;
                float ox = (h & 0xFFFFu) / 65535f * 255.5f + 0.317f;
                float oy = ((h >> 16) & 0xFFFFu) / 65535f * 255.5f + 0.719f;
                return new Vector2(ox, oy);
            }
        }
    }
}
