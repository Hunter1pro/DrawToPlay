using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A placement is picked from a declared manifest — the WorldTag rule, applied to
    /// row ids.</summary>
    [TestFixture]
    public sealed class PlacementIdOffersTests
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
        public void AManifestOffersItsOwnRows()
        {
            LevelObjectRegistry hall = Manifest("Hall", "hall.door.204", "hall.npc.nadine");

            var offered = new List<LevelObjectDef>();
            StateTreeOffers.PlacementsFor(hall, offered);
            Assert.That(Ids(offered), Is.EqualTo(new[] { "hall.door.204", "hall.npc.nadine" }));
        }

        [Test]
        public void ATreeOffersTheManifestsItLists_AndNothingElse()
        {
            LevelObjectRegistry hall = Manifest("Hall", "hall.door.204");
            LevelObjectRegistry elsewhere = Manifest("Roof", "roof.hatch");

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            m_Junk.Add(tree);
            tree.registries.Add(hall);

            var offered = new List<LevelObjectDef>();
            StateTreeOffers.PlacementsFor(tree, offered);
            Assert.That(Ids(offered), Is.EqualTo(new[] { "hall.door.204" }));
            Assert.That(Ids(offered), Does.Not.Contain("roof.hatch"),
                "a manifest this tree does not list is another level's business");

            var silent = ScriptableObject.CreateInstance<StateTreeAsset>();
            m_Junk.Add(silent);
            StateTreeOffers.PlacementsFor(silent, offered);
            Assert.That(offered, Is.Empty, "declaring nothing offers nothing, as everywhere else");
        }

        private LevelObjectRegistry Manifest(string name, params string[] ids)
        {
            var manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            manifest.name = name;
            for (int i = 0; i < ids.Length; i++)
                manifest.entries.Add(new LevelObjectDef { id = ids[i], name = ids[i] });
            m_Junk.Add(manifest);
            return manifest;
        }

        private static List<string> Ids(List<LevelObjectDef> rows)
        {
            var ids = new List<string>();
            for (int i = 0; i < rows.Count; i++)
                ids.Add(rows[i].id);
            return ids;
        }
    }
}
