using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M24 objectives — rows with a chain, one service, four watchers: kills counted and
    /// filtered by tag, pickups as ABSOLUTE carried counts (dropping un-progresses),
    /// dialogs by row name, and MoveTo against the NEAREST zone carrying the tag. The
    /// game reports facts; the service owns what they mean.
    /// </summary>
    [TestFixture]
    public sealed class ObjectiveServiceTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Level;
        private ObjectiveService m_Service;
        private ObjectiveRegistry m_Registry;
        private WorldService m_World;
        private StateTreeContextHost m_Player;

        [SetUp]
        public void SetUp()
        {
            m_Registry = ScriptableObject.CreateInstance<ObjectiveRegistry>();
            m_Assets.Add(m_Registry);

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "objectives";
            def.scope = StateTreeContextKind.Level;
            def.registry = m_Registry;
            m_Assets.Add(def);

            var levelGo = new GameObject("Level");
            levelGo.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(levelGo);
            m_Level = levelGo.AddComponent<StateTreeContextHost>();
            m_Level.kind = StateTreeContextKind.Level;
            m_Level.autoStart = false;
            m_Level.Register();
            m_Hosts.Add(m_Level);

            m_World = levelGo.AddComponent<WorldService>();
            m_World.Connect();

            m_Service = levelGo.AddComponent<ObjectiveService>();
            m_Service.definition = def;
            m_Service.Connect();

            var playerGo = new GameObject("PlayerHost");
            playerGo.hideFlags = HideFlags.HideAndDontSave;
            playerGo.transform.SetParent(levelGo.transform);
            m_Objects.Add(playerGo);
            m_Player = playerGo.AddComponent<StateTreeContextHost>();
            m_Player.kind = StateTreeContextKind.Player;
            m_Player.autoStart = false;
            m_Player.Register();
            m_Hosts.Add(m_Player);
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

        private ObjectiveDef MakeObjective(string objectiveName, ObjectiveKind kind)
        {
            var row = new ObjectiveDef
            {
                id = "objective." + objectiveName, name = objectiveName, kind = kind
            };
            m_Registry.entries.Add(row);
            return row;
        }

        private WorldObjectBehaviour MakeCitizen(string citizenName, Vector3 position,
            params string[] tags)
        {
            var go = new GameObject(citizenName);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(m_Level.transform);
            go.transform.position = position;
            go.SetActive(false);
            m_Objects.Add(go);
            var citizen = go.AddComponent<WorldObjectBehaviour>();
            citizen.tags.AddRange(tags);
            citizen.RegisterToWorld();
            return citizen;
        }

        [Test]
        public void Zones_TheNearestWithWorkWins_AndEachStackKeepsItsPlace()
        {
            // Two zones, two stacks (the HT distance-zone switch, row-shaped).
            ObjectiveDef a1 = MakeObjective("a1", ObjectiveKind.Dialog);
            a1.zone = "zone.a";
            a1.target.entryName = "keeper";
            a1.nextOnComplete.entryName = "a2";
            ObjectiveDef a2 = MakeObjective("a2", ObjectiveKind.EnemyKill);
            a2.zone = "zone.a";

            ObjectiveDef b1 = MakeObjective("b1", ObjectiveKind.Dialog);
            b1.zone = "zone.b";
            b1.target.entryName = "scout";

            MakeCitizen("VolumeA", new Vector3(-5f, 0f, 0f), "zone.a");
            MakeCitizen("VolumeB", new Vector3(5f, 0f, 0f), "zone.b");

            m_Player.transform.position = new Vector3(-4f, 0f, 0f);
            m_Service.OrchestrateNow();
            Assert.AreSame(a1, m_Service.current, "the nearest zone's stack is asked");
            Assert.AreEqual("zone.a", m_Service.activeZone);

            m_Service.ReportDialogFinished("keeper");
            Assert.AreSame(a2, m_Service.current, "completing advances THIS stack");

            m_Player.transform.position = new Vector3(4f, 0f, 0f);
            m_Service.OrchestrateNow();
            Assert.AreSame(b1, m_Service.current, "walking switched the stack");

            m_Player.transform.position = new Vector3(-4f, 0f, 0f);
            m_Service.OrchestrateNow();
            Assert.AreSame(a2, m_Service.current,
                "back in zone A, its stack resumes where it stood — not from the top");
        }

        [Test]
        public void ADoneZone_StopsCompeting_AndTheLinearLineIsTheFallback()
        {
            ObjectiveDef linear = MakeObjective("linear", ObjectiveKind.EnemyKill);
            ObjectiveDef zoned = MakeObjective("zoned", ObjectiveKind.Dialog);
            zoned.zone = "zone.only";
            zoned.target.entryName = "keeper";

            MakeCitizen("Volume", new Vector3(2f, 0f, 0f), "zone.only");
            m_Player.transform.position = Vector3.zero;

            m_Service.Activate(linear);
            m_Service.OrchestrateNow();
            Assert.AreSame(zoned, m_Service.current,
                "a zone with work outranks the linear line");

            m_Service.ReportDialogFinished("keeper");
            m_Service.OrchestrateNow();
            Assert.AreSame(linear, m_Service.current,
                "the zone finished and stopped competing — the linear line resumed its place");
        }

        [Test]
        public void SaveState_RoundTrips_CursorsDoneStacksAndTheLinearLine()
        {
            ObjectiveDef a1 = MakeObjective("a1", ObjectiveKind.Dialog);
            a1.zone = "zone.a";
            a1.target.entryName = "keeper";
            a1.nextOnComplete.entryName = "a2";
            ObjectiveDef a2 = MakeObjective("a2", ObjectiveKind.EnemyKill);
            a2.zone = "zone.a";
            ObjectiveDef b1 = MakeObjective("b1", ObjectiveKind.Dialog);
            b1.zone = "zone.b";
            b1.target.entryName = "scout";
            ObjectiveDef linear = MakeObjective("linear", ObjectiveKind.EnemyKill);

            MakeCitizen("VolumeA", new Vector3(-5f, 0f, 0f), "zone.a");
            m_Player.transform.position = new Vector3(-4f, 0f, 0f);

            m_Service.Activate(linear);
            m_Service.OrchestrateNow();          // zone A takes the screen at a1
            m_Service.ReportDialogFinished("keeper");   // a1 done → cursor a2
            m_Service.ReportDialogFinished("scout");    // wrong current kind/target — no-op

            ObjectiveService.SaveState saved = m_Service.CaptureState();

            // Wreck the live state, then restore — the reload shape.
            m_Service.Activate(b1);
            m_Service.ReportDialogFinished("scout");    // b1 done → zone.b cursor null
            m_Service.RestoreState(saved);

            Assert.AreSame(a2, m_Service.current, "what was current came back");
            m_Player.transform.position = new Vector3(-4f, 0f, 0f);
            m_Service.OrchestrateNow();
            Assert.AreSame(a2, m_Service.current, "zone A resumed at ITS cursor");

            // zone.b was untouched at capture: its entry must still be alive.
            m_Player.transform.position = new Vector3(50f, 0f, 0f);
            MakeCitizen("VolumeB", new Vector3(51f, 0f, 0f), "zone.b");
            m_Service.OrchestrateNow();
            Assert.AreSame(b1, m_Service.current,
                "the restore rewound the wreckage — b1 is back on offer");
        }

        [Test]
        public void TheChain_ActivatesTheNextRow_OnComplete()
        {
            ObjectiveDef talk = MakeObjective("talk", ObjectiveKind.Dialog);
            talk.target.entryName = "keeper";
            talk.nextOnComplete.entryName = "after";
            ObjectiveDef after = MakeObjective("after", ObjectiveKind.EnemyKill);

            ObjectiveDef completed = null;
            m_Service.completedObjective += done => completed = done;

            m_Service.Activate(talk);
            m_Service.ReportDialogFinished("scout");
            Assert.AreSame(talk, m_Service.current, "the wrong conversation changes nothing");
            m_Service.ReportDialogFinished("keeper");
            Assert.AreSame(talk, completed);
            Assert.AreSame(after, m_Service.current, "the chain spoke — a wire, not code");
        }

        [Test]
        public void Kills_Count_AndTheTagFilters()
        {
            ObjectiveDef hunt = MakeObjective("hunt", ObjectiveKind.EnemyKill);
            hunt.count = 2;
            hunt.targetTag = "bandit";
            m_Service.Activate(hunt);

            WorldObjectBehaviour bandit = MakeCitizen("bandit", Vector3.zero, "bandit");
            WorldObjectBehaviour bystander = MakeCitizen("bystander", Vector3.zero, "npc");

            m_Service.ReportKill(bystander);
            Assert.AreEqual(0, m_Service.progress, "the filter held");
            m_Service.ReportKill(bandit);
            Assert.AreEqual(1, m_Service.progress);
            Assert.AreSame(hunt, m_Service.current);
            m_Service.ReportKill(bandit);
            Assert.IsNull(m_Service.current, "two of two — complete, end of chain");
        }

        [Test]
        public void Pickups_AreAbsoluteCounts_DroppingUnprogresses()
        {
            ObjectiveDef carry = MakeObjective("carry", ObjectiveKind.Pickup);
            carry.count = 2;
            carry.target.entryName = "relic";
            m_Service.Activate(carry);

            m_Service.ReportPickupCount("relic", 1);
            Assert.AreEqual(1, m_Service.progress);
            m_Service.ReportPickupCount("relic", 0);
            Assert.AreEqual(0, m_Service.progress,
                "the report is CARRIED NOW, so dropping honestly walks back");
            m_Service.ReportPickupCount("ration", 5);
            Assert.AreEqual(0, m_Service.progress, "another item is another story");
            m_Service.ReportPickupCount("relic", 2);
            Assert.IsNull(m_Service.current, "carrying the goal completes");
        }

        [Test]
        public void MoveTo_ArrivesAtTheNearestZone_AndTheArrowAimsAtIt()
        {
            ObjectiveDef reach = MakeObjective("reach", ObjectiveKind.MoveTo);
            reach.targetTag = "zone.road";
            m_Service.Activate(reach);

            var far = new GameObject("FarZone");
            far.hideFlags = HideFlags.HideAndDontSave;
            far.transform.SetParent(m_Level.transform);
            far.transform.position = new Vector3(30f, 0f, 0f);
            far.SetActive(false);
            m_Objects.Add(far);
            var farZone = far.AddComponent<ObjectiveZoneBehaviour>();
            farZone.radius = 2f;
            farZone.tags.Add("zone.road");
            farZone.RegisterToWorld();

            var near = new GameObject("NearZone");
            near.hideFlags = HideFlags.HideAndDontSave;
            near.transform.SetParent(m_Level.transform);
            near.transform.position = new Vector3(1f, 0f, 0f);
            near.SetActive(false);
            m_Objects.Add(near);
            var nearZone = near.AddComponent<ObjectiveZoneBehaviour>();
            nearZone.radius = 2f;
            nearZone.tags.Add("zone.road");
            nearZone.RegisterToWorld();

            Vector3? aim = m_Service.CurrentTargetPosition();
            Assert.IsTrue(aim.HasValue);
            Assert.AreEqual(1f, aim.Value.x, 0.001f, "nearest wins — always");

            m_Player.transform.position = new Vector3(10f, 0f, 0f);
            m_Service.CheckArrival();
            Assert.AreSame(reach, m_Service.current, "ten metres out is not arrived");

            m_Player.transform.position = new Vector3(0.5f, 0f, 0f);
            m_Service.CheckArrival();
            Assert.IsNull(m_Service.current, "inside the near zone's radius completes");
        }
    }
}
