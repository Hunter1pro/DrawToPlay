using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// DRAW THESE AS THE KIND'S OPTIONS (M34) — the placement's attribute list, shown the way a
    /// device shows its details panel: every option the def declares, with the value it would
    /// have, and a tick where this one differs.
    ///
    /// The list underneath is unchanged — rows only exist for what is actually overridden. What
    /// changes is that an author no longer types a name to find out an option exists; the panel
    /// says what there is, which is the whole difference between data and a form.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class PlacementAttributesAttribute : PropertyAttribute
    {
        /// <summary>The sibling field naming this placement's kind — where the declarations
        /// come from.</summary>
        public readonly string kindField;

        public PlacementAttributesAttribute(string kindField)
        {
            this.kindField = kindField ?? "";
        }
    }
}
