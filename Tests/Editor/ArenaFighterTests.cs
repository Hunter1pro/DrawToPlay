using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.1 — the unit: a capsule the world's mover solves. Falls onto the ground, runs on
    /// intent, jumps when asked on the ground, and is shoved by force. Stepped here against
    /// the default world's queries with no play mode and no level — the way the level's mover
    /// service steps it before every physics step.
    /// </summary>
    [TestFixture]
    public sealed class ArenaFighterTests
    {
        private const float k_Step = 1f / 60f;
        private PhysicsBody m_Ground;
        private GameObject m_Go;
        private ArenaFighter m_Fighter;

        [SetUp]
        public void SetUp()
        {
            PhysicsWorld world = PhysicsWorld.defaultWorld;
            m_Ground = world.CreateBody(new PhysicsBodyDefinition
            {
                type = PhysicsBody.BodyType.Static, position = new Vector2(0f, -1f)
            });
            m_Ground.CreateShape(PolygonGeometry.CreateBox(new Vector2(40f, 2f)), new PhysicsShapeDefinition
            {
                contactFilter = new PhysicsShape.ContactFilter { categories = ArenaLayers.Static, contacts = PhysicsMask.All }
            });
            m_Go = new GameObject("Fighter") { hideFlags = HideFlags.HideAndDontSave };
            m_Go.SetActive(false);
            m_Fighter = m_Go.AddComponent<ArenaFighter>();
            m_Fighter.Wake();
            m_Fighter.Place(new Vector2(0f, 3f));
        }

        [TearDown]
        public void TearDown()
        {
            m_Fighter.Sleep();
            if (m_Ground.isValid)
                m_Ground.Destroy();
            Object.DestroyImmediate(m_Go);
        }

        private void Steps(int count)
        {
            PhysicsWorld world = PhysicsWorld.defaultWorld;
            for (int i = 0; i < count; i++)
                m_Fighter.Step(world, k_Step);
        }

        [Test]
        public void FallsToTheGround_RunsOnIntent_JumpsFromTheGround_AndIsShoved()
        {
            Steps(120);
            Assert.That(m_Fighter.onGround, Is.True, "came to rest on the ground");
            Assert.That(m_Fighter.position.y, Is.EqualTo(0.8f).Within(0.05f), "capsule centre over the floor");
            // The kinematic body is moved by SetTransformTarget — a velocity the SIMULATION
            // applies, and nothing simulates here; live, body and mover read the same place.
            Assert.That(m_Fighter.body.isValid, Is.True, "wears a body for projectiles to hit");

            m_Fighter.Intent(1f, false);
            Steps(60);
            Assert.That(m_Fighter.position.x, Is.GreaterThan(3f), "ran right");
            Assert.That(m_Fighter.velocity.x, Is.EqualTo(m_Fighter.maxSpeed).Within(0.5f), "at full speed");
            Assert.That(m_Fighter.speed01, Is.EqualTo(1f).Within(0.1f));

            m_Fighter.Intent(0f, false);
            Steps(60);
            Assert.That(Mathf.Abs(m_Fighter.velocity.x), Is.LessThan(0.1f), "friction stopped it");

            float before = m_Fighter.position.y;
            m_Fighter.Intent(0f, true);
            Steps(10);
            Assert.That(m_Fighter.onGround, Is.False, "in the air");
            Assert.That(m_Fighter.position.y, Is.GreaterThan(before + 0.5f), "jumped");
            m_Fighter.Intent(0f, false);
            Steps(180);
            Assert.That(m_Fighter.onGround, Is.True, "and landed");

            m_Fighter.Shove(new Vector2(-6f, 4f));
            Steps(5);
            Assert.That(m_Fighter.velocity.x, Is.LessThan(-3f), "knocked back");
        }
    }
}
