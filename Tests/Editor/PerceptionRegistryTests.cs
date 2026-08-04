using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the M9 perception migration: <see cref="TargetDetectedCondition"/>
    /// reads the world registry (<see cref="WorldTags.Combatant"/>) instead of the deleted
    /// polled scan. The semantics the M6 suite pinned — nearest hostile, team filter, alive
    /// filter, hysteresis, clear-on-none — must hold against the new source, plus the two new
    /// facts: a spawn is visible IMMEDIATELY (no poll interval), and a missing WorldService is
    /// a warned wiring error, not silence.
    ///
    /// Citizenship is enrolled the way play mode does it (<see cref="WorldObjectBehaviour"/>
    /// via <c>EnsureCitizen</c>) but explicitly, because EditMode runs no lifecycle;
    /// <see cref="HealthComponent.ResetHealth"/> stands in for Awake.
    /// </summary>
    [TestFixture]
    public sealed class PerceptionRegistryTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        [SetUp]
        public void SetUp()
        {
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                if (!ReferenceEquals(m_Hosts[i], null))
                    m_Hosts[i].Unregister();
            }
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
        }

        [Test]
        public void Detects_NearestLivingHostile_ThroughTheRegistry()
        {
            MakeWorld();
            GameObject zombie = MakeCombatant("Zombie", CombatTeam.Enemy, 0f);
            GameObject nearPrey = MakeCombatant("NearPrey", CombatTeam.Player, 2f);
            MakeCombatant("FarPrey", CombatTeam.Player, 5f);
            MakeCombatant("Comrade", CombatTeam.Enemy, 1f);

            var detect = MakeDetector(6f);
            var context = new StateTreeContext(zombie);

            Assert.IsTrue(detect.Evaluate(context), "a living hostile in range is detected");
            Assert.AreSame(nearPrey, context.blackboard["target"],
                "the NEAREST hostile won — a same-team comrade closer by is not a target");
        }

        [Test]
        public void SpawnIsVisibleImmediately_NoPollInterval()
        {
            MakeWorld();
            GameObject zombie = MakeCombatant("Zombie", CombatTeam.Enemy, 0f);
            var detect = MakeDetector(6f);
            var context = new StateTreeContext(zombie);

            Assert.IsFalse(detect.Evaluate(context), "an empty world detects nothing");

            GameObject spawned = MakeCombatant("Spawned", CombatTeam.Player, 1f);
            Assert.IsTrue(detect.Evaluate(context),
                "a combatant registered THIS tick is visible THIS tick — the poll lag is gone");
            Assert.AreSame(spawned, context.blackboard["target"]);
        }

        [Test]
        public void DeadAndOutOfRange_AreNotTargets_AndTheKeyClears()
        {
            MakeWorld();
            GameObject zombie = MakeCombatant("Zombie", CombatTeam.Enemy, 0f);
            GameObject prey = MakeCombatant("Prey", CombatTeam.Player, 2f);
            var detect = MakeDetector(6f);
            var context = new StateTreeContext(zombie);

            Assert.IsTrue(detect.Evaluate(context));

            prey.GetComponent<HealthComponent>().TakeDamage(999f, Vector2.zero);
            Assert.IsFalse(detect.Evaluate(context), "a dead hostile is no hostile");
            Assert.IsFalse(context.blackboard.ContainsKey("target"),
                "and clearTargetWhenNone dropped the stale key");

            GameObject farPrey = MakeCombatant("FarPrey", CombatTeam.Player, 50f);
            Assert.IsFalse(detect.Evaluate(context), "alive but out of range is out of reach");
        }

        [Test]
        public void Hysteresis_KeepsAHeldTarget_InsideLoseRange()
        {
            MakeWorld();
            GameObject zombie = MakeCombatant("Zombie", CombatTeam.Enemy, 0f);
            GameObject prey = MakeCombatant("Prey", CombatTeam.Player, 2f);
            var detect = MakeDetector(3f);
            detect.loseRange = 10f;
            var context = new StateTreeContext(zombie);

            Assert.IsTrue(detect.Evaluate(context), "acquired inside detectRange");

            prey.transform.position = new Vector3(6f, 0f, 0f);
            Assert.IsTrue(detect.Evaluate(context),
                "outside detectRange but inside loseRange the held target is KEPT");
            Assert.AreSame(prey, context.blackboard["target"]);

            prey.transform.position = new Vector3(20f, 0f, 0f);
            Assert.IsFalse(detect.Evaluate(context), "beyond loseRange it is finally dropped");
        }

        [Test]
        public void NoWorldService_IsAWiringError_FalseNotSilence()
        {
            GameObject zombie = MakeCombatant("Zombie", CombatTeam.Enemy, 0f, citizen: false);
            var detect = MakeDetector(6f);
            var context = new StateTreeContext(zombie);

            Assert.IsFalse(detect.Evaluate(context),
                "no registry = nothing detected, false is the safe answer");
        }

        // ---------------------------------------------------------------------- fixtures

        private WorldService MakeWorld()
        {
            var go = new GameObject("Root");
            m_Objects.Add(go);
            var root = go.AddComponent<StateTreeContextHost>();
            root.kind = StateTreeContextKind.Root;
            root.autoStart = false;
            root.Register();
            m_Hosts.Add(root);

            var world = go.AddComponent<WorldService>();
            world.Connect();
            return world;
        }

        /// <summary>An entity the way play mode makes one: health + citizenship + registration,
        /// with ResetHealth standing in for Awake.</summary>
        private GameObject MakeCombatant(string goName, CombatTeam team, float x,
            bool citizen = true)
        {
            var go = new GameObject(goName);
            m_Objects.Add(go);
            go.transform.position = new Vector3(x, 0f, 0f);

            var health = go.AddComponent<HealthComponent>();
            health.team = team;
            health.ResetHealth();

            if (citizen)
            {
                WorldObjectBehaviour obj =
                    WorldObjectBehaviour.EnsureCitizen(go, WorldTags.Combatant);
                obj.RegisterToWorld();
            }
            return go;
        }

        private TargetDetectedCondition MakeDetector(float range)
        {
            var detect = ScriptableObject.CreateInstance<TargetDetectedCondition>();
            detect.detectRange = range;
            m_Assets.Add(detect);
            return detect;
        }
    }
}
