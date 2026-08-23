using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.2 — a shot is a body and a hit is force: fired at a fighter it shoves them along
    /// its motion and dies on contact; a launcher shell blasts everyone around where it
    /// lands, shooter included. Simulated for real against the default world.
    /// </summary>
    [TestFixture]
    public sealed class ArenaWeaponTests
    {
        private PhysicsBody m_Ground;
        private ArenaMoverService m_Movers;
        private GameObject m_ShooterGo, m_TargetGo;
        private ArenaFighter m_Shooter, m_Target;
        private ArenaWeaponDef m_Rifle, m_Launcher;

        [SetUp]
        public void SetUp()
        {
            PhysicsWorld world = PhysicsWorld.defaultWorld;
            m_Ground = world.CreateBody(new PhysicsBodyDefinition
            {
                type = PhysicsBody.BodyType.Static, position = new Vector2(0f, -1f)
            });
            m_Ground.CreateShape(PolygonGeometry.CreateBox(new Vector2(60f, 2f)), new PhysicsShapeDefinition
            {
                contactFilter = new PhysicsShape.ContactFilter { categories = ArenaLayers.Static, contacts = PhysicsMask.All }
            });
            m_Movers = new ArenaMoverService();
            (m_ShooterGo, m_Shooter) = Fighter("Shooter", new Vector2(0f, 0.8f));
            (m_TargetGo, m_Target) = Fighter("Target", new Vector2(4f, 0.8f));
            m_Rifle = new ArenaWeaponDef
            {
                id = "item.rifle", name = "rifle", speed = 30f, projectileRadius = 0.08f,
                pellets = 1, impulse = 5f, lifeSeconds = 2f
            };
            m_Launcher = new ArenaWeaponDef
            {
                id = "item.launcher", name = "launcher", speed = 14f, gravityScale = 1f,
                projectileRadius = 0.2f, impulse = 8f, blastRadius = 3f, blastImpulse = 15f, lifeSeconds = 3f
            };
        }

        private (GameObject, ArenaFighter) Fighter(string name, Vector2 at)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            var fighter = go.AddComponent<ArenaFighter>();
            fighter.Wake();
            fighter.Place(at);
            m_Movers.Add(fighter);
            return (go, fighter);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ArenaProjectile shot in Object.FindObjectsByType<ArenaProjectile>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(shot.gameObject);
            m_Shooter.Sleep();
            m_Target.Sleep();
            if (m_Ground.isValid)
                m_Ground.Destroy();
            Object.DestroyImmediate(m_ShooterGo);
            Object.DestroyImmediate(m_TargetGo);
        }

        /// <summary>Step, then dispatch contact-begin callbacks the way the player loop does —
        /// a manual Simulate produces the events but only the module's own update delivers
        /// them to callback targets.</summary>
        private static void Simulate(int steps)
        {
            PhysicsWorld world = PhysicsWorld.defaultWorld;
            for (int i = 0; i < steps; i++)
            {
                world.Simulate(1f / 60f);
                foreach (PhysicsEvents.ContactBeginEvent begin in world.contactBeginEvents)
                {
                    if (!begin.shapeA.isValid || !begin.shapeB.isValid)
                        continue;
                    (begin.shapeA.callbackTarget as PhysicsCallbacks.IContactCallback)?.OnContactBegin2D(begin);
                    (begin.shapeB.callbackTarget as PhysicsCallbacks.IContactCallback)?.OnContactBegin2D(begin);
                }
            }
        }

        [Test]
        public void ARifleShot_ShovesWhoItHits_AndDiesOnContact()
        {
            ArenaProjectile.Fire(m_Rifle, new Vector2(1f, 0.8f), Vector2.right, m_Movers, m_Shooter);
            Assert.That(Object.FindObjectsByType<ArenaProjectile>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Simulate(30);
            Assert.That(m_Target.velocity.x, Is.GreaterThan(2f), "shoved along the shot's motion");
            Assert.That(Object.FindObjectsByType<ArenaProjectile>(FindObjectsSortMode.None), Is.Empty,
                "died where it hit");
        }

        [Test]
        public void ALauncherShell_BlastsEveryoneAroundItsLanding()
        {
            // Lobbed at the floor between the two: both are inside the blast.
            ArenaProjectile.Fire(m_Launcher, new Vector2(2f, 2f), Vector2.down, m_Movers, m_Shooter);
            Simulate(40);
            Assert.That(m_Shooter.velocity.magnitude, Is.GreaterThan(2f), "the rocket jump is free");
            Assert.That(m_Target.velocity.magnitude, Is.GreaterThan(2f), "and the target is thrown");
            Assert.That(m_Shooter.velocity.x, Is.LessThan(0.5f), "away from the blast — left");
            Assert.That(m_Target.velocity.x, Is.GreaterThan(-0.5f), "away from the blast — right");
        }
    }
}
