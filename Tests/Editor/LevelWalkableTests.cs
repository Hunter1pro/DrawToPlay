using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEngine;
using UnityEngine.AI;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A point stands on the walkable area when the NavMesh is within a metre across, off it beyond, and there is
    /// nothing to stand on where no NavMesh is near. On a floor built in memory far from any open scene's.
    /// </summary>
    [TestFixture]
    public sealed class LevelWalkableTests
    {
        private static readonly Vector3 Far = new Vector3(5000f, 0f, 5000f);
        private NavMeshDataInstance m_Instance;
        private NavMeshData m_Data;

        [SetUp]
        public void SetUp()
        {
            var floor = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(10f, 0.2f, 10f),
                transform = Matrix4x4.Translate(Far + Vector3.down * 0.1f),
                area = 0
            };
            m_Data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { floor },
                new Bounds(Far, new Vector3(20f, 4f, 20f)), Vector3.zero, Quaternion.identity);
            m_Instance = NavMesh.AddNavMeshData(m_Data);
        }

        [TearDown]
        public void TearDown()
        {
            m_Instance.Remove();
            Object.DestroyImmediate(m_Data);
        }

        [Test]
        public void OnTheFloor_IsOnIt_BesideItIsOff_FarFromAnyIsNoWalkableArea()
        {
            Assert.AreEqual(LevelWalkable.Standing.OnIt, LevelWalkable.Of(Far));
            Assert.AreEqual(LevelWalkable.Standing.OnIt, LevelWalkable.Of(Far + Vector3.up * 1.5f), "a storey's slack");
            Assert.AreEqual(LevelWalkable.Standing.OffIt, LevelWalkable.Of(Far + new Vector3(12f, 0f, 0f), out Vector3 nearest));
            Assert.Less(LevelWalkable.Across(nearest, Far), 5.5f, "the nearest point is on the floor");
            Assert.AreEqual(LevelWalkable.Standing.NoWalkableArea, LevelWalkable.Of(-Far));
        }
    }
}
