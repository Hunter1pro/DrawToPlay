using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The screen-edge arrow's MATH, pure and testable: given a target's viewport point,
    /// decide whether it is off screen and where on the edge (with a margin) the pointer
    /// sits, aimed from the screen's centre toward the target. A target BEHIND the camera
    /// projects mirrored, so its direction is flipped before clamping — the arrow points
    /// where you should turn, not at the projection artifact.
    /// </summary>
    public static class OffscreenIcon
    {
        /// <summary>
        /// True when the icon should show. <paramref name="anchor01"/> is the icon's
        /// position in 0..1 viewport space (y UP — the caller converts to its canvas),
        /// clamped to the edge minus <paramref name="margin01"/>;
        /// <paramref name="angleDegrees"/> is the direction from screen centre toward the
        /// target, counter-clockwise from +x, in viewport space.
        /// </summary>
        public static bool Resolve(Vector3 viewport, float margin01,
            out Vector2 anchor01, out float angleDegrees)
        {
            var behind = viewport.z < 0f;
            var direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (behind)
                direction = -direction;

            var onScreen = !behind
                && viewport.x >= 0f && viewport.x <= 1f
                && viewport.y >= 0f && viewport.y <= 1f;
            if (onScreen || direction.sqrMagnitude < 0.000001f)
            {
                anchor01 = new Vector2(0.5f, 0.5f);
                angleDegrees = 0f;
                return false;
            }

            var half = Mathf.Max(0.05f, 0.5f - margin01);
            var scale = Mathf.Max(Mathf.Abs(direction.x) / half,
                Mathf.Abs(direction.y) / half);
            anchor01 = new Vector2(0.5f, 0.5f) + direction / scale;
            angleDegrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            return true;
        }
    }
}
