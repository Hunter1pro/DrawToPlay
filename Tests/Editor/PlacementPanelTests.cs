using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M34.1c — the placement's options are a PANEL, not a list you add rows to.
    /// (M36.2 — and the panel is the shared one; what is pinned here is what the KIND declares
    /// and what the body's seed contributes, which is all this caller decides.)
    ///
    /// What the panel promises is two things the list could not: it offers every option the
    /// kind declares (so an author finds out an option exists by looking, not by knowing the
    /// name), and it shows what the value would be without an override. Both promises are the
    /// panel LYING if it prints a confident number for something the body never seeds, so the
    /// dash is pinned here as hard as the number is.
    ///
    /// The third promise is subtraction: a row nobody declares any more is shown as the stray
    /// it is. The build already refuses it out loud (see <c>ServiceBodyTests</c>); the panel
    /// is where it gets deleted.
    /// </summary>
    [TestFixture]
    public sealed class PlacementPanelTests
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
        public void TheUntickedValueIsTheBodysSeed_AndADashWhereThereIsNone()
        {
            var prefab = new GameObject("Tree") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(prefab);
            AttributeComponent attributes = prefab.AddComponent<AttributeComponent>();
            var seed = new AttributeComponent.Seed { baseValue = 3f };
            seed.attribute.entryId = "attribute.health";
            seed.attribute.entryName = "health";
            attributes.seeds.Add(seed);

            ServiceDef stand = Def("resource", prefab);

            float health = DeclaredOptions.Seeded(stand, "health", out bool seeded);
            Assert.That(seeded, Is.True);
            Assert.That(health, Is.EqualTo(3f).Within(0.001f),
                "the panel shows what this one would start at, not an empty box");

            DeclaredOptions.Seeded(stand, "attack", out bool guessed);
            Assert.That(guessed, Is.False,
                "half the kinds take their numbers from the row rather than a seed, and a "
                + "confident 0 next to those is the panel's first lie");

            DeclaredOptions.Seeded(Def("bodiless", null), "health", out bool nothing);
            Assert.That(nothing, Is.False, "a def with no body seeds nothing");
        }

        [Test]
        public void ARowTheKindNoLongerDeclares_IsShownAsAStray()
        {
            var registry = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            registry.hideFlags = HideFlags.HideAndDontSave;
            m_Junk.Add(registry);

            var placement = new LevelObjectDef { id = "place.stand" };
            placement.attributes.values.Add(new PlacementAttribute { attribute = "health", value = 5f });
            placement.attributes.values.Add(new PlacementAttribute { attribute = "mana", value = 9f });
            registry.entries.Add(placement);

            var so = new SerializedObject(registry);
            SerializedProperty list = so.FindProperty("entries")
                .GetArrayElementAtIndex(0).FindPropertyRelative("attributes")
                .FindPropertyRelative("values");

            var declared = new List<ServiceAttribute>();
            var has = new ServiceAttribute();
            has.attribute.entryId = "attribute.health";
            has.attribute.entryName = "health";
            declared.Add(has);

            List<DeclaredOption> options = DeclaredOptions.OfKind(KindDef(declared));
            List<int> strays = DeclaredOptionsPanel.Strays(list, options,
                DeclaredOptionRowShape.PlacementAttribute);
            Assert.That(strays, Is.EquivalentTo(new[] { 1 }),
                "'mana' names nothing this kind has, so the panel says so where it can be deleted");

            Assert.That(DeclaredOptionsPanel.Strays(list, null,
                    DeclaredOptionRowShape.PlacementAttribute).Count, Is.EqualTo(2),
                "a placement whose kind lost its def declares nothing, so every row is a stray");
        }

        /// <summary>A def that declares exactly these attributes — the kind behind a placement.</summary>
        private ServiceDef KindDef(List<ServiceAttribute> declared)
        {
            ServiceDef def = Def("kind", null);
            def.attributes.AddRange(declared);
            return def;
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
