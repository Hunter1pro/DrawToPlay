using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The manifest's ground plane — see <see cref="LevelGroundPlane"/>.
    ///
    /// WORTH TESTING because the bug it fixes was invisible: the spawner and the editor overlay
    /// each decided for themselves which two axes a placement's position meant, disagreed, and the
    /// only symptom was ghosts drawn standing in a wall while the objects spawned correctly
    /// elsewhere. Nothing threw. These lock the mapping down in both directions, so a third caller
    /// cannot quietly invent a fourth answer.
    /// </summary>
    public sealed class LevelGroundPlaneTests
    {
        private LevelObjectRegistry m_Manifest;

        [SetUp]
        public void SetUp()
        {
            m_Manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Manifest);
        }

        /// <summary>A manifest written before the plane existed meant XY, and still has to.</summary>
        [Test]
        public void Plane_DefaultsToXY()
        {
            Assert.AreEqual(LevelGroundPlane.XY, m_Manifest.plane);
            Assert.AreEqual(new Vector3(3f, -4f, 0f), m_Manifest.ToWorld(new Vector2(3f, -4f)));
        }

        /// <summary>In XY the height goes into Z — the axis nothing else is using.</summary>
        [Test]
        public void ToWorld_XYPutsHeightOnZ()
        {
            Assert.AreEqual(new Vector3(3f, -4f, 0.5f),
                m_Manifest.ToWorld(new Vector2(3f, -4f), 0.5f));
        }

        /// <summary>In XZ the row's second number is DEPTH, and the height is the up axis. This is
        /// the mapping the outpost spawner used by hand and the overlay did not.</summary>
        [Test]
        public void ToWorld_XZReadsTheSecondNumberAsDepth()
        {
            m_Manifest.plane = LevelGroundPlane.XZ;
            Assert.AreEqual(new Vector3(3f, 0.1f, -4f),
                m_Manifest.ToWorld(new Vector2(3f, -4f), 0.1f));
        }

        /// <summary>What a dragged handle writes has to be what the row said, or moving an object
        /// and letting go moves it somewhere else.</summary>
        [Test]
        public void ToPlan_RoundTripsToWorld([Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)]
            LevelGroundPlane plane)
        {
            m_Manifest.plane = plane;
            var position = new Vector2(2.5f, -7.25f);
            Assert.AreEqual(position, m_Manifest.ToPlan(m_Manifest.ToWorld(position, 1.75f)));
        }

        /// <summary>The height is dropped on the way back, because the row never held one — a
        /// pickup that floats must not write its float into its ground position.</summary>
        [Test]
        public void ToPlan_DiscardsHeight()
        {
            m_Manifest.plane = LevelGroundPlane.XZ;
            Assert.AreEqual(new Vector2(3f, -4f), m_Manifest.ToPlan(new Vector3(3f, 99f, -4f)));
        }

        /// <summary>Zero facing must leave a prefab's own rotation alone in XZ: that is what the
        /// outpost spawner did before this existed, and every authored row assumes it.</summary>
        [Test]
        public void Facing_ZeroIsIdentity([Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)]
            LevelGroundPlane plane)
        {
            m_Manifest.plane = plane;
            Assert.AreEqual(Quaternion.identity, m_Manifest.Facing(0f));
        }

        /// <summary>XZ turns about up, matching Quaternion.Euler(0, facing, 0) — the expression the
        /// spawner used to hold, so no authored facing changes meaning.</summary>
        [Test]
        public void Facing_XZTurnsAboutUp()
        {
            m_Manifest.plane = LevelGroundPlane.XZ;
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(0f, 137f, 0f),
                m_Manifest.Facing(137f)), 0.001f);
        }

        /// <summary>A facing points somewhere IN the level's plane, never out of it — otherwise
        /// "which way is he looking" is a question with a 3D answer in a 2D game.</summary>
        [Test]
        public void Forward_StaysInThePlane()
        {
            Assert.AreEqual(0f, m_Manifest.Forward(64f).z, 0.001f, "XY facing must not leave XY");

            m_Manifest.plane = LevelGroundPlane.XZ;
            Assert.AreEqual(0f, m_Manifest.Forward(64f).y, 0.001f, "XZ facing must not leave XZ");
        }

        /// <summary>Half a turn looks the other way — the ordinary edit ("turn him round"), and
        /// the one that has to be exact rather than approximately opposite.</summary>
        [Test]
        public void Forward_HalfATurnReverses([Values(LevelGroundPlane.XY, LevelGroundPlane.XZ)]
            LevelGroundPlane plane)
        {
            m_Manifest.plane = plane;
            Assert.AreEqual(-1f, Vector3.Dot(m_Manifest.Forward(0f), m_Manifest.Forward(180f)),
                0.001f);
        }
    }
}
