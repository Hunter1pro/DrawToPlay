using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.9 — the pickup asks (the Heavenly Treasures catch flow): a player standing on a
    /// weapon is OFFERED it — take arms them and consumes the pad, keep stays quiet until
    /// they step off; an AI still loots by walking, and a level with no pickup subsystem
    /// falls back to walk-over for everyone.
    /// </summary>
    [TestFixture]
    public sealed class ArenaPickupTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private ArenaMoverService m_Movers;
        private ArenaPickupService m_Pickups;
        private ArenaWeaponRegistry m_Weapons;
        private ArenaWeaponDef m_Launcher;
        private ArenaFighter m_Player;
        private ArenaWeaponPickup m_Pickup;
        private GameObject m_Visual;

        [SetUp]
        public void SetUp()
        {
            m_Weapons = ScriptableObject.CreateInstance<ArenaWeaponRegistry>();
            m_Junk.Add(m_Weapons);
            m_Launcher = new ArenaWeaponDef
            {
                id = "weapon.launcher", name = "launcher", damage = 34f, blastRadius = 3f,
                gravityScale = 1f, color = Color.red
            };
            m_Weapons.entries.Add(m_Launcher);

            var rootGo = new GameObject("Root") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(rootGo);
            var host = rootGo.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Root;
            host.autoStart = false;
            host.Register();
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "pickups";
            def.registry = m_Weapons;
            m_Junk.Add(def);
            m_Movers = new ArenaMoverService();
            m_Pickups = new ArenaPickupService(host, def);

            m_Player = Fighter("PlayerOne", "player");
            m_Movers.Add(m_Player);

            var pickupGo = new GameObject("Pickup") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(pickupGo);
            m_Pickup = pickupGo.AddComponent<ArenaWeaponPickup>();
            m_Pickup.catalog = m_Weapons;
            m_Pickup.weapon.entryName = "launcher";
            m_Visual = new GameObject("Visual");
            m_Visual.transform.SetParent(pickupGo.transform);
            m_Pickup.visual = m_Visual;
            pickupGo.transform.position = Vector3.zero;
            m_Pickup.Bind(m_Movers, m_Pickups);
        }

        private ArenaFighter Fighter(string name, string tag)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            ArenaFighter fighter = go.AddComponent<ArenaFighter>();
            fighter.tags.Add(tag);
            fighter.Place(Vector2.zero);
            return fighter;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object junk in m_Junk)
            {
                if (junk is GameObject go && go.TryGetComponent(out StateTreeContextHost host))
                    host.Unregister();
                if (junk != null)
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ThePlayer_IsAsked_NotArmedBehindTheirBack()
        {
            m_Pickup.Scan(10f);
            Assert.That(m_Pickups.offered, Is.SameAs(m_Pickup), "standing on it is an OFFER");
            Assert.That(m_Player.weapon, Is.Null, "nothing armed yet");
            Assert.That(m_Visual.activeSelf, Is.True, "the pad still stands");
        }

        [Test]
        public void Take_Arms_AndConsumesThePad()
        {
            m_Pickup.Scan(10f);
            m_Pickups.Take();
            Assert.That(m_Player.weapon, Is.SameAs(m_Launcher), "TAKE arms the player");
            Assert.That(m_Visual.activeSelf, Is.False, "the pad is consumed");
            Assert.That(m_Pickups.offered, Is.Null, "the card is cleared");
            m_Pickup.Scan(11f);
            Assert.That(m_Pickups.offered, Is.Null, "a taken pad offers nothing while it returns");
        }

        [Test]
        public void Keep_StaysQuiet_UntilTheyStepOff()
        {
            m_Pickup.Scan(10f);
            m_Pickups.Keep();
            Assert.That(m_Pickups.offered, Is.Null);
            m_Pickup.Scan(11f);
            Assert.That(m_Pickups.offered, Is.Null, "declined: still standing there asks nothing");
            m_Player.Place(new Vector2(9f, 0f));
            m_Pickup.Scan(12f);
            Assert.That(m_Pickups.offered, Is.Null, "walked off — card stays down");
            m_Player.Place(Vector2.zero);
            m_Pickup.Scan(13f);
            Assert.That(m_Pickups.offered, Is.SameAs(m_Pickup), "coming back asks again");
        }

        [Test]
        public void WalkingAway_WithdrawsTheCard()
        {
            m_Pickup.Scan(10f);
            Assert.That(m_Pickups.offered, Is.SameAs(m_Pickup));
            m_Player.Place(new Vector2(9f, 0f));
            m_Pickup.Scan(11f);
            Assert.That(m_Pickups.offered, Is.Null, "leaving takes the card down");
        }

        [Test]
        public void AnAi_StillLootsByWalking()
        {
            ArenaFighter raider = Fighter("Raider", ArenaTeams.Red);
            m_Movers.Add(raider);
            m_Player.Place(new Vector2(9f, 0f));
            m_Pickup.Scan(10f);
            Assert.That(raider.weapon, Is.SameAs(m_Launcher), "an AI loots by walking");
            Assert.That(m_Visual.activeSelf, Is.False);
            Assert.That(m_Pickups.offered, Is.Null, "no card for a machine");
        }

        [Test]
        public void NoPickupSubsystem_FallsBackToWalkOver()
        {
            m_Pickup.Bind(m_Movers, null);
            m_Pickup.Scan(10f);
            Assert.That(m_Player.weapon, Is.SameAs(m_Launcher),
                "a level without the subsystem keeps the old walk-over");
        }
    }
}
