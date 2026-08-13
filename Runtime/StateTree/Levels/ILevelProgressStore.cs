using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A GAME'S SAVED PROGRESS, as much of it as a level editor needs to be able to throw away.
    ///
    /// WHY THE TOOLSET DEFINES THIS. A manifest is what a level was authored as; a save is what
    /// has happened to it since, and the two disagree constantly while you are building a level —
    /// you kill the raider to test the drop, and now the raider is not there to test anything
    /// else. The fix is a button next to the rows, and a button next to the rows needs the
    /// EDITOR to be able to reach a save that only the GAME knows the shape of.
    ///
    /// So the toolset asks for the smallest possible thing: a name, whether there is anything to
    /// clear, and three ways to clear it. Everything about where a save lives, what is in it and
    /// how it is written belongs to whoever implements this — the Level Manifest overlay finds
    /// implementations by type and never learns any of it.
    ///
    /// IT WORKS ON THE FILE, NOT ON A RUNNING GAME. An author presses these while editing, with
    /// nothing playing, so an implementation reads and writes its own storage directly rather
    /// than expecting a live service to exist.
    ///
    /// Implementations need a public parameterless constructor: the overlay makes one to ask.
    /// </summary>
    public interface ILevelProgressStore
    {
        /// <summary>What to call this save in the UI — a game's name, not a file name.</summary>
        string displayName { get; }

        /// <summary>Whether there is anything saved at all. False hides the buttons rather than
        /// offering to delete nothing.</summary>
        bool hasSave { get; }

        /// <summary>Everything this save holds, gone.</summary>
        void ClearAll();

        /// <summary>
        /// Forget what happened to ONE level, leaving the rest of the session alone — so testing
        /// a fight does not cost the items and the conversations that got you there.
        /// </summary>
        /// <param name="placementIds">The ids of that level's manifest rows. A save keyed on
        /// placements can simply drop these; one keyed on level names can ignore them and use its
        /// own bookkeeping.</param>
        /// <param name="levelName">The level, for a save that keys on names.</param>
        void ClearLevel(IEnumerable<string> placementIds, string levelName);

        /// <summary>
        /// Forget the SHARED half — what is carried and what has been said — leaving every level's
        /// own state alone. The opposite test to the one above: walk into a finished level with
        /// empty pockets.
        /// </summary>
        void ClearShared();
    }
}
