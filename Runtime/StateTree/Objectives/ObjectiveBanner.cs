using Unity.Properties;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHAT THE QUEST LINE SAYS, as a thing to BIND to (M34).
    ///
    /// The widget used to compute this itself, every frame, from three service properties: what
    /// the current objective is called, which zone it belongs to, how far along it is. That is a
    /// view knowing the domain's shape AND polling it — the two habits the UI doctrine exists to
    /// end.
    ///
    /// [CreateProperty] IS THE WHOLE CONTRACT with UI Toolkit's binding: fields are picked up
    /// automatically (which is why the payload classes that already bind are fields), a PROPERTY
    /// is not — and a binding to an unmarked property fails silently, which cost this pass a
    /// live probe to notice.
    ///
    /// So the subsystem publishes the sentence instead. The view binds a label to a property
    /// path and stops having an opinion; the service updates it when something actually changes,
    /// which is the same event it already raises for everything else.
    /// </summary>
    public sealed class ObjectiveBanner
    {
        /// <summary>The line on the banner: the objective's name, with its count when it has
        /// one ("Drive off the raider  1 / 3"). Empty when nothing is asked.</summary>
        [CreateProperty]
        public string title { get; internal set; } = "";

        /// <summary>Which stack it belongs to — the zone's display name, or empty.</summary>
        [CreateProperty]
        public string zone { get; internal set; } = "";

        /// <summary>The objective's accent colour, for the parts of a skin that are not text.
        /// Kept here rather than read back off the row so a skin never touches a def.</summary>
        [CreateProperty]
        public Color accent { get; internal set; } = Color.white;

        /// <summary>Whether anything is being asked at all — what a skin shows or hides on.</summary>
        [CreateProperty]
        public bool asking { get; internal set; }
    }
}
