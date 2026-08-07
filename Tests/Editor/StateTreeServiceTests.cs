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
    }
}
