using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the M15 service layer: instances and RECIPES registered on
    /// hosts (order-free — the first resolve constructs the graph, constructor-injected
    /// from the registering scope's view of the spine, cached, cycle-guarded, every
    /// failure one error and a null), and the typed capability field the executor injects
    /// into tasks at StartTree from the OWNER's chain.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeServiceTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        // ------------------------------------------------------------------ test doubles

        public interface IPing
        {
            string Answer();
        }

        public interface IClock
        {
            float Now();
        }

        private sealed class FixedClock : IClock
        {
            public float Now() => 42f;
        }

        /// <summary>Constructor-injected: needs a clock from the spine.</summary>
        private sealed class PingService : IPing, IDisposable
        {
            private readonly IClock m_Clock;
            public static int constructed;
            public bool disposed;
            public static PingService last;

            public PingService(IClock clock)
            {
                m_Clock = clock;
                constructed++;
                last = this;
            }

            public string Answer() => "pong@" + m_Clock.Now();

            public void Dispose() => disposed = true;
        }

        private sealed class NeedsB
        {
            public NeedsB(NeedsA a) { }
        }

        private sealed class NeedsA
        {
            public NeedsA(NeedsB b) { }
        }

        public sealed class PingTask : StateTreeTaskAsset
        {
            public StateTreeServiceRef<IPing> ping = new StateTreeServiceRef<IPing>();

            public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
            {
                if (ping.service == null)
                    return StateTreeStatus.Failure;
                context.blackboard["answer"] = ping.service.Answer();
                return StateTreeStatus.Success;
            }
        }

        [SetUp]
        public void SetUp()
        {
            PingService.constructed = 0;
            PingService.last = null;
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
                    UnityEngine.Object.DestroyImmediate(m_Objects[i]);
            }
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    UnityEngine.Object.DestroyImmediate(m_Assets[i]);
            }
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
        }

        [Test]
        public void ASubsystemCanBeTakenOutAndPutBack_WithoutItsScope()
        {
            (StateTreeContextHost host, InventoryService service, ServiceDef def) =
                MakeFlowsFixture();

            var installerObject = new GameObject("Installer");
            installerObject.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(installerObject);
            var installer = installerObject.AddComponent<StateTreeServiceInstaller>();
            installer.scope = host;

            // The fixture's own instance stands in for whatever built it before; the installer
            // is what owns a LIFETIME, so the subsystem is installed through it.
            host.Forget(service);
            def.serviceTypeName = nameof(InventoryService);
            StateTreeSubsystem subsystem = installer.Install(def);

            Assert.That(subsystem, Is.Not.Null);
            Assert.That(subsystem.installed, Is.True);
            Assert.That(host.GetService<InventoryService>(), Is.SameAs(subsystem.service),
                "installed means resolvable from its scope");

            InventoryService first = host.GetService<InventoryService>();
            Assert.That(installer.Uninstall(def), Is.True);
            Assert.That(host.GetService<InventoryService>(), Is.Null,
                "taken out means gone from every scope that saw it — the scope is untouched");

            StateTreeSubsystem again = installer.Reinstall(def);
            Assert.That(again.service, Is.Not.SameAs(first),
                "reinstalling BUILDS one, which is the point: a swapped implementation, a "
                + "rebuilt subsystem, without restarting the level around it");
            Assert.That(host.GetService<InventoryService>(), Is.SameAs(again.service));
        }

        [Test]
        public void Recipe_ConstructsLazily_OrderFree_ResolvingAcrossScopes()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            StateTreeContextHost level = MakeHost("Level", StateTreeContextKind.Level, root);

            // The recipe lands BEFORE its dependency exists anywhere — order must not matter.
            level.Provide<IPing, PingService>();
            Assert.AreEqual(0, PingService.constructed, "a recipe alone constructs nothing");

            root.Provide<IClock>(new FixedClock());

            Assert.AreEqual("pong@42", level.GetService<IPing>().Answer(),
                "first ask constructed the service, resolving IClock from the PARENT scope");
            Assert.AreEqual(1, PingService.constructed);
            Assert.AreSame(level.GetService<IPing>(), level.GetService<IPing>(),
                "the instance is cached — a container, not a factory");
        }

        [Test]
        public void MissingDependency_IsOneError_AndNull()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            root.Provide<IPing, PingService>();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "nothing on the spine provides IClock"));
            Assert.IsNull(root.GetService<IPing>(),
                "an unconstructable recipe answers null, not a throw mid-load");
        }

        [Test]
        public void RecipeCycle_IsOneError_NotAHang()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            root.Provide<NeedsA, NeedsA>();
            root.Provide<NeedsB, NeedsB>();

            // Three errors, outermost last: the inner re-entry trips the cycle guard,
            // then each ring member reports the parameter it could not get.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "recipe cycle"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "nothing on the spine provides NeedsA"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "nothing on the spine provides NeedsB"));
            Assert.IsNull(root.GetService<NeedsA>());
        }

        [Test]
        public void MultiInterface_OneInstance_SeveralCapabilities()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            var clock = new FixedClock();
            root.Provide<IClock>(clock);
            root.Provide<FixedClock>(clock);

            Assert.AreSame(root.GetService<IClock>(), root.GetService<FixedClock>(),
                "one instance answers under every capability it was registered for");
        }

        [Test]
        public void ServiceRef_IsInjectedAtStart_FromTheOwnersSpine()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            root.Provide<IClock>(new FixedClock());
            root.Provide<IPing, PingService>();

            var task = ScriptableObject.CreateInstance<PingTask>();
            m_Assets.Add(task);
            StateTreeRunner runner = MakeRunner(MakeTree(task), root);
            runner.StartTree();
            runner.TickTree(0.1f);

            Assert.AreEqual("pong@42", runner.context.blackboard["answer"],
                "the task's capability field was injected from the spine before it ticked");
            Assert.IsNull(task.ping.service,
                "the AUTHORED asset's field stays empty — only the deep copy was injected");
        }

        [Test]
        public void ServiceRef_MissingCapability_IsOneError_AndTheTaskFails()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);

            var task = ScriptableObject.CreateInstance<PingTask>();
            m_Assets.Add(task);
            StateTreeRunner runner = MakeRunner(MakeTree(task), root);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "the spine provides none"));
            runner.StartTree();
            runner.TickTree(0.1f);
            Assert.IsFalse(runner.context.blackboard.ContainsKey("answer"),
                "no capability = the task Failed instead of pretending");
        }

        [Test]
        public void OwnedConstructedService_IsDisposedWithItsHost()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            root.Provide<IClock>(new FixedClock());
            root.Provide<IPing, PingService>();
            root.GetService<IPing>();
            PingService constructed = PingService.last;
            Assert.IsNotNull(constructed);

            // EditMode never runs a plain component's OnDestroy, so the sweep is invoked
            // the way Register/Unregister are — directly, doing what the lifecycle would.
            root.DisposeOwnedServices();
            Assert.IsTrue(constructed.disposed,
                "a service the HOST constructed dies with the host's scope");
        }

        // ---------------------------------------------------------------------- fixtures

        private StateTreeContextHost MakeHost(string goName, StateTreeContextKind kind,
            StateTreeContextHost parent)
        {
            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = kind;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            return host;
        }

        private StateTreeAsset MakeTree(StateTreeTaskAsset task)
        {
            var leaf = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            leaf.nodeId = "work";
            leaf.name = "Node work";
            leaf.tasks.Add(task);
            m_Assets.Add(leaf);

            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            root.name = "Node root";
            root.children.Add(leaf);
            m_Assets.Add(root);

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "ServiceTestTree";
            tree.treeName = "ServiceTestTree";
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }

        private StateTreeRunner MakeRunner(StateTreeAsset tree, StateTreeContextHost under)
        {
            var go = new GameObject("ServiceRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(under.transform);
            go.SetActive(false);
            m_Objects.Add(go);
            var runner = go.AddComponent<StateTreeRunner>();
            runner.autoStart = false;
            runner.data = tree;
            runner.ownerObject = go;
            runner.context = new StateTreeContext(go);
            return runner;
        }

        // ------------------------------------------------------------------ def flows

        private (StateTreeContextHost host, InventoryService service, ServiceDef def)
            MakeFlowsFixture()
        {
            // The §4c shape: a typed request state holding ONLY its meaningful task —
            // no interrupt condition, no consume; the runner derives both from the def.
            var receipt = ScriptableObject.CreateInstance<SetBlackboardTask>();
            receipt.key = new StateTreeKeyField("test.served");
            receipt.kind = SetBlackboardTask.ValueKind.Float;
            receipt.floatValue = 1f;
            m_Assets.Add(receipt);

            var idle = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            idle.name = "idle";
            idle.nodeId = "idle";
            idle.completeWhen = StateTreeCompleteWhen.Never;
            m_Assets.Add(idle);

            var serve = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            serve.name = "serve";
            serve.nodeId = "serve";
            serve.roleKind = "request";
            serve.tasks.Add(receipt);
            serve.transitions.Add(new StateTreeTransition { targetNodeId = "idle" });
            m_Assets.Add(serve);

            var flowsRoot = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            flowsRoot.name = "root";
            flowsRoot.nodeId = "root";
            flowsRoot.children.Add(idle);
            flowsRoot.children.Add(serve);
            m_Assets.Add(flowsRoot);

            var flows = ScriptableObject.CreateInstance<StateTreeAsset>();
            flows.treeName = "TestFlows";
            flows.root = flowsRoot;
            m_Assets.Add(flows);

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "flow-test";
            def.scope = StateTreeContextKind.Root;
            def.flows = flows;
            // The bag stands in for "a service with a def" here, and it refuses to be built
            // without the catalog it manages — so the fixture gives it one (M33).
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
            m_Assets.Add(items);
            def.registry = items;
            def.requests.Add(new ServiceRequest
            {
                key = "test.request", stateId = "serve", description = "serve the test"
            });
            m_Assets.Add(def);

            var rootGo = new GameObject("FlowRoot");
            rootGo.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(rootGo);
            var host = rootGo.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Root;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);

            var service = new InventoryService(host, def);
            host.Provide(service);
            return (host, service, def);
        }

        [Test]
        public void DefFlows_ServeADeclaredRequest_EntryAndConsumeDerived()
        {
            (StateTreeContextHost host, InventoryService service, _) = MakeFlowsFixture();

            host.Context.blackboard["test.request"] = "1";
            for (int i = 0; i < 3; i++)
                service.Tick(0.02f);

            Assert.IsTrue(service.flowsRunning, "the def's tree runs with the service");
            Assert.IsTrue(host.Context.blackboard.ContainsKey("test.served"),
                "the pending key entered its DECLARED state — no authored interrupt");
            Assert.IsFalse(host.Context.blackboard.ContainsKey("test.request"),
                "and leaving the request state consumed the key — no authored clear");
        }

        [Test]
        public void TypedRequest_GoesThroughTheDefsRows()
        {
            (StateTreeContextHost host, InventoryService service, _) = MakeFlowsFixture();
            service.Tick(0.02f);   // start the tree

            service.Request("test.request");
            for (int i = 0; i < 3; i++)
                service.Tick(0.02f);
            Assert.IsTrue(host.Context.blackboard.ContainsKey("test.served"),
                "the typed door writes the same key the flow serves");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "not a declared request"));
            service.Request("test.typo");
        }
    }
}
