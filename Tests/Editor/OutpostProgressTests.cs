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

        // ---------------------------------------------------------- what a level GAINED

        /// <summary>A drop belongs to the level it fell in, and to no other. Getting this wrong
        /// rains one level's loot onto another's floor.</summary>
        [Test]
        public void Gained_IsCollectedPerLevel()
        {
            var progress = new OutpostProgressService();
            progress.MarkGained(Drop("drop.1", "yard", "keycard"));
            progress.MarkGained(Drop("drop.2", "cellar", "relic"));

            var here = new List<OutpostGainedObject>();
            progress.CollectGained("yard", here);

            Assert.AreEqual(1, here.Count);
            Assert.AreEqual("keycard", here[0].entry);
        }

        /// <summary>Picking a drop up is the end of it — it must not be rebuilt on the next load,
        /// and it must not sit in the save forever behind a skip check.</summary>
        [Test]
        public void GoneRemovesAGain()
        {
            var progress = new OutpostProgressService();
            progress.MarkGained(Drop("drop.1", "yard", "keycard"));

            progress.MarkGone("drop.1");

            var here = new List<OutpostGainedObject>();
            progress.CollectGained("yard", here);
            Assert.AreEqual(0, here.Count, "a spent drop is not rebuilt");
            Assert.AreEqual(0, progress.gained.Count, "and it does not linger in the save");
            Assert.IsTrue(progress.IsGone("drop.1"));
        }

        /// <summary>The same drop written down twice is one drop — an object that moved or
        /// restacked, not a second one.</summary>
        [Test]
        public void Gained_SameIdUpdatesRatherThanDuplicates()
        {
            var progress = new OutpostProgressService();
            progress.MarkGained(Drop("drop.1", "yard", "keycard"));

            OutpostGainedObject moved = Drop("drop.1", "yard", "keycard");
            moved.x = 9f;
            progress.MarkGained(moved);

            Assert.AreEqual(1, progress.gained.Count);
            Assert.AreEqual(9f, progress.gained[0].x);
        }

        /// <summary>Something already spent cannot be re-gained — otherwise a stale spawn event
        /// resurrects what the player just picked up.</summary>
        [Test]
        public void Gained_IgnoresWhatIsAlreadyGone()
        {
            var progress = new OutpostProgressService();
            progress.MarkGone("drop.1");

            progress.MarkGained(Drop("drop.1", "yard", "keycard"));

            Assert.AreEqual(0, progress.gained.Count);
        }

        private static OutpostGainedObject Drop(string id, string level, string entry)
        {
            return new OutpostGainedObject
            {
                id = id, level = level, entry = entry, kind = "pickup", count = 1
            };
        }
    }
}
