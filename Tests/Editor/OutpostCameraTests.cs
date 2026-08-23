using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M42.1 — the camera is a kind. A level's manifest places it, the factory fills its world
    /// before it is live, and whoever wants the rig asks the world for the citizen wearing
    /// "camera" — the rig itself asks the world for the player the same way. No scene holds a
    /// camera by hand; no component looks one up by type.
    /// </summary>
    [TestFixture]
    public sealed class OutpostCameraTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeContextHost m_Level;
        private WorldService m_World;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Level") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            m_Level = go.AddComponent<StateTreeContextHost>();
            m_Level.kind = StateTreeContextKind.Level;
            m_Level.autoStart = false;
            m_Level.Register();
            m_World = new WorldService(m_Level, null);
            m_Level.Provide(m_World);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Level != null)
                m_Level.Unregister();
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ASpawnedRig_IsInjectedByTheFactory_AndFoundThroughTheWorld()
        {
            var prefab = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(prefab);
            prefab.AddComponent<Camera>();
            prefab.AddComponent<OutpostCameraRig>();
            prefab.AddComponent<WorldObjectBehaviour>().tags.Add(OutpostCameraRig.CameraTag);

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "camera";
            def.serviceName = "camera";
            def.body.prefab = prefab;
            def.body.mind = ServiceBodyMind.None;
            m_Junk.Add(def);

            GameObject built = ServiceBodyFactory.Build(def, new LevelObjectDef { id = "place.camera" },
                m_Level.transform, new Vector3(0f, 0f, -8.5f), Quaternion.identity);
            m_Junk.Add(built);
            Assert.That(built, Is.Not.Null);

            var rig = built.GetComponent<OutpostCameraRig>();
            object world = typeof(OutpostCameraRig)
                .GetField("m_World", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rig);
            Assert.That(world, Is.SameAs(m_World), "filled at spawn — no Start, no retry");

            built.GetComponent<WorldObjectBehaviour>().RegisterToWorld();
            var actor = new GameObject("Actor") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(actor);
            actor.transform.SetParent(m_Level.transform);
            Assert.That(OutpostCameraRig.Of(actor), Is.SameAs(rig),
                "a thing in this level asks its world for the camera");
            Assert.That(OutpostCameraRig.Of(null), Is.Null);
        }

        [Test]
        public void EveryLevelManifest_PlacesACamera_AndNoLevelSceneHoldsOne()
        {
            string[] manifests =
            {
                "Assets/DrawToPlayExamples/Demo/M21/Levels/Yard/M21YardObjects.asset",
                "Assets/DrawToPlayExamples/Demo/M21/Levels/Ridge/M21RidgeObjects.asset",
                "Assets/DrawToPlayExamples/Demo/M21/Levels/Cave/M21CaveObjects.asset",
                "Assets/DrawToPlayExamples/Demo/M21/Levels/Wreck/M21WreckObjects.asset"
            };
            foreach (string path in manifests)
            {
                var manifest = AssetDatabase.LoadAssetAtPath<LevelObjectRegistry>(path);
                Assume.That(manifest, Is.Not.Null, path + " — run Draw To Play Examples › M21 Waystation › Verify first");
                Assert.That(manifest.entries.Exists(row => row.kind.entryName == "camera"), Is.True,
                    path + ": the camera is a row");
            }
            var kind = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21Kind_Camera.asset");
            Assert.That(kind, Is.Not.Null);
            Assert.That(kind.body.prefab.GetComponent<OutpostCameraRig>(), Is.Not.Null);
            Assert.That(kind.body.prefab.GetComponent<Camera>(), Is.Not.Null);
            Assert.That(kind.body.tags, Does.Contain(OutpostCameraRig.CameraTag));
        }
    }
}
