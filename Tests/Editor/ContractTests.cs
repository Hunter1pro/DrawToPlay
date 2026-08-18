using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>An interface that joins the vocabulary from CODE — the edge case contracts have
    /// to meet: a promise easier to state in C# than in data, usable by the data side anyway.</summary>
    [StateTreeContract("openable")]
    internal interface ITestOpenable
    {
    }

    internal sealed class TestDoorLeaf : MonoBehaviour, ITestOpenable
    {
    }

    internal sealed class TestPlainProp : MonoBehaviour
    {
    }

    /// <summary>
    /// M30.2 — contracts are runtime-real.
    ///
    /// Three questions and they must not collapse into each other: what a def CLAIMS (authoring),
    /// what it actually DELIVERS (the check that stops a claim being a label), and what a live
    /// body keeps the promise WITH (the facet a task dereferences). Plus the neighbourhood rule
    /// again — asking for a promise offers the implementers you declared, not the project's.
    /// </summary>
    [TestFixture]
    public sealed class ContractTests
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
        public void AClaimIsCheckable_AndAnEmptyPromiseIsCaught()
        {
            ContractDef damageable = Contract("damageable",
                requests: new[] { "damage" }, attributes: new[] { "health" });

            ServiceDef honest = Service("combat");
            honest.requests.Add(new ServiceRequest { key = "damage", action = "damage" });
            var vitals = ScriptableObject.CreateInstance<AttributeRegistry>();
            m_Junk.Add(vitals);
            vitals.entries.Add(new AttributeDef { id = "attribute.health", name = "health" });
            honest.registry = vitals;
            Claim(honest, damageable);

            ServiceDef boaster = Service("scenery");
            Claim(boaster, damageable);

            var missing = new List<string>();
            StateTreeContracts.Missing(honest, damageable, missing);
            Assert.That(missing, Is.Empty, "it serves the request and holds the attribute");

            StateTreeContracts.Missing(boaster, damageable, missing);
            Assert.That(missing.Count, Is.EqualTo(2),
                "a claim is not proof — say exactly what is not delivered");
            Assert.That(string.Join(" ", missing), Does.Contain("damage").And.Contain("health"));
        }

        [Test]
        public void ALiveBody_KeepsThePromise_ThroughTheFacetTheRowNames()
        {
            ContractDef carriable = Contract("carriable");
            carriable.facetTypeName = typeof(TestPlainProp).AssemblyQualifiedName;

            var body = new GameObject("Crate");
            body.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(body);
            Assert.That(StateTreeContracts.Keeps(body, carriable), Is.False, "nothing on it yet");

            body.AddComponent<TestPlainProp>();
            Assert.That(StateTreeContracts.Keeps(body, carriable), Is.True);
            Assert.That(StateTreeContracts.Facet(body, carriable), Is.InstanceOf<TestPlainProp>(),
                "the promise is dereferenceable, which is the whole reason it is not a filter");
        }

        [Test]
        public void ExposedFacets_AnswerForACompositeBody()
        {
            ContractDef carriable = Contract("carriable");
            carriable.facetTypeName = typeof(TestPlainProp).AssemblyQualifiedName;

            var body = new GameObject("Composite");
            body.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(body);
            var citizen = body.AddComponent<WorldObjectBehaviour>();

            var partHolder = new GameObject("Part");
            partHolder.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(partHolder);
            var part = partHolder.AddComponent<TestPlainProp>();
            citizen.Expose(part);

            Assert.That(StateTreeContracts.Facet(body, carriable), Is.SameAs(part),
                "an object that is the sum of its parts keeps promises through them");
        }

        [Test]
        public void CodeCanExtendTheVocabulary_WithNoRowNamingTheType()
        {
            // The row says nothing about C#; the interface says which contract it keeps.
            ContractDef openable = Contract("openable");

            var door = new GameObject("Door");
            door.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(door);
            var leaf = door.AddComponent<TestDoorLeaf>();

            Assert.That(StateTreeContracts.Facet(door, openable), Is.SameAs(leaf),
                "[StateTreeContract] on an interface is enough — the edge case, met");
            Assert.That(StateTreeContracts.MarkedAs(typeof(TestPlainProp), "openable"), Is.False);
        }

        [Test]
        public void ImplementersAreOffered_OnlyThroughDeclaredDependencies()
        {
            ContractDef openable = Contract("openable");
            ServiceDef doorDef = Service("door");
            Claim(doorDef, openable);

            // A catalog of object rows, each carrying its def — the M30.3 shape, early.
            var objects = ScriptableObject.CreateInstance<LevelObjectKindRegistry>();
            objects.name = "Kinds";
            m_Junk.Add(objects);
            objects.entries.Add(new LevelObjectKindDef
            {
                id = "kind.door", name = "door", service = doorDef
            });

            var declaring = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            declaring.name = "Declaring";
            m_Junk.Add(declaring);
            declaring.dependsOn.Add(objects);

            var silent = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            silent.name = "Silent";
            m_Junk.Add(silent);

            var found = new List<ServiceDef>();
            StateTreeOffers.ImplementersOf(openable, declaring, found);
            Assert.That(found, Is.EqualTo(new[] { doorDef }));

            StateTreeOffers.ImplementersOf(openable, silent, found);
            Assert.That(found, Is.Empty, "undeclared neighbourhoods offer nothing, as with rows");
        }

        private ContractDef Contract(string name, string[] requests = null,
            string[] attributes = null)
        {
            var contract = new ContractDef { id = "contract." + name, name = name };
            if (requests != null)
                contract.requests.AddRange(requests);
            if (attributes != null)
                contract.attributes.AddRange(attributes);
            return contract;
        }

        private ServiceDef Service(string name)
        {
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = name;
            def.serviceName = name;
            m_Junk.Add(def);
            return def;
        }

        private static void Claim(ServiceDef def, ContractDef contract)
        {
            var reference = new StateTreeEntryRef<ContractDef>();
            reference.entryId = contract.id;
            reference.entryName = contract.name;
            def.implements.Add(reference);
        }
    }
}
