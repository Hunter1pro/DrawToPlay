using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M31.1 — a tag is picked from a declared vocabulary.
    ///
    /// Tags are matched by exact ordinal text at runtime, which makes a typo a quest that never
    /// completes and a raider nobody can find. The fix is the rule this project already applies
    /// to every other reference: you may name what you DECLARE, and the picker offers exactly
    /// that — a manifest's listed vocabularies, or a tag catalog reached the ordinary way.
    /// </summary>
    [TestFixture]
    public sealed class WorldTagOffersTests
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
        public void AManifestOffersTheVocabulariesItLists_AndNothingElse()
        {
            WorldTagRegistry spoken = Vocabulary("Spoken",
                ("Objective", "objective.raider"), ("World", "water"));
            WorldTagRegistry unknown = Vocabulary("Unknown", ("World", "lava"));

            var manifest = Make<LevelObjectRegistry>("Yard");
            manifest.tags.Add(spoken);

            var offered = new List<WorldTagDef>();
            StateTreeOffers.TagsFor(manifest, offered);
            Assert.That(Names(offered), Is.EqualTo(new[] { "objective.raider", "water" }));
            Assert.That(Names(offered), Does.Not.Contain("lava"),
                "a vocabulary this manifest does not list is not this manifest's vocabulary");

            var silent = Make<LevelObjectRegistry>("Silent");
            StateTreeOffers.TagsFor(silent, offered);
            Assert.That(offered, Is.Empty, "declaring nothing offers nothing, as everywhere else");
        }

        [Test]
        public void AGroupIsACategory_SoAFieldCanAskForObjectiveMarkersOnly()
        {
            WorldTagRegistry vocabulary = Vocabulary("Tags",
                ("Objective", "objective.raider"), ("Objective", "objective.relic"),
                ("World", "water"), ("State", "sailor"));
            var manifest = Make<LevelObjectRegistry>("Yard");
            manifest.tags.Add(vocabulary);

            var offered = new List<WorldTagDef>();
            StateTreeOffers.TagsFor(manifest, offered, "Objective");
            Assert.That(Names(offered),
                Is.EqualTo(new[] { "objective.raider", "objective.relic" }),
                "the row says which family it is in — no dotted hierarchy to enumerate");

            StateTreeOffers.TagsFor(manifest, offered, "Nothing");
            Assert.That(offered, Is.Empty);
        }

        [Test]
        public void ADefOrATreeReachesItsTagsTheOrdinaryWay()
        {
            WorldTagRegistry vocabulary = Vocabulary("Tags", ("Cast", "keeper"));

            var def = Make<ServiceDef>("cutscenes");
            def.declares.Add(vocabulary);

            var offered = new List<WorldTagDef>();
            StateTreeOffers.TagsFor(def, offered);
            Assert.That(Names(offered), Is.EqualTo(new[] { "keeper" }),
                "a def declares a tag catalog exactly as it declares any other");

            var tree = Make<StateTreeAsset>("KeeperTree");
            StateTreeOffers.TagsFor(tree, offered);
            Assert.That(offered, Is.Empty, "until it lists it in its own Data");
            tree.registries.Add(vocabulary);
            StateTreeOffers.TagsFor(tree, offered);
            Assert.That(Names(offered), Is.EqualTo(new[] { "keeper" }));
        }

        private static string[] Names(List<WorldTagDef> rows)
        {
            var names = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                names[i] = rows[i].name;
            return names;
        }

        private WorldTagRegistry Vocabulary(string name,
            params (string group, string tag)[] rows)
        {
            var vocabulary = Make<WorldTagRegistry>(name);
            for (int i = 0; i < rows.Length; i++)
            {
                vocabulary.entries.Add(new WorldTagDef
                {
                    id = "tag." + rows[i].tag, name = rows[i].tag, group = rows[i].group
                });
            }
            return vocabulary;
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            m_Junk.Add(asset);
            return asset;
        }
    }
}
