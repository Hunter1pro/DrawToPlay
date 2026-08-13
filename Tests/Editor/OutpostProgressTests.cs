using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The list that decides whether a level rebuilds a row — see
    /// <see cref="OutpostProgressService"/>.
    ///
    /// Worth its own suite because the failure is invisible in the small: everything works, the
    /// raider dies, and the only symptom is that it is alive again next time. The demo shipped
    /// exactly that bug for pickups — taken, destroyed, and handed straight back on the next load.
    /// </summary>
    public sealed class OutpostProgressTests
    {
        [Test]
        public void MarkGone_IsRememberedAndIdempotent()
        {
            var progress = new OutpostProgressService();
            var changes = 0;
            progress.changed += () => changes++;

            Assert.IsFalse(progress.IsGone("place.raider"), "nothing is gone to begin with");

            progress.MarkGone("place.raider");
            Assert.IsTrue(progress.IsGone("place.raider"));
            Assert.AreEqual(1, changes, "writing something off is a change worth saving");

            progress.MarkGone("place.raider");
            Assert.AreEqual(1, changes,
                "dying twice is not two changes — a second announcement is a second file write");
        }

        /// <summary>An empty or absent id is not a placement, and must not become one — a row
        /// with no id would otherwise write off every other row with no id.</summary>
        [Test]
        public void EmptyIdIsNeverGone()
        {
            var progress = new OutpostProgressService();
            progress.MarkGone("");
            progress.MarkGone(null);

            Assert.IsFalse(progress.IsGone(""));
            Assert.IsFalse(progress.IsGone(null));
            Assert.AreEqual(0, progress.gone.Count);
        }

        /// <summary>What a save file puts back, and what it reports for writing out.</summary>
        [Test]
        public void Restore_ReplacesEverything()
        {
            var progress = new OutpostProgressService();
            progress.MarkGone("place.old");

            progress.Restore(new List<string> { "place.raider", "place.relic" });

            Assert.IsTrue(progress.IsGone("place.raider"));
            Assert.IsTrue(progress.IsGone("place.relic"));
            Assert.IsFalse(progress.IsGone("place.old"),
                "a restore is the whole state, not an addition to whatever was there");
            Assert.AreEqual(2, progress.gone.Count);
        }

        /// <summary>Restoring nothing is a new game, not a crash.</summary>
        [Test]
        public void Restore_HandlesNothing()
        {
            var progress = new OutpostProgressService();
            progress.MarkGone("place.raider");

            progress.Restore(null);

            Assert.AreEqual(0, progress.gone.Count);
        }
    }
}
