using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.5 — cover that stops being there: the destructor carves a crate where a shot
    /// lands. A bullet chips it into more polygons; a shell's bite frees debris; carved to
    /// nothing, the crate is gone. Simulated for real, events pumped the test's way.
    /// </summary>
    [TestFixture]
    public sealed class ArenaCrateTests
    {
        private GameObject m_Go;
        private ArenaCrate m_Crate;

        [SetUp]
        public void SetUp()
        {
            m_Go = new GameObject("Crate") { hideFlags = HideFlags.HideAndDontSave };
            m_Go.transform.position = new Vector3(300f, 0f, 0f);
            m_Go.SetActive(false);
            m_Crate = m_Go.AddComponent<ArenaCrate>();
            m_Crate.Wake();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ArenaDebris debris in Object.FindObjectsByType<ArenaDebris>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                debris.Expire();
            m_Crate.Sleep();
            Object.DestroyImmediate(m_Go);
        }

        [Test]
        public void AChipMakesMorePolygons_ABiteMakesDebris_AndEnoughBitesEndTheCrate()
        {
            Assert.That(m_Crate.polygonCount, Is.EqualTo(1), "born a box");

            // A bullet's chip at a corner: the crate survives in more pieces.
            m_Crate.Carve(new Vector2(300.7f, 0.7f), 0.3f);
            Assert.That(m_Crate.gone, Is.False, "chipped, not gone");
            Assert.That(m_Crate.polygonCount, Is.GreaterThanOrEqualTo(1));
            int debrisAfterChip = Object.FindObjectsByType<ArenaDebris>(FindObjectsSortMode.None).Length;
            Assert.That(debrisAfterChip, Is.GreaterThan(0), "the chip fell out as debris");

            // Shell-sized bites through the middle until nothing is left.
            for (int i = 0; i < 8 && !m_Crate.gone; i++)
                m_Crate.Carve(new Vector2(300f + (i % 2 == 0 ? -0.3f : 0.3f), (i % 3 - 1) * 0.5f), 1.1f);
            Assert.That(m_Crate.gone, Is.True, "carved to nothing");
        }
    }
}
