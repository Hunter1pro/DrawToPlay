using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.3 — what a hit means: the shield absorbs first, health takes the rest, a pool
    /// crossing zero is one readable death — a ragdoll thrown with the killing hit, a downed
    /// fighter with no body, and a return at a spawn with full health and an empty shield.
    /// </summary>
    [TestFixture]
    public sealed class ArenaCombatTests
    {
        private GameObject m_Go;
        private ArenaFighter m_Fighter;
        private ArenaCombatService m_Combat;

        [SetUp]
        public void SetUp()
        {
            m_Go = new GameObject("Fighter") { hideFlags = HideFlags.HideAndDontSave };
            m_Go.SetActive(false);
            var vitals = m_Go.AddComponent<AttributeComponent>();
            var health = new AttributeComponent.Seed { baseValue = 100f };
            health.attribute.entryName = ArenaCombatService.Health;
            vitals.seeds.Add(health);
            var shield = new AttributeComponent.Seed { baseValue = 100f };
            shield.attribute.entryName = ArenaCombatService.Shield;
            vitals.seeds.Add(shield);
            m_Fighter = m_Go.AddComponent<ArenaFighter>();
            m_Fighter.Wake();
            m_Fighter.Place(new Vector2(0f, 0.8f));
            m_Combat = new ArenaCombatService();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ArenaRagdoll ragdoll in Object.FindObjectsByType<ArenaRagdoll>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                ragdoll.Expire();
            if (!m_Fighter.dead)
                m_Fighter.Sleep();
            Object.DestroyImmediate(m_Go);
        }

        [Test]
        public void TheShieldAbsorbsFirst_HealthTakesTheRest_AndZeroIsARagdoll()
        {
            AttributeComponent vitals = m_Fighter.vitals;
            Assert.That(vitals.Value(ArenaCombatService.Shield), Is.EqualTo(0f), "born uncovered");
            vitals.Restore(ArenaCombatService.Shield, 50f);

            m_Combat.Damage(m_Fighter, 30f, Vector2.right);
            Assert.That(vitals.Value(ArenaCombatService.Shield), Is.EqualTo(20f), "the shield took it");
            Assert.That(vitals.Value(ArenaCombatService.Health), Is.EqualTo(100f), "health untouched");

            m_Combat.Damage(m_Fighter, 60f, Vector2.right);
            Assert.That(vitals.Value(ArenaCombatService.Shield), Is.EqualTo(0f));
            Assert.That(vitals.Value(ArenaCombatService.Health), Is.EqualTo(60f), "the rest reached health");
            Assert.That(m_Fighter.dead, Is.False);

            m_Combat.Damage(m_Fighter, 60f, new Vector2(8f, 4f));
            Assert.That(m_Fighter.dead, Is.True, "the pool crossed zero");
            Assert.That(m_Fighter.body.isValid, Is.False, "a downed fighter has no body");
            ArenaRagdoll[] ragdolls = Object.FindObjectsByType<ArenaRagdoll>(FindObjectsSortMode.None);
            Assert.That(ragdolls, Has.Length.EqualTo(1), "and a ragdoll where it stood");

            m_Combat.Damage(m_Fighter, 60f, Vector2.right);
            Assert.That(Object.FindObjectsByType<ArenaRagdoll>(FindObjectsSortMode.None),
                Has.Length.EqualTo(1), "a corpse does not die again");
        }

        [Test]
        public void TheFallen_ComeBackAtASpawn_FullHealthEmptyShield()
        {
            var spawnGo = new GameObject("Spawn") { hideFlags = HideFlags.HideAndDontSave };
            spawnGo.transform.position = new Vector3(12f, 0f, 0f);
            spawnGo.SetActive(false);
            m_Combat.Add(spawnGo.AddComponent<ArenaSpawnPoint>());
            m_Combat.respawnSeconds = -1f;   // due immediately — the clock is the level's tick

            m_Fighter.vitals.Restore(ArenaCombatService.Shield, 50f);
            m_Combat.Damage(m_Fighter, 1000f, Vector2.right);
            Assert.That(m_Fighter.dead, Is.True);

            m_Combat.Tick(1f / 60f);
            Assert.That(m_Fighter.dead, Is.False, "back");
            Assert.That(m_Fighter.position.x, Is.EqualTo(12f).Within(0.1f), "at the spawn");
            Assert.That(m_Fighter.body.isValid, Is.True, "with a body");
            Assert.That(m_Fighter.vitals.Value(ArenaCombatService.Health), Is.EqualTo(100f), "healed");
            Assert.That(m_Fighter.vitals.Value(ArenaCombatService.Shield), Is.EqualTo(0f), "uncovered again");
            Object.DestroyImmediate(spawnGo);
        }
    }
}
