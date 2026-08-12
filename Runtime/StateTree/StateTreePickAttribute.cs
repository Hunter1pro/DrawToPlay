using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Marks a <see cref="StateTreeAsset"/> field that should be CHOSEN rather than dragged —
    /// drawn as the project's searchable picker instead of an asset slot.
    ///
    /// WHY OPT IN RATHER THAN EVERYWHERE. An object slot is the right control when the author
    /// already has the asset in hand: they drag it from a folder they were just looking at. It is
    /// the wrong one when the answer is "whichever of the project's forty trees is the guard's" —
    /// then the question is a search, and Unity's object picker searches names only, flat, with no
    /// idea what a tree IS. So the fields where choosing is the real gesture say so, and the rest
    /// keep the slot they have always had.
    ///
    /// The picker lists every tree in the project, foldered by where it lives and described by its
    /// kind, through the same window the task list and the registry rows use.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StateTreePickAttribute : PropertyAttribute
    {
    }
}
