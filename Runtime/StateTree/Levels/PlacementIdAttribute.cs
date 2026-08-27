using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THIS STRING IS A PLACEMENT — a manifest row's id, picked from the manifests the asking
    /// asset declares rather than typed. The sibling of <see cref="WorldTagAttribute"/>: a beat
    /// that makes 'the hall's north door' live, a task that waits for the player to reach it,
    /// both name a row, and a row id spelled from memory is a wait that never ends.
    ///
    /// Works on a string field and on each element of a <c>List&lt;string&gt;</c>.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class PlacementIdAttribute : PropertyAttribute
    {
    }
}
