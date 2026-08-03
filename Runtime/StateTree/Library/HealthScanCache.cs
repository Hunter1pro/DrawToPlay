using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Process-wide cache of every live <see cref="HealthComponent"/>, refreshed by polling
    /// <c>FindObjectsByType</c> at most once per interval. This is the perception source for
    /// <see cref="TargetDetectedCondition"/>.
    ///
    /// WHY A POLL AND NOT A REGISTRY: the obvious design is a static list maintained in
    /// HealthComponent.OnEnable/OnDisable — O(1) per entity, always exact. HealthComponent is
    /// frozen M5 code that this milestone may not edit, so the perception layer polls instead.
    ///
    /// COST (the number to keep an eye on): <c>FindObjectsByType</c> walks every loaded
    /// component of the type and allocates a fresh array each call — roughly O(total objects
    /// in the scene), tens of microseconds at demo scale and a garbage allocation of
    /// <c>sizeof(ref) * healthCount</c>. Caching turns "one scan per detector per frame" into
    /// "one scan per interval for the WHOLE scene": at 60 fps, 0.25 s and 20 zombies that is
    /// 4 scans/second instead of 1200. The 0.25 s staleness shows up as up to a quarter second
    /// of extra reaction time when a target spawns — deliberate, and the same order as the
    /// perception delay a real AI wants anyway. A future milestone that owns HealthComponent
    /// should replace the body of <see cref="Scan"/> with the OnEnable/OnDisable registry and
    /// delete the interval; nothing else in the library changes.
    ///
    /// SHARED BUDGET: all detectors share one timer. Whichever component triggers a refresh
    /// sets the next deadline from ITS interval, so mixing intervals gives the last refresher's
    /// spacing rather than the shortest requested one — acceptable at authoring scale, and the
    /// reason the interval is documented on the component as a hint rather than a guarantee.
    ///
    /// STALENESS: entries can be destroyed between scans. The returned list is never
    /// guaranteed clean — <b>callers must null-check every entry</b>.
    ///
    /// EDIT MODE: <see cref="Time.realtimeSinceStartup"/> rather than <c>Time.time</c>, so the
    /// interval also advances outside play mode (an inspector-driven condition evaluation
    /// re-scans on schedule instead of pinning the first result forever).
    /// </summary>
    public static class HealthScanCache
    {
        /// <summary>The interval M6 conventions specify for perception scans.</summary>
        public const float DefaultInterval = 0.25f;

        private static readonly List<HealthComponent> s_Cached = new List<HealthComponent>();
        private static float s_NextScanTime = float.NegativeInfinity;

        /// <summary>Live health components, refreshed at most once per
        /// <paramref name="interval"/> seconds. The returned list is the internal buffer:
        /// read it, never mutate it, never hold it across frames.</summary>
        public static List<HealthComponent> Scan(float interval)
        {
            float now = Time.realtimeSinceStartup;
            if (now < s_NextScanTime)
                return s_Cached;

            s_NextScanTime = now + Mathf.Max(interval, 0f);

            // FindObjectsInactive.Exclude: an entity whose GameObject is disabled is not a
            // valid target anyway. The FindObjectsSortMode overload M6 conventions name is
            // [Obsolete] in 6000.5 ("InstanceID will be replaced ... previous sort order
            // cannot be maintained") and m0 conventions forbid obsolete members; this
            // overload is the documented replacement and is already unsorted, which is what
            // FindObjectsSortMode.None asked for.
            HealthComponent[] found =
                Object.FindObjectsByType<HealthComponent>(FindObjectsInactive.Exclude);

            s_Cached.Clear();
            for (int i = 0; i < found.Length; i++)
            {
                HealthComponent health = found[i];
                if (health != null && health.isActiveAndEnabled)
                    s_Cached.Add(health);
            }
            return s_Cached;
        }

        /// <summary>Drop the cache so the next <see cref="Scan"/> rebuilds immediately —
        /// for a scene load or a test that just spawned its actors and cannot wait out the
        /// interval.</summary>
        public static void Invalidate()
        {
            s_Cached.Clear();
            s_NextScanTime = float.NegativeInfinity;
        }
    }
}
