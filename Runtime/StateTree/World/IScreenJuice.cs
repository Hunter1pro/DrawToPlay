using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE SCREEN'S SHARE OF AN IMPACT, as a capability (meta-rule 5): whatever renders the
    /// world offers shake, bump and a zoom kick — and the camera it renders with — under this
    /// name, provided on its scope at birth. A feel skin, a blast, a cue asks for the
    /// CAPABILITY; none of them knows which camera class answered, and none of them ever
    /// scans a scene to find out.
    /// </summary>
    public interface IScreenJuice
    {
        /// <summary>Add trauma — shakes hard first, dies on its own.</summary>
        void Shake(float amount);

        /// <summary>Abruptly offset the frame; it eases back on its own.</summary>
        void Bump(Vector2 offset);

        /// <summary>A brief zoom kick — the landing's punch.</summary>
        void BumpZoom(float amount);

        /// <summary>The camera the juice rides on — for world-to-screen maths, never for reaching
        /// around this interface.</summary>
        Camera rendering { get; }
    }
}
