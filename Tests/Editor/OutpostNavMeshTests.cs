using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The level bakes itself at load: a surface with no data gets a mesh from the geometry
    /// that is actually there; one that already holds data (an asset someone baked) is left
    /// alone. A build-time bake used to leave a reference to an object nobody saved.
    /// </summary>
    [TestFixture]
    public sealed class OutpostNavMeshTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private readonly List<NavMeshSurface> m_Surfaces = new List<NavMeshSurface>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Surfaces.Count; i++)
            {
                if (m_Surfaces[i] != null)
                    m_Surfaces[i].RemoveData();
            }
            m_Surfaces.Clear();
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ALevelWithNoMesh_BakesOneFromItsOwnGround_AndOneWithAMeshIsLeftAlone()
        {
            var level = new GameObject("Level");
            m_Junk.Add(level);
            level.transform.position = new Vector3(100f, 0f, 100f);   // away from any other mesh
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(level.transform, false);
            ground.isStatic = true;
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            m_Surfaces.Add(surface);
            Assert.That(surface.navMeshData, Is.Null, "as a built scene holds it after a restart");

            OutpostLevelServices.EnsureNavMesh(level);
            Assert.That(surface.navMeshData, Is.Not.Null, "baked at load");
            Assert.That(NavMesh.SamplePosition(level.transform.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas),
                Is.True, "and it is the live mesh under the level");
            Assert.That(hit.position.x, Is.EqualTo(100f).Within(0.05f));

            NavMeshData first = surface.navMeshData;
            OutpostLevelServices.EnsureNavMesh(level);
            Assert.That(surface.navMeshData, Is.SameAs(first), "a level that has a mesh keeps it");
        }
    }
}
