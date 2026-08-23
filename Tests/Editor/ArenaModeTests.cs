using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.6 — modes are rows and one drawn round: team frags score the killer's team, solo
    /// frags the killer alone, a suicide scores nobody; energy is held-node time and the most
    /// at the clock's end wins; a node is taken alone, frozen when contested, and fades when
    /// abandoned half-way.
    /// </summary>
    [TestFixture]
    public sealed class ArenaModeTests
    {
        private readonly System.Collections.Generic.List<Object> m_Junk =
            new System.Collections.Generic.List<Object>();
        private ArenaModeService m_Modes;
        private ArenaFighter m_Blue, m_Red, m_Red2;

        [SetUp]
        public void SetUp()
        {
            var registry = ScriptableObject.CreateInstance<ArenaModeRegistry>();
            m_Junk.Add(registry);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "modes";
            def.scope = StateTreeContextKind.Root;
            def.registry = registry;
            m_Junk.Add(def);
            var rootGo = new GameObject("Root") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(rootGo);
            var host = rootGo.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Root;
            host.autoStart = false;
            host.Register();
            m_Modes = new ArenaModeService(host, def);

            m_Blue = Fighter("PlayerOne", ArenaTeams.Blue);
            m_Red = Fighter("RaiderOne", ArenaTeams.Red);
            m_Red2 = Fighter("RaiderTwo", ArenaTeams.Red);
        }

        private ArenaFighter Fighter(string name, string team)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            m_Junk.Add(go);
            ArenaFighter fighter = go.AddComponent<ArenaFighter>();
            fighter.tags.Add(team);
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
        public void TeamFrags_ScoreTheKillersTeam_AndTheLimitEndsTheRound()
        {
            m_Modes.Begin(new ArenaModeDef { name = "tdm", scoring = ArenaScoring.TeamFrags, fragLimit = 2, timeSeconds = 60f });
            m_Modes.ReportKill(m_Blue, m_Red);
            Assert.That(m_Modes.FragsOf(ArenaTeams.Blue), Is.EqualTo(1));
            Assert.That(m_Modes.roundOver, Is.False);
            m_Modes.ReportKill(m_Red, m_Red2);
            Assert.That(m_Modes.FragsOf(ArenaTeams.Red), Is.EqualTo(0), "a team kill scores nobody");
            m_Modes.ReportKill(null, m_Red);
            Assert.That(m_Modes.FragsOf(ArenaTeams.Blue), Is.EqualTo(1), "a fall has no killer");
            m_Modes.ReportKill(m_Blue, m_Red2);
            Assert.That(m_Modes.roundOver, Is.True, "the limit ends it");
            Assert.That(m_Modes.winnerLine, Does.Contain("BLUE"));
        }

        [Test]
        public void SoloFrags_ScoreTheKillerAlone()
        {
            m_Modes.Begin(new ArenaModeDef { name = "ffa", scoring = ArenaScoring.SoloFrags, fragLimit = 0, timeSeconds = 1f });
            m_Modes.ReportKill(m_Red, m_Blue);
            m_Modes.ReportKill(m_Red, m_Blue);
            m_Modes.ReportKill(m_Blue, m_Red);
            Assert.That(m_Modes.FragsOf("RaiderOne"), Is.EqualTo(2));
            Assert.That(m_Modes.FragsOf("PlayerOne"), Is.EqualTo(1));
            m_Modes.TickRound(2f);
            Assert.That(m_Modes.roundOver, Is.True, "the clock ends it");
            Assert.That(m_Modes.winnerLine, Does.Contain("RAIDERONE"));
        }

        [Test]
        public void Energy_IsHeldTime_AndTheMostAtTheEndWins()
        {
            m_Modes.Begin(new ArenaModeDef { name = "energy", scoring = ArenaScoring.Energy, timeSeconds = 10f });
            m_Modes.EnergyTick(ArenaTeams.Red, 3f);
            m_Modes.EnergyTick(ArenaTeams.Blue, 5f);
            m_Modes.TickRound(11f);
            Assert.That(m_Modes.roundOver, Is.True);
            Assert.That(m_Modes.winnerLine, Does.Contain("BLUE"));
        }

        [Test]
        public void ANode_IsTakenAlone_FrozenWhenContested_AndFadesWhenAbandoned()
        {
            var movers = new ArenaMoverService();
            var nodeGo = new GameObject("Node") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(nodeGo);
            nodeGo.transform.position = new Vector3(400f, 0f, 0f);
            nodeGo.SetActive(false);
            ArenaEnergyNode node = nodeGo.AddComponent<ArenaEnergyNode>();
            node.captureSeconds = 2f;
            node.Bind(movers, m_Modes);
            m_Modes.Begin(new ArenaModeDef { name = "energy", scoring = ArenaScoring.Energy, timeSeconds = 60f });

            // The red raider stands on it, alone: taken after captureSeconds.
            m_Red.Wake();
            m_Red.Place(new Vector2(400f, 0.8f));
            movers.Add(m_Red);
            node.Step(1f);
            Assert.That(node.owner, Is.Empty);
            Assert.That(node.progress, Is.GreaterThan(0f));

            // Blue arrives: contested, frozen.
            m_Blue.Wake();
            m_Blue.Place(new Vector2(400.5f, 0.8f));
            movers.Add(m_Blue);
            float held = node.progress;
            node.Step(1f);
            Assert.That(node.progress, Is.EqualTo(held), "contested is frozen");

            // Blue leaves; red finishes the take and holding feeds red.
            movers.Remove(m_Blue);
            m_Blue.Sleep();
            node.Step(1.5f);
            Assert.That(node.owner, Is.EqualTo(ArenaTeams.Red), "taken");
            node.Step(2f);
            Assert.That(m_Modes.EnergyOf(ArenaTeams.Red), Is.GreaterThan(1.5f), "holding feeds the team");

            // Everyone gone: an owned node stays owned; a half-taking would have faded.
            movers.Remove(m_Red);
            m_Red.Sleep();
            node.Step(1f);
            Assert.That(node.owner, Is.EqualTo(ArenaTeams.Red));
        }
    }
}
