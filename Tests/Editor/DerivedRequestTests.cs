using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M30.4 — the requests nobody types.
    ///
    /// A def says what it HAS and its read/change API follows: ask, set, add. What is pinned here
    /// is the half that stops it being decoration — the PERMISSION is real (a read-only attribute
    /// has no set row to find, at runtime as well as in the inspector), an authored row is never
    /// shadowed by a generated one, and a derived request actually moves the attribute on the
    /// body the def built.
    /// </summary>
    [TestFixture]
    public sealed class DerivedRequestTests
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
        public void WhatADefHas_BecomesItsApi_AndReadOnlyStopsAtTheAsk()
        {
            ServiceDef door = Def("door");
            Has(door, "health", writable: true);
            Has(door, "open", writable: false);

            var derived = new List<ServiceRequest>();
            door.DerivedRequests(derived);
            var keys = new List<string>();
            for (int i = 0; i < derived.Count; i++)
                keys.Add(derived[i].key);

            Assert.That(keys, Is.EqualTo(new[]
            {
                "health.ask", "health.set", "health.add", "open.ask"
            }), "three verbs where it may be changed, the ask alone where it may not");

            Assert.That(door.RequestFor("health.add"), Is.Not.Null);
            Assert.That(door.RequestFor("open.set"), Is.Null,
                "the permission is CHECKED, not merely drawn — every caller validates here");
            Assert.That(door.RequestFor("mana.ask"), Is.Null, "it does not have mana");
            Assert.That(door.IsDerived("health.ask"), Is.True);
        }

        [Test]
        public void AnAuthoredRow_IsNeverShadowedByAGeneratedOne()
        {
            ServiceDef vault = Def("vault");
            Has(vault, "gold", writable: true);
            vault.requests.Add(new ServiceRequest
            {
                key = "gold.set",
                description = "Only the bank may do this.",
                action = "bank"
            });

            ServiceRequest row = vault.RequestFor("gold.set");
            Assert.That(row.action, Is.EqualTo("bank"),
                "a def that wrote its own row keeps it, with its own rules");
        }

        [Test]
        public void ADerivedRequest_MovesTheRealAttribute_ThroughTheDefThatBuiltTheBody()
        {
            GameObject prefab = Prefab("Tree");
            prefab.AddComponent<WorldObjectBehaviour>();
            prefab.AddComponent<AttributeComponent>();

            ServiceDef stand = Def("resource");
            stand.body.prefab = prefab;
            Has(stand, "health", writable: true);

            GameObject built = ServiceBodyFactory.Build(stand, new LevelObjectDef { id = "tree.1" },
                null, Vector3.zero, Quaternion.identity);
            m_Junk.Add(built);
            Assert.That(ServiceBodyBinding.Of(built), Is.SameAs(stand),
                "the body knows which def it is — the other direction of 'the def owns the body'");

            var attributes = built.GetComponent<AttributeComponent>();
            attributes.Ensure("health", 10f);

            var context = new StateTreeContext(built);
            var chop = new ObjectRequestTask { request = "health.add", value = "-4" };
            m_Junk.Add(chop);
            Assert.That(chop.OnTick(context, 0f), Is.EqualTo(StateTreeStatus.Success));
            Assert.That(attributes.Value("health"), Is.EqualTo(6f).Within(0.001f),
                "a generated row that did not move the number would be decoration");

            var look = new ObjectRequestTask
            {
                request = "health.ask",
                into = new StateTreeKeyField("seen")
            };
            m_Junk.Add(look);
            Assert.That(look.OnTick(context, 0f), Is.EqualTo(StateTreeStatus.Success));
            Assert.That(context.blackboard["seen"], Is.EqualTo(6f));

            var reset = new ObjectRequestTask { request = "health.set", value = "9" };
            m_Junk.Add(reset);
            reset.OnTick(context, 0f);
            Assert.That(attributes.Value("health"), Is.EqualTo(9f).Within(0.001f));
        }

        [Test]
        public void ADefRefusesWhatItDidNotDeclare_AndSaysWhichDefRefused()
        {
            GameObject prefab = Prefab("Door");
            prefab.AddComponent<WorldObjectBehaviour>();
            prefab.AddComponent<AttributeComponent>();

            ServiceDef door = Def("door");
            door.body.prefab = prefab;
            Has(door, "open", writable: false);

            GameObject built = ServiceBodyFactory.Build(door, new LevelObjectDef { id = "door.1" },
                null, Vector3.zero, Quaternion.identity);
            m_Junk.Add(built);
            built.GetComponent<AttributeComponent>().Ensure("open", 0f);

            var context = new StateTreeContext(built);
            var force = new ObjectRequestTask { request = "open.set", value = "1" };
            m_Junk.Add(force);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "'door' has no request 'open.set'"));
            Assert.That(force.OnTick(context, 0f), Is.EqualTo(StateTreeStatus.Failure));
            Assert.That(built.GetComponent<AttributeComponent>().Value("open"),
                Is.EqualTo(0f).Within(0.001f), "refused means nothing moved");
        }

        private GameObject Prefab(string name)
        {
            var prefab = new GameObject(name);
            prefab.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(prefab);
            return prefab;
        }

        private ServiceDef Def(string name)
        {
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = name;
            def.serviceName = name;
            m_Junk.Add(def);
            return def;
        }

        private static void Has(ServiceDef def, string attribute, bool writable)
        {
            var row = new ServiceAttribute { writable = writable };
            row.attribute.entryId = "attribute." + attribute;
            row.attribute.entryName = attribute;
            def.attributes.Add(row);
        }
    }
}
