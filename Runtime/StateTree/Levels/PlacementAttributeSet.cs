using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THIS PLACEMENT'S OPTIONS (M34) — the attribute values set on one placed object, held in a
    /// wrapper rather than as a bare list for the same reason
    /// <see cref="GraphTaskParameterSet"/> is: a bare list draws as a list, and a list is the
    /// wrong shape for a form.
    ///
    /// What an author wants to see is every option the kind declares with the value it would
    /// have, and a tick where this one differs — a details panel. Unity gives an attribute on a
    /// bare <c>List</c> field to the list's ELEMENTS, so the panel can only exist if the field
    /// is one object. That is the whole reason this type is here; the rows underneath are
    /// unchanged, and absent still means "follow the def".
    /// </summary>
    [System.Serializable]
    public sealed class PlacementAttributeSet
    {
        /// <summary>The overridden attributes, one row each. Rows for attributes nobody touched
        /// are not stored — the body's own seed stands.</summary>
        public List<PlacementAttribute> values = new List<PlacementAttribute>();

        /// <summary>True when this placement takes every number from its def, which is the
        /// ordinary case and the one worth skipping work for.</summary>
        public bool isEmpty => values == null || values.Count == 0;

    }
}
