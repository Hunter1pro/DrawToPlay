using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// 0.3.0: the session tree runs a zone. RunZoneTask asks the zone's stack (no volume
    /// needed), the ask outranks the distance orchestrator, a pre-empted state releases the
    /// ask but keeps the cursor, and the objective-complete verb lets a flow finish the step
    /// no watcher can see.
    /// </summary>
    [TestFixture]
    public sealed class RunZoneTaskTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Level;
        private ObjectiveService m_Service;
        private ObjectiveRegistry m_Registry;
        private ZoneRegistry m_Zones;
        private WorldService m_World;
        private StateTreeContextHost m_Player;

        [SetUp]
        public void SetUp()
        {
            m_Registry = ScriptableObject.CreateInstance<ObjectiveRegistry>();
            m_Assets.Add(m_Registry);
            m_Zones = ScriptableObject.CreateInstance<ZoneRegistry>();
            m_Assets.Add(m_Zones);
            m_Registry.dependsOn.Add(m_Zones);
            m_Zones.dependsOn.Add(m_Registry);

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

            m_World = new WorldService(m_Level, null);
            m_Level.Provide(m_World);

            m_Service = new ObjectiveService(m_Level, def);
            m_Level.Provide(m_Service);

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

        [Test]
        public void AskedZone_RunsItsStack_InOrder_AndTheStateCompletes()
        {
            ObjectiveDef meet = MakeDialog("meet", "intro");
            ObjectiveDef brief = MakeDialog("brief", "orders");
            ZoneDef zone = MakeZone("dorm", meet, brief);
            RunZoneTask task = MakeTask("dorm");
            StateTreeContext context = Context();

            task.OnEnter(context);
            Assert.AreSame(meet, m_Service.current, "the ask resumes at the stack's cursor");
            Assert.AreEqual(zone.id, m_Service.activeZone);
            Assert.AreEqual(StateTreeStatus.Running, task.OnTick(context, 0.1f));

            m_Service.ReportDialogFinished("intro");
            Assert.AreSame(brief, m_Service.current, "the stack's ORDER is the chain");
            Assert.AreEqual(StateTreeStatus.Running, task.OnTick(context, 0.1f));

            m_Service.ReportDialogFinished("orders");
            Assert.AreEqual(StateTreeStatus.Success, task.OnTick(context, 0.1f),
                "running past the end completes the state");
            task.OnExit(context, StateTreeStatus.Success);
            Assert.AreEqual("", m_Service.askedZone);
        }

        [Test]
        public void AnAsk_OutranksDistance_AndNeedsNoVolume()
        {
            ObjectiveDef meet = MakeDialog("meet", "intro");
            MakeZone("dorm", meet);
            ObjectiveDef stroll = MakeObjective("stroll", ObjectiveKind.MoveTo);
            ZoneDef yard = MakeZone("yard", stroll);
            MakeCitizen("YardVolume", m_Player.transform.position, yard.id);
            RunZoneTask task = MakeTask("dorm");
            StateTreeContext context = Context();

            task.OnEnter(context);
            m_Service.OrchestrateNow();
            Assert.AreEqual("zone.dorm", m_Service.activeZone,
                "the asked zone has no volume and still outranks the placed one");
            Assert.AreSame(meet, m_Service.current);

            task.OnExit(context, StateTreeStatus.Cancelled);
            m_Service.OrchestrateNow();
            Assert.AreEqual("zone.yard", m_Service.activeZone,
                "released, distance competes again");
        }

        [Test]
        public void Preempted_KeepsTheCursor_AndReasking_Resumes()
        {
            ObjectiveDef meet = MakeDialog("meet", "intro");
            ObjectiveDef brief = MakeDialog("brief", "orders");
            MakeZone("dorm", meet, brief);
            RunZoneTask task = MakeTask("dorm");
            StateTreeContext context = Context();

            task.OnEnter(context);
            m_Service.ReportDialogFinished("intro");
            task.OnExit(context, StateTreeStatus.Cancelled);   // an ancestor pulled us out
            Assert.IsNull(m_Service.current, "released: nothing asks");

            task.OnEnter(context);
            Assert.AreSame(brief, m_Service.current, "re-asking resumes where the stack stood");
            m_Service.ReportDialogFinished("orders");
            Assert.AreEqual(StateTreeStatus.Success, task.OnTick(context, 0.1f));
        }

        [Test]
        public void TheCompleteVerb_CompletesTheCurrentRow_OnlyWhenItIsTheNamedOne()
        {
            ObjectiveDef open = MakeObjective("open-the-door", ObjectiveKind.MoveTo);
            m_Service.Activate(open);
            var row = new ServiceRequest { key = "x", action = ObjectiveService.CompleteAction };

            Serve(row, "some-other-row");
            Assert.AreSame(open, m_Service.current, "a stale write completes nothing");

            Serve(row, "open-the-door");
            Assert.IsNull(m_Service.current, "the flow said when");

            m_Service.Activate(open);
            Serve(row, "");
            Assert.IsNull(m_Service.current, "empty completes whatever is current");
        }

        // ------------------------------------------------------------------------ helpers

        private void Serve(ServiceRequest row, string value)
        {
            typeof(ObjectiveService).GetMethod("OnRequest",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(m_Service, new object[] { row, value });
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

        private ObjectiveDef MakeDialog(string objectiveName, string dialogRowName)
        {
            ObjectiveDef row = MakeObjective(objectiveName, ObjectiveKind.Dialog);
            row.target.entryName = dialogRowName;
            return row;
        }

        private ZoneDef MakeZone(string zoneName, params ObjectiveDef[] stack)
        {
            var asset = ScriptableObject.CreateInstance<ZoneAsset>();
            asset.name = "Zone_" + zoneName;
            asset.displayName = zoneName;
            m_Assets.Add(asset);
            for (int i = 0; i < stack.Length; i++)
                asset.entries.Add(stack[i]);

            var zone = new ZoneDef
            {
                id = "zone." + zoneName, name = zoneName, asset = asset
            };
            m_Zones.entries.Add(zone);
            return zone;
        }

        private void MakeCitizen(string citizenName, Vector3 position, string tag)
        {
            var go = new GameObject(citizenName) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(m_Level.transform);
            go.transform.position = position;
            m_Objects.Add(go);
            var citizen = go.AddComponent<WorldObjectBehaviour>();
            citizen.tags.Add(tag);
            m_World.Register(citizen);
        }

        private RunZoneTask MakeTask(string zoneName)
        {
            var task = ScriptableObject.CreateInstance<RunZoneTask>();
            task.zone.entryName = zoneName;
            m_Assets.Add(task);
            return task;
        }

        private StateTreeContext Context()
        {
            return new StateTreeContext(m_Level.gameObject);
        }
    }
}
