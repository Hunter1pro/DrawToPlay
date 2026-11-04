using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>Two rows of a one-per-level kind are one thing in the level: the first is built, the next is
    /// warned about and left out; rows of other kinds are all built.</summary>
    [TestFixture]
    public sealed class ManifestSpawnerOnePerLevelTests
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
        public void TwoStarts_BuildOnePlayer_AndSayWhichWasLeftOut()
        {
            var kinds = Make<LevelObjectKindRegistry>();
            kinds.entries.Add(Kind("player", true));
            kinds.entries.Add(Kind("prop", false));
            var manifest = Make<LevelObjectRegistry>();
            manifest.plane = LevelGroundPlane.XZ;
            manifest.entries.Add(Row("Emma", "player"));
            manifest.entries.Add(Row("lamp 1", "prop"));
            manifest.entries.Add(Row("level.player.1", "player"));
            manifest.entries.Add(Row("lamp 2", "prop"));
            var level = Make<LevelContent>();
            level.objects = manifest;

            var go = new GameObject("Level");
            m_Junk.Add(go);
            var spawner = go.AddComponent<ManifestSpawner>();
            spawner.level = level;
            spawner.kinds = kinds;

            LogAssert.Expect(LogType.Warning, new Regex("'level.player.1' is another 'player'"));
            typeof(ManifestSpawner).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(spawner, null);

            var built = new List<string>();
            foreach (Transform body in go.transform)
                built.Add(body.name);
            CollectionAssert.AreEquivalent(new[] { "Emma", "lamp 1", "lamp 2" }, built);
        }

        private LevelObjectKindDef Kind(string name, bool onePerLevel)
        {
            var body = new GameObject(name + " body");
            m_Junk.Add(body);
            var def = Make<ServiceDef>();
            def.serviceName = name;
            def.body = new ServiceBody { prefab = body };
            return new LevelObjectKindDef { id = "kind." + name, name = name, service = def, onePerLevel = onePerLevel };
        }

        private static LevelObjectDef Row(string name, string kind)
        {
            var row = new LevelObjectDef { id = name, name = name };
            row.kind.entryId = "kind." + kind;
            row.kind.entryName = kind;
            return row;
        }

        private T Make<T>() where T : ScriptableObject
        {
            var made = ScriptableObject.CreateInstance<T>();
            m_Junk.Add(made);
            return made;
        }
    }
}
