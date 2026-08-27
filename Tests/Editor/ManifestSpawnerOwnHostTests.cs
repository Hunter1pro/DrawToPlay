using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The level's own tree is held the way a body's is: a level host with autoStart off on
    /// the spawner's object starts on frame two, after its rows are citizens — and one that
    /// starts itself is left alone.
    /// </summary>
    [TestFixture]
    public sealed class ManifestSpawnerOwnHostTests
    {
        private readonly List<Object> m_Junk = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void AHeldLevelHost_StartsOnFrameTwo_WithTheBodies()
        {
            StateTreeContextHost host = Level(autoStart: false);
            var spawner = host.gameObject.AddComponent<ManifestSpawner>();
            spawner.level = EmptyLevel();

            Tick(spawner);
            Assert.IsFalse(host.isRunning, "frame one spawns the rows; nothing starts yet");
            Tick(spawner);
            Assert.IsTrue(host.isRunning, "frame two starts what was held, the level's own host among it");
        }

        [Test]
        public void AHostThatStartsItself_IsNotTheSpawnersBusiness()
        {
            StateTreeContextHost host = Level(autoStart: true);
            var spawner = host.gameObject.AddComponent<ManifestSpawner>();
            spawner.level = EmptyLevel();

            Tick(spawner);
            Tick(spawner);
            Assert.IsFalse(host.isRunning,
                "autoStart is Start's job; a spawner that also started it would start it twice");
        }

        private StateTreeContextHost Level(bool autoStart)
        {
            var go = new GameObject("Level");
            m_Junk.Add(go);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Level;
            host.autoStart = autoStart;
            host.tree = HoldingTree();
            return host;
        }

        /// <summary>A tree that runs until interrupted, so isRunning is a fact worth asserting.</summary>
        private StateTreeAsset HoldingTree()
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            var hold = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            hold.nodeId = "hold";
            hold.completeWhen = StateTreeCompleteWhen.Never;
            root.children.Add(hold);
            tree.root = root;
            m_Junk.Add(hold);
            m_Junk.Add(root);
            m_Junk.Add(tree);
            return tree;
        }

        private LevelContent EmptyLevel()
        {
            var manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            var content = ScriptableObject.CreateInstance<LevelContent>();
            content.objects = manifest;
            m_Junk.Add(manifest);
            m_Junk.Add(content);
            return content;
        }

        private static void Tick(ManifestSpawner spawner)
        {
            typeof(ManifestSpawner)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(spawner, null);
        }
    }
}
