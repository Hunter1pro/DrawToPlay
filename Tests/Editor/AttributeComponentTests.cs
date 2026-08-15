using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M23 attributes — the GAS step's foundation: named values with a base, a consumable
    /// current, and REVERTIBLE modifiers. Effective = (base + Σ additive) × Π multiplicative
    /// is both the derived read a stat uses and the cap a pool clamps against; Consume never
    /// clamps from below (overkill is information) and never gates (guard windows are a
    /// domain rule, health's); Restore clamps to the effective cap. HealthComponent rides
    /// this as a facade — its rules, this number.
    /// </summary>
    [TestFixture]
    public sealed class AttributeComponentTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            m_Objects.Clear();
        }

        private AttributeComponent MakeActor()
        {
            var go = new GameObject("actor");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);   // EditMode ground rule: no Unity message runs uninvited
            m_Objects.Add(go);
            return go.AddComponent<AttributeComponent>();
        }

        // ------------------------------------------------------------------- pool basics

        [Test]
        public void ConsumeAndRestore_MoveCurrent_RestoreClampsToTheCap()
        {
            AttributeComponent attributes = MakeActor();
            attributes.Ensure("stamina", 10f);

            attributes.Consume("stamina", 4f);
            Assert.AreEqual(6f, attributes.Value("stamina"), 0.001f);

            attributes.Restore("stamina", 100f);
            Assert.AreEqual(10f, attributes.Value("stamina"), 0.001f,
                "restore clamps to the effective cap");
        }

        [Test]
        public void Consume_DoesNotClampFromBelow_OverkillIsInformation()
        {
            AttributeComponent attributes = MakeActor();
            attributes.Ensure("health", 3f);

            attributes.Consume("health", 5f);
            Assert.AreEqual(-2f, attributes.Value("health"), 0.001f,
                "the number says HOW dead — the reader decides what zero means");
        }

        [Test]
        public void Ensure_IsANoOp_WhenTheAttributeAlreadyExists()
        {
            // A domain component ensuring after a seed must not reset the seed.
            AttributeComponent attributes = MakeActor();
            attributes.Ensure("health", 7f);
            attributes.Consume("health", 2f);

            attributes.Ensure("health", 100f);
            Assert.AreEqual(7f, attributes.BaseOf("health"), 0.001f);
            Assert.AreEqual(5f, attributes.Value("health"), 0.001f);
        }

        [Test]
        public void Seeds_CreateTheirAttributes_OnFirstUse()
        {
            AttributeComponent attributes = MakeActor();
            var seed = new AttributeComponent.Seed { baseValue = 12f };
            seed.attribute.entryName = "mana";
            attributes.seeds.Add(seed);

            Assert.IsTrue(attributes.Has("mana"));
            Assert.AreEqual(12f, attributes.Value("mana"), 0.001f);
        }

        // -------------------------------------------------------------------- modifiers

        [Test]
        public void Modifiers_AdditiveThenMultiplicative_AndTheyRevert()
        {
            AttributeComponent attributes = MakeActor();
            attributes.Ensure("speed", 10f);

            AttributeComponent.ModifierHandle boots =
                attributes.AddModifier("speed", additive: 2f);
            AttributeComponent.ModifierHandle slow =
                attributes.AddModifier("speed", additive: 0f, multiplicative: 0.5f);
            Assert.AreEqual(6f, attributes.Effective("speed"), 0.001f,
                "(10 + 2) × 0.5 — additive sums first, multiplicative scales the sum");

            attributes.RemoveModifier(slow);
            Assert.AreEqual(12f, attributes.Effective("speed"), 0.001f);
            attributes.RemoveModifier(boots);
            Assert.AreEqual(10f, attributes.Effective("speed"), 0.001f,
                "everything granted came back off — nothing drifts");
        }

        [Test]
        public void ACapThatRises_DoesNotFillThePool_ACapThatFalls_ClampsIt()
        {
            AttributeComponent attributes = MakeActor();
            attributes.Ensure("health", 10f);
            attributes.Consume("health", 1f);   // current 9

            AttributeComponent.ModifierHandle fortify =
                attributes.AddModifier("health", additive: 5f);
            Assert.AreEqual(9f, attributes.Value("health"), 0.001f,
                "+max health is headroom, not a heal");
            Assert.AreEqual(15f, attributes.Effective("health"), 0.001f);

            attributes.Restore("health", 100f);   // fill to the raised cap: 15
            attributes.RemoveModifier(fortify);
            Assert.AreEqual(10f, attributes.Value("health"), 0.001f,
                "the cap fell out from under the pool — current clamps down with it");
        }

        [Test]
        public void RemoveModifier_IsQuiet_ForNullAndForAHandleAlreadyGone()
        {
            AttributeComponent attributes = MakeActor();
            attributes.Ensure("speed", 10f);
            AttributeComponent.ModifierHandle handle = attributes.AddModifier("speed", 1f);

            attributes.RemoveModifier(null);
            attributes.RemoveModifier(handle);
            attributes.RemoveModifier(handle);   // double-revert must not re-clamp or re-fire
            Assert.AreEqual(10f, attributes.Effective("speed"), 0.001f);
        }

        // ----------------------------------------------------------------------- events

        [Test]
        public void Changed_FiresForCurrent_EffectiveChanged_ForModifiersAndBase()
        {
            AttributeComponent attributes = MakeActor();
            attributes.Ensure("stamina", 10f);

            var moves = new List<string>();
            attributes.changed += (name, previous, current) =>
                moves.Add(name + ":" + previous + "->" + current);
            var reshapes = new List<string>();
            attributes.effectiveChanged += name => reshapes.Add(name);

            attributes.Consume("stamina", 3f);
            AttributeComponent.ModifierHandle handle = attributes.AddModifier("stamina", 2f);
            attributes.RemoveModifier(handle);
            attributes.SetBase("stamina", 8f);

            CollectionAssert.AreEqual(new[] { "stamina:10->7" }, moves,
                "only the consume moved CURRENT — modifier traffic is the other channel");
            CollectionAssert.AreEqual(new[] { "stamina", "stamina", "stamina" }, reshapes,
                "add, remove, and the base change each announced a new effective value");
        }

        // ------------------------------------------------------------- the health facade

        [Test]
        public void HealthComponent_RidesTheAttribute_RulesStayOnTheFacade()
        {
            var go = new GameObject("guarded");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
            m_Objects.Add(go);
            var health = go.AddComponent<HealthComponent>();
            health.maxHP = 3f;

            health.TakeDamage(1f);
            var attributes = go.GetComponent<AttributeComponent>();
            Assert.IsNotNull(attributes, "the facade backed itself with the attribute");
            Assert.AreEqual(2f, attributes.Value(HealthComponent.AttributeName), 0.001f,
                "one number — the facade's hp and the attribute's current agree");

            health.TakeDamage(1f);
            Assert.AreEqual(2f, health.hp, 0.001f,
                "the guard window blocked the second hit — health's RULE, kept on the "
                + "facade, not in the attribute store");
        }

        [Test]
        public void MaxHealthModifier_RaisesTheHealCeiling()
        {
            var go = new GameObject("fortified");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
            m_Objects.Add(go);
            var health = go.AddComponent<HealthComponent>();
            health.maxHP = 3f;
            health.ResetHealth();

            var attributes = go.GetComponent<AttributeComponent>();
            AttributeComponent.ModifierHandle fortify =
                attributes.AddModifier(HealthComponent.AttributeName, additive: 2f);
            health.Heal(10f);
            Assert.AreEqual(5f, health.hp, 0.001f,
                "the heal clamped to the MODIFIED cap — the thing the flat maxHP model "
                + "could never say");
            attributes.RemoveModifier(fortify);
            Assert.AreEqual(3f, health.hp, 0.001f);
        }
    }
}
