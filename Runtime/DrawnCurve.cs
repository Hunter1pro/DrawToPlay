using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Serializable cubic-Bézier path — the C# stand-in for Godot's Curve2D as used by the
    /// terrain_paint toolset. Control handles are stored relative to their point, matching
    /// Godot's point_in/point_out convention. Closed rings follow the Godot fit_curve
    /// convention: the last control point duplicates the first position (see DrawKit.FitCurve).
    /// </summary>
    [Serializable]
    public sealed class DrawnCurve
    {
        [Serializable]
        public struct ControlPoint
        {
            public Vector2 position;
            public Vector2 inHandle;   // relative to position, Godot point_in
            public Vector2 outHandle;  // relative to position, Godot point_out

            public ControlPoint(Vector2 position, Vector2 inHandle, Vector2 outHandle)
            {
                this.position = position;
                this.inHandle = inHandle;
                this.outHandle = outHandle;
            }
        }

        [SerializeField] private List<ControlPoint> m_Points = new List<ControlPoint>();

        public int pointCount => m_Points.Count;
        public bool isEmpty => m_Points.Count == 0;

        public ControlPoint this[int index]
        {
            get => m_Points[index];
            set => m_Points[index] = value;
        }

        public void AddPoint(Vector2 position, Vector2 inHandle, Vector2 outHandle)
            => m_Points.Add(new ControlPoint(position, inHandle, outHandle));

        public void AddPoint(Vector2 position)
            => AddPoint(position, Vector2.zero, Vector2.zero);

        public void Clear() => m_Points.Clear();

        public Vector2 GetPosition(int index) => m_Points[index].position;

        public void SetPosition(int index, Vector2 position)
        {
            var p = m_Points[index];
            p.position = position;
            m_Points[index] = p;
        }

        public DrawnCurve Clone()
        {
            var c = new DrawnCurve();
            c.m_Points.AddRange(m_Points);
            return c;
        }

        public void CopyFrom(DrawnCurve other)
        {
            m_Points.Clear();
            if (other != null)
                m_Points.AddRange(other.m_Points);
        }

        /// <summary>
        /// Control-point signature (positions + handles) — cheap change detection.
        /// Port of curve_kit.gd curve_sig.
        /// </summary>
        public float[] Signature()
        {
            var sig = new float[m_Points.Count * 6];
            for (int i = 0; i < m_Points.Count; i++)
            {
                var p = m_Points[i];
                int k = i * 6;
                sig[k] = p.position.x;
                sig[k + 1] = p.position.y;
                sig[k + 2] = p.inHandle.x;
                sig[k + 3] = p.inHandle.y;
                sig[k + 4] = p.outHandle.x;
                sig[k + 5] = p.outHandle.y;
            }
            return sig;
        }

        /// <summary>
        /// Sample the path into points spaced evenly ~interval apart (Godot
        /// Curve2D.get_baked_points parity: dense cubic subdivision followed by even
        /// arclength resampling via DrawKit.Resample). A closed ring is represented by
        /// the fit_curve convention (last control point == first position), so no
        /// closed flag is needed here.
        /// </summary>
        public List<Vector2> GetBakedPoints(float interval)
        {
            var dense = new List<Vector2>();
            int n = m_Points.Count;
            if (n == 0)
                return dense;
            dense.Add(m_Points[0].position);
            for (int i = 0; i < n - 1; i++)
            {
                var a = m_Points[i];
                var b = m_Points[i + 1];
                Vector2 p0 = a.position;
                Vector2 p1 = a.position + a.outHandle;
                Vector2 p2 = b.position + b.inHandle;
                Vector2 p3 = b.position;
                float chord = Vector2.Distance(p0, p3) + a.outHandle.magnitude + b.inHandle.magnitude;
                int steps = Mathf.Clamp(Mathf.CeilToInt(chord / Mathf.Max(interval * 0.25f, 1e-4f)), 1, 256);
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    dense.Add(EvaluateCubic(p0, p1, p2, p3, t));
                }
            }
            return DrawKit.Resample(dense, interval, false);
        }

        private static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0
                + 3f * u * u * t * p1
                + 3f * u * t * t * p2
                + t * t * t * p3;
        }
    }
}
