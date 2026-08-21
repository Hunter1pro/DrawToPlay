using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A part that holds the row it is an instance of — every kind in the old switch had
    /// one of these under a different name.</summary>
    internal sealed class TestCarrier : MonoBehaviour
    {
        public StateTreeEntryRef<ItemDef> item = new StateTreeEntryRef<ItemDef>();
    }

    /// <summary>A part worth exposing: what a contract would be dereferenced through.</summary>
    internal sealed class TestVitals : MonoBehaviour
    {
    }

    /// <summary>
    /// M30.3 — the def owns the body.
    ///
    /// What is being pinned is the SENTENCE the old per-kind switch wrote nine times: build the
    /// prefab, stamp the placement's identity on it, point one of its parts at the row's entry,
    /// hold its tree until the world knows it. Said once as data, so the tenth kind of object
    /// costs an asset rather than a case.
    /// </summary>
    [TestFixture]
    public sealed class ServiceBodyTests
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
        public void TheDefBuildsItsBody_AndThePlacementIsItsIdentity()
        {
            GameObject prefab = Prefab("Pickup");
            prefab.AddComponent<WorldObjectBehaviour>();

            ServiceDef def = Def("pickup", prefab);
            def.body.wearsEntryName = true;
            def.body.entryNamePrefix = "item-";

            var row = new LevelObjectDef
            {
                id = "place.keycard", name = "the keycard on the step",
                entry = new LevelObjectEntryRef { entryId = "item.keycard", entryName = "keycard" }
            };
            row.tags.Add(new LevelObjectTagRef { tag = "objective.loot" });

            GameObject built = ServiceBodyFactory.Build(def, row, null,
                new Vector3(1f, 2f, 3f), Quaternion.identity);
            m_Junk.Add(built);

            Assert.That(built, Is.Not.Null);
            Assert.That(built.activeSelf, Is.True, "it is born inactive and switched on last");
            Assert.That(built.name, Is.EqualTo("the keycard on the step"));
            Assert.That(built.transform.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));

            var citizen = built.GetComponent<WorldObjectBehaviour>();
            Assert.That(citizen.stableId, Is.EqualTo("place.keycard"),
                "the placement's id IS the citizen's, or a save cannot say this one is gone");
            Assert.That(citizen.entryName, Is.EqualTo("item-keycard"));
            Assert.That(citizen.HasTag("objective.loot"), Is.True);
        }

        [Test]
        public void TheEntryLandsOnThePartThatActsOnIt_AndNoEntryLeavesThePrefabAlone()
        {
            GameObject prefab = Prefab("Pickup");
            prefab.AddComponent<WorldObjectBehaviour>();
            TestCarrier authored = prefab.AddComponent<TestCarrier>();
            authored.item.entryId = "item.default";
            authored.item.entryName = "keycard-of-the-prefab";

            ServiceDef def = Def("pickup", prefab);
            def.body.links.Add(new ServiceBodyLink
            {
                component = nameof(TestCarrier), field = "item"
            });

            GameObject named = ServiceBodyFactory.Build(def, new LevelObjectDef
            {
                id = "place.a",
                entry = new LevelObjectEntryRef { entryId = "item.timber", entryName = "timber" }
            }, null, Vector3.zero, Quaternion.identity);
            m_Junk.Add(named);

            TestCarrier carried = named.GetComponent<TestCarrier>();
            Assert.That(carried.item.entryName, Is.EqualTo("timber"));
            Assert.That(carried.item.entryId, Is.EqualTo("item.timber"),
                "both halves, or the link is a spelling rather than a reference");

            GameObject silent = ServiceBodyFactory.Build(def, new LevelObjectDef { id = "place.b" },
                null, Vector3.zero, Quaternion.identity);
            m_Junk.Add(silent);
            Assert.That(silent.GetComponent<TestCarrier>().item.entryName,
                Is.EqualTo("keycard-of-the-prefab"),
                "a placement that names nothing keeps what the prefab shipped with");
        }

        [Test]
        public void AHeldMindWaitsForTheWorld_AndOneThatStartsItselfIsLeftAlone()
        {
            GameObject prefab = Prefab("Npc");
            prefab.AddComponent<WorldObjectBehaviour>();
            StateTreeContextHost authored = prefab.AddComponent<StateTreeContextHost>();
            authored.autoStart = true;

            ServiceDef def = Def("npc", prefab);
            var held = new List<StateTreeContextHost>();

            GameObject waiting = ServiceBodyFactory.Build(def, new LevelObjectDef { id = "a" },
                null, Vector3.zero, Quaternion.identity, held);
            m_Junk.Add(waiting);
            Assert.That(held.Count, Is.EqualTo(1));
            Assert.That(waiting.GetComponent<StateTreeContextHost>().autoStart, Is.False,
                "a tree that starts in the frame its body was built asks a world that has not "
                + "adopted it yet");

            def.body.mind = ServiceBodyMind.StartsItself;
            held.Clear();
            GameObject character = ServiceBodyFactory.Build(def, new LevelObjectDef { id = "b" },
                null, Vector3.zero, Quaternion.identity, held);
            m_Junk.Add(character);
            Assert.That(held, Is.Empty);
            Assert.That(character.GetComponent<StateTreeContextHost>().autoStart, Is.True);
        }

        [Test]
        public void AnExposedPart_IsWhatAContractResolvesTo()
        {
            GameObject prefab = Prefab("Resource");
            prefab.AddComponent<WorldObjectBehaviour>();
            var limb = new GameObject("Trunk");
            limb.transform.SetParent(prefab.transform, false);
            limb.AddComponent<TestVitals>();

            ServiceDef def = Def("resource", prefab);
            def.body.exposes.Add(nameof(TestVitals));

            GameObject built = ServiceBodyFactory.Build(def, new LevelObjectDef { id = "tree.1" },
                null, Vector3.zero, Quaternion.identity);
            m_Junk.Add(built);

            var choppable = new ContractDef
            {
                id = "contract.choppable", name = "choppable",
                facetTypeName = typeof(TestVitals).AssemblyQualifiedName
            };
            // THE TWO HALVES MEET: the def assembled the body, so the promise it claims is
            // dereferenceable on the thing it built — whatever the body is made of.
            Assert.That(StateTreeContracts.Facet(built, choppable),
                Is.SameAs(built.GetComponentInChildren<TestVitals>()));
        }

        [Test]
        public void APlacementSetsItsOwnNumbers_AndOnlyWhatTheDefDeclares()
        {
            GameObject prefab = Prefab("Tree");
            prefab.AddComponent<WorldObjectBehaviour>();
            AttributeComponent seeded = prefab.AddComponent<AttributeComponent>();
            // SEEDED, not Ensured: the runtime dictionary is not serialized, so a copy of this
            // prefab would carry nothing. The seeds list is what travels — which is exactly why
            // a placement value has to be applied to the INSTANCE.
            var seed = new AttributeComponent.Seed { baseValue = 3f };
            seed.attribute.entryId = "attribute.health";
            seed.attribute.entryName = "health";
            seeded.seeds.Add(seed);

            ServiceDef stand = Def("resource", prefab);
            var has = new ServiceAttribute { writable = true };
            has.attribute.entryId = "attribute.health";
            has.attribute.entryName = "health";
            stand.attributes.Add(has);

            // ONE DEF, TWO NUMBERS: the sapling says 2, the old trunk says 5, and the third
            // says nothing and keeps the prefab's seed.
            GameObject sapling = ServiceBodyFactory.Build(stand, Placement("t1", ("health", 2f)),
                null, Vector3.zero, Quaternion.identity);
            GameObject trunk = ServiceBodyFactory.Build(stand, Placement("t2", ("health", 5f)),
                null, Vector3.zero, Quaternion.identity);
            GameObject plain = ServiceBodyFactory.Build(stand, new LevelObjectDef { id = "t3" },
                null, Vector3.zero, Quaternion.identity);
            m_Junk.Add(sapling);
            m_Junk.Add(trunk);
            m_Junk.Add(plain);

            Assert.That(sapling.GetComponent<AttributeComponent>().Value("health"),
                Is.EqualTo(2f).Within(0.001f));
            Assert.That(trunk.GetComponent<AttributeComponent>().Value("health"),
                Is.EqualTo(5f).Within(0.001f),
                "a second prefab was the old answer to a different number");
            Assert.That(plain.GetComponent<AttributeComponent>().Value("health"),
                Is.EqualTo(3f).Within(0.001f), "silence keeps what the body was seeded with");

            // AND A NUMBER FOR SOMETHING THE DEF DOES NOT HAVE is refused out loud, because a
            // value sitting there doing nothing is the worst kind of typo.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "does not declare it has"));
            GameObject wrong = ServiceBodyFactory.Build(stand, Placement("t4", ("mana", 9f)),
                null, Vector3.zero, Quaternion.identity);
            m_Junk.Add(wrong);
            Assert.That(wrong.GetComponent<AttributeComponent>().Has("mana"), Is.False);
        }

        private static LevelObjectDef Placement(string id, params (string name, float value)[] set)
        {
            var row = new LevelObjectDef { id = id };
            for (int i = 0; i < set.Length; i++)
            {
                row.attributes.values.Add(new PlacementAttribute
                {
                    attribute = set[i].name, value = set[i].value
                });
            }
            return row;
        }

        [Test]
        public void ADefWithNoBody_BuildsNothing()
        {
            ServiceDef subsystem = Def("inventory", null);
            Assert.That(subsystem.body.IsThing, Is.False);
            Assert.That(ServiceBodyFactory.Build(subsystem, new LevelObjectDef { id = "x" }, null,
                Vector3.zero, Quaternion.identity), Is.Null,
                "a subsystem is a def too — asking it for a body is a question, not an error");
        }

        private GameObject Prefab(string name)
        {
            var prefab = new GameObject(name);
            prefab.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(prefab);
            return prefab;
        }

        private ServiceDef Def(string name, GameObject prefab)
        {
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = name;
            def.serviceName = name;
            def.body.prefab = prefab;
            m_Junk.Add(def);
            return def;
        }
    }
}
