using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M40.2 — the death path is one method. A health crossing is what the combat service
    /// watches; what a death MEANS — the drop, the kill report — is <c>Died(citizen)</c>,
    /// readable top to bottom, calling the spawner and the quest line. No event, no bridge.
    /// The M26 rule rides along: a composed object (two citizens on one attribute pool) dies
    /// once.
    /// </summary>
    [TestFixture]
    public sealed class OutpostDeathPathTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeContextHost m_Level;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Level") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            m_Level = go.AddComponent<StateTreeContextHost>();
            m_Level.kind = StateTreeContextKind.Level;
            m_Level.autoStart = false;
            m_Level.Register();
            m_Level.Provide(new WorldService(m_Level, null));
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
        public void AnEnemysHealthCrossingZero_ReportsOneKill_ToTheQuestLine()
        {
            var registry = ScriptableObject.CreateInstance<ObjectiveRegistry>();
            var hunt = new ObjectiveDef
            {
                id = "objective.hunt", name = "hunt", kind = ObjectiveKind.EnemyKill, count = 2
            };
            registry.entries.Add(hunt);
            m_Junk.Add(registry);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "objectives";
            def.scope = StateTreeContextKind.Level;
            def.registry = registry;
            m_Junk.Add(def);
            var objectives = new ObjectiveService(m_Level, def);
            m_Level.Provide(objectives);
            objectives.Activate(hunt);

            var combat = new OutpostCombatService(m_Level.gameObject);
            m_Level.Provide(combat);

            // A raider: an enemy character with a health pool, and a second citizen facet on
            // the same object — the composed shape that once dropped two piles of timber.
            var raiderGo = new GameObject("Raider") { hideFlags = HideFlags.HideAndDontSave };
            raiderGo.transform.SetParent(m_Level.transform);
            raiderGo.SetActive(false);
            m_Junk.Add(raiderGo);
            var vitals = raiderGo.AddComponent<AttributeComponent>();
            vitals.Ensure(AttributeNames.Health, 5f);
            var raider = raiderGo.AddComponent<OutpostCharacter>();
            raider.team = RaiderTeam.Enemy;
            raider.RegisterToWorld();
            var facet = raiderGo.AddComponent<WorldObjectBehaviour>();
            facet.RegisterToWorld();

            // The combat service watches the world on first use — force that first use.
            typeof(OutpostCombatService)
                .GetProperty("World", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(combat);

            Assert.AreEqual(0, objectives.progress);
            vitals.Consume(AttributeNames.Health, 5f);   // 5 → 0: the crossing
            Assert.AreEqual(1, objectives.progress,
                "the pool crossed once, so ONE kill reached the quest line — not one per facet");
            Assert.AreSame(hunt, objectives.current, "one of two; the hunt goes on");

            vitals.Consume(AttributeNames.Health, 1f);   // already at zero: no second crossing
            Assert.AreEqual(1, objectives.progress, "a corpse does not die again");

            combat.Dispose();
        }
    }
}
